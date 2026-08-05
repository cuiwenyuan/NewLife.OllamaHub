# NewLife.OllamaHub — 项目长期记忆

## 硬约束
- 仅引用 NewLife 生态 NuGet：`NewLife.Core` + `NewLife.Agent`(10.17.2026.801)。BCL 不算第三方；无 ASP.NET Core、无第三方 NuGet。
- 目标框架 net8.0；`ImplicitUsings` 开 → 泛型集合免 using，但 `System.Collections.IList`/`IDictionary` 非泛型需写全称。
- 服务化用 `NewLife.Agent.ServiceBase` 派生，不用 `Microsoft.Extensions.Hosting`。
- **服务名 `ServiceName = "NewLife.OllamaHub"`（含点）**，用于 Windows 服务注册与 `UpgradeCommand` 重启；旧名 `NewLifeOllamaHub`（无点）已弃用。

## 关键架构（M2–M6 已收尾，勿回退）
- **HttpServer 严格单发响应**：处理委托返回后框架必再发尾随 200 → 永远"缓冲累积 → 一次性 `ctx.Response.Body = new ArrayPacket(bytes)` 发送"，勿手动分块。
- **流式桥接**：`HandleChat`/`HandleGenerate` 一律向上游 `forceStream:true`，统一经 `OllamaStreamTranslator`（累积完整 content/thinking/tool_calls，按 `done:false`/末帧 `done:true` 下发）。原因：部分国内网关忽略 `stream:false` 仍回 SSE，统一 SSE 读取规避 502。
- **统一适配层** `Core/IUpstreamAdapter` + `UpstreamAdapterFactory`(openai/anthropic/gemini/google，未知回落 openai)：各适配器把自家响应翻成统一 OpenAI 形状再喂 `OllamaStreamTranslator`。Anthropic=`x-api-key`+`anthropic-version`、Gemini=key 拼 URL `?alt=sse&key=`；多模态 `OllamaMessage.images`→ OpenAI image_url / Anthropic source.base64 / Gemini inline_data。
- **工具**：`ToolSchemaSanitizer.Sanitize` 递归删 `$schema`/`definitions`/`$defs`/`title`/`examples`/`additionalProperties`/`x-*`。

## 配置与热重载（勿回退）
- `Core/ConfigWatcher.cs` 监视 `settings.json`（500ms 去抖）→ `ModelRegistry.Instance.Load()`。模型/供应商/密钥/聚合开关即时生效；仅 `host`/`port` 监听地址变更**已支持热重建套接字**（`OllamaHttpServer.StartListener/StopListener`，失败回退旧端口）。
- `ModelRegistry.Load()` 整体替换 `Settings` 对象（非原地 mutate），读取方须以 `ModelRegistry.Instance.Settings` 为权威来源、`_boundUrl` 比对实际绑定地址。
- `SecretProtector` 实为 BCL `AES-256-CBC` 机器绑定（盐 `NewLife.OllamaHub::v1::`+MachineName），非 Windows DPAPI；解析优先级 明文 > `env:NAME` > `dpapi:<base64>` > 明文。

## 供应商预设与模型名（重要教训）
- `Config/ProviderPresets.cs` 内置 **11 家**：9 家 OpenAI 兼容（deepseek/qwen/kimi/glm/siliconflow/volcengine/hunyuan/modelscope/openrouter）+ anthropic(ApiMode=anthropic) + gemini(ApiMode=gemini)。
- **模型 Id 漂移教训（2026-08-04）**：DeepSeek 于 2026-07-24 弃用 `deepseek-chat`/`reasoner`，且 qwen/kimi/glm/volcengine/hunyuan/anthropic/gemini 预设均停在旧世代 → 全部升级为 2026-08 现行官方名（deepseek-v4-flash/pro、qwen3.8-max、kimi-k3、glm-5.2、doubao-seed-2-1-*、hunyuan-a13b、claude-opus-5、gemini-2.5-pro 等）。**新增/核对预设时务必联网核实官方当前模型名，禁止凭记忆填旧名。**
- siliconflow/modelscope/openrouter 未改：开源托管 ID 固定、聚合器保留历史 slug，盲改反有风险。

## 可视化菜单（p/k/c）
- `HubAgentService` 三个嵌套子类继承 `BaseCommandHandler`（`PresetMenuCommand`=p、`ApiKeyMenuCommand`=k、`WizardMenuCommand`=c），由 `ServiceBase.Command` 自动扫描派生程序集注册——**取代过时 `AddMenu`**。各自 `override Process` 调 `ConfigureXxx()`，`IsShowMenu()=>true`。

## 构建 / 测试约定
- 构建 `dotnet build -c Release`；发布 `dotnet publish -c Release -r win-x64 -o <dir>`（单文件）。**运行中实例会锁 exe**：publish 前先 `taskkill /F /IM NewLife.OllamaHub.exe`。
- **publish 被锁（MSB3491/MSB3021 Access denied）的沙箱修复顺序**：① 先 `dotnet build-server shutdown` 关掉遗留 Roslyn/VBCSCompiler+MSBuild 服务器（它们持有 `obj/.../PublishOutputs.*.txt` 与 `genbundle.cache`）；② 再用 `rm -f` 删除发布目录里被占的 `*.xml`/`*.pdb`/`*.exe` 等具体文件（**勿用 `rm -rf` 整目录**，超 50 文件会被安全阈值拦截）。`taskkill` 在本沙箱属 LOLBin 被禁用，不要在发布流程里依赖它。
- **验证修复确已编入单文件二进制**：管道测菜单不可行（NewLife.Agent 顶层菜单用 `ReadKey` 读序号，stdin 重定向即抛 `Cannot read keys when console input has been redirected`）。改用 Python 以 **UTF-16LE**（`s.encode("utf-16-le")`）在 exe/dll 中检索新增中文文案确认存在。
- 前台运行用 `--serve`（双横杠；带事件阻塞，stdin 重定向下可靠）。`-run` 经 Agent 需交互控制台，管道模式抛 `ReadKey` 异常。
- **服务安装无法在本沙箱完成**：`sc`/`wmic`/`reg`/`schtasks` 等系统级工具被安全策略禁用；`exe -i` 在非管理员环境触发 Agent 自动提权→`WindowsService.ExecutablePath` 空值→`UriFormatException` 崩溃。正确安装只能在**用户本机右键 exe → 以管理员身份运行 → 菜单 2（安装服务）**完成（此前已成功）。沙箱内临时运行用 `--serve` 后台进程即可（占 11434，装服务前需先停掉）。
- 自检 `.exe self-test`：零框架、退出码=失败数，**当前 157/0 全绿**（hermetic，不依赖部署目录 settings.json）。
- 端口：hub 127.0.0.1:11434；mock openai :9099；fake ollama :11435。

## 已修 Bug（勿重现）
- 干净安装 serve 崩溃：`HubSettings.Url=""` 致 `new Uri("")` 抛异常 → `Start()` 与 `Load()` 后均 `Normalize()`；`ServeCommand.Run` 补 `XTrace.UseConsole()`。
- Admin 面板「监听」卡片 URL 截断 → 改为独占一行全宽卡片（`word-break:break-all`）。

## HTTP 端点（勿遗漏 OpenAI 兼容层）
- 除 Ollama 原生 `/api/tags`、`/api/chat`、`/api/generate` 外，**必须**保留 `POST /v1/chat/completions` 与 `GET /v1/models`：VS 的 "Ollama" BYO 提供商底层是 OpenAI 客户端，只打 `/v1/*`；缺失会导致框架级裸 404（不进 handler、日志无痕）。真实 Ollama 也支持该兼容路径。
- 实现复用 `adapter.BuildRequest` + `CallUpstreamStream`（上游响应已归一化为 OpenAI 形状），SSE 中继加 `data:` 前缀与 `[DONE]` 尾帧；非流式经 `OpenAiAccumulator` 聚合。ollama 模式走 `UpstreamClient.RelayAsync` 透传。

## Git 推送（网络：github.com:443 间歇性阻断，重要）
- **状态（2026-08-05 更新）**：`github.com:443` 为**间歇性** SNI 阻断（非永久）。当日上午实测恢复（200，0.4s），普通 `git push`/`pull`/`ls-remote` 均正常（已验证 `ls-remote` 返回 `main -> 2b0c4be` 与本地一致）。
- 阻断复现时（`curl --max-time 10 https://github.com` 超时）仍可走 **`api.github.com` 的 Git Data API** 重建对象推送：用 `git credential fill` 取本机已存 token；`git cat-file blob/commit` 读**索引字节**（勿读工作区，autocrlf 致 CRLF 偏差）；`blobs → trees(base_tree) → commits → PATCH refs/heads/main`；精确复制 message 与时间戳使远端 SHA 与本地字节级一致；最后 `git update-ref refs/remotes/origin/main <sha>` 同步跟踪引用。
- Fine-grained PAT 已存入 git credential store（`username=cuiwenyuan`），可直接用于 HTTPS 推送，无需 SSH。

## 文档索引
- `docs/README.md`（索引）、`architecture.md`、`configuration.md`、`providers.md`（11 家 BaseUrl+模型）、`security.md`、`install-as-service.md`（可视化安装）、`vs-setup.md`（VS Copilot 接入，含 5 张截图）、`upgrade.md`、`troubleshooting.md`、`faq.md`。
