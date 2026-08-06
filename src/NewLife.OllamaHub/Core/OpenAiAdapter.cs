using System;
using System.Collections.Generic;
using System.Text;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// OpenAI 兼容协议 ⇄ Ollama 协议 转换（M1 非流式）。
/// 职责：
///   - Ollama /api/chat 请求 → OpenAI /v1/chat/completions 请求（JSON 字符串）
///   - OpenAI 非流式响应 → Ollama 单帧 NDJSON 响应（done:true）
///   - 模型级 dropParams（推理模型不支持 temperature/top_p 等）生效
///   - reasoning_content ⇄ thinking 映射
/// 流式桥接与更细的 schema 清洗在 M2 扩展。
/// </summary>
public static class OpenAiAdapter
{
    /// <summary>把 Ollama 聊天请求转换为 OpenAI 请求体 JSON。</summary>
    /// <param name="req">Ollama /api/chat 请求（不可为 null）。</param>
    /// <param name="model">模型配置（用于取上游 model 名与 dropParams）。</param>
    /// <param name="forceStream">强制在请求体写入 stream:true（流式桥接场景，忽略客户端原始设置）。</param>
    /// <returns>可直接 POST 给上游 /v1/chat/completions 的 JSON 字符串。</returns>
    public static String BuildOpenAiRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream = false)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (model == null) throw new ArgumentNullException(nameof(model));

        // P0-3 force-mode：先按模型配置填充/覆盖采样参数，再走既有 dropParams 逻辑
        ApplyModelParams(req, model);

        // 用字典逐字段构造，而非"定义带可空属性的 DTO 再序列化"：
        //   - 若保留可空 DTO 且 nullValue=true，会输出 "temperature":null，dropParams 形同虚设，
        //     推理类模型（DeepSeek-R1 等）收到显式 null 直接报参数错误；
        //   - 若改用 nullValue=false，序列化器连"默认值"一并丢弃，
        //     用户显式指定的 temperature=0（确定性输出）会被静默吞掉，stream:false 也会消失。
        // 字典方式对"是否发送某字段"有完全控制权，是唯一两头都不踩的做法。
        var messages = new List<Object>();
        foreach (var m in req.messages)
        {
            // 多模态：user 消息带 images 时，content 必须是数组（text + image_url 交替），
            // 否则仍用纯字符串 content（OpenAI 规范里仅含 tool_calls 的 assistant 消息 content:null）
            if (m.images != null && m.images.Count > 0 && String.Equals(m.role, "user", StringComparison.OrdinalIgnoreCase))
            {
                var parts = new List<Object?>();
                if (!String.IsNullOrEmpty(m.content))
                    parts.Add(new Dictionary<String, Object?> { ["type"] = "text", ["text"] = m.content });
                foreach (var img in m.images)
                {
                    var (mime, b64) = SplitImage(img);
                    parts.Add(new Dictionary<String, Object?>
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new Dictionary<String, Object?> { ["url"] = $"data:{mime};base64,{b64}" },
                    });
                }
                var oneMulti = new Dictionary<String, Object?> { ["role"] = "user", ["content"] = parts };
                if (m.tool_calls != null) oneMulti["tool_calls"] = m.tool_calls;
                messages.Add(oneMulti);
            }
            else
            {
                var one = new Dictionary<String, Object?>
                {
                    ["role"] = m.role,
                    ["content"] = m.content,
                };
                if (m.tool_calls != null) one["tool_calls"] = m.tool_calls;
                // P0-1：推理模型多轮对话时，把缓存的 reasoning_content 随 assistant 消息回传上游，保持推理连贯
                if (String.Equals(m.role, "assistant", StringComparison.OrdinalIgnoreCase)
                    && m.thinking is String th && th.Length > 0)
                {
                    one["reasoning_content"] = th;
                }
                messages.Add(one);
            }
        }

        var oa = new Dictionary<String, Object?>
        {
            // 上游模型标识：默认使用 ModelOptions.Id（如 deepseek-v4-flash 即 DeepSeek 官方模型名）
            ["model"] = model.Id,
            ["messages"] = messages,
            // stream：默认透传客户端偏好（Copilot 通常 stream:true）；
            // forceStream=true 时强制向上游请求 SSE，由翻译器统一累积后再按 req.stream 决定下发形态，
            // 以兼容"忽略 stream:false 仍回 SSE"的上游（详见 HandleChat 注释）。
            ["stream"] = forceStream ? true : req.stream,
        };

        // P0-3：reasoning_effort 仅在 openai 上游生效（o-series 等），客户端不 forwards 该字段，
        // 故只要模型配置了即下发（受 OverrideClientParams 语义一致：缺省即填、强制即覆盖）。
        if (!String.IsNullOrEmpty(model.ReasoningEffort))
            oa["reasoning_effort"] = model.ReasoningEffort;

        // 采样参数：从 Ollama options 提取，受模型 dropParams 约束
        var drop = model.DropParams ?? new List<String>();
        if (!drop.Contains("temperature") && TryGetDouble(req.options, "temperature", out var t)) oa["temperature"] = t;
        if (!drop.Contains("top_p") && TryGetDouble(req.options, "top_p", out var tp)) oa["top_p"] = tp;
        if (!drop.Contains("max_tokens") &&
            (TryGetInt(req.options, "max_tokens", out var mt) || TryGetInt(req.options, "num_predict", out mt)))
        {
            oa["max_tokens"] = mt;
        }

        // 工具定义：Copilot Agent 模式下发（OpenAI 风格），经 schema 清洗后原样透传上游。
        // 不清洗的话上游可能因 $schema / additionalProperties 等直接 400。
        if (req.tools != null)
        {
            var cleaned = ToolSchemaSanitizer.Sanitize(req.tools);
            if (cleaned != null) oa["tools"] = cleaned;
        }
        // 工具选择策略（auto / none / 指定函数），原样透传
        if (req.tool_choice != null) oa["tool_choice"] = req.tool_choice;

        return JsonHelper.ToJson(oa);
    }

    /// <summary>
    /// P0-3 force-mode：按模型配置填充/覆盖采样参数（temperature / top_p / max_tokens）。
    /// 原地修改 <paramref name="req"/>.options，使后续 dropParams 逻辑在已应用的值之上继续过滤。
    /// </summary>
    /// <param name="req">Ollama 聊天请求（options 会被原地修改）。</param>
    /// <param name="model">模型配置（提供采样默认值与 <see cref="ModelOptions.OverrideClientParams"/> 开关）。</param>
    internal static void ApplyModelParams(OllamaChatRequest req, ModelOptions model)
    {
        if (req == null || model == null) return;
        var opts = req.options ??= new Dictionary<String, Object>();
        var force = model.OverrideClientParams;

        ApplyParam(opts, "temperature", model.Temperature, force);
        ApplyParam(opts, "top_p", model.TopP, force);
        if (model.MaxTokens > 0)
        {
            // max_tokens 的"客户端已指定"需同时识别上游别名 num_predict，
            // 否则模型默认 4096 会在非强制模式下覆盖客户端用 num_predict 表达的意图（回归点）。
            var clientHas = HasNumeric(opts, "max_tokens") || HasNumeric(opts, "num_predict");
            if (force || !clientHas) opts["max_tokens"] = model.MaxTokens;
        }
    }

    private static void ApplyParam(Dictionary<String, Object> opts, String key, Object? configured, Boolean force)
    {
        if (configured == null) return;
        var hasClient = opts.TryGetValue(key, out var o) && o != null && IsNumber(o);
        // 强制模式：一律覆盖；默认模式：仅当客户端缺省时填入
        if (force) opts[key] = configured;
        else if (!hasClient) opts[key] = configured;
    }

    private static Boolean HasNumeric(Dictionary<String, Object> opts, String key)
        => opts.TryGetValue(key, out var o) && o != null && IsNumber(o);

    private static Boolean IsNumber(Object o) => o is Double or Single or Int64 or Int32;

    /// <summary>把上游 OpenAI 非流式响应转换为 Ollama /api/chat 单帧 NDJSON（含尾换行）。</summary>
    /// <param name="oaJson">上游返回的 JSON 文本。</param>
    /// <param name="model">模型配置（用于回填 model 字段）。</param>
    /// <returns>一行 JSON（done:true）+ 换行，可直接作为响应体。</returns>
    public static String ToOllamaNdJson(String oaJson, ModelOptions model)
    {
        var r = ParseUpstream(oaJson, model);

        // 同样用字典构造：thinking / tool_calls 缺省时必须整个省略（真实 Ollama 如此），
        // 而 done 等字段必须始终输出——M2 流式帧的 done:false 若被序列化器当默认值丢弃，
        // 客户端将永远等不到结束信号。
        var message = new Dictionary<String, Object>
        {
            ["role"] = "assistant",
            ["content"] = r.Content,
        };
        if (r.Thinking != null) message["thinking"] = r.Thinking;
        if (r.ToolCalls != null) message["tool_calls"] = r.ToolCalls;

        var resp = new Dictionary<String, Object>
        {
            ["model"] = model.Id,
            ["created_at"] = r.CreatedAt,
            ["message"] = message,
            ["done"] = true,
            ["done_reason"] = r.DoneReason,
            ["eval_count"] = r.EvalCount,
            ["prompt_eval_count"] = r.PromptEvalCount,
        };

        return ToNdJsonLine(resp);
    }

    /// <summary>
    /// 把上游 OpenAI 非流式响应转换为 Ollama /api/generate 单帧 NDJSON（含尾换行）。
    /// 与 chat 的区别：内容放在 response 字符串字段而非 message 对象。
    /// </summary>
    /// <param name="oaJson">上游返回的 JSON 文本。</param>
    /// <param name="model">模型配置（用于回填 model 字段）。</param>
    /// <returns>一行 JSON（done:true）+ 换行。</returns>
    public static String ToOllamaGenerateNdJson(String oaJson, ModelOptions model)
    {
        var r = ParseUpstream(oaJson, model);

        var resp = new Dictionary<String, Object>
        {
            ["model"] = model.Id,
            ["created_at"] = r.CreatedAt,
            // 关键差异：generate 用 response 字符串承载内容，没有 message 对象
            ["response"] = r.Content,
            ["done"] = true,
            ["done_reason"] = r.DoneReason,
            ["eval_count"] = r.EvalCount,
            ["prompt_eval_count"] = r.PromptEvalCount,
        };
        if (r.Thinking != null) resp["thinking"] = r.Thinking;

        return ToNdJsonLine(resp);
    }

    /// <summary>解析上游响应，抽取 chat/generate 共用的字段。</summary>
    private static UpstreamResult ParseUpstream(String oaJson, ModelOptions model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (String.IsNullOrEmpty(oaJson))
            throw HubException.BadGateway("上游返回空响应");

        OpenAiChatResponse? oa;
        try
        {
            oa = JsonHelper.ToJsonEntity<OpenAiChatResponse>(oaJson);
        }
        catch (Exception ex)
        {
            // 上游可能返回 HTML 错误页或非标准 JSON，需附带原文片段便于排查
            throw HubException.BadGateway($"无法解析上游响应：{Truncate(oaJson, 200)}", ex);
        }
        if (oa == null) throw HubException.BadGateway($"无法解析上游响应：{Truncate(oaJson, 200)}");

        var choice = oa.choices != null && oa.choices.Count > 0 ? oa.choices[0] : null;
        var msg = choice?.message;

        return new UpstreamResult
        {
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Content = msg?.content ?? "",
            // 推理模型：上游 reasoning_content → Ollama thinking
            Thinking = msg?.reasoning_content,
            ToolCalls = msg?.tool_calls,
            DoneReason = choice?.finish_reason == "length" ? "length" : "stop",
            EvalCount = oa.usage?.completion_tokens ?? 0,
            PromptEvalCount = oa.usage?.prompt_tokens ?? 0,
        };
    }

    /// <summary>
    /// 序列化为 NDJSON 单行。
    /// 使用默认 nullValue=true：字段是否输出已在字典构造阶段精确控制，
    /// 此处不能再交给序列化器做"省略"判断，否则 done:false / eval_count:0 会被误删。
    /// </summary>
    private static String ToNdJsonLine(Object resp) => JsonHelper.ToJson(resp) + "\n";

    private static String Truncate(String text, Int32 max)
        => text.Length <= max ? text : text.Substring(0, max) + "…";

    /// <summary>上游响应解析结果（chat / generate 共用）。</summary>
    private class UpstreamResult
    {
        public String CreatedAt { get; set; } = "";
        public String Content { get; set; } = "";
        public Object? Thinking { get; set; }
        public Object? ToolCalls { get; set; }
        public String DoneReason { get; set; } = "stop";
        public Int64 EvalCount { get; set; }
        public Int64 PromptEvalCount { get; set; }
    }

    /// <summary>
    /// 拆分多模态图片为 (mime, base64)。支持两种入参：
    ///   - 含 data URI 前缀（<c>data:image/png;base64,xxxx</c>）→ 取其 mime 与纯 base64；
    ///   - 纯 base64 原文（Ollama /api/chat 的 images 字段即如此）→ 默认 image/png。
    /// </summary>
    internal static (String mime, String b64) SplitImage(String image)
    {
        if (String.IsNullOrEmpty(image)) return ("image/png", "");
        const String prefix = "data:";
        if (image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var comma = image.IndexOf(',');
            if (comma > 0)
            {
                var meta = image.Substring(prefix.Length, comma - prefix.Length);
                var mime = meta.Contains(";") ? meta.Split(';')[0] : meta;
                if (!String.IsNullOrEmpty(mime)) return (mime, image.Substring(comma + 1));
            }
        }
        return ("image/png", image);
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

    private static Boolean TryGetInt(Dictionary<String, Object>? opts, String key, out Int32 val)
    {
        val = 0;
        if (opts == null || !opts.TryGetValue(key, out var o) || o == null) return false;
        if (o is Int32 i32) { val = i32; return true; }
        if (o is Int64 i64) { val = (Int32)i64; return true; }
        return Int32.TryParse(o.ToString(), out val);
    }
}

/// <summary>OpenAI /v1/chat/completions 请求（仅含 M1 需要的字段）。</summary>
public class OpenAiChatRequest
{
    /// <summary>上游模型名。</summary>
    public String model { get; set; } = "";

    /// <summary>消息列表。</summary>
    public List<OpenAiMessage> messages { get; set; } = new();

    /// <summary>是否流式（M1 恒 false）。</summary>
    public Boolean stream { get; set; }

    /// <summary>温度（受模型 dropParams 约束）。</summary>
    public Double? temperature { get; set; }

    /// <summary>top_p（受模型 dropParams 约束）。</summary>
    public Double? top_p { get; set; }

    /// <summary>最大生成 token。</summary>
    public Int32? max_tokens { get; set; }

    /// <summary>工具定义（Copilot Agent 模式下发，OpenAI 风格），原样透传上游。</summary>
    public Object? tools { get; set; }

    /// <summary>工具选择策略（auto / none / 指定函数），原样透传上游。</summary>
    public Object? tool_choice { get; set; }
}

/// <summary>OpenAI 消息。</summary>
public class OpenAiMessage
{
    /// <summary>角色。</summary>
    public String role { get; set; } = "";

    /// <summary>文本内容。</summary>
    public String? content { get; set; }

    /// <summary>工具调用（Copilot 下发，原样透传）。</summary>
    public Object? tool_calls { get; set; }

    /// <summary>推理过程（DeepSeek 等返回，映射至 Ollama thinking）。</summary>
    public String? reasoning_content { get; set; }
}

/// <summary>OpenAI /v1/chat/completions 非流式响应。</summary>
public class OpenAiChatResponse
{
    /// <summary>选择列表（取 [0]）。</summary>
    public List<OpenAiChoice> choices { get; set; } = new();

    /// <summary>用量统计。</summary>
    public OpenAiUsage usage { get; set; } = new();
}

/// <summary>OpenAI 单个选择。</summary>
public class OpenAiChoice
{
    /// <summary>生成的消息。</summary>
    public OpenAiMessage message { get; set; } = new();

    /// <summary>结束原因：stop / length / tool_calls 等。</summary>
    public String finish_reason { get; set; } = "";
}

/// <summary>OpenAI 用量。</summary>
public class OpenAiUsage
{
    /// <summary>提示 token 数。</summary>
    public Int32 prompt_tokens { get; set; }

    /// <summary>生成 token 数。</summary>
    public Int32 completion_tokens { get; set; }

    /// <summary>总 token 数。</summary>
    public Int32 total_tokens { get; set; }
}

/// <summary>
/// 把多个 OpenAI 流式块（<see cref="OpenAiChunk"/>）累积成单个非流式 OpenAI 响应 JSON。
/// 用于 /v1/chat/completions 在客户端要求非流式（stream:false）时的输出。
/// 与 <see cref="OllamaStreamTranslator"/> 对称：后者把上游 OpenAI 块翻成 Ollama NDJSON，
/// 本类把同一份 OpenAI 块聚合成 OpenAI 单 JSON。
/// </summary>
public sealed class OpenAiAccumulator
{
    private readonly ModelOptions _model;
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly List<Dictionary<String, Object?>> _toolCalls = new();
    private String _id = "";
    private String _upstreamModel = "";
    private Int64 _created;
    private String _finishReason = "stop";
    private Int32 _promptTokens, _completionTokens;

    /// <summary>构造累积器。</summary>
    public OpenAiAccumulator(ModelOptions model) => _model = model ?? throw new ArgumentNullException(nameof(model));

    /// <summary>消费一个 OpenAI SSE 块 JSON（已去除 data: 前缀）。</summary>
    public void Consume(String sseJson)
    {
        if (String.IsNullOrEmpty(sseJson)) return;
        OpenAiChunk? chunk;
        try { chunk = JsonHelper.ToJsonEntity<OpenAiChunk>(sseJson); }
        catch { return; }
        if (chunk == null) return;

        if (!String.IsNullOrEmpty(chunk.id)) _id = chunk.id!;
        if (!String.IsNullOrEmpty(chunk.model)) _upstreamModel = chunk.model!;
        if (chunk.created != 0) _created = chunk.created;

        var choice = chunk.choices != null && chunk.choices.Count > 0 ? chunk.choices[0] : null;
        var delta = choice?.delta;
        if (delta?.content != null) _content.Append(delta.content);
        if (delta?.reasoning_content != null) _reasoning.Append(delta.reasoning_content);
        if (delta?.tool_calls != null) MergeToolCalls(delta.tool_calls);
        if (!String.IsNullOrEmpty(choice?.finish_reason)) _finishReason = choice!.finish_reason!;

        if (chunk.usage != null)
        {
            _promptTokens = chunk.usage.prompt_tokens;
            _completionTokens = chunk.usage.completion_tokens;
        }
    }

    /// <summary>产出单个非流式 OpenAI 响应 JSON。</summary>
    public String BuildSingle()
    {
        var created = _created != 0 ? _created : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var message = new Dictionary<String, Object?>
        {
            ["role"] = "assistant",
            ["content"] = _content.ToString(),
        };
        if (_reasoning.Length > 0) message["reasoning_content"] = _reasoning.ToString();
        if (_toolCalls.Count > 0) message["tool_calls"] = _toolCalls.ToArray();

        var choice = new Dictionary<String, Object?>
        {
            ["index"] = 0,
            ["message"] = message,
            ["finish_reason"] = _finishReason,
        };

        var resp = new Dictionary<String, Object?>
        {
            ["id"] = String.IsNullOrEmpty(_id) ? "chatcmpl-ollamahub" : _id,
            ["object"] = "chat.completion",
            ["created"] = created,
            ["model"] = String.IsNullOrEmpty(_upstreamModel) ? _model.Id : _upstreamModel,
            ["choices"] = new[] { choice },
            ["usage"] = new Dictionary<String, Object?>
            {
                ["prompt_tokens"] = _promptTokens,
                ["completion_tokens"] = _completionTokens,
                ["total_tokens"] = _promptTokens + _completionTokens,
            },
        };
        return JsonHelper.ToJson(resp);
    }

    /// <summary>截至当前累积的推理文本（P0-1 多轮缓存用）。</summary>
    public String ReasoningText => _reasoning.ToString();

    /// <summary>截至当前的累计提示 token（供 UsageStats 埋点）。</summary>
    public Int64 PromptTokens => _promptTokens;

    /// <summary>截至当前的累计生成 token（供 UsageStats 埋点）。</summary>
    public Int64 CompletionTokens => _completionTokens;

    private void MergeToolCalls(List<OpenAiToolCallDelta>? deltas)
    {
        if (deltas == null) return;
        foreach (var d in deltas)
        {
            Dictionary<String, Object?> tc;
            if (d.index >= 0 && d.index < _toolCalls.Count) tc = _toolCalls[d.index];
            else { tc = new Dictionary<String, Object?>(); _toolCalls.Add(tc); }

            if (!String.IsNullOrEmpty(d.id)) tc["id"] = d.id;
            if (!String.IsNullOrEmpty(d.type)) tc["type"] = d.type;
            if (d.function != null)
            {
                var fn = tc.ContainsKey("function") && tc["function"] is Dictionary<String, Object?> f
                    ? f : new Dictionary<String, Object?>();
                if (!String.IsNullOrEmpty(d.function.name)) fn["name"] = d.function.name;
                if (d.function.arguments != null)
                {
                    var prev = fn.ContainsKey("arguments") && fn["arguments"] != null ? fn["arguments"]!.ToString() : "";
                    fn["arguments"] = prev + d.function.arguments;
                }
                tc["function"] = fn;
            }
        }
    }
}
