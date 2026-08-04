using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// OpenAI 兼容上游适配器（默认）。请求体构造复用 <see cref="OpenAiAdapter.BuildOpenAiRequest"/>（含多模态），
/// 响应已是 OpenAI 形状，故流式块直接转发、非流式响应原样交回 <see cref="OpenAiAdapter"/> 的 NDJSON 转换。
/// </summary>
public sealed class OpenAiUpstreamAdapter : IUpstreamAdapter
{
    /// <inheritdoc/>
    public String ApiMode => "openai";

    /// <inheritdoc/>
    public String BuildRequest(OllamaChatRequest req, ModelOptions model, Boolean forceStream)
        => OpenAiAdapter.BuildOpenAiRequest(req, model, forceStream);

    /// <inheritdoc/>
    public String GetRequestUrl(ProviderOptions provider, ModelOptions model)
    {
        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        return baseUrl + "/chat/completions";
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
        var sawData = false;
        String? line;
        while ((line = reader.ReadLine()) != null)
        {
            raw.Append(line).Append('\n');
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line.Substring("data:".Length).Trim();
            if (data == "[DONE]" || String.IsNullOrEmpty(data)) continue;

            sawData = true;
            // OpenAI 块本身就是 OpenAI 形状，直接转发给翻译器
            emitOpenAiChunk(data);
        }
        // 上游未用 SSE（回单 JSON）→ 把整段原文交回调用方降级处理
        return sawData ? null : raw.ToString();
    }

    /// <inheritdoc/>
    public String ConvertNonStream(String upstreamJson, ModelOptions model) => upstreamJson;
}
