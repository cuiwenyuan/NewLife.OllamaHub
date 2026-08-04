using System;
using System.Security.Cryptography;
using System.Text;
using NewLife.Log;
using NewLife.OllamaHub.Config;

namespace NewLife.OllamaHub.Security;

/// <summary>
/// API Key 保护解析（M1 可用版，密钥保护全程在 NewLife 生态 + BCL 内，无第三方依赖）。
/// 解析优先级：明文 apiKey &gt; env:NAME（环境变量）&gt; dpapi:（本地 AES 加密串）。
/// 加密采用 BCL System.Security.Cryptography.AES-256（CBC，随机 IV 作熵），密钥由
/// 固定应用盐 + 机器名派生（LocalMachine 语义：仅本机可解密，等同 DPAPI LocalMachine）。
/// 本机威胁模型下与 DPAPI LocalMachine 等价，且不引入任何 NuGet 依赖。
/// </summary>
public static class SecretProtector
{
    private const String Prefix = "dpapi:";

    // 应用盐：避免密钥直接等于机器名；与机器名一并 SHA-256 派生为 32 字节 AES 密钥
    private static readonly Byte[] _key = DeriveKey();

    /// <summary>解析供应商的可用 Key。</summary>
    /// <param name="provider">供应商配置（不可为 null）。</param>
    /// <returns>解密后的明文 Key；任何解析失败返回空字符串（不抛异常，由调用方提示）。</returns>
    public static String Resolve(ProviderOptions provider)
    {
        if (provider == null) return "";
        // 1) 明文（开发便利，生产不建议提交到仓库）
        if (!String.IsNullOrEmpty(provider.ApiKey)) return provider.ApiKey!;

        var raw = provider.ProtectedApiKey;
        if (String.IsNullOrEmpty(raw)) return "";

        // 2) 环境变量注入：env:NHUB_KEY_DEEPSEEK
        if (raw.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var name = raw.Substring(4).Trim();
            return Environment.GetEnvironmentVariable(name) ?? "";
        }

        // 3) 本地 AES 密文：dpapi:&lt;base64(IV+cipher)&gt;
        if (raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var blob = Convert.FromBase64String(raw.Substring(Prefix.Length));
                if (blob.Length <= 16) return "";
                var iv = new Byte[16];
                Buffer.BlockCopy(blob, 0, iv, 0, 16);
                var cipher = new Byte[blob.Length - 16];
                Buffer.BlockCopy(blob, 16, cipher, 0, cipher.Length);
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv;
                using var dec = aes.CreateDecryptor();
                var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
                return "";
            }
        }

        // 兜底：视为明文
        return raw;
    }

    /// <summary>用本地 AES 加密明文 Key，返回 dpapi: 前缀的存储串。</summary>
    /// <param name="plain">明文 Key。</param>
    /// <returns>dpapi: 前缀密文；空输入返回空串。</returns>
    public static String Protect(String plain)
    {
        if (String.IsNullOrEmpty(plain)) return "";
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV(); // 随机 IV 即"熵"，每次加密结果不同，防重放/字典
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, plain.Length);
        var blob = new Byte[16 + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, blob, 0, 16);
        Buffer.BlockCopy(cipher, 0, blob, 16, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    private static Byte[] DeriveKey()
    {
        // 固定应用盐 + 机器名 → 机器绑定密钥（LocalMachine 语义）
        var salt = "NewLife.OllamaHub::v1::";
        var material = salt + (Environment.MachineName ?? "localhost");
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(material));
    }
}
