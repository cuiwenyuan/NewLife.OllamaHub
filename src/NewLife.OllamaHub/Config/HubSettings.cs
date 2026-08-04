using System;
using System.Collections.Generic;
using System.IO;
using NewLife.Log;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Config
{
    /// <summary>
    /// NewLife.OllamaHub 全局配置，对应 settings.json。
    /// 同时兼容两种写法：
    ///   - 单字段 <see cref="Url"/>（如 http://127.0.0.1:11434）；
    ///   - 拆分 <see cref="Host"/> + <see cref="Port"/>（与 examples 一致），
    ///     未显式给 Url 时由两者推导。
    /// </summary>
    public class HubSettings
    {
        /// <summary>对外暴露的 Ollama 兼容端点。为空时由 Host:Port 推导（见 <see cref="Normalize"/>）。</summary>
        public String Url { get; set; } = "";

        /// <summary>监听主机（拆分写法），默认 127.0.0.1。</summary>
        public String Host { get; set; } = "127.0.0.1";

        /// <summary>监听端口（拆分写法），默认 11434。</summary>
        public Int32 Port { get; set; } = 11434;

        /// <summary>日志配置。</summary>
        public LoggingOptions Logging { get; set; } = new();

        /// <summary>是否聚合本机真实 Ollama（自动注册一个 ollama 模式供应商并并入 /api/tags）。</summary>
        public Boolean AggregateLocalOllama { get; set; }

        /// <summary>本机 Ollama 基址（配合 <see cref="AggregateLocalOllama"/>），默认 http://127.0.0.1:11434。</summary>
        public String LocalOllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

        /// <summary>自升级用的版本清单地址（JSON，含 version/url/notes）。为空时使用内置默认地址。</summary>
        public String? UpgradeUrl { get; set; }

        /// <summary>供应商列表。</summary>
        public List<ProviderOptions> Providers { get; set; } = new List<ProviderOptions>();

        /// <summary>模型列表。</summary>
        public List<ModelOptions> Models { get; set; } = new List<ModelOptions>();

        /// <summary>
        /// 从 settings.json 加载配置。
        /// 文件不存在或内容损坏时，不抛异常，返回带默认值的实例并记录告警。
        /// </summary>
        /// <param name="file">配置文件路径，默认当前目录 settings.json。</param>
        /// <returns>配置实例（永不为 null）。</returns>
        public static HubSettings Load(String file = "settings.json")
        {
            if (File.Exists(file))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var settings = JsonHelper.ToJsonEntity<HubSettings>(json);
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    // 配置损坏不应阻断启动，回退默认值并告警
                    XTrace.WriteException(ex);
                }
            }
            var fallback = new HubSettings();
            fallback.Normalize();
            return fallback;
        }

        /// <summary>将当前配置写回 settings.json（utf-8，缩进）。</summary>
        /// <param name="file">目标路径，默认当前目录 settings.json。</param>
        public void Save(String file = "settings.json")
        {
            var json = JsonHelper.ToJson(this, true);
            File.WriteAllText(file, json);
        }

        /// <summary>规范化：未显式给 Url 时由 Host:Port 推导，保证监听地址始终有效。</summary>
        internal void Normalize()
        {
            if (!String.IsNullOrEmpty(Url)) return;
            Url = (!String.IsNullOrEmpty(Host) && Port > 0)
                ? $"http://{Host}:{Port}"
                : "http://127.0.0.1:11434";
        }
    }

    /// <summary>日志配置（对应 settings.json 的 logging 段）。</summary>
    public class LoggingOptions
    {
        /// <summary>日志级别：Debug / Info / Warn / Error，默认 Info。</summary>
        public String Level { get; set; } = "Info";

        /// <summary>日志保留天数，默认 7。</summary>
        public Int32 RetentionDays { get; set; } = 7;
    }
}
