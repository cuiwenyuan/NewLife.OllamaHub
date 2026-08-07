using System;
using System.Collections.Generic;
using System.Text;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// OpenAI SSE 流 → Ollama NDJSON 帧 的增量翻译器（M2 流式）。
/// 设计要点（与真实 Ollama 行为对齐）：
///   - Ollama 每个流式帧携带当前块的【增量】content / thinking；
///     这里同时累积完整内容用于非流式响应与推理缓存，但流式下发只发送本块增量。
///   - 上游 SSE 末块通常带 finish_reason 与 usage（或单独一个 usage 块），
///     流结束后由 Finalize() 补一帧 done:true（含 eval_count / prompt_eval_count）。
///   - 推理模型的 reasoning_content 累积进 thinking；工具调用按 index 合并后放进末帧。
/// 由于 NewLife.HttpServer 是单发响应模型（见 Probe 结论），调用方负责把每帧
/// 用换行连接成完整 NDJSON 体后一次性 WriteRaw——此处只产出"帧"，不碰 socket。
/// </summary>
public sealed class OllamaStreamTranslator
{
    private readonly ModelOptions _model;
    private readonly Boolean _forGenerate;
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _thinking = new();
    private readonly List<Dictionary<String, Object?>> _toolCalls = new();
    private Int64 _promptEval;
    private Int64 _eval;
    private String _doneReason = "stop";

    /// <summary>构造翻译器。</summary>
    /// <param name="model">模型配置（用于回填 model 字段）。</param>
    /// <param name="forGenerate">true 时产出 response 字符串字段（/api/generate），否则产出 message 对象（/api/chat）。</param>
    public OllamaStreamTranslator(ModelOptions model, Boolean forGenerate)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _forGenerate = forGenerate;
    }

    /// <summary>消费一个上游 SSE data: 块的 JSON，返回本块增量 done:false 帧（不含换行）。</summary>
    /// <param name="sseJson">已去除 "data:" 前缀的 OpenAI 块 JSON。</param>
    /// <returns>本块增量帧 JSON 字符串（done:false）。</returns>
    public String Consume(String sseJson)
    {
        if (String.IsNullOrEmpty(sseJson)) return BuildFrame(false, "", "");

        OpenAiChunk? chunk;
        try { chunk = JsonHelper.ToJsonEntity<OpenAiChunk>(sseJson); }
        catch { return BuildFrame(false, "", ""); } // 单块解析失败不中断整条流，跳过该块

        if (chunk == null) return BuildFrame(false, "", "");

        var choice = chunk.choices != null && chunk.choices.Count > 0 ? chunk.choices[0] : null;
        var delta = choice?.delta;
        var contentDelta = delta?.content ?? "";
        var thinkingDelta = delta?.reasoning_content ?? "";

        _content.Append(contentDelta);
        _thinking.Append(thinkingDelta);
        if (delta?.tool_calls != null) MergeToolCalls(delta.tool_calls);

        if (!String.IsNullOrEmpty(choice?.finish_reason))
            _doneReason = choice!.finish_reason == "length" ? "length" : "stop";

        // usage 可能在末块或独立 usage 块出现，优先采用最后一次非空值
        if (chunk.usage != null)
        {
            _promptEval = chunk.usage.prompt_tokens;
            _eval = chunk.usage.completion_tokens;
        }

        return BuildFrame(false, contentDelta, thinkingDelta);
    }

    /// <summary>流结束后补一帧 done:true；流式响应默认只发送结束信号，非流式调用可保留完整内容。</summary>
    /// <param name="includeContent">是否在结束帧中包含完整内容；非流式响应需传 true。</param>
    public String Finalize(Boolean includeContent = true)
        => BuildFrame(true, includeContent ? _content.ToString() : "", includeContent ? _thinking.ToString() : "");

    /// <summary>截至当前的累计 token 用量（供 UsageStats 埋点）。</summary>
    public (Int64 Prompt, Int64 Completion) Usage => (_promptEval, _eval);

    /// <summary>截至当前累积的推理文本（P0-1 多轮缓存用）。</summary>
    public String ThinkingText => _thinking.ToString();

    // ---- 内部 ----

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
                    // arguments 分片追加：Function Calling 的入参 JSON 是跨多块流式下发的
                    var prev = fn.ContainsKey("arguments") && fn["arguments"] != null ? fn["arguments"]!.ToString() : "";
                    fn["arguments"] = prev + d.function.arguments;
                }
                tc["function"] = fn;
            }
        }
    }

    /// <summary>构造 Ollama 帧；流式帧只放当前增量，结束帧可按调用方选择放完整内容。</summary>
    private String BuildFrame(Boolean done, String content, String thinking)
    {
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (_forGenerate)
        {
            var resp = new Dictionary<String, Object?>
            {
                ["model"] = _model.Id,
                ["created_at"] = created,
                ["response"] = content,
                ["done"] = done,
                ["done_reason"] = _doneReason,
                ["eval_count"] = _eval,
                ["prompt_eval_count"] = _promptEval,
            };
            if (!String.IsNullOrEmpty(thinking)) resp["thinking"] = thinking;
            return JsonHelper.ToJson(resp);
        }

        var message = new Dictionary<String, Object?>
        {
            ["role"] = "assistant",
            ["content"] = content,
        };
        if (!String.IsNullOrEmpty(thinking)) message["thinking"] = thinking;
        if (done && _toolCalls.Count > 0) message["tool_calls"] = _toolCalls.ToArray();

        var respChat = new Dictionary<String, Object?>
        {
            ["model"] = _model.Id,
            ["created_at"] = created,
            ["message"] = message,
            ["done"] = done,
            ["done_reason"] = _doneReason,
            ["eval_count"] = _eval,
            ["prompt_eval_count"] = _promptEval,
        };
        return JsonHelper.ToJson(respChat);
    }
}

// ---- OpenAI SSE 块 DTO（仅含 M2 需要的字段） ----

/// <summary>OpenAI /v1/chat/completions 流式块。</summary>
public class OpenAiChunk
{
    /// <summary>块 Id（上游回显，常用于聚合非流式响应时回填）。</summary>
    public String? id { get; set; }

    /// <summary>上游模型标识（回显）。</summary>
    public String? model { get; set; }

    /// <summary>Unix 时间戳（秒）。</summary>
    public Int64 created { get; set; }

    /// <summary>选择列表（取 [0]）。</summary>
    public List<OpenAiChunkChoice> choices { get; set; } = new();

    /// <summary>用量统计（常在末块或独立 usage 块出现，可能为空）。</summary>
    public OpenAiUsage? usage { get; set; }
}

/// <summary>流式块中的单个选择。</summary>
public class OpenAiChunkChoice
{
    /// <summary>增量消息。</summary>
    public OpenAiChunkDelta delta { get; set; } = new();

    /// <summary>结束原因：stop / length / tool_calls 等（末块才有）。</summary>
    public String? finish_reason { get; set; }
}

/// <summary>流式增量消息。</summary>
public class OpenAiChunkDelta
{
    /// <summary>角色（首块出现）。</summary>
    public String? role { get; set; }

    /// <summary>增量文本内容。</summary>
    public String? content { get; set; }

    /// <summary>推理过程增量（DeepSeek 等）。</summary>
    public String? reasoning_content { get; set; }

    /// <summary>工具调用增量（按 index 分片下发）。</summary>
    public List<OpenAiToolCallDelta>? tool_calls { get; set; }
}

/// <summary>工具调用增量片段。</summary>
public class OpenAiToolCallDelta
{
    /// <summary>工具调用序号（用于跨块归并）。</summary>
    public Int32 index { get; set; }

    /// <summary>工具调用 Id（首片出现）。</summary>
    public String? id { get; set; }

    /// <summary>类型（通常为 function）。</summary>
    public String? type { get; set; }

    /// <summary>函数片段（name / arguments 分片下发）。</summary>
    public OpenAiToolCallDeltaFunction? function { get; set; }
}

/// <summary>工具调用增量中的函数部分。</summary>
public class OpenAiToolCallDeltaFunction
{
    /// <summary>函数名（首片出现）。</summary>
    public String? name { get; set; }

    /// <summary>函数参数 JSON 片段（跨块追加）。</summary>
    public String? arguments { get; set; }
}
