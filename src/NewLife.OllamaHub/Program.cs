using System;
using NewLife;

namespace NewLife.OllamaHub
{
    /// <summary>
    /// NewLife.OllamaHub 程序入口。
    /// 默认进入 Windows 服务模式（交由 NewLife.Agent 处理 -i/-u/-run 等参数）；
    /// 识别到自管子命令（serve/self-test/upgrade/setkey）时直接执行后退出。
    /// </summary>
    public static class Program
    {
        /// <summary>程序入口。</summary>
        /// <param name="args">命令行参数。空或 Agent 参数时进入服务模式；serve/self-test/upgrade/setkey 时直接执行。</param>
        public static void Main(String[] args)
        {
            var cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : String.Empty;
            switch (cmd)
            {
                case "serve":
                case "-serve":
                case "--serve":
                    // 前台运行：容器/CI/调试场景，不进入 Agent 交互式菜单
                    Environment.ExitCode = Commands.ServeCommand.Run(args ?? Array.Empty<String>());
                    return;

                case "self-test":
                    // 内置自检：验证配置加载与协议转换等核心链路，零测试框架
                    Environment.ExitCode = Commands.SelfTest.Run();
                    return;

                case "upgrade":
                    // 自替换 exe 并借 Agent 自动重启（M3 完善）
                    Commands.UpgradeCommand.Run(args ?? Array.Empty<String>());
                    return;

                case "setkey":
                    // 写入加密存储或环境变量（M4 完善）
                    Commands.SetKeyCommand.Run(args ?? Array.Empty<String>());
                    return;

                case "presets":
                    // 列出/生成内置供应商预设（M4 全内置）；传播退出码（拒绝覆盖等）
                    Environment.ExitCode = Commands.PresetsCommand.Run(args ?? Array.Empty<String>());
                    return;

                default:
                    // 其余情况（含 -i/-u/-run/无参）交由 NewLife.Agent 处理服务模式
                    new HubAgentService().Main(args);
                    return;
            }
        }
    }
}
