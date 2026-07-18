# 构建与打包指南

本文档介绍如何使用 Wails.Net CLI 工具构建和打包桌面应用，涵盖从开发到分发的完整流程。

---

## 1. 环境准备

### 1.1 必需工具

| 工具 | 版本要求 | 说明 |
|------|---------|------|
| .NET SDK | 10.0+ | `dotnet --version` 检查 |
| Wails.Net CLI | latest | `dotnet tool install -g wails.net` 或源码构建 |

### 1.2 平台特定依赖

**Windows**：
- WebView2 Runtime（Windows 11 内置，Windows 10 需安装）
- 无需额外依赖

**Linux**（以 Ubuntu/Debian 为例）：
```bash
sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0
```

**Android**：
- .NET Android 工作负载（`dotnet workload install android`）
- Android SDK + JDK 17+

### 1.3 环境诊断

```bash
# 使用 CLI 诊断工具检查环境
wails.net doctor

# 或从源码运行
dotnet run --project src/Wails.Net.Cli -- doctor
```

---

## 2. 开发模式

### 2.1 基本开发

```bash
# 直接使用 dotnet watch 热重载开发
cd examples/Wails.Net.Demo
dotnet watch run
```

### 2.2 使用 CLI dev 命令

```bash
# 使用 wails.net dev 命令（支持 wails.json 中的 beforeDevCommand 钩子）
wails.net dev --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj
```

---

## 3. 构建应用

### 3.1 基本构建

```bash
# 使用标准 dotnet 命令
dotnet build examples/Wails.Net.Demo/Wails.Net.Demo.csproj -c Release

# 使用 CLI build 命令（支持前端构建钩子）
wails.net build --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj
```

### 3.2 构建选项

| 选项 | 说明 | 示例 |
|------|------|------|
| `--project` | 项目文件路径 | `--project path/to/App.csproj` |
| `--configuration` | 构建配置 | `--configuration Release`（默认）|
| `--runtime` | 目标运行时 | `--runtime win-x64` / `--runtime linux-x64` |
| `--self-contained` | 自包含发布 | `--self-contained`（包含 .NET 运行时）|
| `--skip-hooks` | 跳过构建钩子 | `--skip-hooks` |
| `--skip-frontend` | 跳过前端构建 | `--skip-frontend` |

### 3.3 自包含构建（推荐分发方式）

自包含构建将 .NET 运行时打包进应用，目标机器无需安装 .NET：

```bash
# Windows 自包含
wails.net build --project MyApp.csproj --runtime win-x64 --self-contained

# Linux 自包含
wails.net build --project MyApp.csproj --runtime linux-x64 --self-contained

# Linux ARM64（树莓派等）
wails.net build --project MyApp.csproj --runtime linux-arm64 --self-contained
```

### 3.4 wails.json 构建钩子

在 `wails.json` 中配置构建前后执行的命令：

```json
{
  "beforeBuildCommand": "npm run build",
  "afterBuildCommand": "echo Build completed"
}
```

使用 `--skip-hooks` 跳过钩子执行。

---

## 4. 打包分发

### 4.1 基本打包

```bash
# 使用 CLI pack 命令
wails.net pack --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj
```

默认输出到 `bin/Release/packages/` 目录。

### 4.2 打包选项

| 选项 | 说明 | 默认值 |
|------|------|--------|
| `--format` | 打包格式 | Windows: `zip` / Linux: `targz` |
| `--output` | 输出目录 | `bin/packages` |
| `--app-name` | 应用名称 | 取 `wails.json` 的 `name` |
| `--version` | 版本号 | 取 `wails.json` 的 `version` |
| `--self-contained` | 自包含 | `false` |

### 4.3 打包格式

#### Windows

```bash
# ZIP 压缩包（默认）
wails.net pack --project MyApp.csproj --format zip

# NSIS 安装程序（需要 WiX Toolset）
wails.net pack --project MyApp.csproj --format nsis
```

#### Linux

```bash
# tar.gz 压缩包（默认）
wails.net pack --project MyApp.csproj --format targz

# AppImage（便携式）
wails.net pack --project MyApp.csproj --format appimage

# .deb 包（Debian/Ubuntu）
wails.net pack --project MyApp.csproj --format deb

# .rpm 包（Fedora/RHEL）
wails.net pack --project MyApp.csproj --format rpm
```

### 4.4 完整打包示例（Demo）

```bash
# 1. 构建 Demo 并打包为 Windows ZIP
wails.net pack \
  --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained \
  --format zip \
  --output dist/ \
  --app-name "Wails.Net.Demo" \
  --version "1.0.0"

# 2. 输出：dist/Wails.Net.Demo-1.0.0-win-x64.zip
```

---

## 5. wails.json 配置详解

`wails.json` 是 Wails.Net 项目的核心配置文件，位于项目根目录。

### 5.1 完整示例

参见 [examples/Wails.Net.Demo/wails.json](../../examples/Wails.Net.Demo/wails.json)。

### 5.2 关键字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | 项目名称 |
| `version` | string | 版本号（如 `1.0.0`）|
| `outputFilename` | string | 输出可执行文件名（不含扩展名）|
| `assetDir` | string | 前端构建产物目录 |
| `frontend` | object | 前端配置（dir/buildCommand/installCommand/outputDir）|
| `bindings` | object | 绑定代码生成配置（outputDir）|
| `bundle` | object | 打包配置（identifier/iconPath/category/windows/linux）|
| `beforeBuildCommand` | string | 构建前钩子 |
| `afterBuildCommand` | string | 构建后钩子 |
| `beforeDevCommand` | string | 开发模式前钩子 |
| `afterDevCommand` | string | 开发模式后钩子 |

### 5.3 bundle 配置（平台特定）

```json
{
  "bundle": {
    "identifier": "com.example.myapp",
    "iconPath": "build/icons",
    "category": "Productivity",
    "windows": {
      "publisher": "Your Company",
      "webviewInstallMode": true
    },
    "linux": {
      "maintainer": "Your Name <you@example.com>",
      "debDependencies": ["libwebkit2gtk-4.1-0", "libgtk-3-0"],
      "rpmDependencies": ["webkit2gtk3", "gtk3"]
    }
  }
}
```

---

## 6. 代码签名（Windows Authenticode）

Wails.Net 支持自动 Authenticode 签名，确保 Windows 分发包的可信度。

### 6.1 使用 signtool 签名

```bash
# 使用 CLI signer 命令
wails.net signer \
  --file path/to/MyApp.exe \
  --cert-path path/to/cert.pfx \
  --password "your-cert-password" \
  --timestamp-url http://timestamp.digicert.com
```

### 6.2 在打包流程中集成签名

在 `wails.json` 的 `afterBuildCommand` 中调用签名：

```json
{
  "afterBuildCommand": "wails.net signer --file bin/Release/net10.0/MyApp.exe --cert-path cert.pfx --password %CERT_PASSWORD%"
}
```

---

## 7. TypeScript 绑定生成

构建前生成前端 TypeScript 绑定文件：

```bash
# 从已编译的程序集生成绑定
wails.net generate \
  --assembly bin/Release/net10.0/MyApp.dll \
  --output frontend/src/wails
```

生成的文件包含：
- `bindings.ts` — 类型化的绑定方法
- `events.ts` — 事件常量
- `runtime.js` — Wails 运行时

---

## 8. Android 构建

### 8.1 构建 APK

```bash
# 构建 Android APK
dotnet build examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj -c Release -t:InstallAndroid

# 或使用 CLI
wails.net build --project examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj -r android-arm64
```

### 8.2 平台命令

```bash
# 使用 platform 命令管理目标平台
wails.net platform list
wails.net platform add android
```

---

## 9. CI/CD 集成

### 9.1 GitHub Actions 示例

```yaml
name: Build and Pack

on:
  push:
    tags: ['v*']

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build CLI
        run: dotnet build src/Wails.Net.Cli/Wails.Net.Cli.csproj -c Release

      - name: Build and Pack
        run: |
          dotnet run --project src/Wails.Net.Cli -- pack \
            --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj \
            --configuration Release \
            --runtime win-x64 \
            --self-contained \
            --format zip \
            --output dist/

      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: wails-net-demo-windows
          path: dist/*.zip

  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install Linux dependencies
        run: |
          sudo apt update
          sudo apt install -y libwebkit2gtk-4.1-0 libgtk-3-0

      - name: Build and Pack
        run: |
          dotnet run --project src/Wails.Net.Cli -- pack \
            --project examples/Wails.Net.Demo/Wails.Net.Demo.csproj \
            --configuration Release \
            --runtime linux-x64 \
            --self-contained \
            --format targz \
            --output dist/

      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: wails-net-demo-linux
          path: dist/*.tar.gz
```

---

## 10. 常见问题

### Q1: 构建时报 "WebView2 未找到"

Windows 上需要 WebView2 Runtime。Windows 11 内置，Windows 10 可从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装。

### Q2: Linux 构建缺少 WebKitGTK

```bash
# Ubuntu/Debian
sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0

# Fedora
sudo dnf install webkit2gtk3 gtk3

# Arch Linux
sudo pacman -S webkit2gtk gtk3
```

### Q3: 自包含构建体积过大

自包含构建会打包整个 .NET 运行时（约 60-80MB）。可通过以下方式减小体积：

```bash
# 使用 trimming（注意：可能破坏反射）
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true

# 使用 NativeAOT（实验性）
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

### Q4: 打包后应用无法启动

检查：
1. 目标机器是否有所需运行时（非自包含时）
2. 前端资源是否已正确打包到输出目录
3. DLL 依赖是否完整（使用 `wails.net doctor` 诊断）

---

## 相关文档

- [CLI 工具文档](./cli-tool.md)
- [Demo 示例](../../examples/Wails.Net.Demo/)
- [发布指南](./release-guide.md)
- [功能对比](../comparison-with-tauri2-wails3.md)

---

**文档更新**：2026-07-18
