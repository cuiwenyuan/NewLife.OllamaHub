# ChangeLog

本文件记录所有发版（Git tag）的主要变化。**每个版本对应一个独立 tag**，单文件 exe 发布包见 [GitHub Releases](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases)。

版本号规则：exe 文件版本采用「构建日期 + 时间」编码；语义版本由 Release tag 承载。

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
