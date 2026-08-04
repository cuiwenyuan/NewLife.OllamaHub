# Visual Studio / VS Code 接入 NewLife.OllamaHub

## 前置

1. 已安装并启动 NewLife.OllamaHub 服务（或 `-run` 前台运行），默认端点 `http://127.0.0.1:11434`。
2. `settings.json` 中至少配置好一个 provider 与 model（参考 `examples/` 与根目录 `settings.sample.json`）。
3. Visual Studio 2026 18.6.0+（或 VS Code + Copilot Chat 0.41+），并已登录 GitHub 账号（个人账号）。

## 在 Visual Studio 中

1. 打开 **Copilot Chat** 窗口。
2. 在模型选择下拉中，选择 **Manage Models（管理模型）**。
3. 提供程序选择 **Ollama**。
4. 端点填写：`http://localhost:11434`（若改过端口则填对应端口）。
5. 点击 **Add（添加）**，稍候转圈完成后，配置在 `settings.json` 里的模型即出现在列表中。
6. 勾选需要的模型，点击 **Save（保存）**。
7. 回到 Copilot Chat，模型选择器里即包含你添加的模型，正常对话 / 使用 Agent 模式即可。

## 在 VS Code 中

1. 打开 Copilot Chat，点击模型选择器 → **Manage Models**。
2. 选择 **Ollama**，端点填 `http://localhost:11434`，添加并保存。

## 验证

```bash
curl http://127.0.0.1:11434/api/tags
```

应返回包含你配置模型的 JSON。若为空，检查 `Log/` 下“已加载 N 个模型”日志与端口占用。

## 排错

- 415 错误：检查 provider/model 的 `headers` 是否含 `Content-Type: application/json`（见 `docs/troubleshooting.md`）。
- 模型不显示：确认端点可达、VS 版本够新、登录的是个人账号。
- Agent 模式不可用：确认模型 `tools: true`（`/api/show` 返回 `capabilities` 含 `tools`）。
