# Wails.Net CLI

Wails.Net 命令行工具，提供项目脚手架、构建、TypeScript 绑定生成、环境诊断等功能。

## 安装

```bash
# 全局工具安装（发布后）
dotnet tool install -g wails.net

# 本地构建后测试
dotnet run --project src/Wails.Net.Cli -- <command>
```

## 命令

```bash
# 环境诊断
wails.net doctor

# 脚手架新项目
wails.net new MyApp --template vue-ts

# 构建项目
wails.net build --project path/to/MyApp.csproj

# 生成 TypeScript 绑定
wails.net generate --assembly path/to/MyApp.dll --output bindings

# 查看版本
wails.net version
```

## 详细文档

参见 [CLI 工具文档](../../docs/implementation/cli-tool.md)。
