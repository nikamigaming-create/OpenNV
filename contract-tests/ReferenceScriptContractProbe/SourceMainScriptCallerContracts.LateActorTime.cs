using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunLateActorTimeContracts()
    {
        using var fixture = new LateActorTimeFixture();
        var returned = fixture.Enter();
        Require(returned.Returned && returned.Lives is { Count: 3 } && returned.GlobalAi is { Value: 0, Boundary: null } &&
            returned.HardwareThreads is { Number: 2 } && returned.ActorValueFlags is null &&
            returned.Stores.Select(value => value.Field).SequenceEqual([
                FalloutPlayerLateActorField.MobileState, FalloutPlayerLateActorField.SelfPredicate,
                FalloutPlayerLateActorField.ScalarWord, FalloutPlayerLateActorField.DateMismatch]) &&
            fixture.Scalars == FalloutPlayerSceneScalarFields.Constructed &&
            returned.Timer is { ScaledSecondsBits: 0, HasReturnedUpdate: false } &&
            returned.After is { MobileState: 255, SelfPredicate: 0, DateMismatch: 1 },
            "Actual Actor-time child lost its same Common/reference/global owners or source stores.");
        var saved = fixture.Capture(); var stable = JsonSerializer.Serialize(saved);
        Reject(() => fixture.Owner.Update(returned.Invocation));
        Require(stable == JsonSerializer.Serialize(fixture.Capture()), "Replay mutated a returned Actor-time child.");
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { Fields = saved.Fields with { DateMismatch = 0 } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { Stores = saved.LastCall!.Stores.Reverse().ToArray() } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { Invocation = saved.LastCall!.Invocation with { PlayerConstructor = Guid.NewGuid() } } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { CalendarDay = saved.LastCall!.CalendarDay! with { SignedDay = 127 } } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { GlobalAi = saved.LastCall!.GlobalAi! with { Value = 1 } } }));
        var hardware = saved.LastCall!.HardwareThreads!;
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { HardwareThreads = hardware with { Declaration = hardware.Declaration with { Name = "uExterior Cell Buffer:General" } } } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { HardwareThreads = hardware with { Number = (double)uint.MaxValue } } }));
        Reject(() => FalloutPlayerLateActorTimeState.Validate(saved with { LastCall = saved.LastCall! with
        { HardwareThreads = hardware with { Number = 1 } } }));
        var fieldsState = RoundTripPackage(fixture.Base.Fields.Capture());
        var commonState = RoundTripPackage(fixture.Base.Common.Capture());
        var updatesState = RoundTripPackage(fixture.Updates.Capture());
        var globalsState = RoundTripPackage(fixture.Globals.Capture());
        FalloutPlayerLateActorTimeSaveContract.Validate(saved, fieldsState, globalsState, updatesState, fixture.Forms.Day);
        FalloutPlayerLateActorTimeState.ValidateSharedAiRead(saved, updatesState);
        Reject(() => FalloutPlayerLateActorTimeState.ValidateSharedAiRead(saved, updatesState with
        { PlayerFrameGlobalAi = updatesState.PlayerFrameGlobalAi! with { Source = new('c', 64) } }));
        Reject(() => new LateActorTimeFixture(saved, fieldsState, commonState, updatesState with { PlayerFrameGlobalAi = null }));
        Reject(() => new LateActorTimeFixture(saved, fieldsState, commonState, updatesState with
        { PlayerFrameGlobalAi = updatesState.PlayerFrameGlobalAi! with { Process = Guid.NewGuid() } }));
        Reject(() => new LateActorTimeFixture(saved, fieldsState, commonState, updatesState with
        { PlayerFrameGlobalAi = updatesState.PlayerFrameGlobalAi! with { Revision = 1 } }));
        Reject(() => FalloutActorProcessCommonState.Validate(commonState with { Schema = "opennv-actor-process-common/v17" }));
        Reject(() => FalloutActorProcessCommonState.Validate(commonState with
        { Current = commonState.Current.Select(row => row with { Scalars = row.Scalars with { PackedDateWord = null } }).ToArray() }));
        Reject(() => FalloutActorProcessCommonState.Validate(commonState with
        { Current = commonState.Current.Select(row => row with { Scalars = row.Scalars with { PackedDateWord = 0x100 } }).ToArray() }));
        Require(commonState.Current.Single().Scalars.PackedDateWord == 0 &&
            commonState.Current.Single().Scalars.FirstWord == 0 &&
            commonState.Current.Single().Scalars.ClockBits == 0xbf800000,
            "Packed date replaced a neighbouring scalar or floating clock.");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new LateActorTimeFixture(saved, fieldsState, commonState, updatesState, globals: globalsState);
            var before = cold.Capture();
            Require(before.Factory != saved.Factory && before.PlayerProcess != saved.PlayerProcess &&
                JsonSerializer.Serialize(before.LastCall) == JsonSerializer.Serialize(saved.LastCall) && before.Calls == saved.Calls,
                "Cold Actor-time replayed a child or promoted historical process/constructor identity.");
            FalloutPlayerLateActorTimeSaveContract.Validate(before, cold.Base.Fields.Capture(), cold.Globals.Capture(), cold.Updates.Capture(), cold.Forms.Day);
            var next = cold.Enter(saved.LastCall!.Invocation.Caller.Ordinal + 1);
            Require(next.Returned && next.Factory == cold.Capture().Factory && next.Invocation.PlayerProcess == cold.Capture().PlayerProcess,
                "Fresh Actor-time cold invocation retained another live parent.");
            saved = cold.Capture(); fieldsState = RoundTripPackage(cold.Base.Fields.Capture());
            commonState = RoundTripPackage(cold.Base.Common.Capture());
            updatesState = RoundTripPackage(cold.Updates.Capture());
            globalsState = RoundTripPackage(cold.Globals.Capture());
            FalloutPlayerLateActorTimeSaveContract.Validate(saved, fieldsState, globalsState, updatesState, cold.Forms.Day);
            FalloutPlayerLateActorTimeState.ValidateSharedAiRead(saved, updatesState);
            Require(updatesState.PlayerFrameGlobalAi!.Publications.Count == cycle + 2 &&
                updatesState.PlayerFrameGlobalAi.Revision == 0 && updatesState.PlayerFrameGlobalAi.Writers.Count == 0,
                "Cold shared AI replayed a constructor/write or omitted its actual fresh domain publication.");
        }
        using (var disabled = new LateActorTimeFixture())
        {
            disabled.Updates.Set(disabled.Base.Actor.Reference, 0, null, "authored-real-Actor-AI-byte-writer");
            var skip = disabled.Enter();
            Require(skip.Returned && skip.ActorUpdate is { Value: false } && skip.Stores.Count == 0 && skip.Lives is null && skip.Timer is null,
                "Disabled Actor update read or mutated source-skipped fields.");
            Reject(() => FalloutPlayerLateActorTimeState.Validate(disabled.Capture() with
            { LastCall = skip with { ReferenceFlags = null } }));
        }
        using (var equal = new LateActorTimeFixture())
        {
            equal.Globals.Set(equal.Forms.Day, 0); var first = equal.Enter();
            Require(first.Returned && first.After.DateMismatch == 0, "Equal source day did not commit mismatch-zero.");
            equal.Globals.Set(equal.Forms.Day, 1); Reject(() => equal.Enter(2));
            var partial = equal.Capture();
            Require(partial.LastCall is { Returned: false, Failure: FalloutPlayerLateActorTimeState.WeekdayBoundary,
                FailedAt: FalloutPlayerLateActorTimeStep.DateMismatch, Stores.Count: 3 } && partial.Fields.DateMismatch == 0,
                "A changed day fabricated an unowned weekday/Extra return or erased earlier scalar stores.");
        }
        using (var positive = new LateActorTimeFixture())
        {
            positive.Scalars = positive.Scalars with
            { ActorRemainingBits = BitConverter.SingleToUInt32Bits(0.05f), ActorWord = 17 };
            positive.PublishFixedTimer(100);
            var first = positive.Enter(); var before = 0.05f;
            var after = before - BitConverter.UInt32BitsToSingle(first.Timer!.ScaledSecondsBits);
            Require(first.Returned && first.Timer.HasReturnedUpdate && after < 0 &&
                first.AfterScalars.ActorRemainingBits == BitConverter.SingleToUInt32Bits(after) && first.AfterScalars.ActorWord == 17 &&
                first.Stores[2].Field == FalloutPlayerLateActorField.RemainingScalar,
                "Positive Actor time clamped the Float32 subtraction or cleared its unrelated word.");
            var second = positive.Enter(2);
            Require(second.Returned && second.AfterScalars.ActorRemainingBits == first.AfterScalars.ActorRemainingBits &&
                second.AfterScalars.ActorWord == 0 && second.Stores[2].Field == FalloutPlayerLateActorField.ScalarWord,
                "The next nonpositive branch reset the scalar instead of its independent word.");
            _ = positive.Capture();
        }
        using (var failed = new LateActorTimeFixture())
        {
            failed.FailAfterScalarCommit = true; Reject(() => failed.Enter()); var partial = failed.Capture();
            Require(partial.LastCall is { Returned: false, Stores.Count: 3 } &&
                partial.LastCall.Stores[^1].Field == FalloutPlayerLateActorField.ScalarWord && partial.LastCall.Timer is not null &&
                partial.LastCall.Lives is null && failed.Owner.SaveBlocker is not null,
                "Failed scalar child lost its actual unchanged-word store or claimed later reads/return.");
            Reject(() => FalloutPlayerLateActorTimeState.Validate(partial with
            { Failure = null, LastCall = partial.LastCall! with { Returned = true, Step = FalloutPlayerLateActorTimeStep.Returned, FailedAt = null, Failure = null } }));
        }
        using (var unknown = new LateActorTimeFixture())
        {
            unknown.Updates.ObserveUnownedGlobalAiWriter("authored-global-command-first-mutation-ingress");
            Reject(() => unknown.Enter());
            Require(unknown.Capture().LastCall is { FailedAt: FalloutPlayerLateActorTimeStep.GlobalAi, Stores.Count: 4, Returned: false },
                "An unowned global writer was hidden by static constructor-zero or erased earlier stores.");
            var blocked = RoundTripPackage(unknown.Updates.Capture());
            Require(unknown.Updates.SaveBlocker == blocked.PlayerFrameGlobalAi!.Boundary &&
                blocked.PlayerFrameGlobalAi is { Revision: 1, Writers.Count: 1, Value: 0 },
                "The actual shared AI save hook lost an entered unowned producer or fabricated a toggle.");
            using var continuation = new FalloutActorUpdateState(
                FalloutActorUpdateDeclaration.ForExecutable(unknown.Base.FieldSource.Controls.Main.EngineSha256),
                unknown.Base.Stack, _ => unknown.Base.Actor, blocked);
            continuation.BindPlayerFrameGlobalAi(unknown.Capture().Source, () => { });
            Require(continuation.SaveBlocker == blocked.PlayerFrameGlobalAi.Boundary &&
                continuation.Capture().PlayerFrameGlobalAi!.Writers.SequenceEqual(blocked.PlayerFrameGlobalAi.Writers),
                "A fresh shared domain erased the failed writer or replayed its effects.");
        }
        using (var singleThread = new LateActorTimeFixture(hardwareThreads: 1))
        {
            Exception? reached = null;
            try { singleThread.Enter(); } catch (NotSupportedException error) { reached = error; }
            var call = singleThread.Capture().LastCall!;
            Require(call.HardwareThreads is { Number: 1 } && call.ActorValueFlags is { } flags &&
                ((flags.Flags & FalloutPlayerLateActorTimeSource.ActorConstructionFlag) != 0
                    ? call.Returned && reached is null
                    : !call.Returned && call.FailedAt == FalloutPlayerLateActorTimeStep.ActorValues &&
                        reached?.Message == FalloutPlayerLateActorTimeState.ActorValueBoundary),
                "The actual single-thread branch skipped its independent Actor flag child or fabricated its nonempty return.");
        }
        using (var signed = new LateActorTimeFixture(hardwareThreads: -2))
        {
            var call = signed.Enter();
            Require(call.Returned && call.HardwareThreads is { Number: -2 } && call.ActorValueFlags is null,
                "The original signed comparison was replaced with an unsigned exterior-buffer restriction.");
        }
        RunLateActorTimeReadOwnerContracts();
        Console.WriteLine("OPENNV_LATE_ACTOR_TIME_PASS actualOwnedFields=true constructorDefaultMobileStore=true " +
            "sameSceneScalars=true sameGlobalAiDomain=true repeatedLife=true orderedPartialStores=true " +
            "coldCycles=3 wholePlayer=UNEXECUTED gameplay=UNEXECUTED");
    }

    private sealed class LateActorTimeFixture : IDisposable
    {
        internal readonly ReferenceScriptInitializationFixture Base;
        internal readonly FalloutActorProcessRuntimeState Runtime;
        internal readonly FalloutActorUpdateState Updates;
        internal readonly FalloutPlayerLateActorTimeState Owner;
        internal readonly FalloutGlobalState Globals;
        internal readonly FalloutGameTimeBindings Forms;
        private readonly FalloutSourceFrameTimerState _timer;
        private readonly FalloutGameTime _calendar;
        private readonly FalloutNumericIniSettings _settings;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private FalloutPlayerLateFrameInvocation? _entered;
        private bool _alive = true;
        internal FalloutPlayerSceneScalarFields Scalars = FalloutPlayerSceneScalarFields.Constructed;
        internal bool FailAfterScalarCommit;
        internal LateActorTimeFixture(FalloutPlayerLateActorSnapshot? saved = null,
            FalloutPackageCommandFieldSnapshot? fields = null, FalloutProcessCommonSnapshot? common = null,
            FalloutActorUpdateSnapshot? updates = null, int hardwareThreads = 2, FalloutGlobalStateSnapshot? globals = null)
        {
            Base = new(saved: fields, common: common); if (fields is null) Base.Initialize();
            var main = Base.FieldSource.Controls.Main;
            var current = Base.Common.ReadSceneQueries(Base.Actor.Reference, 1);
            var source = FalloutPlayerLateActorTimeSource.Reviewed(FalloutPlayerFollowingContinuationSource.Reviewed(Base.FieldSource.Controls));
            Runtime = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), Base.Stack, Base.Actor.Reference, _ => Base.Actor);
            Updates = new(FalloutActorUpdateDeclaration.ForExecutable(main.EngineSha256), Base.Stack, _ => Base.Actor, updates);
            Updates.Construct(Base.Actor.Reference); Updates.BindPlayerFrameGlobalAi(source, Lifetime);
            Forms = FalloutGameTimeBindings.Read(Base.Records);
            var keys = new[] { Forms.Year, Forms.Month, Forms.Day, Forms.Hour, Forms.DaysPassed, Forms.TimeScale };
            float[] values = [2277, 0, 1, 12, 5, 30];
            Globals = new(keys.Select((key, index) => new FalloutGlobal(key, "AuthoredTime" + index, (byte)'f', values[index], new('a', 64))));
            if (globals is not null) Globals.Restore(globals);
            _calendar = new(Globals, Forms, new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "authored-calendar"));
            _calendar.BindActorFrameCalendar(source, Base.Stack, Base.ThreadContext.Process, Lifetime);
            var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
            _timer = new(timerSource, new LateActorCounter(), Base.ThreadContext.Process);
            _settings = FalloutInstallationSettings.ReadIniLayers(() => [new(
                FalloutPlayerLateActorTimeSource.HardwareThreadsSetting, FalloutIniCollection.Main, unchecked((uint)hardwareThreads))], [], "authored-real-INI-owner").NumericIni;
            Owner = new(source, Base.Stack, Base.ThreadContext.Process, Base.Fields.Constructor, current,
                Base.Common, Runtime, Updates, Base.Initializer, _calendar, _timer, _settings,
                () => Scalars, (value, committed) =>
                {
                    Scalars = value; committed();
                    if (!FailAfterScalarCommit) return;
                    try { _ = Owner!.Capture(); } catch (InvalidOperationException) { }
                }, () => 0, Guard, Lifetime, saved);
        }
        internal FalloutPlayerLateActorTimeCall Enter(long ordinal = 1)
        {
            var current = Base.Common.ReadSceneQueries(Base.Actor.Reference, 1);
            _entered = new(new(Guid.NewGuid(), ordinal, Guid.NewGuid()), Base.ThreadContext.Process,
                Base.Fields.Constructor, Guid.NewGuid(), current, _thread, BitConverter.SingleToUInt32Bits(0.1f));
            try { return Owner.Update(_entered); } finally { _entered = null; }
        }
        internal void PublishFixedTimer(float milliseconds)
        {
            _timer.SetFixedMilliseconds(milliseconds); _timer.Update();
        }
        internal FalloutPlayerLateActorSnapshot Capture()
        {
            var saved = JsonSerializer.Deserialize<FalloutPlayerLateActorSnapshot>(JsonSerializer.Serialize(Owner.Capture()))!;
            FalloutPlayerLateActorTimeState.Validate(saved); return saved;
        }
        private void Lifetime()
        {
            if (!_alive || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored Actor-time lifetime or thread changed.");
        }
        private void Guard(FalloutPlayerLateFrameInvocation invocation)
        {
            Lifetime(); if (invocation != _entered) throw new InvalidOperationException("Authored Actor-time has no actual entered frame scope.");
            FalloutPlayerLateActorTimeState.RequireSameProcess(invocation, Base.Common.ReadSceneQueries(Base.Actor.Reference, 1));
        }
        public void Dispose()
        {
            _alive = false; _timer.Dispose(); Updates.Dispose(); Runtime.Dispose(); Base.Dispose();
        }
    }
    private sealed class LateActorCounter : IFalloutSourceTickCounter
    {
        public string Owner => "authored-Actor-time-current-Main-counter";
        public uint Read() => 1000;
        public void Wait(uint milliseconds) => throw new InvalidDataException("Actor-time scalar read sampled or waited on its source timer.");
    }
}
