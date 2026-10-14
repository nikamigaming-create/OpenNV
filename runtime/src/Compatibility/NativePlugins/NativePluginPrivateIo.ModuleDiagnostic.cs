using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    internal NativePluginIoRoute ResolveModuleDiagnostic(ulong parent, NativePluginModuleDiagnosticCrtSource source,
        string declared, string mode, int sharing, uint attempt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = _generation ?? throw new InvalidOperationException("Module diagnostic lost its actual shared generation.");
        if (!source.StackSha256.Equals(Selection.StackSha256, StringComparison.OrdinalIgnoreCase) ||
            !Selection.OriginalRoots.Any(root => Within(root, Canonical(source.Path))) ||
            mode != source.Mode || sharing != source.Sharing || declared != source.AttemptName(attempt))
            throw new NotSupportedException("Module diagnostic opening escaped its source-declared path/mode/share/attempt.");
        var path = Virtual(declared);
        var physical = PrepareModuleDiagnosticPath(source, attempt);
        var id = checked(++_sequence);
        var route = new NativePluginIoRoute(id, 1, NativePluginIoAction.Write, path, physical, 0, null, NativePluginIoRole.Diagnostic);
        _routes.Add(id, route); _receipts.Add(new(id, generation, parent, 1, path, physical, null, NativePluginIoRole.Diagnostic, 0));
        return route;
    }
    internal string PrepareModuleDiagnosticPath(NativePluginModuleDiagnosticCrtSource source, uint attempt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation is null || !source.StackSha256.Equals(Selection.StackSha256, StringComparison.OrdinalIgnoreCase) ||
            !Selection.OriginalRoots.Any(root => Within(root, Canonical(source.Path))))
            throw new InvalidDataException("Module diagnostic destination has no current selected shared owner.");
        var declared = source.AttemptName(attempt);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.StackSha256.ToUpperInvariant() + "\0" + source.Sha256.ToUpperInvariant() + "\0" + Canonical(source.Path).ToUpperInvariant())));
        var directory = Path.Combine(ModuleRoot, NativePluginIoRole.Diagnostic.ToString(), "Modules", identity);
        var physical = Path.Combine(directory, declared);
        if (!Within(ModuleRoot, physical) || Selection.OriginalRoots.Any(root => Within(root, physical)))
            throw new InvalidDataException("Module diagnostic destination overlaps an original source.");
        NoReparse(directory); NoReparse(physical); Directory.CreateDirectory(directory); NoReparse(directory); return physical;
    }
    internal void CompleteModuleDiagnostic(ulong routeId, bool success)
    {
        if (!_routes.Remove(routeId, out var route) || route.Api != 1 || route.Action != NativePluginIoAction.Write || route.Role != NativePluginIoRole.Diagnostic)
            throw new InvalidDataException("Module diagnostic lost its exact shared pending writable route.");
        if (success) _fileRoutes.Add(routeId, route);
    }
}
