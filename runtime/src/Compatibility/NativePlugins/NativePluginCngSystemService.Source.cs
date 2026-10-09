using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginCngSystemSources : IDisposable
{
    private sealed record Lease(string Path, string Sha256, FileStream Input);
    private readonly List<Lease> _leases = [];
    private bool _disposed;
    internal NativePluginCryptoProviderSource Provider { get; }
    internal NativePluginCryptoProviderSource Primitives { get; }
    internal NativePluginCngServiceImage Image { get; }
    internal NativePluginCngSystemSources(NativePluginCngServiceImage image, NativePluginCryptoProviderSource provider)
    {
        try
        {
            Image = image with { Path = Path.GetFullPath(image.Path) };
            Retain(Image.Path, Image.Sha256, false);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            Provider = provider;
            // The declared frontend must remain the exact source that private I/O
            // already selected; the service never substitutes another version.
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(provider.Path), Path.Combine(windows, "bcrypt.dll")))
                throw new InvalidDataException("CNG service frontend is not the selected Windows x86 source.");
            Retain(provider.Path, provider.Sha256, true);
            var primitives = Path.Combine(windows, "bcryptPrimitives.dll");
            using (var input = new FileStream(primitives, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var sha = Convert.ToHexString(SHA256.HashData(input));
                Primitives = new(primitives, sha, "actual-Windows-x86-CNG-primitives:" + sha);
            }
            Retain(Primitives.Path, Primitives.Sha256, true); RequireCurrent();
        }
        catch { Dispose(); throw; }
    }
    private void Retain(string path, string sha, bool dll)
    {
        if (!OperatingSystem.IsWindows() || sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("CNG service requires an exact Windows source/build identity.");
        path = Path.GetFullPath(path);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CNG service image identity traverses an unowned reparse point.");
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32 ||
                pe.PEHeaders.CorHeader is not null || ((pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0) != dll)
                throw new InvalidDataException("CNG service source is not its declared unmanaged x86 executable/DLL kind.");
            input.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CNG source/build bytes changed before retention.");
            _leases.Add(new(path, sha, input));
        }
        catch { input.Dispose(); throw; }
    }
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var lease in _leases)
        {
            lease.Input.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(lease.Input)).Equals(lease.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Retained CNG service/provider bytes changed during the current lifetime.");
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var lease in _leases) lease.Input.Dispose();
        _leases.Clear(); _disposed = true;
    }
}
