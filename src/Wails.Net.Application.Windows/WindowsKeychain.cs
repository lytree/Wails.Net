using System.Runtime.InteropServices;
using System.Text;
using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows Credential Manager 钥匙串实现。
/// 对应 Wails v3 Go 版本 <c>internal/keychain/keychain_windows.go</c>，
/// 通过 <c>CredWriteW/CredReadW/CredDeleteW</c>（advapi32.dll）调用 Windows Credential Manager。
/// </summary>
/// <remarks>
/// 凭据以 <c>CRED_TYPE_GENERIC</c> 类型存储，TargetName 命名约定为 <c>{Service}:{Account}</c>，
/// 由调用方传入的 service + account 组合以保证命名空间隔离。
/// </remarks>
public sealed class WindowsKeychain : IPlatformKeychain
{
    /// <summary>
    /// 通用凭据类型（CRED_TYPE_GENERIC = 1）。
    /// </summary>
    private const uint CredTypeGeneric = 1;

    /// <summary>
    /// 凭据持久化类型：本地计算机（CRED_PERSIST_LOCAL_MACHINE = 2）。
    /// </summary>
    private const uint CredPersistLocalMachine = 2;

    /// <summary>
    /// CredWriteW 函数声明：将凭据写入 Credential Manager。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref CREDENTIAL cred, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr cred);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    /// <inheritdoc />
    public bool SetPassword(string service, string account, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);
        ArgumentNullException.ThrowIfNull(password);

        var target = BuildTargetName(service, account);
        var passwordBytes = Encoding.Unicode.GetBytes(password);

        var targetPtr = Marshal.StringToCoTaskMemUni(target);
        var blobPtr = Marshal.AllocCoTaskMem(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, blobPtr, passwordBytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = targetPtr, // 用 TargetName 复用作 UserName 占位
            };

            return CredWriteW(ref cred, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(blobPtr);
        }
    }

    /// <inheritdoc />
    public string? GetPassword(string service, string account)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        var target = BuildTargetName(service, account);
        if (!CredReadW(target, CredTypeGeneric, 0, out var credPtr))
        {
            return null;
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <inheritdoc />
    public bool DeletePassword(string service, string account)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        var target = BuildTargetName(service, account);
        // 凭据不存在时 CredDeleteW 返回 false 并 ERROR_NOT_FOUND，业务上视作成功
        return CredDeleteW(target, CredTypeGeneric, 0) || Marshal.GetLastWin32Error() == 1168; // ERROR_NOT_FOUND
    }

    /// <summary>
    /// 构造凭据目标名称，命名约定 <c>{Service}:{Account}</c>。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>组合后的目标名称。</returns>
    private static string BuildTargetName(string service, string account)
        => $"{service}:{account}";
}
