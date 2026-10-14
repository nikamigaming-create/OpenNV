using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerWeaponContinuationStep
{
    WeaponAction, WeaponSuppression, WeaponEntry,
    Activity, ActorStatus, Effects, Returned, Failed
}
internal sealed record FalloutPlayerWeaponContinuationCall(string Source, string Stack, Guid PlayerProcess,
    Guid Main, long MainOrdinal, Guid Invocation, FalloutPlayerWeaponContinuationStep Step,
    FalloutPlayerWeaponContinuationStep? FailedAt, FalloutProcessWeaponActionRead? Action,
    FalloutProcessSceneQuery? WeaponEntry, FalloutProcessWeaponActivityRead? Activity,
    FalloutProcessActorStatusRead? Status, bool? EffectsRequired, bool Returned, string? Failure);
internal interface IFalloutPlayerWeaponContinuationConsumers
{
    void RequireCurrent(Guid main, long ordinal, Guid invocation);
    FalloutProcessWeaponActionRead ReadAction();
    bool ReadSuppression();
    FalloutProcessSceneQuery ReadWeaponEntry();
    FalloutProcessWeaponActivityRead ReadActivity();
    FalloutProcessActorStatusRead ReadStatus();
}

internal static class FalloutPlayerWeaponContinuation
{
    internal static void Validate(FalloutPlayerWeaponContinuationCall call,
        FalloutPlayerWeaponContinuationSource source, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(call);
        if (call.Source != source.Identity || call.Stack != stack || string.IsNullOrWhiteSpace(stack) ||
            call.PlayerProcess == Guid.Empty || call.Main == Guid.Empty || call.MainOrdinal < 1 || call.Invocation == Guid.Empty ||
            call.Action is { Process: null } || call.Activity is { Process: null } || call.Status is { Process: null } ||
            !Enum.IsDefined(call.Step) || call.Returned != (call.Step == FalloutPlayerWeaponContinuationStep.Returned) ||
            (call.Step == FalloutPlayerWeaponContinuationStep.Failed) != (call.Failure is not null) ||
            (call.Step == FalloutPlayerWeaponContinuationStep.Failed) != (call.FailedAt is not null) ||
            call.FailedAt is { } failed && (!Enum.IsDefined(failed) || failed is FalloutPlayerWeaponContinuationStep.Returned or FalloutPlayerWeaponContinuationStep.Failed) ||
            call.Failure is not null && string.IsNullOrWhiteSpace(call.Failure))
            throw new InvalidDataException("Weapon continuation lost its actual source/caller or entered return/failure prefix.");
        var step = call.FailedAt ?? call.Step;
        var rank = (int)step;
        if (call.Action is null && (call.WeaponEntry is not null || call.Activity is not null || call.Status is not null ||
                call.EffectsRequired is not null || step != FalloutPlayerWeaponContinuationStep.WeaponAction) ||
            call.Action is { Value: 0 } && (call.WeaponEntry is not null || step is FalloutPlayerWeaponContinuationStep.WeaponSuppression or FalloutPlayerWeaponContinuationStep.WeaponEntry) ||
            call.WeaponEntry is not null && (call.Action is not { Value: not 0 } || rank < (int)FalloutPlayerWeaponContinuationStep.WeaponEntry) ||
            call.Activity is null && (call.Status is not null || call.EffectsRequired is not null || rank > (int)FalloutPlayerWeaponContinuationStep.Activity) ||
            call.Activity is not null && rank < (int)FalloutPlayerWeaponContinuationStep.Activity ||
            call.Status is not null && (call.Activity is not { Value: 0 } || rank < (int)FalloutPlayerWeaponContinuationStep.ActorStatus) ||
            call.EffectsRequired is { } effects && (call.Activity is null || effects !=
                (call.Activity.Value != 0 || (call.Status ?? throw new InvalidDataException("Zero activity omitted its actual status getter.")).Positive)) ||
            call.Returned && (call.Action is null || call.Activity is null || call.EffectsRequired != false ||
                call.Activity.Value == 0 && call.Status is null || call.WeaponEntry?.WeaponEntry is not null) ||
            step == FalloutPlayerWeaponContinuationStep.Effects && call.EffectsRequired != true)
            throw new InvalidDataException("Weapon continuation lost its source branch order or skipped getter/effect boundary.");
        foreach (var process in new[] { call.Action?.Process, call.WeaponEntry, call.Activity?.Process, call.Status?.Process }.OfType<FalloutProcessSceneQuery>())
        {
            if (process.Actor is null) throw new InvalidDataException("Weapon continuation getter omitted its actual Actor identity.");
            process.Actor.Validate();
            if (process.Process == Guid.Empty || process.Stack != stack || process.ExecutableSha256 != source.Controls.Main.EngineSha256 ||
                !process.Actor.EnginePlayer || process.Epoch < 1 || process.GameplayOwnership == Guid.Empty || process.Changed < 1 ||
                process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
                call.Action is { Process: { } expected } && (process.Process != expected.Process || process.Actor != expected.Actor ||
                    process.Epoch != expected.Epoch || process.Level != expected.Level || process.GameplayOwnership != expected.GameplayOwnership))
                throw new InvalidDataException("Weapon continuation retained a foreign source/process getter.");
        }
        if (call.Status is { } status && (status.Context == Guid.Empty ||
            status.AfterFlags != (status.BeforeFlags | FalloutPlayerWeaponContinuationSource.StatusCacheMask) ||
            status.PoolQueried != ((status.BeforeFlags & FalloutPlayerWeaponContinuationSource.StatusCacheMask) == 0) ||
            status.Changed < status.Process.Changed || status.PoolQueried && status.Changed - status.Process.Changed < 2 ||
            !status.PoolQueried && status.Changed != status.Process.Changed ||
            status.Positive != (BitConverter.UInt32BitsToSingle(status.ScalarBits) > 0)))
            throw new InvalidDataException("Actor status lost its retained context, ordered cache stores or raw Float32 comparison.");
    }

    internal static FalloutPlayerWeaponContinuationCall Execute(FalloutPlayerWeaponContinuationSource source,
        string stack, Guid playerProcess, Guid main, long ordinal, Guid invocation,
        IFalloutPlayerWeaponContinuationConsumers consumers, Action<FalloutPlayerWeaponContinuationCall> observe)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(observe);
        if (playerProcess == Guid.Empty || main == Guid.Empty || ordinal < 1 || invocation == Guid.Empty)
            throw new InvalidDataException("Player weapon continuation lost its actual entered Main caller.");
        consumers.RequireCurrent(main, ordinal, invocation);
        var call = new FalloutPlayerWeaponContinuationCall(source.Identity, stack, playerProcess, main, ordinal, invocation,
            FalloutPlayerWeaponContinuationStep.WeaponAction, null, null, null, null, null, null, false, null);
        try
        {
            var action = consumers.ReadAction(); call = call with { Action = action }; Check();
            if (action.Value != 0)
            {
                Step(FalloutPlayerWeaponContinuationStep.WeaponSuppression);
                if (!consumers.ReadSuppression())
                {
                    Check(); Step(FalloutPlayerWeaponContinuationStep.WeaponEntry);
                    var entry = consumers.ReadWeaponEntry(); call = call with { WeaponEntry = entry }; Check();
                    if (entry.WeaponEntry is not null)
                        throw new NotSupportedException("source-Player-following-nonnull-weapon-state-ammo-and-form-effects-unbound");
                }
                else Check();
            }
            Step(FalloutPlayerWeaponContinuationStep.Activity);
            var activity = consumers.ReadActivity(); call = call with { Activity = activity }; Check();
            var effects = activity.Value != 0;
            if (!effects)
            {
                Step(FalloutPlayerWeaponContinuationStep.ActorStatus);
                var status = consumers.ReadStatus(); call = call with { Status = status }; Check(); effects = status.Positive;
            }
            call = call with { EffectsRequired = effects };
            if (effects)
            {
                Step(FalloutPlayerWeaponContinuationStep.Effects);
                throw new NotSupportedException("source-Player-following-activity-or-status-source-effects-unbound");
            }
            call = call with { Step = FalloutPlayerWeaponContinuationStep.Returned, Returned = true };
            observe(call); return call;
        }
        catch (Exception error)
        {
            call = call with { FailedAt = call.Step, Step = FalloutPlayerWeaponContinuationStep.Failed, Failure = error.Message };
            observe(call); throw;
        }
        void Step(FalloutPlayerWeaponContinuationStep step) { call = call with { Step = step }; observe(call); }
        void Check()
        {
            consumers.RequireCurrent(main, ordinal, invocation);
            foreach (var process in new[] { call.Action?.Process, call.WeaponEntry, call.Activity?.Process, call.Status?.Process }.OfType<FalloutProcessSceneQuery>())
                if (process.Stack != stack || process.ExecutableSha256 != source.Controls.Main.EngineSha256 || !process.Actor.EnginePlayer ||
                    call.Action is { Process: { } expected } && (process.Process != expected.Process || process.Actor != expected.Actor ||
                        process.Epoch != expected.Epoch || process.Level != expected.Level || process.GameplayOwnership != expected.GameplayOwnership))
                    throw new InvalidOperationException("Weapon continuation changed its actual selected Player/process getter.");
        }
    }
}
