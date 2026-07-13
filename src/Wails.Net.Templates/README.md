# Wails.Net.Templates

Wails.Net 项目的 `dotnet new` 项目模板包。

## 安装模板

```bash
# 安装模板包（从本地 nupkg 或 nuget.org）
dotnet new install Wails.Net.Templates

# 从本地 nupkg 文件直接安装
dotnet new install artifacts/packages/Wails.Net.Templates.0.1.0-alpha.1.nupkg
```

## 可用模板

### `wails-net-app`

基础 Wails.Net 桌面应用模板（HTML/JS 前端），包含：

- `Program.cs` — 使用 `DesktopApplicationBuilder` 启动应用
- `Services/GreetingService.cs` — 示例绑定服务
- `frontend/index.html` + `app.js` + `styles.css` — 最小前端
- `appsettings.json` — 配置文件
- `app.manifest` — Windows 应用清单

#### 创建项目

```bash
# 创建名为 MyApp 的项目，输出到 MyApp/ 目录
dotnet new wails-net-app -n MyApp -o MyApp

cd MyApp
dotnet run
```

#### 模板参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `-n` / `--name` | `Wails.Net.App` | 项目名与根命名空间 |
| `-o` / `--output` | 当前目录 | 输出目录 |

#### 创建后即可运行

```bash
cd MyApp
dotnet run
```

将打开一个 1200x800 的窗口，显示 "Hello from Wails.Net" 按钮和计数器。

## 卸载模板

```bash
dotnet new uninstall Wails.Net.Templates
```

## 开发模板

修改 `content/App/` 下的源文件后，重新打包：

```bash
dotnet pack src/Wails.Net.Templates/Wails.Net.Templates.csproj -o artifacts/packages
```

本地测试安装：

```bash
dotnet new install artifacts/packages/Wails.Net.Templates.0.1.0-alpha.1.nupkg
dotnet new wails-net-app -n TestApp -o TestApp
```
