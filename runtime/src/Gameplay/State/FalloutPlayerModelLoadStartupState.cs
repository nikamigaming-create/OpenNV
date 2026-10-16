using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// This process-global byte is independent of ScriptSession.InCharGen. The PE
// loader initializes it; only the selected startup prefix or actual teardown
// owns a later value. Merely constructing a menu, model or quest cannot set it.
internal sealed partial class FalloutPlayerModelLoadStartupState
{
    internal const string Schema = "opennv-player-model-load-startup-byte/v1";
    private readonly FalloutPluginStack _records;
    private readonly FalloutInstallationSettings _settings;
    private readonly FalloutPluginStackLoadReceipt _recordLoad;
    private readonly Action<FalloutPlayerModelStartupInvocation, FalloutNewGameBootstrap?> _requireActualWriter;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActualLoad;
    private readonly Action _requireCurrent;
    private readonly List<FalloutPlayerModelStartupPublication> _publications = [];
    private readonly List<FalloutPlayerModelStartupCall> _calls = [];
    private readonly HashSet<Guid> _writerCalls = [];
    private byte _value = FalloutPlayerModelLoadPrerequisiteSource.LoaderInitializedCharacterGenerationByte;
    private long _sequence, _fault;
    private bool _busy, _retired;
    private string? _writerBoundary, _failure;
    internal FalloutPlayerModelLoadPrerequisiteSource Source { get; }
    internal FalloutPluginStack Records => _records;
    internal FalloutInstallationSettings Settings => _settings;
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; } = Guid.NewGuid();
    internal FalloutPlayerModelStartupCall? LastCall { get; private set; }
    internal FalloutPlayerModelLoadGateRead? LastGate { get; private set; }
    internal string? SaveBlocker => _busy ? "source-Player-model-startup-byte-entered" :
        _retired ? "source-Player-model-startup-byte-retired" : _writerBoundary ?? _failure;

    internal FalloutPlayerModelLoadStartupState(FalloutPlayerModelLoadPrerequisiteSource source,
        FalloutPluginStack records, FalloutInstallationSettings settings, string stack, Guid process,
        Action<FalloutPlayerModelStartupInvocation, FalloutNewGameBootstrap?> requireActualWriter,
        Action<FalloutReferenceModelDataInvocation> requireActualLoad, Action requireCurrent,
        FalloutPlayerModelStartupSnapshot? saved = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(requireActualWriter); ArgumentNullException.ThrowIfNull(requireActualLoad);
        ArgumentNullException.ThrowIfNull(requireCurrent);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty)
            throw new InvalidDataException("Player model startup byte lacks its actual current engine process.");
        Source = source; _records = records; _settings = settings; Stack = stack; Process = process;
        _requireActualWriter = requireActualWriter; _requireActualLoad = requireActualLoad; _requireCurrent = requireCurrent;
        _recordLoad = records.LoadReceipt; RequireRecords();
        if (records.OwnedSource is not null && _recordLoad.StackIdentity != stack)
            throw new InvalidDataException("Player startup byte changed the complete current owned stack.");
        if (saved is not null)
        {
            Validate(saved, source, Stack, records);
            if (saved.Retired || saved.Publications.Any(row => row.Process == process || row.Factory == Factory ||
                row.Constructor == Constructor || row.RecordsFactory == _recordLoad.Factory))
                throw new InvalidDataException("Cold startup byte reused an old actual process, reader or constructor.");
            _sequence = saved.Sequence; _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls);
            foreach (var call in saved.Calls) _writerCalls.Add(call.Invocation.Call);
            // A new PE loader owns a fresh zero. Saved byte-one history is not
            // a writer in this new process, nor a replay of character creation.
        }
        _publications.Add(new(Process, Factory, Constructor, _recordLoad.Factory, checked(++_sequence), _value,
            saved?.Process, saved?.Factory));
    }

    internal FalloutPlayerModelStartupCall StoreEnteredCharacterGeneration(FalloutPlayerModelStartupInvocation invocation,
        FalloutNewGameBootstrap bootstrap) => Run(invocation, bootstrap, scope =>
    {
        if (invocation.Ingress != FalloutPlayerModelStartupIngress.CharacterGenerationStart)
            throw new InvalidOperationException("Character-generation byte store has another actual caller.");
        var declaration = Source.CharacterGenerationQuest.Declaration.Name; var separator = declaration.LastIndexOf(':');
        scope.Setting = _settings.Require(declaration[(separator + 1)..], declaration[..separator]);
        var selected = FalloutNewGameBootstrap.StartingQuest(_records, _settings);
        scope.Step = FalloutPlayerModelStartupStep.SelectedQuest; scope.SelectedQuest = selected.FormKey;
        if (invocation.Quest != selected.FormKey || !ReferenceEquals(bootstrap.Quest, selected) ||
            !ReferenceEquals(_records.GetEffective(bootstrap.Quest.FormKey), bootstrap.Quest))
            throw new InvalidDataException("Character-generation prefix changed its actual current configured Quest.");
        GuardWriter(invocation, bootstrap, scope.Fault);
        _value = FalloutPlayerModelLoadPrerequisiteSource.EnteredCharacterGenerationByte;
        scope.Stored = checked(++_sequence); scope.Step = FalloutPlayerModelStartupStep.ByteStored;
    });

    internal FalloutPlayerModelStartupCall StoreEnteredLoadingMenuTeardown(FalloutPlayerModelStartupInvocation invocation) =>
        Run(invocation, null, scope =>
        {
            if (invocation.Ingress != FalloutPlayerModelStartupIngress.LoadingMenuTeardown || invocation.Quest is not null)
                throw new InvalidOperationException("Startup-byte zero store has another actual destructor caller.");
            _value = FalloutPlayerModelLoadPrerequisiteSource.LoaderInitializedCharacterGenerationByte;
            scope.Stored = checked(++_sequence); scope.Step = FalloutPlayerModelStartupStep.ByteStored; _writerBoundary = null;
        });

    internal FalloutPlayerModelStartupCall ObserveEnteredWriter(FalloutPlayerModelStartupInvocation invocation, string boundary) =>
        Run(invocation, null, scope =>
        {
            if (invocation.Ingress != FalloutPlayerModelStartupIngress.UnownedWriter || invocation.Quest is not null)
                throw new InvalidOperationException("Unknown startup-byte writer has another actual ingress.");
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
            scope.Writer = boundary; _writerBoundary = boundary; scope.Step = FalloutPlayerModelStartupStep.Invalidated;
        });

    internal FalloutPlayerModelLoadGateRead ReadEarlyGate(FalloutReferenceModelDataInvocation invocation,
        FalloutSourceReferenceChangeManager changes)
    {
        ArgumentNullException.ThrowIfNull(changes); RequireIdle(); var fault = _fault; _busy = true;
        try
        {
            GuardLoad(invocation, fault);
            var flags = changes.ReadPlayerModelLoadFlags(invocation, Source, actual => GuardLoad(actual, fault));
            GuardLoad(invocation, fault);
            if (flags.Stack != Stack || flags.Source != Source.Identity)
                throw new InvalidDataException("Player loader borrowed current flags from a different actual stack.");
            FalloutPlayerModelStartupRead? characterGeneration = null;
            if ((flags.Flags & FalloutPlayerModelLoadPrerequisiteSource.InventoryPreparationSuppressionMask) == 0)
            {
                if (_writerBoundary is not null) throw new NotSupportedException(_writerBoundary);
                characterGeneration = new(invocation, Source.Identity, Stack, Process, Factory,
                    Constructor, _recordLoad.Factory, _sequence, _value);
                GuardLoad(invocation, fault);
            }
            var result = new FalloutPlayerModelLoadGateRead(invocation, Source.Identity, Factory, flags, characterGeneration,
                characterGeneration is { Value: 0 });
            LastGate = result; return result;
        }
        finally { _busy = false; }
    }

    private FalloutPlayerModelStartupCall Run(FalloutPlayerModelStartupInvocation invocation,
        FalloutNewGameBootstrap? bootstrap, Action<WriterScope> action)
    {
        RequireIdle();
        if (invocation.Call == Guid.Empty || invocation.Process != Process || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            !Enum.IsDefined(invocation.Ingress))
            throw new InvalidOperationException("Startup-byte invocation has no actual current process, call or thread.");
        if (!_writerCalls.Add(invocation.Call)) throw new InvalidOperationException("Startup-byte writer replayed its actual call.");
        var scope = new WriterScope(checked(++_sequence), _value, _fault); _busy = true; string? failure = null;
        try
        {
            GuardWriter(invocation, bootstrap, scope.Fault); action(scope); GuardWriter(invocation, bootstrap, scope.Fault);
            scope.Step = FalloutPlayerModelStartupStep.Returned;
        }
        catch (Exception error) { failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message; throw; }
        finally
        {
            LastCall = new(invocation, Factory, Constructor, scope.Entered, checked(++_sequence), scope.Step,
                scope.Before, _value, scope.Stored, scope.Setting, scope.SelectedQuest, scope.Writer, failure is null, failure);
            _calls.Add(LastCall); _failure ??= failure; _busy = false;
        }
        return LastCall ?? throw new InvalidOperationException("Startup-byte writer omitted its completed receipt.");
    }

    private void GuardWriter(FalloutPlayerModelStartupInvocation invocation, FalloutNewGameBootstrap? bootstrap, long fault)
    {
        if (_retired || !_busy || _fault != fault || invocation.Call == Guid.Empty || invocation.Process != Process ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || !Enum.IsDefined(invocation.Ingress) ||
            (invocation.Ingress == FalloutPlayerModelStartupIngress.CharacterGenerationStart) != (bootstrap is not null))
            throw new InvalidOperationException("Startup-byte writer lost its real current process, caller or thread.");
        RequireRecords(); _requireActualWriter(invocation, bootstrap);
        if (_retired || !_busy || _fault != fault)
            throw new InvalidOperationException("Startup-byte writer guard swallowed actual owner reentry.");
    }
    private void GuardLoad(FalloutReferenceModelDataInvocation invocation, long fault)
    {
        invocation.Actor.Validate();
        if (_retired || !_busy || _fault != fault || invocation.Call == Guid.Empty || invocation.ReferenceProcess == Guid.Empty ||
            invocation.ReferenceConstructor == Guid.Empty || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
            throw new InvalidOperationException("Startup-byte read lost the actual outer Player load owner or thread.");
        RequireRecords(); _requireActualLoad(invocation);
        if (_retired || !_busy || _fault != fault)
            throw new InvalidOperationException("Startup-byte getter swallowed actual owner reentry.");
    }
    private void RequireRecords()
    { _records.RequireLiveLoadReceipt(_recordLoad); _requireCurrent(); _records.RequireLiveLoadReceipt(_recordLoad); }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { ++_fault; throw new InvalidOperationException("Player startup-byte owner reentered."); }
    }
    internal void Retire() { if (_retired) return; RequireIdle(); _retired = true; }
    private sealed class WriterScope(long entered, byte before, long fault)
    {
        internal long Entered = entered, Fault = fault;
        internal byte Before = before;
        internal long? Stored;
        internal string? Setting, Writer;
        internal FalloutFormKey? SelectedQuest;
        internal FalloutPlayerModelStartupStep Step;
    }
}
