# 升级

## 方式一：upgrade 命令（推荐，零依赖）

程序内置 `upgrade` 子命令，使用 BCL `HttpClient` 拉取 GitHub Release 最新资产，自替换后借 `NewLife.Agent` 自动重启服务——整个过程不依赖任何第三方工具。

```bat
NewLife.OllamaHub.exe upgrade
```

- `upgrade`：拉取最新并替换重启。
- `upgrade --check`：仅检查是否有新版本，不下载、不替换（已实装）。
- `upgrade --dry-run`：下载新版本到临时目录验证可下载，但不替换当前程序。
- `upgrade --url <URL>`：覆盖版本清单地址（默认 `https://api.github.com/repos/NewLifeX/DotNet.OllamaHub/releases/latest`，可被 `settings.json` 的 `upgradeUrl` 覆盖）。

版本清单兼容两种 JSON 形态：

1. 普通清单：`{ "version": "1.2.3", "url": "<exe下载地址>", "notes": "..." }`
2. GitHub `releases/latest`：自动取 `tag_name`（兼容前导 `v`），并从 `assets` 里**挑选与当前运行时匹配的产物**——当前进程是 net8.0 版就取 `*-net8.0.exe`，是 net10.0 版就取 `*-net10.0.exe`。避免 net8 用户被升级成 net10 产物后，因机器没装 .NET 10 运行时直接起不来。若 Release 只发了一个不带框架标记的 exe（早期版本），则回退取第一个。

## 方式二：手动替换

从 [Releases](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases) 下载单文件，覆盖原目录即可。**请下载与现有产物同框架的那个**（原来是 `-net8.0` 就继续用 `-net8.0`）：换了框架但机器没装对应大版本运行时，服务会起不来。`settings.json` 与 `Log/` 不会被覆盖（请勿删除）。

## 版本与发布

- **版本号规则对齐 NewLife 全家桶主干库**：`AssemblyVersion` = `FileVersion` = `主.次.年.月日`，tag 同名（例 `v1.3.2026.1113`）。
  只含日期不含时刻 → 同一天任意次构建版本号恒定（一天一个版本号）；年月日单调递增 → 升级比较天然正确。
- 查看当前版本：`upgrade --check`，或 `GET /api/status` 的 `version` 字段。（`GET /api/version` 返回的是**Ollama 协议版本**，用于客户端探测兼容性，不是本程序版本。）
- 从 exe 右键「属性 → 详细信息」可看到 `文件版本 1.3.2026.1113`、`产品版本 1.3.2026.1113+<提交 sha>`，`sha` 即对应源码提交，便于确认线上跑的是哪次构建。
- 打 Git tag（如 `v1.3.2026.1113`）会触发 GitHub Actions 自动构建，并发布 **两个**单文件 exe：`NewLife.OllamaHub-net8.0.exe` 与 `NewLife.OllamaHub-net10.0.exe`。tag 必须是 4 段格式，否则 CI 直接失败。
- 升级前建议备份 `settings.json`。
