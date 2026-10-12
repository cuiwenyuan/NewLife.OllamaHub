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
        // 2026-09 升级：官方于 2026-09-10 将 V4 Flash / V4 Flash Vision Exp 统一为规范名 deepseek-flash（原生多模态 + 可切换思考），
        // deepseek-v4-pro 于 2026-09-14 起自动回落到 V4.1 Flash 并退役——故直接切换为 deepseek-flash，不再保留 v4 别名。
        list.Add(new ProviderPreset
        {
            Id = "deepseek",
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Models =
            {
                // 规范名 deepseek-flash：1M 上下文 / 384K 输出，原生视觉理解，思考默认可开关。
                M("deepseek-flash", "DeepSeek Flash (V4.1)", "deepseek", ctx: 1000000, max: 384000, vision: true, thinking: true),
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
        // 两供应商 Id 不同且各自模型 Id 已命名空间化（zhipu-*），故可【同时启用】互不冲突：
        //   - 模型 Id 用 zhipu-glm-5.3 等，避免与 glm 供应商的 glm-5.3 在 /api/tags 撞车；
        //   - upstreamModel 仍填真正上游名 glm-5.3，发给 Z.ai 的请求体 model 字段正确。
        // 用户可按需二选一，或两家都启用、在客户端各自选择。
        list.Add(new ProviderPreset
        {
            Id = "zhipu",
            Name = "智谱 GLM (Z.ai)",
            BaseUrl = "https://api.z.ai/v1",
            Models =
            {
                M("zhipu-glm-5.3", "GLM-5.3 (Z.ai)", "glm", ctx: 1000000, max: 8192, vision: true, upstreamModel: "glm-5.3"),
                M("zhipu-glm-5.3-flash", "GLM-5.3 Flash (Z.ai)", "glm", ctx: 1000000, max: 8192, vision: true, thinking: true, upstreamModel: "glm-5.3-flash"),
                M("zhipu-glm-5.2", "GLM-5.2 (Z.ai)", "glm", ctx: 1000000, max: 8192, upstreamModel: "glm-5.2"),
                M("zhipu-glm-5.1", "GLM-5.1 (Z.ai)", "glm", ctx: 200000, max: 8192, upstreamModel: "glm-5.1"),
                M("zhipu-glm-4.7", "GLM-4.7 (Z.ai)", "glm", ctx: 200000, max: 8192, upstreamModel: "glm-4.7"),
                M("zhipu-glm-4.7-flashx", "GLM-4.7 FlashX (Z.ai)", "glm", ctx: 200000, max: 8192, upstreamModel: "glm-4.7-flashx"),
            },
        });

        // 5) 硅基流动（2026-09 刷新：DeepSeek 升到 V4 系列、新增智谱 GLM-5.3 / Qwen3.8 / Kimi-K3；旧 V3/V3.1/R1 已退役）
        // 注意：硅基流动模型 Id 的 owner 大小写在第三方源间有分歧，下列采用与既有 deepseek-ai/、Qwen/ 一致的大写惯例；
        // 若调用 404，请以你自己账号 GET https://api.siliconflow.cn/v1/models 返回的实际 Id 为准。
        list.Add(new ProviderPreset
        {
            Id = "siliconflow",
            Name = "硅基流动",
            BaseUrl = "https://api.siliconflow.cn/v1",
            Models =
            {
                M("deepseek-ai/DeepSeek-V4-Flash-0731", "SiliconFlow DeepSeek-V4-Flash", "siliconflow", ctx: 1048576, max: 131072),
                M("deepseek-ai/DeepSeek-V4-Flash-Vision-Exp", "SiliconFlow DeepSeek-V4-Flash Vision", "siliconflow", ctx: 1048576, max: 131072, vision: true),
                M("deepseek-ai/DeepSeek-V4-Pro-0813", "SiliconFlow DeepSeek-V4-Pro", "siliconflow", ctx: 1048576, max: 131072,
                  thinking: true, drop: new() { "temperature", "top_p" }),
                M("zhipuai/GLM-5.3", "SiliconFlow GLM-5.3", "siliconflow", ctx: 1048576, max: 262144),
                M("zhipuai/GLM-5.3-Flash", "SiliconFlow GLM-5.3-Flash", "siliconflow", ctx: 1048576, max: 262144, vision: true),
                M("Qwen/Qwen3.8-2.4T-A95B", "SiliconFlow Qwen3.8 2.4T", "siliconflow", ctx: 1048576, max: 131072, vision: true),
                M("moonshotai/Kimi-K3", "SiliconFlow Kimi-K3", "siliconflow", ctx: 1048576, max: 262144, vision: true, thinking: true),
            },
        });

        // 6) 火山方舟（2026-09 刷新：doubao-seed-2.1 系列(260628) 经核实仍在线；新增 260915 旗舰快照，保留统一 ID 思考模型 doubao-seed-evolving）
        list.Add(new ProviderPreset
        {
            Id = "volcengine",
            Name = "火山方舟",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            Models =
            {
                M("doubao-seed-2-1-pro-260915", "Doubao Seed 2.1 Pro (260915)", "volcengine", ctx: 256000, max: 32768, thinking: true),
                M("doubao-seed-2-1-pro-260628", "Doubao Seed 2.1 Pro", "volcengine", ctx: 256000, max: 32768, thinking: true),
                M("doubao-seed-2-1-turbo-260628", "Doubao Seed 2.1 Turbo", "volcengine", ctx: 256000, max: 32768),
                M("doubao-seed-2-0-code-preview-260215", "Doubao Seed 2.0 Code", "volcengine", ctx: 256000, max: 32768, vision: true),
                // 统一 ID 思考模型：平台自动升级底层版本，1M 上下文，推理型。
                M("doubao-seed-evolving", "Doubao Seed Evolving", "volcengine", ctx: 1000000, max: 32768, thinking: true),
            },
        });

        // 7) 腾讯混元（2026-09 切换：混元旗舰 HY3 / Hy4 现经腾讯云 TokenHub 暴露，统一 OpenAI 兼容基址；
        //    旧 api.hunyuan.cloud.tencent.com 的 hunyuan-a13b 等不在 TokenHub 体系，故迁移到 TokenHub。）
        list.Add(new ProviderPreset
        {
            Id = "hunyuan",
            Name = "腾讯混元 (TokenHub)",
            BaseUrl = "https://tokenhub.tencentmaas.com/v1",
            Models =
            {
                // HY3 正式版：256K 上下文 / 64K 输出，推理型（快慢思考切换），原生工具调用，文本输入。
                M("hy3", "Hunyuan Hy3", "hunyuan", ctx: 256000, max: 64000, thinking: true),
                // Hy4 预览版：约 1M 上下文 / 262K 输出，推理型。
                M("hy4-preview", "Hunyuan Hy4 Preview", "hunyuan", ctx: 1048576, max: 262144, thinking: true),
            },
        });

        // 8) ModelScope（2026-09 刷新：Qwen 升到 3.8 系列、DeepSeek 升到 V4 系列、新增智谱 GLM-5.3；旧 V3/V3.1 已退役）
        list.Add(new ProviderPreset
        {
            Id = "modelscope",
            Name = "ModelScope",
            BaseUrl = "https://api-inference.modelscope.cn/v1",
            Models =
            {
                M("Qwen/Qwen3.8-27B", "ModelScope Qwen3.8 27B", "modelscope", ctx: 262144, max: 131072, vision: true),
                M("Qwen/Qwen3.8-Flash-Next", "ModelScope Qwen3.8 Flash-Next", "modelscope", ctx: 262144, max: 131072, vision: true),
                M("deepseek-ai/DeepSeek-V4-Flash-0731", "ModelScope DeepSeek-V4-Flash", "modelscope", ctx: 1048576, max: 131072),
                M("deepseek-ai/DeepSeek-V4.1-Flash", "ModelScope DeepSeek-V4.1-Flash", "modelscope", ctx: 1048576, max: 131072, vision: true),
                M("ZhipuAI/GLM-5.3", "ModelScope GLM-5.3", "modelscope", ctx: 1048576, max: 262144),
            },
        });

        // 9) OpenRouter（聚合器，2026-09 升级：新增 GPT-6 系列、Claude Fable 5.1、Gemini 3.8 Flash；保留 gpt-5.6 / claude-opus-5 / gemini-3.7-flash）
        list.Add(new ProviderPreset
        {
            Id = "openrouter",
            Name = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            Models =
            {
                M("openai/gpt-6-astra", "GPT-6 Astra", "openrouter", ctx: 1050000, max: 16384, vision: true),
                M("openai/gpt-5.6", "GPT-5.6", "openrouter", ctx: 256000, max: 16384),
                M("anthropic/claude-opus-5", "Claude Opus 5", "openrouter", ctx: 1000000, max: 16384),
                M("anthropic/claude-fable-5.1", "Claude Fable 5.1", "openrouter", ctx: 1000000, max: 16384, vision: true),
                M("google/gemini-3.8-flash", "Gemini 3.8 Flash", "openrouter", ctx: 1000000, max: 8192, vision: true),
                M("google/gemini-3.7-flash", "Gemini 3.7 Flash", "openrouter", ctx: 1000000, max: 8192, vision: true),
            },
        });

        // 10) Anthropic Claude（ApiMode=anthropic；2026-09 新增 Fable 5.1 / Opus 5.5；保留 Opus 5 / Sonnet 5 / Fable 5 / Haiku 4.5）
        list.Add(new ProviderPreset
        {
            Id = "anthropic",
            Name = "Anthropic Claude",
            BaseUrl = "https://api.anthropic.com",
            ApiMode = "anthropic",
            Models =
            {
                M("claude-opus-5-5", "Claude Opus 5.5", "anthropic", ctx: 1000000, max: 16384),
                M("claude-opus-5", "Claude Opus 5", "anthropic", ctx: 1000000, max: 16384),
                M("claude-sonnet-5", "Claude Sonnet 5", "anthropic", ctx: 1000000, max: 16384),
                M("claude-fable-5-1", "Claude Fable 5.1", "anthropic", ctx: 1000000, max: 16384, vision: true),
                M("claude-fable-5", "Claude Fable 5", "anthropic", ctx: 1000000, max: 16384, vision: true),
                M("claude-haiku-4-5", "Claude Haiku 4.5", "anthropic", ctx: 200000, max: 8192),
            },
        });

        // 11) Google Gemini（ApiMode=gemini；2026-09 新增 Gemini 3.8 Flash；保留 3.1 Pro / 3.7 Flash / 3.5 Flash-Lite）
        list.Add(new ProviderPreset
        {
            Id = "gemini",
            Name = "Google Gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ApiMode = "gemini",
            Models =
            {
                M("gemini-3.1-pro-preview", "Gemini 3.1 Pro", "gemini", ctx: 2000000, max: 8192, vision: true),
                M("gemini-3.8-flash", "Gemini 3.8 Flash", "gemini", ctx: 1000000, max: 8192, vision: true),
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
        Boolean vision = false, Boolean thinking = false, List<String>? drop = null, String? upstreamModel = null)
        => new ModelOptions
        {
            Id = id,
            UpstreamModel = upstreamModel,
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
