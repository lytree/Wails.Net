# 分类清单（v3.1.0 → v3.2.0-nightly.20261103）

定性口径沿用 `docs/development/wails-v3-beta-comparison.md` §11–§12：
✅ 已实现/已具备（无需改动）、⏳ 待实现、⛔ 不适用；优先级 P0 阻断 / P1 行为差异 / P2 增强。

| # | 上游变更 | 优先级 | 状态 | 判定依据 |
|---|----------|--------|------|----------|
| 1 | `#6502` Windows 应用级 `SetIcon` 应用到已有窗口 + 保留最后图标供新建窗口套用 | — | ✅ 已具备 | `WindowsPlatformApp.SetIcon` / `_appIconBytes` / `CreateWebviewWindow` 内套用，与上游语义等价 |
| 2 | `#6502` 更换/销毁时 `DestroyIcon` 释放旧句柄 | P2 | ⏳ 待实现（可选） | `LoadIconFromBytes` 每次 `LoadImage` 生成 HICON，全仓无 `DestroyIcon`；改动仅资源释放，无功能影响 |
| 3 | `#6502` `DisableIcon` 时临时清 `WS_SYSMENU` 规避标题栏图标 | P2 | ⛔ 本移植不适用 | `WindowsOptions` 无 `DisableIcon`；属既有功能差距，非本 PR 引入，不做硬凑 |
| 4 | `#6314` Linux 托盘菜单随 `Menu.Update` 重建 dbusmenu 布局 | — | ✅ 已具备（架构差异） | GTK4 侧 `LinuxContextMenu` 每次弹出从源 `Menu.Items` 重建模型；不存在陈旧布局 |
| 5 | `#6314` gtk3 destroy hook 修复 `gtkSignalToMenuItem` 泄漏 | — | ⛔ 不适用 | Wails.Net 仅 GTK4 + 整体重建 action 组，无按 ID 保留的原生项映射 |
| 6 | `#6077` `AndroidOptions.DisableOverscroll` 注释修正 | — | ⛔ 不适用 | 仅 Go 注释；Wails.Net 无 `AndroidOptions` 类型 |
| 7 | `#6348` WebView2 生命周期测试 checkptr 适配 | — | ⛔ 不适用 | 纯 Go 内部测试（`uintptr`→指针），本移植走托管 WebView2 SDK |
| 8 | nightly.20261101 重复列出的 `#6521`/`#6531`/`#6519`/`#6533`/`#6538` | — | ⛔ 不适用（已定性） | 均属 v3.1.0 已发布内容，已在对比文档 §12.2 第 19/20 项定性为 Go 专有 / 发布 CI |
| 9 | 版本 bump（`version.txt`、`@wailsio/runtime` `package.json`） | — | ⛔ 不适用 | 上游发布流水线；本仓库前端运行时为自有 npm 包 |
| 10 | `docs/mpress/**`、`website/**`、showcase、赞助/贡献者图、发布影片、`changelog.md` | — | ⛔ 不适用 | 上游站点与营销物料 |

## 分布统计

| 维度 | 数量 |
|------|------|
| P0 阻断 | **0** |
| P1 行为差异 | **0** |
| P2 增强（可选） | **2**（HICON 生命周期；`DisableIcon` 选项缺失，后者标⛔不适用） |
| 已具备 / 无需改动 | **2**（SetIcon 主体语义、Linux 托盘菜单刷新） |
| 不适用 | **6 组**（#6077 注释、#6348 Go 测试、#6314 gtk3 泄漏、版本 bump、文档/物料、v3.1.0 重复项） |
| 上游代码改动总量 | 30 commits / 61 files；其中生产代码仅 4 个文件（`application_options.go` + `application_windows.go` + `webview_window_windows.go` + Linux 菜单/托盘 4 个文件），其余为测试/文档/版本 |
