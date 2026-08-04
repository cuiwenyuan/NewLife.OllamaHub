using System.Collections.Generic;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 工具 JSON Schema 清洗器。
/// Copilot Agent 模式下发的工具定义是 OpenAI 风格 JSON Schema，但部分上游
/// （尤其非官方 OpenAI 兼容网关）对其中某些键敏感，会直接 400：
///   - $schema / definitions / $defs：上游不认识，直接拒绝
///   - additionalProperties：严格模式语义不一致，多数上游不支持
///   - title / examples / 以 x- 开头的厂商扩展键：可剥离以保证兼容
/// 本清洗器递归遍历工具树，删除上述键，并确保 parameters 带 type=object。
/// 注意：只删"可安全删除"的键——保留 name/description/type/properties/required/
/// enum/format/items 等承载语义的字段，避免把工具改坏。
/// </summary>
public static class ToolSchemaSanitizer
{
    // 需剥离的键（大小写不敏感）
    private static readonly HashSet<String> _dropKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "$schema", "definitions", "$defs", "title", "examples",
        "additionalProperties", "additionalItems",
    };

    /// <summary>清洗工具定义（或任意 JSON 结构）。返回清洗后的结构；输入为 null 时返回 null。</summary>
    /// <param name="tools">Copilot 下发的 tools（FastJson 反序列化为 IList/IDictionary 树）。</param>
    /// <returns>清洗后的同构结构，可直接塞回上游请求体。</returns>
    public static Object? Sanitize(Object? tools)
    {
        if (tools == null) return null;
        return Clean(tools);
    }

    private static Object Clean(Object node)
    {
        // 数组：逐元素递归
        if (node is System.Collections.IList list)
        {
            var outList = new List<Object>(list.Count);
            foreach (var item in list) outList.Add(item is null ? new object() : Clean(item));
            return outList;
        }

        // 对象：剥离禁用键，并对 parameters 做特殊保证
        if (node is System.Collections.IDictionary dict)
        {
            var outDict = new Dictionary<String, Object>(StringComparer.Ordinal);
            foreach (var key in dict.Keys)
            {
                var k = key?.ToString() ?? "";
                if (_dropKeys.Contains(k)) continue;                 // 跳过禁用键
                if (k.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) continue; // 厂商扩展键

                var val = dict[key];
                if (val == null) { outDict[k] = ""; continue; }

                // parameters 必须存在且为对象，并补 type=object，否则上游参数校验可能失败
                if (String.Equals(k, "parameters", StringComparison.OrdinalIgnoreCase) && val is System.Collections.IDictionary)
                {
                    var pm = (Dictionary<String, Object>)Clean(val);
                    if (!pm.ContainsKey("type")) pm["type"] = "object";
                    outDict[k] = pm;
                }
                else
                {
                    outDict[k] = Clean(val);
                }
            }
            return outDict;
        }

        // 标量：原样返回
        return node;
    }
}
