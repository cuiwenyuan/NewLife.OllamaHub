using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using NewLife.Data;
using NewLife.Http;
using NewLife.Log;
using NewLife.Net;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Core;
using NewLife.OllamaHub.Diagnostics;
using NewLife.OllamaHub.Security;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Http;

/// <summary>
/// Ollama 兼容 HTTP 服务封装（基于 NewLife.Core.HttpServer）。
/// 注册 Copilot 需要的 Ollama 端点，并回调上游 OpenAI 兼容供应商完成 /api/chat。
/// 路由采用显式 MapGet/MapPost（不依赖 MVC 特性路由，最稳）。
/// </summary>
public class OllamaHttpServer
{
    private HubSettings _settings;
    private HttpServer? _server;
    private readonly UpstreamClient _upstream = new();
    private DateTime _startedAt = DateTime.UtcNow;
    private ConfigWatcher? _watcher;

    /// <summary>重建监听套接字时的重入锁，避免文件变更去抖回调与 Stop 并发。</summary>
    private readonly Object _reloadLock = new();

    /// <summary>当前实际绑定的监听地址（如 http://127.0.0.1:11434）。</summary>
    private String _boundUrl = "";

    /// <summary>对外监听地址（如 http://127.0.0.1:11434）。</summary>
    public String ListenUrl { get; private set; } = "";

    /// <summary>构造服务封装。</summary>
    /// <param name="settings">全局配置（不可为 null）。</param>
    public OllamaHttpServer(HubSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ListenUrl = _settings.Url ?? "";
    }

    /// <summary>启动 HTTP 服务并注册 Ollama 路由。</summary>
    public void Start()
    {
        // 无 settings.json 时默认 HubSettings.Url 为空，须由 host/port 推导出监听地址
        if (String.IsNullOrEmpty(_settings.Url))
            _settings.Normalize();

        // 先起监听，再挂热重载监视（避免监视器先于服务就绪触发无意义回调）
        StartListener(_settings.Url);

        // M4/M6 热重载：监视 settings.json，变更后立即重载注册表；
        // 若仅模型/供应商/密钥/聚合变更则即时生效，若监听地址（host/port）变更则重建监听套接字，
        // 二者均无需重启进程。
        _watcher = new ConfigWatcher(Path.Combine(AppContext.BaseDirectory, "settings.json"), OnConfigChanged);
        _watcher.Start();
    }

    /// <summary>在给定监听地址上创建并启动一个新的 HttpServer（绑定 Ollama 路由）。</summary>
    /// <param name="url">监听地址（如 http://127.0.0.1:11434）。</param>
    private void StartListener(String url)
    {
        var uri = new Uri(url);
        var address = ResolveBindAddress(uri.Host);

        var server = new HttpServer
        {
            ServerName = "NewLife.OllamaHub",
            Port = uri.Port,

            // 必须显式指定 Local，否则 NetServer 会绑定 0.0.0.0 + [::] 全部网卡。
            // 本进程持有付费大模型的 API Key 且不做鉴权，暴露到局域网等同于泄露密钥，
            // 因此默认只监听回环地址（与真实 Ollama 行为一致）。
            Local = new NetUri(NetType.Tcp, address, uri.Port),

            // 限定 IPv4，避免额外再创建一个 IPv6 监听套接字
            AddressFamily = AddressFamily.InterNetwork,
        };

        // 显式配置为非回环时给出告警：这是有意为之还是配置失误，用户需要知情
        if (!IPAddress.IsLoopback(address))
            XTrace.WriteLine("[安全告警] 正在监听非回环地址 {0}，本服务无鉴权且持有上游 API Key，请确保处于可信网络。", address);

        BindRoutes(server);

        // 仅当启动成功后才提交到 _server，避免半初始化的实例残留导致后续重建/停止出错
        server.Start();
        _server = server;
        _startedAt = DateTime.UtcNow;
        _boundUrl = url;
        ListenUrl = url;
        XTrace.WriteLine("NewLife.OllamaHub 已启动，监听 {0}:{1}", uri.Host, uri.Port);
    }

    /// <summary>停止并释放当前监听套接字（不释放文件监视器）。</summary>
    private void StopListener()
    {
        try { _server?.Stop("配置热重载：重建监听地址"); }
        catch (Exception ex) { XTrace.WriteException(ex); }
        _server = null;
    }

    /// <summary>把 Ollama 兼容路由注册到给定 HttpServer（初始启动与重建监听时复用，避免重复声明）。</summary>
    private void BindRoutes(HttpServer server)
    {
        server.MapGet("/", HandleRoot);
        server.MapGet("/api/version", HandleVersion);
        server.MapGet("/api/tags", HandleTags);
        server.MapGet("/api/ps", HandlePs);
        server.MapPost("/api/show", HandleShow);
        server.MapPost("/api/chat", HandleChat);
        server.MapPost("/api/generate", HandleGenerate);
        // OpenAI 兼容端点：VS/GitHub Copilot 的 "Ollama" BYO 提供商底层用 OpenAI 客户端，
        // 实际打的是 /v1/chat/completions（真实 Ollama 同样支持），此前缺失导致 404。
        server.MapPost("/v1/chat/completions", HandleOpenAiChat);
        server.MapGet("/v1/models", HandleOpenAiModels);
        server.MapGet("/api/status", HandleStatus);
        server.MapGet("/admin", HandleAdmin);
    }

    /// <summary>停止服务并释放资源（含配置监视器）。</summary>
    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        StopListener();
        XTrace.WriteLine("NewLife.OllamaHub 已停止。");
    }

    /// <summary>settings.json 变更回调：重新加载注册表，必要时重建监听套接字。</summary>
    /// <remarks>
    /// 监听地址（host/port）变更现在会<b>重建监听套接字</b>即时生效，无需重启进程；
    /// 其余配置（模型 / 供应商 / 密钥 / 聚合开关）本就即时生效。
    /// 注意 <see cref="ModelRegistry.Load()"/> 会整体替换 <c>Settings</c> 对象，因此须把
    /// <c>_settings</c> 重新指向最新实例，否则 <see cref="HandleStatus"/> 读取的 host/port/aggregate
    /// 等会停留在旧对象（修正既有隐患）。
    /// </remarks>
    private void OnConfigChanged()
    {
        lock (_reloadLock)
        {
            ModelRegistry.Instance.Load();
            var fresh = ModelRegistry.Instance.Settings;
            if (String.IsNullOrEmpty(fresh.Url))
                fresh.Normalize();
            _settings = fresh;

            var newUrl = fresh.Url;
            if (String.Equals(_boundUrl, newUrl, StringComparison.OrdinalIgnoreCase))
            {
                // 监听地址未变：模型/供应商/密钥/聚合开关已即时生效（_settings 已同步到最新实例）
                XTrace.WriteLine("[配置热重载] 已重新加载 settings.json：模型 {0} / 供应商 {1}。",
                    ModelRegistry.Instance.Models.Count, ModelRegistry.Instance.Providers.Count);
                return;
            }

            // 监听地址（host/port）变更：重建监听套接字，无需重启进程
            var oldUrl = _boundUrl;
            XTrace.WriteLine("[配置热重载] 检测到监听地址变更（{0} → {1}），正在重建监听套接字…", oldUrl, newUrl);
            StopListener();
            try
            {
                StartListener(newUrl);
                XTrace.WriteLine("[配置热重载] 监听地址已切换至 {0}，无需重启进程。", newUrl);
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
                XTrace.WriteLine("[配置热重载] 新地址 {0} 启动失败：{1}。尝试回退到原地址 {2}。", newUrl, ex.Message, oldUrl);
                try
                {
                    StartListener(oldUrl);
                    XTrace.WriteLine("[配置热重载] 已回退到原监听地址 {0}，服务继续可用。", oldUrl);
                }
                catch (Exception ex2)
                {
                    XTrace.WriteException(ex2);
                    XTrace.WriteLine("[配置热重载] 回退也失败，服务暂时停止监听，请检查端口占用或重启进程。");
                }
            }
        }
    }

    // ---- 端点处理 ----

    private void HandleRoot(IHttpContext ctx) =>
        WriteJsonNoCharset(ctx, new OllamaVersionResponse(), HttpStatusCode.OK);

    private void HandleVersion(IHttpContext ctx) =>
        WriteJsonNoCharset(ctx, new OllamaVersionResponse(), HttpStatusCode.OK);

    private void HandleTags(IHttpContext ctx)
    {
        var resp = new OllamaTagsResponse();
        foreach (var m in ModelRegistry.Instance.Models)
        {
            resp.models.Add(new OllamaTagModel
            {
                name = m.Id,
                model = m.Id,
                modified_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                details = new OllamaTagDetails { family = m.Family ?? "" },
            });
        }
        // 聚合真实 Ollama：把配置为 ollama 模式的供应商（指向本机 11434 等）的模型也并入列表
        try { AggregateOllamaTags(resp); }
        catch (Exception ex) { XTrace.WriteLine("聚合真实 Ollama 标签失败：{0}", ex.Message); }
        WriteJsonNoCharset(ctx, resp, HttpStatusCode.OK);
    }

    /// <summary>把 ApiMode=ollama 的供应商所暴露的模型并入 /api/tags（聚合能力）。失败静默跳过。</summary>
    private static void AggregateOllamaTags(OllamaTagsResponse resp)
    {
        foreach (var p in ModelRegistry.Instance.Providers.Values)
        {
            if (!String.Equals(p.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase)) continue;
            var baseUrl = (p.BaseUrl ?? "").TrimEnd('/');
            if (String.IsNullOrEmpty(baseUrl)) continue;

            try
            {
                // 短超时拉一次真实 Ollama 标签；HttpServer 处理委托是同步签名，此处同步阻塞。
                // 用专用短超时 HttpClient（不复用 UpstreamClient 的无限超时实例），避免上游挂死时拖垮 /api/tags。
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = http.GetStringAsync(baseUrl + "/api/tags").GetAwaiter().GetResult();
                var tags = JsonHelper.ToJsonEntity<OllamaTagsResponse>(json);
                if (tags?.models == null) continue;
                foreach (var t in tags.models)
                {
                    if (resp.models.Exists(x => x.name == t.name)) continue; // 不与本地模型重名
                    resp.models.Add(t);
                }
            }
            catch
            {
                // 真实 Ollama 没开 / 不可达：跳过该供应商，不阻断主流程
            }
        }
    }

    private void HandlePs(IHttpContext ctx) =>
        WriteJsonNoCharset(ctx, new OllamaPsResponse(), HttpStatusCode.OK);

    private void HandleShow(IHttpContext ctx)
    {
        try
        {
            var req = JsonHelper.ToJsonEntity<OllamaShowRequest>(ReadBody(ctx))
                      ?? throw HubException.BadRequest("请求体无法解析为 Ollama show 请求");
            if (String.IsNullOrEmpty(req.model)) throw HubException.BadRequest("缺少 model 字段");

            // 未注册模型必须 404：若返回 200 且带默认 capabilities，
            // 等于向 Copilot 谎报一个不存在的模型支持工具调用，后续调用必然失败且难以定位
            var model = ModelRegistry.Instance.GetModel(req.model)
                        ?? throw HubException.NotFound($"未知模型：{req.model}");

            var resp = new OllamaShowResponse
            {
                details = new OllamaTagDetails { family = model.Family ?? "" },
            };

            // 能力声明：缺 tools 会导致 Copilot Agent/工具调用整体不可用（竞品坑 #2）
            var caps = new List<String> { "completion" };
            if (model.Tools) caps.Add("tools");
            if (model.Vision) caps.Add("vision");
            if (model.Thinking) caps.Add("thinking");
            resp.capabilities = caps.ToArray();

            // model_info：Copilot 据此判断上下文长度与截断（竞品坑 #3）
            var family = (model.Family ?? model.Id).ToLowerInvariant();
            resp.model_info = new Dictionary<String, Object>
            {
                [$"{family}.context_length"] = model.ContextLength,
                [$"{family}.architecture"] = family,
                ["family"] = family,
                ["parameter_size"] = "unknown",
                ["quantization_level"] = "unknown",
            };

            WriteJsonNoCharset(ctx, resp, HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            WriteException(ctx, ex);
        }
    }

    private void HandleChat(IHttpContext ctx)
    {
        String? modelId = null;
        try
        {
            var rawBody = ReadBody(ctx);
            var req = JsonHelper.ToJsonEntity<OllamaChatRequest>(rawBody)
                      ?? throw HubException.BadRequest("请求体无法解析为 Ollama chat 请求");
            var (model, provider) = ResolveRoute(req.model);
            modelId = model.Id;

            // 真实 Ollama 透传：原样转发请求、原样中继响应（NDJSON），实现聚合/兜底
            if (String.Equals(provider.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                var upstream = CallUpstreamRaw(provider, rawBody);
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                WriteRaw(ctx, upstream, HttpStatusCode.OK);
                return;
            }

            // 统一向上游请求 stream:true，再由翻译器累积成 Ollama NDJSON：
            //   - 上游正常回 SSE → 逐块累积（done:false 帧 + 末帧 done:true）；
            //   - 上游忽略 stream 仍回 SSE（部分国内网关如此）→ 同样被正确处理；
            //   - 极少数上游回单 JSON（fallback）→ 降级走 M1 非流式单帧转换。
            // 关键点：Copilot 要的是 Ollama 帧，是否逐帧下发由 req.stream 决定，但上游一律按 SSE 读，
            // 从而彻底规避"上游忽略 stream:false 仍回 SSE"导致整段 SSE 被当 JSON 解析而 502 的坑。
            var adapter = UpstreamAdapterFactory.Get(provider.ApiMode);
            var oaReq = adapter.BuildRequest(req, model, forceStream: true);
            var acc = new OllamaStreamTranslator(model, forGenerate: false);
            var frames = new StringBuilder();
            var fallback = CallUpstreamStream(provider, model, oaReq, chunk => frames.Append(acc.Consume(chunk)).Append('\n'));
            if (fallback != null)
            {
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                var oaLike = adapter.ConvertNonStream(fallback, model);
                WriteRaw(ctx, OpenAiAdapter.ToOllamaNdJson(oaLike, model), HttpStatusCode.OK);
                return;
            }

            if (req.stream)
            {
                frames.Append(acc.Finalize()).Append('\n');
                WriteRaw(ctx, frames.ToString(), HttpStatusCode.OK);
            }
            else
            {
                // 非流式：只回汇总末帧（含完整 content 与 usage），丢弃中间 done:false 帧
                WriteRaw(ctx, acc.Finalize(), HttpStatusCode.OK);
            }
            var usage = acc.Usage;
            UsageStats.Instance.RecordSuccess(model.Id, usage.Prompt, usage.Completion);
        }
        catch (Exception ex)
        {
            if (modelId != null) UsageStats.Instance.RecordError(modelId, ex.Message);
            WriteException(ctx, ex);
        }
    }

    private void HandleGenerate(IHttpContext ctx)
    {
        String? modelId = null;
        try
        {
            var greq = JsonHelper.ToJsonEntity<OllamaGenerateRequest>(ReadBody(ctx))
                       ?? throw HubException.BadRequest("请求体无法解析为 Ollama generate 请求");
            var (model, provider) = ResolveRoute(greq.model);
            modelId = model.Id;

            // generate 语义上是单轮补全，转写为单条 user 消息复用 chat 链路
            var chat = new OllamaChatRequest { model = greq.model, stream = greq.stream, options = greq.options };
            chat.messages.Add(new OllamaMessage { role = "user", content = greq.prompt });

            // 真实 Ollama 透传
            if (String.Equals(provider.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                var upstream = CallUpstreamRaw(provider, ReadBody(ctx));
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                WriteRaw(ctx, upstream, HttpStatusCode.OK);
                return;
            }

            var adapter = UpstreamAdapterFactory.Get(provider.ApiMode);
            var oaReq = adapter.BuildRequest(chat, model, forceStream: true);
            var acc = new OllamaStreamTranslator(model, forGenerate: true);
            var frames = new StringBuilder();
            var fallback = CallUpstreamStream(provider, model, oaReq, chunk => frames.Append(acc.Consume(chunk)).Append('\n'));
            if (fallback != null)
            {
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                var oaLike = adapter.ConvertNonStream(fallback, model);
                WriteRaw(ctx, OpenAiAdapter.ToOllamaGenerateNdJson(oaLike, model), HttpStatusCode.OK);
                return;
            }

            if (chat.stream)
            {
                frames.Append(acc.Finalize()).Append('\n');
                WriteRaw(ctx, frames.ToString(), HttpStatusCode.OK);
            }
            else
            {
                WriteRaw(ctx, acc.Finalize(), HttpStatusCode.OK);
            }
            var usage = acc.Usage;
            UsageStats.Instance.RecordSuccess(model.Id, usage.Prompt, usage.Completion);
        }
        catch (Exception ex)
        {
            if (modelId != null) UsageStats.Instance.RecordError(modelId, ex.Message);
            WriteException(ctx, ex);
        }
    }

    // ---- OpenAI 兼容端点（VS Copilot "Ollama" BYO 提供商底层用 OpenAI 客户端实际调用） ----

    private void HandleOpenAiChat(IHttpContext ctx)
    {
        String? modelId = null;
        try
        {
            var rawBody = ReadBody(ctx);
            var req = JsonHelper.ToJsonEntity<OpenAiChatRequest>(rawBody)
                      ?? throw HubException.BadRequest("请求体无法解析为 OpenAI chat 请求");
            var (model, provider) = ResolveRoute(req.model);
            modelId = model.Id;

            // 真实 Ollama 透传：原样转发到其原生 /v1/chat/completions（真实 Ollama 同样支持该 OpenAI 兼容端点）
            if (String.Equals(provider.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
                var upstream = _upstream.RelayAsync(provider, "/v1/chat/completions", rawBody, cts.Token)
                                      .GetAwaiter().GetResult();
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                WriteSse(ctx, upstream);
                return;
            }

            // 统一转换为 Ollama 请求形状，复用既有的适配 / 路由 / 流式逻辑
            var ollamaReq = ToOllamaRequest(req);
            var adapter = UpstreamAdapterFactory.Get(provider.ApiMode);
            var oaReq = adapter.BuildRequest(ollamaReq, model, forceStream: true);

            var sse = new StringBuilder();
            var acc = new OpenAiAccumulator(model);
            var fallback = CallUpstreamStream(provider, model, oaReq, chunk =>
            {
                // 各适配器已将上游响应归一化为 OpenAI 形状 SSE 块，直接中继给 OpenAI 客户端即可
                if (req.stream)
                    sse.Append("data: ").Append(chunk).Append("\n\n");
                else
                    acc.Consume(chunk);
            });

            if (fallback != null)
            {
                // 上游未用 SSE（返回单 JSON）：该 JSON 本身即合法 OpenAI 非流式响应，原样返回
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                WriteRaw(ctx, fallback, HttpStatusCode.OK);
                return;
            }

            if (req.stream)
            {
                sse.Append("data: [DONE]\n\n");
                WriteSse(ctx, sse.ToString());
            }
            else
            {
                // 非流式：把累积的 OpenAI 块聚合成单个响应 JSON
                var single = acc.BuildSingle();
                UsageStats.Instance.RecordSuccess(model.Id, 0, 0);
                WriteRaw(ctx, single, HttpStatusCode.OK);
            }
        }
        catch (Exception ex)
        {
            if (modelId != null) UsageStats.Instance.RecordError(modelId, ex.Message);
            WriteException(ctx, ex);
        }
    }

    private void HandleOpenAiModels(IHttpContext ctx)
    {
        var data = new List<Object>();
        foreach (var m in ModelRegistry.Instance.Models)
        {
            data.Add(new Dictionary<String, Object?>
            {
                ["id"] = m.Id,
                ["object"] = "model",
                ["owned_by"] = m.OwnedBy ?? m.Family ?? "ollamahub",
                ["created"] = 0,
            });
        }
        var resp = new Dictionary<String, Object?> { ["object"] = "list", ["data"] = data.ToArray() };
        WriteJsonNoCharset(ctx, resp, HttpStatusCode.OK);
    }

    /// <summary>把 OpenAI chat 请求转换为 Ollama chat 请求形状（供适配器构造上游请求）。</summary>
    private static OllamaChatRequest ToOllamaRequest(OpenAiChatRequest req)
    {
        var ollama = new OllamaChatRequest { model = req.model, stream = req.stream };
        foreach (var m in req.messages)
        {
            ollama.messages.Add(new OllamaMessage
            {
                role = m.role,
                content = m.content,
                tool_calls = m.tool_calls,
            });
        }
        var opts = new Dictionary<String, Object>();
        if (req.temperature != null) opts["temperature"] = req.temperature.Value;
        if (req.top_p != null) opts["top_p"] = req.top_p.Value;
        if (req.max_tokens != null) opts["max_tokens"] = req.max_tokens.Value;
        if (opts.Count > 0) ollama.options = opts;
        ollama.tools = req.tools;
        ollama.tool_choice = req.tool_choice;
        return ollama;
    }

    /// <summary>以 text/event-stream 写出 SSE 响应体（OpenAI 兼容流式）。</summary>
    private static void WriteSse(IHttpContext ctx, String text)
    {
        ctx.Response.StatusCode = HttpStatusCode.OK;
        ctx.Response.ContentType = "text/event-stream";
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.Body = new ArrayPacket(bytes, 0, bytes.Length);
    }

    private void HandleStatus(IHttpContext ctx)
    {
        var snapshot = UsageStats.Instance.Snapshot();
        var models = new List<Object>();
        foreach (var m in ModelRegistry.Instance.Models)
        {
            snapshot.TryGetValue(m.Id, out var st);
            models.Add(new Dictionary<String, Object>
            {
                ["id"] = m.Id,
                ["displayName"] = m.DisplayName ?? m.Id,
                ["family"] = m.Family ?? "",
                ["provider"] = m.Provider ?? m.OwnedBy ?? "",
                ["tools"] = m.Tools,
                ["vision"] = m.Vision,
                ["thinking"] = m.Thinking,
                ["requests"] = st?.Requests ?? 0,
                ["errors"] = st?.Errors ?? 0,
                ["promptTokens"] = st?.PromptTokens ?? 0,
                ["completionTokens"] = st?.CompletionTokens ?? 0,
                ["lastError"] = st?.LastError ?? "",
            });
        }

        var providers = new List<Object>();
        foreach (var p in ModelRegistry.Instance.Providers.Values)
        {
            var hasKey = !String.IsNullOrEmpty(SecretProtector.Resolve(p));
            providers.Add(new Dictionary<String, Object>
            {
                ["id"] = p.Id,
                ["name"] = p.Name ?? p.Id,
                ["apiMode"] = p.ApiMode,
                ["baseUrl"] = p.BaseUrl,
                ["hasKey"] = hasKey,
            });
        }

        var status = new Dictionary<String, Object>
        {
            ["name"] = "NewLife.OllamaHub",
            ["version"] = typeof(OllamaHttpServer).Assembly.GetName().Version?.ToString() ?? "?",
            ["uptimeSeconds"] = (Int64)(DateTime.UtcNow - _startedAt).TotalSeconds,
            ["listenUrl"] = _settings.Url,
            ["aggregateLocalOllama"] = _settings.AggregateLocalOllama,
            ["models"] = models.ToArray(),
            ["providers"] = providers.ToArray(),
            ["totalRequests"] = snapshot.Values.Sum(e => e.Requests),
            ["totalErrors"] = snapshot.Values.Sum(e => e.Errors),
        };
        WriteJsonNoCharset(ctx, status, HttpStatusCode.OK);
    }

    private void HandleAdmin(IHttpContext ctx)
    {
        var html = AdminPanel.Html;
        ctx.Response.StatusCode = HttpStatusCode.OK;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.Body = new ArrayPacket(bytes, 0, bytes.Length);
    }

    // ---- 内部工具 ----

    /// <summary>
    /// 把配置 URL 中的主机名解析为监听 IP。
    /// localhost / 空 → 127.0.0.1；0.0.0.0 / + / * → 全网卡；其余按 IP 字面量解析。
    /// </summary>
    /// <param name="host">配置 URL 的主机部分。</param>
    /// <returns>用于绑定的 IP 地址。</returns>
    private static IPAddress ResolveBindAddress(String host)
    {
        if (String.IsNullOrEmpty(host)) return IPAddress.Loopback;

        if (String.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
        if (host == "0.0.0.0" || host == "+" || host == "*") return IPAddress.Any;

        // 非法主机名不静默降级为全网卡（那样会意外放开访问面），而是回落到最安全的回环
        return IPAddress.TryParse(host, out var ip) ? ip : IPAddress.Loopback;
    }

    /// <summary>按模型名解析出模型与其归属供应商，失败时抛出带状态码的异常。</summary>
    private static (ModelOptions Model, ProviderOptions Provider) ResolveRoute(String modelId)
    {
        if (String.IsNullOrEmpty(modelId)) throw HubException.BadRequest("缺少 model 字段");

        var model = ModelRegistry.Instance.GetModel(modelId)
                    ?? throw HubException.NotFound($"未知模型：{modelId}");
        var provider = ModelRegistry.Instance.GetProvider(model)
                       ?? throw HubException.NotFound($"模型 {modelId} 找不到归属供应商");

        return (model, provider);
    }

    /// <summary>流式：增量读上游 SSE 并逐块回调；返回 null 表示已按 SSE 发帧，否则返回未用 SSE 的原始响应体（降级用）。</summary>
    private String? CallUpstreamStream(ProviderOptions provider, ModelOptions model, String oaReqJson, Action<String> onChunk)
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
        return _upstream.StreamChatAsync(provider, model, oaReqJson, onChunk, cts.Token).GetAwaiter().GetResult();
    }

    /// <summary>真实 Ollama 透传：原样把请求体转发给上游 /api/chat，返回其原始响应文本（NDJSON）供中继。</summary>
    private String CallUpstreamRaw(ProviderOptions provider, String rawBody)
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
        return _upstream.ChatAsync(provider, new ModelOptions(), rawBody, cts.Token).GetAwaiter().GetResult();
    }

    /// <summary>按异常类型映射 HTTP 状态码并写出错误响应。</summary>
    private static void WriteException(IHttpContext ctx, Exception ex)
    {
        // HubException 自带语义状态码；其余未预期异常统一 500
        var status = ex is HubException he ? he.StatusCode : HttpStatusCode.InternalServerError;

        // 4xx 属客户端可预期错误（Copilot 会主动探测未注册模型），只记一行避免刷屏；
        // 5xx 才是需要排查的服务端/上游故障，保留完整堆栈
        if ((Int32)status is >= 400 and < 500)
            XTrace.WriteLine("[{0}] {1}", (Int32)status, ex.Message);
        else
            XTrace.WriteException(ex);

        WriteError(ctx, status, ex.Message);
    }

    /// <summary>读取请求体文本（UTF-8）。</summary>
    private static String ReadBody(IHttpContext ctx)
    {
        var body = ctx.Request.Body;
        if (body == null || body.Length == 0) return "";
        if (body.TryGetArray(out var seg) && seg.Array != null)
            return Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Count);
        return Encoding.UTF8.GetString(body.GetSpan());
    }

    /// <summary>以 application/json 写入原始文本响应体。</summary>
    private static void WriteRaw(IHttpContext ctx, String text, HttpStatusCode status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.Body = new ArrayPacket(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// 以 application/json（不含 charset）写出 JSON 对象。
    /// 注意：不能用 HttpContextExtensions.WriteJson——它会附加 "; charset=utf-8"。
    /// Copilot 的 Ollama 客户端对带 charset 的 Content-Type 可能直接返回 415，
    /// 因此所有端点统一发干净的 application/json（与真实 Ollama 一致）。
    /// </summary>
    private static void WriteJsonNoCharset(IHttpContext ctx, Object obj, HttpStatusCode status)
    {
        WriteRaw(ctx, JsonHelper.ToJson(obj), status);
    }

    /// <summary>写入 Ollama 风格错误响应（{"error":"..."}）。</summary>
    private static void WriteError(IHttpContext ctx, HttpStatusCode status, String message)
    {
        WriteRaw(ctx, JsonHelper.ToJson(new OllamaErrorResponse { error = message }), status);
    }
}
