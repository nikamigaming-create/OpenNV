namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativeNvseRuntimeDataAuthority? _nvseData;
    private IDisposable? _nvseDataLease;
    private uint _nvseDataVersion, _nvseInputAddress;
    private ulong _nvseInputSequence;
    private NativeNvseInputKey[]? _nvseInputBaseline;
    private NativeNvseInputSnapshot? _nvseInputSnapshot;
    private readonly Dictionary<uint, NativeNvseInventoryPublication> _nvseInventoryReferences = [];
    private readonly List<NativeNvseInventoryPublication> _nvseInventoryPending = [];
    private readonly List<NativeNvseDataCallback> _nvseDataCallbacks = [];
    internal IReadOnlyList<NativeNvseDataCallback> NvseDataCallbacks => _nvseDataCallbacks.AsReadOnly();
    internal object NvseDataState => new
    {
        InterfaceVersion = _nvseDataVersion,
        InputAddress = _nvseInputAddress,
        Input = _nvseInputSnapshot,
        Inventory = _nvseInventoryReferences.Select(value => new
        { value.Value.FormId, value.Value.Reference, value.Value.InventoryReference, value.Value.Entry, value.Value.ExtraData }).ToArray(),
        PendingInventory = _nvseInventoryPending.Count,
        Lambdas = _nvseLambdaCaptures.Values.Select(value => new { value.Script.Address, value.Context.Id, value.Saves }).ToArray()
    };

    internal void AttachNvseData(NativeNvsePlugin plugin, NativeNvseRuntimeDataAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(authority);
        if (_nvseData is not null || plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue))
            throw new InvalidOperationException("Data authority attaches once before the original Query/Load entry.");
        authority.RequireCurrent();
        if (!StringComparer.OrdinalIgnoreCase.Equals(authority.SourceIdentity, _nvseHostSource!.StackIdentity))
            throw new InvalidDataException("Data authority differs from the actual selected native host graph.");
        _nvseDataVersion = _nvseHostSource.NvseVersion switch
        {
            0x06040080 => 3, // reviewed public 6.4.8 layout
            0x06040090 => 4, // reviewed public 6.4.9 layout; appended FormExtraData arms remain distinct
            _ => throw new NotSupportedException("Selected xNVSE version has no reviewed Data interface layout.")
        };
        _nvseDataLease = authority.RetainSource(); _nvseData = authority;
    }
    private byte[] DispatchNvseData(Frame frame, ulong parent)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Data callback has no actual native module.");
        VerifyNvse(plugin);
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle)
            throw new InvalidDataException("Data callback has a foreign generation/thread/module/handle.");
        var call = (NativeNvseDataCall)reader.ReadUInt32(); var identity = 0U;
        try
        {
            var authority = _nvseData ?? throw new NotSupportedException("Data callback has no actual campaign/device authority.");
            authority.RequireCurrent();
            switch (call)
            {
                case NativeNvseDataCall.Singleton:
                    identity = reader.ReadUInt32(); Finish(reader);
                    if (identity != 1) throw new NotSupportedException("Data singleton " + identity + " requires its native map/class ABI owner.");
                    Record(); return Payload(writer => writer.Write(1U));
                case NativeNvseDataCall.InputCreate:
                    identity = reader.ReadUInt32(); Finish(reader);
                    if (identity == 0 || _nvseInputAddress != 0) throw new InvalidDataException("DIHookControl pointer publication was absent or repeated.");
                    var input = RequireInput(authority.CaptureInput());
                    _nvseInputAddress = identity; _nvseInputBaseline = input.Keys.ToArray(); _nvseInputSnapshot = input; Record(); return InputReply(input);
                case NativeNvseDataCall.InputExchange:
                    identity = reader.ReadUInt32(); var sequence = reader.ReadUInt64(); var count = reader.ReadUInt32();
                    if (identity != _nvseInputAddress || identity == 0 || sequence != _nvseInputSequence + 1 || count != NativeNvseInputSnapshot.KeyCount || _nvseInputBaseline is null)
                        throw new InvalidDataException("DIHookControl transfer has no exact retained object/sequence/complete key extent.");
                    var changes = new List<NativeNvseInputControlChange>();
                    for (var key = 0; key < NativeNvseInputSnapshot.KeyCount; ++key)
                    {
                        var current = ReadInputKey(reader); var before = _nvseInputBaseline[key];
                        if (current.Raw != before.Raw || current.Game != before.Game || current.Inserted != before.Inserted)
                            throw new InvalidDataException("Original plugin changed device-owned raw/game/inserted state.");
                        if (!before.SameControls(current)) changes.Add(new(key, before, current));
                    }
                    Finish(reader); authority.PublishInput(changes);
                    var next = RequireInput(authority.CaptureInput()); _nvseInputBaseline = next.Keys.ToArray(); _nvseInputSequence = sequence; _nvseInputSnapshot = next;
                    Record(); return InputReply(next);
                case NativeNvseDataCall.Function:
                    identity = reader.ReadUInt32(); Finish(reader);
                    if (identity is not (2 or 3 or 7 or 8 or 9 or 10))
                        throw new NotSupportedException("Data function " + identity + " has no used native class/cache/extra-data producer.");
                    Record(); return Payload(writer => writer.Write(1U));
                case NativeNvseDataCall.InventoryCreate:
                    var container = reader.ReadUInt32(); var item = reader.ReadUInt32(); var countDelta = reader.ReadInt32(); var extra = reader.ReadUInt32(); Finish(reader);
                    var created = authority.CreateInventory(container, item, countDelta, extra);
                    _nvseInventoryPending.Add(created);
                    created.RequireCurrent();
                    if (created.FormId == 0 || created.Reference == 0 || created.InventoryReference == 0 || created.Entry == 0 ||
                        created.ExtraData != extra || _nvseInventoryReferences.ContainsKey(created.FormId) ||
                        _nvseInventoryReferences.Values.Any(value => value.Reference == created.Reference || value.InventoryReference == created.InventoryReference))
                        throw new InvalidDataException("Inventory constructor lacks genuine independent form/reference/entry/extra-data receipts.");
                    _nvseInventoryReferences.Add(created.FormId, created); _nvseInventoryPending.Remove(created); identity = created.FormId; Record();
                    return Payload(writer => { writer.Write(created.FormId); writer.Write(created.Reference); writer.Write(created.InventoryReference); });
                case NativeNvseDataCall.InventoryGet:
                    identity = reader.ReadUInt32(); Finish(reader);
                    var found = _nvseInventoryReferences.GetValueOrDefault(identity);
                    if (found is null) authority.RequireInventoryAbsence(identity); else found.RequireCurrent(); Record();
                    return Payload(writer => writer.Write(found?.InventoryReference ?? 0U));
                case NativeNvseDataCall.InventorySelf:
                    identity = reader.ReadUInt32(); Finish(reader);
                    if (identity == 0) { Record(); return Payload(writer => writer.Write(0U)); }
                    var self = _nvseInventoryReferences.Values.SingleOrDefault(value => value.InventoryReference == identity)
                        ?? throw new InvalidDataException("InventoryReference pointer has no current created entry owner.");
                    self.RequireCurrent(); Record(); return Payload(writer => writer.Write(self.Reference));
                case NativeNvseDataCall.LambdaSave:
                case NativeNvseDataCall.LambdaUnsave:
                case NativeNvseDataCall.IsLambda:
                    identity = reader.ReadUInt32(); Finish(reader);
                    var lambda = DataLambda(plugin, identity, call); Record(); return Payload(writer => writer.Write(lambda ? 1U : 0U));
                case NativeNvseDataCall.Data:
                    identity = reader.ReadUInt32(); Finish(reader);
                    throw new NotSupportedException("Data pointer " + identity + " requires its current preload/cold owner.");
                case NativeNvseDataCall.ClearCache:
                    Finish(reader); authority.ClearScriptDataCache(); Record(); return Payload(writer => writer.Write(1U));
                default: throw new InvalidDataException("Unknown Data interface callback.");
            }
        }
        catch (Exception error) { _nvseDataCallbacks.Add(new(Generation, frame.Id, parent, call, identity, error.Message)); throw; }
        void Record() => _nvseDataCallbacks.Add(new(Generation, frame.Id, parent, call, identity, null));
    }
    private NativeNvseInputSnapshot RequireInput(NativeNvseInputSnapshot input)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(input.Source, _nvseHostSource!.StackIdentity) || input.Window == 0 || input.Sample == 0 ||
            input.Revision <= 0 || input.Keys.Count != NativeNvseInputSnapshot.KeyCount)
            throw new InvalidDataException("DIHookControl has no complete actual product-window/device sample.");
        return input;
    }
    private static byte[] InputReply(NativeNvseInputSnapshot input) => Payload(writer =>
    {
        writer.Write(input.Window); writer.Write(input.Sample); writer.Write(input.Revision); writer.Write((uint)input.Keys.Count);
        foreach (var value in input.Keys)
        { writer.Write(value.Raw); writer.Write(value.Game); writer.Write(value.Inserted); writer.Write(value.Hold); writer.Write(value.Tap); writer.Write(value.UserDisabled); writer.Write(value.ScriptDisabled); }
    });
    private static NativeNvseInputKey ReadInputKey(BinaryReader reader)
    {
        bool Bit() { var value = reader.ReadByte(); return value < 2 ? value == 1 : throw new InvalidDataException("DIHookControl bool byte is outside its public layout."); }
        return new(Bit(), Bit(), Bit(), Bit(), Bit(), Bit(), Bit());
    }
    internal void RequireNvseDataIdleForSave(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall();
        if (_nvseInventoryReferences.Count != 0 || _nvseInventoryPending.Count != 0 || _nvseLambdaPendingValues.Count != 0 ||
            _nvseLambdaCaptures.Values.Any(value => value.Saves != 0 || value.Leases.Count != 0))
            throw new InvalidOperationException("Current native inventory/lambda references still own source-frame or event-list state.");
        if (_nvseInputAddress != 0)
            throw new NotSupportedException("Used native DirectInput controls require the current campaign cold/process-state owner; a live sample is not a save receipt.");
    }
    internal void RetireNvseInventoryFrame(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall();
        foreach (var publication in _nvseInventoryReferences.Values.ToArray())
        { publication.Lifetime.Dispose(); _nvseInventoryReferences.Remove(publication.FormId); }
        foreach (var publication in _nvseInventoryPending.ToArray())
        { publication.Lifetime.Dispose(); _nvseInventoryPending.Remove(publication); }
    }
    private void ClearNvseData()
    {
        // Unload or verified child closure precedes every native callable owner.
        foreach (var publication in _nvseInventoryReferences.Values.ToArray())
        { publication.Lifetime.Dispose(); _nvseInventoryReferences.Remove(publication.FormId); }
        foreach (var publication in _nvseInventoryPending.ToArray())
        { publication.Lifetime.Dispose(); _nvseInventoryPending.Remove(publication); }
        ClearNvseLambdaCaptures();
        _nvseDataLease?.Dispose(); _nvseDataLease = null;
        _nvseData = null; _nvseDataVersion = _nvseInputAddress = 0; _nvseInputSequence = 0; _nvseInputBaseline = null; _nvseInputSnapshot = null;
    }
}
