using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// Anthropic（Claude）上游适配器（M6）。
/// 职责：把 Ollama 请求翻译为 Anthropic <c>/v1/messages</c> 格式，并把 Anthropic 的 SSE / 非流式响应
/// 翻译为统一的 OpenAI 形状，复用下游 <see cref="OllamaStreamTranslator"/> 与 <see cref="OpenAiAdapter"/>。
///
/// 关键差异处理：
///   - Anthropic 的 system 必须放在顶层字段（不能是 role=system 的消息）；
///   - 角色只有 user/assistant，工具调用以 content 数组里的 tool_use / tool_result 块表达；
///   - 流式是 <c>event: xxx\n data: {json}</c> 事件块，而非纯 data: 行，需按事件类型解析；
///   - 思考过程走 thinking_delta（映射至 reasoning_content）。
/// </summary>
public sealed class AnthropicUpstreamAdapter : IUpstreamAdapter
{
    /// <inheritdoc/>
    public String ApiMode => "anthropic";

    /// <inheritdoc/>
    public String BuildRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (model == null) throw new ArgumentNullException(nameof(model));

        var systemParts = new List<String>();
        var messages = new List<Object>();

        foreach (var m in req.messages)
        {
            var role = (m.role ?? "").ToLowerInvariant();

            // system 单独收集为顶层字段
            if (role == "system")
            {
                if (!String.IsNullOrEmpty(m.content)) systemParts.Add(m.content!);
                continue;
            }

            // 工具结果消息：Ollama/Copilot 以 role=tool 表达，Anthropic 需包成 user 的 tool_result 块
            if (role == "tool")
            {
                var toolResult = new Dictionary<String, Object?>
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = m.tool_call_id ?? "unknown",
                    ["content"] = m.content ?? "",
                };
                messages.Add(new Dictionary<String, Object?>
                {
                    ["role"] = "user",
                    ["content"] = new List<Object?> { toolResult },
                });
                continue;
            }

            // user 消息：可能含图片 → content 数组
            if (role == "user")
            {
                messages.Add(new Dictionary<String, Object?> { ["role"] = "user", ["content"] = BuildUserContent(m) });
                continue;
            }

            // assistant 消息：可能含工具调用（tool_calls，OpenAI 风格）→ tool_use 块
            if (!String.IsNullOrEmpty(m.content) || m.tool_calls != null)
            {
                var content = new List<Object?>();
                if (!String.IsNullOrEmpty(m.content))
                    content.Add(new Dictionary<String, Object?> { ["type"] = "text", ["text"] = m.content });
                if (m.tool_calls != null)
                {
                    foreach (var tc in ParseToolCalls(m.tool_calls))
                        content.Add(new Dictionary<String, Object?>
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.id,
                            ["name"] = tc.name,
                            ["input"] = tc.input,
                        });
                }
                messages.Add(new Dictionary<String, Object?> { ["role"] = "assistant", ["content"] = content });
            }
        }

        var body = new Dictionary<String, Object?>
        {
            ["model"] = model.Id,
            ["stream"] = forceStream,
            ["messages"] = messages,
            // Anthropic 强制要求 max_tokens，缺失会直接 400；取 options 或模型上限
            ["max_tokens"] = ResolveMaxTokens(req, model),
        };
        if (systemParts.Count > 0)
            body["system"] = String.Join("\n\n", systemParts);
        if (req.tools != null)
        {
            var tools = ConvertTools(req.tools);
            if (tools != null) body["tools"] = tools;
        }

        return JsonHelper.ToJson(body);
    }

    /// <inheritdoc/>
    public String GetRequestUrl(ProviderOptions provider, ModelOptions model)
    {
        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        return baseUrl + "/v1/messages";
    }

    /// <inheritdoc/>
    public void ApplyAuth(HttpRequestMessage req, ProviderOptions provider, String apiKey)
    {
        if (!String.IsNullOrEmpty(apiKey))
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        // Anthropic API 版本头是必填项，缺失返回 400
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
    }

    /// <inheritdoc/>
    public String? ReadStream(HttpResponseMessage resp, Action<String> emitOpenAiChunk, CancellationToken cancellationToken)
    {
        // 跨块累积状态：工具调用块（按 index 记录 id/name/已累积 arguments）
        var toolBlocks = new Dictionary<Int32, (String id, String name, StringBuilder args)>();
        var inputTokens = 0L;
        var outputTokens = 0L;
        var stopReason = "end_turn";

        using var stream = resp.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var currentEvent = "";
        var sawData = false;
        var raw = new StringBuilder();
        String? line;
        while ((line = reader.ReadLine()) != null)
        {
            raw.Append(line).Append('\n');
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line.Substring("event:".Length).Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            var data = line.Substring("data:".Length).Trim();
            if (String.IsNullOrEmpty(data)) continue;
            sawData = true;

            try { DispatchEvent(currentEvent, data, emitOpenAiChunk, toolBlocks, ref inputTokens, ref outputTokens, ref stopReason); }
            catch (Exception ex) { XTrace.WriteLine("Anthropic SSE 块解析失败（已跳过）：{0}", ex.Message); }
        }

        // 确保末帧带有 usage 与 finish_reason（无 message_delta 时兜底）
        emitOpenAiChunk(BuildUsageChunk(inputTokens, outputTokens, stopReason));
        return sawData ? null : raw.ToString();
    }

    /// <inheritdoc/>
    public String ConvertNonStream(String upstreamJson, ModelOptions model)
    {
        if (String.IsNullOrEmpty(upstreamJson)) return upstreamJson;

        var msg = JsonHelper.ToJsonEntity<AnthropicMessage>(upstreamJson);
        if (msg == null) return upstreamJson;

        // content 可能是 [text] / [tool_use] 混合，分别抽文本与工具调用
        var sb = new StringBuilder();
        var toolCalls = new List<Object?>();
        if (msg.content is List<Object> blocks)
        {
            foreach (var b in blocks)
            {
                var d = b as Dictionary<String, Object?>;
                if (d == null) continue;
                var type = d.Val("type")?.ToString();
                if (type == "text")
                {
                    if (d.Val("text") is String t) sb.Append(t);
                }
                else if (type == "tool_use")
                {
                    var name = d.Val("name")?.ToString() ?? "";
                    var id = d.Val("id")?.ToString() ?? "";
                    var inputJson = d.ContainsKey("input") && d["input"] != null ? JsonHelper.ToJson(d["input"]) : "{}";
                    toolCalls.Add(new Dictionary<String, Object?>
                    {
                        ["id"] = id,
                        ["type"] = "function",
                        ["function"] = new Dictionary<String, Object?> { ["name"] = name, ["arguments"] = inputJson },
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
                    ["finish_reason"] = MapStopReason(msg.stop_reason),
                },
            },
            ["usage"] = new Dictionary<String, Object>
            {
                ["prompt_tokens"] = msg.usage?.input_tokens ?? 0,
                ["completion_tokens"] = msg.usage?.output_tokens ?? 0,
                ["total_tokens"] = (msg.usage?.input_tokens ?? 0) + (msg.usage?.output_tokens ?? 0),
            },
        };
        return JsonHelper.ToJson(oa);
    }

    // ---- 内部 ----

    private static void DispatchEvent(String eventType, String data,
        Action<String> emit, Dictionary<Int32, (String id, String name, StringBuilder args)> toolBlocks,
        ref Int64 inputTokens, ref Int64 outputTokens, ref String stopReason)
    {
        var root = JsonHelper.ToJsonEntity<Dictionary<String, Object?>>(data);
        if (root == null) return;

        switch (eventType)
        {
            case "message_start":
                if (root.Val("message") is Dictionary<String, Object?> m &&
                    m.Val("usage") is Dictionary<String, Object?> u)
                {
                    inputTokens = ToLong(u.Val("input_tokens"));
                }
                break;

            case "content_block_start":
                var idx = ToInt(root.Val("index"));
                var block = root.Val("content_block") as Dictionary<String, Object?>;
                if (block != null && block.Val("type")?.ToString() == "tool_use")
                {
                    // 记录工具块起点，供后续 input_json_delta 归并
                    toolBlocks[idx] = (block.Val("id")?.ToString() ?? "", block.Val("name")?.ToString() ?? "", new StringBuilder());
                    // 首片：带 id/name，arguments 留空（真正入参分片随后到达）
                    emit(BuildToolCallChunk(idx, toolBlocks[idx].id, toolBlocks[idx].name, ""));
                }
                break;

            case "content_block_delta":
                var d = root.Val("delta") as Dictionary<String, Object?>;
                if (d == null) break;
                var dtype = d.Val("type")?.ToString();
                if (dtype == "text_delta")
                {
                    var text = d.Val("text")?.ToString() ?? "";
                    if (text.Length > 0)
                        emit("{\"choices\":[{\"delta\":{\"content\":" + JsonHelper.ToJson(text) + "}}]}");
                }
                else if (dtype == "thinking_delta")
                {
                    var t = d.Val("thinking")?.ToString() ?? "";
                    if (t.Length > 0)
                        emit("{\"choices\":[{\"delta\":{\"reasoning_content\":" + JsonHelper.ToJson(t) + "}}]}");
                }
                else if (dtype == "input_json_delta")
                {
                    var i = ToInt(root.Val("index"));
                    var frag = d.Val("partial_json")?.ToString() ?? "";
                    if (toolBlocks.TryGetValue(i, out var tb))
                    {
                        tb.args.Append(frag);
                        // 仅发新增分片，由翻译器跨块归并（与 OpenAI 流式一致）
                        emit("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":" + i +
                             ",\"function\":{\"arguments\":" + JsonHelper.ToJson(frag) + "}}]}}]}");
                    }
                }
                break;

            case "message_delta":
                if (root.Val("delta") is Dictionary<String, Object?> md)
                    stopReason = md.Val("stop_reason")?.ToString() ?? stopReason;
                if (root.Val("usage") is Dictionary<String, Object?> mu)
                    outputTokens = ToLong(mu.Val("output_tokens"));
                break;
        }
    }

    private static String BuildToolCallChunk(Int32 index, String id, String name, String args)
        => "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":" + index +
           ",\"id\":" + JsonHelper.ToJson(id) +
           ",\"type\":\"function\",\"function\":{\"name\":" + JsonHelper.ToJson(name) +
           ",\"arguments\":" + JsonHelper.ToJson(args) + "}}]}}]}";

    private static String BuildUsageChunk(Int64 prompt, Int64 completion, String stopReason)
        => "{\"choices\":[{\"delta\":{},\"finish_reason\":" + JsonHelper.ToJson(MapStopReason(stopReason)) + "}]," +
           "\"usage\":{\"prompt_tokens\":" + prompt + ",\"completion_tokens\":" + completion + ",\"total_tokens\":" + (prompt + completion) + "}}";

    private static String MapStopReason(String? reason) => reason switch
    {
        "max_tokens" => "length",
        "tool_use" => "tool_calls",
        _ => "stop",
    };

    private static Object BuildUserContent(OllamaMessage m)
    {
        if (m.images == null || m.images.Count == 0)
            return (Object)m.content!;

        var parts = new List<Object?>();
        if (!String.IsNullOrEmpty(m.content))
            parts.Add(new Dictionary<String, Object?> { ["type"] = "text", ["text"] = m.content });
        foreach (var img in m.images)
        {
            var (mime, b64) = OpenAiAdapter.SplitImage(img);
            parts.Add(new Dictionary<String, Object?>
            {
                ["type"] = "image",
                ["source"] = new Dictionary<String, Object?>
                {
                    ["type"] = "base64",
                    ["media_type"] = mime,
                    ["data"] = b64,
                },
            });
        }
        return parts;
    }

    private static Int32 ResolveMaxTokens(OllamaChatRequest req, ModelOptions model)
    {
        if (req.options != null &&
            (TryGetInt(req.options, "max_tokens", out var mt) || TryGetInt(req.options, "num_predict", out mt)))
            return mt;
        return model.MaxTokens > 0 ? model.MaxTokens : 4096;
    }

    private static List<Object?>? ConvertTools(Object tools)
    {
        if (tools is not List<Object> list) return null;
        var outList = new List<Object?>();
        foreach (var t in list)
        {
            if (t is not Dictionary<String, Object?> d) continue;
            var fn = d.Val("function") as Dictionary<String, Object?>;
            if (fn == null) continue;
            var name = fn.Val("name")?.ToString() ?? "";
            var desc = fn.Val("description")?.ToString() ?? "";
            // input_schema 取参数 schema（经清洗：Anthropic 同样不认 $schema 等键）
            var rawParams = fn.ContainsKey("parameters") ? fn["parameters"] : null;
            var cleaned = ToolSchemaSanitizer.Sanitize(rawParams) ?? new Dictionary<String, Object?> { ["type"] = "object" };
            outList.Add(new Dictionary<String, Object?>
            {
                ["name"] = name,
                ["description"] = desc,
                ["input_schema"] = cleaned,
            });
        }
        return outList;
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

    private static Int64 ToLong(Object? o)
    {
        if (o == null) return 0;
        if (o is Int64 i64) return i64;
        if (o is Int32 i32) return i32;
        return Int64.TryParse(o.ToString(), out var v) ? v : 0;
    }

    private static Int32 ToInt(Object? o)
    {
        if (o == null) return 0;
        if (o is Int32 i32) return i32;
        if (o is Int64 i64) return (Int32)i64;
        return Int32.TryParse(o.ToString(), out var v) ? v : 0;
    }

    // ---- Anthropic 非流式响应 DTO ----

    private class AnthropicMessage
    {
        public List<Object>? content { get; set; }
        public String? stop_reason { get; set; }
        public AnthropicUsage? usage { get; set; }
    }

    private class AnthropicUsage
    {
        public Int32 input_tokens { get; set; }
        public Int32 output_tokens { get; set; }
    }
}
