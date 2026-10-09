using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal enum FalloutNativeModuleDisposition { SelectedPlugin, AvailableNested, AvailableArchive, LoaderNotPlugin, SourceFailure }
internal sealed record FalloutNativeModuleSource(string LogicalPath, string PhysicalPath, string Sha256,
    FalloutNativeModuleDisposition Disposition, uint? QueryHandle, string? Failure,
    IReadOnlyList<NativePluginImageImport> Imports, IReadOnlyList<string> UnownedImports, string? ExpressionDeclarationFailure);
internal sealed record FalloutNativePluginHostDependency(string PhysicalPath, string SourceOwner);

// The product's source declaration producer. Installed helpers and nested or
// BSA DLL declarations remain visible without inventing loader selection.
internal static class FalloutNativePluginStartupDeclarations
{
    internal static FalloutNativePluginCampaignSelection Build(RuntimeLiveContentSource source,
        FalloutNativePluginHostDependency? declaredHost)
    {
        var inventory = new List<FalloutNativeModuleSource>();
        var modules = new List<FalloutNativePluginModuleAdmission>();
        var logicalPaths = source.ResourcePathsUnder("NVSE/Plugins").Where(path =>
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        uint queryHandle = 0;
        foreach (var logical in logicalPaths)
        {
            if (!source.TryResolve(logical, null, out var physical)) throw new InvalidDataException("Native source winner disappeared: " + logical);
            if (physical.Contains("::", StringComparison.Ordinal))
            { inventory.Add(new(logical, physical, HashStored(source, physical), FalloutNativeModuleDisposition.AvailableArchive, null, null, [], [], null)); continue; }
            if (logical.Replace('/', '\\').Split('\\').Length != 3)
            { inventory.Add(new(logical, physical, Hash(physical), FalloutNativeModuleDisposition.AvailableNested, null, null, [], [], null)); continue; }
            // xNVSE ranks every top-level DLL before deciding whether its Query
            // export makes it a plugin. Preserve gaps created by helper DLLs.
            var handle = checked(++queryHandle);
            NativePluginImageDeclaration? image = null;
            try
            {
                image = NativePluginImageDeclarations.Read(physical, true, requireX86: false);
                var query = image.Exports.SingleOrDefault(row => row.Name == "NVSEPlugin_Query");
                if (query is null || image.Exports.Count > 50)
                {
                    inventory.Add(new(logical, physical, image.Sha256, FalloutNativeModuleDisposition.LoaderNotPlugin,
                        handle, null, image.Imports, [], null)); continue;
                }
                if (image.Machine != System.Reflection.PortableExecutable.Machine.I386 ||
                    image.Magic != System.Reflection.PortableExecutable.PEMagic.PE32 || image.Managed)
                    throw new NotSupportedException("Selected original Query image has no unmanaged x86 native execution owner.");
                var load = image.Exports.SingleOrDefault(row => row.Name == "NVSEPlugin_Load");
                if (!query.Executable || query.Forwarded || load is null || !load.Executable || load.Forwarded)
                    throw new InvalidDataException("Selected plugin lacks its genuine direct executable Query/Load exports.");
                var unowned = new List<string>(); var nonIo = new HashSet<string>(StringComparer.Ordinal);
                foreach (var import in image.Imports)
                {
                    if (import.Name is not { } name) { unowned.Add(import.Library + "!#" + import.Ordinal); continue; }
                    var platform = import.Library is "kernel32.dll" or "kernelbase.dll" || import.Library.StartsWith("api-ms-win-core-", StringComparison.Ordinal);
                    if (platform && NativePluginIoImports.Owned.Contains(name)) continue;
                    if (NativePluginCrtImports.IsFileImport(import.Library, name))
                    { if (!NativePluginCrtImports.Owned.Contains(name)) unowned.Add(import.Library + "!" + name); continue; }
                    if (NativePluginPlatformImports.Owner(import.Library, name) is not null) nonIo.Add(import.Library + "!" + name);
                    else unowned.Add(import.Library + "!" + name);
                }
                if (image.DelayImports) unowned.Add("delay-import-loader-entry/lifetime-owner-unbound");
                NativeNvseExpressionAbi? expression = null; string? expressionFailure = null;
                try
                {
                    var pdbLogical = Path.ChangeExtension(logical, ".pdb");
                    if (!source.TryResolve(pdbLogical, null, out var pdbPath) || pdbPath.Contains("::", StringComparison.Ordinal))
                        throw new NotSupportedException("Selected original client has no exact loose matched PDB type declaration.");
                    expression = NativePluginExpressionSource.Read(image, pdbPath);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or IOException or ArgumentException or OverflowException)
                { expressionFailure = error.Message; }
                var owner = "selected-PE32/public-NVSE/source-loader:" + source.StackId + ":" + image.Sha256;
                var inputs = source.ContentRoots.Concat([Path.GetDirectoryName(physical)!]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var runtimeDirectory = Path.GetDirectoryName(source.FalloutExecutablePath)!;
                var writes = Writes(source, image, runtimeDirectory, owner);
                var reads = Configurations(source, runtimeDirectory);
                modules.Add(new(logical, physical, image.Sha256, writes, reads, inputs, owner,
                    nonIo, expression, null) { QueryHandle = handle, PreEntryFailure = unowned.Count == 0 ? null :
                        "Original imported operations have no actual callable source owner: " + string.Join(", ", unowned),
                        ExpressionDeclarationFailure = expressionFailure });
                inventory.Add(new(logical, physical, image.Sha256, FalloutNativeModuleDisposition.SelectedPlugin,
                    handle, null, image.Imports, unowned.AsReadOnly(), expressionFailure));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or IOException or ArgumentException or OverflowException)
            { inventory.Add(new(logical, physical, image?.Sha256 ?? Hash(physical), FalloutNativeModuleDisposition.SourceFailure, handle, error.ToString(), image?.Imports ?? [], [], null)); }
        }
        var runtimeHash = Hash(source.FalloutExecutablePath);
        if (modules.Count == 0 && !inventory.Any(row => row.Disposition == FalloutNativeModuleDisposition.SourceFailure))
            return new(source, runtimeHash, "", "", null, "no-selected-native-images", "source-top-level-selection", []) { Inventory = inventory.AsReadOnly() };
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new NotSupportedException("The selected native source graph has no standalone FOSE host ABI owner.");
        var host = declaredHost ?? Host(source);
        var nvseHash = Hash(host.PhysicalPath); var edition = Edition(source.FalloutExecutablePath);
        // Validate the exact existing host owner now, before a child exists.
        using var validated = NativeNvseHostSource.Open(source.FalloutExecutablePath, runtimeHash, host.PhysicalPath,
            nvseHash, source.StackId, edition.NoGore, edition.Owner);
        return new(source, runtimeHash, host.PhysicalPath, nvseHash, edition.NoGore, edition.Owner,
            "xNVSE6.4.9/top-level-case-insensitive-sort/query-all-then-load:" + host.SourceOwner,
            modules.AsReadOnly()) { Inventory = inventory.AsReadOnly() };
    }

    private static FalloutNativePluginHostDependency Host(RuntimeLiveContentSource source)
    {
        if (source.TryResolve("nvse_1_4.dll", null, out var selected) && !selected.Contains("::", StringComparison.Ordinal))
            return new(selected, "selected-content-root-xNVSE:" + source.StackId);
        var root = Path.GetDirectoryName(source.FalloutExecutablePath)!;
        var path = new FalloutContentLayers([root]).ResolveFile("nvse_1_4.dll") ??
            throw new FileNotFoundException("Selected native plugins have no exact selected xNVSE host dependency.");
        return new(path, "selected-runtime-root-xNVSE:" + source.StackId);
    }
    private static (bool NoGore, string Owner) Edition(string path)
    {
        var image = NativePluginImageDeclarations.Read(path, false);
        var declarations = image.CodeViews.Select(view => Path.GetFileName(view.Path.Replace('\\', '/')))
            .Where(name => name.Equals("FalloutNV.pdb", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("FalloutNVng.pdb", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (declarations.Length != 1) throw new NotSupportedException("Owned runtime CodeView does not establish its New Vegas standard/NoGore edition.");
        return (declarations[0].Equals("FalloutNVng.pdb", StringComparison.OrdinalIgnoreCase),
            "original-runtime-RSDS-edition:" + image.Sha256 + ":" + declarations[0]);
    }
    private static IReadOnlyList<NativePluginIoWriteScope> Writes(RuntimeLiveContentSource source,
        NativePluginImageDeclaration image, string runtime, string owner)
    {
        var result = new Dictionary<string, NativePluginIoWriteScope>(StringComparer.OrdinalIgnoreCase);
        var plugins = Path.Combine(runtime, "Data", "NVSE", "Plugins");
        result.Add(plugins, new(plugins, true, NativePluginIoRole.Configuration, "selected-module-private-plugin-config-namespace:" + owner));
        foreach (var literal in image.PathLiterals)
        {
            if (literal.IndexOfAny(['%', '*', '?', '\0', '"', '\r', '\n']) >= 0 || literal.Any(char.IsControl) ||
                literal.Split(['/', '\\']).Any(segment => segment is ".." or ".") || Path.IsPathFullyQualified(literal)) continue;
            var extension = Path.GetExtension(literal).ToLowerInvariant();
            if (extension is not (".ini" or ".cfg" or ".log")) continue;
            var path = Path.GetFullPath(literal, runtime);
            if (!NativePluginPrivateIo.Within(runtime, path) || NativePluginPrivateIo.Within(plugins, path)) continue;
            result.TryAdd(path, new(path, false, extension == ".log" ? NativePluginIoRole.Diagnostic : NativePluginIoRole.Configuration,
                "selected-original-path-literal/private-route:" + image.Sha256));
        }
        return result.Values.ToArray();
    }
    private static IReadOnlyList<FalloutNativePluginIoReadDeclaration> Configurations(RuntimeLiveContentSource source, string runtime)
    {
        var result = new Dictionary<string, FalloutNativePluginIoReadDeclaration>(StringComparer.OrdinalIgnoreCase);
        foreach (var logical in source.ResourcePathsUnder("NVSE").Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ini" or ".cfg"))
        {
            if (!source.TryResolve(logical, null, out var physical) || physical.Contains("::", StringComparison.Ordinal)) continue;
            var hash = Hash(physical);
            // Every selected layer spelling refers to the same winning config.
            // Never read a loser because a caller names that layer directory.
            foreach (var root in source.ContentRoots)
            {
                var path = Path.Combine(root, logical);
                result.Add(path, new(path, physical, hash, "selected-native-configuration-winner:" + source.StackId, true));
            }
        }
        foreach (var path in Directory.EnumerateFiles(runtime).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ini" or ".cfg"))
            result.TryAdd(path, new(path, path, Hash(path), "selected-runtime-original-configuration:" + source.StackId, true));
        return result.Values.ToArray();
    }
    private static string HashStored(RuntimeLiveContentSource source, string identity)
    {
        // Archive DLLs are inventory only; hashing the stored original extent
        // does not turn them into executable files or decoded native images.
        var extent = source.ResourceExtent(identity);
        using var input = new FileStream(extent.File, FileMode.Open, FileAccess.Read, FileShare.Read); input.Position = extent.Offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; var remaining = extent.StoredBytes;
        while (remaining > 0) { var count = input.Read(buffer, 0, Math.Min(buffer.Length, remaining)); if (count == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, count); remaining -= count; }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string Hash(string path)
    { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(input)); }
}
