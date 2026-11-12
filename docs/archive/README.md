# docs/archive

本目录保存已被主文档体系取代的历史文档快照，**不参与日常维护，也不作为当前实现依据**。

| 文件 | 归档日期 | 说明 |
|------|---------|------|
| `comparison-with-tauri2-wails3.md` | 2026-11-12 | 原「Wails.Net vs Tauri 2 vs Wails 3 功能对比详情」三方矩阵。主文档已收敛为 [Wails.Net vs Wails v3 能力矩阵](../comparison-with-wails-v3.md)（两方对比） |
| `fusion-strategy-comparison.md` | 2026-11-12 | 原「架构融合策略对比详情」中含 Tauri v2 参考维度的版本。主文档 [架构策略对比详情](../fusion-strategy-comparison.md) 已将第三维度改为自研表述 |
| `plugins-analysis-2026-08-07.md` | 2026-11-12 | 原仓库根目录 `PLUGINS-ANALYSIS.md`「插件全景分析报告」（2026-08-07 时点）。主文档已由 [插件开发指南](../plugins.md) 与 [插件框架](../architecture/plugin-system.md) 覆盖；原文件的数据来源路径、`UsePluginsFromAssembly` 用法、双包发布状态均以主文档为准 |

## 归档原因

项目自 2026-11 起收敛参考基线：**仅以 Wails v3 作为唯一上游参考实现**（见 `../../AGENTS.md` §1 参考范围）。Tauri v2 不再作为对比基线或设计来源，因此相关对比内容移出主文档树，仅保留快照以便回溯历史结论。

## 使用建议

- 需要了解历史技术判断的来龙去脉时查阅本目录。
- **不要**引用本目录内容作为当前设计依据：其中的版本号、能力矩阵与差距结论均已过时。

## 相关维护规则

- 归档文件保持原样，不随主文档同步修改。
- 新增归档时，请在上表登记文件名、归档日期与被取代的主文档链接。
