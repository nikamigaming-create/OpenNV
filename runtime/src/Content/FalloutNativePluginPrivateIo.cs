using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativePluginIoReadDeclaration(string VirtualPath, string PhysicalPath,
    string Sha256, string SourceOwner, bool Configuration);

// The active source resolves loose/archive and configuration winners. The
// native file adapter cannot substitute an arbitrary existing installation
// file for this selected winner or persist a decoded retail archive member.
internal static partial class FalloutNativePluginPrivateIo
{
    internal static NativePluginPrivateIo Create(RuntimeLiveContentSource source, string modulePath,
        string moduleSha256, string privateStateRoot, IReadOnlyList<NativePluginIoWriteScope> writeScopes,
        IReadOnlyList<FalloutNativePluginIoReadDeclaration> readDeclarations, IReadOnlyList<string> additionalInputRoots,
        string importDeclarationOwner, IReadOnlySet<string> declaredNonIoImports)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(readDeclarations);
        ArgumentNullException.ThrowIfNull(additionalInputRoots);
        var runtimeDirectory = NativePluginPrivateIo.Canonical(Path.GetDirectoryName(source.FalloutExecutablePath)!);
        var module = NativePluginPrivateIo.Canonical(modulePath);
        var declared = new Dictionary<string, NativePluginIoReadWinner>(StringComparer.OrdinalIgnoreCase);
        foreach (var read in readDeclarations)
        {
            var path = NativePluginPrivateIo.Canonical(Path.GetFullPath(read.VirtualPath, runtimeDirectory));
            var winner = new NativePluginIoReadWinner(NativePluginPrivateIo.Canonical(read.PhysicalPath), read.Sha256,
                read.SourceOwner, read.Configuration);
            if (!declared.TryAdd(path, winner)) throw new InvalidDataException("Native read declarations repeat a virtual winner identity.");
        }
        foreach (var scope in writeScopes.Where(scope => !scope.Directory && scope.Role == NativePluginIoRole.Diagnostic))
        {
            var path = NativePluginPrivateIo.Canonical(Path.GetFullPath(scope.VirtualPath, runtimeDirectory));
            if (!NativePluginPrivateIo.Within(runtimeDirectory, path) || string.IsNullOrWhiteSpace(scope.DeclarationOwner))
                throw new InvalidDataException("Native diagnostic input lost its selected original path declaration.");
            NativePluginPrivateIo.NoReparse(path);
            if (!File.Exists(path)) continue;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var winner = new NativePluginIoReadWinner(path, Convert.ToHexString(SHA256.HashData(input)),
                "selected-original-diagnostic:" + scope.DeclarationOwner + ":" + source.StackId, false, true);
            if (!declared.TryAdd(path, winner)) throw new InvalidDataException("Diagnostic input conflicts with another selected read role.");
        }
        var roots = source.ContentRoots.Concat(new[] { runtimeDirectory, module }).Concat(additionalInputRoots)
            .Select(NativePluginPrivateIo.Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new NativePluginPrivateIo(new(source.StackId, moduleSha256, module, runtimeDirectory, privateStateRoot,
            roots, writeScopes, Resolve, importDeclarationOwner, declaredNonIoImports)
        { ResolveWinningDirectory = DirectoryResolver(source, runtimeDirectory, declared) });

        NativePluginIoReadWinner? Resolve(string virtualPath)
        {
            if (declared.TryGetValue(virtualPath, out var read)) return read;
            if (StringComparer.OrdinalIgnoreCase.Equals(virtualPath, module))
                return new(module, moduleSha256, "selected-module:" + source.StackId, false);
            foreach (var root in source.ContentRoots)
            {
                var originalRoot = NativePluginPrivateIo.Canonical(root);
                if (!NativePluginPrivateIo.Within(originalRoot, virtualPath)) continue;
                var logical = Path.GetRelativePath(originalRoot, virtualPath);
                if (!source.TryResolve(logical, null, out var identity))
                {
                    if (source.ContentRoots.Any(layer => Directory.Exists(Path.Combine(layer, logical))))
                        throw new NotSupportedException("Native selected directory has no merged directory/iterator owner: " + virtualPath);
                    return null;
                }
                if (identity.Contains("::", StringComparison.Ordinal))
                    throw new NotSupportedException("Native file read names an archive winner without an actual virtual-file byte/handle owner: " + identity);
                using var original = new FileStream(identity, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new(identity, Convert.ToHexString(SHA256.HashData(original)),
                    "selected-resource:" + source.StackId + ":" + logical, false);
            }
            if (File.Exists(virtualPath) || Directory.Exists(virtualPath))
                throw new NotSupportedException("Native existing input has no exact selected read/configuration declaration: " + virtualPath);
            return null;
        }
    }
}
