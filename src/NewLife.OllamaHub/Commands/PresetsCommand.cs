using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Commands;

/// <summary>
/// 内置供应商预设子命令（M4）。
///   - <c>presets</c>                 列出全部 9 家内置预设（Id / 名称 / BaseUrl）
///   - <c>presets &lt;id&gt; [&lt;id&gt;...]</c>   生成含这些供应商与已知模型的 settings.json 脚手架（不含密钥）
///   - 追加 <c>--write</c>            把脚手架写入程序目录 settings.json（已存在则拒绝，除非 <c>--force</c>）
/// 生成后请用 <c>setkey &lt;providerId&gt; &lt;APIKey&gt;</c> 写入密钥。
/// </summary>
public static class PresetsCommand
{
    /// <summary>执行预设子命令。</summary>
    /// <param name="args">完整命令行参数（args[0] 应为 "presets"）。</param>
    /// <returns>进程退出码，0 表示成功，非 0 表示参数错误或写入被拒。</returns>
    public static Int32 Run(String[] args)
    {
        XTrace.UseConsole();

        var rest = args.Skip(1).ToList();
        var write = false;
        var force = false;
        var ids = new List<String>();
        foreach (var a in rest)
        {
            if (a == "--write") write = true;
            else if (a == "--force") force = true;
            else ids.Add(a);
        }

        // 无 Id → 列出全部预设
        if (ids.Count == 0)
        {
            Console.WriteLine("内置供应商预设（共 {0} 家）。生成配置：presets <id>；直接写入：presets <id> --write",
                ProviderPresets.All.Count);
            Console.WriteLine();
            Console.WriteLine("  {0,-12} {1,-10} {2}", "ID", "名称", "BaseUrl");
            Console.WriteLine("  {0,-12} {1,-10} {2}", "----", "----", "-------");
            foreach (var p in ProviderPresets.All)
                Console.WriteLine("  {0,-12} {1,-10} {2}", p.Id, p.Name, p.BaseUrl);
            return 0;
        }

        // 解析 Id → 预设
        var chosen = new List<ProviderPreset>();
        foreach (var id in ids)
        {
            var p = ProviderPresets.Find(id);
            if (p == null)
            {
                Console.WriteLine("未知预设：{0}（运行 `presets` 查看全部）", id);
                return 1;
            }
            chosen.Add(p);
        }

        var settings = ProviderPresets.BuildSettings(chosen);
        settings.Normalize();
        var json = JsonHelper.ToJson(settings, true);

        if (!write)
        {
            Console.WriteLine(json);
            Console.WriteLine();
            Console.WriteLine("提示：密钥未包含。运行 `setkey {0} <你的APIKey>` 写入后即为可用配置。",
                String.Join(" ", chosen.Select(c => c.Id)));
            return 0;
        }

        // --write：落盘到程序目录 settings.json
        var file = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (File.Exists(file) && !force)
        {
            Console.WriteLine("已存在 settings.json，拒绝覆盖。请先备份/删除，或用 --force 强制覆盖。");
            return 1;
        }
        File.WriteAllText(file, json);
        Console.WriteLine("已写入 {0}（含 {1} 家供应商 / {2} 个模型）。运行 `setkey <providerId> <APIKey>` 写入密钥。",
            file, settings.Providers.Count, settings.Models.Count);
        return 0;
    }
}
