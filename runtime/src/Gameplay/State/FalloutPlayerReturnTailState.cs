using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// A bounded current-call owner for the source work after returned mover delta.
// Timer and bracket stores remain committed if an independent child fails.
internal sealed partial class FalloutPlayerReturnTailState
{
    private readonly FalloutPlayerReturnTailOwners _owners;
    private readonly Action<FalloutPlayerLateFrameInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly List<FalloutPlayerReturnTailPublication> _publications = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence, _calls, _fault;
    private bool _busy, _retired;
    private uint _deathTimer = FalloutPlayerReturnTailSource.InitialDeathTimerBits;
    private string? _deathWriter, _failure;
    internal FalloutPlayerReturnTailSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Constructor { get; }
    internal Guid CommonProcess { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutPlayerReturnTailCall? LastCall { get; private set; }
    internal string? SaveBlocker => _retired ? "source-Player-return-tail-retired" :
        _busy ? "source-Player-return-tail-entered" : _failure ?? _deathWriter;

    internal FalloutPlayerReturnTailState(FalloutPlayerReturnTailSource source, string stack,
        Guid process, Guid constructor, Guid commonProcess, FalloutPlayerReturnTailOwners owners,
        Action<FalloutPlayerLateFrameInvocation> requireActual, Action requireLifetime,
        FalloutPlayerReturnTailSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(owners); ArgumentNullException.ThrowIfNull(requireActual);
        ArgumentNullException.ThrowIfNull(requireLifetime);
        if (process == Guid.Empty || constructor == Guid.Empty || commonProcess == Guid.Empty ||
            owners.Fields.Process != commonProcess || owners.Fields.Constructor != constructor ||
            owners.Fields.Stack != stack || owners.Fields.Source != source.Movement.Mover.Controller.Fields ||
            !owners.Fields.Actor.EnginePlayer || owners.ReferenceExtras.FieldsFactory != owners.Fields.Factory)
            throw new InvalidDataException("Player tail lacks the actual Scene, Common, reference and source owners.");
        Source = source; Stack = stack; Process = process; Constructor = constructor; CommonProcess = commonProcess;
        _owners = owners; _requireActual = requireActual; _requireLifetime = requireLifetime; requireLifetime();
        owners.ReferenceExtras.RequireActualAnimationRecords(owners.Records);
        if (saved is not null)
        {
            Validate(saved);
            if (saved.Source != source || saved.Stack != stack || saved.Process == process || saved.Factory == Factory ||
                saved.Constructor == constructor || saved.CommonProcess == commonProcess || saved.Failure is not null)
                throw new InvalidDataException("Cold Player tail reused its actual lifetime or an unreturned bracket.");
            _deathTimer = saved.DeathTimerBits;
            _deathWriter = saved.DeathTimerWriter;
            _sequence = saved.Sequence; _calls = saved.Calls; LastCall = saved.LastCall;
            _publications.AddRange(saved.Publications);
        }
        _publications.Add(new(process, Factory, constructor, commonProcess, _sequence, saved?.Process, saved?.Factory));
    }

    internal FalloutPlayerReturnTailCall UpdateThroughAnimation(FalloutPlayerLateFrameInvocation invocation)
    {
        Enter(invocation); var fault = _fault; _busy = true;
        LastCall = new(invocation, Factory, checked(++_calls), Next(), _sequence,
            FalloutPlayerReturnTailStep.DeathDelay, null, null, null, [], null, [], [], null, null, false, null);
        try
        {
            var delay = ReadSetting(FalloutPlayerReturnTailSource.DeathDelayName); Check();
            LastCall = LastCall with { Delay = delay, Changed = Next() };
            Step(FalloutPlayerReturnTailStep.RawLife);
            var raw = _owners.Runtime.Life(invocation.Common.Actor.Reference);
            LastCall = LastCall with { RawLife = raw, Changed = Next() }; Check();
            var life = raw.Require();
            if (BitConverter.UInt32BitsToSingle(delay.Bits) > 0)
            {
                Step(FalloutPlayerReturnTailStep.DeathTimer);
                if (life is not (1 or 2))
                {
                    // This actual overwrite does not borrow the old value.
                    // An old unknown timer writer cannot prohibit this source
                    // store. Its earlier history remains independently visible.
                    Store(FalloutPlayerReturnTailField.DeathTimer, FalloutPlayerReturnTailSource.InitialDeathTimerBits);
                    _deathWriter = null;
                }
                else
                {
                    if (_deathWriter is { } unavailable) throw new NotSupportedException(unavailable);
                    Step(FalloutPlayerReturnTailStep.DeathChild);
                    if (BitConverter.UInt32BitsToSingle(_deathTimer) < 0)
                        throw new NotSupportedException("source-Player-death-menu-return-and-winning-delay-reread-unowned");
                    var next = BitConverter.UInt32BitsToSingle(_deathTimer) - BitConverter.UInt32BitsToSingle(invocation.ArgumentBits);
                    Store(FalloutPlayerReturnTailField.DeathTimer, BitConverter.SingleToUInt32Bits(next));
                    if (next < 0)
                    {
                        Store(FalloutPlayerReturnTailField.DeathTimer, FalloutPlayerReturnTailSource.InitialDeathTimerBits);
                        throw new NotSupportedException("source-Player-death-expiry-menu-query-and-interface-byte-writer-unowned");
                    }
                }
            }
            else if (life == 2)
            {
                Step(FalloutPlayerReturnTailStep.DeathChild);
                throw new NotSupportedException("source-Player-immediate-death-once-byte-menu-audio-dialog-owner-unbound");
            }

            Step(FalloutPlayerReturnTailStep.AnimationBracketEnter);
            Bracket(enter: true);
            Step(FalloutPlayerReturnTailStep.AnimationClock);
            var clock = _owners.ReadAnimationClock(invocation, Factory); Check();
            RequireClock(clock, invocation); LastCall = LastCall with { Clock = clock, Changed = Next() };

            Step(FalloutPlayerReturnTailStep.CommonAnimation);
            var common = _owners.Common.ReadAnimationContextForReturnTail(invocation, CheckFrame);
            AddAnimation(new(invocation, common, null));
            UpdateContext(common.AnimationContext);

            Step(FalloutPlayerReturnTailStep.FirstAnimation);
            var first = _owners.Fields.ReadMoverDeltaFirstAnimation(invocation, CheckFrame);
            AddAnimation(new(invocation, null, first)); UpdateContext(first.Context);

            Step(FalloutPlayerReturnTailStep.ProcessAnimationQueue);
            var queue = _owners.Common.UpdatePlayerAnimationPairs(Source, invocation, clock, _owners.Records, CheckFrame);
            LastCall = LastCall with { Queue = queue, Changed = Next() }; Check();
            Step(FalloutPlayerReturnTailStep.ReferenceAnimationExtra);
            var extra = _owners.ReferenceExtras.ReadPlayerAnimationNodeExtra(Source, invocation, CheckFrame);
            LastCall = LastCall with { Extra = extra, Changed = Next() }; Check();
            if (extra.Payload is not null)
                throw new NotSupportedException("source-Player-Extra41-current-node-flags-and-retained-view-swap-unowned");
            Step(FalloutPlayerReturnTailStep.AnimationBracketLeave);
            Bracket(enter: false);
            Step(FalloutPlayerReturnTailStep.Returned);
            LastCall = LastCall with { Returned = true, Changed = Next() }; return LastCall;
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutPlayerReturnTailStep.Failed,
                Failure = _failure,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }

        void AddAnimation(FalloutPlayerAnimationPointerRead read)
        { LastCall = LastCall! with { Animations = [.. LastCall!.Animations, read], Changed = Next() }; Check(); }
        void UpdateContext(Guid? context)
        {
            if (context is null) return;
            var owner = _owners.RequireAnimationContext(context.Value); Check();
            if (owner.Identity != context || owner.Actor != invocation.Common.Actor || owner.Process != Process ||
                owner.CommonProcess != CommonProcess || owner.Source != Source)
                throw new InvalidDataException("Player animation selected a foreign actual AnimData factory.");
            try { owner.Update(invocation, clockDelta(), CheckFrame); }
            finally
            {
                if (owner.LastCall is { } call && call.Frame == invocation)
                    LastCall = LastCall! with { ContextUpdates = [.. LastCall!.ContextUpdates, call], Changed = Next() };
            }
            uint clockDelta() => LastCall!.Clock!.DeltaBits;
        }
        void Step(FalloutPlayerReturnTailStep step) { Check(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
        void Store(FalloutPlayerReturnTailField field, uint value)
        {
            Check(); uint? before = _deathWriter is null ? _deathTimer : null; _deathTimer = value;
            var write = new FalloutPlayerReturnTailStore(field, Next(), before, value);
            LastCall = LastCall! with { Stores = [.. LastCall!.Stores, write], Changed = _sequence }; Check();
        }
        void Bracket(bool enter)
        {
            Check(); var write = _owners.StoreAnimationBracket(invocation, Factory, FalloutPlayerAnimationBracketChild.Updater, enter);
            Check();
            if (write.Frame != invocation || write.Source != Source.Scene.Identity || write.SceneProcess != Process ||
                write.SceneConstructor != Constructor || write.ConsumerFactory != Factory || write.Child != FalloutPlayerAnimationBracketChild.Updater ||
                write.Enter != enter || write.Changed < 1 || write.After != unchecked((byte)(write.Before + (enter ? 1 : -1))))
                throw new InvalidDataException("Animation updater replaced the same Scene bracket store/current caller.");
            LastCall = LastCall! with
            {
                Stores = [.. LastCall!.Stores,
                new(FalloutPlayerReturnTailField.AnimationBracket, Next(), write.Before, write.After, write)],
                Changed = _sequence
            };
        }
        void CheckFrame(FalloutPlayerLateFrameInvocation actual)
        { if (actual != invocation) throw new InvalidOperationException("Animation child changed its entered late Player frame."); Check(); }
        void Check()
        {
            RequireFrame(invocation);
            if (_fault != fault) throw new InvalidOperationException("Player return tail swallowed a real owner reentry.");
        }
    }

    private FalloutPlayerTailSettingRead ReadSetting(string name)
    {
        var load = _owners.Records.LoadReceipt; _owners.Records.RequireLiveLoadReceipt(load);
        var value = _owners.Records.NumericSettings.Float(name);
        if (!float.IsFinite(value)) throw new NotSupportedException("source-Player-tail-nonfinite-winning-setting-unowned:" + name);
        _owners.Records.RequireLiveLoadReceipt(load);
        return new(name, BitConverter.SingleToUInt32Bits(value), load.Factory, load.SourceHash);
    }
    private void Enter(FalloutPlayerLateFrameInvocation frame)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Player return tail is already entered."); }
        RequireFrame(frame);
        if (_failure is not null || LastCall is { Returned: false } ||
            LastCall is { } previous && previous.Invocation.PlayerProcess == Process && frame.Caller.Ordinal <= previous.Invocation.Caller.Ordinal)
            throw new InvalidOperationException("Player tail replayed a failed or already consumed source frame.");
    }
    private void RequireFrame(FalloutPlayerLateFrameInvocation frame)
    {
        _requireLifetime(); _requireActual(frame);
        if (_retired || Environment.CurrentManagedThreadId != _thread || frame.ThreadId != _thread ||
            frame.PlayerProcess != Process || frame.PlayerConstructor != Constructor || frame.Common.Process != CommonProcess ||
            frame.Common.Stack != Stack || frame.Common.Actor != _owners.Fields.Actor || frame.Common.Epoch != _owners.Fields.Epoch ||
            frame.Common.GameplayOwnership == Guid.Empty || frame.Caller.Main == Guid.Empty || frame.Caller.Ordinal < 1 ||
            frame.Caller.Invocation == Guid.Empty || frame.Prelude == Guid.Empty)
            throw new InvalidOperationException("Player return tail escaped its actual source, Main, Actor, epoch or current thread.");
    }
    internal void ObserveUnownedSharedWriter(FalloutPlayerReturnTailField field, string producer, Action requireActualWriter)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Shared late field writer reentered Player tail."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(producer); ArgumentNullException.ThrowIfNull(requireActualWriter);
        _requireLifetime(); requireActualWriter();
        if (_retired || Environment.CurrentManagedThreadId != _thread || field != FalloutPlayerReturnTailField.DeathTimer)
            throw new InvalidOperationException("Shared tail writer changed its actual field/thread.");
        var boundary = "source-Player-shared-late-field-writer-unowned:" + field + ":" + producer;
        _deathWriter = boundary;
        Next();
    }
    internal FalloutPlayerReturnTailSnapshot Capture() => new(Source, Stack, Process, Factory, Constructor,
        CommonProcess, _sequence, _calls, _deathTimer, _deathWriter,
        _publications.ToArray(), LastCall, _failure);
    internal void Retire()
    {
        if (_busy || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Player return tail cannot retire its actual entered call or foreign thread.");
        _retired = true;
    }
    private long Next() => checked(++_sequence);
    private static void RequireClock(FalloutPlayerAnimationClockRead clock, FalloutPlayerLateFrameInvocation frame)
    {
        if (clock.Frame != frame || clock.SceneProcess != frame.PlayerProcess || clock.SceneConstructor != frame.PlayerConstructor ||
            clock.SceneSequence < 0 || clock.Kind == 4 || clock.NumeratorBits != FalloutStandalonePlayerSceneSource.One ||
            clock.DenominatorBits != FalloutStandalonePlayerSceneSource.One || clock.Timer.Process != frame.PlayerProcess ||
            clock.Timer.ScaledSecondsBits != clock.DeltaBits || clock.Timer.ScaledSecondsBits != frame.ArgumentBits)
            throw new InvalidDataException("Player animation clock replaced its actual ratio/current cached Main delta.");
    }
}
