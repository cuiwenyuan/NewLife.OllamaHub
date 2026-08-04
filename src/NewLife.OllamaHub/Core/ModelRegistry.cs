using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewLife.Log;
using NewLife.Serialization;
using NewLife.OllamaHub.Config;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 模型/供应商注册表：从程序目录 settings.json 加载配置，对外提供模型与供应商查询。
/// M1 实现基础加载与查询；聚合本机真实 Ollama 在 M2 扩展，热重载在 M4 扩展。
/// </summary>
public class ModelRegistry
{
    /// <summary>全局单例。</summary>
    public static ModelRegistry Instance { get; } = new();

    /// <summary>当前配置（Load 后生效）。</summary>
    public HubSettings Settings { get; private set; } = new();

    /// <summary>模型列表（来自配置）。</summary>
    public IList<ModelOptions> Models => Settings.Models;

    /// <summary>供应商字典（Id 不区分大小写）。</summary>
    public IDictionary<String, ProviderOptions> Providers { get; private set; } =
        new Dictionary<String, ProviderOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>从程序目录下的 settings.json 加载配置。</summary>
    public void Load() => Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));

    /// <summary>从指定路径的 settings.json 加载配置（便于自检用合成配置，避免依赖部署目录）。</summary>
    /// <param name="file">配置文件完整路径。</param>
    public void Load(String file)
    {
        if (!File.Exists(file))
        {
            XTrace.WriteLine("未找到 settings.json（{0}），使用空配置。可用 settings.sample.json 作为模板。", file);
            return;
        }

        try
        {
            var json = File.ReadAllText(file);
            var s = JsonHelper.ToJsonEntity<HubSettings>(json);
            if (s == null) return;

            Settings = s;
            s.Normalize(); // 确保 Url 由 host/port 派生（缺省空串时）
            Providers = new Dictionary<String, ProviderOptions>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in s.Providers)
                if (!String.IsNullOrEmpty(p.Id)) Providers[p.Id] = p;

            // 聚合开关：未手动配置 ollama 供应商时，自动注册一个指向本机 Ollama 的供应商，
            // 使 /api/tags 能并入真实 Ollama 的模型列表（M3）。
            if (s.AggregateLocalOllama &&
                !Providers.Values.Any(p => String.Equals(p.ApiMode, "ollama", StringComparison.OrdinalIgnoreCase)))
            {
                var baseUrl = (s.LocalOllamaBaseUrl ?? "").TrimEnd('/');
                if (!String.IsNullOrEmpty(baseUrl))
                {
                    Providers["local-ollama"] = new ProviderOptions
                    {
                        Id = "local-ollama",
                        Name = "本机 Ollama",
                        BaseUrl = baseUrl,
                        ApiMode = "ollama",
                    };
                    XTrace.WriteLine("已按 aggregateLocalOllama 自动注册本地 Ollama 供应商：{0}", baseUrl);
                }
            }

            XTrace.WriteLine("已加载 {0} 个模型、{1} 个供应商（来自 {2}）", s.Models.Count, s.Providers.Count, file);
        }
        catch (Exception ex)
        {
            XTrace.WriteLine("加载 settings.json 失败：{0}", ex.Message);
        }
    }

    /// <summary>按 Id 查询供应商。</summary>
    /// <param name="id">供应商 Id（不区分大小写）。</param>
    /// <returns>命中返回供应商，否则 null。</returns>
    public ProviderOptions? GetProvider(String? id) =>
        id != null && Providers.TryGetValue(id, out var p) ? p : null;

    /// <summary>按模型配置反查其归属供应商（Provider 优先，回退 OwnedBy）。</summary>
    /// <param name="model">模型配置（不可为 null）。</param>
    /// <returns>命中返回供应商，否则 null。</returns>
    public ProviderOptions? GetProvider(ModelOptions model) =>
        model == null ? null : GetProvider(model.Provider ?? model.OwnedBy);

    /// <summary>按 Id 查询模型。</summary>
    /// <param name="id">模型 Id。</param>
    /// <returns>命中返回模型，否则 null。</returns>
    public ModelOptions? GetModel(String? id) =>
        id != null ? Settings.Models.Find(m => m.Id == id) : null;
}
