# NewLife.OllamaHub — 项目长期记忆

## 硬约束
- 仅引用 NewLife 生态 NuGet：`NewLife.Core` + `NewLife.Agent`(10.17.2026.801)；无 ASP.NET Core、无第三方 NuGet。
- net8.0；`ImplicitUsings` 开（泛型集合免 using，非泛型 `IList`/`IDictionary` 写全称）。
- 服务化用 `NewLife.Agent.ServiceBase` 派生，不用 `Microsoft.Extensions.Hosting`。
- 服务名 `ServiceName = "NewLife.OllamaHub"`（含点）；旧 `NewLifeOllamaHub`（无点）已弃用。

## 关键架构
- HttpServer 严格单发：处理委托返回后框架必发尾随 200 → 永远"缓冲累积 → 一次性 `ctx.Response.Body = new ArrayPacket(bytes)`"，勿手动分块。
- 流式桥接：`HandleChat`/`HandleGenerate` 一律上游 `forceStream:true`，统一经 `OllamaStreamTranslator` 翻 Ollama 帧。**已对齐真实 Ollama 协议（2026-08-07 合并 PR #2 增量方案修复）**：流式每帧下发【增量 delta】，`done:true` 末帧 `Finalize(false)` 仅发结束信号（content 空）；非流式 `Finalize()` 保留完整内容。此前"累积完整内容下发"违反协议 → CherryStudio/OpenWebUI 重复输出（已修）。保留原因：部分网关忽略 `stream:false` 仍回 SSE，统一 SSE 规避 502。VS Copilot 走 `/v1/chat/completions` 直接中继 OpenAI 增量 SSE，不经此累积逻辑，不受影响。
- 统一适配层 `Core/IUpstreamAdapter` + `UpstreamAdapterFactory`(openai/anthropic/gemini/google/responses，未知回落 openai)。Anthropic=`x-api-key`+`anthropic-version`、Gemini=key 拼 `?alt=sse&key=`、Responses=`/v1/responses`（input items + max_output_tokens + reasoning.effort）；多模态 images→ OpenAI image_url / Anthropic source.base64 / Gemini inline_data。
- 工具 `ToolSchemaSanitizer.Sanitize` 递归删 `$schema`/`definitions`/`$defs`/`title`/`examples`/`additionalProperties`/`x-*`。
- HTTP 端点：Ollama 原生 `/api/tags`/`/api/chat`/`/api/generate` + **必须**保留 `POST /v1/chat/completions`、`GET /v1/models`（VS "Ollama" BYO 是 OpenAI 客户端，只打 `/v1/*`，缺失裸 404）。ollama 模式走 `UpstreamClient.RelayAsync`。

## 配置与双监听（2026-08-06，不向后兼容）
- `HubSettings` 两独立节点：`Local`（明文 HTTP，默认 enabled/127.0.0.1/11434）+ `LanHttps`（TLS，默认 disabled/0.0.0.0/11435/`certificate`(PFX)/`certPassword`）。旧顶层 `url`/`host`/`port`/`httpsPort`/`certificate`/`certPassword` **已删，不再兼容**。`Normalize()` 归一化两子对象派生 `Local.Url`；`ProviderPresets` 默认构造改无参 `new HubSettings()`。
- `OllamaHttpServer` 两 `HttpServer`：`_localServer`(Local.Url) + `_httpsServer`(LanHttps，证书缺失告警跳过)；`StartListenerInner` 按 `UseTls` 选 HTTP/HTTPS；`HandleStatus` 输出 `listeners[]`(name/scheme/enabled/url/host/port/bound)，无单一 `listenUrl`/`httpsPort`。
- 热重载：`ConfigWatcher` 监视 settings.json(500ms 去抖)→`ModelRegistry.Instance.Load()` 整体替换 Settings（非原地）。监听变更经 `ReconcileListeners()` 逐监听对账 enabled/地址/证书，失败回退，无需重启。
- `SecretProtector` 实为 BCL AES-256-CBC 机器绑定（盐 `NewLife.OllamaHub::v1::`+MachineName），非 DPAPI；优先级 明文>`env:NAME`>`dpapi:<base64>`>明文。
- 证书须被 VS 信任（自签导入受信任根）；Hub 无鉴权，暴露局域网=Key 暴露，**仅限可信网络/VPN**。文档见 `docs/configuration.md`「本地与局域网监听（双节点）」+ `docs/faq.md`。

## 供应商预设与模型名
- `ProviderPresets.cs` 内置 11 家：9 OpenAI 兼容(deepseek/qwen/kimi/glm/siliconflow/volcengine/hunyuan/modelscope/openrouter)+anthropic+gemini。
- 模型 Id 漂移（2026-08-04 DeepSeek 弃用 `deepseek-chat`/`reasoner`→全升 2026-08 现行名）。**新增/核对预设务必联网核实官方当前模型名，禁凭记忆填旧名**。siliconflow/modelscope/openrouter 未改。

## 可视化菜单（p/k/c）
- `HubAgentService` 三嵌套子类继承 `BaseCommandHandler`(PresetMenuCommand=p/ApiKeyMenuCommand=k/WizardMenuCommand=c)，`ServiceBase.Command` 自动扫描注册——取代过时 `AddMenu`。

## 构建 / 发布 / 测试
- 构建 `dotnet build -c Release`；发布 `dotnet publish -c Release -r win-x64 -o <dir>`（单文件）。
- 服务实例锁（2026-08-06）：发布目标被 Windows 服务锁住（Session 0，普通 `Stop-Process`/`taskkill` 拒绝访问，`sc`/`net` 沙箱禁用）→ 须用户本机管理员 `Stop-Service NewLife.OllamaHub` 后重发。
- 服务安装无法在沙箱完成（`exe -i` 非管理员提权崩溃）→ 用户本机右键 exe→管理员→菜单 2。
- 验证修复编入单文件：菜单用 `ReadKey`（stdin 重定向抛异常）→ 用 Python UTF-16LE(`s.encode("utf-16-le")`) 在 exe/dll 检索中文文案。
- 前台 `--serve`（双横杠，stdin 重定向下可靠）；`-run` 抛 `ReadKey` 异常。
- self-test `.exe self-test` 零框架，退出码=失败数，当前 204/0 全绿（2026-08-07 +3 Responses 适配器测试 + 增量流式断言重写）。
- exe 版本号=构建日期+时间(csproj：`AssemblyVersion=1.0.<距2000-01-01天数>.<自午夜半秒数>`、`FileVersion=1.0.<YYYY>.<MMDD>`，用户 2026-08-05 明确要求，勿改固定)。
- 端口：hub 127.0.0.1:11434；mock openai :9099；fake ollama :11435。

## GitHub Release / 推送
- 推送 `v*` tag（如 `git tag v1.0.0 && git push origin v1.0.0`）→ `.github/workflows/build.yml` 自动 build+publish+`softprops/action-gh-release@v2`，用内置 GITHUB_TOKEN，无需 PAT。CI 仅覆盖 `Version=tag`，文件版本仍走日期+时间。
- `github.com:443` 间歇 SNI 阻断：先试常规 `git push origin main`；阻断复现（`curl --max-time 10 https://github.com` 超时）走 `api.github.com` Git Data API（`git credential fill` 取 Fine-grained PAT；用 `curl` 子进程、`--data @file` 写临时文件避 WinError 206）。PAT 已存 credential store(username=cuiwenyuan)。

## 战略决策：不引入 NewLife.AI（2026-08-05）
- 不引入 NewLife.AI（核心库与 ChatAI 网关均不引入）；继续自维护手写适配+Ollama 兼容端点。再评估：① 原生提供 Ollama 兼容服务端；② 非 ASP.NET Core 轻量网关；③ 手写维护成本陡增。

## 文档索引
- `docs/`：README/architecture/configuration/providers/security/install-as-service/vs-setup/upgrade/troubleshooting/faq/competitor-analysis/newlife-ai-evaluation.md。
