---
name: project-architecture
description: This skill should be used when choosing architecture for a NewLife/.NET project: 2-tier vs 3-tier, anemic vs rich (充血) model, whether to extract a Service layer, or resolving cross-component trade-offs. Trigger on "架构选型", "三层架构", "充血模型", "Service 抽取", "分层设计", "项目架构".
agent_created: true
---

# 项目架构选型

## 用途

在 NewLife/.NET 项目中做架构决策：两层/三层取舍、贫血 vs 充血模型、Service 抽取判断、跨组件依赖与边界划分。

## 激活条件

- 新建项目或重构既有分层
- 争论"是否抽 Service""实体要不要带行为"
- 需要统一模块边界、依赖方向、可测试性

## 执行步骤

1. 用 `Read` 阅读项目 `README.md`（AI 协作声明）与现有工程结构（`Glob` `**/*.csproj`）。
2. 判定业务复杂度：
   - 简单 CRUD + Cube 后台 → 两层（实体 + 控制器）可接受；
   - 多步骤业务、跨实体事务、需独立测试 → 抽 Service / 应用服务层。
3. 充血模型判断：把与实体强相关、无外部依赖的行为放在实体方法内；跨实体/调用外部（缓存、消息、网络）的逻辑放在 Service。
4. 依赖方向：UI → Service → 实体/仓储；禁止实体反向依赖 Service 或 UI。
5. 用 `Write`/`Edit` 产出或调整分层文件，保证命名与 `newlife-global-standards` 一致。
6. 用 `Bash` 执行 `dotnet build` 校验分层编译通过。
7. 在架构设计文档中记录决策理由（配合 `development-process`）。

## 关联

- `newlife-global-standards`：命名/类型/风格硬约束
- `development-process`：架构设计是研发流程的一环
- `testing-strategy`：分层影响单元测试/集成测试划分
