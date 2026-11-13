using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Commands
{
    /// <summary>
    /// 自升级子命令。拉取版本清单（JSON），与当前程序集版本比较，若远程更新则下载 exe
    /// 并借一个 detached cmd 脚本在进程退出后自替换、再重启服务/进程。
    /// 零第三方依赖：下载用 BCL HttpClient，自替换用 Windows cmd（move + net start）。
    /// 用法：
    ///   upgrade               检查并升级（如需则下载+替换+重启）
    ///   upgrade --check       仅检查，不下载
    ///   upgrade --dry-run     下载到临时目录但不替换
    ///   upgrade --url &lt;URL&gt;  覆盖版本清单地址
    /// </summary>
    public static class UpgradeCommand
    {
        /// <summary>内置默认版本清单地址（可被 settings.json 的 UpgradeUrl 或 --url 覆盖）。</summary>
        public const String DefaultManifestUrl = "https://api.github.com/repos/NewLifeX/DotNet.OllamaHub/releases/latest";

        /// <summary>服务名（用于替换后重启；与 HubAgentService.ServiceName 保持一致）。</summary>
        private const String ServiceName = "NewLife.OllamaHub";

        /// <summary>执行升级。</summary>
        /// <param name="args">命令行参数（含 upgrade 自身）。</param>
        public static void Run(String[] args)
        {
            XTrace.UseConsole();
            var check = false;
            var dryRun = false;
            String? urlOverride = null;

            var rest = (args ?? Array.Empty<String>()).Skip(1).ToArray();
            for (var i = 0; i < rest.Length; i++)
            {
                if (rest[i] is "--check") check = true;
                else if (rest[i] is "--dry-run") dryRun = true;
                else if ((rest[i] is "--url") && i + 1 < rest.Length) urlOverride = rest[++i];
            }

            try
            {
                var manifestUrl = urlOverride
                                  ?? HubSettings.Load().UpgradeUrl
                                  ?? DefaultManifestUrl;

                XTrace.WriteLine("正在检查更新：{0}", manifestUrl);
                var json = FetchTextAsync(manifestUrl).GetAwaiter().GetResult();
                if (String.IsNullOrEmpty(json))
                {
                    XTrace.WriteLine("无法获取版本清单，升级中止。");
                    Environment.ExitCode = 1;
                    return;
                }

                var (version, exeUrl, notes) = ParseManifest(json);
                if (String.IsNullOrEmpty(version) || String.IsNullOrEmpty(exeUrl))
                {
                    XTrace.WriteLine("版本清单缺少 version 或 url 字段，升级中止。");
                    Environment.ExitCode = 1;
                    return;
                }

                var cur = GetCurrentVersion();
                var cmp = CompareVersions(version, cur);
                XTrace.WriteLine("当前版本 {0}，远程版本 {1}。", cur, version);
                if (!String.IsNullOrEmpty(notes)) XTrace.WriteLine("更新说明：{0}", notes);

                // 多产物 Release 的误配告警：拿到的是另一个框架的 exe，替换后大概起不来
                var tfmCur = CurrentTargetFramework;
                if (!String.IsNullOrEmpty(tfmCur))
                {
                    var other = tfmCur == "net8.0" ? "-net10.0" : "-net8.0";
                    if (exeUrl!.Contains(other, StringComparison.OrdinalIgnoreCase))
                        XTrace.WriteLine("[警告] 远程产物是 {0} 版本，与当前运行时 {1} 不匹配；升级后可能因缺少对应 .NET 运行时无法启动，建议手工下载匹配版本。", other.TrimStart('-'), tfmCur);
                }

                if (cmp <= 0)
                {
                    XTrace.WriteLine(cmp == 0 ? "已经是最新版本。" : "远程版本不高于当前，无需升级。");
                    return;
                }

                if (check)
                {
                    XTrace.WriteLine("发现新版本 {0}（--check 仅检查，未下载）。", version);
                    return;
                }

                var tmp = Path.Combine(Path.GetTempPath(), "OllamaHub_upgrade_" + Guid.NewGuid().ToString("N") + ".exe");
                XTrace.WriteLine("下载新版本到 {0} …", tmp);
                DownloadFileAsync(exeUrl, tmp).GetAwaiter().GetResult();

                if (dryRun)
                {
                    XTrace.WriteLine("--dry-run：已下载至 {0}，未替换当前程序。", tmp);
                    return;
                }

                PerformSelfReplace(tmp);
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
                Environment.ExitCode = 1;
            }
        }

        /// <summary>在当前进程退出后，将已下载的 exe 移动到当前程序位置并重启。</summary>
        /// <param name="downloadedExe">已下载的新 exe 完整路径。</param>
        private static void PerformSelfReplace(String downloadedExe)
        {
            var current = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                          ?? throw new InvalidOperationException("无法确定当前 exe 路径");
            if (String.Equals(Path.GetFullPath(downloadedExe), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
            {
                XTrace.WriteLine("下载目标与当前 exe 相同，跳过替换。");
                return;
            }

            var script = BuildReplaceScript(Environment.ProcessId, current, downloadedExe, ServiceName);
            var scriptPath = Path.Combine(Path.GetTempPath(), "OllamaHub_upgrade_" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(scriptPath, script);

            XTrace.WriteLine("即将在进程退出后自替换并重启。脚本：{0}", scriptPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            // 退出当前进程，让脚本得以执行替换（服务停止后由脚本 net start 重启）
            Environment.Exit(0);
        }

        /// <summary>生成自替换 cmd 脚本：等待当前进程退出 → 移动新 exe → 重启服务/进程。</summary>
        internal static String BuildReplaceScript(Int32 pid, String currentExe, String downloadedExe, String serviceName)
        {
            var src = downloadedExe.Replace("\"", "\"\"");
            var dst = currentExe.Replace("\"", "\"\"");
            return $@"@echo off
chcp 65001 >nul
:wait
tasklist /fi ""PID eq {pid}"" | findstr /r ""[0-9][0-9]*"" >nul
if %errorlevel%==0 ( timeout /t 1 /nobreak >nul & goto wait )
move /Y ""{src}"" ""{dst}""
if %errorlevel%==0 (
  net start {serviceName} >nul 2>&1
  if not %errorlevel%==0 ( start "" ""{dst}"" -run )
)
del ""%~f0"" >nul 2>&1
";
        }

        /// <summary>把已下载的 exe 移动到目标（直接覆盖）。用于未锁定的目标（如自测）。</summary>
        internal static void PerformReplace(String currentExe, String downloadedExe)
        {
            if (File.Exists(currentExe)) File.Delete(currentExe);
            File.Move(downloadedExe, currentExe);
        }

        /// <summary>直接下载文件到目标路径（BCL HttpClient，30s 超时）。</summary>
        internal static async Task DownloadFileAsync(String url, String dest)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
        }

        /// <summary>获取版本清单文本（可被覆盖地址）。</summary>
        internal static async Task<String> FetchTextAsync(String url)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            return await http.GetStringAsync(url).ConfigureAwait(false);
        }

        /// <summary>解析版本清单 JSON（{ version, url, notes }）。</summary>
        internal static (String version, String url, String notes) ParseManifest(String json)
        {
            var m = JsonHelper.ToJsonEntity<UpgradeManifest>(json);
            if (m == null) return ("", "", "");
            // 兼容普通清单与 GitHub releases/latest（tag_name + assets[]）。
            var version = NormalizeVersion(m.version ?? m.tag_name ?? "");
            var notes = m.notes ?? m.body ?? "";
            var url = m.url;
            if (String.IsNullOrEmpty(url) && m.assets != null)
                url = PickAssetUrl(m.assets) ?? "";
            return (version, url ?? "", notes);
        }

        /// <summary>当前运行时的目标框架短名（net8.0 / net10.0），用于在多产物 Release 中挑对 exe。</summary>
        internal static String CurrentTargetFramework
        {
            get
            {
#if NET10_0_OR_GREATER
                return "net10.0";
#elif NET8_0_OR_GREATER
                return "net8.0";
#else
                return "";
#endif
            }
        }

        /// <summary>
        /// 从 Release 资产中挑选与当前运行时匹配的 exe。
        /// 本项目同时发布 <c>-net8.0</c> 与 <c>-net10.0</c> 两个单文件产物，必须按框架挑：
        /// 否则 net8 用户可能被升级成 net10 产物，而机器上没有 .NET 10 运行时会直接起不来。
        /// 匹配不到时回退第一个下载地址，兼容早期只发单个 exe（文件名不带框架标记）的 Release。
        /// </summary>
        /// <param name="assets">Release 资产列表。</param>
        internal static String? PickAssetUrl(List<UpgradeAsset> assets)
        {
            var urls = assets.Where(a => !String.IsNullOrEmpty(a?.browser_download_url))
                             .Select(a => a!.browser_download_url!)
                             .ToList();
            if (urls.Count == 0) return null;

            var tfm = CurrentTargetFramework;
            if (!String.IsNullOrEmpty(tfm))
            {
                foreach (var u in urls)
                {
                    if (!u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    // 形如 NewLife.OllamaHub-net8.0.exe
                    if (Path.GetFileNameWithoutExtension(u).EndsWith("-" + tfm, StringComparison.OrdinalIgnoreCase)) return u;
                }
            }

            // 没找到框架匹配项：只有单个产物（老 Release）时这是正常路径；多产物时说明当前框架没有产物，
            // 仍回退到首个以免升级功能直接失效，但由调用方日志提示用户手工确认。
            return urls[0];
        }

        /// <summary>当前程序集版本（如 1.0.0.0）。</summary>
        internal static String GetCurrentVersion() =>
            typeof(UpgradeCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        /// <summary>比较两个版本号。a&gt;b 返回 1，相等 0，a&lt;b 返回 -1。</summary>
        internal static Int32 CompareVersions(String a, String b)
        {
            if (Version.TryParse(Normalize(a), out var va) && Version.TryParse(Normalize(b), out var vb))
                return va.CompareTo(vb);
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }

        private static String Normalize(String v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            return v;
        }

        /// <summary>去掉版本号前导 v（GitHub tag 常见 v1.2.3）。</summary>
        private static String NormalizeVersion(String v) => Normalize(v);
    }

    /// <summary>版本清单 DTO（同时兼容普通 JSON 与 GitHub releases API 字段名）。</summary>
    internal class UpgradeManifest
    {
        public String? version { get; set; }
        public String? url { get; set; }
        public String? notes { get; set; }
        // GitHub releases/latest 兼容
        public String? tag_name { get; set; }
        public String? body { get; set; }
        public List<UpgradeAsset>? assets { get; set; }
    }

    /// <summary>GitHub release asset（仅取下载地址）。</summary>
    internal class UpgradeAsset
    {
        public String? browser_download_url { get; set; }
    }
}
