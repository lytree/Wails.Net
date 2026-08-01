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
| dist | dist-linux | ubuntu-latest | Linux 自包含构建（linux-x64/arm64，含 .deb/.rpm 原生包打包） |
| dist | dist-android | windows-latest | Android APK 构建（android-arm64/x64/arm，允许失败） |
| publish | publish-nuget | windows-latest | 通过 Trusted Publishing(OIDC) 推送全部 nupkg（含 CLI/SDK/Templates）到 nuget.org（仅 tag 触发，无需 API Key） |
| publish | publish-github-release | ubuntu-latest | 创建 GitHub Release 并上传 nupkg + 三平台 dist 产物（仅 tag 触发） |

### 触发条件

| 事件 | 触发的 Jobs |
|------|-------------|
| Pull Request | build, test |
| 推送到任意分支 | build, test |
| 推送到 main 分支 | build, test, pack, dist |
| 推送 tag（`v*.*.*` 格式） | build, test, pack, dist, publish-nuget, publish-github-release |

### Runner 要求

#### Windows Runner（windows-latest）

- GitHub Actions 托管的 windows-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 用于构建全部项目、Windows 测试、打包、三平台自包含构建

#### Linux Runner（ubuntu-latest）

- GitHub Actions 托管的 ubuntu-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 安装 GTK4 + WebKitGTK 6.0 原生库
- 安装 `dpkg-dev` 与 `rpm` 包（提供 `dpkg-deb` 和 `rpmbuild` 命令，用于 .deb/.rpm 原生包打包）
- 用于 Linux 平台测试（允许失败）与 Linux 自包含构建（dist-linux，必须成功）

### 必需的 Secrets

本项目使用 **NuGet Trusted Publishing**（基于 GitHub OIDC）发布到 nuget.org，无需长期 API Key。仅需一个 Secret：

| Secret | 说明 | 配置位置 |
|--------|------|--------|
| `NUGET_USER` | nuget.org 用户名（profile name，非邮箱），用于 Trusted Publishing 换取临时 API key | GitHub → Settings → Secrets and variables → Actions → Repository secrets |

> 不再需要 `NUGET_API_KEY`。Trusted Publishing 通过 `NuGet/login@v1` 用 GitHub OIDC token 换取一次性临时 key（约 1 小时过期），详见下方 [Trusted Publishing 配置](#trusted-publishing-配置) 章节。

### 构建产物

- `artifacts/packages/*.nupkg` — NuGet 包
- `artifacts/packages/*.snupkg` — 符号包（含 SourceLink 源码映射）

### GitHub Release 产物

tag 触发时，`publish-github-release` job 会创建 GitHub Release 并上传以下资产，供用户直接从 [Releases 页面](https://github.com/lytree/Wails.Net/releases) 下载：

- **NuGet 包**：全部 `.nupkg` / `.snupkg`（SDK / CLI / Templates / Bundle 等）
- **Windows 自包含**：`.zip`（win-x64 / win-x86 / win-arm64）
- **Linux 自包含**：`.tar.gz` / `.deb` / `.rpm`（linux-x64 / linux-arm64）
- **Android**：`.apk`（android-arm64 / android-x64 / android-arm，构建允许失败时可能缺失）

Release 标题为 `Wails.Net <版本号>`，Release Notes 由 GitHub 自动生成（基于 commit 历史）。使用内置 `GITHUB_TOKEN`，无需额外配置 Secret。

## Trusted Publishing 配置

本项目通过 **NuGet Trusted Publishing**（OIDC）发布到 nuget.org，替代传统的长期 API Key。优势：

- **无长期密钥**：不在仓库或 CI 存储 API Key，无需每年轮换
- **短期凭证**：每次发布用 GitHub OIDC token 换取一次性临时 API key（约 1 小时过期）
- **绑定身份**：nuget.org 校验 token 来自指定仓库 + workflow + environment，即使 workflow 文件泄露也无法被其他仓库利用

### 一次性配置步骤（nuget.org 侧）

1. 登录 [nuget.org](https://www.nuget.org) → 右上角用户菜单 → **Trusted Publishing**（在 API Keys 旁边）
2. 点击 **Create** 创建 policy，填写：
   - **Policy Name**：如 `Wails.Net CI`
   - **Package Owner**：选择拥有 Wails.Net.* 包的个人账户或组织
   - **Repository Owner**：`lytree`
   - **Repository**：`Wails.Net`
   - **Workflow File**：`ci.yml`
   - **Environment**：`nuget-org`（与 ci.yml 中 publish-nuget job 的 environment 一致）
3. 点击 **Create** 保存 policy

> 一个 policy 覆盖该 owner 名下的**全部包**（Wails.Net.Sdk / Cli / Templates / Bundle 等），无需每个包单独配置。

### GitHub 仓库侧配置

1. 在 GitHub 仓库 → Settings → Environments 创建 `nuget-org` environment（与 ci.yml 一致），可按需添加 protection rules（如要求审批）
2. 在 Settings → Secrets and variables → Actions → Repository secrets 添加 `NUGET_USER`，值为 nuget.org 用户名（profile name，非邮箱）
3. **删除**旧的 `NUGET_API_KEY` secret（如存在），避免与 Trusted Publishing 冲突

### 工作机制

```
GitHub Actions (tag 触发)
  └─ publish-nuget job
       ├─ permissions: id-token: write        ← 启用 OIDC token 颁发
       ├─ NuGet/login@v1                      ← OIDC token 换取临时 API key
       │    └─ 输出 steps.nuget_login.outputs.NUGET_API_KEY
       └─ dotnet nuget push --api-key <临时key> ← 推送全部 nupkg
```

> 私有仓库的 policy 初始 7 天活跃；首次成功 `NuGet/login`（OIDC 换 key）后永久激活并绑定不可变 GitHub ID。如错过 7 天窗口，可在 Trusted Publishing 页面手动重新激活。

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

### 一键打包与冒烟测试（推荐）

仓库提供 `scripts/pack-and-test.sh` 脚本，一键完成"打包 → 验证 SDK 可依赖 → 验证 CLI 可安装"全流程，模拟外部消费者真实体验：

```bash
# 打包全部 src/ 项目并完整验证（在 git bash 或 Linux/macOS 终端运行）
bash scripts/pack-and-test.sh

# 跳过打包，仅用 artifacts/nupkg 中已有的包验证
bash scripts/pack-and-test.sh --skip-pack

# 一并打包 Android 平台包（需已安装 android 工作负载）
bash scripts/pack-and-test.sh --include-android

# 保留临时测试目录便于排查
bash scripts/pack-and-test.sh --keep-temp
```

脚本执行步骤：

1. **打包**：按依赖顺序打包全部可发布项目到 `artifacts/nupkg/`（含 CLI、Templates，默认跳过 Android）
2. **验证关键包**：检查 Sdk / Cli / Templates / Bundle / Application / SourceGenerators 等包已生成并解析版本号
3. **SDK 依赖验证**：在仓库外临时目录创建测试项目，用 `PackageReference` 引用 `Wails.Net.Sdk`，验证可还原、可构建（依赖链 + 源生成器 analyzer 加载正常）
4. **CLI 安装验证**：`dotnet tool install Wails.Net.Cli` 到临时路径，运行 `wails-net --version` 验证可执行

> 该脚本不依赖 android 工作负载，开箱即用。Android 包的发布由 CI（已配置 android workload 安装）保障。

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

## Capability 自动加载

Wails.Net 默认从 `{ContentRoot}/capabilities/` 目录自动加载 Capability JSON 文件并注册到 `PermissionManager`，对齐 Tauri v2 的默认行为。

### 配置

通过 `appsettings.json` 的 `Wails:Permissions` 节配置：

```json
{
  "Wails": {
    "Permissions": {
      "Enabled": true,
      "DenyByDefault": true,
      "CapabilitiesDirectory": "capabilities"
    }
  }
}
```

- `Enabled` 默认 `false`，启用后才触发自动加载与权限校验
- `CapabilitiesDirectory` 默认 `"capabilities"`（相对 `ContentRoot`），设为 `null` 或空字符串禁用自动加载
- 目录不存在时静默跳过（不抛异常）

### Capability 文件格式

```json
{
  "identifier": "main-capability",
  "description": "主窗口能力",
  "permissions": ["core:default", "fs:allow-read"],
  "windows": ["main"]
}
```

详细字段参见 `Wails.Net.Application.Security.CapabilityFileLoader` 文档。

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

### CI 自动签名

`dist-windows` job 检测以下环境变量自动调用 signtool 或 AzureSignTool，对发布目录下的所有 `.exe` / `.dll` 文件签名：

| 环境变量 | 说明 |
|----------|------|
| `WINDOWS_SIGN_BACKEND` | `signtool` 或 `azure`，未设置则跳过签名 |
| `WINDOWS_CERT_PFX_PATH` | PFX 证书文件路径（signtool 模式） |
| `WINDOWS_CERT_PASSWORD` | PFX 密码（signtool 模式） |
| `WINDOWS_CERT_THUMBPRINT` | 证书存储指纹（signtool 模式，与 PFX 二选一） |
| `WINDOWS_TIMESTAMP_URL` | 时间戳服务器，默认 `http://timestamp.digicert.com` |
| `AZURE_SIGNING_ENDPOINT` | Azure Trusted Signing endpoint（azure 模式） |
| `AZURE_SIGNING_ACCOUNT` | 账户名（azure 模式） |
| `AZURE_SIGNING_PROFILE` | 证书 profile 名（azure 模式） |

在 GitHub Actions 中通过 Repository Secrets 注入上述环境变量。签名失败将中断流水线（与 Android 签名策略一致）。

**signtool 查找顺序**：

1. `PATH` 中的 `signtool.exe`
2. Windows SDK 安装路径：`C:\Program Files (x86)\Windows Kits\10\bin\{version}\{arch}\signtool.exe`（自动取最新版本目录）

**AzureSignTool 查找**：

- `PATH` 中的 `azuresigntool.exe`
- 未找到时提示安装：`dotnet tool install --global AzureSignTool`

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

AppImage 打包已集成到 `Wails.Net.Cli` 的 `Packager`（通过 `pack` 命令的 `--format appimage` 选项触发）。Cake `Dist-Linux` task 默认仅生成 `tar.gz`，不默认生成 AppImage（需手动调用 CLI）。

手动构建 AppImage：

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

## Linux 原生包打包（.deb / .rpm）

Cake Frosting `build/` 项目的 `Dist-Linux` task 默认生成 `tar.gz`，通过 `--linux-formats` 参数可同时生成 `.deb` 和 `.rpm`：

```bash
# 生成全部 Linux 格式
dotnet run --project build/Wails.Net.Build -- --target=Dist-Linux --platform=linux --rid=all --linux-formats=tar.gz,deb,rpm

# 仅生成 .deb
dotnet run --project build/Wails.Net.Build -- --target=Dist-Linux --platform=linux --rid=linux-x64 --linux-formats=deb
```

**依赖**：构建机需安装 `dpkg-deb`（dpkg-dev 包）与 `rpmbuild`（rpm 包）。在 CI 的 ubuntu-latest 上通过 `sudo apt-get install -y dpkg-dev rpm` 自动安装。

**Debian 包内容**：

- `/usr/bin/{appName}` — 主可执行文件
- `/usr/share/applications/{appName}.desktop` — 桌面入口
- `/usr/share/icons/hicolor/256x256/apps/{appName}.png` — 图标
- `Depends: libgtk-4-1, libwebkitgtk-6.0-4` — 自动声明 GTK4/WebKitGTK 依赖

**RPM 包内容**：与 .deb 对齐，`Requires: gtk4, webkitgtk6.0`

**验证**：

```bash
# 查看 .deb 元数据
dpkg-deb -I MyApplication-1.0.0-linux-x64.deb

# 查看 .rpm 元数据
rpm -qpi MyApplication-1.0.0-linux-x64.rpm
```

## 回滚

如需回滚已发布的版本：

1. 在 [nuget.org](https://www.nuget.org) 后台取消列出该版本
2. 修改 `Directory.Build.props` 的 `WailsNetVersion` 回到上一个版本
3. 重新打 tag 并推送

**注意**：NuGet 不允许删除已上传的包，只能取消列出（unlist）。
