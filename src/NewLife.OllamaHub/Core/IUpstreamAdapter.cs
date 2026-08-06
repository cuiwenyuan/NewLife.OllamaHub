using System;
using System.Collections.Generic;
using System.Net.Http;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 上游协议适配器接口（M6：支持 openai / responses / anthropic / gemini 等多种上游）。
/// 设计要点——所有适配器都把"自家上游的 SSE 块 / 非流式响应"翻译成统一的
/// <b>OpenAI 形状</b>，再交给既有的 <see cref="OllamaStreamTranslator"/>（专吃 OpenAI 块）
/// 与 <see cref="OpenAiAdapter"/> 的 NDJSON 转换逻辑，从而避免为每种上游各写一套流式累积。
/// 这样新增上游只需实现"请求体构造 + 响应→OpenAI 形状"两件事，复用全部下游代码。
/// </summary>
public interface IUpstreamAdapter
{
    /// <summary>协议模式标识（与 ProviderOptions.ApiMode 对应，如 openai / responses / anthropic / gemini）。</summary>
    String ApiMode { get; }

    /// <summary>把 Ollama /api/chat 请求转换为该上游的请求体 JSON。</summary>
    /// <param name="req">Ollama 聊天请求（不可为 null）。</param>
    /// <param name="model">模型配置（用于取上游模型名与 dropParams）。</param>
    /// <param name="forceStream">是否强制向上游请求流式（M2 起统一 true 以做流式桥接）。</param>
    String BuildRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream);

    /// <summary>由供应商 BaseUrl + 模型拼出完整上游请求 URL。</summary>
    String GetRequestUrl(ProviderOptions provider, ModelOptions model);

    /// <summary>写入鉴权头与协议必须头（如 Anthropic 的 anthropic-version）。</summary>
    void ApplyAuth(HttpRequestMessage req, ProviderOptions provider, String apiKey);

    /// <summary>
    /// 读取上游流式响应，把每个上游数据块翻译成 OpenAI 形状的块 JSON 并通过 <paramref name="emitOpenAiChunk"/> 回调。
    /// </summary>
    /// <param name="resp">已拿到响应头的上游响应（成功状态码）。</param>
    /// <param name="emitOpenAiChunk">回调，接收 1 个 OpenAI 形状的 SSE 块 JSON（供 <see cref="OllamaStreamTranslator.Consume"/> 消费）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若上游确实以 SSE 发流则返回 null（帧已通过回调发出）；若上游回了单 JSON（未用 SSE），则返回该原始响应体（调用方应降级走非流式）。</returns>
    String? ReadStream(HttpResponseMessage resp, Action<String> emitOpenAiChunk, System.Threading.CancellationToken cancellationToken);

    /// <summary>把上游非流式响应 JSON 转换为 OpenAI 形状 JSON（供 <see cref="OpenAiAdapter.ToOllamaNdJson"/> 复用）。</summary>
    String ConvertNonStream(String upstreamJson, ModelOptions model);
}

/// <summary>
/// 上游适配器工厂（M6）。按 ProviderOptions.ApiMode 分发到具体实现。
/// 未知模式安全回落到 OpenAI（最通用的兼容层）。
/// </summary>
public static class UpstreamAdapterFactory
{
    private static readonly Dictionary<String, IUpstreamAdapter> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new OpenAiUpstreamAdapter(),
        ["responses"] = new ResponsesUpstreamAdapter(),
        ["anthropic"] = new AnthropicUpstreamAdapter(),
        ["gemini"] = new GeminiUpstreamAdapter(),
        ["google"] = new GeminiUpstreamAdapter(),
    };

    /// <summary>按 ApiMode 取适配器；未知值回落 openai 并记录告警。</summary>
    public static IUpstreamAdapter Get(String? apiMode)
    {
        var mode = String.IsNullOrEmpty(apiMode) ? "openai" : apiMode!;
        if (_map.TryGetValue(mode, out var a)) return a;

        XTrace.WriteLine("未知 ApiMode={0}，回落到 openai 适配器", mode);
        return _map["openai"];
    }
}
