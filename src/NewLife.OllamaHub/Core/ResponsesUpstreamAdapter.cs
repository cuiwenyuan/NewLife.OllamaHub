using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// OpenAI Responses API 上游适配器。
/// 把 Ollama 对话转换为 /v1/responses 的 input/item 形状，并把 Responses SSE 或非流式响应
/// 归一化为现有下游可消费的 OpenAI Chat Completions 形状（再交给 OllamaStreamTranslator）。
/// 事件名与 input/tools 结构对齐 OpenAI 官方 Responses 协议。
/// </summary>
public sealed class ResponsesUpstreamAdapter : IUpstreamAdapter
{
    /// <inheritdoc/>
    public String ApiMode => "responses";

    /// <inheritdoc/>
    public String BuildRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (model == null) throw new ArgumentNullException(nameof(model));

        // 与 OpenAI Chat 适配器保持一致：先应用模型默认/强制参数，再执行 dropParams 过滤。
        OpenAiAdapter.ApplyModelParams(req, model);

        var body = new Dictionary<String, Object?>
        {
            ["model"] = model.Id,
            ["input"] = BuildInput(req.messages),
            ["stream"] = forceStream ? true : req.stream,
        };

        var drop = model.DropParams ?? new List<String>();
        if (!drop.Contains("temperature") && TryGetDouble(req.options, "temperature", out var temperature))
            body["temperature"] = temperature;
        if (!drop.Contains("top_p") && TryGetDouble(req.options, "top_p", out var topP))
            body["top_p"] = topP;
        if (!drop.Contains("max_tokens") && !drop.Contains("max_output_tokens") &&
            (TryGetInt(req.options, "max_tokens", out var maxTokens) || TryGetInt(req.options, "num_predict", out maxTokens)))
        {
            body["max_output_tokens"] = maxTokens;
        }

        if (!String.IsNullOrEmpty(model.ReasoningEffort))
            body["reasoning"] = new Dictionary<String, Object?> { ["effort"] = model.ReasoningEffort };

        if (req.tools != null)
        {
            var tools = ConvertTools(req.tools);
            if (tools.Count > 0) body["tools"] = tools;
        }

        if (req.tool_choice != null)
            body["tool_choice"] = ConvertToolChoice(req.tool_choice);

        return JsonHelper.ToJson(body);
    }

    /// <inheritdoc/>
    public String GetRequestUrl(ProviderOptions provider, ModelOptions model)
    {
        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        return baseUrl + "/responses";
    }

    /// <inheritdoc/>
    public void ApplyAuth(HttpRequestMessage req, ProviderOptions provider, String apiKey)
    {
        if (!String.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc/>
    public String? ReadStream(HttpResponseMessage resp, Action<String> emitOpenAiChunk, CancellationToken cancellationToken)
    {
        using var stream = resp.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var raw = new StringBuilder();
        var toolIndexes = new Dictionary<String, Int32>(StringComparer.Ordinal);
        var nextToolIndex = 0;
        var sawData = false;
        var emittedFinal = false;
        var hasToolCall = false;
        var currentEvent = "";

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
            if (String.IsNullOrEmpty(data) || data == "[DONE]") continue;
            sawData = true;

            try
            {
                var root = JsonHelper.ToJsonEntity<Dictionary<String, Object?>>(data);
                if (root == null) continue;
                var eventType = root.Val("type")?.ToString() ?? currentEvent;
                DispatchEvent(eventType, root, emitOpenAiChunk, toolIndexes, ref nextToolIndex, ref hasToolCall, ref emittedFinal);
            }
            catch (HubException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个非关键事件解析失败不应中断完整回答，保留日志便于定位供应商协议差异。
                XTrace.WriteLine("Responses SSE 块解析失败（已跳过）：{0}", ex.Message);
            }
        }

        // 某些兼容网关不发送 response.completed；补一个结束块，避免下游一直等待 finish_reason。
        if (sawData && !emittedFinal)
            emitOpenAiChunk(BuildUsageChunk(0, 0, hasToolCall ? "tool_calls" : "stop"));

        return sawData ? null : raw.ToString();
    }

    /// <inheritdoc/>
    public String ConvertNonStream(String upstreamJson, ModelOptions model)
    {
        if (String.IsNullOrEmpty(upstreamJson)) return upstreamJson;

        var root = JsonHelper.ToJsonEntity<Dictionary<String, Object?>>(upstreamJson);
        if (root == null) return upstreamJson;

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var toolCalls = new List<Object?>();

        foreach (var item in AsList(root.Val("output")))
        {
            if (item is not Dictionary<String, Object?> one) continue;
            var type = one.Val("type")?.ToString();
            if (type == "message")
            {
                AppendMessageContent(one.Val("content"), content);
            }
            else if (type == "reasoning")
            {
                AppendReasoning(one.Val("summary"), reasoning);
            }
            else if (type == "function_call")
            {
                toolCalls.Add(ToOpenAiToolCall(one));
            }
        }

        var message = new Dictionary<String, Object?>
        {
            ["role"] = "assistant",
            ["content"] = content.ToString(),
        };
        if (reasoning.Length > 0) message["reasoning_content"] = reasoning.ToString();
        if (toolCalls.Count > 0) message["tool_calls"] = toolCalls;

        var usage = root.Val("usage") as Dictionary<String, Object?>;
        var promptTokens = ToLong(usage.Val("input_tokens"));
        var completionTokens = ToLong(usage.Val("output_tokens"));
        var response = new Dictionary<String, Object?>
        {
            ["id"] = root.Val("id")?.ToString() ?? "resp_ollamahub",
            ["object"] = "chat.completion",
            ["created"] = ToLong(root.Val("created_at")),
            ["model"] = root.Val("model")?.ToString() ?? model.Id,
            ["choices"] = new List<Object?>
            {
                new Dictionary<String, Object?>
                {
                    ["index"] = 0,
                    ["message"] = message,
                    ["finish_reason"] = ResolveFinishReason(root, toolCalls.Count > 0),
                },
            },
            ["usage"] = new Dictionary<String, Object?>
            {
                ["prompt_tokens"] = promptTokens,
                ["completion_tokens"] = completionTokens,
                ["total_tokens"] = promptTokens + completionTokens,
            },
        };

        return JsonHelper.ToJson(response);
    }

    /// <summary>把 Ollama 消息列表转换为 Responses input items。</summary>
    private static List<Object?> BuildInput(List<OllamaMessage> messages)
    {
        var input = new List<Object?>();
        foreach (var message in messages)
        {
            var role = (message.role ?? "").ToLowerInvariant();
            if (role == "tool")
            {
                input.Add(new Dictionary<String, Object?>
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.tool_call_id ?? "unknown",
                    ["output"] = message.content ?? "",
                });
                continue;
            }

            if (!String.IsNullOrEmpty(message.content) || role != "assistant")
            {
                input.Add(new Dictionary<String, Object?>
                {
                    ["role"] = role,
                    ["content"] = BuildMessageContent(message),
                });
            }

            // Responses 用独立 function_call item 表示助手历史中的工具调用，不能嵌在 message 内。
            if (role == "assistant")
            {
                foreach (var toolCall in ParseToolCalls(message.tool_calls))
                {
                    input.Add(new Dictionary<String, Object?>
                    {
                        ["type"] = "function_call",
                        ["call_id"] = toolCall.id,
                        ["name"] = toolCall.name,
                        ["arguments"] = toolCall.arguments,
                    });
                }
            }
        }
        return input;
    }

    /// <summary>构造 Responses message.content；用户图片转换为 input_image 块。</summary>
    private static Object BuildMessageContent(OllamaMessage message)
    {
        if (message.images == null || message.images.Count == 0 ||
            !String.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return message.content ?? "";
        }

        var parts = new List<Object?>();
        if (!String.IsNullOrEmpty(message.content))
            parts.Add(new Dictionary<String, Object?> { ["type"] = "input_text", ["text"] = message.content });
        foreach (var image in message.images)
        {
            var (mime, base64) = OpenAiAdapter.SplitImage(image);
            parts.Add(new Dictionary<String, Object?>
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:{mime};base64,{base64}",
            });
        }
        return parts;
    }

    /// <summary>把 Chat Completions 的嵌套函数工具转换为 Responses 的扁平函数工具。</summary>
    private static List<Object?> ConvertTools(Object tools)
    {
        var result = new List<Object?>();
        foreach (var item in AsList(tools))
        {
            if (item is not Dictionary<String, Object?> tool) continue;
            var function = tool.Val("function") as Dictionary<String, Object?>;
            if (function == null) continue;

            var converted = new Dictionary<String, Object?>
            {
                ["type"] = "function",
                ["name"] = function.Val("name")?.ToString() ?? "",
                ["parameters"] = ToolSchemaSanitizer.Sanitize(function.Val("parameters"))
                    ?? new Dictionary<String, Object?> { ["type"] = "object" },
            };
            if (function.Val("description") != null) converted["description"] = function.Val("description");
            if (function.Val("strict") != null) converted["strict"] = function.Val("strict");
            result.Add(converted);
        }
        return result;
    }

    /// <summary>把 Chat Completions 的指定函数 tool_choice 转换为 Responses 形状。</summary>
    private static Object ConvertToolChoice(Object toolChoice)
    {
        if (toolChoice is not Dictionary<String, Object?> choice) return toolChoice;
        var function = choice.Val("function") as Dictionary<String, Object?>;
        if (function == null) return toolChoice;
        return new Dictionary<String, Object?>
        {
            ["type"] = "function",
            ["name"] = function.Val("name")?.ToString() ?? "",
        };
    }

    /// <summary>解析 OpenAI 风格 tool_calls，供 Responses 历史 function_call item 使用。</summary>
    private static IEnumerable<(String id, String name, String arguments)> ParseToolCalls(Object? toolCalls)
    {
        foreach (var item in AsList(toolCalls))
        {
            if (item is not Dictionary<String, Object?> call) continue;
            var function = call.Val("function") as Dictionary<String, Object?>;
            if (function == null) continue;
            var rawArguments = function.Val("arguments");
            var arguments = rawArguments is String text ? text : JsonHelper.ToJson(rawArguments ?? new Dictionary<String, Object?>());
            yield return (call.Val("id")?.ToString() ?? "", function.Val("name")?.ToString() ?? "", arguments);
        }
    }

    /// <summary>分发 Responses SSE 事件，并转换为 OpenAI Chat 流式块。</summary>
    private static void DispatchEvent(String eventType, Dictionary<String, Object?> root, Action<String> emit,
        Dictionary<String, Int32> toolIndexes, ref Int32 nextToolIndex, ref Boolean hasToolCall, ref Boolean emittedFinal)
    {
        switch (eventType)
        {
            case "response.output_text.delta":
                EmitTextChunk(emit, "content", root.Val("delta")?.ToString() ?? "");
                break;

            case "response.reasoning_text.delta":
            case "response.reasoning_summary_text.delta":
                EmitTextChunk(emit, "reasoning_content", root.Val("delta")?.ToString() ?? "");
                break;

            case "response.output_item.added":
                if (root.Val("item") is Dictionary<String, Object?> item && item.Val("type")?.ToString() == "function_call")
                {
                    hasToolCall = true;
                    var itemId = item.Val("id")?.ToString() ?? item.Val("call_id")?.ToString() ?? "";
                    var callId = item.Val("call_id")?.ToString() ?? itemId;
                    var index = GetToolIndex(toolIndexes, itemId, ref nextToolIndex);
                    emit(BuildToolCallChunk(index, callId, item.Val("name")?.ToString() ?? "", item.Val("arguments")?.ToString() ?? ""));
                }
                break;

            case "response.function_call_arguments.delta":
                hasToolCall = true;
                var key = root.Val("item_id")?.ToString() ?? root.Val("output_index")?.ToString() ?? "";
                var toolIndex = GetToolIndex(toolIndexes, key, ref nextToolIndex);
                emit(BuildToolCallArgumentsChunk(toolIndex, root.Val("delta")?.ToString() ?? ""));
                break;

            case "response.completed":
            case "response.incomplete":
                var response = root.Val("response") as Dictionary<String, Object?> ?? root;
                var usage = response.Val("usage") as Dictionary<String, Object?>;
                emit(BuildUsageChunk(ToLong(usage.Val("input_tokens")), ToLong(usage.Val("output_tokens")),
                    ResolveFinishReason(response, hasToolCall)));
                emittedFinal = true;
                break;

            case "response.failed":
            case "error":
                var error = root.Val("error") as Dictionary<String, Object?>;
                throw HubException.BadGateway(error.Val("message")?.ToString() ?? "Responses 上游返回失败事件");
        }
    }

    /// <summary>发送普通文本或推理文本的 OpenAI 增量块。</summary>
    private static void EmitTextChunk(Action<String> emit, String field, String text)
    {
        if (String.IsNullOrEmpty(text)) return;
        emit("{\"choices\":[{\"delta\":{" + JsonHelper.ToJson(field) + ":" + JsonHelper.ToJson(text) + "}}]}");
    }

    /// <summary>为 Responses function_call item 分配稳定且连续的 OpenAI tool_calls index。</summary>
    private static Int32 GetToolIndex(Dictionary<String, Int32> indexes, String key, ref Int32 nextIndex)
    {
        if (indexes.TryGetValue(key, out var index)) return index;
        index = nextIndex++;
        indexes[key] = index;
        return index;
    }

    /// <summary>构造工具调用首块，包含 call_id、函数名和可能的初始参数。</summary>
    private static String BuildToolCallChunk(Int32 index, String id, String name, String arguments)
        => "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":" + index +
           ",\"id\":" + JsonHelper.ToJson(id) + ",\"type\":\"function\",\"function\":{\"name\":" +
           JsonHelper.ToJson(name) + ",\"arguments\":" + JsonHelper.ToJson(arguments) + "}}]}}]}";

    /// <summary>构造工具调用参数增量块。</summary>
    private static String BuildToolCallArgumentsChunk(Int32 index, String arguments)
        => "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":" + index +
           ",\"function\":{\"arguments\":" + JsonHelper.ToJson(arguments) + "}}]}}]}";

    /// <summary>构造带 finish_reason 与 token 用量的 OpenAI 结束块。</summary>
    private static String BuildUsageChunk(Int64 prompt, Int64 completion, String finishReason)
        => "{\"choices\":[{\"delta\":{},\"finish_reason\":" + JsonHelper.ToJson(finishReason) + "}]," +
           "\"usage\":{\"prompt_tokens\":" + prompt + ",\"completion_tokens\":" + completion +
           ",\"total_tokens\":" + (prompt + completion) + "}}";

    /// <summary>把 Responses function_call 输出项转换为 OpenAI tool_call。</summary>
    private static Object ToOpenAiToolCall(Dictionary<String, Object?> item)
        => new Dictionary<String, Object?>
        {
            ["id"] = item.Val("call_id")?.ToString() ?? item.Val("id")?.ToString() ?? "",
            ["type"] = "function",
            ["function"] = new Dictionary<String, Object?>
            {
                ["name"] = item.Val("name")?.ToString() ?? "",
                ["arguments"] = item.Val("arguments")?.ToString() ?? "{}",
            },
        };

    /// <summary>从 Responses message.content 中提取 output_text 文本（Ollama 无 refusal 字段，拒绝内容降级丢弃）。</summary>
    private static void AppendMessageContent(Object? value, StringBuilder target)
    {
        foreach (var item in AsList(value))
        {
            if (item is not Dictionary<String, Object?> part) continue;
            var type = part.Val("type")?.ToString();
            if (type is "output_text") target.Append(part.Val("text")?.ToString());
        }
    }

    /// <summary>从 reasoning.summary 中提取推理摘要文本。</summary>
    private static void AppendReasoning(Object? value, StringBuilder target)
    {
        foreach (var item in AsList(value))
        {
            if (item is Dictionary<String, Object?> part)
                target.Append(part.Val("text")?.ToString());
        }
    }

    /// <summary>根据工具调用、完成状态和 incomplete_details 映射 OpenAI finish_reason。</summary>
    private static String ResolveFinishReason(Dictionary<String, Object?> response, Boolean hasToolCall)
    {
        if (hasToolCall) return "tool_calls";
        var incomplete = response.Val("incomplete_details") as Dictionary<String, Object?>;
        return incomplete.Val("reason")?.ToString() == "max_output_tokens" ? "length" : "stop";
    }

    /// <summary>把动态 JSON 数组安全转换为可枚举列表（兼容 List&lt;Object?&gt; / List&lt;Object&gt; / 数组）。</summary>
    private static IEnumerable<Object?> AsList(Object? value)
    {
        if (value is List<Object?> lo) return lo;
        if (value is List<Object> lo2) return lo2;
        if (value is Object[] arr) return arr;
        return new List<Object?>();
    }

    /// <summary>从 Ollama options 读取整数参数。</summary>
    private static Boolean TryGetInt(Dictionary<String, Object>? options, String key, out Int32 value)
    {
        value = 0;
        if (options == null || !options.TryGetValue(key, out var raw) || raw == null) return false;
        if (raw is Int32 i32) { value = i32; return true; }
        if (raw is Int64 i64) { value = (Int32)i64; return true; }
        return Int32.TryParse(raw.ToString(), out value);
    }

    /// <summary>从 Ollama options 读取浮点参数。</summary>
    private static Boolean TryGetDouble(Dictionary<String, Object>? options, String key, out Double value)
    {
        value = 0;
        if (options == null || !options.TryGetValue(key, out var raw) || raw == null) return false;
        if (raw is Double number) { value = number; return true; }
        if (raw is Single single) { value = single; return true; }
        if (raw is Int64 i64) { value = i64; return true; }
        if (raw is Int32 i32) { value = i32; return true; }
        return Double.TryParse(raw.ToString(), out value);
    }

    /// <summary>把动态 JSON 数值安全转换为 Int64。</summary>
    private static Int64 ToLong(Object? value)
    {
        if (value is Int64 i64) return i64;
        if (value is Int32 i32) return i32;
        return Int64.TryParse(value?.ToString(), out var number) ? number : 0;
    }
}
