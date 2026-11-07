# v3.1.0 → v3.2.0-nightly.20261103 变更来源明细（原始取证）

- 取证时间：2026-11-07（Asia/Shanghai）
- 取证通道：GitHub REST API（releases / tags / compare / pulls / pulls.files）+ raw.githubusercontent + npm registry
- 原始响应快照：`001-releases.json`、`002-compare-v3.1.0-nightly20261103.json`、`003-npm-runtime.json`

## 1. 版本核实

| 项目 | 值 | 时间（UTC） | 来源 |
|------|----|-------------|------|
| 最新正式版 | `v3.1.0` | 2026-11-01 11:57:27 | releases API |
| 最新 nightly | `v3.2.0-nightly.20261103` | 2026-11-03 15:21:58 | releases API + tags API |
| 次新 nightly | `v3.2.0-nightly.20261102` | 2026-11-02 15:22:50 | releases API |
| 最早 v3.2.0 nightly | `v3.2.0-nightly.20261101` | 2026-11-01 15:19:58 | releases API |
| npm `@wailsio/runtime` dist-tag `latest` | `3.1.0` | 2026-11-01 11:59:42 | npm registry |
| npm `@wailsio/runtime` dist-tag `nightly` | `3.2.0-nightly.20261103` | 2026-11-03 15:27:34 | npm registry |
| v3.1.x hotfix | 无 | — | releases API（v3.1.0 上方仅 nightly） |

结论：自 `v3.1.0` 起没有新的正式版，最新可观察上游状态为 `v3.2.0-nightly.20261103`（预发布，按 AGENTS.md §1.1.1 不作基线）。

## 2. compare 结果

`/compare/v3.1.0...v3.2.0-nightly.20261103`：

- `status=diverged`，`ahead_by=30`，`behind_by=1`，`total_commits=30`，`files=61`
- `behind_by=1` 来自 tag 提交（`v3.1.0` tag 指向 `4b454e44`，master 上的对应提交为 `e4d963d6` chore 同步版本），非内容回退；文件级 diff 已完整获取（61 个文件）。

30 个提交按性质归并：

| 提交 | 说明 | 归属 |
|------|------|------|
| `95b5ea54` | DisableOverscroll instead of DisableBounce（仅注释） | PR #6077 |
| `cc28190a` | fix(v3/linux): refresh systray menus on Menu.Update | PR #6314 |
| `64fd9300` / `370e37fb` / `662c0018` | feat: Implement SetIcon for windows + 两轮 CodeRabbit 修订 | PR #6502 |
| `8b5c397a` / `16ef0137` / `2eddde8e` 等 | WebView2 生命周期测试 checkptr 适配（仅测试） | PR #6348 |
| `e4d963d6` `63bc0010` `69fc9aca` `332f3f37` | 版本同步 / nightly bump（含 `version.txt`、npm 包 `package.json`） | chore |
| `8b86a407` `3076c5cc` `2da1513a` `ad40c2e8` `9993edfe` `6afe11e3` `282b5e6f` `d7124354` `cf095416` `6666b7f3` | 文档 / showcase / 赞助与贡献者图 / 发布影片 | docs |

## 3. 四个代码相关 PR

### 3.1 PR #6077（`v3.2.0-nightly.20261102`）

- 标题：`DisableOverscroll instead of DisableBounce`
- 变更：`v3/pkg/application/application_options.go` **+1/-1**，仅改注释（把 `// DisableBounce disables…` 改为 `// DisableOverscroll …`，字段名本来就是 `DisableOverscroll bool`）
- 无行为变化、无测试

### 3.2 PR #6502（`v3.2.0-nightly.20261103`）

- 标题：`[v3/Windows] Implement SetIcon for windows`
- 变更：`application_windows.go` +51/-1、`webview_window_windows.go` +6/-0
- 行为：
  1. `windowsApp.setIcon(icon)` 由原来的空实现改为 `InvokeAsync` 内执行：`CreateLargeHIconFromImage` → 缓存到 `currentIcon` → 快照全部窗口 → 逐个 `applyIcon` → `DestroyIcon(previousIcon)`；
  2. 新增 `applyIcon`：`windows.WebviewWindowOptions.DisableIcon == false` 时直接 `window.setIcon(icon)`；为 `true` 时临时清零 `WS_SYSMENU` → `setIcon` → 恢复样式（规避 Windows 把图标加回标题栏）；
  3. `windowsWebviewWindow.run()` 中新增：若 `currentIcon != 0`，对新创建窗口套用最后一次设置的图标；
  4. `windowsApp.destroy()` 中新增 `DestroyIcon(currentIcon)` 并清零。
- 测试：无新增测试（PR 描述为手工在 Windows 验证 taskbar/titlebar 图标）

### 3.3 PR #6314（`v3.2.0-nightly.20261103`）

- 标题：`[v3] refresh systray menus on Menu.Update`
- 变更：`menu_linux.go` +2、`menu_linux_gtk3.go` +2、`menuitem_linux_gtk3.go` +6/-2（gofmt 计 +6/-0）、`systemtray_linux.go` +24、新增 `systemtray_linux_update_test.go` +54、`menu_linux_gtk3_test.go` +20
- 行为：
  1. `linuxMenu.update()` 两种构建变体均在 `processMenu` 之前 `defer refreshSystemTrayMenus(m.menu)`；
  2. `refreshSystemTrayMenus` 在 `InvokeSync` 内收集所有 `tray.menu == menu` 的 `linuxSystemTray`，逐个 `setMenu(menu)` 重建 dbusmenu 布局（bump revision、发 `LayoutUpdated`）；
  3. gtk3 destroy hook：被移除菜单项的 `gtkSignalToMenuItem` 条目在「新原生 item 未接管」时删除，修复 `Menu.Update` 反复重建导致的 handler 泄漏；
  4. 已知限制（PR 描述）：子菜单 `Update` 不刷新托盘；同一 `Menu` 同时被托盘与原生菜单栏/上下文菜单共享时 `impl` 单槽冲突（本 PR 让 `Update` 后托盘成为持有者）。
- 测试：新增 `TestLinuxSystemTrayMenuUpdate`（rebuild 后 itemMap 含新项、revision 递增、`item.impl` 为 `*systrayMenuItem`）+ gtk3 集成用例补充托盘绑定段

### 3.4 PR #6348（`v3.2.0-nightly.20261103`）

- 标题：`test(v3/windows): keep the WebView2 lifetime tests valid under checkptr (-race)`
- 变更：`internal/webview2/pkg/edge/` 下 5 个 `*_test.go`（+98/-38）
- 性质：**纯测试**（`-race` 的 checkptr 拒绝把 `uintptr` 转回 Go 指针），无生产代码、无 changelog 条目

## 4. nightly.20261101 的“新增”项实为 v3.1.0 内容

`v3.2.0-nightly.20261101` release notes 与 `v3.1.0` 重复列出的 `#6521`（CGO-free macOS/Linux 后端）、`#6531`（原生 YAML 构建/便携打包/签名）、`#6519`/`#6533`/`#6538`（发布校验/CI）在 `v3.1.0` 已发布，已在对比文档 §12.2 第 19/20 项定性为 ⛔ 不适用，本轮无新增内容。

## 5. 未能取证项

- GitHub `/commits?since=…`（列出 nightly.20261103 之后 master 未发布提交）连续返回 502/超时，未取得列表；本轮范围止于 `v3.2.0-nightly.20261103`，不影响结论。下次基线复核时需重新核对。
