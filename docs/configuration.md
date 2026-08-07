# 配置参考（settings.json）

复制到 `settings.json` 后修改。字段全部可选，缺省使用括号内默认值。

```jsonc
{
  "local": {                          // 本机明文 HTTP 监听（默认启用）
    "enabled": true,
    "host": "127.0.0.1",              // 仅本机可连
    "port": 11434
  },
  "lanHttps": {                       // 局域网 HTTPS 监听（默认禁用）
    "enabled": false,
    "host": "0.0.0.0",                // 面向局域网
    "port": 11435,
    "certificate": "hub.pfx",         // PFX 路径（相对 settings.json 目录或绝对路径）
    "certPassword": "证书密码（如有）"
  },
  "logging": { "level": "Info", "retentionDays": 7 },
  "aggregateLocalOllama": false,           // 是否聚合本机真实 Ollama 的模型
  "localOllamaBaseUrl": "http://127.0.0.1:11434",
  "upgradeUrl": "",                        // 可选：版本清单地址（覆盖 upgrade 命令默认 GitHub Release）
  "providers": [ /* 见 providers.md */ ],
  "models": [ /* 见下 */ ]
}
```

## 双监听：本地 HTTP + 局域网 HTTPS

Hub 把监听拆成两个**独立、可并存、可各自启停**的节点：

- **`local`**：明文 HTTP，默认 `127.0.0.1:11434`，仅本机使用（VS Copilot 本机接入走这里）。默认启用。
- **`lanHttps`**：TLS HTTPS，默认 `0.0.0.0:11435`，面向局域网。VS / VS Code 的 Copilot 对**非 localhost 地址强制要求 HTTPS**，局域网接入必须启用它并配置证书。默认禁用。

两者可**同时在线**，亦可只开其一；配置变更经热重载即时生效（无需重启进程）。

```jsonc
{
  "local": { "enabled": true, "host": "127.0.0.1", "port": 11434 },
  "lanHttps": {
    "enabled": true,                  // 启用局域网 HTTPS
    "host": "0.0.0.0",
    "port": 11435,
    "certificate": "hub.pfx",         // PFX 路径（相对 settings.json 目录或绝对路径）
    "certPassword": "证书密码（如有）"
  }
}
```

- HTTPS 监听复用与主端口完全相同的路由（`/api/*`、`/v1/*`、`/admin`）。
- 证书须为 **PFX** 格式，且**必须被 VS 所在机器信任**：自签证书需手动导入该系统/浏览器的“受信任的根证书颁发机构”，否则 VS 仍会拒绝连接。
- 生成自签证书（PowerShell）：
  ```powershell
  $c = New-SelfSignedCertificate -DnsName "localhost","<服务器IP>" -CertStoreLocation "Cert:\CurrentUser\My"
  Export-PfxCertificate -Cert $c -FilePath hub.pfx -Password (ConvertTo-SecureString -String "密码" -AsPlainText -Force)
  ```
  也可用 `mkcert <服务器IP>` 生成受信任证书（配合下方反向代理方案）。
- 安全提醒：Hub **不带鉴权**，暴露到局域网即等同把上游 API Key 暴露给同网段任何人。仅限可信网络 / VPN 使用，并妥善保管 Key。

> **临时方案（不想启用原生 HTTPS 时）**：在服务器前置 Caddy（`caddy reverse_proxy localhost:11434` + 自动 HTTPS），VS 填 `https://<服务器IP>:11435`，客户端执行 `caddy trust` 信任其根证书即可。详见 Issue #1。

## providers[]

| 字段 | 说明 |
|---|---|
| `id` | 唯一标识，模型通过 `provider` 引用 |
| `name` | 展示名 |
| `baseUrl` | 上游 BaseUrl（到 `/v1` 或 `/v1/chat/completions` 之前的部分） |
| `apiMode` | `openai`（默认）/ `anthropic` / `gemini` / `ollama`（透传 `/api/chat`）/ `responses`（OpenAI Responses API） |
| `apiKey` | 明文 Key（仅开发；生产请用 `setkey` 加密或环境变量） |
| `protectedApiKey` | `setkey` 命令写入的 `dpapi:` 本地 AES 密文，或 `env:NAME` 环境变量引用 |
| `headers` | 固定请求头，**务必含 `Content-Type: application/json`**（避免上游 415） |

## models[]

| 字段 | 说明 |
|---|---|
| `id` | 模型 Id（Copilot 展示与引用） |
| `ownedBy` / `displayName` | 归属与展示名 |
| `family` | 模型族，写入 `/api/show` 的 `<family>.context_length` |
| `provider` | 引用的 provider `id` |
| `contextLength` | 上下文长度（token），Copilot 据此截断 |
| `maxTokens` | 单次最大生成 |
| `tools` | 是否支持工具调用（Agent 模式必须 `true`） |
| `vision` | 是否视觉 |
| `thinking` | 是否推理模型（`reasoning_content` ⇄ `thinking`） |
| `includeReasoningInRequest` | 回传上游时是否带推理内容（DeepSeek 设 `false`） |
| `dropParams` | 向上游丢弃的参数（如 `["temperature","top_p"]`） |
| `headers` | 模型级请求头 |

> Key 解析支持三种形式（优先级从高到低）：
> - `apiKey` 明文（开发便利，生产不建议提交到仓库）
> - `protectedApiKey = "env:NAME"`：运行时读取名为 `NAME` 的环境变量（如 `env:NHUB_KEY_DEEPSEEK`，可用 `NHUB_KEY_<PROVIDER>` 命名约定）
> - `protectedApiKey = "dpapi:<base64>"`：本地 AES-256（CBC，随机 IV 作熵）密文，密钥由固定应用盐 + 机器名派生，**本机绑定**（仅本机可解密，语义等同 DPAPI LocalMachine），不引入任何第三方 NuGet 依赖
>
> `dpapi:` 前缀只是历史命名；实际并未使用 Windows DPAPI，而是 BCL `System.Security.Cryptography.AES`，以保证纯 NewLife 生态 / 零 NuGet 依赖。`setkey` 命令负责写入这种密文（见 `docs/security.md` 思路与下文命令说明）。

## 上游协议模式（apiMode）

Hub 内置统一适配层，把不同上游响应翻译成 Ollama 兼容帧。通过 `provider.apiMode` 选择：

| apiMode | 上游协议 | 关键差异（Hub 已自动处理） |
|---|---|---|
| `openai` | OpenAI / 兼容 `/chat/completions`（DeepSeek、Qwen、Kimi、GLM、硅基、火山、混元、ModelScope、OpenRouter 等） | 默认模式；请求体为 OpenAI 形状，SSE 块为 `data: {"choices":[...]}` |
| `responses` | OpenAI Responses API `/responses` | 请求体为 Responses 形状（`input` items + `max_output_tokens` + `reasoning.effort`）；SSE 为 `event: response.*` 事件块（`output_text.delta` / `reasoning_text.delta` / `reasoning_summary_text.delta` / `function_call_arguments.delta`）；`reasoning_text`/`reasoning_summary_text` ↦ `thinking`；事件名与 `input`/`tools` 结构对齐官方协议。适合需要 o 系列原生 items/工具流或原生 Responses 端点的场景；普通推理模型用 `openai` 模式即可 |
| `anthropic` | Anthropic Messages API `/v1/messages` | `system` 必须顶层字段；鉴权头 `x-api-key` + `anthropic-version: 2023-06-01`；SSE 为 `event: xxx\n data: {...}` 事件块（`message_start`/`content_block_*`/`message_delta`/`message_stop`）；`tool_use` 块 ↔ Ollama `tool_calls`；思考走 `thinking_delta` |
| `gemini` | Google Gemini `/models/{model}:streamGenerateContent?alt=sse` | API Key 拼在 URL `?key=`；`systemInstruction` 顶层；`functionCall`/`functionResponse` ↔ 工具；`thought:true` 的 part ↦ `reasoning_content`（思考）；SSE 每行为一完整 `GenerateContentResponse` |
| `ollama` | 真实 Ollama `/api/chat` | 原样透传请求与 NDJSON 响应，用于聚合本机 Ollama |

> 未知 `apiMode` 会安全回落到 `openai` 并记录告警，不会中断服务。

示例：用 OpenAI Responses API 接入一个 o 系列模型

```jsonc
{
  "providers": [
    {
      "id": "openai-responses",
      "name": "OpenAI Responses",
      "baseUrl": "https://api.openai.com/v1",
      "apiMode": "responses",
      "apiKey": "sk-..."
    }
  ],
  "models": [
    {
      "id": "o3",
      "provider": "openai-responses",
      "thinking": true,
      "reasoningEffort": "medium"   // 映射到 Responses 的 reasoning.effort
    }
  ]
}
```

## 多模态（图像）透传

当请求消息带 `images` 字段（Ollama 原生格式，base64 原文或 `data:image/png;base64,...` URI）时，Hub 会自动转换为对应上游的图像块：

- **openai / 兼容**：`content` 变为数组，插入 `{ "type": "image_url", "image_url": { "url": "data:image/png;base64,..." } }`。
- **anthropic**：插入 `image` 块，`source: { "type": "base64", "media_type": "image/png", "data": "..." }`。
- **gemini**：插入 `inline_data: { "mime_type": "image/png", "data": "..." }` 到 `parts`。

视觉模型请把 `models[].vision` 设为 `true`（见 `examples/settings.vision.json` 的 `qwen-vl-max` 示例）。

## 聚合本机 Ollama

当 `aggregateLocalOllama = true` 时，若配置里没有已有的 `apiMode=ollama` 供应商，Hub 会自动注册一个 `local-ollama` 供应商，指向 `localOllamaBaseUrl`（默认 `http://127.0.0.1:11434`）。该供应商以透传方式把 `/api/chat` 转给真实 Ollama，使其模型自动出现在 Copilot 模型列表中——**无需任何额外配置**。

## 配置热重载

`settings.json` 变更**无需重启进程**即可生效：Hub 通过 `FileSystemWatcher` 监视文件，去抖（500ms）后自动重新加载注册表；变更若涉及监听地址，还会**自动重建监听套接字**。

- **即时生效（不重启，含监听节点）**：新增/移除模型、增删供应商、轮换 API Key（`setkey` 改写文件即触发）、切换 `aggregateLocalOllama`，以及**修改任一监听节点的 `enabled` / `host` / `port` / `certificate`**——变更会停止对应旧套接字、按新配置重建（仅该监听受影响，另一监听不中断），Copilot/浏览器切换到新端口即可，不必停服务。
- **失败回退**：若新监听（端口被占用等）启动失败，Hub 会跳过该监听并继续服务其它监听，同时记录错误日志；需检查端口占用或重启进程。
- **`_settings` 同步**：重载会整体替换配置对象，Hub 内部会同步到最新实例，因此 `/api/status` 等读取的 `listeners` / `aggregateLocalOllama` 始终反映最新值。

> 典型用法：想临时加一个模型调试，直接改 `settings.json` 保存，Copilot 切回对话即可看到新模型，不必停服务；改 `port` 后浏览器/IDE 改用新端口即可，同样无需重启。

## 内置供应商预设（presets 命令）

不想手写 `providers`/`models`？用内置 `presets` 子命令一键生成脚手架（11 家全内置，见 `docs/providers.md`）：

```bat
NewLife.OllamaHub.exe presets                 # 列出全部 11 家（id / 名称 / baseUrl）
NewLife.OllamaHub.exe presets deepseek       # 输出含 deepseek 供应商与已知模型的 settings.json（不含密钥）
NewLife.OllamaHub.exe presets deepseek --write       # 直接写入程序目录 settings.json（已存在则拒绝）
NewLife.OllamaHub.exe presets deepseek --write --force  # 强制覆盖
```

生成后运行 `setkey <providerId> <APIKey>` 写入密钥即为可用配置。

> **不想敲命令行？** 直接右键 `NewLife.OllamaHub.exe` → 以管理员身份运行，在交互菜单里按 `p` 生成预设、按 `k` 配置密钥、或按 `c` 走配置向导，效果与下面命令完全一致（详见 `docs/install-as-service.md` 的「配置大模型」一节）。

## Web 管理面板

Hub 内置一个零依赖的只读管理面板，方便查看运行状态与用量：

| 端点 | 说明 |
|---|---|
| `GET /api/status` | 返回 JSON 状态（见下），供面板/监控拉取 |
| `GET /admin` | 内置 HTML 面板（卡片 + 供应商/模型/用量表，每 10s 自动刷新，无任何外部依赖） |

`/api/status` 返回字段：

```jsonc
{
  "name": "NewLife.OllamaHub",
  "version": "1.0.0.0",
  "uptimeSeconds": 123,
  "listeners": [
    { "name": "local", "scheme": "http", "enabled": true, "url": "http://127.0.0.1:11434", "bound": true },
    { "name": "lanHttps", "scheme": "https", "enabled": false, "url": "https://0.0.0.0:11435", "bound": false }
  ],
  "aggregateLocalOllama": false,
  "totalRequests": 0,
  "totalErrors": 0,
  "providers": [
    { "id": "deepseek", "name": "DeepSeek", "apiMode": "openai",
      "baseUrl": "https://api.deepseek.com/v1", "hasKey": true }
  ],
  "models": [
    { "id": "deepseek-v4-flash", "displayName": "DeepSeek V4 Flash", "family": "deepseek",
      "provider": "deepseek", "tools": true, "vision": false, "thinking": false,
      "requests": 0, "errors": 0, "promptTokens": 0, "completionTokens": 0, "lastError": "" }
  ]
}
```

> 用浏览器打开 `http://127.0.0.1:11434/admin` 即可看到面板（监听端点由 `local` / `lanHttps` 节点决定，默认本机 `11434`）。
