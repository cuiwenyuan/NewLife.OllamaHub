# 竞品分析报告：iqmeta/copilot-ollama-multi-provider-ai-proxy

> 对标对象：`https://github.com/iqmeta/copilot-ollama-multi-provider-ai-proxy`
> 第一轮分析：2026-08-05（仅分析，不改动代码）
> 第二轮复评：2026-08-05（P0 三项落地后复盘，见第 6 节）
> 目的：学习同赛道竞品，识别「必须有」与「锦上添花」的功能，并跟踪落地进度。

---

## 1. 竞品概况

| 维度 | 竞品 | 我们的 OllamaHub |
|---|---|---|
| 定位 | VS 2026 / VS Code Copilot / Cursor / Continue.dev 的多供应商代理 | VS / VS Code Copilot + Ollama 客户端的多供应商代理 |
| 语言/框架 | C# / **.NET 10** / **ASP.NET Core Minimal API** | C# / **.NET 8** / **NewLife Agent（无 ASP.NET）** |
| 默认端口 | 11434 | 11434 |
| 供应商数 | 9 家 | 11 预设（+ ollama 透传） |
| 测试 | 336 xUnit 测试 | 168 项 self-test（+ 11 项端到端冒烟） |
| 许可证 | WTFPL | MIT（见仓库 LICENSE） |
| 热度 | 49★ / 16 fork / 2026-05 创建，持续更新 | 自用 + 开源 |
| 部署 | Docker + bare metal | Windows 服务（单文件 exe）+ 可视化菜单安装 |

**一句话**：它是用 ASP.NET Core 写的、把 9 家大模型 API 统一成「OpenAI 兼容 + Ollama 兼容」双协议、专门喂给 GitHub Copilot 的代理。

---

## 2. 能力逐条对比

| # | 能力 | 竞品 | OllamaHub（我们） | 状态 |
|---|---|---|---|---|
| 1 | 双协议兼容（OpenAI `/v1/*` + Ollama `/api/*`） | ✅ | ✅（我们刚补上 `/v1/chat/completions`、`/v1/models`） | 已对齐 |
| 2 | 供应商覆盖 | 9 家：deepseek、openai、**nvidia、groq、cerebras、zenmux**、ollama cloud、moonshot/kimi、openrouter | 11 预设：9 OpenAI 兼容（deepseek/qwen/kimi/glm/siliconflow/volcengine/hunyuan/modelscope/openrouter）+ **anthropic + gemini** + ollama 透传 | 广度相当、覆盖互补 |
| 3 | **原生 Anthropic / Gemini 适配** | ❌（全部走 OpenAI 兼容上游） | ✅（IUpstreamAdapter 含 anthropic/gemini/google） | **我们领先** |
| 4 | 多模态 / 视觉（图片多部分转换） | ✅ OpenAI↔Ollama 图片转换 | ✅ images→OpenAI/Anthropic/Gemini 三态 | 已对齐 |
| 5 | 工具调用 / Function calling | ✅ `supports_tools` 标记 | ✅ ToolSchemaSanitizer + 流式 tool_calls | 已对齐 |
| 6 | **推理内容多轮缓存与重注入** | ✅ `ReasoningCacheService` 缓存 DeepSeek `reasoning_content` 并在后续轮次重注 | ✅ `ReasoningCache` 按会话前缀指纹缓存并重注（已验证多轮注入） | 已对齐 |
| 7 | **强制参数覆盖 force-mode** | ✅ `override_client_params` 静默纠正客户端参数（如 Kimi 强制 temperature=1.0） | ✅ `OverrideClientParams` + `ApplyModelParams`（已验证覆盖；且 `dropParams` 优先级更高） | 已对齐（我们领先） |
| 8 | **provider/model 三级提示解析** | ✅ `nvidia/qwen3.5-...` 消歧到上游 id | ❌ 按 ModelRegistry 模型名匹配，无前缀语法 | **差距（低成本）** |
| 9 | **诊断响应头 `X-Proxy-*`** | ✅ 每个响应带路由决策头，便于排查 | ✅ `SetDiagnosticHeaders` 输出 `X-Proxy-Requested/Resolved/Provider/Upstream-Mode`（双协议路径均覆盖） | 已对齐 |
| 10 | **/health 健康检查端点** | ✅ | ❌（仅有 `exe self-test` 离线条令） | **差距（低成本）** |
| 11 | 可选 Bearer 鉴权 | ✅ `PROXY_API_KEY` 全端点鉴权 | ❌（SecretProtector 是加密配置密钥，非客户端鉴权） | 差距（按需） |
| 12 | 配置方式 | `.env` + `config/model-selection/*.json` | `settings.json` + **可视化菜单 p/k/c** + **热重载** | **我们领先** |
| 13 | 密钥安全 | `.env` 明文（gitignore） | ✅ `SecretProtector` AES-256 机器绑定加密 | **我们领先** |
| 14 | 热重载 | 启动加载（未提热重载） | ✅ ConfigWatcher 500ms 去抖 | **我们领先** |
| 15 | 部署形态 | Docker + bare metal | Windows 服务（单文件 exe） | 各有侧重 |
| 16 | 性能优化 | HTTP/2 池 256/服务端、零拷贝 SSE | NewLife HttpServer 自有实现 | 各有侧重 |
| 17 | 管理后台 | ❌（靠诊断头） | ✅ Web Admin 面板 | **我们领先** |
| 18 | 免费聚合器/免费模型 | ✅ ZenMux 免费层 | ❌ | 差距（商业模式型） |

---

## 3. 值得参考的功能（分级建议）

### A. 强烈建议 / 必须有（填补真实差距，成本低、价值高）
1. **推理内容多轮缓存与重注入（对应 #6）**
   - 场景：DeepSeek / Kimi 等推理模型做多轮对话时，把 `reasoning_content` 缓存并按会话重注入后续轮次，保持推理连贯性。
   - 契合度：极高——我们主场景就是「国产推理模型 + VS Copilot」，目前只透传不缓存，多轮会丢推理上下文。
   - 成本：中（需会话级缓存，按会话标识关联多轮消息）。
2. **诊断响应头 `X-Proxy-*`（对应 #9）**
   - 场景：用户问「为什么走了这个模型 / 供应商」时，响应头直接给出路由决策，无需翻日志。
   - 成本：低（在响应写入处加几个 header）。支持价值高。
3. **强制参数覆盖 force-mode（对应 #7）**
   - 场景：Copilot / 客户端常发模型不支持的参数（如 Kimi 拒绝 `temperature≠1.0`），代理静默纠正，减少用户报错。
   - 成本：低（在 adapter 构建请求时加 override 规则 + 模型级配置开关）。
4. **provider/model 三级提示解析（对应 #8）**
   - 场景：模型名跨供应商冲突时，用 `provider/model` 前缀消歧（如 `nvidia/qwen3.5-...`）。
   - 成本：低–中（ModelRegistry 扩展匹配逻辑）。

### B. 锦上添花（按需）
5. **`/health` 健康检查端点（对应 #10）** — 便于监控 / 未来容器化。成本：低。
6. **可选 Bearer 鉴权开关（对应 #11）** — 仅当用户把端口暴露出本机（非 localhost）才有意义；当前 localhost 威胁模型下优先级低，但加一个开关成本很低。
7. **qualified model aliases（`model@provider`）** — 与 #4 同源，列出带供应商后缀的别名便于客户端选择。

### C. 暂不需要 / 不适合（与架构定位冲突，勿盲目抄）
8. **重写到 ASP.NET Core** — 我们 deliberately 选 NewLife Agent（无 ASP.NET、单文件、Windows 服务、自有 HttpServer），不值得为对标而重写。
9. **Docker 化** — 当前定位 Windows 服务；若未来要跨平台再考虑。
10. **扩供应商（NVIDIA / Groq / Cerebras / ZenMux）** — 属「扩预设」而非架构参考；可酌情加（尤其 Groq 高速、ZenMux 免费），但非必须。
11. **ZenMux 免费聚合** — 商业模式型差异，非技术必须。

---

## 4. 我们相对竞品的优势（别为了对标而丢掉）

- **原生 Anthropic / Gemini 适配层**（竞品全部 OpenAI 兼容上游，无原生适配）。
- **密钥加密保护 SecretProtector**（竞品明文 `.env`）。
- **可视化菜单 + 热重载 + Windows 服务一键安装**（竞品无菜单、无热重载）。
- **Web Admin 管理面板**（竞品靠诊断头，无面板）。
- **168 项自检 `exe self-test`**。
- **供应商覆盖更广**：qwen / glm / siliconflow / volcengine / hunyuan / modelscope 等国内厂商竞品未覆盖。

---

## 5. 建议立项优先级（仅建议，等确认后再动）

- **P0（强烈建议先做）**：#1 推理缓存、#2 诊断响应头、#3 强制参数覆盖。三者均低成本高价值，且完全契合「国产推理模型 + VS Copilot」主场景。**✅ 已于 2026-08-05 全部落地并通过验证（见第 6 节）。**
- **P1**：#4 provider/model 提示解析、#5 `/health`。
- **P2**：#6 鉴权开关、按需扩供应商。
- **不建议**：#8 / #9 架构重写、Docker 化。

---

## 6. 第二轮对比（P0 落地复评，2026-08-05）

P0 三项（#6 推理缓存、#9 诊断响应头、#7 强制参数）已全部实现并通过验证：**168 项 self-test 全绿 + 11 项端到端冒烟全绿**。结论：

- 竞品 P0 级能力已 **100% 对齐**。其中 force-mode 我们额外有 `dropParams` 优先级保护（竞品无此层）；诊断头我们覆盖**双协议路径**（`/api/chat` 与 `/v1/chat/completions`），而竞品仅 OpenAI 路径。
- 剩余差距集中在 P1 / P2：**#8 provider/model 三级提示解析、#10 `/health`、#11 可选鉴权**。均为低成本、非阻塞，按需推进。
- 相对竞品的既有优势（原生 Anthropic/Gemini 适配、密钥加密、可视化菜单 + 热重载、Web Admin、供应商广度）保持不变。

### 6.1 实现落点（便于回溯）
- **P0-1 推理缓存**：`Core/ReasoningCache.cs` + `OpenAiAdapter` 在 assistant 消息发出 `reasoning_content` + `OllamaHttpServer` 的 `Inject/Store` 调用点。指纹取 `role:content` 并排除 thinking，保证多轮哈希稳定对齐、注入不污染下一轮指纹。
- **P0-2 诊断头**：`OllamaHttpServer.SetDiagnosticHeaders` 在写响应体**之前**设置 `X-Proxy-Requested-Model` / `X-Proxy-Resolved-Model` / `X-Proxy-Provider` / `X-Proxy-Upstream-Mode`。
- **P0-3 强制参数**：`ModelOptions.OverrideClientParams / Temperature / TopP / ReasoningEffort` + `OpenAiAdapter.ApplyModelParams`（`dropParams` 优先级高于覆盖；`num_predict` 视为 `max_tokens` 的客户端别名以免被默认值覆盖）。

### 6.2 验证证据
- **单测**（新增 5 项，全部 PASS；self-test 总数 157 → 168）：`CheckForceModeOverride`、`CheckForceModeFillDefault`、`CheckForceModeDropWins`、`CheckReasoningEffortEmission`、`CheckReasoningCacheInjectStore`。
  - 过程中发现并修复一处回归：`ApplyModelParams` 原会把模型默认 `MaxTokens=4096` 注入 `options.max_tokens`，覆盖客户端用 `num_predict` 表达的意图（表现为旧单测 `num_predict → max_tokens` 失败）；已让 max_tokens 的「客户端已指定」同时识别 `num_predict` 别名。
- **端到端**（mock 上游 + 真实 `dotnet ... --serve`）：`X-Proxy-*` 头在 `/api/chat` 与 `/v1/chat/completions` 均出现；多轮推理次轮上游请求含注入的 `reasoning_content`；force-mode 把客户端 `temperature=0.2` 覆盖为 `1`、`top_p` 覆盖为 `0.5`。11/11 PASS。

---

*注：本报告基于竞品公开 README / ARCHITECTURE.md / CONFIGURATION.md（2026-08-05 抓取）。未读取其源码细节，功能实现描述以文档为准。*
