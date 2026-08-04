# 密钥安全（API Key 保护）

Hub 不要求明文把上游 Key 写在 `settings.json` 里。推荐用 `setkey` 命令以加密形式或环境变量引用写入，避免 Key 泄露到仓库/磁盘明文。

## Key 的三种形态

优先级从高到低：

1. **明文 `apiKey`**：仅开发调试用，生产不建议提交到仓库。
2. **环境变量 `protectedApiKey = "env:NAME"`**：运行时读取名为 `NAME` 的环境变量（建议约定 `NHUB_KEY_<PROVIDER>`，如 `NHUB_KEY_DEEPSEEK`）。
3. **本地密文 `protectedApiKey = "dpapi:<base64>"`**：`setkey` 写入的 AES 加密串。

任何解析失败（如缺环境变量、密文损坏）均回退为空串，由调用方提示「未配置 Key」，不会抛异常。

## `dpapi:` 实际是 BCL AES，不是 Windows DPAPI

> 历史命名 `dpapi:` 沿用自早期方案；**当前实现并未调用 Windows DPAPI**，而是：

- 算法：`System.Security.Cryptography.AES-256`（CBC，PKCS7）。
- 熵：每次加密随机生成 16 字节 IV，拼在密文前（`IV[16] + cipher`），防重放/字典。
- 密钥：由固定应用盐 `NewLife.OllamaHub::v1::` 拼接 `Environment.MachineName`，SHA-256 派生 32 字节。
- 语义：**机器绑定**——仅本机可解密，等价 DPAPI LocalMachine。

这样设计的原因：纯 NewLife 生态 / 零第三方 NuGet 依赖的硬约束下，既要「落盘密文不泄露明文」，又要「跨进程重启仍可解密」。机器名派生密钥天然满足这两点（重装系统/换机则无法解密，符合预期）。

## setkey 命令

```bat
# 列出所有供应商及其密钥状态
NewLife.OllamaHub.exe setkey --list

# 写入加密密钥（dpapi: 前缀，存于 settings.json）
NewLife.OllamaHub.exe setkey <providerId> <apiKey>

# 改用环境变量注入（env:NAME）
NewLife.OllamaHub.exe setkey <providerId> --env NHUB_KEY_DEEPSEEK

# 查看某供应商密钥状态（不泄露明文）
NewLife.OllamaHub.exe setkey <providerId> --show

# 清除某供应商密钥
NewLife.OllamaHub.exe setkey <providerId> --clear

# 以上命令均可追加 --file <path> 指定配置（默认程序目录 settings.json）
NewLife.OllamaHub.exe setkey --file C:\path\settings.json deepseek sk-xxx
```

写入加密密钥会同时把明文 `apiKey` 字段清空，避免双份生效；改用 `env:` 或 `--clear` 同理。
