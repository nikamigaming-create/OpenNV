namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginCrtPureBinding(ulong Generation, ulong Parent, uint Thread, ulong Provider,
    uint Module, string Library, string Name, uint Address, uint Rva, string Path, string Sha256, string SourceOwner);

internal sealed partial class NativePluginExecutionDomain
{
    private readonly List<NativePluginCrtPureBinding> _crtPureBindings = [];
    internal IReadOnlyList<NativePluginCrtPureBinding> CrtPureBindings => _crtPureBindings.AsReadOnly();

    private void ReadCrtPureBindings(BinaryReader reader, NativePluginPrivateIo io, ulong publicationCall)
    {
        var expected = io.CrtProviders.SelectMany(provider => provider.Imports.SelectMany(pair => pair.Value
            .Where(name => NativePluginCrtPureImports.Owns(pair.Key, name)).Select(name => (provider, pair.Key, name)))).ToArray();
        var count = reader.ReadUInt32();
        if (_crtPureBindings.Count != 0 || count != expected.Length)
            throw new InvalidDataException("Pure CRT publication omitted/repeated selected original import bindings.");
        var current = new HashSet<(ulong Provider, string Library, string Name)>();
        for (var at = 0U; at < count; ++at)
        {
            var provider = reader.ReadUInt64(); var parent = reader.ReadUInt64();
            var module = reader.ReadUInt32(); var thread = reader.ReadUInt32();
            var library = ReadText(reader); var name = ReadText(reader); var address = reader.ReadUInt32(); var rva = reader.ReadUInt32();
            if (parent == 0 || parent != publicationCall || !_crtProviders.TryGetValue(provider, out var owner) || owner.Module != module || thread != NativeThread ||
                !current.Add((provider, library, name)))
                throw new InvalidDataException("Pure CRT import has a foreign/repeated actual provider, thread or library/name.");
            io.RequireCrtPureBinding(owner.Selection, library, name, module, address, rva);
            _crtPureBindings.Add(new(Generation, parent, thread, provider, module, library, name, address, rva,
                owner.Selection.Path, owner.Selection.Sha256, owner.Selection.SourceOwner));
        }
    }
}
