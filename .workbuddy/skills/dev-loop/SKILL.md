---
name: dev-loop
description: This skill should be used when running an autonomous development loop for a NewLife project: pick a feature, implement, test, self-check against requirements, update the feature list, commit, and emit a checkpoint report. Trigger on "开发循环", "自治开发", "dev loop", "自动实现功能", "批量实现".
agent_created: true
---

# 自治开发循环

## 用途

驱动"选功能 → 实现 → 测试 → 需求对照自检 → 更新清单 → 提交 → 检查点报告"的自治循环。对应原 Copilot 的 `@dev-loop` 智能体。

## 激活条件

- 用户希望 WorkBuddy 自主推进多个功能点
- 进入迭代/批处理模式

## 执行步骤

1. 用 `Read` 读取 `README.md` 的 AI 协作声明（编译/测试命令），读取 `Doc/功能清单.md` 取未实现项。
2. 选取一个功能点；必要时调用 `xcode-data-modeling` / `cube-mvc-backend` 实现。
3. 用 `Bash` 运行编译（`dotnet build`）与测试（`dotnet test`）。
4. **需求对照自检**：用 `Grep`/`Read` 验证实现覆盖该功能点的验收标准。
5. 用 `Edit` 将 `Doc/功能清单.md` 对应项状态置为"已完成"。
6. 用 `Bash` 提交（`git add`/`commit`，遵循规范提交信息）。
7. 循环直至清单清空或达到用户设定的停止条件，输出检查点报告（已完成/失败/阻塞）。
8. 遇阻塞则停下并说明，不静默跳过。

## 关联

- `development-process`、`testing-strategy`、`code-review`、`implementation-audit`
