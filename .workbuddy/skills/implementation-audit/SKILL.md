---
name: implementation-audit
description: This skill should be used when auditing a NewLife project's implementation gaps: compare each feature in the requirement/feature list against the actual code to find incompletely implemented features and plan fixes. Trigger on "实现缺口审计", "实现审计", "功能缺口", "implementation audit", "对照需求".
agent_created: true
---

# 实现缺口审计

## 用途

逐功能对照需求文档，发现未完整实现的功能并规划修复。对应原 Copilot 的 `@implementation-audit` 智能体。

## 激活条件

- 用户要"审计实现了多少""哪些功能没做完"
- 里程碑/发版前完整性核查

## 执行步骤

1. 用 `Read` 读取 `Doc/需求文档.md` 与 `Doc/功能清单.md`。
2. 用 `Glob`/`Grep`/`Read` 在代码中逐条检索每个功能点的实现痕迹（类型、方法、路由、测试）。
3. 评级：✅完整 / 🟡部分（核心有、边界缺）/ ❌缺失 / ⚠️走样（行为与需求不符）。
4. 用 `Write` 生成审计表：功能、证据（文件:行或"未找到"）、评级、缺口描述。
5. 对 ❌/🟡 项用 `Edit` 起草修复计划（归入功能清单待办），估算优先级。
6. 输出审计摘要与修复路线图，经 `present_files` 呈现。

## 关联

- `development-process`（功能清单来源）、`dev-loop`（驱动修复）、`doc-sync`（文档一致性）
