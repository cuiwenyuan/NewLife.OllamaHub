---
name: development-conventions
description: This skill should be used when starting a new system, doing requirement analysis, architecture design, iterative development, or entering autonomous mode in a NewLife project. Trigger on "新建系统", "需求分析", "架构设计", "迭代开发", "自治模式", "研发规范".
agent_created: true
---

# 研发约定

## 用途

承载"新建系统 / 需求分析 / 架构设计 / 迭代开发 / 自治模式"的通用研发约定。对应原 Copilot 的 `development.instructions.md`。

## 激活条件

- 启动新项目或新模块
- 做需求拆解、架构决策、迭代规划
- 进入自治/批处理开发

## 核心约定

1. **文档先行**：新建系统先产出 `Doc/需求文档.md` → `Doc/功能清单.md` → `Doc/架构设计.md`（见 `development-process`）。
2. **清单驱动**：功能清单是唯一进度事实源；每条带优先级与验收标准。
3. **小步迭代**：每轮可编译、可测试、可提交；不自建大特性再一次性合入。
4. **自治模式**：`dev-loop` 循环选功能→实现→测试→自检→更新清单→提交，遇阻塞即停。
5. **AI 协作声明**：`README.md` 写明项目类型、编译/测试命令、核心工程，供工具自动读取。
6. **反模式**：禁止无文档大改、禁止跳过测试、禁止清单与代码脱节。

## 关联

- `development-process`、`dev-loop`、`project-architecture`、`implementation-audit`
