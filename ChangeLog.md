# ChangeLog

本文件记录所有发版（Git tag）的主要变化。**每个版本对应一个独立 tag**，单文件 exe 发布包见 [GitHub Releases](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases)。

版本号规则：exe 文件版本采用「构建日期 + 时间」编码；语义版本由 Release tag 承载。

---

## [Unreleased] - 模型预设全量刷新（待发版）

> 对应待打 tag；本版将 12 家供应商模型预设一次性对齐到 2026-09 各家官方最新 Id 与端点。

### 供应商预设升级（12 家模型全量刷新）
- **DeepSeek 直接切换为 `deepseek-flash`**：2026-09-10 官方将 V4 Flash / V4 Flash Vision Exp 统一为规范名 `deepseek-flash`（原生多模态 + 可切换思考），`deepseek-v4-pro` 于 2026-09-14 起自动回落到 V4.1 Flash 并退役——不再保留任何 v4 别名（`deepseek-v4-flash` / `deepseek-v4-pro` / `deepseek-v4-flash-vision-exp` 全部移除）。
- **腾讯混元迁移到 TokenHub**：混元旗舰 HY3 / Hy4 现经腾讯云 TokenHub（`https://tokenhub.tencentmaas.com/v1`）暴露，旧 `api.hunyuan.cloud.tencent.com` 的 `hunyuan-a13b` 等不在 TokenHub 体系；`hunyuan` 预设基址与模型同步改为 `hy3` / `hy4-preview`。
- **硅基流动 / ModelScope 升到 V4 系列**：DeepSeek 由 V3/V3.1/R1 升到 `DeepSeek-V4-Flash-0731` / `-Vision-Exp` / `-Pro-0813`（ModelScope 另含 `DeepSeek-V4.1-Flash`）；新增智谱 `zhipuai/GLM-5.3`(+Flash)、`Qwen/Qwen3.8-2.4T-A95B`（硅基流动）/ `Qwen/Qwen3.8-27B`、`Qwen/Qwen3.8-Flash-Next`（ModelScope）、`ZhipuAI/GLM-5.3`（ModelScope）、`moonshotai/Kimi-K3`（硅基流动）。
- **火山方舟**：保留 `doubao-seed-2.1`(260628) 与统一 ID 思考模型 `doubao-seed-evolving`（经核实仍在线），新增旗舰快照 `doubao-seed-2-1-pro-260915`。
- **OpenRouter**：新增 `openai/gpt-6-astra`、`anthropic/claude-fable-5.1`、`google/gemini-3.8-flash`（保留 gpt-5.6 / claude-opus-5 / gemini-3.7-flash）。
- **Anthropic**：新增 `claude-opus-5-5` / `claude-fable-5-1`（保留 opus-5 / sonnet-5 / fable-5 / haiku-4-5）。
- **Google Gemini**：新增 `gemini-3.8-flash`（保留 3.1 Pro / 3.7 Flash / 3.5 Flash-Lite）。
- **Kimi 未加 `kimi-k2.8-preview`**：经核实 K2.8 Preview 仅在 Kimi Code 订阅服务，官方开放 API（`api.moonshot.cn/v1`）无此 Id，加上会 404；`kimi` 预设维持 `kimi-k3` / `kimi-k2.7-code` / `kimi-k2.6`。

### 文档与示例
- `docs/providers.md` 12 家 + 2 家原生协议表格刷新到上述模型 Id / 端点；`examples/settings.*.json`（deepseek / siliconflow / gemini / anthropic / vision / newmodels）同步。
- **已知风险**：硅基流动模型 Id 的 owner 大小写在第三方源间有分歧（openaisuite 显示小写 `zhipuai/glm-5.3`，而官方模型名为大写 GLM-5.3）；本版采用与既有 `deepseek-ai/` / `Qwen/` 一致的大写惯例，若调用 404，请以你自己账号 `GET https://api.siliconflow.cn/v1/models` 返回的实际 Id 为准并反馈。

---

## [v1.3.4] - 2026-08-29

> Tag：[v1.3.4](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.3.4)

### 供应商预设升级
- **GLM 双供应商模型命名空间化（Z.ai 与 BigModel 可并存）**：`zhipu`（智谱 Z.ai）模型 Id 由 `glm-5.3` 等改为命名空间形式 `zhipu-glm-5.3` / `zhipu-glm-5.3-flash` / `zhipu-glm-5.2` / `zhipu-glm-5.1` / `zhipu-glm-4.7` / `zhipu-glm-4.7-flashx`，避免与 `glm`（智谱 BigModel）的 `glm-5.3` 在 `/api/tags` / `/v1/models` 撞车。新增 `ModelOptions.UpstreamModel` 字段，`zhipu` 模型借此仍向上游发送真实模型名（如 `glm-5.3`）。上游请求（OpenAI/Anthropic/Gemini/Responses）一律改用 `model.UpstreamModelName`（回落 `Id`）；响应回填客户端的 `model` 仍用 `Id`。以此实现「两家 GLM 同时启用、客户端各自选择」而互不冲突。
- `self-test` 新增 `CheckUpstreamModelMapping` 防回归断言（6 条）：校验 `zhipu` 请求体发出真实上游名 `glm-5.3`（而非 `zhipu-glm-5.3`）、`UpstreamModelName` 回落逻辑、以及 12 家标志性模型 Id 存在性。

---

## [v1.3.3] - 2026-08-28

> Tag：[v1.3.3](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.3.3)

### 供应商预设升级（2026-08 模型刷新）
- **GLM 拆分为双供应商**：保留 `glm`（智谱 BigModel `open.bigmodel.cn/api/paas/v4`），新增 `zhipu`（智谱 Z.ai `api.z.ai/v1`，OpenAI 兼容），两者镜像同一 GLM 模型集，用户可按密钥/配额二选一或同时启用。内置供应商总数 11 → **12**。
- **9 家供应商模型刷新**：
  - `deepseek`：新增 `deepseek-v4-flash-vision-exp`（实验多模态）。
  - `siliconflow`：新增 `Qwen/Qwen3-235B-A22B-Instruct`、`deepseek-ai/DeepSeek-V3.1`。
  - `volcengine`：新增统一 ID 思考模型 `doubao-seed-evolving`（1M 上下文）。
  - `hunyuan`：新增 `hunyuan-hy4-preview`（2026-08 开源 Hy4 系列）。
  - `modelscope`：基址修正为 API-Inference 域名 `api-inference.modelscope.cn/v1`，新增 `Qwen/Qwen3-235B-A22B-Instruct`、`deepseek-ai/DeepSeek-V3.1`。
  - `openrouter`：模型迭代为 `openai/gpt-5.6` / `anthropic/claude-opus-5` / `google/gemini-3.7-flash`（旧 `gpt-4o` / `claude-3.5-sonnet` / `gemini-pro-1.5` 已弃用）。
  - `anthropic`：新增 `claude-fable-5`。
  - `gemini`：迭代为 `gemini-3.1-pro-preview` / `gemini-3.7-flash` / `gemini-3.5-flash-lite`（2.5 系列退役）。
  - `kimi`：保持 `kimi-k3` / `kimi-k2.7-code` / `kimi-k2.6` 当前集。

### 质量
- `self-test` 新增 `CheckPresetModelIds` 防漂移断言：对 12 家供应商的标志性模型 Id 做存在性校验，误改名/误删会立即 FAIL，把"静默漂移"变成可见回归。全量 **271/0**（含新增 ~42 条断言）。
- `self-test` 新增 `CheckToolCallIdRequestPassthrough`：验证上游 OpenAI 请求中 tool 消息带 `tool_call_id` 且与 assistant `tool_calls` 的 id 对应（DeepSeek 等强制要求，缺失即 400）。

### 修复
- **调用 DeepSeek（含工具调用）返回 400 `missing field tool_call_id`**：根因为三层叠加——① `OpenAiMessage` 类原本没有 `tool_call_id` 属性，入站 `/v1/chat/completions` 的 `tool_call_id` 未被解析；② `ToOllamaRequest`（OpenAI→Ollama 转换）未透传 `tool_call_id`；③ `OpenAiAdapter.BuildOpenAiRequest` 构造上游消息时，对 `role:"tool"` 只写 `role`+`content` 而从不输出 `tool_call_id`。现已补齐：解析端加属性、转换端透传、构造端输出 `tool_call_id`（优先用入站值，缺失时按最近 assistant 的 `tool_calls` 顺序合成 `call_0`/`call_1`… 并严格配对）。这样即使客户端未下发 `tool_call_id`（如 Ollama 原生格式），也能生成合规请求，避免对 DeepSeek/OpenAI 兼容上游 400。

### 示例与文档
- `examples/` 新增 `settings.newmodels.json`（汇集本次新增模型 + Z.ai 通道）；`settings.gemini.json`、`settings.qwen-kimi-glm.json` 更新为当前模型。
- `docs/providers.md`、`README.md`、`docs/configuration.md`、`docs/install-as-service.md`、`docs/README.md`、`docs/newlife-ai-evaluation.md`：供应商计数 11 → 12，补充 `zhipu` 与刷新后的模型 Id。

---

## [v1.3.2] - 2026-08-20

> Tag：[v1.3.2](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.3.2)

### 修复与增强
- **诊断响应头全端点一致**：`HandleGenerate` 此前全程漏调 `SetDiagnosticHeaders`，现已补齐 `ollama 透传 / fallback / 流式 / 非流式` 四个分支，所有端点均注入 `X-Proxy-Requested-Model` / `X-Proxy-Resolved-Model` / `X-Proxy-Provider` / `X-Proxy-Upstream-Mode`（排障时一眼看出路由到哪家供应商、哪个模型）。
- **OpenAI 兼容端点对齐 Ollama v0.32.6**：此前未把客户端 `stream_options`（含 `include_usage`）透传给上游，导致 `usage` 独立块缺失。现已在 `OpenAiChatRequest` / `OllamaChatRequest` 增加 `stream_options` 字段，经 `ToOllamaRequest` → `OpenAiAdapter.BuildOpenAiRequest` 透传（仅 openai 上游生效，原生 / anthropic / gemini 不受影响）。原生 `/api/chat` 帧格式 v0.32.6 未变更，本项目的 `OllamaStreamTranslator` 行为保持正确。

### 文档
- `docs/architecture.md` / `docs/vs-setup.md`：VS Code 1.127+ 已弃用内置 Ollama 提供程序（[microsoft/vscode#299907](https://github.com/microsoft/vscode/issues/299907)），引导 VS Code 用户改走「自定义端点（Chat Completions）」`http://localhost:11434/v1/chat/completions` 或官方 Ollama 扩展；新增「VS Code（1.127+）接入」整节。Visual Studio 的 Ollama BYO 路径不受影响。
- `docs/faq.md` / `docs/vs-setup.md`：**新增 Visual Studio 版本要求**——Ollama（Bring Your Own Model）提供程序是 **Visual Studio 2026（18.6.0+）** 才引入的能力，**Visual Studio 2022（17.x）内置的 Copilot Chat 没有 Ollama 选项、也无法手动添加**；需升级到 VS 2026，或改用 VS Code 自定义端点（不依赖 VS 的 Ollama 提供程序）。

### 质量
- `self-test` 全量 **214/0**（新增 3 条 `stream_options` 透传断言）；`dotnet build` 0 错误（既有 NRT 警告，零新增）。

---

## [v1.3.1] - 2026-08-13

> Tag：[v1.3.1](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases/tag/v1.3.1)

### 修复（交互配置向导）
- **修复向导“一闪取消”**：菜单用 `Console.ReadKey` 读字符后，用户按 Enter 的回车符残留在输入缓冲区，导致向导内部 `ReadLine` 立即读到空行、误判“未输入供应商 id，已取消”。已在读取前清空输入缓冲区。
- **修复首次配置提示不明确**：`c` 配置向导在首次配置（无 `settings.json`）时直接等待输入却无任何提示，用户不知该键入什么。现已在向导中打印明确的输入规则（单个 / 空格逗号分号分隔多个 / `all` 启用全部 / 回车取消）并列出全部 11 家内置供应商的 id 与名称。
- **修复残留回车跳过首个 API Key**：`ReadKey` 后的回车残留会在沿用现有配置时跳过向导中第一个供应商的 Key 输入；已统一在每次 `ReadKey` 后清空输入缓冲区。
- 涉及文件：`src/NewLife.OllamaHub/HubAgentService.cs`（`PickPresetIds` 增加 `prompt` 参数、新增 `ClearInputBuffer` 辅助方法、`ConfigureWizard` 首次/覆盖/沿用分支均补清晰引导）。

### 文档
- `docs/install-as-service.md`「配置大模型」一节：补全 `c` 配置向导的交互流程说明（输入规则、供应商列表、逐家填 Key、已存在配置时的覆盖询问），与修复后的交互一致。

### 质量
- `self-test` 全量 **211/0**；`dotnet build` 0 错误（9 个历史遗留 NRT 警告，本次零新增）。

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
