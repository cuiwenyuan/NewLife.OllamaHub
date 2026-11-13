# NewLife.OllamaHub — 项目长期记忆

## 硬约束
- 仅引用 NewLife 生态 NuGet：`NewLife.Core` 12.0.2026.1024 + `NewLife.Agent` 10.18.2026.1024；无 ASP.NET Core、无第三方 NuGet。**升级 NewLife.Core 跨大版本（11→12）后实测编译 0 错误、双框架 self-test 306/0、HTTP 端点冒烟全通**，本库只用了 `HttpServer`/`JsonHelper`/`XTrace`/`NetUri`/`Setting` 等稳定 API。
- **双目标 `net8.0;net10.0`**（`SelfContained=false` + `PublishSingleFile=true` + `RuntimeIdentifier=win-x64`，两个产物均为非自包含单文件）：发布为 `NewLife.OllamaHub-net8.0.exe`（需 .NET 8 运行时）与 `NewLife.OllamaHub-net10.0.exe`（需 .NET 10 运行时），**装错版本直接起不来**；功能完全一致。构建全部目标需 SDK 10；只有 SDK 8 时用 `-p:TargetFrameworks=net8.0` 单构建。`dotnet run` 多目标下**必须带 `-f net8.0|net10.0`**。`ImplicitUsings` 开（泛型集合免 using，非泛型 `IList`/`IDictionary` 写全称）。
- 服务化用 `NewLife.Agent.ServiceBase` 派生，不用 `Microsoft.Extensions.Hosting`。
- 服务名 `ServiceName = "NewLife.OllamaHub"`（含点）；旧 `NewLifeOllamaHub`（无点）已弃用。

## 关键架构
- HttpServer 严格单发：处理委托返回后框架必发尾随 200 → 永远"缓冲累积 → 一次性 `ctx.Response.Body = new ArrayPacket(bytes)`"，勿手动分块。
- 流式桥接：`HandleChat`/`HandleGenerate` 一律上游 `forceStream:true`，统一经 `OllamaStreamTranslator` 翻 Ollama 帧。**已对齐真实 Ollama 协议（2026-08-07 合并 PR #2 增量方案修复）**：流式每帧下发【增量 delta】，`done:true` 末帧 `Finalize(false)` 仅发结束信号（content 空）；非流式 `Finalize()` 保留完整内容。此前"累积完整内容下发"违反协议 → CherryStudio/OpenWebUI 重复输出（已修）。保留原因：部分网关忽略 `stream:false` 仍回 SSE，统一 SSE 规避 502。VS Copilot 走 `/v1/chat/completions` 直接中继 OpenAI 增量 SSE，不经此累积逻辑，不受影响。
- 统一适配层 `Core/IUpstreamAdapter` + `UpstreamAdapterFactory`(openai/anthropic/gemini/google/responses，未知回落 openai)。Anthropic=`x-api-key`+`anthropic-version`、Gemini=key 拼 `?alt=sse&key=`、Responses=`/v1/responses`（input items + max_output_tokens + reasoning.effort）；多模态 images→ OpenAI image_url / Anthropic source.base64 / Gemini inline_data。
- 工具 `ToolSchemaSanitizer.Sanitize` 递归删 `$schema`/`definitions`/`$defs`/`title`/`examples`/`additionalProperties`/`x-*`。
- HTTP 端点：Ollama 原生 `/api/tags`/`/api/chat`/`/api/generate` + **必须**保留 `POST /v1/chat/completions`、`GET /v1/models`（VS "Ollama" BYO 是 OpenAI 客户端，只打 `/v1/*`，缺失裸 404）。ollama 模式走 `UpstreamClient.RelayAsync`。
- **`UpgradeCommand.PickAssetUrl` 按当前 TFM 挑 Release 资产**（`CurrentTargetFramework`，`#if NET10_0_OR_GREATER` → `net10.0`，否则 `net8.0`）：多产物后**禁止回退成"盲取首个 `browser_download_url`"**，否则 net8 用户会被升级成 net10 产物、机器无 .NET 10 运行时直接起不来；匹配不到才回退首个并打印告警（兼容老的单产物 Release）。self-test 3 条断言守护。
- 证书加载条件编译：`#if NET9_0_OR_GREATER` 用 `X509CertificateLoader.LoadPkcs12FromFile`（消除 SYSLIB0057），net8.0 目标保持 `X509Certificate2` 构造。

## 配置与三监听（2026-08-06 双监听；2026-08-07 加 lanHttp）
- `HubSettings` 三独立节点（不向后兼容）：`Local`（明文 HTTP，默认 enabled/127.0.0.1/11434）+ `LanHttp`（明文 HTTP，默认 disabled/0.0.0.0/11436/无证书）+ `LanHttps`（TLS，默认 disabled/0.0.0.0/11435/`certificate`(PFX)/`certPassword`）。旧顶层 `url`/`host`/`port`/`httpsPort`/`certificate`/`certPassword` **已删，不再兼容**。`Normalize()` 归一化三子对象；`ProviderPresets` 默认构造改无参 `new HubSettings()`。
- `OllamaHttpServer` 三 `HttpServer`：`_local`(Local) + `_lanHttp`(LanHttp，无证书) + `_https`(LanHttps，证书缺失告警跳过)；`ReconcileListeners()` 逐监听对账 enabled/地址/证书，失败回退，无需重启；`HandleStatus` 输出 `listeners[]`(name/scheme/enabled/url/bound)。
- **VS 局域网接入（证书受信任 = 原生 HTTPS 直连，无需 lanHttp 替换）**：VS "Ollama" BYO 是非 localhost 强制 HTTPS 的 OpenAI 客户端，走 `GET /v1/models` 列模型。**正确做法**：启用 `lanHttps`（11435）+ 一张**被 VS 机器信任**的证书（纯 PowerShell 自签已实测可用：生成时 IP 写进 iPAddress 型 SAN，导出 `hub.cer` 导入 VS 机器「受信任的根证书颁发机构」），VS 直接填 `https://<IP>:11435`（**Endpoint URL 不带 `/v1`**，VS 自动拼接）即可拉到模型。**之前那条"证书在 VS 里不行、需 lanHttp 替换"的 workaround 是错误的**——证书一旦受信任，原生 HTTPS 完全可用，lanHttp 只是可选明文备选，并非必需。文档见 `docs/configuration.md`「三监听」+「VS 局域网接入说明」、`docs/vs-setup.md`「局域网接入」、`docs/faq.md`。
- `SecretProtector` 实为 BCL AES-256-CBC 机器绑定（盐 `NewLife.OllamaHub::v1::`+MachineName），非 DPAPI；优先级 明文>`env:NAME`>`dpapi:<base64>`>明文。
- 证书须被 VS 机器**真正**信任（自签导出 `hub.cer` 导入「受信任的根证书颁发机构」；存储位置错装成"个人"则仍报未信任）；Hub 无鉴权，暴露局域网=Key 暴露，**仅限可信网络/VPN**。

## 供应商预设与模型名
- `ProviderPresets.cs` 内置 12 家（含 GLM 双通道 glm/zhipu）：9 OpenAI 兼容(deepseek/qwen/kimi/glm/zhipu/siliconflow/volcengine/hunyuan/modelscope/openrouter)+anthropic+gemini。
- 模型 Id 漂移频繁，**新增/核对预设务必联网核实官方当前模型名，禁凭记忆填旧名**；无线上端点的预告模型（如 Qwen4）不写入，避免 404。
- 2026-11-13 全量刷新后现行：deepseek=`deepseek-flash`(V4.1-1017,1M/384K,视觉)+`deepseek-v4-pro`(纯文本)；kimi=`kimi-k3.1-pro`/`-highspeed`/k3/k2.7-code/k2.6；glm/zhipu=`glm-5.3`+`glm-5.3-flash`+`glm-5.3-flashx`…；gemini=`gemini-4-hydrogen`(1M 统一输入输出)/`carbon`/`argon`+3.8-flash；hunyuan=`hy4`(1M/64K,视觉)/`hy3`；volcengine 豆包 2.1 `pro/lite-260915`(1024K/256K)+evolving；anthropic=`claude-fable-5-2`/opus-5-5/sonnet-5-5/haiku-5-5；openrouter 含 gpt-6-astra/gemini-4/tencent-hy4。

## 可视化菜单（p/k/c）
- `HubAgentService` 三嵌套子类继承 `BaseCommandHandler`(PresetMenuCommand=p/ApiKeyMenuCommand=k/WizardMenuCommand=c)，`ServiceBase.Command` 自动扫描注册——取代过时 `AddMenu`。

## 构建 / 发布 / 测试
- 构建 `dotnet build -c Release`；发布 `dotnet publish -c Release -r win-x64 -o <dir>`（单文件）。
- 服务实例锁（2026-08-06）：发布目标被 Windows 服务锁住（Session 0，普通 `Stop-Process`/`taskkill` 拒绝访问，`sc`/`net` 沙箱禁用）→ 须用户本机管理员 `Stop-Service NewLife.OllamaHub` 后重发。
- 服务安装无法在沙箱完成（`exe -i` 非管理员提权崩溃）→ 用户本机右键 exe→管理员→菜单 2。
- 验证修复编入单文件：菜单用 `ReadKey`（stdin 重定向抛异常）→ 用 Python UTF-16LE(`s.encode("utf-16-le")`) 在 exe/dll 检索中文文案。
- 前台 `--serve`（双横杠，stdin 重定向下可靠）；`-run` 抛 `ReadKey` 异常。
- self-test `.exe self-test` 零框架，退出码=失败数，当前 **306/0 全绿**（net8.0 与 net10.0 各跑一遍均 306/0；历史：2026-11 预设刷新 303，2026-08 为 211）。
- **版本号=日期 4 段，对齐 NewLife 全家桶主干库**（用户 2026-11-13 二次调研后拍板，**勿再改回"天数+半秒"**）：`AssemblyVersion=FileVersion=主.次.<YYYY>.<MMDD>`（如 `1.3.2026.1113`），tag 同名 `v1.3.2026.1113`；`ProductVersion=1.3.2026.1113+<sha>`（SDK 自动追加，本地构建也有，无需注入 `SourceRevisionId`）。csproj 实现：`<BaseVersion>1.3</BaseVersion>` + `_BuildYear`/`_BuildMmdd`（`[System.DateTime]::Now`）+ `<Version Condition="'$(Version)'==''">`，**`AssemblyVersion`/`FileVersion` 一律取自 `$(Version)`**（不要各自重算日期，否则 CI 传 tag 时会被构建当天日期覆盖）。日期不含时刻 → 同一天构建幂等（一天一版本），年月日单调 → `UpgradeCommand` 版本比较无需改动。主.次为语义版本，发版时手工递增。
- 端口：hub 本机 127.0.0.1:11434(local) / 局域网明文 0.0.0.0:11436(lanHttp) / 局域网 HTTPS 0.0.0.0:11435(lanHttps)；mock openai :9099；fake ollama :11435。

## GitHub Release / 推送
- 推送 `v*` tag（形如 `git tag v1.3.2026.1113 && git push origin v1.3.2026.1113`，**必须 4 段**，CI 用正则 `^[0-9]+\.[0-9]+\.[0-9]{4}\.[0-9]{4}$` 校验，不合规直接失败）→ `.github/workflows/build.yml` 自动 build+publish+`softprops/action-gh-release@v2`，用内置 GITHUB_TOKEN，无需 PAT。**tag 触发时版本号完全取自 tag**（`-p:Version=` 全局属性覆盖 csproj，AssemblyVersion/FileVersion 随之等于 tag），分支构建则留空由 csproj 按当天日期推导；发布后新增 `Verify published version` 步骤校验产物 `FileVersion` 与 tag 一致。**CI 装 8.0.x + 10.0.x 两套 SDK**，两框架各跑 self-test，分别 `publish -f net8.0|net10.0`，再复制改名为 `dist/NewLife.OllamaHub-net8.0.exe` / `-net10.0.exe` 一并上传 Release。
- `github.com:443` 间歇 SNI 阻断：先试常规 `git push origin main`；阻断复现（`curl --max-time 10 https://github.com` 超时）走 `api.github.com` Git Data API（`git credential fill` 取 Fine-grained PAT；PAT 已存 credential store，username=cuiwenyuan）。**纯文档/代码提交用 Git Data API 重建 commit 并精确对齐本地 SHA 的步骤（2026-08-08 实测通过）**：
  - **沙箱无法非交互取 PAT（2026-08-13 实测）**：`git credential fill` / `git-credential-manager get` 在沙箱里失败——前者报 `/dev/tty: No such device`（GCM 尝试交互提示），后者段错误（管道调用）；PowerShell `Add-Type` 运行时编译 .NET 也被安全策略拦截。但 `git ls-remote origin` 能正常解析凭据（git 自身调 GCM 成功），`git push` 却会因 SNI 重置触发 GCM 误判鉴权失败→重提示→同样失败。**结论**：沙箱内走 API 推送时，PAT 无法自动取得，需用户本机手动 `git push origin main <tag>`（其 GCM 正常），或向用户索取 PAT。
  1. `git log -1 --format='%an%x00%ae%x00%ad%x00%cn%x00%ce%x00%cd' --date=raw <SHA>` 取作者/提交者/日期；**日期须转成 ISO8601 带冒号时区（`2026-08-08T10:23:06+08:00`）**——`git cat-file` 给的 `+0800` 原样传给 API 会报 `not a valid date-time`，必须用 `datetime.fromtimestamp(epoch, tz=timezone(+8h)).isoformat()` 转成 `+08:00`；用正则把 `Name <email> epoch +tz` 拆成 name/email/date 三部分再分别传。
  2. **致命坑（中文 Windows）**：Python 读 `git show`/`cat-file` 输出**必须按字节**（`capture_output=True, text=False`）再 `.decode('utf-8')`；用 `text=True` 会按系统默认 **GBK** 解码，把 UTF-8 中文字节弄坏 → blob/tree SHA 全错，但树能重建、commit 仍不匹配。
  2b. **推送前先 GET `/git/refs/heads/main` 拿真实远端 SHA 作父节点**：本地 `origin/main` 跟踪引用因从未 `git fetch`（SNI 阻断）会停在旧值，不可信；须以 API 返回的 SHA 为准，并 `git merge-base --is-ancestor <remote> HEAD` 校验其为本地祖先后再链推 `git rev-list --reverse <remote>..HEAD`。
  3. 取 blob 内容用 `git show <SHA>:<path>`（字节），逐文件 POST `/git/blobs`(`encoding=utf-8`)。
  4. 嵌套树用 `base_tree` 增量重建：逐层 `POST /git/trees`（根→`.workbuddy`→`docs` 等），每层 `base_tree` 取父 commit 对应子树 SHA（`git rev-parse <parent>:<path>`），只覆盖变更条目。
  5. **commit message 必须取原始字节**：`git cat-file commit <SHA>` 后从首个 `committer ...\n\n` 之后切片取消息（含末尾换行）；**不能用 `git log --format=%B`**——它会剥掉末尾换行导致 SHA 不一致。
  6. `POST /git/commits`(tree/parents/author/committer/message) → `PATCH /git/refs/heads/main`(`force:true`)。远端 SHA 应与本地完全一致，避免 fetch 后分叉。
  7. SSH 不可用（无 `~/.ssh` 私钥），勿走 22 端口。

## 战略决策：不引入 NewLife.AI（2026-08-05）
- 不引入 NewLife.AI（核心库与 ChatAI 网关均不引入）；继续自维护手写适配+Ollama 兼容端点。再评估：① 原生提供 Ollama 兼容服务端；② 非 ASP.NET Core 轻量网关；③ 手写维护成本陡增。

## 文档索引
- `docs/`：README/architecture/configuration/providers/security/install-as-service/vs-setup/upgrade/troubleshooting/faq/competitor-analysis/newlife-ai-evaluation.md。
