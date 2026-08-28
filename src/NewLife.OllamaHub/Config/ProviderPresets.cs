using System;
using System.Collections.Generic;
using System.Linq;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Config;

/// <summary>
/// 内置供应商预设（M4：12 家全内置，含 GLM 双供应商 BigModel + Z.ai）。
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
/// 12 家国内/海外模型供应商的内置预设目录（含 GLM 双供应商 BigModel + Z.ai）。
/// 决策来源：项目 2026-08-03 选定"首批 11 家全内置"，2026-08 增补 Z.ai 镜像 GLM 预设。列表与 <c>docs/providers.md</c> 保持一致。
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

        var settings = new HubSettings();
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
            BaseUrl = "https://api.deepseek.com/v1",
            Models =
            {
                // 2026-08 更新：DeepSeek 官方已弃用 deepseek-chat / deepseek-reasoner（2026-07-24 起返回错误），
                // 当前可用模型为 deepseek-v4-flash / deepseek-v4-pro，二者均支持 1M 上下文与 tools。
                M("deepseek-v4-flash", "DeepSeek V4 Flash", "deepseek", ctx: 1000000, max: 384000),
                M("deepseek-v4-pro", "DeepSeek V4 Pro", "deepseek", ctx: 1000000, max: 384000,
                  thinking: true, drop: new() { "temperature", "top_p" }),
                // 2026-08 新增：实验性多模态（图生文），vision 支持；属实验模型，可能随官方迭代改名。
                M("deepseek-v4-flash-vision-exp", "DeepSeek V4 Flash Vision (Exp)", "deepseek", ctx: 1000000, max: 384000, vision: true),
            },
        });

        // 2) 阿里通义千问（2026-08 升级：qwen-plus/qwen-max 等旧名已迭代为 qwen3 系列；2026-08-26 新增 qwen3.8-flash / qwen3.8-27b）
        list.Add(new ProviderPreset
        {
            Id = "qwen",
            Name = "通义千问",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Models =
            {
                M("qwen3.8-max", "通义千问 Qwen3.8 Max", "qwen", ctx: 1000000, max: 8192, vision: true),
                M("qwen3.8-flash", "通义千问 Qwen3.8 Flash", "qwen", ctx: 1000000, max: 8192, vision: true),
                M("qwen3.8-27b", "通义千问 Qwen3.8 27B", "qwen", ctx: 1000000, max: 8192, vision: true),
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

        // 4) 智谱 GLM（2026-08 升级：glm-4-plus/air/flash 已迭代为 GLM-5.x / GLM-4.7；2026-08-14/26 新增 GLM-5.3 / GLM-5.3-Flash）
        // BigModel 通道（open.bigmodel.cn）—— 默认 GLM 供应商，沿用历史 baseUrl 不破坏老配置。
        list.Add(new ProviderPreset
        {
            Id = "glm",
            Name = "智谱 GLM (BigModel)",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            Models =
            {
                M("glm-5.3", "GLM-5.3", "glm", ctx: 1000000, max: 8192, vision: true),
                M("glm-5.3-flash", "GLM-5.3 Flash", "glm", ctx: 1000000, max: 8192, vision: true, thinking: true),
                M("glm-5.2", "GLM-5.2", "glm", ctx: 1000000, max: 8192),
                M("glm-5.1", "GLM-5.1", "glm", ctx: 200000, max: 8192),
                M("glm-4.7", "GLM-4.7", "glm", ctx: 200000, max: 8192),
                M("glm-4.7-flashx", "GLM-4.7 FlashX", "glm", ctx: 200000, max: 8192),
            },
        });

        // 4b) 智谱 GLM (Z.ai) 镜像通道——与 BigModel 同模型集，仅 baseUrl 不同（api.z.ai/v1，OpenAI 兼容）。
        // 用户可按需二选一：若 BigModel 通道需企业密钥/配额，可改用 Z.ai 个人令牌；两供应商 Id 不同，可同时启用互不冲突。
        list.Add(new ProviderPreset
        {
            Id = "zhipu",
            Name = "智谱 GLM (Z.ai)",
            BaseUrl = "https://api.z.ai/v1",
            Models =
            {
                M("glm-5.3", "GLM-5.3 (Z.ai)", "glm", ctx: 1000000, max: 8192, vision: true),
                M("glm-5.3-flash", "GLM-5.3 Flash (Z.ai)", "glm", ctx: 1000000, max: 8192, vision: true, thinking: true),
                M("glm-5.2", "GLM-5.2 (Z.ai)", "glm", ctx: 1000000, max: 8192),
                M("glm-5.1", "GLM-5.1 (Z.ai)", "glm", ctx: 200000, max: 8192),
                M("glm-4.7", "GLM-4.7 (Z.ai)", "glm", ctx: 200000, max: 8192),
                M("glm-4.7-flashx", "GLM-4.7 FlashX (Z.ai)", "glm", ctx: 200000, max: 8192),
            },
        });

        // 5) 硅基流动（2026-08 升级：新增 Qwen3-235B、DeepSeek-V3.1；保留既有 DeepSeek-V3/R1、Qwen2.5-72B）
        list.Add(new ProviderPreset
        {
            Id = "siliconflow",
            Name = "硅基流动",
            BaseUrl = "https://api.siliconflow.cn/v1",
            Models =
            {
                M("deepseek-ai/DeepSeek-V3", "SiliconFlow DeepSeek-V3", "siliconflow", ctx: 128000, max: 8192),
                M("deepseek-ai/DeepSeek-V3.1", "SiliconFlow DeepSeek-V3.1", "siliconflow", ctx: 128000, max: 8192),
                M("deepseek-ai/DeepSeek-R1", "SiliconFlow DeepSeek-R1", "siliconflow", ctx: 64000, max: 65536,
                  thinking: true, drop: new() { "temperature", "top_p" }),
                M("Qwen/Qwen2.5-72B-Instruct", "SiliconFlow Qwen2.5-72B", "siliconflow", ctx: 131072, max: 8192),
                M("Qwen/Qwen3-235B-A22B-Instruct", "SiliconFlow Qwen3-235B", "siliconflow", ctx: 128000, max: 8192),
            },
        });

        // 6) 火山方舟（2026-08 升级：doubao-pro-* 已迭代为 doubao-seed-2.1 系列；新增统一 ID 思考模型 doubao-seed-evolving）
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
                // 统一 ID 思考模型：平台自动升级底层版本，1M 上下文，推理型。
                M("doubao-seed-evolving", "Doubao Seed Evolving", "volcengine", ctx: 1000000, max: 8192, thinking: true),
            },
        });

        // 7) 腾讯混元（2026-08 升级：hunyuan-pro/turbo 已迭代为 hunyuan-a13b；2026-08 开源 Hy4 系列）
        list.Add(new ProviderPreset
        {
            Id = "hunyuan",
            Name = "腾讯混元",
            BaseUrl = "https://api.hunyuan.cloud.tencent.com/v1",
            Models =
            {
                M("hunyuan-a13b", "Hunyuan A13B", "hunyuan", ctx: 224000, max: 8192, thinking: true),
                M("hunyuan-vision-1.5-instruct", "Hunyuan Vision 1.5", "hunyuan", ctx: 24000, max: 8192, vision: true),
                // 2026-08 开源 Hy4（770B/49B/1M）；model id 经腾讯云 TokenHub/OpenRouter 暴露，具体 slug 以官方为准（此处以 hy4-preview 占位）。
                M("hunyuan-hy4-preview", "Hunyuan Hy4 Preview", "hunyuan", ctx: 1000000, max: 8192, thinking: true),
            },
        });

        // 8) ModelScope（2026-08 修正：API-Inference 基址为 api-inference.modelscope.cn/v1；新增 Qwen3-235B、DeepSeek-V3.1）
        list.Add(new ProviderPreset
        {
            Id = "modelscope",
            Name = "ModelScope",
            BaseUrl = "https://api-inference.modelscope.cn/v1",
            Models =
            {
                M("Qwen/Qwen2.5-72B-Instruct", "ModelScope Qwen2.5-72B", "modelscope", ctx: 131072, max: 8192),
                M("Qwen/Qwen3-235B-A22B-Instruct", "ModelScope Qwen3-235B", "modelscope", ctx: 128000, max: 8192),
                M("deepseek-ai/DeepSeek-V3", "ModelScope DeepSeek-V3", "modelscope", ctx: 128000, max: 8192),
                M("deepseek-ai/DeepSeek-V3.1", "ModelScope DeepSeek-V3.1", "modelscope", ctx: 128000, max: 8192),
            },
        });

        // 9) OpenRouter（聚合器，2026-08 升级：gpt-4o / claude-3.5-sonnet / gemini-pro-1.5 已迭代为 5.x / 3.x 系列）
        list.Add(new ProviderPreset
        {
            Id = "openrouter",
            Name = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            Models =
            {
                M("openai/gpt-5.6", "GPT-5.6", "openrouter", ctx: 256000, max: 16384),
                M("anthropic/claude-opus-5", "Claude Opus 5", "openrouter", ctx: 1000000, max: 16384),
                M("google/gemini-3.7-flash", "Gemini 3.7 Flash", "openrouter", ctx: 1000000, max: 8192, vision: true),
            },
        });

        // 10) Anthropic Claude（ApiMode=anthropic；2026-08 升级：claude-3.5-* 已迭代为 Claude 5 系列；新增 Fable 5）
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
                M("claude-fable-5", "Claude Fable 5", "anthropic", ctx: 1000000, max: 16384, vision: true),
                M("claude-haiku-4-5", "Claude Haiku 4.5", "anthropic", ctx: 200000, max: 8192),
            },
        });

        // 11) Google Gemini（ApiMode=gemini；2026-08 升级：2.5 系列已迭代为 3.x 系列）
        list.Add(new ProviderPreset
        {
            Id = "gemini",
            Name = "Google Gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ApiMode = "gemini",
            Models =
            {
                M("gemini-3.1-pro-preview", "Gemini 3.1 Pro", "gemini", ctx: 2000000, max: 8192, vision: true),
                M("gemini-3.7-flash", "Gemini 3.7 Flash", "gemini", ctx: 1000000, max: 8192, vision: true),
                M("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", "gemini", ctx: 1000000, max: 8192, vision: true),
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
