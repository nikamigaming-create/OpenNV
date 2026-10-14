using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerFollowingContinuationStep { Inhibition, Life, CurrentPackage, CurrentClear, PrimaryPackage, PrimaryClear, TimerByte, TimerScalar, ReadyItem, ReadyItemGlobals, ReadyItemPositive, ReadyItemTail, LateTail, Failed }
internal sealed record FalloutPlayerFollowingLifeRead(FalloutProcessSceneQuery Process,
    FalloutActorProcessFact<int> Raw, bool Argument, bool? Result);
internal sealed record FalloutPlayerFollowingContinuationCall(string Source, string Stack, Guid PlayerProcess,
    FalloutPlayerSourceCaller Caller, FalloutPlayerFollowingContinuationStep Step,
    IReadOnlyList<FalloutPlayerInhibitionRead> InhibitionReads, bool? Inhibited,
    FalloutPlayerFollowingLifeRead? Life, FalloutProcessPackageRead? Current,
    bool? CurrentClearSelected, FalloutProcessPackageRead? Primary, bool? PrimaryClearSelected,
    FalloutPlayerFollowingTimerFlagRead? Timer, bool ScalarSkipped,
    FalloutPlayerFollowingContinuationStep? FailedAt, string? Failure,
    IReadOnlyList<FalloutOriginalInputQuery>? ReadyItemQueries = null);

internal interface IFalloutPlayerFollowingContinuationConsumers
{
    void RequireCurrent(FalloutPlayerSourceCaller caller);
    FalloutPlayerInhibitionPredicate ReadInhibition(FalloutPlayerSourceCaller caller, Action<FalloutPlayerInhibitionRead> observe);
    FalloutPlayerFollowingLifeRead ReadLife(bool argument);
    FalloutProcessPackageRead ReadCurrentPackage();
    FalloutProcessPackageRead ReadPrimaryPackage();
    FalloutPlayerFollowingTimerFlagRead ReadTimerByte(FalloutPlayerSourceCaller caller);
}

internal interface IFalloutPlayerReadyItemConsumers
{
    FalloutOriginalInputQuery ReadReadyItem(FalloutPlayerSourceCaller caller, int mode,
        Action<FalloutOriginalInputQuery> observe);
}

// This component owns a source prefix and deliberately has no Returned state.
// Each selected unowned continuation remains a distinct entered boundary. The
// real enclosing Player/Main, thread exit and save guards remain authoritative.
internal static partial class FalloutPlayerFollowingContinuation
{
    internal const string CurrentClearBoundary = "source-Player-current-package-clear-refcount-procedure-effects-unbound";
    internal const string PrimaryClearBoundary = "source-Player-primary-package-clear-and-High-callback-effects-unbound";
    internal const string TimerBoundary = "source-Player-following-current-timer-scalar-delta-threshold-writers-unbound";
    internal const string ReadyItemBoundary = "source-Player-Ready-Item-current-input-mode-count-owner-unbound";
    internal const string ReadyItemGlobalsBoundary = "source-Player-Ready-Item-independent-hold-time-and-latch-global-writers-unbound";
    internal const string ReadyItemPositiveBoundary = "source-Player-Ready-Item-positive-count-disabled-word-and-weapon-services-unbound";
    internal const string ReadyItemTailBoundary = "source-Player-following-Ready-Item-frame-services-unbound";
    internal const string LateTailBoundary = "source-Player-following-inhibited-or-life-late-frame-tail-unbound";

    internal static bool LifePredicate(int raw, bool argument) => raw is 1 or 2 || !argument && raw == 6;

    internal static void Execute(FalloutPlayerFollowingContinuationSource source, string stack, Guid playerProcess,
        FalloutPlayerSourceCaller caller, IFalloutPlayerFollowingContinuationConsumers consumers,
        Action<FalloutPlayerFollowingContinuationCall> observe)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(observe);
        if (playerProcess == Guid.Empty || caller is null || caller.Main == Guid.Empty || caller.Ordinal < 1 || caller.Invocation == Guid.Empty)
            throw new InvalidDataException("Following Player service lost its actual entered Main/Player invocation.");
        consumers.RequireCurrent(caller);
        var call = new FalloutPlayerFollowingContinuationCall(source.Identity, stack, playerProcess, caller,
            FalloutPlayerFollowingContinuationStep.Inhibition, [], null, null, null, null, null, null, null, false, null, null);
        var reads = new List<FalloutPlayerInhibitionRead>(4);
        try
        {
            var inhibition = consumers.ReadInhibition(caller, read =>
            {
                reads.Add(read); call = call with { InhibitionReads = reads.ToArray() }; observe(call);
            });
            call = call with { Inhibited = inhibition.Result }; Check();
            if (!inhibition.Reads.SequenceEqual(reads)) throw new InvalidDataException("Following inhibition getter omitted its actual entered read prefix.");
            if (inhibition.Result) { Step(FalloutPlayerFollowingContinuationStep.LateTail); throw new NotSupportedException(LateTailBoundary); }
            Step(FalloutPlayerFollowingContinuationStep.Life);
            var life = consumers.ReadLife(false); call = call with { Life = life }; Check();
            if (life.Raw.Value is not { } raw || life.Raw.Failure is not null || life.Argument || life.Result != LifePredicate(raw, false))
                throw new NotSupportedException(life.Raw.Failure ?? "source-Player-current-Actor-life-predicate-owner-unbound");
            if (life.Result == true) { Step(FalloutPlayerFollowingContinuationStep.LateTail); throw new NotSupportedException(LateTailBoundary); }
            Step(FalloutPlayerFollowingContinuationStep.CurrentPackage);
            var current = consumers.ReadCurrentPackage(); call = call with { Current = current }; Check();
            if (current.Fields.Fields.Package is not null)
            {
                call = call with { CurrentClearSelected = true }; Step(FalloutPlayerFollowingContinuationStep.CurrentClear);
                throw new NotSupportedException(CurrentClearBoundary);
            }
            call = call with { CurrentClearSelected = false };
            Step(FalloutPlayerFollowingContinuationStep.PrimaryPackage);
            var primary = consumers.ReadPrimaryPackage(); call = call with { Primary = primary }; Check();
            if (primary.Fields.Fields.Package is not null)
            {
                call = call with { PrimaryClearSelected = true }; Step(FalloutPlayerFollowingContinuationStep.PrimaryClear);
                throw new NotSupportedException(PrimaryClearBoundary);
            }
            call = call with { PrimaryClearSelected = false };
            Step(FalloutPlayerFollowingContinuationStep.TimerByte);
            var timer = consumers.ReadTimerByte(caller); call = call with { Timer = timer }; Check();
            if (timer.Value != 0) { Step(FalloutPlayerFollowingContinuationStep.TimerScalar); throw new NotSupportedException(TimerBoundary); }
            call = call with { ScalarSkipped = true };
            Step(FalloutPlayerFollowingContinuationStep.ReadyItem);
            if (consumers is not IFalloutPlayerReadyItemConsumers ready) throw new NotSupportedException(ReadyItemBoundary);
            foreach (var mode in new[] { 1, 0, 2 })
            {
                var query = ready.ReadReadyItem(caller, mode, ObserveQuery);
                if (call.ReadyItemQueries is not { Count: > 0 } queries || queries[^1] != query ||
                    query.Step != FalloutOriginalInputQueryStep.Returned || query.Failure is not null)
                    throw new InvalidDataException("Ready Item omitted its actual returned input-query prefix.");
                Check();
                if (query.Count == 0) continue;
                if (mode == 2)
                {
                    Step(FalloutPlayerFollowingContinuationStep.ReadyItemTail);
                    throw new NotSupportedException(ReadyItemTailBoundary);
                }
                Step(FalloutPlayerFollowingContinuationStep.ReadyItemPositive);
                throw new NotSupportedException(ReadyItemPositiveBoundary);
            }
            Step(FalloutPlayerFollowingContinuationStep.ReadyItemGlobals);
            throw new NotSupportedException(ReadyItemGlobalsBoundary);
        }
        catch (Exception error)
        {
            call = call with { FailedAt = call.Step, Step = FalloutPlayerFollowingContinuationStep.Failed, Failure = error.Message };
            observe(call); throw;
        }
        void Step(FalloutPlayerFollowingContinuationStep step) { call = call with { Step = step }; observe(call); }
        void ObserveQuery(FalloutOriginalInputQuery query)
        {
            var preceding = call.ReadyItemQueries ?? [];
            call = call with { ReadyItemQueries = preceding.Concat([query]).ToArray() };
            Check(); observe(call);
        }
        void Check()
        {
            consumers.RequireCurrent(caller); RequireReadIdentity(call, source, stack, playerProcess);
        }
    }

    internal static void Validate(FalloutPlayerFollowingContinuationCall call, FalloutPlayerFollowingContinuationSource source, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(call);
        RequireReadIdentity(call, source, stack, call.PlayerProcess);
        if (!Enum.IsDefined(call.Step) || call.Step != FalloutPlayerFollowingContinuationStep.Failed ||
            call.FailedAt is not { } at || !Enum.IsDefined(at) || at == FalloutPlayerFollowingContinuationStep.Failed ||
            string.IsNullOrWhiteSpace(call.Failure) || call.InhibitionReads.Count > 4)
            throw new InvalidDataException("Following prefix lost its actual entered service/failure boundary.");
        for (var index = 0; index < call.InhibitionReads.Count; ++index)
            if (call.InhibitionReads[index].Lane != (FalloutPlayerInhibitionLane)index || index != 0 && call.InhibitionReads[index - 1].Value != 0)
                throw new InvalidDataException("Following Player inhibition changed source read order or skipped a short circuit.");
        if (call.Inhibited is { } inhibited && (call.InhibitionReads.Count == 0 ||
            inhibited != (call.InhibitionReads[^1].Value != 0) || !inhibited && call.InhibitionReads.Count != 4) ||
            call.Inhibited is null && (at != FalloutPlayerFollowingContinuationStep.Inhibition || call.Life is not null || call.Current is not null) ||
            call.Inhibited == true && (at != FalloutPlayerFollowingContinuationStep.LateTail || call.Life is not null || call.Current is not null) ||
            call.Life is null && (call.Current is not null || call.Primary is not null || call.Timer is not null ||
                call.Inhibited == false && at != FalloutPlayerFollowingContinuationStep.Life) ||
            call.Life is { } life && (life.Argument || life.Raw is null ||
                (life.Raw.Value is not { } raw || life.Raw.Failure is not null
                    ? life.Result is not null || at != FalloutPlayerFollowingContinuationStep.Life || call.Current is not null
                    : life.Result != LifePredicate(raw, false)) ||
                life.Result == true && (at != FalloutPlayerFollowingContinuationStep.LateTail || call.Current is not null)))
            throw new InvalidDataException("Following prefix lost its actual inhibition/life branch or admitted later getters.");
        if (call.Current is null && (call.CurrentClearSelected is not null || call.Primary is not null || call.Timer is not null ||
                call.Life is { Result: false } && at != FalloutPlayerFollowingContinuationStep.CurrentPackage) ||
            call.Current is { } current && (current.Slot != FalloutProcessPackageSlot.Current ||
                call.CurrentClearSelected != (current.Fields.Fields.Package is not null) ||
                call.CurrentClearSelected == true && (at != FalloutPlayerFollowingContinuationStep.CurrentClear || call.Primary is not null)) ||
            call.Primary is null && (call.PrimaryClearSelected is not null || call.Timer is not null ||
                call.CurrentClearSelected == false && at != FalloutPlayerFollowingContinuationStep.PrimaryPackage) ||
            call.Primary is { } primary && (primary.Slot != FalloutProcessPackageSlot.Primary || call.CurrentClearSelected != false ||
                call.PrimaryClearSelected != (primary.Fields.Fields.Package is not null) ||
                call.PrimaryClearSelected == true && (at != FalloutPlayerFollowingContinuationStep.PrimaryClear || call.Timer is not null)))
            throw new InvalidDataException("Following source package getter/clear decisions were exchanged, fabricated or reordered.");
        if (call.Timer is null && (call.ScalarSkipped || call.PrimaryClearSelected == false && at != FalloutPlayerFollowingContinuationStep.TimerByte) ||
            call.Timer is { } timer && (call.PrimaryClearSelected != false || call.ScalarSkipped != (timer.Value == 0) ||
                (timer.Value == 0 ? at is not (FalloutPlayerFollowingContinuationStep.ReadyItem or
                    FalloutPlayerFollowingContinuationStep.ReadyItemGlobals or FalloutPlayerFollowingContinuationStep.ReadyItemPositive or
                    FalloutPlayerFollowingContinuationStep.ReadyItemTail) : at != FalloutPlayerFollowingContinuationStep.TimerScalar)))
            throw new InvalidDataException("Following timer byte read acquired skipped scalar reads/stores or lost its actual branch boundary.");
        ValidateReadyItemQueries(call, source, stack);
        var expectedFailure = at switch
        {
            FalloutPlayerFollowingContinuationStep.CurrentClear => CurrentClearBoundary,
            FalloutPlayerFollowingContinuationStep.PrimaryClear => PrimaryClearBoundary,
            FalloutPlayerFollowingContinuationStep.TimerScalar => TimerBoundary,
            FalloutPlayerFollowingContinuationStep.ReadyItem => call.ReadyItemQueries is { Count: > 0 } ? call.Failure : ReadyItemBoundary,
            FalloutPlayerFollowingContinuationStep.ReadyItemGlobals => ReadyItemGlobalsBoundary,
            FalloutPlayerFollowingContinuationStep.ReadyItemPositive => ReadyItemPositiveBoundary,
            FalloutPlayerFollowingContinuationStep.ReadyItemTail => ReadyItemTailBoundary,
            FalloutPlayerFollowingContinuationStep.LateTail => LateTailBoundary,
            _ => call.Failure
        };
        if (call.Failure != expectedFailure) throw new InvalidDataException("Following Player service changed its precise selected unowned boundary.");
    }
    private static void RequireReadIdentity(FalloutPlayerFollowingContinuationCall call, FalloutPlayerFollowingContinuationSource source,
        string stack, Guid process)
    {
        if (call.Source != source.Identity || call.Stack != stack || process == Guid.Empty || call.PlayerProcess != process ||
            call.Caller is null || call.Caller.Main == Guid.Empty || call.Caller.Ordinal < 1 || call.Caller.Invocation == Guid.Empty || call.InhibitionReads is null)
            throw new InvalidDataException("Following Player service lost source/stack/process or actual caller identity.");
        Guid? inhibitionFactory = null; long? revision = null;
        foreach (var read in call.InhibitionReads)
        {
            if (read is null || read.Source != source.Identity || read.Stack != stack || read.Process != process || read.Factory == Guid.Empty ||
                read.Caller != call.Caller || read.Consumer != FalloutPlayerInhibitionConsumer.Following || read.Revision < 6 ||
                inhibitionFactory is { } first && first != read.Factory || revision is { } oldRevision && oldRevision != read.Revision)
                throw new InvalidDataException("Following inhibition changed current field factory/revision or entered source caller.");
            inhibitionFactory = read.Factory; revision = read.Revision;
        }
        FalloutProcessSceneQuery? expected = null;
        foreach (var query in new[] { call.Life?.Process, call.Current?.Process, call.Primary?.Process }.OfType<FalloutProcessSceneQuery>())
        {
            if (query.Actor is null) throw new InvalidDataException("Following getter omitted the actual source Actor identity.");
            query.Actor.Validate();
            if (query.Stack != stack || query.Process == Guid.Empty || query.ExecutableSha256 != source.Controls.Main.EngineSha256 ||
                !query.Actor.EnginePlayer || query.Epoch < 1 || query.GameplayOwnership == Guid.Empty || query.Changed < 1 ||
                expected is { } prior && (query.Process != prior.Process || query.Actor != prior.Actor || query.Epoch != prior.Epoch ||
                    query.Level != prior.Level || query.GameplayOwnership != prior.GameplayOwnership))
                throw new InvalidDataException("Following getter selected a foreign actual process/Actor/epoch/lease.");
            expected = query;
        }
        if (call.Life is { Process: null } || call.Current is { Process: null } || call.Primary is { Process: null })
            throw new InvalidDataException("Following getter omitted its process owner.");
        foreach (var package in new[] { call.Current, call.Primary }.OfType<FalloutProcessPackageRead>())
        {
            FalloutProcessPackageState.ValidateRead(package, source.Controls.Main.EngineSha256, stack);
            if (package.Fields is null || package.Fields.Fields is null || package.Fields.Publication is null ||
                package.Fields.Source != FalloutProcessPackageState.Source(source.Controls.Main.EngineSha256) || !package.Fields.Available ||
                package.Fields.Slot != package.Slot || package.Fields.Publication.Factory == Guid.Empty ||
                package.Fields.Publication.Process != package.Process.Process)
                throw new InvalidDataException("Following package getter borrowed an unavailable or foreign retained field lifetime.");
        }
        if (call.Current is { } current && call.Primary is { } primary &&
            current.Fields.Publication.Factory == primary.Fields.Publication.Factory)
            throw new InvalidDataException("Following package getters aliased independently constructed state objects.");
        if (call.Timer is { } timer && (timer.Source != source.Identity || timer.Stack != stack || timer.Process != process ||
            timer.Caller != call.Caller || timer.Factory == Guid.Empty || timer.Revision < 0))
            throw new InvalidDataException("Following timer getter borrowed a foreign current source byte owner.");
    }
}
