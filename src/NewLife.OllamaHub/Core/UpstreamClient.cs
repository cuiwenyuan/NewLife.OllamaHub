using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Security;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 上游调用客户端（M1 实现 OpenAI 兼容模式 + 基础 Ollama 透传）。
/// 使用 BCL System.Net.Http.HttpClient + SocketsHttpHandler（框架自带，非第三方 NuGet）。
/// 关键点：Content-Type 只设一次且**不带 charset**，避免 DeepSeek 等返回 415（竞品已知坑 #1）。
/// </summary>
public class UpstreamClient
{
    // 复用单例 HttpClient，避免频繁建连；长请求靠 CancellationToken 控制超时
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    })
    {
        // 无限超时：由调用方传入的 CancellationToken 兜底（首字节/总时长）
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>向上游发起聊天补全请求，返回原始响应文本。</summary>
    /// <param name="provider">供应商配置（提供 BaseUrl / ApiMode / Key）。</param>
    /// <param name="model">模型配置（用于解析上游模型名）。</param>
    /// <param name="requestBody">已转换好的上游请求体（OpenAI JSON 或原样 Ollama JSON）。</param>
    /// <param name="cancellationToken">取消令牌（建议带 5 分钟超时）。</param>
    /// <returns>上游响应文本（调用方负责解析）。</returns>
    public virtual async Task<String> ChatAsync(ProviderOptions provider, ModelOptions model, String requestBody, CancellationToken cancellationToken)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (String.IsNullOrEmpty(requestBody)) throw new ArgumentException("请求体为空", nameof(requestBody));

        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        if (String.IsNullOrEmpty(baseUrl))
            throw HubException.BadGateway($"供应商 {provider.Id} 未配置 BaseUrl");

        // Ollama 透传模式：直接转发 /api/chat；否则走 OpenAI /chat/completions
        var isOllama = String.Equals(provider.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase);
        var url = isOllama ? baseUrl + "/api/chat" : baseUrl + "/chat/completions";

        var apiKey = SecretProtector.Resolve(provider);
        if (!isOllama && String.IsNullOrEmpty(apiKey))
            throw HubException.BadGateway($"供应商 {provider.Id} 未配置 API Key（请在 settings.json 填 apiKey，或用 env:NAME / dpapi: 形式）");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!isOllama)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 关键：手动设置 Content-Type=application/json 且不带 charset（StringContent 会默认追加 ; charset=utf-8 触发 415）
        req.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        req.Content.Headers.Remove("Content-Type");
        req.Content.Headers.Add("Content-Type", "application/json");

        // 供应商级自定义头（跳过已被接管的 Content-Type / Authorization）
        foreach (var h in provider.Headers)
        {
            if (String.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (String.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        XTrace.WriteLine("→ 上游 {0} {1}", provider.Id, url);

        HttpResponseMessage resp;
        String body;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 非调用方主动取消 → 视为上游超时
            throw HubException.BadGateway($"上游 {provider.Id} 请求超时");
        }
        catch (HttpRequestException ex)
        {
            // 网络不可达 / DNS / TLS 失败等
            throw HubException.BadGateway($"上游 {provider.Id} 连接失败：{ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var code = (Int32)resp.StatusCode;
                // 注意：此处必须用编号占位符（{0}/{1}/{2}），XTrace 走 String.Format，
                // 写成 {(Int32)resp.StatusCode} 会抛 FormatException 并掩盖真正的上游错误
                XTrace.WriteLine("← 上游 {0} 返回 {1}：{2}", provider.Id, code, body);
                throw HubException.BadGateway($"上游 {provider.Id} 返回 {code}：{body}");
            }
            return body;
        }
    }

    /// <summary>
    /// 向上游发起流式聊天补全（stream:true），增量读取 SSE，逐块回调。
    /// 用于 M2 流式桥接：上游每个 data: 块经 OllamaStreamTranslator 翻译成 Ollama NDJSON 帧。
    /// </summary>
    /// <param name="provider">供应商配置。</param>
    /// <param name="model">模型配置。</param>
    /// <param name="requestBody">已转换好的上游请求体（应含 stream:true）。</param>
    /// <param name="onChunk">每解析到一个 SSE data: 块 JSON 时回调（已去除 "data:" 前缀）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 若上游确实以 SSE（data: 行）返回则返回 null（帧已通过 onChunk 发出）；
    /// 若上游未使用 SSE（直接返回单 JSON），则返回该原始响应体，调用方应降级为非流式处理。
    /// </returns>
    public virtual async Task<String?> StreamChatAsync(ProviderOptions provider, ModelOptions model, String requestBody, Action<String> onChunk, CancellationToken cancellationToken)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (onChunk == null) throw new ArgumentNullException(nameof(onChunk));
        if (String.IsNullOrEmpty(requestBody)) throw new ArgumentException("请求体为空", nameof(requestBody));

        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        if (String.IsNullOrEmpty(baseUrl))
            throw HubException.BadGateway($"供应商 {provider.Id} 未配置 BaseUrl");

        // ollama 透传由调用方走 ChatAsync；此处只服务 openai/anthropic/gemini，按 ApiMode 取适配器
        var isOllama = String.Equals(provider.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase);
        var adapter = isOllama ? null : UpstreamAdapterFactory.Get(provider.ApiMode);

        var url = isOllama ? baseUrl + "/api/chat" : adapter!.GetRequestUrl(provider, model);

        var apiKey = SecretProtector.Resolve(provider);
        if (!isOllama && String.IsNullOrEmpty(apiKey))
            throw HubException.BadGateway($"供应商 {provider.Id} 未配置 API Key（请在 settings.json 填 apiKey，或用 env:NAME / dpapi: 形式）");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!isOllama)
            adapter!.ApplyAuth(req, provider, apiKey);

        // 手动设置 Content-Type=application/json 且不带 charset（避免上游 415）
        req.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        req.Content.Headers.Remove("Content-Type");
        req.Content.Headers.Add("Content-Type", "application/json");

        foreach (var h in provider.Headers)
        {
            if (String.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (String.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        XTrace.WriteLine("→ 上游 {0} {1} (stream, mode={2})", provider.Id, url, provider.ApiMode);

        HttpResponseMessage resp;
        try
        {
            // ResponseHeadersRead：拿到响应头即返回，之后逐块读 body，实现真正的增量读取
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw HubException.BadGateway($"上游 {provider.Id} 请求超时");
        }
        catch (HttpRequestException ex)
        {
            throw HubException.BadGateway($"上游 {provider.Id} 连接失败：{ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                // 流式请求的失败响应通常仍是完整 JSON，读出来原样报错
                var errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var code = (Int32)resp.StatusCode;
                XTrace.WriteLine("← 上游 {0} 返回 {1}：{2}", provider.Id, code, errBody);
                throw HubException.BadGateway($"上游 {provider.Id} 返回 {code}：{errBody}");
            }

            // 逐块读取上游响应，由各适配器把自家 SSE 块翻译成 OpenAI 形状喂给 onChunk（翻译器消费）。
            // 返回 null 表示已按 SSE 发帧；返回非 null 表示上游回单 JSON，由调用方降级为非流式。
            return await Task.Run(() => adapter!.ReadStream(resp, onChunk, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 原样把请求体转发到上游的指定相对路径并取回完整响应体（用于 ollama 模式供应商的 /v1/chat/completions 透传）。
    /// 真实 Ollama 原生支持 OpenAI 兼容的 /v1/chat/completions，本代理仅做透明中继，不做协议转换。
    /// </summary>
    /// <param name="provider">供应商配置（提供 BaseUrl）。</param>
    /// <param name="relativePath">上游相对路径，如 /v1/chat/completions。</param>
    /// <param name="requestBody">原始请求体（OpenAI JSON）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>上游完整响应文本（调用方负责原样中继）。</returns>
    public virtual async Task<String> RelayAsync(ProviderOptions provider, String relativePath, String requestBody, CancellationToken cancellationToken)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (String.IsNullOrEmpty(requestBody)) throw new ArgumentException("请求体为空", nameof(requestBody));

        var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
        if (String.IsNullOrEmpty(baseUrl))
            throw HubException.BadGateway($"供应商 {provider.Id} 未配置 BaseUrl");

        var url = baseUrl + relativePath;
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        req.Content.Headers.Remove("Content-Type");
        req.Content.Headers.Add("Content-Type", "application/json");

        foreach (var h in provider.Headers)
        {
            if (String.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (String.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        XTrace.WriteLine("→ 上游(透传) {0} {1}", provider.Id, url);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw HubException.BadGateway($"上游 {provider.Id} 请求超时");
        }
        catch (HttpRequestException ex)
        {
            throw HubException.BadGateway($"上游 {provider.Id} 连接失败：{ex.Message}", ex);
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var code = (Int32)resp.StatusCode;
                XTrace.WriteLine("← 上游(透传) {0} 返回 {1}：{2}", provider.Id, code, body);
                throw HubException.BadGateway($"上游 {provider.Id} 返回 {code}：{body}");
            }
            return body;
        }
    }
}
