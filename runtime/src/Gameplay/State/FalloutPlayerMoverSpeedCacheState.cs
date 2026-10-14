using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerMoverSpeedCacheCall(FalloutReferenceModelDataInvocation Invocation,
    string ValueSource, FalloutFormKey AssignedBase, string AssignedBaseHash, long Entered, long Changed,
    uint? RightLeg, uint? LeftLeg, int? IgnoreCrippling, uint? SpeedMultiplier,
    uint? Result, bool Returned, string? Failure);
internal sealed record FalloutPlayerMoverSpeedCacheRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, uint Bits);
internal sealed record FalloutPlayerMoverSpeedCacheSnapshot(FalloutPlayerMoverDeltaSource Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence,
    uint Bits, FalloutPlayerMoverSpeedCacheCall? Load, string? WriterBoundary, string? Failure,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);

// A genuine current speed cache. Its producer evaluates the selected loaded
// Player's existing AV pools and the winning settings. It does not seed one,
// obtain speed from a rendered body or substitute the frame's zero movement.
internal sealed class FalloutPlayerMoverSpeedCacheState
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutActorProcessRuntimeState _runtime;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireLoad;
    private readonly Action _requireLifetime;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence, _fault;
    private uint _bits;
    private bool _busy, _retired;
    private string? _failure, _writer;
    private FalloutPlayerMoverSpeedCacheCall? _load;
    private readonly Guid? _previousProcess, _previousFactory;
    internal FalloutPlayerMoverDeltaSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal string? SaveBlocker => _busy ? "source-Player-speed-cache-in-flight" : _failure ?? _writer ??
        (_load is not { Returned: true } ? "source-Player-speed-cache-loaded-AV-producer-unreturned" : null);
    internal FalloutPlayerMoverSpeedCacheState(FalloutPlayerMoverDeltaSource source, FalloutPluginStack records,
        string stack, FalloutActorProcessRuntimeState runtime, Guid constructor, FalloutCombatActorIdentity actor,
        Action<FalloutReferenceModelDataInvocation> requireLoad, Action requireLifetime,
        FalloutPlayerMoverSpeedCacheSnapshot? saved = null)
    {
        source.Validate(); actor.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(runtime);
        var process = runtime.CharacterControllerSourceProcess;
        ArgumentNullException.ThrowIfNull(requireLoad); ArgumentNullException.ThrowIfNull(requireLifetime);
        if (!actor.EnginePlayer || process == Guid.Empty || constructor == Guid.Empty || string.IsNullOrWhiteSpace(stack))
            throw new InvalidDataException("Speed cache needs the actual source Player/constructor/records lifetime.");
        Source = source; _records = records; _runtime = runtime; Stack = stack; Process = process; Constructor = constructor;
        Actor = actor; _requireLoad = requireLoad; _requireLifetime = requireLifetime;
        if (saved is null) return;
        Validate(saved);
        if (saved.Source != source || saved.Stack != stack || saved.Actor != actor || saved.Process == process || saved.Constructor == constructor)
            throw new InvalidDataException("Cold speed cache changed its current source or reused a prior process.");
        _sequence = saved.Sequence; _bits = saved.Bits; _load = saved.Load; _writer = saved.WriterBoundary; _failure = saved.Failure;
        _previousProcess = saved.Process; _previousFactory = saved.Factory;
    }
    internal void PopulateFromLoadedPlayer(FalloutReferenceModelDataInvocation invocation,
        FalloutPlayerActorValues values, FalloutPluginRecord actualAssignedBase)
    {
        RequireIdle(); Check();
        if (_load is not null) throw new InvalidOperationException("Loaded Player speed cannot replay its original source producer.");
        if (values.Source.Player != Actor.Base || values.Source.PlayerSha256 != Actor.BaseSha256 ||
            actualAssignedBase.FormKey != Actor.Base || actualAssignedBase.Signature != "NPC_" ||
            !ReferenceEquals(actualAssignedBase, _records.GetEffective(Actor.Base)))
            throw new InvalidDataException("Speed producer replaced the actual assigned Base field with a winning identity or another AV owner.");
        var fault = _fault; _busy = true; var entered = Next();
        _load = new(invocation, FalloutAdvancementRuntimeReceipt.Hash(System.Text.Json.JsonSerializer.Serialize(values.Source)),
            Actor.Base, Actor.BaseSha256, entered, entered, null, null, null, null, null, false, null);
        try
        {
            Guard(); values.BindLoadedMoverValues(FalloutPlayerMoverActorValueSource.Read(Source, _records), _records, actualAssignedBase);
            Guard(); var right = Limb(values.ReadCurrent(30));
            _load = _load with { RightLeg = Bits(right), Changed = Next() }; Guard();
            var left = Limb(values.ReadCurrent(29));
            _load = _load with { LeftLeg = Bits(left), Changed = Next() }; Guard();
            var disabled = values.ReadMoverDeltaIgnoreCrippling();
            _load = _load with { IgnoreCrippling = disabled, Changed = Next() }; Guard();
            var speed = values.ReadCurrent(21);
            _load = _load with { SpeedMultiplier = Bits(speed), Changed = Next() }; Guard();
            var result = LoadedSpeed(right, left, disabled, speed,
                name => FalloutGameSettingFloats.ReadRetained(_records, name, nameof(FalloutPlayerMoverSpeedCacheState)));
            _bits = Bits(result); _load = _load with { Result = _bits, Changed = Next() }; Guard();
            _load = _load with { Returned = true, Changed = Next() };
        }
        catch (Exception error) { _failure ??= error.Message; _load = _load! with { Failure = _failure, Changed = Next() }; throw; }
        finally { _busy = false; }
        void Check()
        {
            _requireLoad(invocation); _requireLifetime();
            if (invocation.Actor != Actor || _runtime.CharacterControllerSourceProcess != Process || invocation.ReferenceProcess == Guid.Empty || invocation.ReferenceConstructor != Constructor ||
                invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.ThreadId != _thread || invocation.Call == Guid.Empty)
                throw new InvalidOperationException("Speed producer lost its genuine entered loaded-Player/AV/source caller.");
        }
        void Guard() { Check(); if (_fault != fault) throw new InvalidOperationException("Speed producer swallowed actual AV callback reentry."); }
    }
    internal FalloutPlayerMoverSpeedCacheRead ReadCurrent(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireIdle(); _requireLifetime(); requireActual(invocation);
        if (_runtime.CharacterControllerSourceProcess != Process || invocation.PlayerProcess == Guid.Empty || invocation.PlayerConstructor != Constructor ||
            invocation.Common.Actor != Actor || invocation.ThreadId != _thread)
            throw new InvalidOperationException("Controller delta borrowed another actual shared Player speed cache.");
        if (SaveBlocker is { } boundary) throw new NotSupportedException(boundary);
        return new(Source.Identity, Stack, Process, Factory, Constructor, Actor, _sequence, _bits);
    }
    internal void ObserveUnownedWriter(string boundary)
    { RequireIdle(); ArgumentException.ThrowIfNullOrWhiteSpace(boundary); _writer ??= boundary; Next(); }
    internal FalloutPlayerMoverSpeedCacheSnapshot Capture()
    {
        RequireIdle(); var saved = new FalloutPlayerMoverSpeedCacheSnapshot(Source, Stack, Process, Factory, Constructor,
            Actor, _sequence, _bits, _load, _writer, _failure, _previousProcess, _previousFactory); Validate(saved); return saved;
    }
    internal void Retire() { if (_retired) return; RequireIdle(); _retired = true; }
    internal static float Limb(float current) => current > 0 ? 1 : 0;
    internal static float LoadedSpeed(float right, float left, int disabled, float speed, Func<string, float> setting)
    {
        var lost = (float)((1d - right) + (1d - left)); var limb = lost == 1 ? setting("fMoveOneCrippledLegSpeedMult") :
            lost == 2 ? setting("fMoveTwoCrippledLegsSpeedMult") : 1f;
        if (disabled != 0) limb = 1;
        var percentage = (float)((double)speed * .01d);
        var baseSpeed = FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Multiply(setting("fMoveBaseSpeed"), percentage), limb);
        var fraction = FalloutPhysicalFloat32.Divide(setting("fMoveWeightMin"),
            FalloutPhysicalFloat32.Subtract(setting("fMoveWeightMax"), setting("fMoveWeightMin")));
        var encumbrance = FalloutPhysicalFloat32.Multiply(fraction, setting("fMoveEncumEffect"));
        var walk = FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(1, encumbrance), baseSpeed);
        if (0 > walk) walk = 0;
        return (float)((double)setting("fMoveRunMult") * (float)((double)walk + 0d));
    }
    internal static void Validate(FalloutPlayerMoverSpeedCacheSnapshot saved)
    {
        saved.Source.Validate(); saved.Actor.Validate();
        if (saved.Process == Guid.Empty || saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Sequence < 0 ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.Failure is { } error && string.IsNullOrWhiteSpace(error) ||
            saved.WriterBoundary is { } writer && string.IsNullOrWhiteSpace(writer) ||
            (saved.PreviousProcess is null) != (saved.PreviousFactory is null) || saved.PreviousProcess == saved.Process ||
            saved.Load is { } load && (load.Invocation.Call == Guid.Empty || load.Invocation.Actor != saved.Actor ||
                load.AssignedBase != saved.Actor.Base || load.AssignedBaseHash != saved.Actor.BaseSha256 ||
                !FalloutAdvancementRuntimeReceipt.Digest(load.ValueSource) || load.Entered < 1 || load.Changed < load.Entered || load.Changed > saved.Sequence ||
                load.Returned != (load.Failure is null) || load.Returned && (load.RightLeg is null || load.LeftLeg is null ||
                    load.IgnoreCrippling is null || load.SpeedMultiplier is null || load.Result != saved.Bits) ||
                load.Failure != saved.Failure) || saved.Load is null && (saved.Bits != 0 || saved.Failure is not null))
            throw new InvalidDataException("Speed cache lost its actual loaded AV result, mutable cache or failed/cold prefix.");
    }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy || _thread != Environment.CurrentManagedThreadId) { ++_fault; throw new InvalidOperationException("Speed cache reentered or changed its actual source thread."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}
