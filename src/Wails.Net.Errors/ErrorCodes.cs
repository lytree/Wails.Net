namespace Wails.Net.Errors;

/// <summary>
/// 定义 Wails.Net 框架使用的标准错误代码。
/// 对应 Wails v3 Go 版本 pkg/errs 包中的错误代码定义。
/// </summary>
public enum ErrorCodes
{
    /// <summary>
    /// 无错误。
    /// </summary>
    None = 0,

    /// <summary>
    /// 未知错误。
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// 无效参数。
    /// </summary>
    InvalidArgument = 2,

    /// <summary>
    /// 未找到资源。
    /// </summary>
    NotFound = 3,

    /// <summary>
    /// 资源已存在。
    /// </summary>
    AlreadyExists = 4,

    /// <summary>
    /// 权限被拒绝。
    /// </summary>
    PermissionDenied = 5,

    /// <summary>
    /// 服务不可用。
    /// </summary>
    Unavailable = 6,

    /// <summary>
    /// 操作超时。
    /// </summary>
    Timeout = 7,

    /// <summary>
    /// 绑定错误。
    /// </summary>
    BindingError = 100,

    /// <summary>
    /// 未找到绑定方法。
    /// </summary>
    BindingNotFound = 101,

    /// <summary>
    /// 绑定调用错误。
    /// </summary>
    BindingCallError = 102,

    /// <summary>
    /// 传输层错误。
    /// </summary>
    TransportError = 200,

    /// <summary>
    /// 传输层未启动。
    /// </summary>
    TransportNotStarted = 201,

    /// <summary>
    /// 未找到资源文件。
    /// </summary>
    AssetNotFound = 300,

    /// <summary>
    /// 窗口错误。
    /// </summary>
    WindowError = 400,

    /// <summary>
    /// 未找到窗口。
    /// </summary>
    WindowNotFound = 401,

    /// <summary>
    /// 当前平台不受支持。
    /// </summary>
    PlatformNotSupported = 500,

    /// <summary>
    /// 服务错误。
    /// </summary>
    ServiceError = 600,

    /// <summary>
    /// 服务启动失败。
    /// </summary>
    ServiceStartupFailed = 601,

    /// <summary>
    /// 服务关闭失败。
    /// </summary>
    ServiceShutdownFailed = 602,

    /// <summary>
    /// 自动更新错误。
    /// </summary>
    UpdaterError = 700
}
