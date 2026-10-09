namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginEnvironmentOperation : uint { CrtRead = 1, CrtWrite = 2, WindowsRead = 3 }
internal sealed record NativePluginEnvironmentReceipt(ulong Sequence, ulong Generation, ulong Parent, ulong Provider,
    NativePluginEnvironmentOperation Operation, byte[] Name, byte[]? SubmittedValue, uint Capacity,
    uint Required, long Result, int Errno, uint DosError, uint LastError, byte[]? ActualOutput);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint EnvironmentCallback = 25;
    private readonly List<NativePluginEnvironmentReceipt> _environmentReceipts = [];
    internal IReadOnlyList<NativePluginEnvironmentReceipt> NvseEnvironmentReceipts => _environmentReceipts.AsReadOnly();
    private byte[] DispatchPrivateEnvironment(ulong parent, BinaryReader reader)
    {
        var provider = reader.ReadUInt64(); var operation = (NativePluginEnvironmentOperation)reader.ReadUInt32();
        var name = ReadEnvironmentBytes(reader); var supplied = ReadEnvironmentOptionalBytes(reader);
        var capacity = reader.ReadUInt32(); var required = reader.ReadUInt32(); var result = reader.ReadInt64();
        var error = reader.ReadInt32(); var dos = reader.ReadUInt32(); var last = reader.ReadUInt32();
        var actual = ReadEnvironmentOptionalBytes(reader); Finish(reader);
        if (!Enum.IsDefined(operation) || name.Length == 0 || name.Contains((byte)0) ||
            (operation == NativePluginEnvironmentOperation.WindowsRead) != (provider == 0) ||
            provider != 0 && !_crtProviders.ContainsKey(provider) ||
            (operation == NativePluginEnvironmentOperation.CrtWrite) != (supplied is not null) ||
            operation == NativePluginEnvironmentOperation.CrtWrite && (capacity != 0 || required != 0 || actual is not null) ||
            operation == NativePluginEnvironmentOperation.WindowsRead && (result < 0 || result > uint.MaxValue || error != 0 || dos != 0) ||
            actual is not null && (actual.Length >= capacity || actual.Contains((byte)0)) ||
            operation == NativePluginEnvironmentOperation.CrtRead && actual is not null && (result != 0 || required != actual.Length + 1))
            throw new InvalidDataException("Actual process/CRT environment result lost its provider, byte encoding or output extent.");
        // Windows and UCRT maintain independent environment views. Record the
        // actual SDK buffers/errors; never replace one with the other's values.
        _environmentReceipts.Add(new(checked((ulong)_environmentReceipts.Count + 1), Generation, parent, provider,
            operation, name, supplied, capacity, required, result, error, dos, last, actual));
        return Payload(writer => writer.Write(1U));
    }
    private static byte[] ReadEnvironmentBytes(BinaryReader reader)
    {
        var length = reader.ReadUInt32();
        if (length > MaximumPayload || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Environment byte string exceeds its complete callback extent.");
        return reader.ReadBytes(checked((int)length));
    }
    private static byte[]? ReadEnvironmentOptionalBytes(BinaryReader reader) => reader.ReadUInt32() switch
    {
        0 => null,
        1 => ReadEnvironmentBytes(reader),
        _ => throw new InvalidDataException("Environment output availability is invalid."),
    };
    private void RequireNativeEnvironmentSaveOwned()
    {
        if (_environmentReceipts.Count != 0)
            throw new NotSupportedException("Original process/UCRT environment views have no complete current/cold producer.");
    }
}
