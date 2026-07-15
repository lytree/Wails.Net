# 发布指南

本文档说明 Wails.Net 的版本号管理机制、发布流程与 GitHub Actions CI/CD 流水线。

## 版本号管理

### 集中管理机制

版本号集中在 [Directory.Build.props](../../Directory.Build.props) 的 `WailsNetVersion` 属性中维护：

```xml
<WailsNetVersion>0.1.0-alpha.1</WailsNetVersion>
```

所有 `src/` 下的程序集与 NuGet 包将自动派生以下属性：

| 属性 | 说明 | 示例 |
|------|------|------|
| `Version` | NuGet 包版本，含预发布标签 | `0.1.0-alpha.1` |
| `PackageVersion` | NuGet 包版本（与 Version 同步） | `0.1.0-alpha.1` |
| `InformationalVersion` | 显示用版本，含预发布标签 | `0.1.0-alpha.1` |
| `AssemblyVersion` | CLR 程序集版本，4 段数字 | `0.1.0.0` |
| `FileVersion` | Windows 文件版本，4 段数字 | `0.1.0.0` |

### 版本号规范

遵循 [Semantic Versioning 2.0](https://semver.org/lang/zh-CN/)：

```
MAJOR.MINOR.PATCH[-prerelease]
```

| 版本类型 | 示例 | 适用场景 |
|----------|------|----------|
| 正式版 | `1.0.0` | 稳定发布 |
| Alpha 预发布 | `1.0.0-alpha.1` | 早期开发，API 可能变动 |
| Beta 预发布 | `1.0.0-beta.1` | 功能完整，可能有 bug |
| RC 候选版 | `1.0.0-rc.1` | 候选发布，仅修复 bug |

### 派生规则

- `Version` / `PackageVersion` / `InformationalVersion` 直接使用 `WailsNetVersion`，保留预发布标签
- `AssemblyVersion` / `FileVersion` 剥离预发布标签并补 `.0` 后缀（必须是 4 段数字）

例如 `WailsNetVersion = 1.2.3-beta.4` 时：
- `Version = 1.2.3-beta.4`
- `AssemblyVersion = 1.2.3.0`

## 发布流程

### 1. 修改版本号

编辑 [Directory.Build.props](../../Directory.Build.props)：

```xml
<WailsNetVersion>0.2.0</WailsNetVersion>
```

### 2. 更新 CHANGELOG（可选）

记录本次发布的新功能、修复与破坏性变更。

### 3. 提交修改

```bash
git add Directory.Build.props
git commit -m "chore: 发布版本 0.2.0"
```

### 4. 打 Git Tag

```bash
git tag v0.2.0
git push origin main --tags
```

### 5. 触发 CI 发布流水线

推送 tag 后，GitHub Actions 将自动触发 `publish-nuget` job，推送 NuGet 包到 nuget.org。

### 6. 验证发布

- 在 [nuget.org](https://www.nuget.org/packages/Wails.Net.Application) 查看包是否上传成功
- 使用 `dotnet add package Wails.Net.Application --version 0.2.0` 验证可安装

## GitHub Actions CI/CD 流水线

### 流水线 Jobs

```
build → test → pack → dist → publish
```

| Job | 名称 | Runner | 说明 |
|-----|------|--------|------|
| build | 构建 | windows-latest | 还原 + 构建全部项目（包括 Linux 平台项目） |
| test | test-application | windows-latest | 运行 Application + CLI 测试 |
| test | test-windows | windows-latest | 运行 Windows 平台测试 |
| test | test-linux | ubuntu-latest | 运行 Linux 平台测试（允许失败） |
| pack | 打包 NuGet | windows-latest | 打包 NuGet 包（nupkg + snupkg） |
| dist | dist-windows | windows-latest | Windows 自包含构建（win-x64/x86/arm64） |
| dist | dist-linux | windows-latest | Linux 自包含构建（linux-x64/arm64，允许失败） |
| dist | dist-android | windows-latest | Android APK 构建（android-arm64/x64/arm，允许失败） |
| publish | publish-nuget | windows-latest | 推送到 nuget.org（仅 tag 触发） |

### 触发条件

| 事件 | 触发的 Jobs |
|------|-------------|
| Pull Request | build, test |
| 推送到任意分支 | build, test |
| 推送到 main 分支 | build, test, pack, dist |
| 推送 tag（`v*.*.*` 格式） | build, test, pack, dist, publish |

### Runner 要求

#### Windows Runner（windows-latest）

- GitHub Actions 托管的 windows-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 用于构建全部项目、Windows 测试、打包、三平台自包含构建

#### Linux Runner（ubuntu-latest）

- GitHub Actions 托管的 ubuntu-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 安装 GTK4 + WebKitGTK 6.0 原生库
- 仅用于 Linux 平台测试（允许失败）

### 必需的 Secrets

| Secret | 说明 | 配置位置 |
|--------|------|--------|
| `NUGET_API_KEY` | nuget.org API Key | GitHub → Settings → Secrets and variables → Actions → Repository secrets |

### 构建产物

- `artifacts/packages/*.nupkg` — NuGet 包
- `artifacts/packages/*.snupkg` — 符号包（含 SourceLink 源码映射）

## 本地验证

发布前可在本地验证：

```bash
# 1. 构建全部项目
dotnet build Wails.Net.slnx -c Release

# 2. 运行测试
dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj
dotnet run --project tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj

# 3. 打包 NuGet 包（slnx 内所有可打包项目）
dotnet pack Wails.Net.slnx -c Release -o artifacts/packages -p:SkipFrontendBuild=true

# 4. 单独打包 Templates 项目
#    Templates 不在 slnx 中（dotnet pack 会因 NU5017 误报退出码 1，但 nupkg 实际正确生成）
dotnet pack src/Wails.Net.Templates/Wails.Net.Templates.csproj -c Release -o artifacts/packages

# 5. 验证包内容
dotnet nuget push --dry-run artifacts/packages/Wails.Net.Application.0.1.0-alpha.1.nupkg
```

## NuGet 包清单

### 平台聚合包（推荐使用）

| 包名 | 说明 |
|------|------|
| `Wails.Net.Bundle.Windows` | Windows 平台聚合包：一键引用 Windows 开发所需全部 Wails.Net 包 |
| `Wails.Net.Bundle.Linux` | Linux 平台聚合包：一键引用 Linux 开发所需全部 Wails.Net 包 |

### 核心运行时包

| 包名 | 说明 |
|------|------|
| `Wails.Net.Application` | 核心应用框架 |
| `Wails.Net.Application.Windows` | Windows 平台实现 |
| `Wails.Net.Application.Linux` | Linux 平台实现 |
| `Wails.Net.AssetServer` | 资源服务器 |
| `Wails.Net.Runtime.Js` | 前端运行时 JS 生成器 |
| `Wails.Net.Errors` | 错误类型 |
| `Wails.Net.Events` | 事件类型 |
| `Wails.Net.Generator` | 代码生成器 |
| `Wails.Net.SourceGenerators` | 源代码生成器（analyzer） |
| `Wails.Net.Cli` | CLI 工具（global tool） |

### 项目模板包

| 包名 | 说明 |
|------|------|
| `Wails.Net.Templates` | dotnet new 项目模板：提供 `wails-net-app` 短名模板 |

## SDK 使用方式

### 方式一：聚合包（推荐）

```xml
<!-- 仅需一行 PackageReference 即可获得 Windows 平台全部依赖 -->
<PackageReference Include="Wails.Net.Bundle.Windows" />
<PackageReference Include="Wails.Net.Bundle.Linux" />
```

聚合包是 meta-package，本身不输出程序集，仅通过传递依赖方式引入对应平台所需的全部 Wails.Net 包。
版本由 `Directory.Packages.props` (CPM) 集中管理，无需指定版本号。

### 方式二：项目模板快速创建

```bash
# 安装模板包
dotnet new install Wails.Net.Templates

# 创建新项目
dotnet new wails-net-app -n MyCompany.MyApp -o MyCompany.MyApp

# 模板内容包含：
# - Program.cs（含 DesktopApplicationBuilder、Service 注册、Plugin 配置）
# - Services/GreetingService.cs（[Binding] 示例服务）
# - frontend/index.html + app.js + styles.css（前端三件套）
# - appsettings.json、app.manifest（DPI 感知 PerMonitorV2）
# - 引用 Wails.Net.Bundle.Windows 聚合包
```

### 方式三：CLI 工具

```bash
# 全局安装 CLI 工具
dotnet tool install -g Wails.Net.Cli

# 使用 CLI 生成绑定代码、脚手架等
wails-net --help
```

## SourceLink 调试支持

所有 NuGet 包均启用 SourceLink，调试时可自动从 GitHub 加载源码：

1. 在 Visual Studio / Rider 中启用 Source Link 支持
2. 安装 NuGet 包后，调试时自动跳转到 GitHub 源码
3. 符号包（.snupkg）发布到 nuget.org，自动加载符号

## 三平台签名流程

### Windows Authenticode 签名

**工具**：`signtool.exe`（Windows SDK 自带）

**签名命令**：

```bash
signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 /sha1 <THUMBPRINT> MyApplication.exe
```

**验证签名**：

```bash
signtool verify /pa /v MyApplication.exe
```

**说明**：Wails.Net 的 `SignerCommand`（Minisign）用于文件完整性校验，不替代 Authenticode 代码签名。生产环境发布 Windows 应用程序应同时进行 Authenticode 签名，确保用户在 SmartScreen 和 UAC 对话框中看到可信发布者信息。

### Linux GPG 签名

**工具**：`gpg`（GnuPG）

**对 tar.gz 签名**：

```bash
gpg --detach-sign --armor Wails.Net.App-linux-x64.tar.gz
```

**验证签名**：

```bash
gpg --verify Wails.Net.App-linux-x64.tar.gz.asc Wails.Net.App-linux-x64.tar.gz
```

**发布公钥**：发布前需将签名公钥上传到公共密钥服务器（如 `keys.openpgp.org`），并在发布说明中提供公钥指纹。

### Android APK 签名

**工具**：`apksigner`（Android SDK build-tools）

**构建时签名**：Cake Frosting `build/` 项目的 `Dist-Android` task 通过 MSBuild 属性配置签名：

| MSBuild 属性 | 说明 |
|--------------|------|
| `AndroidKeyStore` | 设为 `True` 启用签名 |
| `AndroidSigningKeyStore` | keystore 文件路径 |
| `AndroidSigningKeyAlias` | 密钥别名 |
| `AndroidSigningKeyPass` | 密钥密码 |
| `AndroidSigningStorePass` | keystore 密码 |

**验证签名**：

```bash
apksigner verify --verbose MyApplication.apk
```

**说明**：Debug 构建默认使用 Android SDK 的 debug keystore 签名；Release 构建需提供自定义 keystore。keystore 文件应通过 CI/CD Secrets 注入，不入仓库。

## AppImage 构建指南（Linux）

Cake Frosting `build/` 项目的 `Dist-Linux` task 生成 `tar.gz` 自包含包。AppImage 打包需额外工具 `appimagetool`，本期不集成到构建脚本，可手动执行：

```bash
# 安装 appimagetool
wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage

# 准备 AppDir 目录结构
mkdir -p MyApplication.AppDir/usr/bin
cp -r artifacts/dist/linux-x64/* MyApplication.AppDir/usr/bin/

# 创建 .desktop 文件
cat > MyApplication.AppDir/MyApplication.desktop <<EOF
[Desktop Entry]
Name=MyApplication
Exec=MyApplication
Icon=MyApplication
Type=Application
Categories=Utility;
EOF

# 打包 AppImage
./appimagetool-x86_64.AppImage MyApplication.AppDir MyApplication-x86_64.AppImage
```

## 回滚

如需回滚已发布的版本：

1. 在 [nuget.org](https://www.nuget.org) 后台取消列出该版本
2. 修改 `Directory.Build.props` 的 `WailsNetVersion` 回到上一个版本
3. 重新打 tag 并推送

**注意**：NuGet 不允许删除已上传的包，只能取消列出（unlist）。
