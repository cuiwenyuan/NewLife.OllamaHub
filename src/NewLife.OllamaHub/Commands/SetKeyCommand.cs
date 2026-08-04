using System;
using System.IO;
using System.Linq;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Security;

namespace NewLife.OllamaHub.Commands
{
    /// <summary>
    /// 配置 API Key 子命令。把密钥以加密形式（dpapi:）或环境变量引用（env:NAME）写入 settings.json，
    /// 避免明文落盘。覆盖目标默认取程序目录 settings.json（与 serve 加载路径一致）。
    /// 用法：
    ///   setkey --list
    ///   setkey &lt;providerId&gt; &lt;apiKey&gt;
    ///   setkey &lt;providerId&gt; --env &lt;ENV_NAME&gt;
    ///   setkey &lt;providerId&gt; --show
    ///   setkey &lt;providerId&gt; --clear
    ///   （任意命令可加 --file &lt;path&gt; 指定配置）
    /// </summary>
    public static class SetKeyCommand
    {
        /// <summary>执行密钥配置。</summary>
        /// <param name="args">命令行参数（含 setkey 自身）。</param>
        public static void Run(String[] args)
        {
            XTrace.UseConsole();
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "settings.json");
                var rest = (args ?? Array.Empty<String>()).Skip(1).ToList();

                // 可选：--file 覆盖配置路径
                var fi = rest.FindIndex(x => x == "--file" || x == "-f");
                if (fi >= 0 && fi + 1 < rest.Count)
                {
                    file = rest[fi + 1];
                    rest.RemoveAt(fi + 1);
                    rest.RemoveAt(fi);
                }

                if (rest.Count == 0 || rest[0] is "--help" or "-h" or "/?")
                {
                    PrintUsage();
                    return;
                }

                if (rest[0] is "--list" or "-l")
                {
                    ListProviders(file);
                    return;
                }

                // --env NAME：抽取环境变量名并从参数中移除
                String? envName = null;
                var ei = rest.FindIndex(x => x == "--env" || x == "-e");
                if (ei >= 0 && ei + 1 < rest.Count)
                {
                    envName = rest[ei + 1];
                    rest.RemoveAt(ei + 1);
                    rest.RemoveAt(ei);
                }

                // providerId = 第一个非 flag 参数（与顺序无关：setkey mock --show / setkey --show mock 均可）
                var providerId = rest.FirstOrDefault(x => !x.StartsWith("-")) ?? "";
                if (String.IsNullOrEmpty(providerId))
                {
                    XTrace.WriteLine("缺少供应商 Id。");
                    Environment.ExitCode = 1;
                    return;
                }

                var settings = HubSettings.Load(file);
                var provider = settings.Providers
                    .FirstOrDefault(p => String.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
                if (provider == null)
                {
                    XTrace.WriteLine("未找到供应商：{0}（可用 setkey --list 查看）", providerId);
                    Environment.ExitCode = 1;
                    return;
                }

                if (rest.Contains("--clear"))
                {
                    provider.ApiKey = "";
                    provider.ProtectedApiKey = "";
                    settings.Save(file);
                    XTrace.WriteLine("已清除供应商 {0} 的密钥（{1}）。", providerId, file);
                    return;
                }

                if (rest.Contains("--show"))
                {
                    ShowProvider(provider);
                    return;
                }

                if (envName != null)
                {
                    provider.ProtectedApiKey = "env:" + envName;
                    provider.ApiKey = ""; // 清掉明文，避免双份生效
                    settings.Save(file);
                    XTrace.WriteLine("已将供应商 {0} 的密钥切换为环境变量注入：env:{1}（{2}）", providerId, envName, file);
                    return;
                }

                // 位置参数：setkey <id> <key>
                var key = rest.FirstOrDefault(x => x != providerId && !x.StartsWith("-"));
                if (String.IsNullOrEmpty(key))
                {
                    XTrace.WriteLine("缺少密钥值。用法：setkey <providerId> <apiKey>   或   setkey <providerId> --env <ENV_NAME>");
                    Environment.ExitCode = 1;
                    return;
                }

                provider.ProtectedApiKey = SecretProtector.Protect(key);
                provider.ApiKey = ""; // 清掉明文，加密串已写入
                settings.Save(file);
                XTrace.WriteLine("已为供应商 {0} 写入加密密钥（dpapi: 前缀，存于 {1}）。", providerId, file);
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
                Environment.ExitCode = 1;
            }
        }

        private static void ListProviders(String file)
        {
            var settings = HubSettings.Load(file);
            if (settings.Providers.Count == 0)
            {
                XTrace.WriteLine("配置中没有任何供应商（{0}）。", file);
                return;
            }
            XTrace.WriteLine("供应商列表（{0}）：", file);
            foreach (var p in settings.Providers)
                XTrace.WriteLine("  - {0}  [{1}]  {2}", p.Id, p.ApiMode, KeyStatus(p));
        }

        private static void ShowProvider(ProviderOptions p)
        {
            XTrace.WriteLine("供应商：{0}", p.Id);
            XTrace.WriteLine("  名称：{0}", p.Name ?? p.Id);
            XTrace.WriteLine("  模式：{0}", p.ApiMode);
            XTrace.WriteLine("  密钥：{0}", KeyStatus(p));
            if (!String.IsNullOrEmpty(p.ProtectedApiKey))
                XTrace.WriteLine("  存储值：{0}", p.ProtectedApiKey);
        }

        private static String KeyStatus(ProviderOptions p)
        {
            if (!String.IsNullOrEmpty(p.ApiKey)) return "明文";
            if (!String.IsNullOrEmpty(p.ProtectedApiKey))
            {
                if (p.ProtectedApiKey.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                    return "环境变量注入：" + p.ProtectedApiKey.Substring(4);
                if (p.ProtectedApiKey.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
                    return "已加密(dpapi)";
                return "其它形式";
            }
            return "无";
        }

        private static void PrintUsage()
        {
            XTrace.WriteLine("用法：");
            XTrace.WriteLine("  setkey --list                            列出所有供应商及其密钥状态");
            XTrace.WriteLine("  setkey <providerId> <apiKey>            写入加密密钥(dpapi:)");
            XTrace.WriteLine("  setkey <providerId> --env <ENV_NAME>    改用环境变量注入(env:NAME)");
            XTrace.WriteLine("  setkey <providerId> --show              查看该供应商密钥状态");
            XTrace.WriteLine("  setkey <providerId> --clear             清除该供应商密钥");
            XTrace.WriteLine("  （以上均可追加 --file <path> 指定配置）");
        }
    }
}
