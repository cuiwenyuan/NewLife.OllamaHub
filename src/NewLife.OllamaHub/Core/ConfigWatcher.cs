using System;
using System.IO;
using System.Threading;
using NewLife.Log;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 监视 settings.json 变更并在变更后回调（M4 热重载）。
/// 用 <see cref="FileSystemWatcher"/> 监听文件所在目录，配合短去抖（debounce）避免
/// 编辑器"保存即多次写"或 <c>setkey</c> 改写造成的事件抖动。
/// 本类只负责"侦测变更 + 去抖 + 回调"，具体重载逻辑由回调（通常 <c>ModelRegistry.Instance.Load</c>）完成。
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly String _filePath;
    private readonly Action _onChanged;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private Boolean _disposed;

    /// <summary>构造监视器。</summary>
    /// <param name="filePath">被监视的配置文件完整路径（如 settings.json）。</param>
    /// <param name="onChanged">检测到变更（去抖后）时要执行的回调。</param>
    public ConfigWatcher(String filePath, Action onChanged)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    /// <summary>开始监视。目录不存在时静默放弃（无文件可监视）。</summary>
    public void Start()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var name = Path.GetFileName(_filePath);
        _watcher = new FileSystemWatcher(dir, name)
        {
            // 只关心内容/尺寸/重命名，忽略属性等无关噪声
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;

        // 去抖：编辑器保存与 setkey 改写常触发连续多次事件，500ms 内只回调一次
        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private void OnFsEvent(Object? sender, FileSystemEventArgs e)
    {
        // 仅处理目标文件本身（Renamed 时需用 FullPath 比较）
        if (!String.Equals(e.Name, Path.GetFileName(_filePath), StringComparison.OrdinalIgnoreCase) &&
            !String.Equals(e.FullPath, _filePath, StringComparison.OrdinalIgnoreCase))
            return;
        try { _debounce?.Change(500, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* 计时器已释放，忽略 */ }
    }

    private void Fire()
    {
        try { _onChanged(); }
        catch (Exception ex) { XTrace.WriteException(ex); }
    }

    /// <summary>停止监视并释放资源。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Dispose();
        _debounce = null;
    }
}
