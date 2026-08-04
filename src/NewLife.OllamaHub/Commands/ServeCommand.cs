using System;
using System.Threading;
using NewLife.Log;
using NewLife.OllamaHub.Core;
using NewLife.OllamaHub.Http;

namespace NewLife.OllamaHub.Commands;

/// <summary>
/// 前台运行命令（serve）。
/// 不安装服务、不进入 NewLife.Agent 的交互式菜单，直接在当前进程启动 HTTP 服务并阻塞等待退出信号。
/// 适用于容器（Docker/Linux）、CI 冒烟测试与本地调试。
/// 与 Agent 的 -run（模拟运行）区别：本模式用事件阻塞而非 Console.ReadKey，
/// 因此在 stdin 被重定向（后台进程、容器、CI）的场景下同样可靠。
/// </summary>
public static class ServeCommand
{
    /// <summary>执行前台运行，直到收到 Ctrl+C 或进程退出信号。</summary>
    /// <param name="args">命令行参数（预留扩展，当前不解析额外开关）。</param>
    /// <returns>进程退出码，0 表示正常停止。</returns>
    public static Int32 Run(String[] args)
    {
        XTrace.UseConsole();
        OllamaHttpServer? server = null;
        try
        {
            ModelRegistry.Instance.Load();

            server = new OllamaHttpServer(ModelRegistry.Instance.Settings);
            server.Start();
        }
        catch (Exception ex)
        {
            // 启动阶段失败（端口占用、配置损坏等）必须立刻暴露，避免"假装启动成功"
            XTrace.WriteException(ex);
            return 1;
        }

        // 用事件阻塞而非 Console.ReadKey：后台/容器环境下 stdin 被重定向时依旧可靠
        using var quit = new ManualResetEventSlim(false);

        // Ctrl+C / Ctrl+Break：取消默认的强制终止，转为优雅停止
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        // docker stop / SIGTERM / 宿主关闭等进程退出信号
        AppDomain.CurrentDomain.ProcessExit += (s, e) => quit.Set();

        XTrace.WriteLine("前台运行中（serve），按 Ctrl+C 停止……");
        quit.Wait();

        server.Stop();
        return 0;
    }
}
