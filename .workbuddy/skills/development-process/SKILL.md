---
name: development-process
description: This skill should be used when starting a greenfield system or a major iteration in a NewLife project: requirement document, feature list, architecture design, iterative development, and autonomous batch mode. Trigger on "新建系统", "需求分析", "功能清单", "架构设计", "迭代开发", "研发流程", "自治模式".
agent_created: true
---

# 研发全流程

## 用途

覆盖新建系统的完整研发流程：需求文档 → 功能清单 → 架构设计 → 迭代开发 → 自治批处理模式。使 WorkBuddy 像"项目经理+工程师"一样推进。

## 激活条件

- 从零搭建新项目 / 新系统
- 需要把模糊需求落为可执行的功能清单与架构
- 进入多轮迭代或"自治批处理"（一次性完成多个功能点）

## 执行步骤

1. **需求文档**：与用户澄清目标、范围、非功能性需求，用 `Write` 生成 `Doc/需求文档.md`。
2. **功能清单**：拆解为带优先级/验收标准的条目，写入 `Doc/功能清单.md`（表格：编号、名称、优先级、状态）。
3. **架构设计**：调用 `project-architecture` 产出 `Doc/架构设计.md`，明确分层、模块、技术栈、兼容性目标。
4. **迭代开发**：每轮选 1–N 个功能点，结合 `xcode-data-modeling` / `cube-mvc-backend` 实现，提交前跑 `dotnet build` 与 `dotnet test`。
5. **自治批处理模式**：批量选取未实现功能，逐条实现→测试→对照需求自检→更新清单状态→提交，生成检查点报告。
6. 用 `Grep` 抽样验证"已加载"标注与需求覆盖；用 `Read` 回看清单状态。
7. 产物经 `present_files` 呈现给用户评审。

## 文档规范

- `Doc/` 目录标准产物：需求文档、功能清单、架构设计、竞品分析。
- 命名与反模式见 `development-conventions`。

## 关联

- `project-architecture`、`testing-strategy`、`code-review`、`dev-loop`、`doc-sync`
