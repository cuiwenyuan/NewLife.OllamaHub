# 供应商预设

以下 `baseUrl` 均为 OpenAI 兼容接口的“基址”（程序会自动拼接 `/chat/completions`）。`apiMode` 均为 `openai`。

| id | 名称 | baseUrl | 备注 |
|---|---|---|---|
| `deepseek` | DeepSeek | `https://api.deepseek.com/v1` | 当前两个规范名：`deepseek-flash`（= V4.1-Flash-1017，原生多模态 + 可切换思考，1M 上下文 / 384K 输出）与旗舰 `deepseek-v4-pro`（V4-Pro-0813，纯文本深度推理，1M / 384K）；旧 `deepseek-chat` / `deepseek-reasoner` 已于 2026-07-24 停用 |
| `qwen` | 阿里通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | 兼容模式地址；当前模型 `qwen3.8-max` / `qwen3.8-flash` / `qwen3.8-27b` / `qwen3.7-plus` / `qwen3.7-flash`（旧名 `qwen-plus`/`qwen-max`/`qwen2.5-*` 已迭代；Qwen4 系列 2026-11 仅预告、无线上端点，暂不写入） |
| `kimi` | Moonshot Kimi | `https://api.moonshot.cn/v1` | 当前模型 `kimi-k3.1-pro` / `kimi-k3.1-pro-highspeed`（K3 后训练强化版，2.8T/104B，1M，原生视觉，思考常开）/ `kimi-k3` / `kimi-k2.7-code` / `kimi-k2.6`；K4 尚未发布，K2.8 Preview 仅在 Kimi Code |
| `glm` | 智谱 GLM (BigModel) | `https://open.bigmodel.cn/api/paas/v4` | 当前模型 `glm-5.3` / `glm-5.3-flash` / `glm-5.3-flashx`（约 200 tok/s 极速版）/ `glm-5.2` / `glm-5.1` / `glm-4.7` / `glm-4.7-flashx`（GLM-5.4/5.5 仍为路线图，未发布） |
| `zhipu` | 智谱 GLM (Z.ai) | `https://api.z.ai/v1` | **GLM 双通道镜像**：与 `glm` 同模型集，仅基址不同。两供应商可**同时启用**——`zhipu` 模型 Id 已命名空间化为 `zhipu-glm-5.3` 等（避免与 `glm` 的 `glm-5.3` 在 `/api/tags` 撞车），但通过 `upstreamModel: glm-5.3` 字段仍向上游发送真实模型名。当前模型：`zhipu-glm-5.3` / `zhipu-glm-5.3-flash` / `zhipu-glm-5.3-flashx` / `zhipu-glm-5.2` / `zhipu-glm-5.1` / `zhipu-glm-4.7` / `zhipu-glm-4.7-flashx`（上游真名各对应 `glm-*`）。 |
| `siliconflow` | 硅基流动 | `https://api.siliconflow.cn/v1` | 模型 Id 形如 `deepseek-ai/DeepSeek-V4-Flash-0731`；托管开源模型，平台固定 ID；当前 DeepSeek V4 系列（`DeepSeek-V4-Flash-0731` / `-Vision-Exp` / `-Pro-0813`）、智谱 `zhipuai/GLM-5.3`(+Flash)、`Qwen/Qwen3.8-2.4T-A95B`、`moonshotai/Kimi-K3`（K3.1 尚未上架）；旧 V3/V3.1/R1 已退役（**注意：硅基流动模型 Id 的 owner 大小写在第三方源间有分歧，若 404 请以 `GET /v1/models` 实际返回为准**） |
| `volcengine` | 火山方舟 | `https://ark.cn-beijing.volces.com/api/v3` | 需 ARK 接入点 Id；当前模型 `doubao-seed-2-1-pro-260915` / `doubao-seed-2-1-lite-260915`（1024K / 256K，多模态 + 深度思考）/ `doubao-seed-2-1-pro-260628` / `doubao-seed-2-1-turbo-260628`（256K / 256K）/ `doubao-seed-2-0-code` / `doubao-seed-evolving`（统一 ID，1024K / 256K） |
| `hunyuan` | 腾讯混元 (TokenHub) | `https://tokenhub.tencentmaas.com/v1` | **经腾讯云 TokenHub 暴露**：当前模型 `hy4`（正式版，2026-11-04，1M / 64K，首次支持图像理解与电脑操作）/ `hy3`（256K / 64K，推理型）；`hy4-preview` 已被正式版取代。旧 `api.hunyuan.cloud.tencent.com` 的 `hunyuan-a13b` 等不在 TokenHub 体系 |
| `modelscope` | ModelScope | `https://api-inference.modelscope.cn/v1` | 托管开源模型，平台固定 ID；当前 `Qwen/Qwen3.8-27B` / `Qwen/Qwen3.8-Flash-Next`、DeepSeek `DeepSeek-V4-Flash-0731` / `DeepSeek-V4.1-Flash-1017`、`ZhipuAI/GLM-5.3`；旧 V3/V3.1 已退役 |
| `openrouter` | OpenRouter | `https://openrouter.ai/api/v1` | 聚合器；当前模型 `openai/gpt-6-astra` / `openai/gpt-5.6` / `anthropic/claude-fable-5.2` / `anthropic/claude-opus-5.5` / `anthropic/claude-opus-5` / `google/gemini-4-hydrogen` / `google/gemini-4-carbon` / `google/gemini-3.8-flash` / `tencent/hy4` |

下列两家走原生协议（非 OpenAI 兼容），通过 `apiMode` 区分：

| id | 名称 | apiMode | baseUrl | 备注 |
|---|---|---|---|---|
| `anthropic` | Anthropic Claude | `anthropic` | `https://api.anthropic.com` | Messages API `/v1/messages`，鉴权头 `x-api-key` + `anthropic-version`；当前模型 `claude-fable-5-2` / `claude-fable-5-1` / `claude-opus-5-5` / `claude-opus-5` / `claude-sonnet-5-5` / `claude-sonnet-5` / `claude-haiku-5-5` / `claude-haiku-4-5`（旧名 `claude-3.5-*` 已迭代） |
| `gemini` | Google Gemini | `gemini` | `https://generativelanguage.googleapis.com/v1beta` | `streamGenerateContent?alt=sse`，Key 拼在 URL；当前 **Gemini 4 家族** `gemini-4-hydrogen`（取代 3.8 Flash，1M 统一输入输出）/ `gemini-4-carbon` / `gemini-4-argon`，另保留 `gemini-3.8-flash` / `gemini-3.1-pro-preview` |

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
