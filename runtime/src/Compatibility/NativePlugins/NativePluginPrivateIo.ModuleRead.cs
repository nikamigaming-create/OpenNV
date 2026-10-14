using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    internal NativePluginIoRoute ResolveModuleRead(ulong parent, NativePluginModuleReadIoSource source, uint api, string declared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!source.StackSha256.Equals(Selection.StackSha256, StringComparison.OrdinalIgnoreCase) ||
            !Selection.OriginalRoots.Any(root => Within(root, Canonical(source.Path))) || api is not (1 or 8))
            throw new InvalidDataException("Module read route has no actual shared selection/source/API owner.");
        if (api == 8 && !Path.IsPathFullyQualified(declared))
            throw new NotSupportedException("Relative Win32 profile filenames require their independent Windows-directory source owner.");
        var generation = _generation ?? throw new InvalidOperationException("Module read I/O has no actual process generation.");
        var path = Virtual(declared);
        if (api == 8) RequireUnmappedProfile(path);
        var winner = StringComparer.OrdinalIgnoreCase.Equals(path, Canonical(source.Path))
            ? new NativePluginIoReadWinner(source.Path, source.Sha256, "selected-module:" + Selection.StackSha256, false)
            : Selection.ResolveWinningRead(path);
        string physical;
        uint error = 0;
        if (winner is not null)
        {
            if (api == 8 && !winner.Configuration)
                throw new NotSupportedException("Module profile winner has no actual selected configuration declaration.");
            physical = LeaseWinner(path, winner);
        }
        else
        {
            if (File.Exists(path) || Directory.Exists(path))
                throw new NotSupportedException("Module read cannot substitute an existing unselected original input.");
            // Execute the real SDK against a retained absent private name. No
            // primary writable overlay, deletion or scope supplies peer reads.
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                source.Sha256 + "\0" + source.Path.ToUpperInvariant() + "\0" + path.ToUpperInvariant())));
            var root = Path.Combine(ModuleRoot, "AbsentModuleRead");
            NoReparse(root); Directory.CreateDirectory(root);
            var directory = Path.Combine(root, identity); NoReparse(directory);
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(directory);
            else if (Directory.Exists(directory))
                throw new InvalidDataException("Absent module parent conflicts with a retained existing private directory.");
            physical = Path.Combine(directory, Path.GetFileName(path)); NoReparse(physical);
            if (File.Exists(physical) || Directory.Exists(physical))
                throw new InvalidDataException("Absent module read collides with real retained content.");
            error = Directory.Exists(directory) ? 2U : 3U;
        }
        if (api == 8) RequireUnmappedProfile(physical);
        var id = checked(++_sequence);
        var route = new NativePluginIoRoute(id, api, api == 1 ? NativePluginIoAction.Read : NativePluginIoAction.ProfileRead,
            path, physical, error, winner?.Sha256, null);
        _routes.Add(id, route);
        _receipts.Add(new(id, generation, parent, api, path, physical, winner?.Sha256, null, error));
        return route;
    }
}
