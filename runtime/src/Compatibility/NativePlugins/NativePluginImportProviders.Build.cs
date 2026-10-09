using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativePluginImportProviderBuild : IDisposable
{
    private readonly List<FileStream> _leases;
    private bool _disposed;
    internal IReadOnlyList<NativePluginImportProviderImage> Images { get; }
    private NativePluginImportProviderBuild(string companion, NativePluginImageDeclaration original, List<FileStream> leases)
    {
        _leases = leases;
        var imports = original.Imports.Where(row => row.Library == "bcrypt.dll").ToArray();
        if (original.DelayImports || imports.Any(row => row.Name is null || row.Ordinal is not null ||
            !NativePluginCryptoImports.IsOwned(row.Library, row.Name)))
            throw new NotSupportedException("Original dependency has an unowned delayed, ordinal or CNG public ABI arm.");
        if (imports.Length == 0) { Images = []; return; }
        var directory = Path.GetDirectoryName(Path.GetFullPath(companion)) ?? throw new InvalidDataException("Native provider has no actual companion directory.");
        var manifestPath = Path.Combine(directory, "build-manifest.json");
        var manifestInput = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read); _leases.Add(manifestInput);
        using var manifest = JsonDocument.Parse(manifestInput);
        var root = manifest.RootElement;
        if (!root.GetProperty("authoredOnly").GetBoolean() || root.GetProperty("machine").GetString() != "I386")
            throw new InvalidDataException("Import-provider build is not a first-party PE32/I386 source.");
        const string relative = "providers/cng/bcrypt.dll";
        var companionRow = Row(root, "opennv_plugin_domain.exe");
        var companionPath = Path.GetFullPath(Path.Combine(directory, "opennv_plugin_domain.exe"));
        if (!StringComparer.OrdinalIgnoreCase.Equals(companionPath, Path.GetFullPath(companion)))
            throw new InvalidDataException("Import-provider build does not name the actual retained companion.");
        Retain(companionPath, companionRow);
        var providerRow = Row(root, relative);
        var providerPath = Path.GetFullPath(Path.Combine(directory, relative));
        var providerSha = Retain(providerPath, providerRow);
        var source = NativePluginImageDeclarations.Read(providerPath, dll: true);
        var expected = NativePluginCryptoImports.Owned.Concat(new[] {
            "OpenNVImportProviderBind", "OpenNVImportProviderInspect", "OpenNVImportProviderUnbind" }).ToHashSet(StringComparer.Ordinal);
        if (source.Sha256 != providerSha || source.DelayImports || source.Exports.Count != expected.Count ||
            source.Exports.Any(row => !row.Executable || row.Forwarded || !expected.Contains(row.Name)))
            throw new InvalidDataException("First-party dependency image lacks its complete actual public callable/binding exports.");
        Images = [new(NativePluginImportProviderKind.Cng, "bcrypt.dll", providerPath, providerSha,
            Array.AsReadOnly(source.Exports.Select(row => new NativePluginImportProviderExport(row.Name, row.Rva)).ToArray()),
            Array.AsReadOnly(imports))];
    }
    internal static NativePluginImportProviderBuild Open(string companion, NativePluginImageDeclaration original)
    {
        // Construction failures can never have published a native provider.
        // Keep all handles local to this factory so they are closed on failure.
        var leases = new List<FileStream>();
        try { return new(companion, original, leases); }
        catch { foreach (var lease in leases) lease.Dispose(); throw; }
    }
    private static JsonElement Row(JsonElement root, string path)
    {
        var rows = root.GetProperty("files").EnumerateArray().Where(row => row.GetProperty("path").GetString() == path).ToArray();
        return rows.Length == 1 ? rows[0] : throw new InvalidDataException("First-party import-provider build member is absent or repeated: " + path);
    }
    private string Retain(string path, JsonElement row)
    {
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); _leases.Add(input);
        var sha = row.GetProperty("sha256").GetString() ?? throw new InvalidDataException("First-party provider build has no actual hash.");
        var actual = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        if (input.Length != row.GetProperty("bytes").GetInt64() || !actual.Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("First-party import-provider bytes differ from the retained build manifest.");
        return actual;
    }
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var source in Images)
            if (!NativePluginImageDeclarations.Read(source.Path, dll: true).Sha256.Equals(source.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("A published first-party dependency source changed during its original module lifetime.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var lease in _leases) lease.Dispose(); _leases.Clear(); _disposed = true;
    }
}
