# ChangeLog

本文件记录所有发版（Git tag）的主要变化。**每个版本对应一个独立 tag**，单文件 exe 发布包见 [GitHub Releases](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases)。

版本号规则：exe 文件版本采用「构建日期 + 时间」编码；语义版本由 Release tag 承载。

---

## [v1.3.0] - 2026-08-07

> Tag：[v1.3.0](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.3.0)

### 新增
- **局域网明文 HTTP 监听 `lanHttp`**：新增第三监听节点（默认 `0.0.0.0:11436`，默认禁用，无证书）。与 `local`（本机 HTTP `127.0.0.1:11434`）、`lanHttps`（局域网 HTTPS `0.0.0.0:11435`）共同构成「三监听」架构；各节点独立 `enabled` 启停、独立证书，支持配置热重载对账。详见[配置参考](docs/configuration.md)。
  - 引入目的：为 VS Copilot 的 “Ollama” BYO 在**局域网**接入提供明文 HTTP 路径（当时作为解决 VS 非 localhost 强制 HTTPS 证书校验失败的 workaround）。

### 变更（配置结构）
- `HubSettings` 新增 `LanHttp` 节点；`Normalize()` 统一归一化 `local` / `lanHttp` / `lanHttps` 三个子对象（旧顶层字段已删除，见 v1.1.0）。

### 文档
- 新增 `ChangeLog.md` 记录各发版变化，并接入 `README.md`（「文档」列表增加「更新日志 ChangeLog」链接）。
- `docs/configuration.md` / `docs/vs-setup.md` / `docs/faq.md` 补充「三监听」与「VS 局域网接入说明」；`/api/status` 输出三监听状态。
- `self-test` 新增 7 项三监听断言，全量 **211/0** 通过。

### 质量
- `self-test` 全量 **211/0**；`dotnet build` 0 错误（9 个历史遗留 NRT 警告，本次零新增）。

---

## 文档维护 - 2026-08-08（未发版，commit `0ea4b29`）

> 仅文档更新，未打 tag，未触发 Release。

### 文档
- **移除 mkcert（方案 A）**：证书生成统一为「纯 PowerShell 自签」，删除全部 mkcert 相关内容（含 `-p12` 写法与「最省心推荐」措辞）。
- **VS 局域网接入主路径改为证书直连**：经实测，纯 PowerShell 自签证书（IP 写进 `iPAddress` 型 SAN，导出 `hub.cer` 导入 VS 机器「受信任的根证书颁发机构」）后，VS 可直接填 `https://<服务器IP>:11435` 拉到模型列表；`lanHttp` 由此降级为「可选明文备选 / 无法导入证书时的退路」，不再必需。
- **修正 VS Endpoint URL**：去掉尾部 `/v1`（VS 会自动在其后拼接 `/v1/models`、`/v1/chat/completions` 等路径，带 `/v1` 会变成 `/v1/v1/...` 双路径）；`curl` 连通性测试命令（`.../v1/models`）保留完整路径。
- 同步更新 `docs/configuration.md` / `docs/vs-setup.md` / `docs/faq.md` 三处相关段落，并同步项目记忆日志。

---

## [v1.2.0] - 2026-08-07

> Tag：[v1.2.0](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.2.0)

### 新增
- **OpenAI Responses 上游适配器**（`apiMode=responses`）：对接 `/v1/responses` 端点，把 Ollama 对话转换为 Responses 的 `input` items（含图片 `input_image`、工具调用 `function_call`、工具结果 `function_call_output`），并将 Responses 语义事件翻译为统一的 Ollama 帧。详见[配置参考](docs/configuration.md)。
  - 覆盖事件：`response.output_text.delta`、`response.reasoning_text.delta`、`response.reasoning_summary_text.delta`、`response.function_call_arguments.delta`、`response.output_item.added`、`response.completed` / `incomplete` / `failed`、`error`。
  - 相对外部 PR #2 的加固：`AsList` 兼容数组避免非流式丢项；`refusal.delta` 不混入 `content`（Ollama 无该字段，降级丢弃）；`reasoning_text.delta` 也映射 `thinking`。
  - 复用 `OpenAiAdapter.ApplyModelParams` / `SplitImage` 与 `ToolSchemaSanitizer`。

### 修复
- **`/api/chat` 流式增量下发对齐真实 Ollama 协议**：此前每帧下发累积完整内容，违反 Ollama 官方协议（流式每帧 `message.content` 为增量 delta，`done:true` 帧 content 为空），导致 CherryStudio / Open WebUI 等原生 Ollama 客户端**重复输出**。现已改为逐帧下发本块增量，流式结束帧仅发结束信号。
  - `OllamaStreamTranslator.Consume` 改为逐帧下发本块增量；`Finalize(includeContent)` 默认保留完整内容（非流式），流式末帧 `Finalize(false)` 仅发结束信号。
  - VS Copilot 走 `/v1/chat/completions` 直接中继 OpenAI 增量 SSE，**不受影响**。

### 文档
- `docs/configuration.md` 补充 `responses` 的 `apiMode` 说明与配置示例；`self-test` 新增 3 项 Responses 适配/翻译/非流式测试，全量 **204/0** 通过。

---

## [v1.1.0] - 2026-08-06

> Tag：[v1.1.0](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.1.0)

### 新增
- **原生 HTTPS 监听（Issue #1）**：支持局域网 / VS Copilot 通过 HTTPS 接入，无需反代。
- **本地 HTTP + 局域网 HTTPS 双监听节点独立**：`settings.json` 拆为 `local`（明文 HTTP 本机）+ `lanHttps`（TLS 局域网）两个独立子对象，各自按 `enabled` 启停；支持**配置热重载**一键切换（监听地址 / 证书即时生效，无需重启）。详见[配置参考](docs/configuration.md)。

### 变更（不向后兼容）
- `HubSettings` 删除旧顶层字段 `url` / `host` / `port` / `httpsPort` / `certificate` / `certPassword`，直接采用 `local` + `lanHttps` 新结构。`settings.json` 需按新结构改写（旧结构不再兼容）。

---

## [v1.0.0] - 2026-08-05

> Tag：[v1.0.0](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.0.0)
>
> 首个正式发版 tag，确立核心产品形态。

### 新增
- **Ollama 兼容代理内核**：在本机起一个“伪装成本地 Ollama”的 HTTP 服务，让 VS / VS Code 的 GitHub Copilot Chat 直接识别并调用国内（及海外）大模型，**无需任何 IDE 插件**。
- **OpenAI 兼容端点**（`/v1/chat/completions`、`/v1/models`）：修复 VS Copilot 调用 404，模型列表自动出现。
- **统一上游适配层**：OpenAI / Anthropic / Gemini / 透传 Ollama；多模态图像透传；Anthropic·Gemini 原生上游 + 2 家预设，**共 11 家供应商开箱预设**。
- **流式与工具桥接**：NDJSON⇄SSE 桥、`tools` 映射、`thinking` 映射。
- **服务化**：基于 `NewLife.Agent` 装成 Windows 服务（开机自启、崩溃自愈）；内置只读 Web 管理面板（用量统计）。
- **配置与安全**：`setkey` 加密 Key；`presets` 子命令一键生成 11 家供应商预设；配置热重载。
- **推理缓存重注、force-mode 强制参数、X-Proxy 诊断头**。
- **质量与发布**：内置 `self-test` 协议全覆盖（零测试框架）；GitHub Actions 版本化发布（push `v*` tag 触发）；`docs/` + `examples/` 完整。

### 修复
- 修复 Admin 统计为 0 的问题。
- DeepSeek `BaseUrl` 统一补全 `/v1`。
- 修正文档 / 示例中残留的弃用模型名 `deepseek-chat` / `deepseek-reasoner` → `deepseek-v4-flash` / `deepseek-v4-pro`。

### 变更
- exe 版本号改为「构建日期 + 时间」编码（文件版本），语义版本由 Release tag 承载。
