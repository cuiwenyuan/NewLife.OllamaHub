# Visual Studio 接入指南

本服务启动后，对 GitHub Copilot 而言就是一台运行在 `http://localhost:11434` 的本地 Ollama。下面说明如何在 Visual Studio 的 GitHub Copilot Chat 中把模型切到 OllamaHub。

> 也适用于 VS Code：本质都是让 Copilot 把 `localhost:11434` 当作 Ollama 端点。

## 前置条件

1. 已按 [install-as-service.md](install-as-service.md) 安装并启动 `NewLife.OllamaHub` 服务。
2. 已通过菜单 `p` / `k` / `c` 或命令行完成供应商预设与 API Key 配置。
3. 浏览器访问 `http://localhost:11434/admin` 能看到模型列表，确认服务正常。

## 配置步骤

### 1. 打开模型下拉框

在 Visual Studio 的 **GitHub Copilot Chat** 窗口底部，点击当前模型名称（如 `GPT-5 mini`），选择 **Manage models**。

![模型下拉框](assets/vs-copilot-model-dropdown.png)

### 2. 选择 Ollama 提供商

在弹出的 **Bring Your Own Model** 对话框中：

- **Choose an available provider** 下拉选择 **Ollama**。

![选择 Ollama 提供商](assets/vs-copilot-provider-ollama.png)

### 3. 填写本地端点

- **Endpoint URL** 保持默认的 `http://localhost:11434`（即 OllamaHub 默认监听地址）。
- 点击 **Add**。

> 若 OllamaHub 改为其他端口，请填入实际地址，如 `http://localhost:11501`。

![填写端点地址](assets/vs-copilot-endpoint-url.png)

### 4. 勾选要使用的模型

OllamaHub 会把 `settings.json` 里配置的模型以 Ollama 格式暴露出来。勾选你需要的模型（如 `deepseek-v4-flash`、`deepseek-v4-pro`），然后点击 **Save**。

![勾选模型](assets/vs-copilot-select-models.png)

### 5. 切换模型开始使用

保存后，再次点击模型下拉框，会在 **Other models** 下看到刚才添加的 Ollama 模型。选中 `deepseek-v4-flash` 等模型后，发送消息即可看到模型实际应答。

![使用 deepseek-v4-flash 提问](assets/vs-copilot-deepseek-v4-flash-in-use.png)

## 局域网接入（VS 在另一台机器）

若 VS 与 OllamaHub **不在同一台机器**，走局域网而非 `localhost`，需要额外处理：

VS 的 “Ollama” BYO 提供商对非 localhost 地址**只允许 HTTPS**，但自签证书校验在运行时会导致模型列表取不到（502 / 空）。最简单的解决办法：

1. 在 OllamaHub 的 `settings.json` 启用 **`lanHttp`**（局域网明文 HTTP，默认端口 `11436`，无证书）：
   ```jsonc
   { "lanHttp": { "enabled": true, "host": "0.0.0.0", "port": 11436 } }
   ```
2. 在 VS 里添加 Ollama 时，**先**填 `https://<OllamaHub服务器IP>:11435/v1` 保存（VS 只接受这种形式）。
3. 关闭 VS，编辑其 `ConfiguredBringYourOwnModel_v1.json`，把其中的 `https://...:11435/v1` **改回** `http://<OllamaHub服务器IP>:11436/v1`（即指向 `lanHttp` 端口）。
4. 重新打开 VS，模型列表即可正常加载，后续调用也走 `lanHttp` 明文 HTTP。

> 若你已把 `lanHttps` 的证书导入 VS 所在机器「受信任的根证书颁发机构」，则可直接填 `https://<服务器IP>:11435/v1` 使用原生 HTTPS，无需第 2–4 步的替换。
>
> 安全提醒：`lanHttp` 为明文 HTTP 且面向局域网，**等同把上游 API Key 暴露给同网段任何人**。仅限可信网络 / VPN 使用，并妥善保管 Key。详见 [configuration.md](configuration.md) 的「三监听」与「VS 局域网接入说明」。

## 常见问题

- **模型列表为空**：先检查 `/admin` 页面里「模型与用量」是否有模型；若没有，执行 `NewLife.OllamaHub.exe presets deepseek --write` 生成预设，并用 `k` 菜单或 `setkey` 命令填入 API Key。
- **连接失败 / 模型加载不出**：确认服务已启动，且 VS 与 OllamaHub 在同一台机器（或访问 `localhost`）。
- **局域网添加 Ollama 后模型出不来**：这是 VS 对自签 HTTPS 强制校验所致，按上文「局域网接入」用 `lanHttp` + 改配置文件的 workaround 解决；或让 `lanHttps` 证书被 VS 机器信任。
- **没有 Ollama 选项**：更新 Visual Studio / GitHub Copilot 扩展至较新版本；部分旧版本不支持 Bring Your Own Model。
