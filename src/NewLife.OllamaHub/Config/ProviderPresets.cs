using System;
using System.Collections.Generic;
using System.Linq;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Config;

/// <summary>
/// 内置供应商预设（M4：11 家全内置）。
/// 仅描述"基址 + 已知模型"的模板，不含任何 API Key——密钥由 <c>setkey</c> 命令写入。
/// 用途：<c>presets</c> 子命令据此列出全部、或生成开箱即用的 settings.json 脚手架。
/// </summary>
public sealed class ProviderPreset
{
    /// <summary>供应商 Id（与模型 Provider 字段对应，如 deepseek）。</summary>
    public String Id { get; set; } = "";

    /// <summary>展示名（如 DeepSeek）。</summary>
    public String Name { get; set; } = "";

    /// <summary>上游 BaseUrl（OpenAI 兼容基址，程序自动拼接 /chat/completions）。</summary>
    public String BaseUrl { get; set; } = "";

    /// <summary>协议模式，默认 openai。</summary>
    public String ApiMode { get; set; } = "openai";

    /// <summary>该供应商下的已知模型模板（不含密钥）。</summary>
    public List<ModelOptions> Models { get; set; } = new();
}

/// <summary>
/// 11 家国内/海外模型供应商的内置预设目录。
/// 决策来源：项目 2026-08-03 选定"首批 11 家全内置"。列表与 <c>docs/providers.md</c> 保持一致。
/// </summary>
public static class ProviderPresets
{
    /// <summary>全部内置预设（只读）。</summary>
    public static IReadOnlyList<ProviderPreset> All { get; } = Build();

    /// <summary>按 Id 查找预设（不区分大小写）。</summary>
    public static ProviderPreset? Find(String id) =>
        All.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 由若干预设生成一份可直接落盘的 <see cref="HubSettings"/> 脚手架（供应商不含密钥，模型引用对应供应商）。
    /// 用户拿到后用 <c>setkey &lt;providerId&gt; &lt;APIKey&gt;</c> 写入密钥即可。
    /// </summary>
    /// <param name="chosen">要纳入的预设集合（不可为 null）。</param>
    /// <returns>含 providers + models 的配置实例（永不为 null）。</returns>
    public static HubSettings BuildSettings(IEnumerable<ProviderPreset> chosen)
    {
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));

        var settings = new HubSettings { Host = "127.0.0.1", Port = 11434 };
        foreach (var p in chosen)
        {
            settings.Providers.Add(new ProviderOptions
            {
                Id = p.Id,
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiMode = p.ApiMode,
            });
            foreach (var m in p.Models)
                settings.Models.Add(m.Clone());
        }
        return settings;
    }

    private static List<ProviderPreset> Build()
    {
        var list = new List<ProviderPreset>();

        // 1) DeepSeek
        list.Add(new ProviderPreset
        {
            Id = "deepseek",
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com",
            Models =
            {
                // 2026-08 更新：DeepSeek 官方已弃用 deepseek-chat / deepseek-reasoner（2026-07-24 起返回错误），
                // 当前可用模型为 deepseek-v4-flash / deepseek-v4-pro，二者均支持 1M 上下文与 tools。
                M("deepseek-v4-flash", "DeepSeek V4 Flash", "deepseek", ctx: 1000000, max: 384000),
                M("deepseek-v4-pro", "DeepSeek V4 Pro", "deepseek", ctx: 1000000, max: 384000,
                  thinking: true, drop: new() { "temperature", "top_p" }),
            },
        });

        // 2) 阿里通义千问（2026-08 升级：qwen-plus/qwen-max 等旧名已迭代为 qwen3 系列）
        list.Add(new ProviderPreset
        {
            Id = "qwen",
            Name = "通义千问",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Models =
            {
                M("qwen3.8-max", "通义千问 Qwen3.8 Max", "qwen", ctx: 1000000, max: 8192, vision: true),
                M("qwen3.7-plus", "通义千问 Qwen3.7 Plus", "qwen", ctx: 128000, max: 8192),
                M("qwen3.7-flash", "通义千问 Qwen3.7 Flash", "qwen", ctx: 1000000, max: 8192, vision: true),
            },
        });

        // 3) Moonshot Kimi（2026-08 升级：moonshot-v1-* 已迭代为 kimi-k3 / k2.7 / k2.6）
        list.Add(new ProviderPreset
        {
            Id = "kimi",
            Name = "Kimi",
            BaseUrl = "https://api.moonshot.cn/v1",
            Models =
            {
                M("kimi-k3", "Kimi K3", "kimi", ctx: 1000000, max: 8192, vision: true, thinking: true),
                M("kimi-k2.7-code", "Kimi K2.7 Code", "kimi", ctx: 256000, max: 8192, vision: true, thinking: true),
                M("kimi-k2.6", "Kimi K2.6", "kimi", ctx: 256000, max: 8192, vision: true, thinking: true),
            },
        });

        // 4) 智谱 GLM（2026-08 升级：glm-4-plus/air/flash 已迭代为 GLM-5.x / GLM-4.7）
        list.Add(new ProviderPreset
        {
            Id = "glm",
            Name = "智谱 GLM",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            Models =
            {
                M("glm-5.2", "GLM-5.2", "glm", ctx: 1000000, max: 8192),
                M("glm-5.1", "GLM-5.1", "glm", ctx: 200000, max: 8192),
                M("glm-4.7", "GLM-4.7", "glm", ctx: 200000, max: 8192),
                M("glm-4.7-flashx", "GLM-4.7 FlashX", "glm", ctx: 200000, max: 8192),
            },
        });

        // 5) 硅基流动
        list.Add(new ProviderPreset
        {
            Id = "siliconflow",
            Name = "硅基流动",
            BaseUrl = "https://api.siliconflow.cn/v1",
            Models =
            {
                M("deepseek-ai/DeepSeek-V3", "SiliconFlow DeepSeek-V3", "siliconflow", ctx: 128000, max: 8192),
                M("deepseek-ai/DeepSeek-R1", "SiliconFlow DeepSeek-R1", "siliconflow", ctx: 64000, max: 65536,
                  thinking: true, drop: new() { "temperature", "top_p" }),
                M("Qwen/Qwen2.5-72B-Instruct", "SiliconFlow Qwen2.5-72B", "siliconflow", ctx: 131072, max: 8192),
            },
        });

        // 6) 火山方舟（2026-08 升级：doubao-pro-* 已迭代为 doubao-seed-2.1 系列）
        list.Add(new ProviderPreset
        {
            Id = "volcengine",
            Name = "火山方舟",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            Models =
            {
                M("doubao-seed-2-1-pro-260628", "Doubao Seed 2.1 Pro", "volcengine", ctx: 256000, max: 8192),
                M("doubao-seed-2-1-turbo-260628", "Doubao Seed 2.1 Turbo", "volcengine", ctx: 256000, max: 8192),
                M("doubao-seed-2-0-code-preview-260215", "Doubao Seed 2.0 Code", "volcengine", ctx: 256000, max: 8192),
            },
        });

        // 7) 腾讯混元（2026-08 升级：hunyuan-pro/turbo 已迭代为 hunyuan-a13b 等）
        list.Add(new ProviderPreset
        {
            Id = "hunyuan",
            Name = "腾讯混元",
            BaseUrl = "https://api.hunyuan.cloud.tencent.com/v1",
            Models =
            {
                M("hunyuan-a13b", "Hunyuan A13B", "hunyuan", ctx: 224000, max: 8192, thinking: true),
                M("hunyuan-vision-1.5-instruct", "Hunyuan Vision 1.5", "hunyuan", ctx: 24000, max: 8192, vision: true),
            },
        });

        // 8) ModelScope
        list.Add(new ProviderPreset
        {
            Id = "modelscope",
            Name = "ModelScope",
            BaseUrl = "https://api.modelscope.cn/v1",
            Models =
            {
                M("Qwen/Qwen2.5-72B-Instruct", "ModelScope Qwen2.5-72B", "modelscope", ctx: 131072, max: 8192),
                M("deepseek-ai/DeepSeek-V3", "ModelScope DeepSeek-V3", "modelscope", ctx: 128000, max: 8192),
            },
        });

        // 9) OpenRouter
        list.Add(new ProviderPreset
        {
            Id = "openrouter",
            Name = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            Models =
            {
                M("openai/gpt-4o", "GPT-4o", "openrouter", ctx: 128000, max: 16384),
                M("anthropic/claude-3.5-sonnet", "Claude 3.5 Sonnet", "openrouter", ctx: 200000, max: 8192),
                M("google/gemini-pro-1.5", "Gemini Pro 1.5", "openrouter", ctx: 200000, max: 8192),
            },
        });

        // 10) Anthropic Claude（ApiMode=anthropic；2026-08 升级：claude-3.5-* 已迭代为 Claude 5 系列）
        list.Add(new ProviderPreset
        {
            Id = "anthropic",
            Name = "Anthropic Claude",
            BaseUrl = "https://api.anthropic.com",
            ApiMode = "anthropic",
            Models =
            {
                M("claude-opus-5", "Claude Opus 5", "anthropic", ctx: 1000000, max: 16384),
                M("claude-sonnet-5", "Claude Sonnet 5", "anthropic", ctx: 1000000, max: 16384),
                M("claude-haiku-4-5", "Claude Haiku 4.5", "anthropic", ctx: 200000, max: 8192),
            },
        });

        // 11) Google Gemini（ApiMode=gemini；2026-08 升级：gemini-1.5-* 已弃用，2.0-flash 已停服，改用 2.5 系列）
        list.Add(new ProviderPreset
        {
            Id = "gemini",
            Name = "Google Gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ApiMode = "gemini",
            Models =
            {
                M("gemini-2.5-pro", "Gemini 2.5 Pro", "gemini", ctx: 2000000, max: 8192, vision: true),
                M("gemini-2.5-flash", "Gemini 2.5 Flash", "gemini", ctx: 1000000, max: 8192, vision: true),
                M("gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite", "gemini", ctx: 1000000, max: 8192, vision: true),
            },
        });

        // 回填每个模型的 Provider 指向其所属预设，确保脚手架里模型能解析到供应商
        foreach (var p in list)
            foreach (var m in p.Models)
                if (String.IsNullOrEmpty(m.Provider)) m.Provider = p.Id;

        return list;
    }

    /// <summary>构造模型模板的便捷函数。</summary>
    private static ModelOptions M(String id, String display, String family, Int64 ctx, Int32 max,
        Boolean vision = false, Boolean thinking = false, List<String>? drop = null)
        => new ModelOptions
        {
            Id = id,
            DisplayName = display,
            Family = family,
            ContextLength = ctx,
            MaxTokens = max,
            Tools = true,
            Vision = vision,
            Thinking = thinking,
            DropParams = drop ?? new List<String>(),
        };
}
