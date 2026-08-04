# 排错

## DeepSeek 返回 HTTP 415（Expected Content-Type: application/json）

请求头缺少/重复了 `Content-Type`。确保在 provider 或 model 的 `headers` 里写明：

```json
"headers": { "Content-Type": "application/json" }
```

本项目在转发上游时会**只设置一次** `application/json`（不带 charset），规避该问题。

## 端口 11434 被占用（与真实 Ollama 冲突）

- 若本机也装了真实 Ollama，二者不能同占 11434。改本项目的 `port`（如 `2315`），并在 VS 的 Ollama 端点填对应端口；
- 或开启 `aggregateLocalOllama: true` 把真实 Ollama 的模型也聚合进来（此时建议本项目改端口，由本项目统一对外）。

## Copilot 里模型不显示 / 添加后转圈失败

1. 确认端点可达：`curl http://127.0.0.1:<port>/api/tags` 能返回 JSON。
2. 确认已 `Load` 到模型：`Log/` 目录下应有“已加载 N 个模型”日志。
3. 确认 VS / Copilot 为较新版本（VS 2026 18.6.0+）。
4. 登录账号为个人账号（组织受限账号可能看不到“管理模型”）。

## Agent 模式不可用（不能读写文件 / 跑命令）

`/api/show` 的 `capabilities` 必须包含 `tools`。确认模型的 `tools: true`。本项目默认即返回 `["completion","tools"]`。

## 服务启动失败 / 日志在哪

- 服务模式下日志在程序目录的 `Log/` 下（`XTrace` 落盘）。
- 端口占用、配置错误会以中文日志提示，而非直接抛出堆栈。

## 配置改动后不生效

- M0 阶段需重启服务（`-restart`）或重新 `-run`；`aggregateLocalOllama` 与热重载将在 M4 完善。
