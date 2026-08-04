---
name: newlife-global-standards
description: This skill should be used for ANY task touching NewLife/.NET code or projects. It carries the global hard constraints (load annotations, compatibility targets, naming/type/style rules, defensive comments, built-in tool preference, workflow) that apply across all NewLife work. Trigger on any NewLife/.NET file edit, build, test, or design task.
agent_created: true
---

# NewLife 全局协作规范（硬约束）

## 用途

所有 NewLife 项目通用的硬约束，始终生效。对应原 Copilot 的 `.github/copilot-instructions.md`。本 Skill 在所有 NewLife 相关任务中优先加载，作为其他 Skill 的基线。

## 激活条件

- 任何涉及 NewLife/.NET 代码、构建、测试、设计的任务
- 与其他 Skill 同时生效，本规范的约束优先

## 硬约束

1. **加载标注**：涉及外部库 API 时，先查 XML 注释/官方文档再写代码；不确定时显式说明。
2. **兼容性目标**：默认覆盖 .NET Framework / .NET Core / netstandard 多目标；避免仅某平台可用的 API，除非用户明确。
3. **命名/类型/风格**：
   - 类型与公共成员用帕斯卡；私有字段用 `_camel`；局部变量用 camel。
   - 优先具体类型与只读集合；避免 `var` 掩盖关键类型（复杂表达式除外）。
   - 文件/目录遵循项目现有约定，不另起炉灶。
4. **防御性注释**：边界、异常、线程安全、不可逆操作处加注释说明前提与后果。
5. **内置工具优先**：优先复用 NewLife/BCL 内置能力，避免重复造轮子；确需自研时注明理由。
6. **工作流**：
   - 改动先编译（`dotnet build`）再提交；测试改动先跑（`dotnet test`）。
   - 公共 API 必须 XML 注释（见 `xml-comment-enrichment`）。
   - 文档产物归入 `Doc/`（见 `development-conventions`）。
7. **单一事实源**：API 用法看 XML 注释，架构决策看 Skill，硬约束看本规范；不多处复制。

## 执行要点

在其他 Skill 执行前后，以本规范的约束做最终校验（命名、兼容、注释、构建）。违反项在 `code-review` 中列为阻塞。

## 关联

- 全部其他 NewLife Skill 均以此为准线
