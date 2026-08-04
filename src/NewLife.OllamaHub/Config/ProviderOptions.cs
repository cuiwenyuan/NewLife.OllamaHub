using System.Collections.Generic;

namespace NewLife.OllamaHub.Config;

/// <summary>上游供应商配置。</summary>
public class ProviderOptions
{
    /// <summary>供应商唯一标识，模型通过 Provider 字段引用它。</summary>
    public String Id { get; set; } = "";

    /// <summary>展示名（可空，默认等于 Id）。</summary>
    public String? Name { get; set; }

    /// <summary>上游 BaseUrl，例如 https://api.deepseek.com 。</summary>
    public String BaseUrl { get; set; } = "";

    /// <summary>协议模式：openai（默认）/ ollama（透传 /api/chat）。</summary>
    public String ApiMode { get; set; } = "openai";

    /// <summary>API Key 明文（仅开发用，生产请用 ProtectedApiKey 或环境变量）。</summary>
    public String? ApiKey { get; set; }

    /// <summary>经 SecretProtector 加密后的 Key（NewLife ProtectedKey 固定实例密钥，对应 dpapi: 前缀存储串）；或为 env:NAME 形式的环境变量注入。</summary>
    public String? ProtectedApiKey { get; set; }

    /// <summary>固定请求头，例如 Content-Type: application/json（避免上游 415）。</summary>
    public Dictionary<String, String> Headers { get; set; } = new();

    /// <summary>供应商级额外参数（如 organization、apiVersion 等）。</summary>
    public Dictionary<String, Object> Extra { get; set; } = new();
}
