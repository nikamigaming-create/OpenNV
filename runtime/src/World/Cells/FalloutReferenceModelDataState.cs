using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

// This is the inherited reference's nullable data object, not its selected
// model, the Player selection node, a renderer root or a controller wrapper.
internal sealed partial class FalloutReferenceModelDataState
{
    internal const string SchemaBoundary = "source-reference-data-current-writer-unbound";
    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly List<FalloutReferenceModelDataPublication> _publications = [];
    private readonly List<FalloutReferenceModelDataCall> _calls = [];
    private readonly List<FalloutReferenceModelColdBinding> _coldBindings = [];
    private readonly HashSet<Guid> _entries = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private DataObject? _dataObject;
    private Guid? _previousData;
    private Guid? _data => _dataObject?.Identity;
    private FalloutReferenceModelDataValues? _values
    {
        get => _dataObject?.Fields;
        set
        {
            if (value is null) { _dataObject = null; return; }
            (_dataObject ?? throw new InvalidOperationException("Reference data has no actual allocated object.")).Fields = value;
        }
    }
    private FalloutReferenceModelDataNode? _model, _other;
    private FalloutActorLogicalSceneSnapshot? _retainedModelScene;
    private bool _modelRetained, _otherRetained;
    private long _sequence, _fault;
    private bool _busy, _retired;
    private string? _failure, _writer;
    private FalloutReferenceModelDataInvocation? _entered;
    private long _enteredFault;
    internal FalloutPlayerMoverDeltaSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid CommonProcess { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal long Epoch { get; }
    internal FalloutActorLogicalSceneSnapshot? PreparedColdModel => _model is null && _values?.Model is not null ? _retainedModelScene : null;
    internal string? SaveBlocker => _busy ? "source-reference-data-in-flight" : _failure ?? _writer ?? _model?.SaveBlocker ??
        (_values?.Model is not null && _model is null || _values?.OtherModel is not null && _other is null ?
            "source-cold-reference-model-fresh-native-binding-unavailable" : _retired ? "source-reference-data-retired" :
            _calls.Count == 0 ? "source-reference-data-actual-constructor-field-unbound" : null);

    internal FalloutReferenceModelDataState(FalloutPlayerMoverDeltaSource source, FalloutPluginStack records,
        string stack, Guid process, Guid commonProcess, Guid constructor, FalloutCombatActorIdentity actor, long epoch,
        Action<FalloutReferenceModelDataInvocation> requireActual, Action requireLifetime,
        FalloutReferenceModelDataSnapshot? saved = null)
    {
        source.Validate(); actor.Validate(); ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(requireLifetime);
        if (process == Guid.Empty || commonProcess == Guid.Empty || constructor == Guid.Empty || epoch < 1) throw new InvalidDataException("Reference data lacks its actual constructor/process/epoch.");
        Source = source; _records = records; Stack = stack; Process = process; CommonProcess = commonProcess; Constructor = constructor;
        Actor = actor; Epoch = epoch; _requireActual = requireActual; _requireLifetime = requireLifetime;
        if (saved is not null)
        {
            Validate(saved); if (saved.Source != source || saved.Stack != stack || saved.Actor != actor ||
                saved.Process == process || saved.CommonProcess == commonProcess || saved.Constructor == constructor || saved.Retired || saved.Epoch != epoch)
                throw new InvalidDataException("Cold reference data changed source or reused its prior lifetime.");
            _sequence = saved.Sequence; _previousData = saved.Data;
            _dataObject = saved.Fields is null ? null : new(saved.Fields);
            _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls);
            _coldBindings.AddRange(saved.ColdBindings ?? []); _retainedModelScene = saved.ModelScene;
            foreach (var call in _calls) _entries.Add(call.Invocation.Call);
            foreach (var binding in _coldBindings) _entries.Add(binding.Invocation.Call);
            _failure = saved.Failure; _writer = saved.WriterBoundary;
        }
        _publications.Add(new(process, commonProcess, Factory, constructor, _sequence, saved?.Process, saved?.Factory, saved?.Data, _data));
    }

    internal void ConstructReferenceField(FalloutReferenceModelDataInvocation invocation)
    {
        if (invocation.Ingress != FalloutReferenceModelDataIngress.ReferenceConstructor || _publications.Count != 1 || _calls.Count != 0)
            throw new InvalidOperationException("Reference data constructor cannot replay a current/cold load or field publication.");
        Run(invocation, stores => { _dataObject = null; stores.Add(new(Next(), "reference-data", Data: null)); });
    }

    internal void EnsureForThreeDLoad(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceParentCellRead parent)
    {
        if (invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
            throw new InvalidOperationException("Lazy reference data has no actual entered 3D-load caller.");
        Run(invocation, stores => Ensure(invocation, parent, stores));
    }

    internal void StoreReturnedModel(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceParentCellRead? parent, FalloutReferenceModelDataNode? supplied)
    {
        if (invocation.Ingress != FalloutReferenceModelDataIngress.ModelSetter)
            throw new InvalidOperationException("Reference model setter lost its actual returned model-factory caller.");
        Run(invocation, stores =>
        {
            if (supplied is null)
            {
                Release(ref _model, "model"); Release(ref _other, "other-model");
                if (_data is not null) { _dataObject = null; stores.Add(new(Next(), "reference-data", Data: null)); }
                return;
            }
            supplied.RequireCurrent(); CheckEntered();
            if (supplied.Node.Process != Process)
                throw new InvalidDataException("Reference model factory returned another current process's source node.");
            Ensure(invocation, parent ?? throw new InvalidDataException("Nonnull model setter omitted its current ParentCELL read."), stores);
            if (_model?.Node == supplied.Node) return;
            Release(ref _model, "model");
            CheckEntered(); _model = supplied; _values = _values! with { Model = supplied.Node };
            stores.Add(new(Next(), "model", Node: supplied.Node));
            CheckEntered(); supplied.Retain(() =>
            { _modelRetained = true; stores.Add(new(Next(), "model-retained", Node: supplied.Node)); CheckEntered(); }); CheckEntered();

            void Release(ref FalloutReferenceModelDataNode? node, string field)
            {
                if (node is null) return;
                CheckEntered();
                var retained = field == "model" ? _modelRetained : _otherRetained;
                if (retained) node.Release(() => { if (field == "model") _modelRetained = false; else _otherRetained = false; });
                CheckEntered(); node = null;
                if (_values is not null) _values = field == "model" ? _values with { Model = null } : _values with { OtherModel = null };
                stores.Add(new(Next(), field));
                CheckEntered();
            }
        }, supplied: supplied?.Node);
    }

    internal void BindColdModel(FalloutReferenceModelDataInvocation invocation, FalloutReferenceModelDataNode supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied); RequireIdle(); Require(invocation);
        if (invocation.Ingress != FalloutReferenceModelDataIngress.ColdBinding || _publications.Count < 2 ||
            _failure is not null || _writer is not null || _model is not null || _modelRetained || _other is not null ||
            _data is not { } data || _values is not { } fields || fields.OtherModel is not null || fields.Model is not { } previous ||
            _retainedModelScene is not { } before || !_entries.Add(invocation.Call))
            throw new InvalidOperationException("Cold reference model lacks its genuine unbound saved field/current constructor.");
        var owner = _publications[^1];
        if (owner.PreviousData is null || owner.CurrentData != data || supplied.Node.Process != Process ||
            supplied.Node.Factory == previous.Factory || previous.Factory != before.Factory)
            throw new InvalidDataException("Cold reference model substituted another saved node or current native factory.");
        var entered = Next(); var fault = _fault; _busy = true; _entered = invocation; _enteredFault = fault;
        var stored = false; var retained = false; Guid? lifetime = null; long? logicalSequence = null; string? error = null;
        try
        {
            CheckEntered(); supplied.RequireCurrent();
            var after = supplied.CaptureScene(); FalloutActorLogicalScene.RequireColdRestoration(before, after);
            lifetime = after.Lifetime; logicalSequence = after.Sequence;
            CheckEntered(); _model = supplied; _values = fields with { Model = supplied.Node }; stored = true;
            supplied.Retain(() => { _modelRetained = retained = true; CheckEntered(); }); CheckEntered();
        }
        catch (Exception failed) { error = failed.Message; _failure ??= error; throw; }
        finally
        {
            _coldBindings.Add(new(invocation, Factory, entered, Next(), data, previous, before, supplied.Node,
                lifetime, logicalSequence, stored, retained, error is null, error));
            _entered = null; _busy = false;
        }
    }

    internal FalloutReferenceModelDataRead ReadForMover(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); RequireIdle(); var fault = _fault;
        Check(); _model?.RequireCurrent(); _other?.RequireCurrent(); Check();
        if (SaveBlocker is { } boundary) throw new NotSupportedException(boundary);
        if (_calls.Count == 0) throw new NotSupportedException("source-reference-data-actual-constructor-field-unbound");
        return new(Source.Identity, Stack, Process, Factory, Constructor, Actor, Epoch, _sequence, _data, _values);
        void Check()
        {
            requireActual(invocation); _requireLifetime();
            if (_fault != fault || invocation.PlayerProcess != Process || invocation.PlayerConstructor != Constructor ||
                invocation.Common.Actor != Actor || invocation.Common.Process != CommonProcess || invocation.Common.Epoch != Epoch || invocation.ThreadId != _thread)
                throw new InvalidOperationException("Reference data mover borrow changed actual source/caller/factory/epoch/thread.");
        }
    }

    internal void ObserveUnownedWriter(FalloutReferenceModelDataInvocation invocation, string boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        if (invocation.Ingress != FalloutReferenceModelDataIngress.UnownedWriter)
            throw new InvalidDataException("Reference data invalidation has another actual ingress.");
        Run(invocation, _ => _writer ??= boundary, allowWriter: true);
    }

    private void Ensure(FalloutReferenceModelDataInvocation invocation, FalloutReferenceParentCellRead parent,
        List<FalloutReferenceModelDataStore> stores)
    {
        if (_data is not null) return;
        // ParentCELL belongs to the actual World reference owner. Its process
        // is independent from the Scene process that owns this data pointer.
        if (parent.Actor != Actor || parent.Process == Guid.Empty || parent.Constructor != Constructor || parent.Stack != Stack ||
            parent.Factory == Guid.Empty || parent.Sequence < 0 || parent.ExecutableSha256 != Source.Controller.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Lazy reference data lost the actual current ParentCELL owner.");
        // Allocation publishes its actual object and the two constructor-null
        // retained children first. A later water getter failure retains that
        // object and the two already committed scalar words.
        CheckEntered(); _dataObject = new(new(null, null, null, null, null, null, null));
        stores.Add(new(Next(), "reference-data", Data: _data)); CheckEntered();
        _values = _values! with { FirstWord = 0 }; stores.Add(new(Next(), "first-word", 0)); CheckEntered();
        _values = _values! with { SecondWord = 0 }; stores.Add(new(Next(), "second-word", 0)); CheckEntered();
        uint water = FalloutPlayerMoverDeltaSource.UninitializedWater;
        if (parent.Cell is { } cell)
        {
            var definition = FalloutCellSceneReader.ReadDefinition(_records, cell);
            if ((definition.Flags & FalloutPlayerMoverDeltaSource.CellWaterFlag) != 0)
                throw new NotSupportedException("source-reference-data-current-CELL-water-height-getter-unbound");
        }
        CheckEntered(); _values = _values! with { WaterBits = water }; stores.Add(new(Next(), "water", water)); CheckEntered();
        _values = _values! with { ScalarBits = FalloutPlayerMoverDeltaSource.UninitializedScalar };
        stores.Add(new(Next(), "scalar", FalloutPlayerMoverDeltaSource.UninitializedScalar)); CheckEntered();
        _values = _values! with { ThirdWord = 0 }; stores.Add(new(Next(), "third-word", 0)); CheckEntered();
    }
    private void Run(FalloutReferenceModelDataInvocation invocation, Action<List<FalloutReferenceModelDataStore>> body,
        bool allowWriter = false, FalloutSourceSceneNode? supplied = null)
    {
        RequireIdle(); Require(invocation);
        if (_failure is { } failure) throw new NotSupportedException(failure);
        if (!allowWriter && _writer is { } writer) throw new NotSupportedException(writer);
        if (!allowWriter && (_values?.Model is not null && _model is null || _values?.OtherModel is not null && _other is null))
            throw new NotSupportedException("source-cold-reference-model-fresh-native-binding-unavailable");
        if (!_entries.Add(invocation.Call)) throw new InvalidOperationException("Reference data replayed its actual caller.");
        var fault = _fault; var entered = Next(); var before = _data; var stores = new List<FalloutReferenceModelDataStore>();
        _busy = true; _entered = invocation; _enteredFault = fault; string? error = null;
        try { Guard(); body(stores); Guard(); }
        catch (Exception failed) { error = failed.Message; _failure ??= error; throw; }
        finally { _calls.Add(new(invocation, Factory, entered, Next(), before, _data, stores.ToArray(), error is null, error, supplied)); _entered = null; _busy = false; }
        void Guard() { Require(invocation); if (_fault != fault) throw new InvalidOperationException("Reference data swallowed reentry after a committed source store."); }
    }
    private void CheckEntered()
    {
        var invocation = _entered ?? throw new InvalidOperationException("Reference data mutation has no actual entered source caller.");
        Require(invocation); if (_fault != _enteredFault) throw new InvalidOperationException("Reference data swallowed reentry after a committed source store.");
    }
    private void Require(FalloutReferenceModelDataInvocation invocation)
    {
        _requireActual(invocation); _requireLifetime();
        if (invocation.Call == Guid.Empty || invocation.Actor != Actor || invocation.ReferenceProcess != Process ||
            invocation.ReferenceConstructor != Constructor || invocation.CommonProcess != CommonProcess || invocation.Epoch != Epoch || invocation.ThreadId != _thread)
            throw new InvalidOperationException("Reference data lost its actual source constructor/Common/process/epoch/thread.");
    }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy || _thread != Environment.CurrentManagedThreadId) { ++_fault; throw new InvalidOperationException("Reference data reentered or escaped its current source thread."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private sealed class DataObject(FalloutReferenceModelDataValues fields)
    {
        internal Guid Identity { get; } = Guid.NewGuid();
        internal FalloutReferenceModelDataValues Fields { get; set; } = fields;
    }
    internal FalloutReferenceModelDataSnapshot Capture()
    {
        RequireIdle(); var snapshot = new FalloutReferenceModelDataSnapshot(Source, Stack, Process, CommonProcess, Factory, Constructor,
            Actor, Epoch, _sequence, _data, _values, _publications.ToArray(), _calls.ToArray(), _writer, _failure, _retired, _previousData,
            _values?.Model is null ? null : _model?.CaptureScene() ?? _retainedModelScene, _coldBindings.ToArray());
        Validate(snapshot); return snapshot;
    }
    internal bool RetainsReturnedModel(FalloutSourceSceneNode node)
    {
        RequireIdle();
        return _model?.Node == node || _other?.Node == node;
    }
    internal void Retire()
    {
        if (_retired) return; RequireIdle(); _requireLifetime();
        if (_model is not null) _retainedModelScene = _model.CaptureScene();
        if (_modelRetained) _model!.Release(() => _modelRetained = false);
        _model = null;
        if (_otherRetained) _other!.Release(() => _otherRetained = false);
        _other = null; _retired = true;
    }
}
