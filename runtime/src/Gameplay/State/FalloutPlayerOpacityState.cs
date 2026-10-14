using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;
using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerOpacityGlobals(byte FirstGate, byte SecondGate, byte OverrideEnabled,
    int OverrideMode, uint PlayerAlphaBits, long Stores, bool Available)
{
    internal static FalloutPlayerOpacityGlobals Constructed => new(0, 0, 0, 0, FalloutActorTeleportFade.One, 0, true);
}
internal enum FalloutPlayerOpacityStep { Override, ProcessQuery, Product, EffectiveStore, FirstPerson, SceneAlpha, Returned, Failed }
internal sealed record FalloutPlayerOpacityCall(Guid Invocation, Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutPlayerOpacityStep Step, FalloutPlayerOpacityStep? FailedAt, FalloutProcessOpacityQuery? Process,
    uint? RequestedBits, bool EffectiveStored, Guid? FirstPersonFactory, FalloutNifSceneAlphaCall? Scene,
    string? Failure);
internal sealed record FalloutPlayerOpacitySnapshot(string Schema, FalloutPlayerOpacitySource Source, string Stack,
    Guid CapturedProcess, Guid Lifetime, long Sequence, FalloutPlayerOpacityGlobals Globals,
    FalloutPlayerOpacityCall? Call, FalloutActorLogicalSceneSnapshot? FirstPerson, string? Failure, bool Retired,
    Guid? RestoredProcess, Guid? RestoredLifetime);

// Source globals, current process alpha and the actual source model stay distinct.
// Godot may observe the completed shader stores; it supplies none of their inputs.
internal sealed class FalloutPlayerOpacityState
{
    internal const string Schema = "opennv-Player-opacity/v1";
    private readonly string _stack;
    private readonly Guid _process, _lifetime = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Func<FalloutStandaloneFirstPersonOwner?> _first;
    private readonly Func<FalloutActorLogicalScene?> _scene;
    private readonly Func<uint> _timer;
    private readonly Func<string, uint> _settings;
    private readonly Action<Guid, long> _requireInvocation;
    private FalloutPlayerOpacityGlobals _globals = FalloutPlayerOpacityGlobals.Constructed;
    private FalloutPlayerOpacityCall? _call;
    private FalloutActorLogicalSceneSnapshot? _historicalScene;
    private Guid? _restoredProcess, _restoredLifetime;
    private long _sequence;
    private bool _busy, _retired;
    private string? _failure;
    internal FalloutPlayerOpacityState(FalloutPlayerOpacitySource source, string stack, Guid process,
        Func<FalloutStandaloneFirstPersonOwner?> readFirstPerson, Func<FalloutActorLogicalScene?> readScene,
        Func<uint> readMainScaledTimer, Func<string, uint> readSetting, Action<Guid, long> requireInvocation,
        FalloutPlayerOpacitySnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new ArgumentException("Player opacity requires its current Main lifetime.", nameof(process));
        ArgumentNullException.ThrowIfNull(readFirstPerson); ArgumentNullException.ThrowIfNull(readScene);
        ArgumentNullException.ThrowIfNull(readMainScaledTimer); ArgumentNullException.ThrowIfNull(readSetting); ArgumentNullException.ThrowIfNull(requireInvocation);
        Source = source; _stack = stack; _process = process; _first = readFirstPerson; _scene = readScene;
        _timer = readMainScaledTimer; _settings = readSetting; _requireInvocation = requireInvocation;
        if (saved is not null)
        {
            Validate(saved);
            if (saved.Source != source || saved.Stack != stack || saved.CapturedProcess == process || saved.Lifetime == _lifetime || saved.Retired)
                throw new InvalidDataException("Cold Player opacity changed its selected source or fresh process lifetime.");
            _globals = saved.Globals; _call = saved.Call; _sequence = saved.Sequence; _failure = saved.Failure;
            _historicalScene = saved.FirstPerson; _restoredProcess = saved.CapturedProcess; _restoredLifetime = saved.Lifetime; Next();
        }
    }
    internal FalloutPlayerOpacitySource Source { get; }
    internal Guid Process => _process;
    internal string Stack => _stack;
    internal string? SaveBlocker => _retired ? "source-Player-opacity-retired" : _failure ??
        (!_globals.Available ? "source-Player-opacity-global-writer-unowned" : null) ??
        (_busy ? "source-Player-opacity-child-entered" : null) ?? _scene()?.SaveBlocker ??
        (_historicalScene is not null && _scene() is null ? "source-Player-opacity-cold-model-publication-unbound" : null);
    internal object State => new
    {
        source = Source.Identity,
        process = _process,
        lifetime = _lifetime,
        _sequence,
        globals = _globals,
        call = _call,
        firstPerson = _scene()?.State,
        coldProcess = _restoredProcess,
        coldLifetime = _restoredLifetime,
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };
    internal FalloutActorLogicalSceneSnapshot? PreparedColdScene => _historicalScene;
    internal bool SkipTeleportUpdate(Func<int> readCurrentState)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(readCurrentState);
        return (_globals.FirstGate != 0 || _globals.SecondGate != 0) && readCurrentState() == 0;
    }
    internal IFalloutActorTeleportFadeConsumers BindTeleportConsumers(Guid main, long ordinal, Guid invocation,
        Func<FalloutProcessOpacityQuery> readCurrentProcess, Action<uint> storeEffective)
    {
        RequireIdle(); _requireInvocation(main, ordinal);
        return new Consumers(this, main, ordinal, invocation, readCurrentProcess, storeEffective);
    }
    private sealed class Consumers(FalloutPlayerOpacityState owner, Guid main, long ordinal, Guid invocation,
        Func<FalloutProcessOpacityQuery> read, Action<uint> store) : IFalloutActorTeleportFadeConsumers
    {
        public string Owner => "actual-CSharp-Player-High-teleport-opacity/" + owner._lifetime;
        public Guid Invocation => invocation;
        public uint ReadMainScaledTimerBits() { owner.RequireIdle(); owner._requireInvocation(main, ordinal); return owner._timer(); }
        public uint ReadTeleportSecondsBits(string setting) { owner.RequireIdle(); owner._requireInvocation(main, ordinal); return owner._settings(setting); }
        public void ApplyOpacity() => owner.Apply(main, ordinal, invocation, read, store);
        public void MoveToTarget(FalloutFormKey target) => throw new NotSupportedException("source-teleport-target-reference-placement-child-unowned");
        public bool ReadNpcTeleportCompletion() => throw new NotSupportedException("source-NPC-teleport-completion-actor-child-unowned");
        public void DisableActor() => throw new NotSupportedException("source-teleport-Actor-disable-virtual-unowned");
        public void EnableActor() => throw new NotSupportedException("source-teleport-reference-enable-child-unowned");
    }
    private void Apply(Guid main, long ordinal, Guid invocation, Func<FalloutProcessOpacityQuery> read, Action<uint> store)
    {
        RequireIdle(); _requireInvocation(main, ordinal);
        if (invocation == Guid.Empty || main == Guid.Empty || ordinal < 1 || _call?.Invocation == invocation)
            throw new InvalidOperationException("Player opacity reused an actual source callback invocation.");
        var entered = Next(); _call = new(invocation, main, ordinal, entered, entered, FalloutPlayerOpacityStep.Override,
            null, null, null, false, null, null, null); _busy = true;
        try
        {
            if (_globals.OverrideEnabled != 0 && _globals.OverrideMode >= 2)
                throw new NotSupportedException("source-Actor-opacity-current-extra-data-override-child-unowned");
            Step(FalloutPlayerOpacityStep.ProcessQuery);
            var current = read(); RequireProcess(current); _call = _call with { Process = current, Changed = Next() };
            Step(FalloutPlayerOpacityStep.Product);
            // The High virtual's inherited base cell and teleport cell are
            // current reads after the fade stores, not cached entry arguments.
            var requested = current.Process.Level == FalloutDetectionProcessLevel.High
                ? F.Multiply(Value(current.Opacity.BaseBits), Value(current.Teleport!.FadeBits)) : 1f;
            if (current.Process.Actor.EnginePlayer) requested = F.Multiply(requested, Value(_globals.PlayerAlphaBits));
            var alpha = Bits(requested); _call = _call with { RequestedBits = alpha, Changed = Next() };
            Step(FalloutPlayerOpacityStep.EffectiveStore);
            if (current.Process.Level == FalloutDetectionProcessLevel.High)
            { store(alpha); _call = _call with { EffectiveStored = true, Changed = Next() }; }
            Step(FalloutPlayerOpacityStep.FirstPerson);
            var first = _first();
            if (first is not null)
            {
                var scene = _scene() ?? throw new NotSupportedException("source-current-first-person-logical-scene-unbound");
                RequireFirstPerson(first, scene);
                _call = _call with { FirstPersonFactory = scene.Factory, Changed = Next() };
                Step(FalloutPlayerOpacityStep.SceneAlpha);
                try { _call = _call with { Scene = scene.SetAlpha(invocation, alpha), Changed = Next() }; }
                catch { _call = _call with { Scene = scene.Capture().AlphaCall, Changed = Next() }; throw; }
            }
            else if (_scene() is not null)
                throw new InvalidDataException("A logical scene cannot replace the actual Player constructor-null field.");
            _call = _call with { Step = FalloutPlayerOpacityStep.Returned, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _call = _call! with { FailedAt = _call.Step, Step = FalloutPlayerOpacityStep.Failed, Changed = Next(), Failure = error.Message }; throw;
        }
        finally { _busy = false; }
        void Step(FalloutPlayerOpacityStep step) => _call = _call! with { Step = step, Changed = Next() };
    }
    private void RequireProcess(FalloutProcessOpacityQuery current)
    {
        if (current is null || current.Process.Process == Guid.Empty || current.Process.Stack != _stack ||
            current.Process.ExecutableSha256 != Source.Controls.Main.EngineSha256 || !current.Process.Actor.EnginePlayer ||
            current.Process.Epoch < 1 || current.Process.GameplayOwnership == Guid.Empty ||
            current.Process.Level == FalloutDetectionProcessLevel.High && current.Teleport is null)
            throw new InvalidDataException("Opacity callback changed its real current Player process class, lease or source.");
    }
    private void RequireFirstPerson(FalloutStandaloneFirstPersonOwner first, FalloutActorLogicalScene scene)
    {
        var publication = first.Publication;
        if (first.Released is not null || publication.Process != _process || publication.Stack != _stack ||
            publication.Source != FalloutStandalonePlayerSceneSource.Read(Source.Controls.Main).Identity ||
            publication.Factory != scene.Factory || publication.Resource != scene.SkeletonPath || publication.ResourceSha256 != scene.SkeletonSha256)
            throw new InvalidDataException("Opacity child changed its actual first-person factory, resource or process publication.");
        if (_historicalScene is { } saved && scene.Capture().RestoredLifetime != saved.Lifetime)
            throw new InvalidDataException("Cold opacity bound a constructor scene instead of its retained source fields.");
    }
    internal void RetainUnownedGlobalWriter(string producer)
    { RequireIdle(allowFailure: true); ArgumentException.ThrowIfNullOrWhiteSpace(producer); _globals = _globals with { Available = false }; _failure ??= producer; Next(); }
    internal void Retire()
    { if (_retired) return; RequireIdle(allowFailure: true); _retired = true; Next(); }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireIdle(bool allowFailure = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Player opacity changed its actual gameplay thread.");
        if (_busy) throw new InvalidOperationException("Player opacity cannot reenter its actual source callback.");
        if (!allowFailure && (_failure is not null || !_globals.Available)) throw new NotSupportedException(_failure ?? "source-Player-opacity-global-writer-unowned");
    }
    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    internal FalloutPlayerOpacitySnapshot Capture()
    {
        RequireIdle(allowFailure: true);
        return new(Schema, Source, _stack, _process, _lifetime, _sequence, _globals, _call,
            _scene()?.Capture() ?? _historicalScene, _failure, _retired, _restoredProcess, _restoredLifetime);
    }
    internal static void Validate(FalloutPlayerOpacitySnapshot saved)
    {
        saved.Source.Validate();
        if (saved.Schema != Schema || string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Lifetime == Guid.Empty ||
            saved.Sequence < 0 || saved.Globals is null || saved.Globals.Stores < 0 || saved.Globals.Available && saved.Globals != FalloutPlayerOpacityGlobals.Constructed ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure) || !saved.Globals.Available && saved.Failure is null ||
            (saved.RestoredProcess is null) != (saved.RestoredLifetime is null) ||
            saved.RestoredProcess is { } old && (old == Guid.Empty || old == saved.CapturedProcess) ||
            saved.RestoredLifetime is { } previous && (previous == Guid.Empty || previous == saved.Lifetime))
            throw new InvalidDataException("Cold Player opacity changed independent globals or the actual source/process lifetime.");
        if (saved.FirstPerson is { } model) FalloutActorLogicalScene.Validate(model);
        if (saved.Call is { } call && (call.Invocation == Guid.Empty || call.Main == Guid.Empty || call.MainOrdinal < 1 || call.Entered < 1 ||
            call.Changed < call.Entered || call.Changed > saved.Sequence || !Enum.IsDefined(call.Step) ||
            call.EffectiveStored && call.RequestedBits is null || call.FirstPersonFactory is not null && call.RequestedBits is null ||
            call.Scene is { } scene && (scene.Invocation != call.Invocation || scene.AlphaBits != call.RequestedBits || call.FirstPersonFactory is null) ||
            call.Step == FalloutPlayerOpacityStep.Returned && call.Scene is { Step: not FalloutNifSceneAlphaStep.Returned } ||
            (call.Step == FalloutPlayerOpacityStep.Failed) != (call.Failure is not null) ||
            call.Failure is not null && (call.FailedAt is null || call.Failure != saved.Failure) ||
            call.FailedAt is { } failed && (!Enum.IsDefined(failed) || failed == FalloutPlayerOpacityStep.Failed)))
            throw new InvalidDataException("Cold Player opacity lost its committed High store or actual first-person child prefix.");
    }
    internal static void RequireComplete(FalloutPlayerOpacitySnapshot saved)
    {
        Validate(saved);
        if (saved.Retired || saved.Failure is not null || !saved.Globals.Available || saved.Call is { Step: not FalloutPlayerOpacityStep.Returned })
            throw new InvalidDataException("A complete Player save cannot publish unfinished opacity callbacks.");
        if (saved.FirstPerson is { } model) FalloutActorLogicalScene.RequireComplete(model);
    }
}
