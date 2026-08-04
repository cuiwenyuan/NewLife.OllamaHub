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
2. GitHub `releases/latest`：自动取 `tag_name`（兼容前导 `v`）与首个 `browser_download_url`。

## 方式二：手动替换

从 [Releases](https://github.com/cuiwenyuan/NewLife.OllamaHub/releases) 下载单文件，覆盖原目录即可。`settings.json` 与 `Log/` 不会被覆盖（请勿删除）。

## 版本与发布

- 程序集语义化版本（`Version`），可通过 `/api/version` 或 `upgrade --check` 查看当前版本。
- 打 Git tag（如 `v1.0.0`）会触发 GitHub Actions 自动构建并发布单文件 exe。
- 升级前建议备份 `settings.json`。
