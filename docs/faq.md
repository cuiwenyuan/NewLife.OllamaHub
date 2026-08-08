# 常见问题

**Q：这跟 mingkuang-Chuyu/OllamaHub 有什么区别？**
A：核心思路一致（伪装 Ollama 让 Copilot 用任意模型）。差异在本项目：纯 NewLife 生态、服务化一等公民（NewLife.Agent，开机自启/自愈）、VS 优先、国内供应商开箱预设、文档/示例/升级齐全。

**Q：会被 GitHub 官方检测到吗？**
A：本项目只是本地 HTTP 代理，Copilot 侧完全走官方支持的 Ollama 提供程序，不涉及破解或绕过协议。

**Q：行内代码补全（ghost text）能用国内模型吗？**
A：不能。这是 GitHub 官方限制：BYOM 只覆盖 Chat / Agent / Plan，补全仍走官方模型。

**Q：只能用国内模型吗？**
A：不。只要上游是 OpenAI 兼容接口即可，OpenRouter 等海外供应商也在预设里。

**Q：必须装成 Windows 服务吗？**
A：不是。开发调试可用 `-run` 前台运行；生产建议装服务以实现开机自启。

**Q：依赖很重吗？**
A：不。仅 `NewLife.Core` + `NewLife.Agent` 两个 NuGet，无 ASP.NET Core、无 Node/Python 运行时，单文件 exe 即可运行。

**Q：Key 安全吗？**
A：`setkey` 命令用本地 AES-256 加密落盘（机器绑定，密钥由固定应用盐 + 机器名派生，语义等同 DPAPI LocalMachine，且零 NuGet 依赖），也支持 `env:NAME` 环境变量注入。

**Q：VS Copilot 连不上局域网/服务器的地址？**
A：两点原因：① Hub 的 `local` 节点默认只绑 `127.0.0.1`，要暴露局域网需启用 `lanHttps`（HTTPS）或 `lanHttp`（明文 HTTP）节点（二者 `host` 默认 `0.0.0.0`）；② **VS 对非 localhost 强制要求 HTTPS**，而 `local` 节点是明文 HTTP。正式做法是在 `settings.json` 启用 `lanHttps`（配 `certificate` PFX 证书），并把证书**导入 VS 所在机器的「受信任的根证书颁发机构」**，VS 即可用 `https://<服务器IP>:11435` 直接连（注意 Endpoint URL **不要**带 `/v1`，VS 会自动拼接；详见《配置参考》的「三监听」与「VS 局域网接入说明」章节）。若确实无法导入证书（受控终端），才退而启用 `lanHttp`（明文 11436）+ 隧道/端口转发把它映射为本机 `127.0.0.1`（localhost 豁免 HTTPS 强制）作为备选。注意 Hub 无鉴权，仅限可信网络使用。详见 Issue #1。
