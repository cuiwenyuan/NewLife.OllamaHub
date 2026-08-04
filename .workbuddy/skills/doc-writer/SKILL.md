---
name: doc-writer
description: This skill should be used when generating high-quality Markdown documentation from C# source code, using its XML comments and public API surface. Trigger on "生成文档", "代码文档", "doc writer", "API 文档", "根据代码写文档".
agent_created: true
---

# 代码文档生成器

## 用途

为 C# 代码生成高质量 Markdown 文档：依据 XML 注释与公共 API 表面产出易读说明。对应原 Copilot 的 `doc-writer.prompt.md`。

## 激活条件

- 用户要求"为这段代码生成文档""输出 API 文档"
- 发版前补齐 `Doc/` 文档（配合 `doc-sync`）

## 执行步骤

1. 用 `Glob`/`Grep` 收集目标类型与方法；用 `Read` 读取签名与 XML 注释。
2. 以 XML 注释为单一事实源（见 `xml-comment-enrichment`），不杜撰行为。
3. 用 `Write` 生成 Markdown：
   - 标题 + 一句话概述（来自 `<summary>`）
   - 成员表：名称、说明、参数（`<param>`）、返回（`<returns>`）
   - 关键用法示例（`<example>`）
4. 保持与 `newlife-global-standards` 命名/术语一致；代码块标注语言（`csharp`）。
5. 产物经 `present_files` 呈现，或写入 `Doc/` 对应位置。

## 关联

- `xml-comment-enrichment`、`doc-sync`、`newlife-global-standards`
