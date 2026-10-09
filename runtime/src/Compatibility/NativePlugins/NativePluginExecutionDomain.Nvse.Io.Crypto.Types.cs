using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginCryptoOperation : uint
{
    OpenAlgorithm = 1, GetProperty = 2, CreateHash = 3, HashData = 4,
    FinishHash = 5, DestroyHash = 6, CloseAlgorithm = 7
}
internal sealed record NativePluginCryptoProviderSource(string Path, string Sha256, string SourceOwner);
internal sealed record NativePluginCryptoReceipt(ulong Sequence, ulong Generation, ulong Parent,
    NativePluginCryptoOperation Operation, ulong Lifetime, ulong Algorithm, uint Handle, uint Flags,
    uint Requested, uint ResultBytes, bool ResultBytesAvailable, int Status, string Name, string Implementation, bool ImplementationPresent,
    uint ObjectAddress, uint ObjectBytes, bool Retired, string SourceOwner);

// Public SDK signatures and the actual Windows x86 export image define this
// owner. There is no substitute hash implementation or plugin-name admission.
internal static class NativePluginCryptoImports
{
    internal static readonly IReadOnlySet<string> Owned = new HashSet<string>(StringComparer.Ordinal)
    {
        "BCryptOpenAlgorithmProvider", "BCryptGetProperty", "BCryptCreateHash", "BCryptHashData",
        "BCryptFinishHash", "BCryptDestroyHash", "BCryptCloseAlgorithmProvider"
    };
    internal static bool IsOwned(string library, string name) =>
        library.Equals("bcrypt.dll", StringComparison.OrdinalIgnoreCase) && Owned.Contains(name);
    internal static bool IsCryptoDeclaration(string declaration)
    {
        var separator = declaration.LastIndexOf('!');
        return separator > 0 && declaration[..separator].Equals("bcrypt.dll", StringComparison.OrdinalIgnoreCase);
    }
    internal static NativePluginCryptoProviderSource ReadWindowsSource()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The native CNG provider requires Windows.");
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "bcrypt.dll");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
            throw new InvalidDataException("The actual Windows CNG export provider is not x86.");
        input.Position = 0; var sha = Convert.ToHexString(SHA256.HashData(input));
        return new(System.IO.Path.GetFullPath(path), sha, "actual-Windows-x86-CNG-export-provider:" + sha);
    }
}

internal sealed partial class NativePluginPrivateIo
{
    private FileStream? _cryptoProviderLease;
    private NativePluginCryptoProviderSource? _cryptoProviderSource;
    internal NativePluginCryptoProviderSource CryptoProvider => _cryptoProviderSource ??
        throw new InvalidOperationException("The actual native CNG source lease is absent.");
    private void InitializeCryptoProvider()
    {
        var source = NativePluginCryptoImports.ReadWindowsSource(); NoReparse(source.Path);
        var lease = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected Windows CNG provider changed before native entry.");
            _cryptoProviderLease = lease; _cryptoProviderSource = source;
        }
        catch { lease.Dispose(); throw; }
    }
    internal void RequireCryptoSourceCurrent(string path, string sha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); var source = CryptoProvider;
        if (!StringComparer.OrdinalIgnoreCase.Equals(Canonical(path), Canonical(source.Path)) ||
            !StringComparer.OrdinalIgnoreCase.Equals(sha, source.Sha256))
            throw new InvalidDataException("Native CNG provider is not this retained exact Windows source.");
        var input = _cryptoProviderLease ?? throw new InvalidDataException("CNG source lost its retained original lease.");
        input.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The actual Windows CNG source changed during its native lifetime.");
    }
    private void DisposeCryptoSource()
    {
        if (_cryptoProviderLease is null) return;
        _cryptoProviderLease.Dispose(); _cryptoProviderLease = null; _cryptoProviderSource = null;
    }
}
