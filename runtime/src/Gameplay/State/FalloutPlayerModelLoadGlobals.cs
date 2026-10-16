using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerModelGlobalStore(FalloutReferenceModelDataInvocation Load, Guid Factory,
    long Changed, string Field, bool BeforeKnown, Guid? Before, Guid? After, Guid CommonProcess,
    Guid? ModelFactory = null, FalloutNifSceneKey? Node = null);
internal sealed record FalloutPlayerFirstNamedGlobal(bool Known, Guid? Model, FalloutNifSceneKey? Node);
internal sealed record FalloutPlayerModelLoadGlobalsSnapshot(string Source, Guid Process, Guid Factory, long Sequence,
    bool WeaponKnown, Guid? Weapon, bool CameraKnown, Guid? CameraModel, FalloutNifSceneKey? CameraNode,
    IReadOnlyList<FalloutPlayerModelGlobalStore> Stores, string? Writer,
    IReadOnlyList<FalloutPlayerFirstNamedGlobal> FirstNamed);

// These are engine globals, not GLOB records, camera presentation or another
// reference field. A dominating current-Player store can own zero independently
// of an unavailable old value; no fabricated old zero is reported.
internal sealed class FalloutPlayerModelLoadGlobals
{
    private readonly FalloutPlayerModelLoadContinuationSource _source;
    private readonly Guid _process;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly List<FalloutPlayerModelGlobalStore> _stores = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _weaponKnown, _cameraKnown;
    private Guid? _weapon, _cameraModel;
    private FalloutNifSceneKey? _camera;
    private readonly FalloutPlayerFirstNamedGlobal[] _firstNamed = Enumerable.Repeat(new FalloutPlayerFirstNamedGlobal(false, null, null), 3).ToArray();
    private long _sequence;
    private string? _writer;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal string SourceIdentity => _source.Identity;
    internal FalloutPlayerModelLoadGlobals(FalloutPlayerModelLoadContinuationSource source, Guid process,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireActual);
        if (process == Guid.Empty) throw new InvalidDataException("Model globals require their actual Player process.");
        _source = source; _process = process; _requireActual = requireActual;
    }
    internal FalloutPlayerModelGlobalStore StoreCurrentPlayerWeaponNull(FalloutReferenceModelDataInvocation invocation,
        FalloutProcessAnimationLoadOwner actualCurrentPlayerProcess)
    {
        Require(invocation);
        if (actualCurrentPlayerProcess.Process != invocation.CommonProcess || actualCurrentPlayerProcess.Actor != invocation.Actor ||
            !invocation.Actor.EnginePlayer || actualCurrentPlayerProcess.Epoch != invocation.Epoch)
            throw new InvalidOperationException("Weapon global zero lacks the actual current Player/Common association.");
        var result = new FalloutPlayerModelGlobalStore(invocation, Factory, checked(++_sequence), "Player-weapon-global",
            _weaponKnown, _weapon, null, invocation.CommonProcess);
        _weapon = null; _weaponKnown = true; _stores.Add(result); Require(invocation); return result;
    }
    internal FalloutPlayerModelGlobalStore StoreThirdCamera(FalloutReferenceModelDataInvocation invocation,
        FalloutActorLogicalScene model, FalloutNifSceneObject? camera)
    {
        Require(invocation);
        if (camera is not null && !model.Objects.Any(value => ReferenceEquals(value, camera)))
            throw new InvalidDataException("Loader camera lookup returned a node from another actual logical graph.");
        var result = new FalloutPlayerModelGlobalStore(invocation, Factory, checked(++_sequence), "Player-third-camera-node",
            _cameraKnown, _cameraModel, camera is null ? null : model.Factory, invocation.CommonProcess, model.Factory, camera?.Key);
        _cameraKnown = true; _cameraModel = camera is null ? null : model.Factory; _camera = camera?.Key;
        _stores.Add(result); Require(invocation); return result;
    }
    internal FalloutPlayerModelGlobalStore StoreFirstNamedNode(FalloutReferenceModelDataInvocation invocation,
        FalloutActorLogicalScene model, int index, FalloutNifSceneObject? node)
    {
        Require(invocation);
        if ((uint)index >= 3 || node is not null && !model.Objects.Any(value => ReferenceEquals(value, node)))
            throw new InvalidDataException("First named global changed its actual selected slot/graph result.");
        var before = _firstNamed[index]; var after = new FalloutPlayerFirstNamedGlobal(true, node is null ? null : model.Factory, node?.Key);
        var result = new FalloutPlayerModelGlobalStore(invocation, Factory, checked(++_sequence), "Player-first-named-node-" + index,
            before.Known, before.Model, after.Model, invocation.CommonProcess, model.Factory, node?.Key);
        _firstNamed[index] = after; _stores.Add(result); Require(invocation); return result;
    }
    internal void ObserveEnteredWriter(string producer, Action requireActual)
    { requireActual(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); _writer = producer; checked { ++_sequence; } }
    internal FalloutPlayerModelLoadGlobalsSnapshot Capture() => new(_source.Identity, _process, Factory, _sequence,
        _weaponKnown, _weapon, _cameraKnown, _cameraModel, _camera, _stores.ToArray(), _writer, _firstNamed.ToArray());
    private void Require(FalloutReferenceModelDataInvocation invocation)
    {
        _requireActual(invocation);
        if (_writer is { } writer) throw new NotSupportedException("source-Player-model-global-writer-unowned:" + writer);
        if (invocation.ReferenceProcess != _process || invocation.ThreadId != _thread || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player model globals left their actual process/load/thread.");
    }
}
