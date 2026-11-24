# 供应商预设

以下 `baseUrl` 均为 OpenAI 兼容接口的“基址”（程序会自动拼接 `/chat/completions`）。`apiMode` 均为 `openai`。

> **收录口径（2026-11-24 复核）**：只写入**已有线上端点、可直接调用**的模型。已发布但 API 尚未开放（如 GLM-5.4）、
> 仅有泄露/传闻（Claude Opus 5.6、Fable 5.5）、或非对话端点（豆包 Seedance 视频生成）的模型一律不写入，避免用户调用即 404。

| id | 名称 | baseUrl | 备注 |
|---|---|---|---|
| `deepseek` | DeepSeek | `https://api.deepseek.com/v1` | 当前两个规范名：`deepseek-flash`（= V4.1-Flash-1017，原生多模态 + 可切换思考，1M 上下文 / 384K 输出）与旗舰 `deepseek-v4-pro`（V4-Pro-0813，纯文本深度推理，1M / 384K）；旧 `deepseek-chat` / `deepseek-reasoner` 已于 2026-07-24 停用。**2026-11-13 官方新开放的是工具能力**（web search / read_url / 文件输入 / OCR / ASR / TTS），模型名与端点未变 |
| `qwen` | 阿里通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | 兼容模式地址；**新增 `qwen4-max`**（Qwen4 系列首款正式版，2026-11-16 上线，总参 2.4T / 激活 71B，1M 上下文，文本+图像+视频输入，默认开思考 `reasoning_effort` low/high/xhigh，输出上限非思考 131072 / 思考 262144）；另有 `qwen3.8-max` / `qwen3.8-flash` / `qwen3.8-27b` / `qwen3.7-plus` / `qwen3.7-flash`。预览版 `qwen4-max-preview` 仅 Token Plan 且 2026-11-25 下线，不写入 |
| `kimi` | Moonshot Kimi | `https://api.moonshot.cn/v1` | 当前模型 `kimi-k3.1-pro` / `kimi-k3.1-pro-highspeed`（K3 后训练强化版，2.8T/104B，1M，原生视觉，思考常开）/ `kimi-k3` / `kimi-k2.7-code` / `kimi-k2.6`；K4 尚未发布。**K2 系列**：2026-10-31 起对新账号关闭，官方计划 2026-12 底整体下线，仅老账号可用 |
| `glm` | 智谱 GLM (BigModel) | `https://open.bigmodel.cn/api/paas/v4` | 当前模型 `glm-5.3` / `glm-5.3-flash` / `glm-5.3-flashx`（约 200 tok/s 极速版）/ `glm-5.2` / `glm-5.1` / `glm-4.7` / `glm-4.7-flashx`。**GLM-5.4 已于 2026-11-23 发布**（首个 Loop Transformer 架构，750B 基座达 2~3T 级水平，原生视觉 + 泛 Coding），但官方明确「API 将在完成安全评估后开放」，截至 11-24 仅 Coding Plan 与 ZCode/Trae/WorkBuddy/Qoder 可用 → **待 API 开放后补入**。GLM-5.5（约 3T）预计 2026-12 |
| `zhipu` | 智谱 GLM (Z.ai) | `https://api.z.ai/v1` | **GLM 双通道镜像**：与 `glm` 同模型集，仅基址不同。两供应商可**同时启用**——`zhipu` 模型 Id 已命名空间化为 `zhipu-glm-5.3` 等（避免与 `glm` 的 `glm-5.3` 在 `/api/tags` 撞车），但通过 `upstreamModel: glm-5.3` 字段仍向上游发送真实模型名。当前模型：`zhipu-glm-5.3` / `zhipu-glm-5.3-flash` / `zhipu-glm-5.3-flashx` / `zhipu-glm-5.2` / `zhipu-glm-5.1` / `zhipu-glm-4.7` / `zhipu-glm-4.7-flashx`（上游真名各对应 `glm-*`）。GLM-5.4 API 开放后两通道一并补入 |
| `siliconflow` | 硅基流动 | `https://api.siliconflow.cn/v1` | 模型 Id 形如 `deepseek-ai/DeepSeek-V4-Flash-0731`；托管开源模型，平台固定 ID；当前 DeepSeek V4 系列（`DeepSeek-V4-Flash-0731` / `-Vision-Exp` / `-Pro-0813`）、智谱 `zhipuai/GLM-5.3`(+Flash)、`Qwen/Qwen3.8-2.4T-A95B`、**`moonshotai/Kimi-K3.1-Pro`（2026-11-05 起开放权重 Day-0 访问）** 与 `moonshotai/Kimi-K3`；平台 2026-11-16 下线了 Qwen3-Coder-30B / GLM-4.5V 等旧模型（本表未涉及）。**注意：硅基流动模型 Id 的 owner 大小写在第三方源间有分歧，若 404 请以 `GET /v1/models` 实际返回为准** |
| `volcengine` | 火山方舟 | `https://ark.cn-beijing.volces.com/api/v3` | 需 ARK 接入点 Id；豆包对话线 `doubao-seed-2-1-pro-260915` / `doubao-seed-2-1-lite-260915`（1024K / 256K，多模态 + 深度思考）/ `doubao-seed-2-1-pro-260628` / `doubao-seed-2-1-turbo-260628`（256K / 256K）/ `doubao-seed-2-0-code` / `doubao-seed-evolving`（统一 ID，1024K / 256K，2026-11-05 滚动升级强化 Agent 与抗幻觉）；**新增第三方托管 `kimi-k3-1-pro-261024`**（2026-10-24 上架）。2026-11-15 新发的 `doubao-seedance-2-5-flash-261115` 是**视频生成**模型，不走 chat 端点，不写入 |
| `hunyuan` | 腾讯混元 (TokenHub) | `https://tokenhub.tencentmaas.com/v1` | **经腾讯云 TokenHub 暴露**：`hy4`（正式版，2026-11-04，770B，1M / 64K，首次支持图像理解与电脑操作）+ **新增 `hy4-flash`**（2026-11-16，224B 总参 / 12B 激活，1M 上下文，文本+图像，主打文档/检索/幻灯片等高频日常，$0.07/$0.281）+ `hy3`（256K / 64K，推理型）。`hy4-preview` 平台计划 2026-12-07 下线；Hy5 官方口径「今年底明年初」 |
| `modelscope` | ModelScope | `https://api-inference.modelscope.cn/v1` | 托管开源模型，平台固定 ID；当前 `Qwen/Qwen3.8-27B` / `Qwen/Qwen3.8-Flash-Next`、DeepSeek `DeepSeek-V4-Flash-0731` / `DeepSeek-V4.1-Flash-1017`、`ZhipuAI/GLM-5.3`；旧 V3/V3.1 已退役。`moonshotai/Kimi-K3.1-Pro` 在魔搭目前只有权重模型页，推理 API 需 `:DashScope` 后缀，暂不写入 |
| `openrouter` | OpenRouter | `https://openrouter.ai/api/v1` | 聚合器；**新增 `openai/gpt-6.2-astra` / `openai/gpt-6.2-sol`（2026-11-17，均 1.05M 上下文 / 128K 输出）、`google/gemini-4-neon`、`tencent/hy4-flash`**；另含 `openai/gpt-6-astra` / `openai/gpt-5.6` / `anthropic/claude-fable-5.2` / `anthropic/claude-opus-5.5` / `anthropic/claude-opus-5` / `google/gemini-4-hydrogen` / `google/gemini-4-carbon` / `google/gemini-3.8-flash` / `tencent/hy4` |

下列两家走原生协议（非 OpenAI 兼容），通过 `apiMode` 区分：

| id | 名称 | apiMode | baseUrl | 备注 |
|---|---|---|---|---|
| `anthropic` | Anthropic Claude | `anthropic` | `https://api.anthropic.com` | Messages API `/v1/messages`，鉴权头 `x-api-key` + `anthropic-version`；当前最新正式版 **`claude-fable-5-2`**（2026-11-09，$8/$40），另有 `claude-fable-5-1` / `claude-opus-5-5`（9-22）/ `claude-opus-5` / `claude-sonnet-5-5`（9-28）/ `claude-sonnet-5` / `claude-haiku-5-5`（10-07）/ `claude-haiku-4-5`。传闻中的 Opus 5.6 与 Fable 5.5 截至 11-24 无官方 model ID，不写入 |
| `gemini` | Google Gemini | `gemini` | `https://generativelanguage.googleapis.com/v1beta` | `streamGenerateContent?alt=sse`，Key 拼在 URL；当前 **Gemini 4 家族**：**`gemini-4-neon`（2026-11-18 GA，1M 统一上下文，thinking_level 四档 low/medium/high(默认)/xhigh，输出上限 65,536，$3/$15，无免费额度）** / `gemini-4-hydrogen`（11-12，取代 3.8 Flash，$1.50/$7.50，有免费额度）/ `gemini-4-carbon`（11-10，最强长程推理，非流式输出上限 32,768）/ `gemini-4-argon`（App 侧已被 Neon 取代，API 仍可用），另保留 `gemini-3.8-flash` / `gemini-3.1-pro-preview`。**定价提醒**：Carbon / Argon 首发价 $2/$10 仅到 2026-11-30，12-01 起涨至 $4/$20；Neon 与 Hydrogen 不涨价 |

完整可复制的样例见仓库 `examples/settings.*.json`（含 `settings.anthropic.json`、`settings.gemini.json`、`settings.vision.json`、`settings.newmodels.json`、`settings.qwen-kimi-glm.json`）与根目录 `settings.sample.json`。

> 想要“只跑 DeepSeek”等精简配置？复制 `examples/settings.deepseek.json` 即可。

## 用 `presets` 命令一键生成

不想手写？程序内置这 12 家预设，可用 `presets` 子命令直接生成配置脚手架：

```bat
NewLife.OllamaHub.exe presets            # 列出全部 12 家（id / 名称 / baseUrl）
NewLife.OllamaHub.exe presets deepseek  # 输出含 deepseek 供应商与已知模型的 settings.json（不含密钥）
NewLife.OllamaHub.exe presets deepseek qwen kimi  # 可一次叠多家
NewLife.OllamaHub.exe presets deepseek --write    # 直接写入程序目录 settings.json（已存在则拒绝，--force 强制覆盖）
```

生成后运行 `setkey <providerId> <APIKey>` 写入密钥即为可用配置。详见[配置参考](configuration.md#内置供应商预设presets-命令)。
