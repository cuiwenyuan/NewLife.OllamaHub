using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Security;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// Google Gemini 上游适配器（M6）。
/// 职责：把 Ollama 请求翻译为 Gemini <c>models/{model}:streamGenerateContent?alt=sse</c> 格式，
/// 并把 Gemini 的 SSE（每行为一个 GenerateContentResponse）/ 非流式响应翻译为 OpenAI 形状，
/// 复用下游 <see cref="OllamaStreamTranslator"/> 与 <see cref="OpenAiAdapter"/>。
///
/// 关键差异处理：
///   - 角色仅 user / model（assistant → model）；system 进 systemInstruction 顶层字段；
///   - 多模态走 parts[].inline_data；工具走 functionCall / functionResponse 块；
///   - 思考过程（thought:true 的 part）映射至 reasoning_content。
/// </summary>
public sealed class GeminiUpstreamAdapter : IUpstreamAdapter
{
    /// <inheritdoc/>
    public String ApiMode => "gemini";

    /// <inheritdoc/>
    public String BuildRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (model == null) throw new ArgumentNullException(nameof(model));

        var contents = new List<Object?>();
        var sysParts = new List<Object?>();

        foreach (var m in req.messages)
        {
            var role = (m.role ?? "").ToLowerInvariant();
            if (role == "system")
            {
                if (!String.IsNullOrEmpty(m.content)) sysParts.Add(new Dictionary<String, Object?> { ["text"] = m.content });
                continue;
            }

            // 工具结果消息 → user 的 functionResponse 块
            if (role == "tool")
            {
                contents.Add(new Dictionary<String, Object?>
                {
                    ["role"] = "user",
                    ["parts"] = new List<Object?>
                    {
                        new Dictionary<String, Object?>
                        {
                            ["functionResponse"] = new Dictionary<String, Object?>
                            {
                                ["name"] = m.tool_call_id ?? "tool",
                                ["response"] = new Dictionary<String, Object?> { ["result"] = m.content ?? "" },
                            },
                        },
                    },
                });
                continue;
            }

            var parts = new List<Object?>();
            if (!String.IsNullOrEmpty(m.content)) parts.Add(new Dictionary<String, Object?> { ["text"] = m.content });

            // 多模态图片 → inline_data
            if (m.images != null)
                foreach (var img in m.images)
                {
                    var (mime, b64) = OpenAiAdapter.SplitImage(img);
                    parts.Add(new Dictionary<String, Object?>
                    {
                        ["inline_data"] = new Dictionary<String, Object?> { ["mime_type"] = mime, ["data"] = b64 },
                    });
                }

            // assistant 的工具调用 → model 的 functionCall 块
            if (m.tool_calls != null)
                foreach (var tc in ParseToolCalls(m.tool_calls))
                    parts.Add(new Dictionary<String, Object?>
                    {
                        ["functionCall"] = new Dictionary<String, Object?> { ["name"] = tc.name, ["args"] = tc.input },
                    });

            if (parts.Count == 0) continue;
            var geminiRole = role == "assistant" ? "model" : "user";
            contents.Add(new Dictionary<String, Object?> { ["role"] = geminiRole, ["parts"] = parts });
        }

        var body = new Dictionary<String, Object?>
        {
            ["contents"] = contents,
        };
        if (sysParts.Count > 0)
            body["systemInstruction"] = new Dictionary<String, Object?> { ["parts"] = sysParts };

        var drop = model.DropParams ?? new List<String>();
        var gen = new Dictionary<String, Object?>();
        if (!drop.Contains("max_tokens") && TryGetInt(req.options, "max_tokens", out var mt)) gen["maxOutputTokens"] = mt;
        else if (!drop.Contains("max_tokens") && TryGetInt(req.options, "num_predict", out mt)) gen["maxOutputTokens"] = mt;
        else gen["maxOutputTokens"] = model.MaxTokens > 0 ? model.MaxTokens : 4096;
        if (!drop.Contains("temperature") && TryGetDouble(req.options, "temperature", out var t)) gen["temperature"] = t;
        if (!drop.Contains("top_p") && TryGetDouble(req.options, "top_p", out var tp)) gen["topP"] = tp;
        if (gen.Count > 0) body["generationConfig"] = gen;

        if (req.tools != null)
        {
            var fns = ConvertTools(req.tools);
            if (fns != null) body["tools"] = new List<Object?> { new Dictionary<String, Object?> { ["functionDeclarations"] = fns } };
        }

        return JsonHelper.ToJson(body);
    }

    /// <inheritdoc/>
    public String GetRequestUrl(ProviderOptions provider, ModelOptions model)
    {
        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        var apiKey = SecretProtector.Resolve(provider) ?? "";
        return $"{baseUrl}/models/{model.Id}:streamGenerateContent?alt=sse&key={apiKey}";
    }

    /// <inheritdoc/>
    public void ApplyAuth(HttpRequestMessage req, ProviderOptions provider, String apiKey)
    {
        // Gemini 用 URL 查询参数里的 key 鉴权，无需 Authorization 头
    }

    /// <inheritdoc/>
    public String? ReadStream(HttpResponseMessage resp, Action<String> emitOpenAiChunk, CancellationToken cancellationToken)
    {
        var promptTokens = 0L;
        var completionTokens = 0L;
        var finish = "STOP";
        var sawData = false;

        using var stream = resp.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = new StringBuilder();
        String? line;
        while ((line = reader.ReadLine()) != null)
        {
            raw.Append(line).Append('\n');
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line.Substring("data:".Length).Trim();
            if (String.IsNullOrEmpty(data)) continue;
            sawData = true;

            try { DispatchChunk(data, emitOpenAiChunk, ref promptTokens, ref completionTokens, ref finish); }
            catch (Exception ex) { XTrace.WriteLine("Gemini SSE 块解析失败（已跳过）：{0}", ex.Message); }
        }

        if (promptTokens > 0 || completionTokens > 0)
            emitOpenAiChunk(BuildUsageChunk(promptTokens, completionTokens, finish));
        return sawData ? null : raw.ToString();
    }

    /// <inheritdoc/>
    public String ConvertNonStream(String upstreamJson, ModelOptions model)
    {
        if (String.IsNullOrEmpty(upstreamJson)) return upstreamJson;
        var resp = JsonHelper.ToJsonEntity<GeminiResponse>(upstreamJson);
        if (resp == null) return upstreamJson;

        var sb = new StringBuilder();
        var toolCalls = new List<Object?>();
        var cand = resp.candidates?.Count > 0 ? resp.candidates[0] : null;
        if (cand?.content?.parts != null)
        {
            foreach (var p in cand.content.parts)
            {
                var d = p as Dictionary<String, Object?>;
                if (d == null) continue;
                if (d.Val("text") is String t) sb.Append(t);
                var fc = d.Val("functionCall") as Dictionary<String, Object?>;
                if (fc != null)
                {
                    var name = fc.Val("name")?.ToString() ?? "";
                    var argsJson = fc.ContainsKey("args") && fc["args"] != null ? JsonHelper.ToJson(fc["args"]) : "{}";
                    toolCalls.Add(new Dictionary<String, Object?>
                    {
                        ["id"] = "call_" + name,
                        ["type"] = "function",
                        ["function"] = new Dictionary<String, Object?> { ["name"] = name, ["arguments"] = argsJson },
                    });
                }
            }
        }

        var oaMessage = new Dictionary<String, Object?>
        {
            ["role"] = "assistant",
            ["content"] = sb.ToString(),
        };
        if (toolCalls.Count > 0) oaMessage["tool_calls"] = toolCalls;

        var oa = new Dictionary<String, Object>
        {
            ["choices"] = new List<Object>
            {
                new Dictionary<String, Object>
                {
                    ["message"] = oaMessage,
                    ["finish_reason"] = MapFinish(cand?.finishReason),
                },
            },
            ["usage"] = new Dictionary<String, Object>
            {
                ["prompt_tokens"] = resp.usageMetadata?.promptTokenCount ?? 0,
                ["completion_tokens"] = resp.usageMetadata?.candidatesTokenCount ?? 0,
                ["total_tokens"] = resp.usageMetadata?.totalTokenCount ?? 0,
            },
        };
        return JsonHelper.ToJson(oa);
    }

    // ---- 内部 ----

    private static void DispatchChunk(String data, Action<String> emit,
        ref Int64 promptTokens, ref Int64 completionTokens, ref String finish)
    {
        var root = JsonHelper.ToJsonEntity<GeminiResponse>(data);
        if (root?.candidates == null) return;

        var cand = root.candidates.Count > 0 ? root.candidates[0] : null;
        var text = new StringBuilder();
        var thinking = new StringBuilder();
        if (cand?.content?.parts != null)
        {
            foreach (var p in cand.content.parts)
            {
                var d = p as Dictionary<String, Object?>;
                if (d == null) continue;
                // thought:true 的 part 视为推理过程
                var isThought = d.Val("thought") is Boolean b && b;
                if (d.Val("text") is String t)
                {
                    if (isThought) thinking.Append(t);
                    else text.Append(t);
                }
            }
        }

        var fr = MapFinish(cand?.finishReason);
        if (cand?.finishReason != null) finish = cand.finishReason!;

        var choice = new Dictionary<String, Object?>
        {
            ["delta"] = new Dictionary<String, Object?>(),
            ["finish_reason"] = String.IsNullOrEmpty(fr) ? null : fr,
        };
        var delta = (Dictionary<String, Object?>)choice["delta"]!;
        if (text.Length > 0) delta["content"] = text.ToString();
        if (thinking.Length > 0) delta["reasoning_content"] = thinking.ToString();

        emit("{\"choices\":[" + JsonHelper.ToJson(choice) + "]}");

        if (root.usageMetadata != null)
        {
            promptTokens = root.usageMetadata.promptTokenCount;
            completionTokens = root.usageMetadata.candidatesTokenCount;
        }
    }

    private static String BuildUsageChunk(Int64 prompt, Int64 completion, String finish)
        => "{\"choices\":[{\"delta\":{},\"finish_reason\":" + JsonHelper.ToJson(MapFinish(finish)) + "}]," +
           "\"usage\":{\"prompt_tokens\":" + prompt + ",\"completion_tokens\":" + completion + ",\"total_tokens\":" + (prompt + completion) + "}}";

    private static String? MapFinish(String? reason) => reason?.ToUpperInvariant() switch
    {
        "STOP" or "STOP_SEQUENCE" => "stop",
        "MAX_TOKENS" => "length",
        "SAFETY" or "RECITATION" or "OTHER" => "content_filter",
        null or "" => null,
        _ => "stop",
    };

    private static List<Object?>? ConvertTools(Object tools)
    {
        if (tools is not List<Object> list) return null;
        var fns = new List<Object?>();
        foreach (var t in list)
        {
            if (t is not Dictionary<String, Object?> d) continue;
            var fn = d.Val("function") as Dictionary<String, Object?>;
            if (fn == null) continue;
            var name = fn.Val("name")?.ToString() ?? "";
            var desc = fn.Val("description")?.ToString() ?? "";
            var rawParams = fn.ContainsKey("parameters") ? fn["parameters"] : null;
            var cleaned = ToolSchemaSanitizer.Sanitize(rawParams) ?? new Dictionary<String, Object?> { ["type"] = "object" };
            fns.Add(new Dictionary<String, Object?>
            {
                ["name"] = name,
                ["description"] = desc,
                ["parameters"] = cleaned,
            });
        }
        return fns;
    }

    private static IEnumerable<(String id, String name, Object? input)> ParseToolCalls(Object? toolCalls)
    {
        if (toolCalls is not List<Object> list) yield break;
        foreach (var t in list)
        {
            if (t is not Dictionary<String, Object?> d) continue;
            var id = d.Val("id")?.ToString() ?? "";
            var fn = d.Val("function") as Dictionary<String, Object?>;
            var name = fn?.Val("name")?.ToString() ?? "";
            var argsStr = fn?.Val("arguments")?.ToString() ?? "{}";
            Object? input;
            try { input = JsonHelper.ToJsonEntity<Object>(argsStr) ?? new Dictionary<String, Object?>(); }
            catch { input = new Dictionary<String, Object?>(); }
            yield return (id, name, input);
        }
    }

    private static Boolean TryGetInt(Dictionary<String, Object>? opts, String key, out Int32 val)
    {
        val = 0;
        if (opts == null || !opts.TryGetValue(key, out var o) || o == null) return false;
        if (o is Int32 i32) { val = i32; return true; }
        if (o is Int64 i64) { val = (Int32)i64; return true; }
        return Int32.TryParse(o.ToString(), out val);
    }

    private static Boolean TryGetDouble(Dictionary<String, Object>? opts, String key, out Double val)
    {
        val = 0;
        if (opts == null || !opts.TryGetValue(key, out var o) || o == null) return false;
        if (o is Double d) { val = d; return true; }
        if (o is Int64 i64) { val = (Double)i64; return true; }
        if (o is Int32 i32) { val = i32; return true; }
        return Double.TryParse(o.ToString(), out val);
    }

    // ---- Gemini 响应 DTO ----

    private class GeminiResponse
    {
        public List<GeminiCandidate>? candidates { get; set; }
        public GeminiUsage? usageMetadata { get; set; }
    }

    private class GeminiCandidate
    {
        public GeminiContent? content { get; set; }
        public String? finishReason { get; set; }
    }

    private class GeminiContent
    {
        public String? role { get; set; }
        public List<Object>? parts { get; set; }
    }

    private class GeminiUsage
    {
        public Int32 promptTokenCount { get; set; }
        public Int32 candidatesTokenCount { get; set; }
        public Int32 totalTokenCount { get; set; }
    }
}
