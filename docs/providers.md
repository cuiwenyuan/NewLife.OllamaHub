# 供应商预设

以下 `baseUrl` 均为 OpenAI 兼容接口的“基址”（程序会自动拼接 `/chat/completions`）。`apiMode` 均为 `openai`。

| id | 名称 | baseUrl | 备注 |
|---|---|---|---|
| `deepseek` | DeepSeek | `https://api.deepseek.com/v1` | 当前模型：`deepseek-flash`（2026-09-10 起为规范名，原生多模态 + 可切换思考，1M 上下文 / 384K 输出）；旧 `deepseek-v4-flash` / `deepseek-v4-pro` / `deepseek-v4-flash-vision-exp` 已退役（Pro 于 2026-09-14 自动回落并停用） |
| `qwen` | 阿里通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | 兼容模式地址；当前模型 `qwen3.8-max` / `qwen3.8-flash` / `qwen3.8-27b` / `qwen3.7-plus` / `qwen3.7-flash`（旧名 `qwen-plus`/`qwen-max`/`qwen2.5-*` 已迭代） |
| `kimi` | Moonshot Kimi | `https://api.moonshot.cn/v1` | 当前模型 `kimi-k3` / `kimi-k2.7-code` / `kimi-k2.6`（旧名 `moonshot-v1-*` 已迭代；K2.8 Preview 仅在 Kimi Code，开放 API 无此 Id） |
| `glm` | 智谱 GLM (BigModel) | `https://open.bigmodel.cn/api/paas/v4` | 当前模型 `glm-5.3` / `glm-5.3-flash` / `glm-5.2` / `glm-5.1` / `glm-4.7` / `glm-4.7-flashx`（旧名 `glm-4-plus/air/flash` 已迭代） |
| `zhipu` | 智谱 GLM (Z.ai) | `https://api.z.ai/v1` | **GLM 双通道镜像**：与 `glm` 同模型集，仅基址不同。两供应商可**同时启用**——`zhipu` 模型 Id 已命名空间化为 `zhipu-glm-5.3` 等（避免与 `glm` 的 `glm-5.3` 在 `/api/tags` 撞车），但通过 `upstreamModel: glm-5.3` 字段仍向上游发送真实模型名。当前模型：`zhipu-glm-5.3` / `zhipu-glm-5.3-flash` / `zhipu-glm-5.2` / `zhipu-glm-5.1` / `zhipu-glm-4.7` / `zhipu-glm-4.7-flashx`（上游真名各对应 `glm-*`）。 |
| `siliconflow` | 硅基流动 | `https://api.siliconflow.cn/v1` | 模型 Id 形如 `deepseek-ai/DeepSeek-V4-Flash-0731`；托管开源模型，平台固定 ID；当前已升到 DeepSeek V4 系列（`DeepSeek-V4-Flash-0731` / `-Vision-Exp` / `-Pro-0813`）、智谱 `zhipuai/GLM-5.3`(+Flash)、`Qwen/Qwen3.8-2.4T-A95B`、`moonshotai/Kimi-K3`；旧 V3/V3.1/R1 已退役（**注意：硅基流动模型 Id 的 owner 大小写在第三方源间有分歧，若 404 请以 `GET /v1/models` 实际返回为准**） |
| `volcengine` | 火山方舟 | `https://ark.cn-beijing.volces.com/api/v3` | 需 ARK 接入点 Id；当前模型 `doubao-seed-2-1-pro/turbo`(260628/260915) / `doubao-seed-2-0-code` / `doubao-seed-evolving`（统一 ID 思考模型，1M；旧名 `doubao-pro-*` 已迭代） |
| `hunyuan` | 腾讯混元 (TokenHub) | `https://tokenhub.tencentmaas.com/v1` | **2026-09 迁移到腾讯云 TokenHub**：当前模型 `hy3`（正式版，256K / 64K 输出，推理型）/ `hy4-preview`（约 1M 上下文）；旧 `api.hunyuan.cloud.tencent.com` 的 `hunyuan-a13b` / `hunyuan-vision-1.5-instruct` 不在 TokenHub 体系 |
| `modelscope` | ModelScope | `https://api-inference.modelscope.cn/v1` | 托管开源模型，平台固定 ID；当前已升到 `Qwen/Qwen3.8-27B` / `Qwen/Qwen3.8-Flash-Next`、DeepSeek V4（`DeepSeek-V4-Flash-0731` / `DeepSeek-V4.1-Flash`）、`ZhipuAI/GLM-5.3`；旧 V3/V3.1 已退役 |
| `openrouter` | OpenRouter | `https://openrouter.ai/api/v1` | 聚合器；当前模型 `openai/gpt-6-astra` / `openai/gpt-5.6` / `anthropic/claude-opus-5` / `anthropic/claude-fable-5.1` / `google/gemini-3.8-flash` / `google/gemini-3.7-flash`（旧 `gpt-4o`/`claude-3.5-sonnet`/`gemini-pro-1.5` 已迭代） |

下列两家走原生协议（非 OpenAI 兼容），通过 `apiMode` 区分：

| id | 名称 | apiMode | baseUrl | 备注 |
|---|---|---|---|---|
| `anthropic` | Anthropic Claude | `anthropic` | `https://api.anthropic.com` | Messages API `/v1/messages`，鉴权头 `x-api-key` + `anthropic-version`；当前模型 `claude-opus-5-5` / `claude-opus-5` / `claude-sonnet-5` / `claude-fable-5-1` / `claude-fable-5` / `claude-haiku-4-5`（旧名 `claude-3.5-*` 已迭代） |
| `gemini` | Google Gemini | `gemini` | `https://generativelanguage.googleapis.com/v1beta` | `streamGenerateContent?alt=sse`，Key 拼在 URL；当前模型 `gemini-3.1-pro-preview` / `gemini-3.8-flash` / `gemini-3.7-flash` / `gemini-3.5-flash-lite`（2.5 系列已迭代为 3.x） |

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
