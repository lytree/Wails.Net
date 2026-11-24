import { wails } from "./wails-runtime/index.js";

/**
 * Wails.Net 资源嗅探下载器前端。
 * 后端绑定：ResDownloaderService.*
 * 事件：res:sniffed / res:updated / res:proxy / res:cleared / res:deleted
 */

/* ------------------------------------------------------------------ */
/* 国际化                                                              */
/* ------------------------------------------------------------------ */

const I18N = {
    zh: {
        "app.name": "资源下载器",
        "app.sub": "Res Downloader",
        "nav.resources": "获取资源",
        "nav.settings": "系统设置",
        "nav.about": "关于",
        "nav.theme": "主题",
        "resources.title": "获取资源",
        "resources.sub": "开启抓取后，在浏览器 / 小程序 / 客户端里播放或浏览内容即可自动嗅探",
        "resources.grab": "开启抓取",
        "resources.grabbing": "关闭抓取",
        "resources.count": "共 {n} 个资源",
        "resources.type": "抓取类型",
        "resources.typeAll": "全部",
        "resources.search": "搜索描述 / 域名 / 链接",
        "resources.clear": "清空列表",
        "resources.batchDownload": "批量下载",
        "resources.batchCancel": "取消下载",
        "resources.batchExport": "批量导出",
        "resources.batchImport": "批量导入",
        "resources.exportUrl": "导出链接",
        "resources.openFolder": "下载目录",
        "resources.download": "下载",
        "resources.copyLink": "复制链接",
        "resources.copyData": "复制数据",
        "resources.openLink": "打开链接",
        "resources.delete": "删除",
        "resources.preview": "预览",
        "resources.noPreview": "无法预览",
        "resources.decrypt": "视频解密",
        "resources.more": "更多操作",
        "resources.emptyTitle": "还没有嗅探到资源",
        "resources.empty1": "点击左上角「开启抓取」，代理默认监听 127.0.0.1:8899。",
        "resources.empty2": "首次使用请安装根证书，否则 HTTPS 无法解密。",
        "resources.empty3": "开启系统代理后，在浏览器、小程序或客户端里播放目标内容。",
        "resources.selectFirst": "请先选择需要的数据",
        "resources.saveFirst": "请先在设置中配置保存目录",
        "resources.downloadQueued": "已加入队列：{n} 个",
        "resources.nothingToDownload": "所选资源均在下载中或类型不支持",
        "table.domain": "域名",
        "table.type": "类型",
        "table.preview": "预览",
        "table.status": "状态",
        "table.desc": "描述",
        "table.size": "大小",
        "table.path": "保存路径",
        "table.actions": "操作",
        "settings.title": "系统设置",
        "settings.sub": "不清楚的项保持默认即可，修改 Host / Port 会自动重启代理",
        "settings.basic": "基础设置",
        "settings.advanced": "高级设置",
        "settings.saveDir": "保存目录",
        "settings.open": "打开",
        "settings.filename": "文件命名",
        "settings.filenameTime": "末尾加时间",
        "settings.filenameTip": "长度仅在描述非空时生效，0 表示默认 10 个字符",
        "settings.quality": "清晰度",
        "settings.qualityTip": "仅对微信视频号有效",
        "settings.autoProxy": "自动拦截",
        "settings.autoProxyTip": "打开软件时自动启用抓取与系统代理",
        "settings.wxAction": "全量拦截",
        "settings.wxActionTip": "微信视频号是否拦截列表中的全部视频",
        "settings.insertTail": "添入尾部",
        "settings.insertTailTip": "新资源追加到列表末尾，否则插入头部",
        "settings.cert": "根证书",
        "settings.certTip": "用于 HTTPS 解密嗅探。私钥仅保存在本机应用数据目录，不会上传。Windows 安装时会弹出安全警告，需要点击「是」。",
        "settings.installCert": "一键安装",
        "settings.exportCert": "导出 .cer",
        "settings.downloadCert": "浏览器下载",
        "settings.reset": "重置应用",
        "settings.resetTip": "会删除已拦截数据与本地设置并重启应用，请谨慎操作。",
        "settings.resetAction": "清理缓存并重启",
        "settings.upstream": "上游代理",
        "settings.enable": "启用",
        "settings.upstreamTip": "用于结合其他代理工具，格式：http://user:pass@host:port",
        "settings.downloadProxy": "下载代理",
        "settings.downloadProxyTip": "下载时是否使用上游代理",
        "settings.connections": "连接数",
        "settings.connectionsTip": "单任务分片并发，通常 CPU 核心数 ×2",
        "settings.downNumber": "下载数",
        "settings.downNumberTip": "同时进行的下载任务数",
        "settings.headersTip": "下载时重放的请求头，默认 default 使用系统内置集合",
        "settings.rule": "域名规则",
        "settings.ruleTip": "每行一条，* 匹配全部，支持 *.domain 与 ! 排除；后者覆盖前者",
        "settings.mimeMap": "拦截规则（MIME）",
        "settings.mimeMapTip": "JSON 格式，键为 Content-Type，值为类型与后缀，修改后重启生效",
        "settings.saved": "设置已保存",
        "settings.badJson": "拦截规则不是合法的 JSON",
        "settings.badPort": "端口必须在 1-65535 之间",
        "about.title": "关于",
        "about.sub": "一款集资源嗅探 + 多连接下载于一体的桌面工具",
        "about.desc": "通过本地 MITM 代理抓包并筛选可下载资源，原理与 Fiddler / Charles / 浏览器 DevTools 类似，但针对视频、音频、图片、m3u8 与直播流做了更友好的筛选与展示。",
        "about.env": "运行环境",
        "about.links": "相关链接",
        "about.origin": "原项目 res-downloader",
        "about.framework": "Wails.Net 框架",
        "about.disclaimer": "本示例仅用于学习与研究，请勿用于商业或违法用途。下载内容请确保拥有合法授权。",
        "about.name": "名称",
        "about.version": "版本",
        "about.platform": "平台",
        "about.data": "数据目录",
        "about.settingsPath": "设置文件",
        "about.downloadDir": "下载目录",
        "preview.title": "预览",
        "preview.unsupported": "该类型无法直接预览，请复制链接后用播放器打开。",
        "preview.needProxy": "请先开启抓取，预览通过本地代理转发。",
        "import.title": "批量导入",
        "import.tip": "每行一条：URL 编码的 JSON（批量导出的格式）或纯链接。",
        "import.done": "已导入 {n} 条",
        "import.empty": "请输入需要导入的数据",
        "clear.title": "清空列表",
        "clear.desc": "清空所有记录？正在下载的任务会保留。",
        "clear.remember": "记住此选择，下次直接清除",
        "common.save": "保存",
        "common.cancel": "取消",
        "common.submit": "提交",
        "common.confirm": "确认",
        "footer.cert": "证书下载",
        "footer.source": "软件源码",
        "footer.help": "帮助支持",
        "kind.image": "图片",
        "kind.audio": "音频",
        "kind.video": "视频",
        "kind.m3u8": "m3u8",
        "kind.live": "直播流",
        "kind.stream": "流数据",
        "kind.pdf": "pdf",
        "kind.ppt": "演示",
        "kind.xls": "表格",
        "kind.doc": "文档",
        "kind.font": "字体",
        "status.ready": "就绪",
        "status.pending": "待处理",
        "status.running": "运行中",
        "status.error": "错误",
        "status.done": "完成",
        "status.handle": "后续处理",
        "proxy.off": "代理未启动",
        "proxy.on": "代理运行中 {addr}",
        "proxy.sysOn": "系统代理已开启",
        "proxy.sysOff": "系统代理未开启",
        "proxy.certOn": "证书已安装",
        "proxy.certOff": "证书未安装",
        "proxy.startFirst": "请先开启抓取",
        "msg.copyOk": "复制成功",
        "msg.copyFail": "复制失败",
        "msg.certOk": "根证书已安装",
        "msg.certPending": "安装未完成，请确认系统安全警告并点击「是」",
        "msg.certExported": "证书已导出：{path}",
        "msg.decrypting": "解密中…",
        "msg.decryptOk": "解密成功：{path}",
        "msg.noKey": "该资源没有可用的解密密钥",
        "msg.downloadFirst": "请先下载该资源，再执行解密",
        "msg.deleteRunning": "运行中的任务无法删除",
        "msg.selected": "已选 {n} 项",
        "msg.exported": "已导出：{path}",
        "msg.reset": "确认清理缓存并重启应用？",
        "msg.needSelect": "请先选择需要的数据",
        "msg.actionFailed": "操作失败：{err}",
    },
    en: {
        "app.name": "Res Downloader",
        "app.sub": "Wails.Net demo",
        "nav.resources": "Resources",
        "nav.settings": "Settings",
        "nav.about": "About",
        "nav.theme": "Theme",
        "resources.title": "Resources",
        "resources.sub": "Start capturing, then play or browse content in a browser, mini program or app.",
        "resources.grab": "Start capture",
        "resources.grabbing": "Stop capture",
        "resources.count": "{n} resources",
        "resources.type": "Capture types",
        "resources.typeAll": "All",
        "resources.search": "Search description / domain / URL",
        "resources.clear": "Clear list",
        "resources.batchDownload": "Download selected",
        "resources.batchCancel": "Cancel downloads",
        "resources.batchExport": "Export selected",
        "resources.batchImport": "Import",
        "resources.exportUrl": "Export URLs",
        "resources.openFolder": "Open folder",
        "resources.download": "Download",
        "resources.copyLink": "Copy link",
        "resources.copyData": "Copy data",
        "resources.openLink": "Open link",
        "resources.delete": "Delete",
        "resources.preview": "Preview",
        "resources.noPreview": "No preview",
        "resources.decrypt": "Decrypt video",
        "resources.more": "More",
        "resources.emptyTitle": "No resources captured yet",
        "resources.empty1": "Click “Start capture”; the proxy listens on 127.0.0.1:8899.",
        "resources.empty2": "Install the root certificate first, otherwise HTTPS cannot be decrypted.",
        "resources.empty3": "Enable the system proxy, then play the target content.",
        "resources.selectFirst": "Select some rows first",
        "resources.saveFirst": "Set the save directory in settings first",
        "resources.downloadQueued": "Queued: {n}",
        "resources.nothingToDownload": "Selected items are downloading or unsupported",
        "table.domain": "Domain",
        "table.type": "Type",
        "table.preview": "Preview",
        "table.status": "Status",
        "table.desc": "Description",
        "table.size": "Size",
        "table.path": "Save path",
        "table.actions": "Actions",
        "settings.title": "Settings",
        "settings.sub": "Keep defaults if unsure. Changing Host / Port restarts the proxy automatically.",
        "settings.basic": "Basic",
        "settings.advanced": "Advanced",
        "settings.saveDir": "Save directory",
        "settings.open": "Open",
        "settings.filename": "File naming",
        "settings.filenameTime": "Append time",
        "settings.filenameTip": "Length applies only when a description exists; 0 means 10 characters",
        "settings.quality": "Quality",
        "settings.qualityTip": "WeChat Channels only",
        "settings.autoProxy": "Auto capture",
        "settings.autoProxyTip": "Start capture and system proxy on launch",
        "settings.wxAction": "Full capture",
        "settings.wxActionTip": "Capture all videos in WeChat Channels feeds",
        "settings.insertTail": "Append to tail",
        "settings.insertTailTip": "Append new resources, otherwise prepend",
        "settings.cert": "Root certificate",
        "settings.certTip": "Required for HTTPS sniffing. The private key stays in the local app data directory. Windows shows a security warning — click Yes.",
        "settings.installCert": "Install",
        "settings.exportCert": "Export .cer",
        "settings.downloadCert": "Download in browser",
        "settings.reset": "Reset application",
        "settings.resetTip": "Deletes captured data and local settings, then restarts.",
        "settings.resetAction": "Clear cache and restart",
        "settings.upstream": "Upstream proxy",
        "settings.enable": "Enable",
        "settings.upstreamTip": "Chain with another proxy: http://user:pass@host:port",
        "settings.downloadProxy": "Download proxy",
        "settings.downloadProxyTip": "Use the upstream proxy for downloads",
        "settings.connections": "Connections",
        "settings.connectionsTip": "Segments per task, usually CPU cores ×2",
        "settings.downNumber": "Concurrent downloads",
        "settings.downNumberTip": "Tasks downloading at the same time",
        "settings.headersTip": "Headers replayed on download; default uses the built-in set",
        "settings.rule": "Domain rules",
        "settings.ruleTip": "One per line: * matches all, *.domain and ! exclusion supported; later rules win",
        "settings.mimeMap": "Capture rules (MIME)",
        "settings.mimeMapTip": "JSON keyed by Content-Type with type and suffix; restart to apply",
        "settings.saved": "Settings saved",
        "settings.badJson": "Capture rules is not valid JSON",
        "settings.badPort": "Port must be between 1 and 65535",
        "about.title": "About",
        "about.sub": "Resource sniffing plus multi-connection downloading",
        "about.desc": "Captures traffic through a local MITM proxy and filters downloadable resources, similar to Fiddler / Charles / DevTools, but tuned for video, audio, images, m3u8 and live streams.",
        "about.env": "Environment",
        "about.links": "Links",
        "about.origin": "Original project res-downloader",
        "about.framework": "Wails.Net framework",
        "about.disclaimer": "This demo is for learning and research only. Make sure you have the right to download any content.",
        "about.name": "Name",
        "about.version": "Version",
        "about.platform": "Platform",
        "about.data": "Data directory",
        "about.settingsPath": "Settings file",
        "about.downloadDir": "Download directory",
        "preview.title": "Preview",
        "preview.unsupported": "This type cannot be previewed here. Copy the link and open it with a player.",
        "preview.needProxy": "Start capture first — preview is forwarded through the local proxy.",
        "import.title": "Batch import",
        "import.tip": "One per line: URL-encoded JSON (export format) or a plain URL.",
        "import.done": "Imported {n} items",
        "import.empty": "Enter the data to import",
        "clear.title": "Clear list",
        "clear.desc": "Clear all records? Running downloads are kept.",
        "clear.remember": "Remember this choice",
        "common.save": "Save",
        "common.cancel": "Cancel",
        "common.submit": "Submit",
        "common.confirm": "Confirm",
        "footer.cert": "Certificate",
        "footer.source": "Source",
        "footer.help": "Help",
        "kind.image": "Image",
        "kind.audio": "Audio",
        "kind.video": "Video",
        "kind.m3u8": "m3u8",
        "kind.live": "Live",
        "kind.stream": "Stream",
        "kind.pdf": "PDF",
        "kind.ppt": "Slides",
        "kind.xls": "Sheet",
        "kind.doc": "Doc",
        "kind.font": "Font",
        "status.ready": "Ready",
        "status.pending": "Queued",
        "status.running": "Running",
        "status.error": "Error",
        "status.done": "Done",
        "status.handle": "Handle",
        "proxy.off": "Proxy stopped",
        "proxy.on": "Proxy running {addr}",
        "proxy.sysOn": "System proxy on",
        "proxy.sysOff": "System proxy off",
        "proxy.certOn": "Certificate installed",
        "proxy.certOff": "Certificate missing",
        "proxy.startFirst": "Start capture first",
        "msg.copyOk": "Copied",
        "msg.copyFail": "Copy failed",
        "msg.certOk": "Root certificate installed",
        "msg.certPending": "Installation not finished — confirm the system warning with Yes",
        "msg.certExported": "Certificate exported: {path}",
        "msg.decrypting": "Decrypting…",
        "msg.decryptOk": "Decrypted: {path}",
        "msg.noKey": "No decryption key for this resource",
        "msg.downloadFirst": "Download the resource before decrypting",
        "msg.deleteRunning": "Running tasks cannot be deleted",
        "msg.selected": "{n} selected",
        "msg.exported": "Exported: {path}",
        "msg.reset": "Clear cache and restart the application?",
        "msg.needSelect": "Select some rows first",
        "msg.actionFailed": "Failed: {err}",
    },
};

/* ------------------------------------------------------------------ */
/* 状态                                                                */
/* ------------------------------------------------------------------ */

const STORAGE = {
    resources: "rd.resources",
    types: "rd.types",
    rememberClear: "rd.rememberClear",
    theme: "rd.theme",
    locale: "rd.locale",
};

const state = {
    config: null,
    proxy: {
        running: false,
        address: "127.0.0.1:8899",
        systemProxyEnabled: false,
        certificateInstalled: false,
        certificatePath: "",
        certificateUrl: "",
        message: "",
    },
    appInfo: null,
    resources: [],
    availableTypes: ["all"],
    activeTypes: new Set(["all"]),
    keyword: "",
    selected: new Set(),
    locale: "zh",
    theme: "light",
    previewId: null,
    rowMenuId: null,
    confirmHandler: null,
    loadingRows: new Set(),
};

const dom = {};

document.addEventListener("DOMContentLoaded", () => {
    cacheDom();
    applyPreferences();
    bindUi();
    boot();
});

function cacheDom() {
    const ids = [
        "proxyPill", "proxyText", "sysProxyPill", "certPill", "grabBtn", "grabText", "grabCount",
        "typeBtn", "typePanel", "typeDropdown", "moreBtn", "morePanel", "moreDropdown",
        "searchInput", "clearBtn", "batchDownloadBtn",
        "selectAll", "resourceBody", "resourceEmpty", "resCount", "certLink", "sourceLink", "helpLink",
        "view-resources", "view-settings", "view-about",
        "themeBtn", "localeBtn", "localeText",
        "setSaveDirectory", "openSaveFolderBtn", "setFilenameLen", "setFilenameTime", "setQuality",
        "setAutoProxy", "setWxAction", "setInsertTail", "certState", "installCertBtn", "exportCertBtn",
        "openCertUrlBtn", "certPath", "resetBtn", "saveSettingsBtn",
        "setHost", "setPort", "setUpstreamProxy", "setOpenProxy", "setDownloadProxy", "setTaskNumber",
        "setDownNumber", "setUserAgent", "setUseHeaders", "setRule", "setMimeMap",
        "previewModal", "previewBody", "previewCopyBtn", "previewOpenBtn", "previewDownloadBtn",
        "importModal", "importText", "importSubmitBtn",
        "confirmModal", "confirmTitle", "confirmDesc", "confirmRemember", "confirmOkBtn",
        "rowMenu", "toastWrap", "aboutName", "aboutVersion", "aboutEnv", "aboutCertLink",
    ];

    for (const id of ids) {
        if (id.startsWith("view-")) {
            dom[id] = document.getElementById(id);
        } else {
            dom[id] = document.getElementById(id);
        }
    }
}

/* ------------------------------------------------------------------ */
/* 偏好（主题 / 语言）                                                 */
/* ------------------------------------------------------------------ */

function applyPreferences() {
    const storedLocale = localStorage.getItem(STORAGE.locale);
    state.locale = storedLocale === "en" ? "en" : "zh";
    state.theme = localStorage.getItem(STORAGE.theme) || "light";
    applyTheme();
    applyLocale();
}

function applyTheme() {
    let theme = state.theme;
    if (theme === "auto") {
        theme = window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    document.documentElement.dataset.theme = theme;
}

function cycleTheme() {
    const order = ["light", "dark", "auto"];
    const index = order.indexOf(state.theme);
    state.theme = order[(index + 1) % order.length];
    localStorage.setItem(STORAGE.theme, state.theme);
    applyTheme();
    toast(state.theme);
}

function toggleLocale() {
    state.locale = state.locale === "zh" ? "en" : "zh";
    localStorage.setItem(STORAGE.locale, state.locale);
    applyLocale();
    renderProxy();
    renderResources();
    renderTypes();
    renderSettingsSummary();
}

function t(key, vars) {
    const table = I18N[state.locale] || I18N.zh;
    let text = table[key] ?? I18N.zh[key] ?? key;
    if (vars) {
        for (const [name, value] of Object.entries(vars)) {
            text = text.replaceAll(`{${name}}`, String(value));
        }
    }

    return text;
}

function applyLocale() {
    document.documentElement.lang = state.locale === "en" ? "en" : "zh-CN";
    dom.localeText.textContent = state.locale === "zh" ? "EN" : "中";
    dom.searchInput.placeholder = t("resources.search");

    for (const node of document.querySelectorAll("[data-i18n]")) {
        node.textContent = t(node.dataset.i18n);
    }

    for (const node of document.querySelectorAll("[data-i18n-placeholder]")) {
        node.placeholder = t(node.dataset.i18nPlaceholder);
    }

    dom.grabText.textContent = state.proxy.running ? t("resources.grabbing") : t("resources.grab");
}

/* ------------------------------------------------------------------ */
/* 后端调用                                                            */
/* ------------------------------------------------------------------ */

async function call(method, args = []) {
    return await wails.call(`ResDownloaderService.${method}`, args);
}

async function safeCall(method, args = []) {
    try {
        return await call(method, args);
    } catch (error) {
        toast(t("msg.actionFailed", { err: error?.message || String(error) }), "error");
        return null;
    }
}

/* ------------------------------------------------------------------ */
/* 启动                                                                */
/* ------------------------------------------------------------------ */

async function boot() {
    const [config, proxy, appInfo, types] = await Promise.all([
        safeCall("GetConfig"),
        safeCall("GetProxyState"),
        safeCall("GetAppInfo"),
        safeCall("GetAvailableTypes"),
    ]);

    if (config) {
        state.config = config;
        if (!localStorage.getItem(STORAGE.theme) && config.theme) {
            state.theme = config.theme;
            applyTheme();
        }

        if (!localStorage.getItem(STORAGE.locale) && config.locale) {
            state.locale = config.locale === "en" ? "en" : "zh";
            applyLocale();
        }
    }

    if (proxy) {
        state.proxy = proxy;
    }

    if (appInfo) {
        state.appInfo = appInfo;
    }

    if (Array.isArray(types) && types.length > 0) {
        state.availableTypes = types;
    }

    restoreTypeSelection();
    await restoreResources();

    renderTypes();
    renderProxy();
    renderResources();
    renderAppInfo();

    subscribeBackendEvents();
    applyTypeFilterToBackend();
    startReconciler();
}

function restoreTypeSelection() {
    const raw = localStorage.getItem(STORAGE.types);
    if (!raw) {
        state.activeTypes = new Set(["all"]);
        return;
    }

    try {
        const parsed = JSON.parse(raw);
        const known = new Set(state.availableTypes);
        const values = (Array.isArray(parsed) ? parsed : []).filter((value) => known.has(value));
        state.activeTypes = new Set(values.length > 0 ? values : ["all"]);
    } catch {
        state.activeTypes = new Set(["all"]);
    }
}

async function restoreResources() {
    let cache = [];
    try {
        cache = JSON.parse(localStorage.getItem(STORAGE.resources) || "[]");
    } catch {
        cache = [];
    }

    if (!Array.isArray(cache) || cache.length === 0) {
        const list = await safeCall("GetResources");
        state.resources = Array.isArray(list) ? list : [];
        return;
    }

    // 历史列表通过后端重新入库，保证下载 / 删除等操作能按 id 找到资源
    const lines = cache.map((item) => encodeURIComponent(JSON.stringify(item)));
    const imported = await safeCall("ImportResources", [lines]);
    if (Array.isArray(imported) && imported.length > 0) {
        state.resources = imported;
        return;
    }

    const list = await safeCall("GetResources");
    state.resources = Array.isArray(list) ? list : [];
}

function subscribeBackendEvents() {
    wails.events.on("res:sniffed", (item) => {
        if (!item || !item.id || state.resources.some((entry) => entry.id === item.id)) {
            return;
        }

        const insertTail = state.config ? state.config.insertTail !== false : true;
        if (insertTail) {
            state.resources.push(item);
        } else {
            state.resources.unshift(item);
        }

        renderResources();
        pulseRow(item.id);
        persistResources();
    });

    wails.events.on("res:updated", (item) => {
        if (!item || !item.id) {
            return;
        }

        const index = state.resources.findIndex((entry) => entry.id === item.id);
        if (index < 0) {
            return;
        }

        state.resources[index] = { ...state.resources[index], ...item };
        patchRow(state.resources[index]);
        persistResources();
    });

    wails.events.on("res:proxy", (proxy) => {
        if (!proxy) {
            return;
        }

        state.proxy = proxy;
        renderProxy();
    });

    wails.events.on("res:cleared", (payload) => {
        const ids = payload?.ids;
        if (Array.isArray(ids) && ids.length > 0) {
            const set = new Set(ids);
            state.resources = state.resources.filter((item) => !set.has(item.id));
        } else {
            state.resources = [];
        }

        state.selected.clear();
        renderResources();
        persistResources();
    });

    wails.events.on("res:deleted", () => {
        // 删除已由前端同步处理，这里只做持久化
        persistResources();
    });
}

/* ------------------------------------------------------------------ */
/* 兜底对账                                                            */
/* ------------------------------------------------------------------ */

let reconcileTimer = 0;

/**
 * 定时与后端快照对账。
 * 事件通道是首选路径；但若消费的 SDK 包不具备后端事件推送能力，
 * 列表会停在首次拉取的快照上。3 秒一次的轻量对账保证「列表 / 进度」最终一致，
 * 事件正常时它只会读到相同数据、不产生额外渲染。
 */
function startReconciler() {
    if (reconcileTimer) {
        return;
    }

    reconcileTimer = window.setInterval(reconcileWithBackend, 3000);
}

async function reconcileWithBackend() {
    if (document.hidden) {
        return;
    }

    let list;
    try {
        list = await call("GetResources");
    } catch {
        return;
    }

    if (!Array.isArray(list)) {
        return;
    }

    const localIds = state.resources.map((item) => item.id).sort().join(",");
    const remoteIds = list.map((item) => item.id).sort().join(",");

    if (localIds !== remoteIds) {
        state.resources = list;
        for (const id of [...state.selected]) {
            if (!list.some((item) => item.id === id)) {
                state.selected.delete(id);
            }
        }

        renderResources();
        persistResources();
        return;
    }

    let changed = false;
    for (const remote of list) {
        const local = state.resources.find((item) => item.id === remote.id);
        if (!local) {
            continue;
        }

        if (
            local.status !== remote.status
            || local.progress !== remote.progress
            || local.savePath !== remote.savePath
            || local.size !== remote.size
        ) {
            Object.assign(local, remote);
            patchRow(local);
            changed = true;
        }
    }

    if (changed) {
        persistResources();
    }
}

/* ------------------------------------------------------------------ */
/* UI 绑定                                                             */
/* ------------------------------------------------------------------ */

function bindUi() {
    for (const button of document.querySelectorAll(".nav-item")) {
        button.addEventListener("click", () => switchView(button.dataset.view));
    }

    dom.themeBtn.addEventListener("click", cycleTheme);
    dom.localeBtn.addEventListener("click", toggleLocale);
    dom.grabBtn.addEventListener("click", onToggleProxy);
    dom.sysProxyPill.addEventListener("click", onToggleSystemProxy);
    dom.certPill.addEventListener("click", onInstallCert);

    dom.typeBtn.addEventListener("click", (event) => {
        event.stopPropagation();
        dom.typePanel.hidden = !dom.typePanel.hidden;
        dom.morePanel.hidden = true;
    });

    dom.moreBtn.addEventListener("click", (event) => {
        event.stopPropagation();
        dom.morePanel.hidden = !dom.morePanel.hidden;
        dom.typePanel.hidden = true;
    });

    dom.typePanel.addEventListener("click", (event) => event.stopPropagation());
    dom.morePanel.addEventListener("click", (event) => {
        const button = event.target.closest("button[data-more]");
        if (!button) {
            return;
        }

        dom.morePanel.hidden = true;
        onMoreAction(button.dataset.more);
    });

    document.addEventListener("click", () => {
        dom.typePanel.hidden = true;
        dom.morePanel.hidden = true;
        dom.rowMenu.hidden = true;
    });

    dom.searchInput.addEventListener("input", () => {
        state.keyword = dom.searchInput.value.trim().toLowerCase();
        renderResources();
    });

    dom.clearBtn.addEventListener("click", onClear);
    dom.batchDownloadBtn.addEventListener("click", onBatchDownload);
    dom.selectAll.addEventListener("change", onSelectAll);

    dom.resourceBody.addEventListener("click", onResourceClick);
    dom.resourceBody.addEventListener("change", onResourceChange);

    dom.certLink.addEventListener("click", () => openCertificate());
    dom.sourceLink.addEventListener("click", () => safeCall("OpenUrl", ["https://github.com/putyy/res-downloader"]));
    dom.helpLink.addEventListener("click", () => safeCall("OpenUrl", ["https://github.com/putyy/res-downloader/issues"]));
    dom.aboutCertLink.addEventListener("click", () => openCertificate());

    for (const link of document.querySelectorAll("[data-link]")) {
        link.addEventListener("click", () => safeCall("OpenUrl", [link.dataset.link]));
    }

    dom.saveSettingsBtn.addEventListener("click", onSaveSettings);
    dom.openSaveFolderBtn.addEventListener("click", () => safeCall("OpenSaveFolder"));
    dom.installCertBtn.addEventListener("click", onInstallCert);
    dom.exportCertBtn.addEventListener("click", onExportCert);
    dom.openCertUrlBtn.addEventListener("click", () => openCertificate());
    dom.resetBtn.addEventListener("click", onReset);

    for (const tab of document.querySelectorAll(".tab")) {
        tab.addEventListener("click", () => {
            for (const node of document.querySelectorAll(".tab")) {
                node.classList.toggle("active", node === tab);
            }

            for (const panel of document.querySelectorAll(".tab-panel")) {
                panel.classList.toggle("active", panel.id === `tab-${tab.dataset.tab}`);
            }
        });
    }

    for (const closer of document.querySelectorAll("[data-close]")) {
        closer.addEventListener("click", () => {
            const modal = document.getElementById(closer.dataset.close);
            if (modal) {
                modal.hidden = true;
            }

            if (closer.dataset.close === "previewModal") {
                stopPreview();
            }
        });
    }

    dom.previewModal.addEventListener("click", (event) => {
        if (event.target === dom.previewModal) {
            dom.previewModal.hidden = true;
            stopPreview();
        }
    });

    dom.importSubmitBtn.addEventListener("click", onImportSubmit);
    dom.previewCopyBtn.addEventListener("click", () => {
        const resource = findResource(state.previewId);
        if (resource) {
            copyLink(resource);
        }
    });
    dom.previewOpenBtn.addEventListener("click", () => {
        const resource = findResource(state.previewId);
        if (resource) {
            safeCall("OpenUrl", [resource.url]);
        }
    });
    dom.previewDownloadBtn.addEventListener("click", () => {
        const resource = findResource(state.previewId);
        if (resource) {
            downloadResource(resource);
        }
    });

    dom.confirmOkBtn.addEventListener("click", async () => {
        const remember = dom.confirmRemember.checked;
        dom.confirmModal.hidden = true;
        if (typeof state.confirmHandler === "function") {
            await state.confirmHandler(remember);
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            dom.typePanel.hidden = true;
            dom.morePanel.hidden = true;
            dom.rowMenu.hidden = true;
            if (!dom.previewModal.hidden) {
                dom.previewModal.hidden = true;
                stopPreview();
            }
        }
    });

    window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
        if (state.theme === "auto") {
            applyTheme();
        }
    });
}

function switchView(view) {
    for (const button of document.querySelectorAll(".nav-item")) {
        button.classList.toggle("active", button.dataset.view === view);
    }

    for (const section of document.querySelectorAll(".view")) {
        section.classList.toggle("active", section.id === `view-${view}`);
    }

    if (view === "settings" && state.config) {
        fillSettings(state.config);
    }
}

/* ------------------------------------------------------------------ */
/* 代理与证书                                                          */
/* ------------------------------------------------------------------ */

async function onToggleProxy() {
    dom.grabBtn.disabled = true;
    try {
        const proxy = state.proxy.running
            ? await call("StopProxyAsync")
            : await call("StartProxyAsync");
        if (proxy) {
            state.proxy = proxy;
            renderProxy();
            toast(proxy.message || "", proxy.running ? "ok" : null);
        }
    } catch (error) {
        toast(t("msg.actionFailed", { err: error?.message || String(error) }), "error");
    } finally {
        dom.grabBtn.disabled = false;
    }
}

async function onToggleSystemProxy() {
    if (!state.proxy.running) {
        toast(t("proxy.startFirst"), "error");
        return;
    }

    const next = !state.proxy.systemProxyEnabled;
    const proxy = await safeCall("SetSystemProxy", [next]);
    if (proxy) {
        state.proxy = proxy;
        renderProxy();
        toast(proxy.message || "", next ? "ok" : null);
    }
}

async function onInstallCert() {
    const ok = await safeCall("InstallCertificate");
    const installed = await safeCall("IsCertificateInstalled");
    state.proxy.certificateInstalled = Boolean(installed);
    renderProxy();
    renderCertState();
    toast(ok || installed ? t("msg.certOk") : t("msg.certPending"), ok ? "ok" : "error");
}

async function onExportCert() {
    const path = await safeCall("ExportCertificate");
    if (path) {
        dom.certPath.textContent = path;
        toast(t("msg.certExported", { path }), "ok");
    }
}

function openCertificate() {
    const url = state.proxy.certificateUrl || state.proxy.certificatePath;
    if (url) {
        safeCall("OpenUrl", [url]);
    }
}

function renderProxy() {
    const proxy = state.proxy || {};
    const running = Boolean(proxy.running);

    dom.proxyPill.classList.toggle("on", running);
    dom.proxyText.textContent = running
        ? t("proxy.on", { addr: proxy.address || "" })
        : t("proxy.off");

    dom.grabBtn.classList.toggle("on", running);
    dom.grabText.textContent = running ? t("resources.grabbing") : t("resources.grab");

    dom.sysProxyPill.classList.toggle("on", Boolean(proxy.systemProxyEnabled));
    dom.sysProxyPill.textContent = proxy.systemProxyEnabled ? t("proxy.sysOn") : t("proxy.sysOff");

    dom.certPill.classList.toggle("on", Boolean(proxy.certificateInstalled));
    dom.certPill.classList.toggle("warn", !proxy.certificateInstalled);
    dom.certPill.textContent = proxy.certificateInstalled ? t("proxy.certOn") : t("proxy.certOff");

    if (proxy.certificatePath) {
        dom.certPath.textContent = proxy.certificatePath;
    }

    renderCertState();
    renderCount();
}

function renderCertState() {
    const installed = Boolean(state.proxy?.certificateInstalled);
    dom.certState.classList.toggle("on", installed);
    dom.certState.classList.toggle("warn", !installed);
    dom.certState.textContent = installed ? t("proxy.certOn") : t("proxy.certOff");
}

/* ------------------------------------------------------------------ */
/* 类型筛选                                                            */
/* ------------------------------------------------------------------ */

function renderTypes() {
    const types = state.availableTypes.length > 0 ? state.availableTypes : ["all"];
    if (!state.activeTypes || state.activeTypes.size === 0) {
        state.activeTypes = new Set(["all"]);
    }

    const allChecked = state.activeTypes.has("all");
    const rows = [
        `<button class="type-all" data-type="all" type="button">${escapeHtml(t("resources.typeAll"))}</button>`,
    ];

    for (const type of types) {
        if (type === "all") {
            continue;
        }

        rows.push(`
            <label>
                <input type="checkbox" data-type="${escapeHtml(type)}" ${state.activeTypes.has(type) ? "checked" : ""} />
                <span>${escapeHtml(kindLabel(type))}</span>
            </label>`);
    }

    dom.typePanel.innerHTML = rows.join("");

    if (allChecked) {
        const all = dom.typePanel.querySelector('button[data-type="all"]');
        if (all) {
            all.textContent = `✓ ${t("resources.typeAll")}`;
        }
    }

    dom.typePanel.querySelectorAll("input[data-type]").forEach((input) => {
        input.addEventListener("change", () => onTypeToggle(input.dataset.type, input.checked));
    });

    const allButton = dom.typePanel.querySelector('button[data-type="all"]');
    if (allButton) {
        allButton.addEventListener("click", () => {
            state.activeTypes = new Set(["all"]);
            renderTypes();
            applyTypeFilterToBackend();
            renderResources();
            persistTypes();
        });
    }

    const label = state.activeTypes.has("all")
        ? t("resources.typeAll")
        : [...state.activeTypes].map(kindLabel).join(", ");
    dom.typeBtn.querySelector("span").textContent = `${t("resources.type")}: ${label}`;
}

function onTypeToggle(type, checked) {
    if (checked) {
        state.activeTypes.delete("all");
        state.activeTypes.add(type);
    } else {
        state.activeTypes.delete(type);
    }

    if (state.activeTypes.size === 0) {
        state.activeTypes.add("all");
    }

    renderTypes();
    applyTypeFilterToBackend();
    renderResources();
    persistTypes();
}

function applyTypeFilterToBackend() {
    const types = state.activeTypes.has("all") ? ["all"] : [...state.activeTypes];
    safeCall("SetResourceTypes", [types]);
}

function persistTypes() {
    localStorage.setItem(STORAGE.types, JSON.stringify([...state.activeTypes]));
}

/* ------------------------------------------------------------------ */
/* 资源列表                                                            */
/* ------------------------------------------------------------------ */

function filteredResources() {
    const keyword = state.keyword;
    const all = state.activeTypes.has("all");

    return state.resources.filter((item) => {
        if (!all && !state.activeTypes.has(item.classify)) {
            return false;
        }

        if (!keyword) {
            return true;
        }

        return (
            (item.description || "").toLowerCase().includes(keyword) ||
            (item.title || "").toLowerCase().includes(keyword) ||
            (item.url || "").toLowerCase().includes(keyword) ||
            (item.domain || "").toLowerCase().includes(keyword)
        );
    });
}

function renderResources() {
    const list = filteredResources();
    renderCount();

    dom.resourceEmpty.hidden = list.length > 0;
    dom.resourceBody.hidden = list.length === 0;
    dom.resourceBody.innerHTML = list.map(rowHtml).join("");
    syncSelectAll(list);
}

function renderCount() {
    dom.resCount.textContent = t("resources.count", { n: state.resources.length });
    dom.grabCount.hidden = state.resources.length === 0;
    dom.grabCount.textContent = String(state.resources.length);
}

function rowHtml(item) {
    const classify = item.classify || "stream";
    const selected = state.selected.has(item.id);
    const status = item.status || "ready";
    const statusText = statusTextOf(item);
    const title = item.description || item.title || "";
    const preview = previewCellHtml(item);
    const savePath = item.savePath || "";
    const pathText = savePath ? shortenPath(savePath) : "—";

    return `
        <div class="row${selected ? " selected" : ""}" data-id="${escapeHtml(item.id)}">
            <div class="cell check">
                <input type="checkbox" data-select ${selected ? "checked" : ""} />
            </div>
            <div class="cell domain">
                <span title="${escapeHtml(item.url || "")}">${escapeHtml(item.domain || hostOf(item.url))}</span>
            </div>
            <div class="cell type">
                <span class="kind ${escapeHtml(classify)}">${escapeHtml(kindLabel(classify))}</span>
            </div>
            <div class="cell preview">${preview}</div>
            <div class="cell status">
                <button class="status-btn ${escapeHtml(status)}" data-act="status"
                        title="${escapeHtml(item.message || "")}">${escapeHtml(statusText)}</button>
            </div>
            <div class="cell desc">
                <input class="desc-input" data-act="desc" type="text"
                       value="${escapeHtml(title)}" placeholder="—" spellcheck="false" />
            </div>
            <div class="cell size">${escapeHtml(item.size > 0 ? formatBytes(item.size) : "—")}</div>
            <div class="cell path">
                <span class="path-link${savePath ? "" : " empty"}" data-act="path"
                      title="${escapeHtml(savePath)}">${escapeHtml(pathText)}</span>
            </div>
            <div class="cell actions row-actions">
                <button class="icon-btn down" data-act="download" type="button" title="${escapeHtml(t("resources.download"))}">
                    <svg viewBox="0 0 24 24"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>
                </button>
                <button class="icon-btn del" data-act="delete" type="button" title="${escapeHtml(t("resources.delete"))}">
                    <svg viewBox="0 0 24 24"><path d="M4 7h16M9 7V5h6v2M7 7l1 13h8l1-13"/></svg>
                </button>
                <button class="icon-btn more" data-act="menu" type="button" title="${escapeHtml(t("resources.more"))}">
                    <svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="1.4"/><circle cx="12" cy="12" r="1.4"/><circle cx="19" cy="12" r="1.4"/></svg>
                </button>
            </div>
        </div>`;
}

function previewCellHtml(item) {
    if (item.classify === "image") {
        const src = previewUrl(item.url);
        return `<img class="thumb" src="${escapeHtml(src)}" loading="lazy" alt=""
                     data-act="preview" onerror="this.style.visibility='hidden'" />`;
    }

    if (["video", "audio", "m3u8", "live"].includes(item.classify)) {
        return `<button class="mini-link" data-act="preview" type="button">${escapeHtml(t("resources.preview"))}</button>`;
    }

    return `<span class="mini-link muted">${escapeHtml(t("resources.noPreview"))}</span>`;
}

function patchRow(item) {
    const row = dom.resourceBody.querySelector(`.row[data-id="${cssEscape(item.id)}"]`);
    if (!row) {
        renderResources();
        return;
    }

    const status = item.status || "ready";
    const button = row.querySelector(".status-btn");
    if (button) {
        button.className = `status-btn ${status}`;
        button.textContent = statusTextOf(item);
        button.title = item.message || "";
    }

    const sizeCell = row.querySelector(".cell.size");
    if (sizeCell) {
        sizeCell.textContent = item.size > 0 ? formatBytes(item.size) : "—";
    }

    const pathCell = row.querySelector(".path-link");
    if (pathCell) {
        const savePath = item.savePath || "";
        pathCell.textContent = savePath ? shortenPath(savePath) : "—";
        pathCell.title = savePath;
        pathCell.classList.toggle("empty", !savePath);
    }

    // 进度事件很密集，只在终态时落盘，避免高频写 localStorage
    if (status === "done" || status === "error") {
        persistResources();
    }
}

function pulseRow(id) {
    const row = dom.resourceBody.querySelector(`.row[data-id="${cssEscape(id)}"]`);
    if (!row) {
        return;
    }

    row.classList.remove("fresh");
    void row.offsetWidth;
    row.classList.add("fresh");
}

function statusTextOf(item) {
    const status = item.status || "ready";
    if (status === "running") {
        const progress = Number(item.progress || 0);
        return progress > 0 ? `${progress.toFixed(0)}%` : t("status.running");
    }

    return t(`status.${status}`) || status;
}

function syncSelectAll(list) {
    const visible = list ?? filteredResources();
    const selectedVisible = visible.filter((item) => state.selected.has(item.id)).length;
    dom.selectAll.checked = visible.length > 0 && selectedVisible === visible.length;
    dom.selectAll.indeterminate = selectedVisible > 0 && selectedVisible < visible.length;
}

/* ------------------------------------------------------------------ */
/* 行交互                                                              */
/* ------------------------------------------------------------------ */

function findResource(id) {
    return state.resources.find((item) => item.id === id) || null;
}

function rowResourceFromEvent(event) {
    const row = event.target.closest(".row");
    if (!row) {
        return null;
    }

    return findResource(row.dataset.id);
}

async function onResourceClick(event) {
    const actionNode = event.target.closest("[data-act]");
    const resource = rowResourceFromEvent(event);
    if (!resource || !actionNode) {
        return;
    }

    const action = actionNode.dataset.act;
    switch (action) {
        case "download":
            await downloadResource(resource);
            break;
        case "delete":
            await deleteResource(resource);
            break;
        case "status":
            await onStatusClick(resource);
            break;
        case "preview":
            event.stopPropagation();
            openPreview(resource);
            break;
        case "path":
            if (resource.savePath) {
                await safeCall("OpenPath", [resource.savePath]);
            }
            break;
        case "menu":
            event.stopPropagation();
            openRowMenu(resource, actionNode);
            break;
        default:
            break;
    }
}

async function onResourceChange(event) {
    const checkbox = event.target.closest("input[data-select]");
    if (checkbox) {
        const row = checkbox.closest(".row");
        const id = row?.dataset.id;
        if (!id) {
            return;
        }

        if (checkbox.checked) {
            state.selected.add(id);
        } else {
            state.selected.delete(id);
        }

        row.classList.toggle("selected", checkbox.checked);
        syncSelectAll();
        return;
    }

    const input = event.target.closest("input.desc-input");
    if (input) {
        const row = input.closest(".row");
        const id = row?.dataset.id;
        if (!id) {
            return;
        }

        const resource = findResource(id);
        if (!resource) {
            return;
        }

        const value = input.value.trim();
        if (value === (resource.description || "")) {
            return;
        }

        const updated = await safeCall("UpdateDescription", [id, value]);
        if (updated) {
            resource.description = updated.description;
        }
    }
}

async function onStatusClick(resource) {
    if (resource.status === "ready") {
        await downloadResource(resource);
        return;
    }

    if (resource.status === "done" && resource.savePath) {
        await safeCall("OpenPath", [resource.savePath]);
        return;
    }

    if (resource.status === "error") {
        toast(resource.message || t("status.error"), "error");
    }
}

async function downloadResource(resource) {
    if (!state.config?.saveDirectory) {
        toast(t("resources.saveFirst"), "error");
        return;
    }

    if (state.loadingRows.has(resource.id)) {
        return;
    }

    state.loadingRows.add(resource.id);
    try {
        const updated = await call("DownloadResource", [resource.id]);
        if (updated) {
            Object.assign(resource, updated);
            patchRow(resource);
        }
    } catch (error) {
        toast(t("msg.actionFailed", { err: error?.message || String(error) }), "error");
    } finally {
        state.loadingRows.delete(resource.id);
    }
}

async function deleteResource(resource) {
    if (resource.status === "running" || resource.status === "pending") {
        toast(t("msg.deleteRunning"), "error");
        return;
    }

    await safeCall("DeleteResources", [[resource.urlSign]]);
    state.resources = state.resources.filter((item) => item.id !== resource.id);
    state.selected.delete(resource.id);
    renderResources();
    persistResources();
}

function openRowMenu(resource, anchor) {
    const items = [];
    if (resource.status === "running" || resource.status === "pending") {
        items.push({ key: "cancel", label: t("resources.batchCancel") });
    }

    items.push({ key: "copy", label: t("resources.copyLink") });
    if (resource.classify !== "live" && resource.classify !== "m3u8") {
        items.push({ key: "open", label: t("resources.openLink") });
    }

    if (resource.decodeKey) {
        items.push({ key: "decrypt", label: t("resources.decrypt") });
    }

    items.push({ key: "data", label: t("resources.copyData") });
    items.push({ key: "delete", label: t("resources.delete"), danger: true });

    state.rowMenuId = resource.id;
    dom.rowMenu.innerHTML = items
        .map((item) => `<button type="button" data-row-action="${item.key}" class="${item.danger ? "danger" : ""}">${escapeHtml(item.label)}</button>`)
        .join("");

    const rect = anchor.getBoundingClientRect();
    dom.rowMenu.hidden = false;
    const menuRect = dom.rowMenu.getBoundingClientRect();
    const left = Math.min(rect.right - menuRect.width, window.innerWidth - menuRect.width - 10);
    const top = Math.min(rect.bottom + 6, window.innerHeight - menuRect.height - 10);
    dom.rowMenu.style.left = `${Math.max(8, left)}px`;
    dom.rowMenu.style.top = `${Math.max(8, top)}px`;

    dom.rowMenu.onclick = (event) => {
        const button = event.target.closest("button[data-row-action]");
        if (!button) {
            return;
        }

        event.stopPropagation();
        dom.rowMenu.hidden = true;
        const target = findResource(state.rowMenuId);
        if (target) {
            onRowMenuAction(target, button.dataset.rowAction);
        }
    };
}

async function onRowMenuAction(resource, action) {
    switch (action) {
        case "cancel":
            await safeCall("CancelDownload", [resource.id]);
            break;
        case "copy":
            await copyLink(resource);
            break;
        case "open":
            await safeCall("OpenUrl", [resource.url]);
            break;
        case "data":
            await copyText(encodeURIComponent(JSON.stringify(resource)));
            toast(t("msg.copyOk"), "ok");
            break;
        case "decrypt":
            if (!resource.decodeKey) {
                toast(t("msg.noKey"), "error");
                return;
            }

            if (!resource.savePath) {
                toast(t("msg.downloadFirst"), "error");
                return;
            }

            toast(t("msg.decrypting"));
            try {
                const path = await call("DecryptResource", [resource.id]);
                toast(t("msg.decryptOk", { path }), "ok");
            } catch (error) {
                toast(t("msg.actionFailed", { err: error?.message || String(error) }), "error");
            }

            break;
        case "delete":
            await deleteResource(resource);
            break;
        default:
            break;
    }
}

async function copyLink(resource) {
    const ok = await copyText(resource.url);
    toast(ok ? t("msg.copyOk") : t("msg.copyFail"), ok ? "ok" : "error");
}

/* ------------------------------------------------------------------ */
/* 选择与批量                                                          */
/* ------------------------------------------------------------------ */

function onSelectAll() {
    const list = filteredResources();
    if (dom.selectAll.checked) {
        for (const item of list) {
            state.selected.add(item.id);
        }
    } else {
        for (const item of list) {
            state.selected.delete(item.id);
        }
    }

    renderResources();
}

function selectedIds() {
    return [...state.selected];
}

async function onBatchDownload() {
    const ids = selectedIds();
    if (ids.length === 0) {
        toast(t("msg.needSelect"), "error");
        return;
    }

    if (!state.config?.saveDirectory) {
        toast(t("resources.saveFirst"), "error");
        return;
    }

    const count = await safeCall("DownloadResources", [ids]);
    if (count > 0) {
        toast(t("resources.downloadQueued", { n: count }), "ok");
        state.selected.clear();
        renderResources();
    } else {
        toast(t("resources.nothingToDownload"), "error");
    }
}

async function onMoreAction(action) {
    switch (action) {
        case "cancel": {
            const ids = selectedIds();
            if (ids.length === 0) {
                toast(t("msg.needSelect"), "error");
                return;
            }

            const count = await safeCall("CancelDownloads", [ids]);
            toast(`${count}`, "ok");
            break;
        }
        case "export":
            await exportSelected(false);
            break;
        case "exportUrl":
            await exportSelected(true);
            break;
        case "import":
            dom.importText.value = "";
            dom.importModal.hidden = false;
            dom.importText.focus();
            break;
        case "openFolder":
            await safeCall("OpenSaveFolder");
            break;
        default:
            break;
    }
}

async function exportSelected(onlyUrls) {
    const ids = new Set(selectedIds());
    if (ids.size === 0) {
        toast(t("msg.needSelect"), "error");
        return;
    }

    if (!state.config?.saveDirectory) {
        toast(t("resources.saveFirst"), "error");
        return;
    }

    const items = state.resources.filter((item) => ids.has(item.id));
    const content = onlyUrls
        ? items.map((item) => item.url).join("\n")
        : items.map((item) => encodeURIComponent(JSON.stringify(item))).join("\n");

    const path = await safeCall("ExportResources", [content]);
    if (path) {
        toast(t("msg.exported", { path }), "ok");
    }
}

async function onImportSubmit() {
    const text = dom.importText.value.trim();
    if (!text) {
        toast(t("import.empty"), "error");
        return;
    }

    const lines = text.split(/\r?\n/).filter((line) => line.trim().length > 0);
    const added = await safeCall("ImportResources", [lines]);
    dom.importModal.hidden = true;
    if (Array.isArray(added) && added.length > 0) {
        state.resources = await safeCall("GetResources") || state.resources;
        renderResources();
        persistResources();
        toast(t("import.done", { n: added.length }), "ok");
    }
}

/* ------------------------------------------------------------------ */
/* 清空                                                                */
/* ------------------------------------------------------------------ */

function onClear() {
    if (localStorage.getItem(STORAGE.rememberClear) === "1") {
        clearResources();
        return;
    }

    showConfirm({
        title: t("clear.title"),
        desc: t("clear.desc"),
        remember: true,
        onOk: async (remember) => {
            if (remember) {
                localStorage.setItem(STORAGE.rememberClear, "1");
            }

            await clearResources();
        },
    });
}

async function clearResources() {
    const removed = await safeCall("ClearResources", [true]);
    if (Array.isArray(removed)) {
        const set = new Set(removed);
        state.resources = state.resources.filter((item) => !set.has(item.id));
        state.selected.clear();
        renderResources();
        persistResources();
    }
}

function showConfirm({ title, desc, remember, onOk }) {
    dom.confirmTitle.textContent = title;
    dom.confirmDesc.textContent = desc;
    dom.confirmRemember.checked = false;
    dom.confirmRemember.closest("label").hidden = !remember;
    state.confirmHandler = onOk;
    dom.confirmModal.hidden = false;
}

/* ------------------------------------------------------------------ */
/* 预览                                                                */
/* ------------------------------------------------------------------ */

function previewUrl(url) {
    let address = state.proxy?.address || "127.0.0.1:8899";
    if (address.startsWith("0.0.0.0") || address.startsWith("*") || address.startsWith("+")) {
        address = address.replace(/^[^:]+/, "127.0.0.1");
    }

    return `http://${address}/__rdapi/preview?url=${encodeURIComponent(url)}`;
}

function openPreview(resource) {
    state.previewId = resource.id;
    const body = dom.previewBody;

    if (!state.proxy?.running) {
        body.innerHTML = `<div class="preview-hint">${escapeHtml(t("preview.needProxy"))}</div>`;
    } else if (resource.classify === "image") {
        body.innerHTML = `<img src="${escapeHtml(previewUrl(resource.url))}" alt="" />`;
    } else if (resource.classify === "video" || resource.classify === "audio") {
        const tag = resource.classify === "audio" ? "audio" : "video";
        const attrs = tag === "video" ? "controls autoplay playsinline" : "controls autoplay";
        body.innerHTML = `<${tag} ${attrs} src="${escapeHtml(previewUrl(resource.url))}"></${tag}>`;
    } else {
        body.innerHTML = `
            <div class="preview-hint">
                ${escapeHtml(t("preview.unsupported"))}
                <br />
                <code style="word-break:break-all;user-select:text">${escapeHtml(resource.url)}</code>
            </div>`;
    }

    dom.previewModal.hidden = false;
}

function stopPreview() {
    const media = dom.previewBody.querySelector("video, audio");
    if (media) {
        media.pause();
        media.removeAttribute("src");
        media.load();
    }

    dom.previewBody.innerHTML = "";
    state.previewId = null;
}

/* ------------------------------------------------------------------ */
/* 设置                                                                */
/* ------------------------------------------------------------------ */

function fillSettings(config) {
    dom.setSaveDirectory.value = config.saveDirectory || "";
    dom.setFilenameLen.value = config.filenameLen ?? 0;
    dom.setFilenameTime.checked = config.filenameTime !== false;
    dom.setQuality.value = String(config.quality ?? 0);
    dom.setAutoProxy.checked = Boolean(config.autoProxy);
    dom.setWxAction.checked = config.wxAction !== false;
    dom.setInsertTail.checked = config.insertTail !== false;
    dom.setHost.value = config.host || "127.0.0.1";
    dom.setPort.value = config.port ?? 8899;
    dom.setUpstreamProxy.value = config.upstreamProxy || "";
    dom.setOpenProxy.checked = Boolean(config.openProxy);
    dom.setDownloadProxy.checked = Boolean(config.downloadProxy);
    dom.setTaskNumber.value = config.taskNumber ?? 8;
    dom.setDownNumber.value = config.downNumber ?? 3;
    dom.setUserAgent.value = config.userAgent || "";
    dom.setUseHeaders.value = config.useHeaders || "default";
    dom.setRule.value = config.rule || "*";
    dom.setMimeMap.value = JSON.stringify(config.mimeMap || {}, null, 2);
    renderCertState();
}

async function onSaveSettings() {
    if (!state.config) {
        return;
    }

    let mimeMap;
    try {
        mimeMap = JSON.parse(dom.setMimeMap.value || "{}");
    } catch {
        toast(t("settings.badJson"), "error");
        return;
    }

    const port = Number(dom.setPort.value);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
        toast(t("settings.badPort"), "error");
        return;
    }

    const payload = {
        ...state.config,
        saveDirectory: dom.setSaveDirectory.value.trim(),
        filenameLen: Number(dom.setFilenameLen.value) || 0,
        filenameTime: dom.setFilenameTime.checked,
        quality: Number(dom.setQuality.value) || 0,
        autoProxy: dom.setAutoProxy.checked,
        wxAction: dom.setWxAction.checked,
        insertTail: dom.setInsertTail.checked,
        host: dom.setHost.value.trim() || "127.0.0.1",
        port,
        upstreamProxy: dom.setUpstreamProxy.value.trim(),
        openProxy: dom.setOpenProxy.checked,
        downloadProxy: dom.setDownloadProxy.checked,
        taskNumber: Number(dom.setTaskNumber.value) || 8,
        downNumber: Number(dom.setDownNumber.value) || 3,
        userAgent: dom.setUserAgent.value.trim(),
        useHeaders: dom.setUseHeaders.value.trim() || "default",
        rule: dom.setRule.value,
        mimeMap,
        theme: state.theme,
        locale: state.locale,
    };

    const saved = await safeCall("SaveConfigAsync", [payload]);
    if (!saved) {
        return;
    }

    state.config = saved;
    const types = await safeCall("GetAvailableTypes");
    if (Array.isArray(types) && types.length > 0) {
        state.availableTypes = types;
        const known = new Set(types);
        for (const type of [...state.activeTypes]) {
            if (!known.has(type)) {
                state.activeTypes.delete(type);
            }
        }

        if (state.activeTypes.size === 0) {
            state.activeTypes.add("all");
        }

        renderTypes();
    }

    const proxy = await safeCall("GetProxyState");
    if (proxy) {
        state.proxy = proxy;
        renderProxy();
    }

    toast(t("settings.saved"), "ok");
}

function renderSettingsSummary() {
    renderCertState();
}

function onReset() {
    showConfirm({
        title: t("settings.reset"),
        desc: t("settings.resetTip"),
        remember: false,
        onOk: async () => {
            localStorage.removeItem(STORAGE.resources);
            localStorage.removeItem(STORAGE.types);
            localStorage.removeItem(STORAGE.rememberClear);
            await safeCall("ResetApp");
        },
    });
}

/* ------------------------------------------------------------------ */
/* 关于                                                                */
/* ------------------------------------------------------------------ */

function renderAppInfo() {
    const info = state.appInfo || {};
    dom.aboutName.textContent = info.name || "Wails.Net Res Downloader";
    dom.aboutVersion.textContent = `v${info.version || "1.0.0"}`;

    const rows = [
        [t("about.version"), `v${info.version || "1.0.0"}`],
        [t("about.platform"), info.platform || ""],
        [t("about.data"), info.dataDirectory || ""],
        [t("about.settingsPath"), info.settingsPath || ""],
        [t("about.downloadDir"), info.downloadDirectory || ""],
    ];

    dom.aboutEnv.innerHTML = rows
        .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`)
        .join("");
}

/* ------------------------------------------------------------------ */
/* 持久化与工具                                                        */
/* ------------------------------------------------------------------ */

function persistResources() {
    try {
        localStorage.setItem(STORAGE.resources, JSON.stringify(state.resources));
    } catch {
        // 超出 localStorage 配额时逐步裁剪历史记录
        const trimmed = state.resources.slice(-300);
        try {
            localStorage.setItem(STORAGE.resources, JSON.stringify(trimmed));
        } catch {
            // 仍然失败则放弃本次持久化
        }
    }
}

function kindLabel(classify) {
    return t(`kind.${classify}`) || classify;
}

function hostOf(url) {
    try {
        return new URL(url).host;
    } catch {
        return "";
    }
}

function shortenPath(path) {
    if (!path) {
        return "—";
    }

    const normalized = path.replaceAll("\\", "/");
    const parts = normalized.split("/");
    return parts.length <= 2 ? normalized : `…/${parts.slice(-2).join("/")}`;
}

function formatBytes(value) {
    const bytes = Number(value) || 0;
    if (bytes < 1024) {
        return `${bytes} B`;
    }

    const units = ["KB", "MB", "GB", "TB"];
    let size = bytes / 1024;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
        size /= 1024;
        unit += 1;
    }

    return `${size >= 100 ? size.toFixed(0) : size.toFixed(1)} ${units[unit]}`;
}

function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function cssEscape(value) {
    return window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/["\\]/g, "\\$&");
}

async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        try {
            const area = document.createElement("textarea");
            area.value = text;
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            const ok = document.execCommand("copy");
            document.body.removeChild(area);
            return ok;
        } catch {
            return false;
        }
    }
}

function toast(message, type) {
    if (!message) {
        return;
    }

    const node = document.createElement("div");
    node.className = `toast${type ? ` ${type}` : ""}`;
    node.textContent = message;
    dom.toastWrap.appendChild(node);

    window.setTimeout(() => {
        node.style.opacity = "0";
        window.setTimeout(() => node.remove(), 220);
    }, 2600);
}
