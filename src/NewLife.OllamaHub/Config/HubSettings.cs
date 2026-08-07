using System;
using System.Collections.Generic;
using System.IO;
using NewLife.Log;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Config
{
    /// <summary>
    /// 单个监听节点的配置（本地 HTTP 或 局域网 HTTPS 共用）。
    /// scheme 由使用方决定：Local 为 http，LanHttps 为 https。
    /// </summary>
    public class HttpListenerOptions
    {
        /// <summary>是否启用该监听，默认 true（Local）/ false（LanHttps）。</summary>
        public Boolean Enabled { get; set; } = true;

        /// <summary>监听主机。Local 默认 127.0.0.1（仅本机）；LanHttps 默认 0.0.0.0（面向局域网）。</summary>
        public String Host { get; set; } = "127.0.0.1";

        /// <summary>监听端口。</summary>
        public Int32 Port { get; set; } = 11434;

        /// <summary>
        /// HTTPS 证书（PFX）路径，相对 settings.json 所在目录或绝对路径。仅 HTTPS 监听（LanHttps）使用。
        /// 为空且 Enabled=true 时跳过该监听并告警。证书须被客户端机器信任（自签需手动导入受信任根证书）。
        /// </summary>
        public String? Certificate { get; set; }

        /// <summary>PFX 证书密码（如有）。仅 HTTPS 监听使用。</summary>
        public String? CertPassword { get; set; }

        /// <summary>把当前监听节点推导为对外 URL（scheme 由调用方按节点语义传入）。</summary>
        public String ResolveUrl(String scheme) => $"{scheme}://{Host}:{Port}";
    }

    /// <summary>
    /// NewLife.OllamaHub 全局配置，对应 settings.json。
    /// 监听拆分为三个独立节点：
    ///   - <see cref="Local"/>：本机明文 HTTP（默认启用，127.0.0.1:11434）；
    ///   - <see cref="LanHttps"/>：局域网 TLS HTTPS（默认禁用，0.0.0.0:11435，需证书）；
    ///   - <see cref="LanHttp"/>：局域网明文 HTTP（默认禁用，0.0.0.0:11436，无证书）。
    /// 三者可同时在线，亦可各自独立启停（热重载即时生效，无需重启进程）。
    /// LanHttp 用于 Visual Studio 的 "Ollama" BYO 提供商的局域网接入 workaround：
    /// VS 仅允许 localhost HTTP 或 LAN HTTPS，但 LAN HTTPS 运行时因自签证书校验失败而取不到模型；
    /// 启用 LanHttp 后，在 VS 里先填 https 保存、再改配置文件把 https 改回 http，即可在局域网拿到模型列表。
    /// </summary>
    public class HubSettings
    {
        /// <summary>本机明文 HTTP 监听（默认启用，仅供 127.0.0.1）。</summary>
        public HttpListenerOptions Local { get; set; } = new() { Enabled = true, Host = "127.0.0.1", Port = 11434 };

        /// <summary>局域网 HTTPS 监听（默认禁用，面向 0.0.0.0）。VS / 非 localhost 场景需启用并配置证书。</summary>
        public HttpListenerOptions LanHttps { get; set; } = new() { Enabled = false, Host = "0.0.0.0", Port = 11435 };

        /// <summary>局域网明文 HTTP 监听（默认禁用，面向 0.0.0.0，无证书）。用于 VS "Ollama" BYO 提供商的局域网接入 workaround。</summary>
        public HttpListenerOptions LanHttp { get; set; } = new() { Enabled = false, Host = "0.0.0.0", Port = 11436 };

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

        /// <summary>
        /// 规范化：确保两个监听节点非空、端口有效、Host 不为空。
        /// 旧式 host/port/url 字段已废弃，一律以 local/lanHttps 为准。
        /// </summary>
        internal void Normalize()
        {
            Local ??= new HttpListenerOptions { Enabled = true, Host = "127.0.0.1", Port = 11434 };
            LanHttps ??= new HttpListenerOptions { Enabled = false, Host = "0.0.0.0", Port = 11435 };
            LanHttp ??= new HttpListenerOptions { Enabled = false, Host = "0.0.0.0", Port = 11436 };

            if (String.IsNullOrEmpty(Local.Host)) Local.Host = "127.0.0.1";
            if (Local.Port <= 0) Local.Port = 11434;

            if (String.IsNullOrEmpty(LanHttps.Host)) LanHttps.Host = "0.0.0.0";
            if (LanHttps.Port <= 0) LanHttps.Port = 11435;

            if (String.IsNullOrEmpty(LanHttp.Host)) LanHttp.Host = "0.0.0.0";
            if (LanHttp.Port <= 0) LanHttp.Port = 11436;
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
