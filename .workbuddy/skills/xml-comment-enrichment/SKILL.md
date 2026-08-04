---
name: xml-comment-enrichment
description: This skill should be used when refreshing or enriching C# XML documentation comments to be AI-friendly: graded completion of <summary>, <param>, <returns>, <example>, and <remarks> across classes and members. Trigger on "XML 注释", "补全注释", "文档注释", "summary", "AI 友好注释", "刷新注释".
agent_created: true
---

# XML 注释富化

## 用途

将 C# 代码的 XML 注释刷新为"AI 友好"形态：分级补全 `<summary>`/`<param>`/`<returns>`/`<example>`/`<remarks>`，作为 Tier 1 文档（类与成员的"如何使用"）。

## 激活条件

- 用户要求"补全/刷新 XML 注释"
- 提交前统一注释质量
- 生成文档（`doc-writer`）前先保证注释完整

## 执行步骤

1. 用 `Glob` 选取目标 `*.cs`，用 `Read` 逐文件阅读类型与成员。
2. 分级补全：
   - 公共类型/方法：必填 `<summary>`，参数补全 `<param name="x">`，有返回值补 `<returns>`。
   - 复杂/易误用成员：补 `<remarks>` 说明约束、副作用、线程安全。
   - 典型用法：补 `<example>`（含简短代码片段）。
3. 用 `Edit` 就地插入/修订注释，保持与成员签名一致（参数名、类型）。
4. 用 `Grep` 校验无遗漏的 `public` 成员缺少 `<summary>`。
5. 用 `Bash` 跑 `dotnet build` 确认无 XML 注释警告（如 `CS1591`）。
6. 不杜撰不存在的行为；注释须与实现一致。

## 关联

- `doc-writer`：注释是自动文档的单一事实源
- `newlife-global-standards`：注释风格硬约束
- `xcode-conventions`：实体类注释约定
