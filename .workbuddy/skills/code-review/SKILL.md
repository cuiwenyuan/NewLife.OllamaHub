---
name: code-review
description: This skill should be used when reviewing NewLife/.NET code against the project's conventions: naming, type usage, defensive comments, built-in tool preference, layering, and XML comments. Trigger on "代码审查", "审查代码", "code review", "CR", "帮我看代码", "review".
agent_created: true
---

# 代码审查

## 用途

依据 NewLife 规范审查代码：命名/类型/风格、防御性注释、内置工具优先、分层方向与 XML 注释质量。对应原 Copilot 的 `@code-review` 智能体。

## 激活条件

- 用户提交代码/PR 要求审查
- 合并前质量门禁
- 主动请求"帮我看这段代码"

## 执行步骤

1. 用 `Read`/`Glob`/`Grep` 获取待审改动范围（diff、相关文件）。
2. 对照 `newlife-global-standards` 逐项检查：
   - 命名/类型/风格是否符合硬约束
   - 是否有防御性注释（边界、异常、线程）
   - 是否优先使用内置工具而非重复造轮子
   - 分层/依赖方向是否正确（参 `project-architecture`）
   - 公共成员 XML 注释是否完整（参 `xml-comment-enrichment`）
3. 用 `Write` 生成审查报告：问题分级（阻塞/建议/可选）、位置（文件:行）、修复建议。
4. 严重问题给出可直接应用的 `Edit` 补丁建议；非阻塞项列入后续。
5. 产物经 `present_files` 呈现。

## 关联

- `newlife-global-standards`、`project-architecture`、`xml-comment-enrichment`、`testing-strategy`
