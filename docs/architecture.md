# 架构原理

## 为什么不需要 IDE 插件

Visual Studio 2026（18.6.0+）与 VS Code（Copilot Chat 0.41+）的 Copilot Chat 内置 **Ollama 提供程序**：在模型下拉 → 管理模型 → 提供程序选 Ollama，端点填 `http://localhost:11434`，IDE 就会把该端点当作本地 Ollama 拉取模型列表。

因此只要本机有一个“长得像 Ollama”的 HTTP 服务，Copilot 会自动把配置好的模型列出来，并在对话时把请求发过来。NewLife.OllamaHub 就是这个伪装服务。

> 已知边界：BYOM 只作用于 **Chat / Agent / Plan**，不覆盖行内代码补全（ghost text），补全仍走 GitHub 官方模型。

## 请求链路

```
Visual Studio / VS Code  Copilot Chat
        │  以为在和本地 Ollama 说话
        ▼
┌──────────────────────────────────────────────┐
│  NewLife.OllamaHub  (127.0.0.1:11434)         │
│  HttpServer (NewLife.Core)                    │
│   ① Ollama 兼容面                              │
│      GET  /            GET /api/version       │
│      GET  /api/tags    POST /api/show         │
│      GET  /api/ps      POST /api/chat         │
│      POST /api/generate  POST /api/embed      │
│                          ↕                    │
│   ② 协议转换 OpenAiAdapter                     │
│      Ollama NDJSON ⇄ OpenAI SSE               │
│      tools/tool_calls 映射                     │
│      reasoning_content ⇄ thinking             │
│      JSON Schema 清洗                          │
│      统一上游适配层 IUpstreamAdapter（按 apiMode 翻译） │
│                          ↕                    │
│   ③ UpstreamClient (BCL HttpClient)            │
│      openai | anthropic | gemini | ollama 透传  │
│                          ↕                    │
│   ④ 配置 & 密钥 (settings.json + 本地 AES 密文) │
│   ⑤ 宿主 NewLife.Agent → Windows 服务         │
└──────────────────────────────────────────────┘
        │
        ▼
  DeepSeek / 通义 / Kimi / GLM / 硅基流动 / 火山方舟 / 混元 / ModelScope / OpenRouter / Anthropic Claude / Google Gemini
```

## 技术栈（纯 NewLife 生态）

| 用途 | 选型 |
|---|---|
| HTTP 服务端 | `NewLife.Core.HttpServer` |
| 服务化 | `NewLife.Agent.ServiceBase` |
| JSON | `NewLife.Serialization.JsonHelper` |
| 日志 | `XTrace`（自动落 `Log/`） |
| 配置 | `settings.json` + `JsonHelper` |
| 调用上游 | BCL `System.Net.Http.HttpClient`（框架自带，非第三方 NuGet） |

## 关键端点

| 端点 | 作用 | 是否必须 |
|---|---|---|
| `GET /api/tags` | 列出模型（Copilot 据此显示可选模型） | ✅ |
| `POST /api/show` | 返回模型能力（`capabilities` 必须含 `tools`） | ✅ |
| `POST /api/chat` | 对话补全（支持流式 NDJSON） | ✅ |
| `POST /api/generate` | 文本生成（可选，便于 CLI/自测） | ◻️ |
| `POST /api/embed` | 向量化（可选） | ◻️ |
| 其余 `/api/pull` 等 | 返回桩响应 | ◻️ |
