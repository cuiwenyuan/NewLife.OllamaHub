using System.Collections.Generic;

namespace NewLife.OllamaHub.Core;

// Ollama 协议 DTO：字段名刻意使用 Ollama 真实 JSON 字段（小写 / 下划线），
// 以保证 NewLife FastJson 序列化后输出 Copilot 期望的字段名（不依赖命名约定）。

/// <summary>Ollama /api/version 响应。</summary>
public class OllamaVersionResponse
{
    /// <summary>
    /// 伪装的 Ollama 版本号。
    /// 必须是形似真实 Ollama 的语义化版本：部分客户端（含 Copilot BYOM）会按版本做能力门禁，
    /// 返回 0.0.1 之类的占位值可能被判定为过旧而拒绝挂载。
    /// </summary>
    public const String OllamaVersion = "0.11.4";

    /// <summary>版本号。</summary>
    public String version { get; set; } = OllamaVersion;
}

/// <summary>Ollama /api/tags 响应。</summary>
public class OllamaTagsResponse
{
    /// <summary>已注册模型列表。</summary>
    public List<OllamaTagModel> models { get; set; } = new();
}

/// <summary>/api/tags 的模型条目。</summary>
public class OllamaTagModel
{
    /// <summary>模型名（Copilot 展示与引用）。</summary>
    public String name { get; set; } = "";

    /// <summary>底层模型标识，通常与 name 相同。</summary>
    public String model { get; set; } = "";

    /// <summary>最后修改时间（RFC3339）。</summary>
    public String modified_at { get; set; } = "";

    /// <summary>模型体积（字节）。</summary>
    public Int64 size { get; set; }

    /// <summary>摘要。</summary>
    public String digest { get; set; } = "";

    /// <summary>模型细节（族 / 参数规模 / 量化）。</summary>
    public OllamaTagDetails details { get; set; } = new();
}

/// <summary>模型细节。</summary>
public class OllamaTagDetails
{
    /// <summary>模型族（如 deepseek / qwen）。</summary>
    public String family { get; set; } = "";

    /// <summary>参数规模（如 7B）。</summary>
    public String parameter_size { get; set; } = "";

    /// <summary>量化等级。</summary>
    public String quantization_level { get; set; } = "";
}

/// <summary>/api/show 请求（仅关心 model 字段）。</summary>
public class OllamaShowRequest
{
    /// <summary>要查询的模型 Id。</summary>
    public String model { get; set; } = "";
}

/// <summary>/api/show 响应。</summary>
public class OllamaShowResponse
{
    /// <summary>Modelfile 内容（本代理无真实模型，留空）。</summary>
    public String modelfile { get; set; } = "";

    /// <summary>参数说明（留空）。</summary>
    public String parameters { get; set; } = "";

    /// <summary>对话模板（留空）。</summary>
    public String template { get; set; } = "";

    /// <summary>模型细节。</summary>
    public OllamaTagDetails details { get; set; } = new();

    /// <summary>模型元信息，Copilot 据此判断上下文长度等（关键字段见代码）。</summary>
    public Dictionary<String, Object> model_info { get; set; } = new();

    /// <summary>
    /// 能力声明。缺 tools 会导致 Copilot 的 Agent/工具调用整体不可用，
    /// 但默认值只给 completion——tools/vision/thinking 必须由 ModelOptions 显式声明后再追加，
    /// 避免为未注册模型凭空捏造能力，导致 Copilot 发起注定失败的工具调用。
    /// </summary>
    public String[] capabilities { get; set; } = { "completion" };

    /// <summary>示例对话（留空数组）。</summary>
    public Object[] messages { get; set; } = System.Array.Empty<Object>();

    /// <summary>许可证（留空）。</summary>
    public String license { get; set; } = "";
}

/// <summary>/api/ps 响应（运行中的模型进程）。</summary>
public class OllamaPsResponse
{
    /// <summary>当前加载到显存的模型；本代理为远程模型，恒为空列表。</summary>
    public List<OllamaProcessModel> models { get; set; } = new();
}

/// <summary>/api/ps 的进程条目。</summary>
public class OllamaProcessModel
{
    /// <summary>模型名。</summary>
    public String name { get; set; } = "";

    /// <summary>底层模型标识。</summary>
    public String model { get; set; } = "";

    /// <summary>显存占用（字节）。</summary>
    public Int64 size_vram { get; set; }

    /// <summary>模型体积（字节）。</summary>
    public Int64 size { get; set; }

    /// <summary>摘要。</summary>
    public String digest { get; set; } = "";

    /// <summary>模型细节。</summary>
    public OllamaTagDetails details { get; set; } = new();
}

/// <summary>/api/chat 请求。</summary>
public class OllamaChatRequest
{
    /// <summary>模型 Id（对应 ModelOptions.Id）。</summary>
    public String model { get; set; } = "";

    /// <summary>对话历史（含 system / user / assistant）。</summary>
    public List<OllamaMessage> messages { get; set; } = new();

    /// <summary>Ollama 风格采样参数（temperature / top_p / num_predict 等）。</summary>
    public Dictionary<String, Object>? options { get; set; }

    /// <summary>工具定义（Copilot Agent 模式下发，OpenAI 风格）。</summary>
    public Object? tools { get; set; }

    /// <summary>工具选择策略（Copilot 可能下发 auto/none/指定函数）；原样透传上游。</summary>
    public Object? tool_choice { get; set; }

    /// <summary>输出格式约束（可为 json schema）；本代理透传，不强制。</summary>
    public Object? format { get; set; }

    /// <summary>是否流式。M1 统一以 NDJSON 单帧（done:true）响应，兼容两种客户端。</summary>
    public Boolean stream { get; set; } = true;

    /// <summary>模型驻留时长（本代理忽略）。</summary>
    public Object? keep_alive { get; set; }
}

/// <summary>/api/chat 单条消息。</summary>
public class OllamaMessage
{
    /// <summary>角色：system / user / assistant / tool。</summary>
    public String role { get; set; } = "";

    /// <summary>文本内容（可为 null，如仅含 tool_calls 的 assistant 消息）。</summary>
    public String? content { get; set; }

    /// <summary>推理过程（DeepSeek 等映射自上游 reasoning_content）。</summary>
    public Object? thinking { get; set; }

    /// <summary>工具调用（Copilot 下发的 OpenAI 格式）。</summary>
    public Object? tool_calls { get; set; }

    /// <summary>
    /// 工具结果消息（role=tool）对应的工具调用 Id，用于将 Ollama/Copilot 的 tool 消息
    /// 正确映射回 Anthropic 的 tool_result（需与前置 assistant 的 tool_use id 对齐）。
    /// 缺失则为 null（普通消息）。
    /// </summary>
    public String? tool_call_id { get; set; }

    /// <summary>
    /// 多模态图片（base64 原文或含 data URI 前缀，如 <c>data:image/png;base64,xxxx</c>）。
    /// 仅 user 消息有效；映射至上游 content 数组（OpenAI image_url / Anthropic image / Gemini inline_data）。
    /// 缺失则为 null（普通文本消息）。
    /// </summary>
    public List<String>? images { get; set; }
}

/// <summary>/api/generate 请求（M1 基础实现：转写为单条 user 消息走 chat 链路）。</summary>
public class OllamaGenerateRequest
{
    /// <summary>模型 Id。</summary>
    public String model { get; set; } = "";

    /// <summary>提示词。</summary>
    public String prompt { get; set; } = "";

    /// <summary>Ollama 风格采样参数。</summary>
    public Dictionary<String, Object>? options { get; set; }

    /// <summary>是否流式。</summary>
    public Boolean stream { get; set; } = true;
}

/// <summary>/api/chat 响应（单 JSON 对象，done:true）。</summary>
public class OllamaChatResponse
{
    /// <summary>模型 Id。</summary>
    public String model { get; set; } = "";

    /// <summary>创建时间（RFC3339）。</summary>
    public String created_at { get; set; } = "";

    /// <summary>助手回复消息。</summary>
    public OllamaMessage message { get; set; } = new();

    /// <summary>是否结束（恒为 true，M1 非流式）。</summary>
    public Boolean done { get; set; } = true;

    /// <summary>结束原因：stop / length。</summary>
    public String done_reason { get; set; } = "stop";

    /// <summary>生成 token 数。</summary>
    public Int64 eval_count { get; set; }

    /// <summary>提示 token 数。</summary>
    public Int64 prompt_eval_count { get; set; }
}

/// <summary>
/// /api/generate 响应。
/// 注意与 /api/chat 的差异：本端点返回的是 response 字符串字段，而非 message 对象，
/// 二者不可混用，否则 Ollama 客户端会取不到内容。
/// </summary>
public class OllamaGenerateResponse
{
    /// <summary>模型 Id。</summary>
    public String model { get; set; } = "";

    /// <summary>创建时间（RFC3339）。</summary>
    public String created_at { get; set; } = "";

    /// <summary>生成的文本内容。</summary>
    public String response { get; set; } = "";

    /// <summary>推理过程（可为 null）。</summary>
    public Object? thinking { get; set; }

    /// <summary>是否结束。</summary>
    public Boolean done { get; set; } = true;

    /// <summary>结束原因：stop / length。</summary>
    public String done_reason { get; set; } = "stop";

    /// <summary>生成 token 数。</summary>
    public Int64 eval_count { get; set; }

    /// <summary>提示 token 数。</summary>
    public Int64 prompt_eval_count { get; set; }
}

/// <summary>Ollama 错误响应（{"error":"..."}）。</summary>
public class OllamaErrorResponse
{
    /// <summary>错误描述。</summary>
    public String error { get; set; } = "";
}
