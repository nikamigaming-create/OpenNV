using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// Owned by the actual Player scene. Only four missing inherited Actor/Mobile
// bytes live here; the scalar/word remain the scene's existing cells. Common,
// flags, calendar, timer and global-AI are borrowed from their current owners.
internal sealed partial class FalloutPlayerLateActorTimeState
{
    internal const string MobileBoundary = "source-Mobile-zero-one-three-current-update-children-unbound";
    internal const string WeekdayBoundary = "source-Actor-time-current-weekday-reference-Extra-consumer-unbound";
    internal const string ValueBoundary = "source-Actor-time-nonzero-tier-or-travel-value-modifier-update-unbound";
    internal const string AiBoundary = "source-Actor-time-current-AI-package-date-and-task-children-unbound";
    internal const string DeathBoundary = "source-Actor-time-current-death-process-and-timer-children-unbound";
    internal const string ActorValueBoundary = "source-Actor-values-false-kind-fifty-current-effect-consumers-unbound";
    private readonly FalloutPlayerLateActorTimeSource _source;
    private readonly string _stack;
    private readonly Guid _playerProcess, _constructor, _factory = Guid.NewGuid();
    private readonly FalloutCombatActorIdentity _actor;
    private readonly long _epoch;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly FalloutActorProcessCommonState _common;
    private readonly FalloutActorProcessRuntimeState _runtime;
    private readonly FalloutActorUpdateState _updates;
    private readonly FalloutReferenceScriptExtraInitializer _reference;
    private readonly FalloutGameTime _calendar;
    private readonly FalloutSourceFrameTimerState _timer;
    private readonly FalloutNumericIniSettings _settings;
    private readonly Func<FalloutPlayerSceneScalarFields> _readScalars;
    private readonly Action<FalloutPlayerSceneScalarFields, Action> _storeScalars;
    private readonly Func<uint> _readClockKind;
    private readonly Action<FalloutPlayerLateFrameInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly List<FalloutPlayerLateActorPublication> _publications = [];
    private FalloutPlayerLateActorFields _fields = FalloutPlayerLateActorFields.Constructed;
    private long _sequence, _calls, _fault;
    private bool _busy;
    private string? _writerBoundary, _failure;
    internal FalloutPlayerLateActorTimeCall? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-Actor-time-child-entered" : _writerBoundary ?? _failure;

    internal FalloutPlayerLateActorTimeState(FalloutPlayerLateActorTimeSource source, string stack,
        Guid playerProcess, Guid constructor, FalloutProcessSceneQuery current,
        FalloutActorProcessCommonState common, FalloutActorProcessRuntimeState runtime,
        FalloutActorUpdateState updates, FalloutReferenceScriptExtraInitializer reference,
        FalloutGameTime calendar, FalloutSourceFrameTimerState timer, FalloutNumericIniSettings settings,
        Func<FalloutPlayerSceneScalarFields> readScalars, Action<FalloutPlayerSceneScalarFields, Action> storeScalars,
        Func<uint> readClockKind, Action<FalloutPlayerLateFrameInvocation> requireActual,
        Action requireLifetime, FalloutPlayerLateActorSnapshot? saved = null)
    {
        source.Validate(); current.Actor.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(common); ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(updates); ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(calendar); ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(readScalars);
        ArgumentNullException.ThrowIfNull(storeScalars); ArgumentNullException.ThrowIfNull(readClockKind);
        ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(requireLifetime);
        requireLifetime();
        if (playerProcess == Guid.Empty || constructor == Guid.Empty || !current.Actor.EnginePlayer ||
            current.ExecutableSha256 != source.Following.Controls.Main.EngineSha256 || current.Stack != stack ||
            current.Epoch < 1 || reference.Actor != current.Actor || reference.Process != current.Process ||
            reference.Epoch != current.Epoch || reference.Constructor != constructor ||
            reference.Source.Fields.Controls.Main != source.Following.Controls.Main)
            throw new InvalidDataException("Actor-time child changed its actual Actor/Common/Scene constructor or source.");
        _source = source; _stack = stack; _playerProcess = playerProcess; _constructor = constructor;
        _actor = current.Actor; _epoch = current.Epoch; _common = common; _runtime = runtime;
        _updates = updates; _reference = reference; _calendar = calendar; _timer = timer; _settings = settings;
        _readScalars = readScalars; _storeScalars = storeScalars; _readClockKind = readClockKind;
        _requireActual = requireActual; _requireLifetime = requireLifetime;
        if (saved is not null)
        {
            Validate(saved);
            if (saved.Source != source || saved.Stack != stack || saved.Actor != _actor || saved.Epoch != _epoch ||
                saved.PlayerProcess == playerProcess || saved.Factory == _factory || saved.PlayerConstructor == constructor)
                throw new InvalidDataException("Cold Actor-time child replaced source/Actor or reused a retired lifetime.");
            _sequence = saved.Sequence; _calls = saved.Calls; _fields = saved.Fields;
            _failure = saved.Failure; _writerBoundary = saved.WriterBoundary; LastCall = Clone(saved.LastCall);
            _publications.AddRange(saved.Publications);
            _publications.Add(new(playerProcess, _factory, constructor, current.Process, _actor, _epoch,
                Next(), saved.PlayerProcess, saved.Factory));
        }
        else _publications.Add(new(playerProcess, _factory, constructor, current.Process, _actor, _epoch, 0));
    }

    // Every reached writer outside this source child must invalidate before
    // its callback. Root must wire the actual writer footprint; absence of a
    // Godot node or sparse managed code is not preceding-writer evidence.
    internal void ObserveUnownedActorWriter(string producer)
    {
        RequireIdle(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); _requireLifetime();
        _writerBoundary ??= producer; Next();
    }

    internal FalloutPlayerLateActorTimeCall Update(FalloutPlayerLateFrameInvocation invocation)
    {
        RequireIdle(); RequireInvocation(invocation);
        if ((_writerBoundary ?? _failure) is { } blocked) throw new NotSupportedException(blocked);
        if (LastCall is { } previous && (!previous.Returned || previous.Invocation.Caller.Main == invocation.Caller.Main ||
            previous.Invocation.Caller.Ordinal >= invocation.Caller.Ordinal))
            throw new InvalidOperationException("Actor-time child replayed or crossed an unreturned source call.");
        var scalars = _readScalars(); var stores = new List<FalloutPlayerLateActorStore>(4);
        var lives = new List<FalloutPlayerFollowingLifeRead>(3); var entered = Next(); var fault = _fault;
        _calls = checked(_calls + 1); _busy = true;
        LastCall = new(_source.Identity, _stack, _factory, invocation, _calls, entered, entered,
            FalloutPlayerLateActorTimeStep.ReferenceFlags, null, _fields, _fields, scalars, scalars, []);
        try
        {
            Guard(); var flags = _reference.ReadFrameReferenceFlags(invocation, RequireInvocation);
            LastCall = LastCall with { ReferenceFlags = flags, Changed = Next() }; Guard();
            if ((flags.Flags & FalloutPlayerLateActorTimeSource.ReferenceNoUpdateFlag) != 0) return Return();
            Step(FalloutPlayerLateActorTimeStep.ActorUpdate);
            var update = _updates.Read(_actor.Reference);
            LastCall = LastCall with { ActorUpdate = update, Changed = Next() }; Guard();
            if (!update.Require()) return Return();
            Step(FalloutPlayerLateActorTimeStep.MobileUpdate);
            if (_fields.MobileState is 0 or 1 or 3) throw new NotSupportedException(MobileBoundary);
            Store(FalloutPlayerLateActorField.MobileState, _fields.MobileState, byte.MaxValue,
                () => _fields = _fields with { MobileState = byte.MaxValue });
            // This component is bound to the real selected engine Player. Its
            // source predicate returns false before the nonplayer queries.
            Step(FalloutPlayerLateActorTimeStep.SelfPredicate);
            Store(FalloutPlayerLateActorField.SelfPredicate, _fields.SelfPredicate, 0,
                () => _fields = _fields with { SelfPredicate = 0 });
            Step(FalloutPlayerLateActorTimeStep.MainTimer);
            var kind = _readClockKind(); Guard();
            var timer = _timer.ReadScaledDelta(new(FalloutSourceFrameDeltaReader.ActorTime, _factory,
                invocation.Caller.Invocation, invocation.Caller.Main, invocation.Caller.Ordinal, invocation.PlayerProcess));
            LastCall = LastCall with { ClockKind = kind, Timer = timer, Changed = Next() }; Guard();
            if (timer.Process != _playerProcess || timer.Source != _source.Following.Controls.Main.EngineSha256)
                throw new InvalidDataException("Actor-time selected another current Main scalar owner.");
            if (kind == 4)
                throw new NotSupportedException("source-Actor-time-kind-four-target-and-positive-scalar-owner-unbound");
            Step(FalloutPlayerLateActorTimeStep.Scalar);
            var currentScalars = _readScalars(); Guard();
            if (currentScalars.SubtractsScaledTimer)
            {
                var result = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(currentScalars.ActorRemainingBits) -
                    BitConverter.UInt32BitsToSingle(timer.ScaledSecondsBits));
                StoreScalar(FalloutPlayerLateActorField.RemainingScalar, currentScalars.ActorRemainingBits, result,
                    currentScalars with { ActorRemainingBits = result });
            }
            else StoreScalar(FalloutPlayerLateActorField.ScalarWord, currentScalars.ActorWord, 0,
                currentScalars with { ActorWord = 0 });
            Step(FalloutPlayerLateActorTimeStep.FirstLife);
            if (!Life().Result!.Value)
            {
                Step(FalloutPlayerLateActorTimeStep.PackedDay);
                var packed = _common.ReadPlayerFramePackedDay(invocation, RequireInvocation);
                LastCall = LastCall with { PackedDay = packed, Changed = Next() }; Guard();
                Step(FalloutPlayerLateActorTimeStep.CalendarDay);
                var day = _calendar.ReadActorCalendarDay(invocation, RequireInvocation);
                LastCall = LastCall with { CalendarDay = day, Changed = Next() }; Guard();
                Step(FalloutPlayerLateActorTimeStep.DateMismatch);
                var mismatch = packed.Day != day.SignedDay;
                if (mismatch && _fields.DateMismatch == 0) throw new NotSupportedException(WeekdayBoundary);
                Store(FalloutPlayerLateActorField.DateMismatch, _fields.DateMismatch, mismatch ? 1u : 0u,
                    () => _fields = _fields with { DateMismatch = mismatch ? (byte)1 : (byte)0 });
            }
            Step(FalloutPlayerLateActorTimeStep.Tier);
            var process = _common.ReadSceneQueries(_actor.Reference, _epoch); RequireSameProcess(invocation, process); Guard();
            if (process.Level != FalloutDetectionProcessLevel.High) throw new NotSupportedException(ValueBoundary);
            Step(FalloutPlayerLateActorTimeStep.Travel);
            var travel = _runtime.PlayerTravelCounter;
            LastCall = LastCall with { Travel = travel, Changed = Next() }; Guard();
            if (travel.Require() > 0) throw new NotSupportedException(ValueBoundary);
            Step(FalloutPlayerLateActorTimeStep.SecondLife);
            if (!Life().Result!.Value)
            {
                Step(FalloutPlayerLateActorTimeStep.AiBlocked);
                if (_fields.AiBlocked == 0)
                {
                    Step(FalloutPlayerLateActorTimeStep.GlobalAi);
                    var ai = _updates.ReadPlayerFrameGlobalAi(invocation, RequireInvocation);
                    LastCall = LastCall with { GlobalAi = ai, Changed = Next() }; Guard();
                    if (ai.Boundary is not null) throw new NotSupportedException(ai.Boundary);
                    if (ai.Value != 0)
                    {
                        Step(FalloutPlayerLateActorTimeStep.RawLife);
                        var raw = _runtime.Life(_actor.Reference);
                        LastCall = LastCall with { RawLife = raw, Changed = Next() }; Guard();
                        if (raw.Require() != 6) throw new NotSupportedException(AiBoundary);
                    }
                }
            }
            Step(FalloutPlayerLateActorTimeStep.ThirdLife);
            if (Life().Result!.Value) throw new NotSupportedException(DeathBoundary);
            Step(FalloutPlayerLateActorTimeStep.HardwareThreads);
            var threads = _settings.Find(FalloutIniCollection.Main,
                FalloutPlayerLateActorTimeSource.HardwareThreadsSetting) ??
                throw new NotSupportedException("Actor-time hardware-thread-count has no actual selected INI declaration.");
            LastCall = LastCall with { HardwareThreads = threads, Changed = Next() }; Guard();
            if (threads.Declaration.Kind != 'i' || threads.Number is not { } count ||
                !double.IsFinite(count) || count is < int.MinValue or > int.MaxValue || count != Math.Truncate(count))
                throw new InvalidDataException("Actor-time changed its actual signed hardware-thread-count declaration.");
            if (count == 1)
            {
                // Source High raw tier is zero. The selected Interface leaf is
                // pure false. Its value call still reads the real flag owner.
                Step(FalloutPlayerLateActorTimeStep.ActorValues);
                var valueFlags = _reference.ReadFrameReferenceFlags(invocation, RequireInvocation);
                LastCall = LastCall with { ActorValueFlags = valueFlags, Changed = Next() }; Guard();
                if ((valueFlags.Flags & FalloutPlayerLateActorTimeSource.ActorConstructionFlag) == 0)
                    throw new NotSupportedException(ActorValueBoundary);
            }
            // The final correction compares with the selected Player singleton
            // before any spatial getter. This scope is that actual Player.
            return Return();
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            LastCall = LastCall! with
            {
                FailedAt = LastCall.Step,
                Step = FalloutPlayerLateActorTimeStep.Failed,
                Failure = _failure,
                After = _fields,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }

        void Guard()
        {
            RequireInvocation(invocation);
            if (_fault != fault) throw new InvalidOperationException("Actor-time swallowed a forbidden field-owner reentry.");
        }
        void Step(FalloutPlayerLateActorTimeStep step)
        { Guard(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
        void Store(FalloutPlayerLateActorField field, uint before, uint after, Action write)
        {
            Guard(); write(); var change = Next(); stores.Add(new(field, change, before, after));
            LastCall = LastCall! with { After = _fields, Stores = Array.AsReadOnly(stores.ToArray()), Changed = change }; Guard();
        }
        void StoreScalar(FalloutPlayerLateActorField field, uint before, uint after, FalloutPlayerSceneScalarFields value)
        {
            Guard(); var committed = false;
            _storeScalars(value, () =>
            {
                // The existing scalar owner calls this at its real committed
                // write, before its later guard/callback can fail. Equality of
                // bits cannot establish that an unchanged source store ran.
                if (committed) throw new InvalidDataException("Scene scalar owner duplicated a source store receipt.");
                committed = true; var changed = Next(); stores.Add(new(field, changed, before, after));
                LastCall = LastCall! with
                {
                    AfterScalars = value,
                    Stores = Array.AsReadOnly(stores.ToArray()),
                    Changed = changed
                }; Guard();
            });
            if (!committed) throw new InvalidDataException("Scene scalar owner omitted its real committed source store.");
            var actual = _readScalars();
            LastCall = LastCall! with { AfterScalars = actual, Changed = Next() }; Guard();
            if (actual != value) throw new InvalidDataException("Actor-time scalar store did not commit to the existing Scene cells.");
        }
        FalloutPlayerFollowingLifeRead Life()
        {
            var process = _common.ReadSceneQueries(_actor.Reference, _epoch); RequireSameProcess(invocation, process);
            var raw = _runtime.Life(_actor.Reference);
            var life = new FalloutPlayerFollowingLifeRead(process, raw, false,
                raw.Value is { } value && raw.Failure is null ? FalloutPlayerFollowingContinuation.LifePredicate(value, false) : null);
            lives.Add(life); LastCall = LastCall! with { Lives = Array.AsReadOnly(lives.ToArray()), Changed = Next() }; Guard();
            _ = raw.Require();
            return life;
        }
        FalloutPlayerLateActorTimeCall Return()
        {
            Guard(); LastCall = LastCall! with
            {
                Step = FalloutPlayerLateActorTimeStep.Returned,
                Returned = true,
                Changed = Next()
            }; return Clone(LastCall)!;
        }
    }

    private void RequireInvocation(FalloutPlayerLateFrameInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation); _requireLifetime(); _requireActual(invocation);
        if (invocation.PlayerProcess != _playerProcess || invocation.PlayerConstructor != _constructor ||
            invocation.Prelude == Guid.Empty || invocation.Caller.Main == Guid.Empty ||
            invocation.Caller.Ordinal < 1 || invocation.Caller.Invocation == Guid.Empty ||
            invocation.ThreadId != _thread || _thread != Environment.CurrentManagedThreadId ||
            invocation.Common.Actor != _actor || invocation.Common.Epoch != _epoch ||
            invocation.Common.Stack != _stack || invocation.Common.ExecutableSha256 != _source.Following.Controls.Main.EngineSha256)
            throw new InvalidOperationException("Actor-time child escaped its actual Main/Prelude/Actor/Common/Scene/thread scope.");
        RequireSameProcess(invocation, _common.ReadSceneQueries(_actor.Reference, _epoch));
    }
    internal static void RequireSameProcess(FalloutPlayerLateFrameInvocation invocation, FalloutProcessSceneQuery query)
    {
        var entered = invocation.Common;
        if (query.Process != entered.Process || query.Actor != entered.Actor || query.Epoch != entered.Epoch ||
            query.GameplayOwnership != entered.GameplayOwnership || query.Level != entered.Level ||
            query.Stack != entered.Stack || query.ExecutableSha256 != entered.ExecutableSha256 || query.Changed < entered.Changed)
            throw new InvalidDataException("Actor-time getter changed its actual Common process, class, Actor, epoch or lease.");
    }
    private void RequireIdle()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Actor-time owner changed product thread.");
        if (_busy) { _fault++; throw new InvalidOperationException("Actor-time field owner reentered."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static FalloutPlayerLateActorTimeCall? Clone(FalloutPlayerLateActorTimeCall? value) => value is null ? null :
        value with { Stores = Array.AsReadOnly(value.Stores.ToArray()), Lives = value.Lives is null ? null : Array.AsReadOnly(value.Lives.ToArray()) };
    internal FalloutPlayerLateActorSnapshot Capture()
    {
        RequireIdle();
        return new(_source, _stack, _playerProcess, _factory, _constructor, _actor, _epoch, _sequence,
            _calls, _fields, Clone(LastCall), Array.AsReadOnly(_publications.ToArray()), _writerBoundary, _failure);
    }
}
