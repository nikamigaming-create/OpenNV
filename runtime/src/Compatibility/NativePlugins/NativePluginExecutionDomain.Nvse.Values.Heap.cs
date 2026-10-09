namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseHeapOperation : uint { Allocate = 1, Release = 2 }
internal sealed record NativeNvseHeapThunk(uint Address, NativeNvseHeapOperation Operation, NativePluginAbi Abi, uint Receiver = 0);
internal sealed record NativeNvseHeapDeclaration(string PluginSha256, string DeclarationOwner, IReadOnlyList<NativeNvseHeapThunk> Thunks);
internal sealed record NativeNvseHeapLifetime(ulong Generation, ulong Capability, uint Address, uint Length,
    ulong BirthCaller, ulong BirthCallback, ulong? RetirementCallback);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ValueHeapEvent = 0x230;
    private NativeNvseHeapDeclaration? _nvseHeapDeclaration;
    private readonly Dictionary<ulong, NativeNvseHeapLifetime> _nvseHeapLifetimes = [];
    internal IReadOnlyList<NativeNvseHeapLifetime> NvseHeapLifetimes => _nvseHeapLifetimes.Values.OrderBy(row => row.Capability).ToArray();

    internal void ConfigureNvseValueHeap(NativeNvsePlugin plugin, NativeNvseHeapDeclaration declaration)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(declaration);
        if (_nvseValues is null || _nvseHeapDeclaration is not null || plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue) ||
            !string.Equals(declaration.PluginSha256, plugin.Sha256, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(declaration.DeclarationOwner) || declaration.Thunks is null || declaration.Thunks.Count is 0 or > 128)
            throw new InvalidDataException("Native Element heap needs the exact selected-client callable declaration before Query/Load.");
        var thunks = declaration.Thunks.ToArray();
        if (thunks.Any(thunk => thunk is null)) throw new InvalidDataException("Native heap declaration contains a null entry.");
        for (var index = 0; index < thunks.Length; ++index)
        {
            var thunk = thunks[index];
            if (thunk.Address == 0 || thunk.Address > uint.MaxValue - 7 || !Enum.IsDefined(thunk.Operation) || !Enum.IsDefined(thunk.Abi) ||
                (thunk.Abi == NativePluginAbi.Thiscall ? thunk.Receiver == 0 : thunk.Receiver != 0) ||
                thunks.Where((_, otherIndex) => otherIndex != index).Any(other => (ulong)thunk.Address < (ulong)other.Address + 7 && (ulong)other.Address < (ulong)thunk.Address + 7))
                throw new InvalidDataException("Native heap declaration has an invalid/overlapping entry or ABI receiver.");
        }
        if (!thunks.Any(thunk => thunk.Operation == NativeNvseHeapOperation.Allocate) || !thunks.Any(thunk => thunk.Operation == NativeNvseHeapOperation.Release))
            throw new InvalidDataException("Native heap declaration must own both allocation and free.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseValueHeap, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(checked((uint)thunks.Length));
                foreach (var thunk in thunks) { writer.Write(thunk.Address); writer.Write((uint)thunk.Operation); writer.Write((uint)thunk.Abi); writer.Write(thunk.Receiver); }
            }));
            if (reader.ReadUInt32() != thunks.Length || reader.ReadUInt32() is 0 or > 128)
                throw new InvalidDataException("Native heap callable mapping has no exact declaration receipt.");
            Finish(reader); _nvseHeapDeclaration = declaration with { Thunks = Array.AsReadOnly(thunks) };
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }
    private byte[] AcceptNvseHeapEvent(Frame frame, ulong callerId, BinaryReader reader)
    {
        var declaration = _nvseHeapDeclaration ?? throw new NotSupportedException("Original Element heap caller has no exact allocation/free declaration.");
        var capability = reader.ReadUInt64(); var pointer = reader.ReadUInt32(); var length = reader.ReadUInt32();
        var operation = (NativeNvseHeapOperation)reader.ReadUInt32(); var abi = reader.ReadUInt32(); var receiver = reader.ReadUInt32(); Finish(reader);
        if (capability == 0 || pointer == 0 || length == 0 || length > 8 * 1024 * 1024 || !Enum.IsDefined(operation) ||
            abi != 0 && !declaration.Thunks.Any(thunk => (uint)thunk.Abi == abi && thunk.Operation == operation && thunk.Receiver == receiver) ||
            abi == 0 && receiver != 0)
            throw new InvalidDataException("Native heap event lacks its exact source ABI/allocation extent.");
        if (operation == NativeNvseHeapOperation.Allocate)
        {
            if (_nvseHeapLifetimes.Values.Any(row => row.Address == pointer) ||
                !_nvseHeapLifetimes.TryAdd(capability, new(Generation, capability, pointer, length, callerId, frame.Id, null)))
                throw new InvalidDataException("Native heap birth repeats a prior capability/address in this generation.");
        }
        else
        {
            if (!_nvseHeapLifetimes.TryGetValue(capability, out var lifetime) || lifetime.Generation != Generation ||
                lifetime.Address != pointer || lifetime.Length != length || lifetime.RetirementCallback is not null)
                throw new InvalidDataException("Native heap retirement targets a foreign/absent/interior/already freed allocation.");
            _nvseHeapLifetimes[capability] = lifetime with { RetirementCallback = frame.Id };
        }
        return Payload(writer => writer.Write(1U));
    }
    private void RequireNvseHeapRetired()
    {
        if (_nvseHeapLifetimes.Values.Any(row => row.RetirementCallback is null))
            throw new InvalidDataException("Original native Element buffers retain live allocations after module retirement.");
        _nvseHeapDeclaration = null;
    }
}
