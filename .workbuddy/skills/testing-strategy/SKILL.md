---
name: testing-strategy
description: This skill should be used when writing or planning tests for a NewLife/.NET project: decide between unit testing (xUnit), integration testing (SQLite in-memory), and E2E testing, and apply the right strategy per layer. Trigger on "单元测试", "集成测试", "E2E", "xUnit", "SQLite 测试", "测试策略", "写测试".
agent_created: true
---

# 测试策略

## 用途

为 NewLife/.NET 项目选择并实施恰当的测试策略：单元测试（xUnit）、集成测试（SQLite 内存库）、端到端（E2E）测试，并明确各层适用方式。

## 激活条件

- 用户要求"写测试""补测试""设计测试用例"
- 需要判断某逻辑该用单元还是集成测试
- CI 中配置测试套件

## 执行步骤

1. 用 `Read` 阅读被测代码，用 `Grep` 定位已有 `[Fact]`/`[Theory]` 与测试工程。
2. **单元测试（xUnit）**：纯逻辑、无 IO 的方法用 `[Fact]`/`[Theory]`；mock 外部依赖；覆盖边界与异常分支。
3. **集成测试（SQLite）**：涉及 XCode/仓储/事务的逻辑，用 SQLite 内存库（`DataFactory` 切换）跑真实读写，验证实体映射与关系。
4. **E2E 测试**：跨控制器/API 的端到端场景，模拟请求验证完整链路；仅对核心链路使用，避免脆弱。
5. 用 `Write`/`Edit` 生成/修改测试文件，保持与 `newlife-global-standards` 命名一致。
6. 用 `Bash` 运行 `dotnet test` 收集结果；失败则结合报错定位，必要时回 `code-review`。
7. 输出覆盖率与未覆盖风险点摘要。

## 关联

- `newlife-global-standards`：断言/命名风格
- `cross-compatibility-testing`：跨实现的正确性验证
- `dev-loop`：测试中自检环节
