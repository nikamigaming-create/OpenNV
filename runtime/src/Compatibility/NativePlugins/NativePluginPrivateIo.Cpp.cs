using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    private NativePluginCppRuntimeSelection? _cppSelection;
    private readonly List<FileStream> _cppSourceLeases = [];
    internal NativePluginCppRuntimeSelection? CppProvider => _cppSelection;
    private void InitializeCppProvider()
    {
        var selection = NativePluginCppRuntimeImports.Read(Selection.ModulePath, Selection.ModuleSha256);
        if (selection is null) return;
        _cppSelection = selection;
        foreach (var (path, sha) in new[] { (selection.Path, selection.Sha256), (selection.UcrtPath, selection.UcrtSha256) })
        {
            NoReparse(path);
            var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _cppSourceLeases.Add(lease);
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("C++/UCRT retained provider source changed before process entry.");
        }
        if (!CrtProviders.Any(row => row.Path.Equals(selection.UcrtPath, StringComparison.OrdinalIgnoreCase) && row.Sha256.Equals(selection.UcrtSha256, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("C++ FILE creation lacks the exact shared UCRT provider lease.");
    }
    internal void RequireCppSourcesCurrent()
    {
        if (_cppSelection is not { } selection) return;
        if (_cppSourceLeases.Count != 2) throw new InvalidDataException("C++ provider lost an independent source lease.");
        for (var at = 0; at < _cppSourceLeases.Count; ++at)
        {
            var lease = _cppSourceLeases[at]; var position = lease.Position;
            try
            {
                lease.Position = 0;
                if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(at == 0 ? selection.Sha256 : selection.UcrtSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("C++/UCRT input changed during its native object lifetime.");
            }
            finally { lease.Position = position; }
        }
    }
    private void DisposeCppSources()
    {
        var failures = new List<Exception>();
        foreach (var lease in _cppSourceLeases.ToArray())
            try { lease.Dispose(); _cppSourceLeases.Remove(lease); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("C++ source leases retain independent retirement failures.", failures);
    }
    internal NativePluginIoRoute BindStandardRoute(ulong parent, uint index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (parent == 0 || _generation is null || index is not (1 or 2))
            throw new NotSupportedException("Native stdin/foreign standard channel has no genuine input authority.");
        var path = Path.Combine(ModuleRoot, NativePluginIoRole.Diagnostic.ToString(), index == 1 ? "stdout.private.log" : "stderr.private.log");
        NoReparse(path);
        var route = new NativePluginIoRoute(checked(++_sequence), 1, NativePluginIoAction.Write,
            "standard-channel:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), path, 0, null, NativePluginIoRole.Diagnostic);
        _fileRoutes.Add(route.Id, route);
        return route;
    }
}
