using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// These leases retain the exact original files. No DLL bytes, interface
// pointers or original executable body are mapped in the managed/Godot host.
internal sealed partial class NativePluginSteamSource : IDisposable
{
    private FileStream? _engine, _provider, _application;
    private ulong? _generation;
    internal NativePluginSteamDeclaration Declaration { get; }
    internal string EnginePath { get; }
    internal string ProviderPath { get; }
    internal string RuntimeDirectory => Path.GetDirectoryName(EnginePath)!;
    internal string Stack { get; }
    internal NativePluginSteamApplication? Application { get; }
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Declaration.Identity + "\0" + Stack + "\0" +
        Application?.ManifestSha256 + "\0" + Application?.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal NativePluginSteamSource(string engine, string provider, string stack, FalloutMainUtilitySource main)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        EnginePath = Path.GetFullPath(engine); ProviderPath = Path.GetFullPath(provider); Stack = stack;
        if (!Path.GetDirectoryName(EnginePath)!.Equals(Path.GetDirectoryName(ProviderPath), StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Platform DLL has no exact selected executable-directory loader owner.");
        var game = NativePluginImageDeclarations.Read(EnginePath, false);
        if (!game.Sha256.Equals(main.Main.EngineSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Platform import source is not this campaign's actual selected engine.");
        var required = new[] { "SteamAPI_Init", "SteamAPI_Shutdown", "SteamAPI_IsSteamRunning", "SteamAPI_RunCallbacks", "SteamUser", "SteamUserStats", "SteamUtils" };
        var libraries = game.Imports.Where(row => row.Name is not null && required.Contains(row.Name, StringComparer.Ordinal))
            .Select(row => row.Library).Distinct(StringComparer.Ordinal).ToArray();
        if (libraries.Length != 1 || required.Any(name => game.Imports.Count(row => row.Library == libraries[0] && row.Name == name) != 1) ||
            !Path.GetFileName(ProviderPath).Equals(libraries[0], StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Actual engine imports do not establish one complete selected Steam provider.");
        var dll = NativePluginImageDeclarations.Read(ProviderPath, true);
        foreach (var name in required)
        {
            var rows = dll.Exports.Where(row => row.Name == name).ToArray();
            if (rows.Length != 1 || !rows[0].Executable || rows[0].Forwarded)
                throw new NotSupportedException("Selected platform callable has no genuine direct x86 export: " + name);
        }
        Declaration = NativePluginSteamDeclaration.Read(main, game.Sha256, dll.Sha256, libraries[0]);
        try
        {
            _engine = NativeNvseHostSource.Lease(EnginePath, Declaration.EngineSha256, false);
            _provider = NativeNvseHostSource.Lease(ProviderPath, Declaration.ProviderSha256, true);
            var selected = NativePluginSteamApplications.Read(RuntimeDirectory); Application = selected.Application; _application = selected.Lease;
        }
        catch { _engine?.Dispose(); _provider?.Dispose(); _application?.Dispose(); throw; }
    }
    internal static NativePluginSteamSource Open(RuntimeLiveContentSource source, FalloutMainUtilitySource main)
    {
        var directory = Path.GetDirectoryName(source.FalloutExecutablePath)!;
        var path = new FalloutContentLayers([directory]).ResolveFile("steam_api.dll") ??
            throw new FileNotFoundException("Selected original engine platform import is absent.");
        return new(source.FalloutExecutablePath, path, source.StackId, main);
    }
    internal void Claim(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_engine is null || _provider is null, this);
        if (_generation is not null || generation == 0) throw new InvalidOperationException("Platform original source is already owned by a child epoch.");
        _generation = generation;
    }
    internal void Check(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_engine is null || _provider is null, this);
        if (_generation != generation) throw new InvalidOperationException("Platform original source belongs to another child lifetime.");
    }
    internal void RequireUnchanged()
    {
        ObjectDisposedException.ThrowIf(_engine is null || _provider is null, this);
        var input = _provider!; var position = input.Position;
        try { input.Position = 0; if (!Convert.ToHexString(SHA256.HashData(input)).Equals(Declaration.ProviderSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Retained platform input changed."); }
        finally { input.Position = position; }
    }
    internal void Retire(ulong generation)
    { Check(generation); _generation = null; Dispose(); }
    public void Dispose()
    {
        if (_generation is not null) throw new InvalidOperationException("The actual native child still owns these platform source files.");
        _application?.Dispose(); _provider?.Dispose(); _engine?.Dispose(); _application = null; _provider = null; _engine = null;
    }
}
