# NewLife.OllamaHub 推广文案（小红书 + 微信小绿书）

> 定位：让 GitHub Copilot（VS Code 与 Visual Studio 两端）用上国产大模型——一个 exe 把它「伪装」成本地 Ollama。
> 目标读者：.NET 开发者、VS Code 与 Visual Studio 的 GitHub Copilot 用户、想降本的程序员、AI 工具折腾党。
> 语气：技术人写给技术人，不给「神器」「逆天」这类空话。

---

## 一、小红书

### 标题（3 选 1，推荐第 1 条）

1. 一个 exe，让 GitHub Copilot 用上 DeepSeek ← **推荐**
2. 把 DeepSeek 接进 GitHub Copilot，不改客户端
3. VS Code 和 VS 的 GitHub Copilot，都能接国产模型

> 标题里一律写全「GitHub Copilot」；只要不是特指某一端，就带上「VS Code / Visual Studio」。

> 控制在 20 字上下，前 8 个字必须带信息量（模型名 / 工具名 / 痛点）。

### 正文（直接复制）

```
「GitHub Copilot 只能用官方模型？」
不是。GitHub Copilot 在 VS Code 和 Visual Studio（下面简称 VS）两端都支持自带模型（BYO），前提是有一个 Ollama 兼容的本地服务。

于是我把这件事做成了一个 .NET 单文件 exe：NewLife.OllamaHub（同时发布 .NET 8 与 .NET 10 两个版本，按机器装了什么运行时选）。
它干的事很朴素——在本机起一个「假 Ollama」（127.0.0.1:11434），客户端以为在跟 Ollama 说话，实际请求被转给 DeepSeek、GLM、Kimi、通义千问这些云端大模型，回答再翻译回 Ollama 格式。

为什么值得折腾 👇
① 不用装 Docker、不用 Python，装了 .NET 运行时双击 exe 就跑
② 内置 12 家供应商，改一行 JSON 换模型
③ 支持 OpenAI / Anthropic / Gemini / Responses 多协议
④ 能开局域网共享，一台机器当团队模型网关
⑤ 自带 self-test，306 项断言，升级不怕踩雷

三步上手：
下载 exe → 填 API Key → 在 VS Code 或 Visual Studio 的模型提供程序里选 Ollama，开聊。

两端注意点不一样：
VS Code 里走 Manage Models → Ollama，填本机地址；
Visual Studio 里同样选 Ollama 提供程序，但接局域网时它要求 HTTPS，得配一张被它信任的证书。

说点实话：
它不占显存，本质是协议翻译加转发；效果取决于你接的上游模型，成本也看你选的模型和额度，各家差距不小。

项目开源在这：
GitHub：cuiwenyuan/NewLife.OllamaHub
.NET 开发者可以直接读源码，非 .NET 也能只用 exe。

你平时写代码用哪个国产模型？评论区聊聊 👇
```

### 话题标签

```
#程序员 #VSCode #GitHubCopilot #DeepSeek #国产大模型 #开源项目 #dotnet #AI工具 #效率工具 #编程日常
```

### 配图（8 张，3:4，对应 xiaohongshu-cards.html）

| 序号 | 内容 | 作用 |
|---|---|---|
| 1 | 封面：把 DeepSeek 接进 GitHub Copilot（VS Code / Visual Studio） | 决定点击率，字最大 |
| 2 | 痛点：官方贵 / 自己折腾劝退 | 制造共鸣 |
| 3 | 解法：一个 exe 伪装成本地 Ollama | 给答案 |
| 4 | 原理流程图：GitHub Copilot（VS Code / Visual Studio）→ Hub → 上游 | 建立可信度 |
| 5 | 12 家供应商一览 | 展示覆盖面 |
| 6 | 三步上手 | 降低门槛 |
| 7 | 进阶：局域网 / 多协议 / 自检 / 服务化 | 展示深度 |
| 8 | 结尾：GitHub 地址 + 互动提问 | 引导评论与 Star |

---

## 二、微信小绿书（图片贴图）

### 标题

```
一个 exe，让 GitHub Copilot 用上国产大模型
```

### 正文（直接复制）

```
GitHub Copilot 在 VS Code 和 Visual Studio（VS）两端都支持自带模型，前提是有一个 Ollama 兼容的本地服务。

NewLife.OllamaHub 干的就是这件事：本机起一个「假 Ollama」，把请求转给 DeepSeek、GLM、Kimi、通义千问等 12 家国产/海外大模型，再把回答翻译回 Ollama 格式。

单文件 exe，装 .NET 运行时后双击即用（.NET 8 / .NET 10 双版本）；改一行 JSON 换模型；还能开局域网给团队共用；自带 306 项 self-test。

VS Code 与 Visual Studio 都在模型提供程序里选 Ollama 即可；VS 接局域网时需要一张受信任的 HTTPS 证书。

开源免费，GitHub 搜 NewLife.OllamaHub。
你写代码常用哪个模型？
```

> 小绿书正文建议再砍短，图片承担主要信息，文字只做「补充一句」。

### 配图（6 张，3:4，对应 wechat-xiaolvshu-cards.html）

| 序号 | 内容 |
|---|---|
| 1 | 封面：让 GitHub Copilot 用上国产大模型 |
| 2 | 前提：GitHub Copilot 支持自带模型，卡在「Ollama 兼容」 |
| 3 | 它做了什么：空壳 Ollama + 协议翻译 |
| 4 | 12 家供应商 |
| 5 | 三步上手 |
| 6 | 亮点（局域网 / 自检 / 服务化）+ GitHub 地址 |

---

## 三、发布建议

- **时间**：小红书晚 20:00–22:00；微信图片消息午 12:00–13:00 或晚 21:00 前后。
- **封面**：只用第 1 张，别拼图；封面主标题不超过 12 字，否则手机上会被压缩得看不清。
- **首评引导**：发布后自己先置顶一条评论，例如「需要配置模板的可以留言，我整理了一份 settings.json 示例」。
- **配图导出**：直接打开对应 HTML，用浏览器整页截图或开发者工具逐块截取；尺寸 1080×1440（3:4）。

## 四、合规提醒

- 不写「免费无限使用」「比官方强」这类绝对化表述；本项目只做协议转换，不提供模型额度。
- 引用具体模型名与供应商时保持客观，不贬低任何厂商。
- 涉及 Key 安全时提醒一句：密钥只存本机，局域网暴露等于 Key 暴露，仅限可信网络。
