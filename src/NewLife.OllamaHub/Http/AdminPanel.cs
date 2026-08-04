using System;
using System.IO;
using System.Reflection;

namespace NewLife.OllamaHub.Http;

/// <summary>
/// 内嵌的 Web 管理面板（只读仪表盘）。HTML 作为嵌入资源随 exe 打包，无需任何外部静态资源，离线可用。
/// 通过 /admin 返回；数据来自同进程的 /api/status JSON 端点。
/// </summary>
public static class AdminPanel
{
    private static readonly String _html = LoadHtml();

    /// <summary>面板 HTML（text/html）。</summary>
    public static String Html => _html;

    private static String LoadHtml()
    {
        var asm = typeof(AdminPanel).Assembly;
        var name = asm.GetName().Name + ".Http.AdminPanel.html";
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return "<html><body>panel resource missing</body></html>";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
