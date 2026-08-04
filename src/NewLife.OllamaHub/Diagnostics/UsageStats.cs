using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NewLife.OllamaHub.Diagnostics
{
    /// <summary>
    /// 用量统计（Web 管理面板 /api/status 用）。
    /// 进程内内存统计，按模型记录请求数、错误数、Token 用量与末次错误；线程安全。
    /// 注意：单例随进程生命周期，重启清零（不落盘，避免引入持久化依赖）。
    /// </summary>
    public class UsageStats
    {
        /// <summary>全局单例。</summary>
        public static UsageStats Instance { get; } = new();

        private readonly ConcurrentDictionary<String, Entry> _entries = new();

        /// <summary>单模型统计条目。</summary>
        public sealed class Entry
        {
            /// <summary>成功请求数。</summary>
            public Int64 Requests;

            /// <summary>失败请求数。</summary>
            public Int64 Errors;

            /// <summary>累计提示 token。</summary>
            public Int64 PromptTokens;

            /// <summary>累计生成 token。</summary>
            public Int64 CompletionTokens;

            /// <summary>最近一次使用（UTC）。</summary>
            public DateTime LastUsedUtc;

            /// <summary>最近一次错误信息（成功请求后保留空串）。</summary>
            public String LastError = "";
        }

        /// <summary>记录一次成功请求。</summary>
        /// <param name="modelId">模型 Id。</param>
        /// <param name="promptTokens">提示 token 数。</param>
        /// <param name="completionTokens">生成 token 数。</param>
        public void RecordSuccess(String modelId, Int64 promptTokens, Int64 completionTokens)
        {
            if (String.IsNullOrEmpty(modelId)) return;
            var e = _entries.GetOrAdd(modelId, _ => new Entry());
            System.Threading.Interlocked.Increment(ref e.Requests);
            System.Threading.Interlocked.Add(ref e.PromptTokens, promptTokens);
            System.Threading.Interlocked.Add(ref e.CompletionTokens, completionTokens);
            e.LastUsedUtc = DateTime.UtcNow;
            e.LastError = "";
        }

        /// <summary>记录一次失败请求。</summary>
        /// <param name="modelId">模型 Id（未知时可为 null）。</param>
        /// <param name="error">错误信息。</param>
        public void RecordError(String? modelId, String error)
        {
            if (String.IsNullOrEmpty(modelId)) modelId = "(未知模型)";
            var e = _entries.GetOrAdd(modelId!, _ => new Entry());
            System.Threading.Interlocked.Increment(ref e.Errors);
            e.LastError = error ?? "";
            e.LastUsedUtc = DateTime.UtcNow;
        }

        /// <summary>当前统计快照（模型 Id → 条目）。</summary>
        public IReadOnlyDictionary<String, Entry> Snapshot() => new Dictionary<String, Entry>(_entries);
    }
}
