namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ParallelHardwareCallback = 31;
    private NativePluginCppBinding? _parallelHardwareBinding;
    private bool _parallelHardwareRetired;
    internal NativePluginCppBinding? ParallelHardwareBinding => _parallelHardwareBinding;
    private static void WriteParallelHardwareProvider(BinaryWriter writer, NativePluginPrivateIo io)
    {
        var selection = io.ParallelHardwareProvider;
        writer.Write(selection is null ? 0U : 1U);
        if (selection is null) return;
        WriteText(writer, selection.Path); WriteText(writer, selection.Sha256); WriteText(writer, selection.SourceOwner);
        writer.Write(selection.Rva);
    }
    private byte[] DispatchParallelHardware(ulong parent, NativePluginPrivateIo io, BinaryReader reader)
    {
        var stage = reader.ReadUInt32(); var module = reader.ReadUInt32(); var actual = reader.ReadUInt32();
        var rva = reader.ReadUInt32(); var path = ReadText(reader); var sha = ReadText(reader); Finish(reader);
        var selection = io.ParallelHardwareProvider ?? throw new NotSupportedException("Parallel hardware query has no retained provider declaration.");
        io.RequireParallelHardwareSourceCurrent();
        if (module == 0 || actual == 0 || (ulong)module + rva != actual || selection.Rva != rva ||
            !path.Equals(selection.Path, StringComparison.OrdinalIgnoreCase) || !sha.Equals(selection.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Parallel hardware receipt differs from its actual source provider/export.");
        if (stage == 1 && _parallelHardwareBinding is null && !_parallelHardwareRetired)
            _parallelHardwareBinding = new(Generation, parent, module, path, sha,
                NativePluginParallelHardwareImports.HardwareThreads, actual, rva, true);
        else if (stage == 2 && _parallelHardwareBinding is { } prior && !_parallelHardwareRetired &&
            prior.Module == module && prior.Address == actual)
            _parallelHardwareRetired = true;
        else throw new InvalidDataException("Parallel hardware mapping publication/retirement repeated or omitted its actual prefix.");
        return Payload(writer => writer.Write(1U));
    }
    private void RequireParallelHardwareRetired()
    {
        if (_parallelHardwareBinding is not null && !_parallelHardwareRetired)
            throw new InvalidDataException("Parallel hardware provider still owns its native loader reference.");
        _privateIo?.RequireParallelHardwareSourceCurrent();
    }
}
