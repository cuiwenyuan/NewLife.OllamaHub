using System.Collections.Generic;

namespace NewLife.OllamaHub.Config;

/// <summary>模型配置。Copilot 通过 /api/tags 看到这些模型，通过 /api/show 读取能力。</summary>
public class ModelOptions
{
    /// <summary>模型 Id（Copilot 展示与引用用，如 deepseek-v4-flash）。</summary>
    public String Id { get; set; } = "";

    /// <summary>归属供应商 Id，对应 ProviderOptions.Id。</summary>
    public String? OwnedBy { get; set; }

    /// <summary>展示名（Copilot 模型选择器里显示）。</summary>
    public String? DisplayName { get; set; }

    /// <summary>模型族（写入 /api/show 的 &lt;family>.context_length）。</summary>
    public String? Family { get; set; }

    /// <summary>引用的供应商 Id。</summary>
    public String? Provider { get; set; }

    /// <summary>上下文长度（token），写入 model_info 供 Copilot 截断判断。</summary>
    public Int64 ContextLength { get; set; } = 32768;

    /// <summary>单次最大生成 token。</summary>
    public Int32 MaxTokens { get; set; } = 4096;

    /// <summary>是否支持工具调用（Agent 模式必须）。</summary>
    public Boolean Tools { get; set; } = true;

    /// <summary>是否支持视觉。</summary>
    public Boolean Vision { get; set; }

    /// <summary>是否为推理模型（映射 reasoning_content ⇄ thinking）。</summary>
    public Boolean Thinking { get; set; }

    /// <summary>回传上游时是否携带 reasoning（DeepSeek 要求不带）。</summary>
    public Boolean IncludeReasoningInRequest { get; set; } = true;

    /// <summary>向上游发送时丢弃的参数（如推理模型不接受 temperature/top_p）。</summary>
    public List<String> DropParams { get; set; } = new();

    /// <summary>模型级固定请求头。</summary>
    public Dictionary<String, String> Headers { get; set; } = new();

    /// <summary>深拷贝一份独立实例，避免预设目录等共享模板被运行期配置污染。</summary>
    /// <returns>字段逐一复制的新实例（集合类均新建）。</returns>
    public ModelOptions Clone() => new ModelOptions
    {
        Id = Id,
        OwnedBy = OwnedBy,
        DisplayName = DisplayName,
        Family = Family,
        Provider = Provider,
        ContextLength = ContextLength,
        MaxTokens = MaxTokens,
        Tools = Tools,
        Vision = Vision,
        Thinking = Thinking,
        IncludeReasoningInRequest = IncludeReasoningInRequest,
        DropParams = new List<String>(DropParams),
        Headers = new Dictionary<String, String>(Headers),
    };
}
