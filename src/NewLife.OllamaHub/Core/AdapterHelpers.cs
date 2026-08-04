using System.Collections.Generic;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 上游适配器共用的小工具（M6）。主要为 JSON 字典的安全取值扩展，
/// 避免对缺失键使用索引器抛异常。
/// </summary>
public static class AdapterHelpers
{
    /// <summary>从字典安全取值；键不存在或字典为 null 时返回 null。</summary>
    public static Object? Val(this Dictionary<String, Object?>? d, String key)
        => d != null && d.TryGetValue(key, out var v) ? v : null;
}
