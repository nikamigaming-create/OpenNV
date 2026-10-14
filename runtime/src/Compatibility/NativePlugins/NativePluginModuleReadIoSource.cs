namespace OpenNV.Runtime.Compatibility.NativePlugins;

// This capability is derived before Windows enters the image. An import list
// alone cannot prove that DllMain or a TLS callback did not already perform I/O.
internal sealed class NativePluginModuleReadIoSource
{
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string StackSha256 { get; }
    internal string Construction { get; }
    internal NativePluginModuleConstructionSource ConstructorSource { get; }
    internal uint BoundImports { get; }
    private readonly HashSet<string> _imports;
    private readonly NativePluginImageImport[] _imageImports;
    private NativePluginModuleReadIoSource(NativePluginImageDeclaration image, string stack, NativePluginModuleConstructionSource construction)
    {
        Path = image.Path; Sha256 = image.Sha256; StackSha256 = stack; Construction = construction.Family; ConstructorSource = construction;
        _imageImports = image.Imports.ToArray();
        _imports = image.Imports.Where(row => row.Name is not null && OwnsExport(row.Library, row.Name))
            .Select(row => row.Library.ToLowerInvariant() + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
        BoundImports = checked((uint)image.Imports.Count(row => row.Name is not null && OwnsExport(row.Library, row.Name)));
    }
    internal static bool OwnsExport(string library, string name) =>
        (library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal)) &&
        name is "CreateFileW" or "CloseHandle" or "GetPrivateProfileStringA" or "GetModuleFileNameA";

    internal static NativePluginModuleReadIoSource? Read(NativePluginImageDeclaration image, string stack)
    {
        if (!image.Imports.Any(row => row.Name is not null && OwnsExport(row.Library, row.Name))) return null;
        return new(image, stack.ToUpperInvariant(), NativePluginModuleConstructionSource.Read(image, stack));
    }
    internal bool Owns(string library, string name) => _imports.Contains(library.ToLowerInvariant() + "!" + name);
    internal void Check(NativePluginImageDeclaration image, string? stack = null)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetFullPath(image.Path), System.IO.Path.GetFullPath(Path)) ||
            !Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase) ||
            stack is not null && !StackSha256.Equals(stack, StringComparison.OrdinalIgnoreCase) ||
            !_imageImports.SequenceEqual(image.Imports))
            throw new InvalidDataException("Module read I/O source capability belongs to another original image/stack/import graph.");
    }
}
