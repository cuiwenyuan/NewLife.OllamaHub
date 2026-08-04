---
name: doc-sync
description: This skill should be used when synchronizing code and documentation bidirectionally for a NewLife project: rebuild the doc system from code, or audit code consistency against docs. Trigger on "文档同步", "代码文档对齐", "doc sync", "文档审计", "文档不一致".
agent_created: true
---

# 代码↔文档同步

## 用途

双向同步代码与文档：从代码重建文档体系，或用文档审计代码一致性。对应原 Copilot 的 `@doc-sync` 智能体。

## 激活条件

- 用户说"代码和文档对一下""根据代码更新文档""审计文档是否过期"
- 发版前文档校准（配合 `release-prep`）

## 执行步骤

1. 用 `Glob`/`Grep` 摸清代码结构（公共类型、API 表面、XML 注释）。
2. **由代码生文档**：读取 XML 注释与签名，用 `Write` 生成/更新 `Doc/` 下的 API 与说明文档（参 `doc-writer`）。
3. **由文档审代码**：用 `Read` 对比文档声明与代码实际行为，列出差异清单（缺失实现、行为不符、过期描述）。
4. 对差异给出处置：代码改代码，文档改文档；不杜撰。
5. 用 `Edit` 落地修正，用 `Bash` 跑 `dotnet build` 确认无回归。
6. 输出同步报告：更新项、待决项。

## 关联

- `doc-writer`、`xml-comment-enrichment`、`release-prep`
