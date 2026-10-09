namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginCppBinding(ulong Generation, ulong Parent, uint Module, string Path, string Sha256,
    string Name, uint Address, uint Rva, bool Executable);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CppRuntimeCallback = 28;
    private readonly List<NativePluginCppBinding> _cppRuntimeBindings = [];
    private uint _cppRuntimeModule;
    private bool _cppRuntimePublished, _cppRuntimeRetired;
    private bool _crtRuntimePublicationEntered;
    internal IReadOnlyList<NativePluginCppBinding> CppRuntimeBindings => _cppRuntimeBindings.AsReadOnly();
    private static void WriteCppProvider(BinaryWriter writer, NativePluginPrivateIo io)
    {
        var selection = io.CppProvider; writer.Write(selection is null ? 0U : 1U);
        if (selection is null) return;
        WriteText(writer, selection.Path); WriteText(writer, selection.Sha256); WriteText(writer, selection.SourceOwner);
        writer.Write(checked((uint)selection.Exports.Count));
        foreach (var item in selection.Exports)
        { WriteText(writer, item.Name); writer.Write(item.Rva); writer.Write(item.Executable ? 1U : 0U); }
    }
    private void PublishNativeCrtRuntime(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin);
        var owner = _privateIo ?? throw new InvalidDataException("Mapped original module lost its source-private I/O owner.");
        if (owner.CppProvider is null && owner.CrtProviders.Count == 0) return;
        if (_crtRuntimePublicationEntered) throw new InvalidOperationException("Entered native CRT runtime publication cannot replay.");
        _crtRuntimePublicationEntered = true;
        ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.CrtRuntime, Payload(writer => writer.Write(plugin.Module)));
            var standardCount = reader.ReadUInt32(); var cppCount = reader.ReadUInt32(); Finish(reader);
            if (standardCount != _crtStandardStreams.Count || cppCount != _cppRuntimeBindings.Count ||
                (owner.CppProvider is not null) != _cppRuntimePublished)
                throw new InvalidDataException("Actual CRT runtime publication lost an independently retained provider/standard stream.");
        }
        catch (Exception error) { plugin.Phase = NativeNvsePhase.Faulted; throw Fatal(error); }
        finally { --_callDepth; }
    }
    private byte[] DispatchPrivateCppRuntime(ulong parent, NativePluginPrivateIo io, BinaryReader reader)
    {
        var stage = reader.ReadUInt32(); var module = reader.ReadUInt32(); var path = ReadText(reader); var sha = ReadText(reader);
        var count = reader.ReadUInt32();
        var selection = io.CppProvider ?? throw new NotSupportedException("C++ runtime has no selected original-source provider.");
        io.RequireCppSourcesCurrent();
        if (module == 0 || !path.Equals(selection.Path, StringComparison.OrdinalIgnoreCase) || !sha.Equals(selection.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("C++ native mapping differs from its exact source lease.");
        if (stage == 1)
        {
            if (_cppRuntimePublished || count != selection.Exports.Count || _crtStandardStreams.Count != 2)
                throw new InvalidDataException("C++ provider repeats or precedes genuine standard FILE publication.");
            var declarations = selection.Exports.ToDictionary(row => row.Name, StringComparer.Ordinal);
            var rows = new List<NativePluginCppBinding>();
            for (var at = 0U; at < count; ++at)
            {
                var name = ReadText(reader); var address = reader.ReadUInt32(); var rva = reader.ReadUInt32(); var code = reader.ReadUInt32();
                if (!declarations.Remove(name, out var declaration) || code > 1 || declaration.Rva != rva || declaration.Executable != (code == 1) ||
                    (ulong)module + rva != address)
                    throw new InvalidDataException("C++ export publication changed its source symbol/code/global identity.");
                rows.Add(new(Generation, parent, module, path, sha, name, address, rva, code == 1));
            }
            Finish(reader);
            _cppRuntimeBindings.AddRange(rows); _cppRuntimeModule = module; _cppRuntimePublished = true;
        }
        else if (stage == 2)
        {
            Finish(reader);
            if (count != 0 || !_cppRuntimePublished || _cppRuntimeRetired || module != _cppRuntimeModule)
                throw new InvalidDataException("C++ actual loader reference retirement has a missing/repeated owner.");
            _cppRuntimeRetired = true;
        }
        else throw new InvalidDataException("C++ runtime receipt has an unknown producer stage.");
        return Payload(writer => writer.Write(1U));
    }
    private void RequireCppRuntimeRetired()
    {
        if (_cppRuntimePublished && !_cppRuntimeRetired) throw new InvalidDataException("Original C++ provider/global-object lifetime remains entered.");
        _privateIo?.RequireCppSourcesCurrent();
    }
    private void ClearCppRuntimeAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("C++ source/provider retirement requires exact child closure.");
        _cppRuntimeModule = 0; _cppRuntimePublished = false; _cppRuntimeRetired = false;
    }
}
