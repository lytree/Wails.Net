# 发布指南

本文档说明 Wails.Net 的版本号管理机制、发布流程与 GitHub Actions CI/CD 流水线。

## 版本号管理

### 集中管理机制

版本号集中在 [Directory.Build.props](../../Directory.Build.props) 的 `WailsNetVersion` 属性中维护：

```xml
<WailsNetVersion>1.0.1-alpha.5</WailsNetVersion>
```

所有 `src/` 下的程序集与 NuGet 包将自动派生以下属性：

| 属性 | 说明 | 示例 |
|------|------|------|
| `Version` | NuGet 包版本，含预发布标签 | `1.0.1-alpha.5` |
| `PackageVersion` | NuGet 包版本（与 Version 同步） | `1.0.1-alpha.5` |
| `InformationalVersion` | 显示用版本，含预发布标签 | `1.0.1-alpha.5` |
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
| Alpha 预发布 | `1.0.1-alpha.5` | 早期开发，API 可能变动 |
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

推送 tag 后，GitHub Actions 将自动触发发布流水线：

- `publish-nuget`：推送 NuGet 包到 nuget.org（Trusted Publishing OIDC）
- `publish-npm`：推送前端双包到 npmjs.org——`@wailsnet/runtime` + 全部 `@wailsnet/plugin-*`（M4 闭环）
- `publish-github-release`：创建 GitHub Release 并上传四平台构建产物

发布前 CI 执行版本一致性校验（`scripts/verify-versions.mjs`）：`Directory.Build.props` 的 `WailsNetVersion` 必须等于所有 `packages/*/package.json` 的 `version`，不一致即失败——保证 NuGet 与 npm 同版本发布。

> **tag 触发时版本直取标签名**：CI 的 `gitversion` 作业在 `github.ref_type == tag` 时不再调用 GitVersion，而是直接取 `github.ref_name` 去掉 `v` 前缀（如 `v1.0.1-alpha.5` → `1.0.1-alpha.5`），并派生 `assemblySemVer`。原因是 GitVersion 6.8.2 在当前配置下不会把 tag 识别为 version source，会算出 `1.0.0-271` 之类的分支版本，导致 NuGet / GitHub Release 与 npm 版本错位。因此发布前需保证 **tag 名 = `WailsNetVersion` = 各 `package.json` 的 `version`**；脚本校验后两者，tag 名由发布者保证。非 tag 构建仍由 GitVersion 按分支计算版本。

本地校验命令：

```bash
node scripts/verify-versions.mjs        # 校验（不一致退出码 1）
node scripts/verify-versions.mjs --fix  # 校验并自动把前端包版本对齐 WailsNetVersion
```

### 6. 验证发布

- 在 [nuget.org](https://www.nuget.org/packages/Wails.Net.Application) 查看包是否上传成功
- 使用 `dotnet add package Wails.Net.Application --version 0.2.0` 验证可安装
- 在 [npmjs.com](https://www.npmjs.com/package/@wailsnet/runtime) 查看前端包是否上传成功
- 使用 `npm view @wailsnet/plugin-window version` 验证版本一致

## GitHub Actions CI/CD 流水线

### 流水线 Jobs

```
build → test → pack → dist → publish
```

> **触发分级（非 tag 只构建与测试）**：任意分支 push 与 Pull Request 只运行 `build` 与 `test`；
> `pack` / `dist-windows` / `dist-linux` / `dist-macos` / `dist-android` / `publish-nuget` / `publish-npm` /
> `publish-github-release` 一律以 `if: startsWith(github.ref, 'refs/tags/v')` 限定，只有推送
> `v*.*.*` 标签时才执行。例外是 `test-android-e2e`（模拟器 + KVM 成本高），只在 `main` 与 tag 上运行。
> 本地 `pnpm publish:npm` 亦受同一约束：HEAD 不位于 `v<WailsNetVersion>` 标签时拒绝发布。

| Job | 名称 | Runner | 说明 |
|-----|------|--------|------|
| build | 构建 | windows-latest | 还原 + 构建全部项目（包括 Linux 平台项目） |
| test | test-application | windows-latest | 运行 Application + CLI 测试 |
| test | test-windows | windows-latest | 运行 Windows 平台测试 |
| test | test-linux | ubuntu-latest | 运行 Linux 平台测试（允许失败） |
| pack | 打包 NuGet | windows-latest | 打包 NuGet 包（nupkg + snupkg） |
| dist | dist-windows | windows-latest | Windows 自包含构建（win-x64/x86/arm64） |
| dist | dist-linux | ubuntu-latest | Linux 自包含构建（linux-x64/arm64，含 .deb/.rpm 原生包打包） |
| dist | dist-macos | macos-latest | macOS 自包含构建（osx-arm64/x64 → `.app` + `.zip` + `.dmg`） |
| dist | dist-android | windows-latest | Android APK 构建（android-arm64/x64/arm，允许失败） |
| publish | publish-nuget | windows-latest | 通过 Trusted Publishing(OIDC) 推送全部 nupkg（含 CLI/SDK/Templates）到 nuget.org（仅 tag 触发，无需 API Key） |
| publish | publish-npm | ubuntu-latest | 通过 Trusted Publishing(OIDC) 推送前端双包 `@wailsnet/runtime` + 全部 `@wailsnet/plugin-*`（40 个）到 npmjs.com 的 **wailsnet** 组织（仅 tag 触发，需前置版本一致性校验，无需 token） |
| publish | publish-github-release | ubuntu-latest | 创建 GitHub Release 并上传 nupkg + 四平台 dist 产物（仅 tag 触发） |

### 触发条件

| 事件 | 触发的 Jobs |
|------|-------------|
| Pull Request | build, test |
| 推送到任意分支 | build, test |
| 推送到 main 分支 | build, test, pack, dist |
| 推送 tag（`v*.*.*` 格式） | build, test, pack, dist, publish-nuget, publish-npm, publish-github-release |

### Runner 要求

#### Windows Runner（windows-latest）

- GitHub Actions 托管的 windows-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 用于构建全部项目、Windows 测试、打包、Windows/Android 自包含构建

#### Linux Runner（ubuntu-latest）

- GitHub Actions 托管的 ubuntu-latest 运行器
- 通过 `actions/setup-dotnet@v4` 安装 .NET 10 SDK
- 安装 GTK4 + WebKitGTK 6.0 原生库
- 安装 `dpkg-dev` 与 `rpm` 包（提供 `dpkg-deb` 和 `rpmbuild` 命令，用于 .deb/.rpm 原生包打包）
- 用于 Linux 平台测试（允许失败）与 Linux 自包含构建（dist-linux，必须成功）

#### macOS Runner（macos-latest）

- GitHub Actions 托管的 macos-latest 运行器（Apple Silicon）
- 通过 `actions/setup-dotnet@v6` 安装 .NET 10 SDK
- 安装 `android` 与 `macos` 工作负载：`android` 供 Pack 阶段评估 Android 工程，`macos` 提供 `net10.0-macos` 目标包
- 用于 macOS 自包含构建（dist-macos：`osx-arm64` / `osx-x64` → `.app` + `.zip` + `.dmg`）

### 必需的 Secrets

本项目 NuGet 与 npm 两侧都走 **Trusted Publishing**（基于 GitHub OIDC），无需长期 token / API Key。仅需一个 Secret：

| Secret | 说明 | 配置位置 |
|--------|------|--------|
| `NUGET_USER` | nuget.org 用户名（profile name，非邮箱），用于 Trusted Publishing 换取临时 API key | GitHub → Settings → Secrets and variables → Actions → Repository secrets |

> 不再需要 `NUGET_API_KEY`。Trusted Publishing 通过 `NuGet/login@v1` 用 GitHub OIDC token 换取一次性临时 key（约 1 小时过期），详见下方 [Trusted Publishing 配置](#trusted-publishing-配置) 章节。
>
> npm 侧不需要任何 secret：`publish-npm` 作业声明 `id-token: write`，绑定 trusted publisher 后由 `npm publish` 自行换一次性凭证，详见 [npm 发布](#npm-发布org-wailsnet--trusted-publishing)。

### 发布失败排查（已知报错）

两个发布作业都带「前置预检」步骤，缺配置时会在作业开头直接失败并给出中文提示，而不是等到构建完才报底层错误。若无预检，历史报错签名如下：

| 作业 | 原始报错 | 真实原因 | 处理 |
|------|---------|---------|------|
| `发布到 npmjs.org` | `npm error code ENEEDAUTH` / `E404` | 该包在 npm 侧尚未绑定 trusted publisher（OIDC 换不到凭证）；`E403` 表示绑定存在但 allowed action 不含 `npm publish` | 用 `pnpm trust:publisher -- --dry-run` 核对 41 个包，再 `pnpm trust:publisher` 批量绑定后重跑失败作业 |
| `发布到 NuGet.org` | `##[error]Input required and not supplied: user` | `secrets.NUGET_USER` 未配置 | 配置 `NUGET_USER`；若已配置仍失败，检查 nuget.org 侧 Trusted Publishing policy 的 Repository / Workflow / Environment 是否与 ci.yml 一致 |

> 已发布的正式版本无法"补成"预发布之外的另一种形态：如果 tag 建错了 Release 属性，用 `PATCH /repos/{owner}/{repo}/releases/{id}` 改 `prerelease` / `make_latest` 即可，无需重建 tag。

**预发布 tag 的处理**：tag 名含 `-`（如 `v1.0.1-alpha.5`）时，`publish-github-release` 会自动加 `--prerelease`，避免 alpha 包被 GitHub 标成 Latest 引导稳定版用户下载。

### 构建产物

- `artifacts/packages/*.nupkg` — NuGet 包
- `artifacts/packages/*.snupkg` — 符号包（含 SourceLink 源码映射）

### GitHub Release 产物

tag 触发时，`publish-github-release` job 会创建 GitHub Release 并上传以下资产，供用户直接从 [Releases 页面](https://github.com/lytree/Wails.Net/releases) 下载：

- **NuGet 包**：全部 `.nupkg` / `.snupkg`（SDK / CLI / Templates / Bundle 等）
- **Windows 自包含**：`.zip`（win-x64 / win-x86 / win-arm64）
- **Linux 自包含**：`.tar.gz` / `.deb` / `.rpm`（linux-x64 / linux-arm64）
- **macOS 自包含**：`.zip` / `.dmg`（osx-arm64 / osx-x64，内含 `Wails.Net.Demo.app` bundle）
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

## npm 发布：org `wailsnet` + Trusted Publishing

前端 41 个包全部发布到 npm 组织 **wailsnet** 下，因此包作用域必须是 `@wailsnet/*`
（npm 的 org 名即作用域名，`@wails-net/*` 无法发布进 `wailsnet` 组织）。

### 约定

| 项目 | 值 |
|------|-----|
| npm org / 作用域 | `wailsnet` / `@wailsnet` |
| 包名 | `@wailsnet/runtime` + 40 个 `@wailsnet/plugin-*` |
| 包源目录 | `packages/wails-net-*`（目录名保留历史命名，与包名解耦） |
| 发布目标 | `https://registry.npmjs.org`（由各 `package.json` 的 `publishConfig.registry` 指定，本地 `.npmrc` 指向镜像不影响） |
| 可见性 | `publishConfig.access = public` |

### 发布流程（人工 bootstrap）

CI 的 OIDC 免 token 发布要求 npm 侧已存在包并绑定 trusted publisher，所以**首次发布必须人工完成**。
npm 没有「网页端上传 tarball」入口（网页端只能审批 staged 包、管理 token），发布一律走 CLI。

**Step 1｜认证（三选一，任选其一即可长期复用）**

| 方式 | 命令 / 操作 | 适用 |
|------|-------------|------|
| A. 交互登录 | `npm login --registry https://registry.npmjs.org`（浏览器 + 2FA） | 本机手动发 |
| B. Granular Token（推荐自动化） | 见下方「Token 方式」 | 脚本 / CI 之外的半自动发布 |
| C. 直接复用 CI 的 OIDC | 不需要本地认证，推 tag 让 CI 发 | 已绑定 trusted publisher 之后 |

**Token 方式**：npmjs.com → 头像 → Access Tokens → Generate New Token → **Granular Access Token**
- Packages and scopes：`Read and write`
- Organizations：勾选 `wailsnet`
- 必须勾选 **Bypass two-factor authentication for automation**（否则每次发布都要 OTP）
- 写进**仓库外**的配置文件，避免误提交：

```bash
printf 'registry=https://registry.npmjs.org/\n//registry.npmjs.org/:_authToken=<token>\n' \
  > ~/.npmrc-npmjs-wailsnet && chmod 600 ~/.npmrc-npmjs-wailsnet

# 后续命令统一带上该配置
export NPM_CONFIG_USERCONFIG=~/.npmrc-npmjs-wailsnet
npm whoami                      # 应输出账号名
npm org ls wailsnet             # 应显示 <账号> - owner/developer
```

**Step 2｜构建 + 打包**（脚本自带 dist 预检与 `@wailsnet/` 作用域校验）

```bash
pnpm install
pnpm -r build
pnpm pack:npm        # → artifacts/npm/*.tgz，41 个
```

**Step 3｜发布（两种模式）**

```bash
# 模式一：直接发布（需要 2FA 已通过 / token 带 bypass 2FA）
pnpm publish:npm

# 模式二：staged 暂存 + 人工审批（npm CLI >= 11.15）
pnpm publish:npm:staged
npm stage list                # 查看待审条目与 id
npm stage approve <id>        # 2FA 确认后才真正上线
# 或 npmjs.com → Staged Packages → Approve
```

**Step 4｜校验**

```bash
npm view @wailsnet/runtime version dist-tags --registry https://registry.npmjs.org
npm view @wailsnet/plugin-dialog dependencies --registry https://registry.npmjs.org
# 依赖应显示 "@wailsnet/runtime": "1.0.1-alpha.5"，而不是 workspace:*
```

### dist-tag 规则（重要）

`1.0.1-alpha.5` 这类预发布版本**必须带 `--tag alpha`**，否则会顶掉 `latest`，
让 `pnpm add @wailsnet/runtime` 装到 alpha。脚本与 CI 都已自动处理：

| 版本 | dist-tag |
|------|----------|
| `1.0.1-alpha.5` | `alpha` |
| `1.2.0-beta.3` | `beta` |
| `0.1.0` | `latest` |

手动补救：`npm dist-tag add @wailsnet/runtime@0.1.0 --tag latest`。

> 注意：**首次发布**（包在 registry 上还不存在）时，npm 会无条件把 `latest` 指向这第一个版本，
> 即使传了 `--tag alpha`。所以 alpha 首发阶段 `latest` 与 `alpha` 会同时指向 `1.0.1-alpha.5`；
> 等首个正式版发布后 `latest` 才会自然归位。

### 暂存模式（staged）踩坑记录

| 现象 | 原因 / 处理 |
|------|-------------|
| `npm stage publish` 成功但 `npm publish` 报 `E409 Cannot publish over previously staged version` | 同一版本已入暂存队列，必须先审批或拒绝该暂存条目 |
| `npm stage approve` 报 `EOTP` | staged 审批**不接受** bypass-2FA token 豁免，必须真人 2FA；网页端 Staged Packages 点 Approve 同理 |
| `npm stage list` 报 401 | granular access token 无该只读端点权限；脚本改为把 stage id 记到 `artifacts/npm/staged-ids.json` |
| `npm publish` 返回 `202 Accepted` 但立刻 `npm view` 是 404 | registry 异步落地，等 1–5 分钟；`npm access list packages @wailsnet` 能立刻看到包已存在 |

### 打包机制说明

脚本先 `pnpm pack` 再 `npm publish <tgz>`：插件包依赖 `"@wailsnet/runtime": "workspace:*"`，
npm 不认 `workspace:` 协议，`pnpm pack` 会在打包时改写成真实版本号；同时 pnpm pack 的产物
就是最终上传内容，便于先在本地检验 tarball 再发。


### 配置 npm Trusted Publisher

首次发布完成后，把 41 个包绑定到本仓库的 GitHub Actions，CI 才能免 token 发布。

#### 前置条件（npm 硬性要求）

| 项 | 要求 / 说明 |
|----|-------------|
| npm CLI | **≥ 11.15.0**（`npm trust` 子命令）；仅发布本身 ≥ 11.5.1 即可 |
| Node | ≥ 22.14.0 |
| Runner | **仅云托管**（GitHub-hosted / GitLab.com shared / CircleCI cloud），自托管 runner 不支持 |
| 账号 | 必须启用 2FA；`npm trust` **不接受 bypass-2FA 的 granular token**，只能交互 2FA |
| 包状态 | 必须已在 registry 存在（本项目 41 个包已于 2026-11-17 首发） |
| package.json | `repository.url` 必须与 GitHub 仓库**完全一致**，否则发布时校验失败。<br>本项目 41 个包已补 `repository`（`git+https://github.com/lytree/Wails.Net.git` + `directory`） |
| 数量 | 每个包只允许 1 条 trust 配置（改字段只能 revoke 后重建） |

#### 方式 A：网页配置（逐包）

`npmjs.com → Packages → 该包 → Settings → Trusted publishing → GitHub Actions`，填：

| 字段 | 值 |
|------|-----|
| Organization or user | `lytree` |
| Repository | `Wails.Net` |
| Workflow filename | `ci.yml`（**只填文件名**，区分大小写，不带 `.github/workflows/` 路径） |
| Environment name | 留空（CI 的 publish-npm job 未声明 environment） |
| Allowed actions | 勾 `npm publish`（或只勾 staged，见下） |

> npm 保存时**不做任何校验**，字段拼错只会在真正发布时以 `ENEEDAUTH` 暴露。

#### 方式 B：CLI 批量（推荐，41 个包一次配完）

```bash
# 先演练，确认命令与包列表
node scripts/trust-publisher.mjs --dry-run

# 真正配置（默认 --allow-publish）
pnpm trust:publisher

# 只允许暂存发布（最高安全姿态，每次发版需你 2FA 审批）
pnpm trust:publisher -- --action=stage
```

脚本按官方建议每次调用间隔 2 秒。**第一次请求会打开浏览器要求 2FA**，在页面上勾选
「Skip two-factor authentication for the next 5 minutes」后，其余 40 个包可在窗口内无感配完
（官方口径约 80 个包 / 5 分钟）。

#### 两种授权模式怎么选

| 配置 | CI 行为 | 适用 |
|------|---------|------|
| `--allow-publish` | tag 触发后 `npm publish` 直接上线 | 日常最省事（本项目默认） |
| `--allow-stage-publish` | 只能 `npm stage publish` 入队，你每次用 2FA 审批 | 防供应链投毒的最高安全姿态 |
| 两者都给 | 两条路都通 | 过渡期 |

> npm 正在收紧 bypass-2FA token 的直接发布权限，长期建议 CI 走 `stage` + 人工审批。

#### 激活、验证、改配置

```bash
npm trust list @wailsnet/runtime           # 查看已有配置与 id
npm trust revoke --id <id> @wailsnet/runtime   # 删除（要改字段必须先删）
```

- 配置创建后必须在 **2 天内成功发布一次**才会激活，逾期作废需重建。
- OIDC 可用于 `npm publish` / `npm stage publish` / `npm dist-tag`；
  **不能**用于 `npm stage list/approve/reject`、`install`、`view`、`npm access`（这些仍需传统认证）。
- `npm whoami` **不能**用来验证 trusted publishing 权限。

#### CI 侧配套（已完成）

```
GitHub Actions (tag 触发)
  └─ publish-npm job
       ├─ permissions: id-token: write      ← 启用 OIDC token 颁发
       ├─ pnpm pack                         ← workspace:* 改写 + 产出 tarball
       └─ npm publish <tgz> --tag <tag>     ← OIDC 换一次性凭证；公开仓库+公开包自动带 provenance
```

> `setup-node` **不要**设 `registry-url`：它会写入 `//registry.npmjs.org/:_authToken=${NODE_AUTH_TOKEN}`
> 占位，而 trusted publishing 不需要也没有该变量，反而导致认证失败。

#### 收尾（绑定验证通过后）

1. 删除仓库的 `NPM_TOKEN` secret（已不再使用）
2. 每个包 Settings → **Publishing access** → 选「Require two-factor authentication and disallow tokens」
   （只禁传统 token，不影响 OIDC）
3. 吊销本地那枚 bypass-2FA 的 granular token


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
dotnet nuget push --dry-run artifacts/packages/Wails.Net.Application.1.0.1-alpha.5.nupkg
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
| `@wailsnet/runtime` | 前端运行时 JS 生成器 |
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

Wails.Net 默认从 `{ContentRoot}/capabilities/` 目录自动加载 Capability JSON 文件并注册到 `PermissionManager`，默认采用最小权限姿态。

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

## 四平台签名流程

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
| `WAILS_SIGN_BACKEND` | `signtool` 或 `azuresigntool`，未设置则跳过签名 |
| `WAILS_SIGN_TIMESTAMP_URL` | 时间戳服务器，默认 `http://timestamp.digicert.com` |
| `WAILS_SIGN_CERT_PATH` | PFX 证书文件路径（signtool 模式） |
| `WAILS_SIGN_CERT_PASSWORD` | PFX 密码（signtool 模式） |
| `WAILS_SIGN_AKV_URL` | Azure Key Vault URL（azuresigntool 模式） |
| `WAILS_SIGN_AKV_CERT` | Key Vault 中的证书名（azuresigntool 模式） |
| `WAILS_SIGN_AZURE_CLIENT_ID` | Azure AD 应用 client id（azuresigntool 模式） |
| `WAILS_SIGN_AZURE_CLIENT_SECRET` | Azure AD 应用 client secret（azuresigntool 模式） |
| `WAILS_SIGN_AZURE_TENANT_ID` | Azure AD 租户 id（azuresigntool 模式） |

在 GitHub Actions 中通过 Repository Secrets 注入上述环境变量。签名失败将中断流水线（与 Android 签名策略一致）。

**signtool 查找顺序**：

1. `PATH` 中的 `signtool.exe`
2. Windows SDK 安装路径：`C:\Program Files (x86)\Windows Kits\10\bin\{version}\{arch}\signtool.exe`（自动取最新版本目录）

**AzureSignTool 查找**：

- `PATH` 中的 `azuresigntool.exe`
- 未找到时提示安装：`dotnet tool install --global AzureSignTool`

### macOS codesign 与公证

**工具**：`codesign` / `xcrun notarytool`（Xcode Command Line Tools 自带）

**CI 自动签名**：`Dist-MacOS` task 在 `.app` 生成后、打包 `.zip` / `.dmg` 之前，按环境变量门控调用 `codesign`（`--options runtime` 启用 Hardened Runtime）：

| 环境变量 | 说明 |
|----------|------|
| `WAILS_MACOS_SIGN_IDENTITY` | Developer ID Application 证书标识（如 `Developer ID Application: Your Name (TEAMID)`）；未设置则跳过，产物仅有 .NET SDK 生成的 ad-hoc 签名，适合内部/本机构建 |

**本地签名与验证**：

```bash
codesign --force --deep --options runtime --timestamp \
  --sign "Developer ID Application: Your Name (TEAMID)" Wails.Net.Demo.app
codesign --verify --deep --strict --verbose=2 Wails.Net.Demo.app
spctl --assess --type execute --verbose Wails.Net.Demo.app
```

**公证（尚未集成）**：`cake.cs` 目前不包含 `notarytool` 步骤。对外分发前需自行提交公证并对 `.dmg` 执行 `stapler staple`，否则 Gatekeeper 仍会拦截从网络下载的 `.zip` / `.dmg`。

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

**构建时签名**：Cake 构建脚本 `cake.cs` 的 `Dist-Android` task 通过 MSBuild 属性配置签名：

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

Cake 构建脚本 `cake.cs` 的 `Dist-Linux` task 默认生成 `tar.gz`，通过 `--linux-formats` 参数可同时生成 `.deb` 和 `.rpm`（支持值：`targz` / `deb` / `rpm`）：

```bash
# 生成全部 Linux 格式
dotnet run cake.cs -- --target=Dist-Linux --platform=linux --rid=all --linux-formats=targz,deb,rpm

# 仅生成 .deb
dotnet run cake.cs -- --target=Dist-Linux --platform=linux --rid=linux-x64 --linux-formats=deb
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
