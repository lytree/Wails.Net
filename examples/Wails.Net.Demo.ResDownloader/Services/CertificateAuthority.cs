using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 本地根证书颁发机构（CA）：生成/持久化根证书，为被劫持的域名动态签发服务器证书。
/// <para>
/// MITM 代理必须让浏览器信任本机签发的证书，因此需要用户把根证书安装到
/// “当前用户 → 受信任的根证书颁发机构”。私钥只保存在本机应用数据目录，
/// 不会上传到任何地方。
/// </para>
/// </summary>
public sealed class CertificateAuthority
{
    private const string RootSubject = "CN=Wails.Net Res Downloader Root CA, O=Wails.Net, C=CN";
    private const string PfxPassword = "wails-net-res-downloader";

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, X509Certificate2> _leafCache = new(StringComparer.OrdinalIgnoreCase);
    private X509Certificate2? _root;
    private RSA? _leafKey;

    /// <summary>
    /// 创建 CA 实例，必要时生成根证书。
    /// </summary>
    public CertificateAuthority()
    {
        RootPfxPath = Path.Combine(AppPaths.DataDirectory, "root-ca.pfx");
        RootCerPath = Path.Combine(AppPaths.DataDirectory, "root-ca.cer");
        EnsureCreated();
    }

    /// <summary>根证书（.cer，可双击手动安装）路径。</summary>
    public string RootCerPath { get; }

    /// <summary>根证书私钥（.pfx）路径。</summary>
    public string RootPfxPath { get; }

    /// <summary>根证书指纹（SHA-1，用于在证书存储中比对）。</summary>
    public string Thumbprint => _root?.Thumbprint ?? string.Empty;

    /// <summary>
    /// 获取根证书主体名称，用于界面展示。
    /// </summary>
    /// <returns>主体可分辨名称。</returns>
    public string GetRootSubject() => _root?.Subject ?? RootSubject;

    /// <summary>
    /// 确保根证书已生成并导出 .cer 文件。
    /// </summary>
    public void EnsureCreated()
    {
        lock (_gate)
        {
            if (_root is not null)
            {
                return;
            }

            _root = LoadOrCreateRoot();
            _leafKey ??= RSA.Create(2048);
            TryExportCer(_root);
        }
    }

    /// <summary>
    /// 导出根证书为 .cer（供用户手动安装或分发给其他设备）。
    /// </summary>
    /// <returns>导出文件路径。</returns>
    public string ExportRootCertificate()
    {
        lock (_gate)
        {
            EnsureCreated();
            TryExportCer(_root!);
            return RootCerPath;
        }
    }

    /// <summary>
    /// 获取根证书的 DER 字节（供本地 API 直接下载 .cer）。
    /// </summary>
    /// <returns>证书字节。</returns>
    public byte[] GetRootCertificateBytes()
    {
        lock (_gate)
        {
            EnsureCreated();
            return _root!.Export(X509ContentType.Cert);
        }
    }

    /// <summary>
    /// 判断根证书是否已安装到“当前用户 → 受信任的根证书颁发机构”。
    /// </summary>
    /// <returns>已安装返回 <c>true</c>。</returns>
    public bool IsInstalled()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false).Count > 0;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// 将根证书安装到“当前用户 → 受信任的根证书颁发机构”。
    /// <para>Windows 会弹出系统安全警告对话框，需用户点击“是”才会生效。</para>
    /// </summary>
    /// <returns>安装（或已存在）返回 <c>true</c>；不受支持或用户拒绝返回 <c>false</c>。</returns>
    public bool Install()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            // Linux 需要把 .cer 放到 /usr/local/share/ca-certificates 并执行 update-ca-certificates
            return false;
        }

        try
        {
            lock (_gate)
            {
                EnsureCreated();
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                if (store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false).Count == 0)
                {
                    store.Add(_root!);
                }
            }

            return IsInstalled();
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取（或签发并缓存）指定主机名的服务器证书。
    /// </summary>
    /// <param name="host">目标主机名或 IP。</param>
    /// <returns>带私钥的叶证书。</returns>
    public X509Certificate2 GetServerCertificate(string host)
    {
        var normalized = NormalizeHost(host);
        return _leafCache.GetOrAdd(normalized, CreateLeafCertificate);
    }

    private static string NormalizeHost(string host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "localhost";
        }

        if (value[0] == '[')
        {
            var end = value.IndexOf(']', StringComparison.Ordinal);
            return end > 1 ? value[1..end] : value.Trim('[', ']');
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && value.IndexOf(':', colon + 1) < 0 ? value[..colon] : value;
    }

    private X509Certificate2 CreateLeafCertificate(string host)
    {
        lock (_gate)
        {
            EnsureCreated();
            var issuer = _root!;
            var key = _leafKey!;

            var request = new CertificateRequest(
                $"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

            var san = new SubjectAlternativeNameBuilder();
            if (IPAddress.TryParse(host, out var ip))
            {
                san.AddIpAddress(ip);
            }
            else
            {
                san.AddDnsName(host);
            }

            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var serial = RandomNumberGenerator.GetBytes(12);
            serial[0] &= 0x7F;
            if (serial[0] == 0)
            {
                serial[0] = 1;
            }

            var now = DateTimeOffset.UtcNow;
            using var leaf = request.Create(issuer, now.AddDays(-1), now.AddYears(1), serial);
            using var withKey = leaf.CopyWithPrivateKey(key);

            // Windows 上 SChannel 不接受临时私钥，必须经 PFX 往返一次落成持久密钥
            var pfx = withKey.Export(X509ContentType.Pfx, PfxPassword);
            return X509CertificateLoader.LoadPkcs12(pfx, PfxPassword, X509KeyStorageFlags.Exportable);
        }
    }

    private X509Certificate2 LoadOrCreateRoot()
    {
        if (File.Exists(RootPfxPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(RootPfxPath);
                var loaded = X509CertificateLoader.LoadPkcs12(bytes, PfxPassword, X509KeyStorageFlags.Exportable);
                if (loaded.HasPrivateKey && loaded.NotAfter > DateTime.Now.AddDays(7))
                {
                    return loaded;
                }

                loaded.Dispose();
            }
            catch (CryptographicException)
            {
                // 证书文件损坏或密码不匹配，重新生成
            }
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            RootSubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        using var selfSigned = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        var pfx = selfSigned.Export(X509ContentType.Pfx, PfxPassword);
        File.WriteAllBytes(RootPfxPath, pfx);

        return X509CertificateLoader.LoadPkcs12(pfx, PfxPassword, X509KeyStorageFlags.Exportable);
    }

    private void TryExportCer(X509Certificate2 root)
    {
        try
        {
            File.WriteAllBytes(RootCerPath, root.Export(X509ContentType.Cert));
        }
        catch (IOException)
        {
            // 导出失败不影响代理启动，用户仍可在界面中重新导出
        }
    }
}
