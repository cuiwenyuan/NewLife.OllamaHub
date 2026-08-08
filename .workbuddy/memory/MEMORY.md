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

## 配置与三监听（2026-08-06 双监听；2026-08-07 加 lanHttp）
- `HubSettings` 三独立节点（不向后兼容）：`Local`（明文 HTTP，默认 enabled/127.0.0.1/11434）+ `LanHttp`（明文 HTTP，默认 disabled/0.0.0.0/11436/无证书）+ `LanHttps`（TLS，默认 disabled/0.0.0.0/11435/`certificate`(PFX)/`certPassword`）。旧顶层 `url`/`host`/`port`/`httpsPort`/`certificate`/`certPassword` **已删，不再兼容**。`Normalize()` 归一化三子对象；`ProviderPresets` 默认构造改无参 `new HubSettings()`。
- `OllamaHttpServer` 三 `HttpServer`：`_local`(Local) + `_lanHttp`(LanHttp，无证书) + `_https`(LanHttps，证书缺失告警跳过)；`ReconcileListeners()` 逐监听对账 enabled/地址/证书，失败回退，无需重启；`HandleStatus` 输出 `listeners[]`(name/scheme/enabled/url/bound)。
- **VS 局域网接入（证书受信任 = 原生 HTTPS 直连，无需 lanHttp 替换）**：VS "Ollama" BYO 是非 localhost 强制 HTTPS 的 OpenAI 客户端，走 `GET /v1/models` 列模型。**正确做法**：启用 `lanHttps`（11435）+ 一张**被 VS 机器信任**的证书（纯 PowerShell 自签已实测可用：生成时 IP 写进 iPAddress 型 SAN，导出 `hub.cer` 导入 VS 机器「受信任的根证书颁发机构」），VS 直接填 `https://<IP>:11435`（**Endpoint URL 不带 `/v1`**，VS 自动拼接）即可拉到模型。**之前那条"证书在 VS 里不行、需 lanHttp 替换"的 workaround 是错误的**——证书一旦受信任，原生 HTTPS 完全可用，lanHttp 只是可选明文备选，并非必需。文档见 `docs/configuration.md`「三监听」+「VS 局域网接入说明」、`docs/vs-setup.md`「局域网接入」、`docs/faq.md`。
- `SecretProtector` 实为 BCL AES-256-CBC 机器绑定（盐 `NewLife.OllamaHub::v1::`+MachineName），非 DPAPI；优先级 明文>`env:NAME`>`dpapi:<base64>`>明文。
- 证书须被 VS 机器**真正**信任（自签导出 `hub.cer` 导入「受信任的根证书颁发机构」；存储位置错装成"个人"则仍报未信任）；Hub 无鉴权，暴露局域网=Key 暴露，**仅限可信网络/VPN**。

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
- self-test `.exe self-test` 零框架，退出码=失败数，当前 211/0 全绿（2026-08-07 +3 Responses 测试 + 增量流式断言重写 + 7 三监听 schema 断言）。
- exe 版本号=构建日期+时间(csproj：`AssemblyVersion=1.0.<距2000-01-01天数>.<自午夜半秒数>`、`FileVersion=1.0.<YYYY>.<MMDD>`，用户 2026-08-05 明确要求，勿改固定)。
- 端口：hub 本机 127.0.0.1:11434(local) / 局域网明文 0.0.0.0:11436(lanHttp) / 局域网 HTTPS 0.0.0.0:11435(lanHttps)；mock openai :9099；fake ollama :11435。

## GitHub Release / 推送
- 推送 `v*` tag（如 `git tag v1.0.0 && git push origin v1.0.0`）→ `.github/workflows/build.yml` 自动 build+publish+`softprops/action-gh-release@v2`，用内置 GITHUB_TOKEN，无需 PAT。CI 仅覆盖 `Version=tag`，文件版本仍走日期+时间。
- `github.com:443` 间歇 SNI 阻断：先试常规 `git push origin main`；阻断复现（`curl --max-time 10 https://github.com` 超时）走 `api.github.com` Git Data API（`git credential fill` 取 Fine-grained PAT；PAT 已存 credential store，username=cuiwenyuan）。**纯文档/代码提交用 Git Data API 重建 commit 并精确对齐本地 SHA 的步骤（2026-08-08 实测通过）**：
  1. `git log -1 --format='%an%x00%ae%x00%ad%x00%cn%x00%ce%x00%cd' --date=raw <SHA>` 取作者/提交者/日期；日期转 ISO（`2026-08-08T10:23:06+08:00`）传给 API 即可被还原成 `epoch +0800`。
  2. **致命坑（中文 Windows）**：Python 读 `git show`/`cat-file` 输出**必须按字节**（`capture_output=True, text=False`）再 `.decode('utf-8')`；用 `text=True` 会按系统默认 **GBK** 解码，把 UTF-8 中文字节弄坏 → blob/tree SHA 全错，但树能重建、commit 仍不匹配。
  3. 取 blob 内容用 `git show <SHA>:<path>`（字节），逐文件 POST `/git/blobs`(`encoding=utf-8`)。
  4. 嵌套树用 `base_tree` 增量重建：逐层 `POST /git/trees`（根→`.workbuddy`→`docs` 等），每层 `base_tree` 取父 commit 对应子树 SHA（`git rev-parse <parent>:<path>`），只覆盖变更条目。
  5. **commit message 必须取原始字节**：`git cat-file commit <SHA>` 后从首个 `committer ...\n\n` 之后切片取消息（含末尾换行）；**不能用 `git log --format=%B`**——它会剥掉末尾换行导致 SHA 不一致。
  6. `POST /git/commits`(tree/parents/author/committer/message) → `PATCH /git/refs/heads/main`(`force:true`)。远端 SHA 应与本地完全一致，避免 fetch 后分叉。
  7. SSH 不可用（无 `~/.ssh` 私钥），勿走 22 端口。

## 战略决策：不引入 NewLife.AI（2026-08-05）
- 不引入 NewLife.AI（核心库与 ChatAI 网关均不引入）；继续自维护手写适配+Ollama 兼容端点。再评估：① 原生提供 Ollama 兼容服务端；② 非 ASP.NET Core 轻量网关；③ 手写维护成本陡增。

## 文档索引
- `docs/`：README/architecture/configuration/providers/security/install-as-service/vs-setup/upgrade/troubleshooting/faq/competitor-analysis/newlife-ai-evaluation.md。
