# Wails.Net 重构执行命令语言（RHCL）

> - **配套文档**：`docs/architecture/refactor-command-language.md`（本文）驱动 `docs/architecture/refactor-and-hardening-plan.md`（重构加固方案，下称"计划"）。
> - **定位**：RHCL（Refactor & Hardening Command Language）是一套**面向 AI 智能体的执行命令语言**：把计划中的 RH-\*/SEC-H-\*/BK-\* 任务项、守护规则与验证标准，固化为语法确定、前置明确、可验证、可汇报的命令。人类维护者与 AI 代理使用同一套词汇，避免"每次执行都重新解释计划"。
> - **使用方式**：用户向 AI 代理发送 `rh <命令>`（或下方速查表中的完整中文提示词），代理按本文规范展开执行并按 §7 响应协议回报。命令本身**不是**已实现的 CLI——它是给 AI 的执行规约；若未来落地为 `wails-net rh <cmd>` 真实 CLI，语法保持一致。
> - **创建日期**：2026-08-16。

---

## 1. 语法

```
rh <command> [<positional-args>] [--<flag>[=<value>]]
```

- 命令与参数大小写不敏感；任务编号（`RH-`/`SEC-H-`/`BK-`/`R`）与章节号（`§`）除外。
- 一行一条命令；`&&` 不支持——多步场景使用 `rh run-batch` 或顺序发送。

**全局标志**

| 标志 | 含义 |
|---|---|
| `--dry-run` | 只输出执行计划（步骤、涉及文件、验证命令、提交模板），不做任何修改 |
| `--yes` | 显式授权破坏性/不可逆动作（BK-\* 翻转、删除 API/文件、翻转默认值）。未带此标志时代理必须停下询问 |
| `--stage=<n>` | 只执行到第 n 步（按命令规格中的步骤编号） |
| `--json` | 以 §7 协议输出机器可读结果（默认混排人类可读输出） |
| `--scope=<scope>` | 限定验证范围（`unit`/`platform-win`/`platform-linux`/`cli`/`aot`/`all`，见 §6.2） |

---

## 2. 状态模型与状态文件

每个任务项（task item）生命周期：

```
pending → in_progress → (blocked | done)
                ↘ reverted（回滚后回到 pending）
```

**状态文件 `.rh/state.json`**（首次执行任何 `rh run-*` 命令时由代理创建，提交进仓库，等价于计划的"活进度看板"）：

```json
{
  "plan": "docs/architecture/refactor-and-hardening-plan.md",
  "baseline_commit": "<执行开始时的 HEAD>",
  "items": {
    "RH-WIN-01": { "status": "done", "commits": ["<sha>"], "completed_at": "2026-08-16T10:00:00Z" },
    "RH-IPC-02": { "status": "in_progress", "stage": 3 },
    "BK-01":     { "status": "blocked", "reason": "依赖 R1 批次完成" }
  },
  "breaking_window": { "opened": false, "version": "0.2.0" }
}
```

规则：

1. 状态迁移必须与 git 提交一一对应（`done` 必须能列出提交）。
2. `blocked` 必须写明依赖（另一个任务项或批次）。
3. BK-\*（破坏性更改）在 `breaking_window.opened=false` 时只能停在 `blocked`，除非命令带 `--yes` 且用户已确认开启破坏窗口。

---

## 3. 命令目录（系统类）

| 命令 | 作用 | 关键输出 |
|---|---|---|
| `rh status` | 读取 `.rh/state.json` + git log，汇总各批次/任务项状态 | 批次进度表、阻塞项清单、下一步建议 |
| `rh plan [--batch=R0..R5]` | 解析计划文档，列出批次内任务项、依赖、验收标准 | 任务项清单（含验收条款原文） |
| `rh env` | 环境自检：dotnet SDK、TUnit 可运行性、pnpm、WSL（Linux 测试用）、adb（Android 用） | 通过/缺失矩阵（复用 `wails-net doctor` 语义） |
| `rh rules` | 输出 §6 守护规则全文，代理执行前必须先内化 | 规则清单 |

---

## 4. 命令目录（执行类）

> 执行类命令的通用流程：**读状态 → 校验前置 → dry-run 展开 → （用户确认）→ 逐步执行 → 验证 → 提交 → 更新状态**。

### 4.1 `rh run-batch <R0..R5> [--yes] [--dry-run]`

按计划 §9 批次顺序执行其中所有 `pending` 任务项。

- 前置：批次依赖的前序批次无 `pending`/`blocked` 项（R1 依赖 R0，R2 依赖 R1……）。
- 每完成一个任务项即独立提交并更新 `.rh/state.json`（原子性单位 = 任务项，不是批次）。
- R2 批次（破坏窗口）必须带 `--yes`；执行前输出 BK-01~09 影响清单等待确认。

### 4.2 `rh run-item <id> [--yes] [--dry-run] [--stage=<n>]`

执行单个任务项。`<id>` ∈ {`RH-*`, `SEC-H-*`, `BK-*`}。计划文档中的每个编号都必须能在本命令下展开为：

1. **目标**：计划对应小节的"重构方案"。
2. **涉及文件**：该小节"现状"列出的 file:line 证据点。
3. **步骤**：代理自行拆解，但必须覆盖该小节全部要点。
4. **验证**：该小节"验收标准"逐条映射为可执行命令（见 §6.2 测试命令表）。
5. **提交模板**：见 §6.4。

### 4.3 `rh migrate-plugin <pluginName> [--dry-run]`

SEC-H-003 的原子操作：为单个插件完成安全声明迁移。

标准步骤（代理必须按序执行）：

1. 读 `src/Wails.Net.Plugins.<Name>/<Name>Plugin.cs`，盘点全部 `MapCommand`/`MapCommandAsync` 注册点与已声明的权限集（`Permissions.RegisterPermissionSet`）。
2. 逐命令补 `CommandSecurity.Require("<prefix>:allow-xxx")`（已有权限标识的映射到现有关键命令；新增细粒度权限需同步扩展权限集声明）。
3. 无敏感性的命令显式 `.Public()`；lambda 注册改为私有方法 + 方法组（消除 WNET 盲区）。
4. `[ScopeParameter]` 标注路径参数并 `WithScope(...)`（文件/URL/路径类插件必做）。
5. 构建：`dotnet build src/Wails.Net.Plugins.<Name>` —— `TreatWarningsAsErrors=true` 下 WNET001/002 必须零告警。
6. 测试：`dotnet run --project tests/Wails.Net.Plugins.<Name>.Tests`（无测试项目则先创建，覆盖放行 + 拒绝两条路径）。
7. 更新 `.rh/state.json` 中 SEC-H-003 的分项清单（`items["SEC-H-003.plugins"]["<name>"]="done"`）。

优先序（高敏先行）：`Stronghold → Store → Updater → Keychain → Shell(已完) → Process → Opener → Sql → Upload → Websocket → FsWatch → Deeplink → 其余`。

### 4.4 `rh flip <BK-id> --yes`

执行破坏性更改翻转（仅 BK-01~09）。前置：对应任务项代码改造已完成、迁移指南段落已写入 `docs/architecture/permission-migration-guide.md` 或计划文档、drift 测试已补负向用例。翻转与迁移文档必须同一提交。

### 4.5 `rh report [--batch=R*] [--json]`

汇总进度：已完成项（含提交 SHA 与验证证据）、进行中（卡在第几步）、阻塞（原因）、自上次报告以来的测试结果摘要。

---

## 5. 命令目录（验证类）

| 命令 | 作用 | 判定 |
|---|---|---|
| `rh test [--scope=...]` | 按范围运行测试（命令表见 §6.2） | 全绿 = pass |
| `rh acceptance <§>` | 把计划某小节"验收标准"逐条转译为命令/检查并执行（如 §3.1 的"facade 全方法可用"→ 运行新增的端到端测试） | 每条给出 pass/fail + 证据 |
| `rh drift` | 运行全部漂移守门测试（Options/模板 `AppSettingsTemplateDriftTests`、manifest 校验、`scripts/verify-versions.mjs`、npm dist 新鲜度） | 零失败 |
| `rh manifest` | 生成 commands/capabilities/plugins 三份 manifest 并运行 `FindUncoveredCapabilities` 一致性断言 | 未覆盖清单为空（B2 flip 后） |
| `rh aot` | NativeAOT 发布验证（当前 CI 覆盖 CLI；任务项 RH-TEST 扩展到模板 App） | 发布成功零 IL2xxx |

验证失败 = 任务项不得标记 `done`，状态置 `blocked` 并附失败输出。

---

## 6. 守护规则（硬约束，`rh rules` 输出内容）

### 6.1 行为约束（源自 AGENTS.md，违反即中止）

1. 生产代码（`src/`）零运行时反射（§3.4 白名单除外）；新增 `using System.Reflection;` 需说明白名单依据。
2. 测试唯一框架 TUnit，断言必须 `await`；禁止 MSTest/xUnit/NUnit。
3. 脚本一律 F#（.fsx）；**严禁 Python**。
4. `TreatWarningsAsErrors=true`：任何告警都是错误，修到零告警才能提交。
5. 公共 API 中文 XML 文档注释；命名遵循 §3.2；类名不得与命名空间同名。
6. 插件命令禁止把 `CancellationToken` 声明为业务参数（§3.4.6），从 `ctx.CancellationToken` 获取。
7. 每任务项完成必须运行其声明的验证命令并贴出结果；测试未过不得进入下一项（§2.3 工作循环）。

### 6.2 测试命令表（按 scope）

| scope | 命令 |
|---|---|
| `unit` | `dotnet build tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj && dotnet run --project tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj --no-build` |
| `platform-win` | `dotnet run --project tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj` |
| `platform-linux` | `wsl -d kali-linux -- bash -c "cd /mnt/f/Code/github/Wails.Net && dotnet run --project tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj"` |
| `platform-android` | `dotnet run --project tests/Wails.Net.Application.Android.Tests/Wails.Net.Application.Android.Tests.csproj` |
| `cli` | `dotnet run --project tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj` |
| `assetserver` | `dotnet run --project tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj` |
| `aot` | `dotnet publish src/Wails.Net.Cli -p:WailsAot=true -r win-x64`（对齐 `.github/workflows/nativeaot.yml`） |
| `all` | 上述全部（Linux/Android 仅在其可用环境执行，不可用记 skipped 并说明） |

### 6.3 破坏性动作定义（必须 `--yes`）

- 执行任何 `BK-*` 翻转；
- 删除公共 API / 配置字段 / npm 包；
- 翻转任何默认值（权限模式、传输策略）；
- 重写平台窗口基类（BK-03 影响自定义平台实现者）。

### 6.4 提交模板（Conventional Commits）

```
<type>(<scope>): <任务项编号> <一句话摘要>

- <关键改动点 1（含 file:line）>
- <关键改动点 2>
- 验证: <scope>=pass <附加证据，如测试数>

Refs: docs/architecture/refactor-and-hardening-plan.md §<小节>
```

`type` 选择：`fix`（缺陷修复，如 RH-WIN-01）、`feat`（新能力，如 SEC-H-006）、`refactor`（结构不变行为，如 RH-IPC-02 的等价重构部分）、`test`、`docs`、`chore`。`scope` 用子系统名（`sec`/`ipc`/`win`/`host`/`plugin`/`plat`/`ci`）。

---

## 7. 响应协议（`--json` 或代理默认汇报结构）

```json
{
  "command": "rh run-item RH-WIN-01",
  "item": "RH-WIN-01",
  "status": "done | in_progress | blocked | dry-run",
  "steps": [
    { "n": 1, "desc": "IPlatformApp.CreateWebviewWindow 返回 IWebviewWindowImpl", "evidence": ["src/.../IPlatformApp.cs:88"] }
  ],
  "verification": [
    { "name": "platform-win tests", "result": "pass", "detail": "137/137" }
  ],
  "commits": ["<sha>"],
  "breaking": false,
  "next": "建议: rh run-item RH-WIN-02 --dry-run"
}
```

规则：`evidence` 必须是真实的 file:line 或命令输出摘录；`blocked` 必须含 `reason` 与所需的人类决策。

---

## 8. 示例：`rh run-item RH-WIN-01 --dry-run` 的完整展开

**用户输入**：`rh run-item RH-WIN-01 --dry-run`

**代理预期输出（节选）**：

```
[dry-run] RH-WIN-01 WebviewWindow facade 与平台实现断链修复
目标: Impl 全仓库无赋值点（Windows/WebviewWindow.cs:37），facade 全方法抛异常；
      修复后四平台 facade 端到端可用。
计划步骤:
  1. IPlatformApp.CreateWebviewWindow 签名改为返回 IWebviewWindowImpl（4 平台 + Server/Mock 适配）
     - src/Wails.Net.Application/Platform/IPlatformApp.cs
     - src/Wails.Net.Application.Windows/WindowsPlatformApp.cs:471-507
     - src/Wails.Net.Application.Linux/LinuxPlatformApp.cs:717-737
     - src/Wails.Net.Application.Android/AndroidPlatformApp.cs:522
     - src/Wails.Net.Application.MacOS/MacOSPlatformApp.cs:436
  2. WindowManager.CreateWebviewWindow 接收返回值并回填 window.Impl
     - src/Wails.Net.Application/Managers/WindowManager.cs:78-113
  3. 平台内部 _windows 字典降级为缓存（本项不改语义，注册表统一留给 RH-WIN-02）
  4. 新增回归测试: WailsTestHost + MockWebviewWindow 断言 SetTitle/ExecJS/CapturePreviewAsync 可用；
     EventIPCTransport + Mock 窗口的事件下行端到端
验证:
  scope=unit, platform-win
提交模板:
  fix(win): RH-WIN-01 回填 WebviewWindow.Impl，修复 facade 与平台实现断链
破坏性: 否（现路径本就断链，无存量可用行为被改变）
执行: 去掉 --dry-run 后按步骤执行
```

---

## 9. 提示词速查表（可直接复制给任何 AI 代理）

| 目的 | 提示词 |
|---|---|
| 查看进度 | `rh status` |
| 环境自检 | `rh env`（输出不可用项后停下，等我处理） |
| 预演批次 | `rh run-batch R0 --dry-run` |
| 执行批次 | `rh run-batch R0`（每完成一项独立提交并更新 .rh/state.json，测试不过置 blocked） |
| 单项执行 | `rh run-item RH-IPC-02 --dry-run` → 确认后 `rh run-item RH-IPC-02` |
| 迁移插件 | `rh migrate-plugin Stronghold`（按 docs/architecture/refactor-command-language.md §4.3 七步执行） |
| 开破坏窗口 | `rh flip BK-01 --yes`（先输出影响清单与迁移指南 diff） |
| 验收 | `rh acceptance §3.1`（逐条给出 pass/fail 与证据） |
| 汇报 | `rh report --batch R0` |

**给代理的开场提示词模板**（首次会话）：

> 你是 Wails.Net 仓库的执行代理。先阅读 AGENTS.md 与 `docs/architecture/refactor-command-language.md`（RHCL 规范），然后执行 `rh rules` 内化守护规则，再执行我给出的 `rh ...` 命令。破坏性动作必须停下等我确认。所有验证命令来自 RHCL §6.2，不得自造。每完成一个任务项：提交（规范见 §6.4）→ 更新 `.rh/state.json` → 用 §7 协议汇报。

---

## 10. 命令 ↔ 计划任务项对照总表

| 命令 | 覆盖任务项 |
|---|---|
| `rh run-batch R0` | RH-WIN-01；§2.2 缺陷 A/B 修复（不翻转默认值）；§2.5 卫生项；RH-DOC-01 |
| `rh run-batch R1` | RH-WIN-02；RH-IPC-02；RH-IPC-09 |
| `rh run-batch R2`（需 `--yes`） | SEC-H-003（含 `rh migrate-plugin` 全量）；SEC-H-006；BK-01/06/07/08；B2 flip |
| `rh run-batch R3` | RH-PLUGIN-01/02/03 |
| `rh run-batch R4` | RH-IPC-04/05/06/07/08 |
| `rh run-batch R5` | §7 测试与 CI 治理（持续批） |
| `rh run-item SEC-H-004` | Strict 默认化与 Compatibility 语义修正（= BK-01 前置改造） |
| `rh run-item SEC-H-007` | asset protocol scope / convertFileSrc |
| `rh run-item RH-HOST-01..05` | 宿主主线（协调器化、定位器收敛、ChannelManager DI 化等） |

---

**最后更新**：2026-08-16（首版：随 `refactor-and-hardening-plan.md` 一并交付的 AI 执行命令语言规范）
