using NewLife.Agent;
using NewLife.Agent.Command;
using NewLife.Log;
using NewLife.OllamaHub.Commands;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Core;
using NewLife.OllamaHub.Http;

namespace NewLife.OllamaHub;

/// <summary>
/// NewLife.OllamaHub 的 Windows 服务宿主，派生自 NewLife.Agent.ServiceBase。
/// 负责在系统/手动启动时加载配置、拉起 Ollama 兼容 HTTP 服务，停止时释放资源。
/// </summary>
public class HubAgentService : ServiceBase
{
    private OllamaHttpServer? _server;

    /// <summary>构造并设定服务元信息（服务名/显示名/描述）。</summary>
    public HubAgentService()
    {
        ServiceName = "NewLife.OllamaHub";
        DisplayName = "NewLife.OllamaHub";
        Description = "将 DeepSeek 等国内大模型伪装成本地 Ollama，供 Visual Studio / VS Code 的 GitHub Copilot 使用。";

        // M6 收尾：把「供应商预设」「配置 API Key」「配置向导」注册为 NewLife.Agent 的可视化菜单命令。
        // 采用官方推荐写法：继承 BaseCommandHandler，由 ServiceBase 自动扫描派生类所在程序集并注册，
        // 不再使用已过时（Obsolete）的 AddMenu(Char,String,Action) 重载。
        // 字母键 p/k/c 避免与内置数字键（1 状态 / 2 安装卸载 / 3 启停 / 4 重启 / 0 退出）冲突。
        // 命令处理器定义见本类末尾的嵌套子类（PresetMenuCommand / ApiKeyMenuCommand / WizardMenuCommand）。
    }

    /// <summary>服务启动：加载配置并启动 HTTP 服务。</summary>
    /// <param name="reason">启动原因（系统/手动/服务控制）。</param>
    public override void StartWork(String reason)
    {
        // 先加载注册表（OllamaHttpServer 内部读取 ModelRegistry.Instance）
        ModelRegistry.Instance.Load();

        var settings = ModelRegistry.Instance.Settings;
        _server = new OllamaHttpServer(settings);
        _server.Start();
        XTrace.WriteLine("NewLife.OllamaHub 已启动，监听 {0}", _server.ListenUrl);
    }

    /// <summary>服务停止：释放 HTTP 服务并清空引用。</summary>
    /// <param name="reason">停止原因。</param>
    public override void StopWork(String reason)
    {
        // 防御：停止阶段避免空引用；失败时记录但不向上抛，防止服务控制管理器卡死
        try
        {
            _server?.Stop();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
        _server = null;
        XTrace.WriteLine("NewLife.OllamaHub 已停止。");
    }

    #region 可视化菜单：交互式配置（M6 收尾）

    /// <summary>菜单项 P：交互式生成供应商预设并写入 settings.json。</summary>
    private void ConfigurePresets()
    {
        try
        {
            XTrace.UseConsole();
            Console.WriteLine();
            Console.WriteLine("=== 生成供应商预设 ===");
            Console.WriteLine("可选供应商（运行 `presets` 命令也可查看全部）：");
            foreach (var p in ProviderPresets.All)
                Console.WriteLine("  {0,-12} {1}", p.Id, p.Name);
            Console.WriteLine();
            Console.Write("请输入要启用的供应商 id（空格分隔多个；输入 all 启用全部）：");
            Console.Out.Flush();
            var ids = PickPresetIds();
            if (ids == null) return;

            var args = new List<String> { "presets" };
            args.AddRange(ids);
            args.Add("--write");

            // settings.json 已存在时先确认，避免误覆盖
            var file = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(file))
            {
                Console.Write("检测到 settings.json 已存在，是否覆盖？(y/N)：");
                Console.Out.Flush();
                var key = Console.ReadKey(intercept: true).KeyChar;
                Console.WriteLine(key);
                if (!"yY".Contains(key))
                {
                    Console.WriteLine("已取消。可先备份旧配置，或用 K 菜单单独配置密钥。");
                    return;
                }
                args.Add("--force");
            }

            PresetsCommand.Run(args.ToArray());
            Console.WriteLine("提示：预设已写入。接下来用 K 菜单项为各供应商配置 API Key。");
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>菜单项 K：交互式配置/修改某供应商的 API Key。</summary>
    private void ConfigureApiKey()
    {
        try
        {
            XTrace.UseConsole();
            Console.WriteLine();
            Console.WriteLine("=== 配置/修改大模型 API Key ===");
            var file = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(file))
            {
                var settings = HubSettings.Load(file);
                if (settings.Providers.Count == 0)
                    Console.WriteLine("提示：settings.json 中还没有任何供应商，建议先用 P 菜单生成预设。");
                else
                {
                    Console.WriteLine("当前已配置的供应商：");
                    foreach (var p in settings.Providers)
                        Console.WriteLine("  - {0}", p.Id);
                }
            }
            else
                Console.WriteLine("提示：尚未生成 settings.json，建议先用 P 菜单生成预设。");

            Console.WriteLine();
            Console.Write("请输入供应商 id：");
            Console.Out.Flush();
            var id = (Console.ReadLine() ?? "").Trim();
            if (String.IsNullOrEmpty(id)) { Console.WriteLine("未输入，已取消。"); return; }

            Console.Write("请输入 API Key：");
            Console.Out.Flush();
            var key = (Console.ReadLine() ?? "").Trim();
            if (String.IsNullOrEmpty(key)) { Console.WriteLine("未输入，已取消。"); return; }

            SetKeyCommand.Run(new[] { "setkey", id, key });
            Console.WriteLine("如服务正在运行，配置将在数秒内通过热重载自动生效。");
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>菜单项 C：配置向导——先生成预设，再逐个提示填各供应商 API Key。</summary>
    private void ConfigureWizard()
    {
        try
        {
            XTrace.UseConsole();
            Console.WriteLine();
            Console.WriteLine("=== 配置向导 ===");
            var file = Path.Combine(AppContext.BaseDirectory, "settings.json");

            List<String>? chosenIds = null;
            if (File.Exists(file))
            {
                Console.Write("已存在 settings.json，是否重新生成预设并覆盖？(y/N)：");
                Console.Out.Flush();
                var ack = Console.ReadKey(intercept: true).KeyChar;
                Console.WriteLine(ack);
                if ("yY".Contains(ack))
                {
                    chosenIds = PickPresetIds();
                    if (chosenIds == null) return;
                    var pargs = new List<String> { "presets" };
                    pargs.AddRange(chosenIds);
                    pargs.Add("--write");
                    pargs.Add("--force");
                    PresetsCommand.Run(pargs.ToArray());
                }
                else
                {
                    chosenIds = HubSettings.Load(file).Providers.Select(p => p.Id).ToList();
                    Console.WriteLine("沿用现有 {0} 个供应商。", chosenIds.Count);
                }
            }
            else
            {
                chosenIds = PickPresetIds();
                if (chosenIds == null) return;
                var pargs = new List<String> { "presets" };
                pargs.AddRange(chosenIds);
                pargs.Add("--write");
                PresetsCommand.Run(pargs.ToArray());
            }
            if (chosenIds == null) return;

            Console.WriteLine();
            Console.WriteLine("接下来为每个供应商配置 API Key（直接回车跳过）：");
            foreach (var id in chosenIds)
            {
                Console.Write($"[{id}] API Key（留空跳过）：");
                Console.Out.Flush();
                var key = (Console.ReadLine() ?? "").Trim();
                if (!String.IsNullOrEmpty(key))
                    SetKeyCommand.Run(new[] { "setkey", id, key });
            }
            Console.WriteLine("配置完成。如服务正在运行，将自动热重载生效。");
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>从控制台读取并解析供应商 id 选择（支持 all / 空格或逗号分隔多个）。</summary>
    /// <returns>选中的 id 列表；用户直接回车取消时返回 null。</returns>
    private static List<String>? PickPresetIds()
    {
        Console.Out.Flush();
        var line = (Console.ReadLine() ?? "").Trim();
        if (String.IsNullOrEmpty(line)) { Console.WriteLine("未输入任何供应商 id，已取消。"); return null; }
        if (line.Equals("all", StringComparison.OrdinalIgnoreCase))
            return ProviderPresets.All.Select(p => p.Id).ToList();
        return line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    #endregion

    #region 可视化菜单命令处理器（继承 BaseCommandHandler，由 ServiceBase 自动扫描注册）

    /// <summary>菜单项 p：交互式生成供应商预设并写入 settings.json。</summary>
    private class PresetMenuCommand : BaseCommandHandler
    {
        public PresetMenuCommand(ServiceBase service) : base(service)
        {
            Cmd = "preset";
            Description = "生成供应商预设 (presets → settings.json)";
            ShortcutKey = 'p';
        }

        public override Boolean IsShowMenu() => true;

        public override void Process(String[] args) => ((HubAgentService)Service).ConfigurePresets();
    }

    /// <summary>菜单项 k：交互式配置/修改某供应商的 API Key。</summary>
    private class ApiKeyMenuCommand : BaseCommandHandler
    {
        public ApiKeyMenuCommand(ServiceBase service) : base(service)
        {
            Cmd = "setkey-menu";
            Description = "配置/修改大模型 API Key (setkey)";
            ShortcutKey = 'k';
        }

        public override Boolean IsShowMenu() => true;

        public override void Process(String[] args) => ((HubAgentService)Service).ConfigureApiKey();
    }

    /// <summary>菜单项 c：配置向导——先生成预设，再逐个提示填各供应商 API Key。</summary>
    private class WizardMenuCommand : BaseCommandHandler
    {
        public WizardMenuCommand(ServiceBase service) : base(service)
        {
            Cmd = "wizard";
            Description = "配置向导（预设 + API Key 一次完成）";
            ShortcutKey = 'c';
        }

        public override Boolean IsShowMenu() => true;

        public override void Process(String[] args) => ((HubAgentService)Service).ConfigureWizard();
    }

    #endregion
}
