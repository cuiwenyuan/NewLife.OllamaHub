---
name: project-init
description: This skill should be used when scaffolding a new project on the NewLife technology stack (XCode/Cube/.NET), generating the standard folder layout, entry projects, and AI collaboration declaration. Trigger on "新建项目", "初始化项目", "脚手架", "project init", "NewLife 技术栈".
agent_created: true
---

# 新项目初始化

## 用途

按 NewLife 技术栈搭建新项目脚手架：标准目录、入口工程、基础配置与 `README` 的 AI 协作声明。对应原 Copilot 的 `@project-init` 智能体。

## 激活条件

- 用户要"从零建一个 NewLife 项目""初始化后端/全栈工程"
- 需要统一的可复用模板

## 执行步骤

1. 与用户确认目标形态：.NET 后端 / Node 前端 / 全栈，以及是否用 XCode + Cube。
2. 用 `Bash` 创建解决方案与工程（`dotnet new` 等），建立 `src/`、`Doc/` 目录。
3. 用 `Write` 生成基础文件：入口 `Program.cs`/`Startup`、示例实体、Cube 区域（若选）。
4. 写入 `README.md` 的「AI 协作声明」：项目类型、编译命令（`dotnet build`）、测试命令（`dotnet test`）、核心工程路径——供 `dev-loop` 等读取。
5. 用 `Bash` 跑 `dotnet build` 确认脚手架可编译。
6. 初始化 `Doc/` 标准文档占位（需求/功能清单/架构）。

## 关联

- `development-process`：初始化后即进入研发流程
- `newlife-global-standards`：脚手架遵循统一规范
