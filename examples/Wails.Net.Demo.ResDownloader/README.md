# Wails.Net Demo - ResDownloader（资源嗅探下载器）

功能对齐 [putyy/res-downloader](https://github.com/putyy/res-downloader)，用 Wails.Net（.NET 10）重新实现：
本地 MITM 代理嗅探网页 / 小程序 / 短视频平台中的**视频、音频、图片、m3u8** 资源，并支持批量下载、预览与解密。

> 本项目仅用于学习与研究，请勿用于商业或违法用途。

## 功能

### 代理与嗅探

- **本地 MITM 代理**：监听 `127.0.0.1:8899`，HTTP 直接转发，HTTPS 通过动态签发叶证书解密后再转发
- **域名规则**（对齐 `core/rule.go`）：`*` / `*.example.com` / 精确域名 / `!` 否定，自上而下后者覆盖前者；规则外的域名走**裸隧道透传**（不解密），避免影响正常业务流量与证书固定场景
- **上游代理级联**：可指定上游 HTTP 代理（HTTP 走 absolute-form，HTTPS 走 CONNECT 隧道）
- **可编辑 MimeMap**：`Content-Type → (类型, 后缀)` 全量可改，`default` 表示按 URL 扩展名推断；另内置扩展名兜底表处理 `application/octet-stream` 的 CDN
- **抓取类型筛选**：视频/音频/图片/m3u8/直播/文档/字体等类型即时过滤，前端与后端同时生效
- **请求头捕获**：嗅探时记录 `User-Agent / Referer / Cookie / Origin / Accept` 等，下载时按 `UseHeaders` 白名单重放，缺 Referer 时用同源补全（对抗防盗链）
- **微信视频号**：向详情页注入脚本，改写 `get media()` 与 `finderGetCommentDetail()`，把 `objectDesc` 上报到 `/res-downloader/wechat`；解析直链、封面、`decodeKey`、`spec.fileFormat`
- **本地 API**：`/__rdapi/cert`（下载根证书）、`/__rdapi/preview?url=`（带 CORS / Range / 防盗链头的资源预览转发）、`/__rdapi/ping`

### 下载

- **多连接分片下载**：先 `Range: bytes=0-0` 探测（`206` + `Content-Range` + 大于 1MB 才启用），按 `任务数` 切片并发拉取 `.partN` 后按序合并，单片失败重试
- **m3u8（HLS）**：主列表选最高码率、分片并发、`AES-128` 解密（PKCS7 与 None 两种填充）、`EXT-X-MAP` / `BYTERANGE`、按序合并为 `.ts`
- **视频解密**：按 `decodeKey`（Base64）与文件前 N 字节异或，生成 `_decrypt` 副本（等价 `decodeWxFile`）
- **清晰度**（`Quality`）：视频号场景按 `wx_file_formats` 追加 `X-snsvideoflag`，或重建带 `encfilekey` / `token` 的 URL
- **文件命名**：描述优先 → 非法字符清理 → 长度截断（`文件名长度`，0 视为 10）→ 可选时间戳后缀（`_yyyyMMddHHmmss`）→ 补后缀 → 同名去重
- **队列与并发**：`下载任务数` 控制切片，`同时下载数` 控制在跑任务；支持单条/批量取消、进度与速度、失败重试与错误信息透出

### 交互

- **列表**：9 列（勾选/标题/类型/状态/大小/域名/时间/路径/操作）、搜索、类型徽标、插入头部/尾部（`InsertTail`）、列表持久化（`localStorage`，配额溢出自动裁剪）
- **批量**：批量下载、批量取消、批量删除、导出选中链接 / 导出全部、导入（纯 URL 或导出文件内容）
- **单条操作**：预览（图片/视频/音频走本地 API）、下载/取消、打开文件、打开所在目录、复制链接、复制资源数据、改描述、解密、删除
- **清空与重置**：清空可保留运行中任务；重置应用恢复默认设置并重启进程
- **主题与语言**：浅色/深色/跟随系统，中英双语（`zh` / `en`）

## 运行

```bash
dotnet run --project examples/Wails.Net.Demo.ResDownloader/Wails.Net.Demo.ResDownloader.csproj
```

依赖通过 NuGet 引用（`Wails.Net.Sdk`），不使用 `ProjectReference`。本地未发布包时先填充本地源：

```bash
bash scripts/pack-and-test.sh --skip-pack   # 已有包时的校验流程
dotnet pack src/Wails.Net.Sdk/Wails.Net.Sdk.csproj -c Release -o artifacts/nupkg
```

前端 `frontend/wails-runtime/` 是 `@wailsnet/runtime` 构建产物（`packages/wails-net-runtime/dist`）的副本，
版本需与所引用的 NuGet 包保持一致；修改运行时后执行 `dotnet fsi scripts/sync-runtime.fsx` 重新同步。

## 使用步骤

1. 启动应用 → 右上角「启动代理」。
2. 首次使用打开「设置 → 基础」→ 根证书「一键安装」，在系统安全警告中点击「是」。
3. 点击顶栏「系统代理未开启」开启系统代理（或手动把系统代理设为 `127.0.0.1:8899`）。
4. 在浏览器 / 微信 / 客户端里播放目标内容，返回应用即可看到资源列表。
5. 点击「下载」加入队列；m3u8 会自动合并为 `.ts`。

> 关闭应用后如无法上网，请手动关闭系统代理设置；应用正常退出时会自动还原。

## 架构

```
Services/
  Models.cs                资源/任务/设置/状态 DTO（前端契约）
  AppPaths.cs              应用数据目录与默认下载目录
  SettingsStore.cs         settings.json 读写 + 字段规范化（端口/并发/语言/主题/MimeMap…）
  CertificateAuthority.cs  根 CA 生成/持久化、叶证书签发缓存、证书安装与导出
  Http1.cs                 HTTP/1.x 报文解析、缓冲读取器（含 Detach 回放）、下游响应拼装
  DomainRules.cs           域名规则解析与「是否解密」判定
  MimeTypeMap.cs           默认 MimeMap、扩展名兜底表、Content-Type → (类型, 后缀)
  ResourceNaming.cs        文件名/标题生成、顶级域名、Content-Disposition 解析
  ResourceRepository.cs    资源列表（URL 签名去重、有界、插入头/尾）
  LocalProxyServer.cs      MITM 代理：CONNECT/隧道分流、上游代理、转发、嗅探、视频号注入与上报、本地 API
  DownloadManager.cs       下载队列：并发、进度、取消、分片下载、请求头重放、清晰度、命名
  M3u8Downloader.cs        HLS：选码率、分片并发、AES-128、EXT-X-MAP/BYTERANGE、合并
  MediaDecryptor.cs        视频解密（decodeKey 异或）
  SystemProxyManager.cs    Windows WinINet 系统代理开关与还原
  ResDownloaderService.cs  绑定门面：前端 API + 事件桥接 + 生命周期清理
frontend/                  纯 HTML/CSS/JS（前端运行时来自 frontend/wails-runtime）
```

### 绑定方法（`wails.call('ResDownloaderService.方法名', [参数...])`）

| 分组 | 方法 |
| --- | --- |
| 配置 | `GetConfig`、`SaveConfigAsync`（Host/Port 变更会自动重启代理）、`ResetApp` |
| 应用 | `GetAppInfo`、`GetAvailableTypes`、`SetResourceTypes` |
| 代理 | `GetProxyState`、`StartProxyAsync`、`StopProxyAsync`、`SetSystemProxy` |
| 证书 | `ExportCertificate`、`InstallCertificate`、`IsCertificateInstalled`、`GetCertificateUrl` |
| 列表 | `GetResources`、`ClearResources(keepActive)`、`DeleteResources(signs[])`、`ImportResources(lines[])`、`ExportResources(content)`、`UpdateDescription(id, desc)` |
| 下载 | `DownloadResource(id)`、`DownloadResources(ids[])`、`CancelDownload(id)`、`CancelDownloads(ids[])`、`DecryptResource(id)` |
| 系统 | `OpenSaveFolder`、`OpenPath(path)`、`OpenUrl(url)` |

### 事件（后端 → 前端）

| 事件 | 载荷 | 说明 |
| --- | --- | --- |
| `res:sniffed` | `ResourceItem` | 新资源入库（按 `InsertTail` 插头/尾） |
| `res:updated` | `ResourceItem` | 下载进度/状态/保存路径变化（替代旧 `res:task`） |
| `res:proxy` | `ProxyState` | 代理启停、系统代理、上游代理、证书状态 |
| `res:cleared` | `{ ids }` | 列表清空（`ids` 为被清掉的条目，保留运行中时非空） |
| `res:deleted` | `{ ids }` | 条目删除 |

### 设置项

| 键 | 说明 |
| --- | --- |
| `host` / `port` | 代理监听地址与端口（默认 `127.0.0.1:8899`） |
| `theme` / `locale` | `light`/`dark`/`auto`；`zh`/`en` |
| `quality` | 清晰度档位（0–4，视频号用） |
| `saveDirectory` | 保存目录（留空取系统下载目录） |
| `filenameLen` / `filenameTime` | 文件名长度上限（0≈10）与时间戳后缀 |
| `upstreamProxy` / `openProxy` | 上游代理地址与开关 |
| `downloadProxy` / `autoProxy` | 下载是否走上游代理；启动时是否自动设置系统代理 |
| `wxAction` | 视频号「全量拦截」（false 时只接受 `objectDesc` 上报） |
| `taskNumber` / `downNumber` | 分片连接数（1–16）与同时下载数（1–10） |
| `userAgent` / `useHeaders` | 下载 UA；请求头重放白名单（`default` 或逗号分隔） |
| `insertTail` | 新资源插入尾部 |
| `mimeMap` | `Content-Type → (type, suffix)` 映射表 |
| `rule` | 域名规则（是否解密） |

## 实现要点

- **只协商 HTTP/1.1**：MITM 后强制 `ApplicationProtocols = [Http11]`，避免 HTTP/2 多路复用破坏「一请求一响应」的转发模型。
- **上游连接不复用**：转发给源站时统一加 `Connection: close`，响应按 `Content-Length` / `chunked` / EOF 三种分帧精确转发，保证客户端侧仍可 keep-alive。
- **CONNECT 后的字节回放**：`HttpBufferedReader.Detach(stream)` 把已读入缓冲区的 TLS 首字节回放给后续 TLS 握手（上游代理隧道同理）。
- **下载不走本机代理**：`SocketsHttpHandler.UseProxy = false`（仅显式开启下载代理时才用上游），否则系统代理指向本机会形成回环。
- **注入前先解压**：改写视频号页面脚本时按 `Content-Encoding` 解压（gzip/deflate/br）并剥离 `Accept-Encoding`，改写后回填 `Content-Length`。
- **证书私钥不外传**：根证书私钥仅保存在本机应用数据目录（`%LOCALAPPDATA%\Wails.Net.ResDownloader\root-ca.pfx`）。
- **平台差异**：系统代理与证书存储安装仅在 Windows/macOS 生效；Linux 需手动把 `.cer` 放入
  `/usr/local/share/ca-certificates` 并执行 `update-ca-certificates`。

## 已知限制

- 不解密启用证书固定（certificate pinning）的 App 流量，此类流量无法嗅探；需要时用域名规则把它排除在 MITM 之外。
- 直播流（FLV / HLS-LIVE）只做识别与列表展示，不做长时间录制。
- 视频号注入依赖页面脚本结构，平台改版后需要同步调整正则与上报格式。
- 仅转发 HTTP/1.1，不做 WebSocket 嗅探；上游代理的 HTTPS 级联走 CONNECT 隧道（不解密二次 MITM）。
