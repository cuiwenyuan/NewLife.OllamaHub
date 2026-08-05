using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 推理内容多轮缓存（P0 功能）。
/// 把推理模型（DeepSeek / Kimi 等）每次回答的 <c>reasoning_content</c> 按"会话前缀指纹"缓存，
/// 在后续多轮对话时重注入 assistant 消息，使上游推理模型保持连贯的推理上下文。
///
/// 设计要点：
///   - 指纹只取每条消息的 <c>role:content</c>，刻意忽略 thinking/tool_calls，
///     这样注入操作本身不会改变后续轮次的指纹，保证多轮哈希稳定可对齐。
///   - 存储键 = 完整请求消息列表的指纹；注入键 = 某条 assistant 消息"之前所有消息"的指纹。
///     两者在相邻两轮之间天然相等（见代码内推导），从而把上一轮的推理无缝接回本轮。
///   - 仅 localhost 单用户代理使用，内存有上限（近似 LRU 淘汰），不会无限增长。
/// </summary>
public static class ReasoningCache
{
    private sealed class Entry
    {
        public String Reasoning = "";
        public Int64 Last;
    }

    private static readonly ConcurrentDictionary<String, Entry> _cache = new();
    private static readonly ConcurrentQueue<String> _order = new();
    private const Int32 Cap = 256;
    private static Int64 _tick;

    /// <summary>计算消息前缀指纹（仅取 role:content，忽略 thinking/tool_calls）。</summary>
    /// <param name="msgs">消息列表。</param>
    /// <param name="upToExclusive">只取 [0, upToExclusive) 区间的消息（不含该索引）。</param>
    /// <returns>SHA256 十六进制指纹。</returns>
    public static String ComputeKey(List<OllamaMessage> msgs, Int32 upToExclusive)
    {
        var sb = new StringBuilder();
        var n = Math.Min(upToExclusive, msgs.Count);
        for (var i = 0; i < n; i++)
        {
            var m = msgs[i];
            sb.Append(m.role ?? "").Append(':').Append(m.content ?? "").Append('|');
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    /// <summary>把缓存的推理重注入 assistant 消息（仅填充尚缺 reasoning 的助手消息，已有则不覆盖）。</summary>
    /// <param name="msgs">将发往上游的消息列表（会被原地修改）。</param>
    public static void Inject(List<OllamaMessage> msgs)
    {
        if (msgs == null || msgs.Count == 0) return;
        for (var i = 0; i < msgs.Count; i++)
        {
            var m = msgs[i];
            if (!String.Equals(m.role, "assistant", StringComparison.OrdinalIgnoreCase)) continue;
            // 客户端可能已自行回传 reasoning，则不覆盖
            if (m.thinking is String existing && existing.Length > 0) continue;

            // 该助手消息"之前的所有消息"的指纹，即上一轮生成它时所用请求的完整上下文
            var key = ComputeKey(msgs, i);
            if (_cache.TryGetValue(key, out var e) && e.Reasoning.Length > 0)
                m.thinking = e.Reasoning;
        }
    }

    /// <summary>缓存一次回答的推理内容（按完整请求消息列表指纹索引）。</summary>
    /// <param name="key">由 <see cref="ComputeKey"/> 计算出的请求指纹。</param>
    /// <param name="reasoning">本次回答的推理文本；空值忽略。</param>
    public static void Store(String key, String reasoning)
    {
        if (String.IsNullOrEmpty(key) || String.IsNullOrEmpty(reasoning)) return;

        _cache[key] = new Entry { Reasoning = reasoning, Last = ++_tick };
        _order.Enqueue(key);

        // 近似 LRU：超出上限时淘汰最早写入的键；队列本身也限长，避免无限增长
        while (_order.Count > Cap * 2 && _order.TryDequeue(out _)) { }
        while (_cache.Count > Cap && _order.TryDequeue(out var old))
            _cache.TryRemove(old, out _);
    }
}
