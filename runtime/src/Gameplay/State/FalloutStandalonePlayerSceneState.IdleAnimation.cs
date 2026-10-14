using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerIdleAnimationCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutProcessIdleAnimationUpdate? Result, bool Returned, string? Failure,
    IReadOnlyList<FalloutPlayerThreadModelCacheRead>? ModelCacheReads = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private Func<Func<float>, FalloutProcessIdleAnimationUpdate>? _updateIdleAnimation;
    private Func<FalloutProcessIdleAnimationUpdate?>? _lastIdleAnimation;
    private FalloutPlayerIdleAnimationCall? _idleAnimationCall;
    internal void BindIdleAnimation(Func<Func<float>, FalloutProcessIdleAnimationUpdate> update,
        Func<FalloutProcessIdleAnimationUpdate?> readLast)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(update); ArgumentNullException.ThrowIfNull(readLast);
        if (_updateIdleAnimation is not null) throw new InvalidOperationException("Player idle animation already has its actual process owner.");
        _updateIdleAnimation = update; _lastIdleAnimation = readLast;
    }
    internal FalloutSourceSceneNode? ReadSelectedIdleModel(Func<FalloutSourceSceneNode?> readFallback)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(readFallback);
        if (!_writing || _idleAnimationCall is not { Returned: false, Failure: null })
            throw new InvalidOperationException("Selected Player model getter has no actual entered idle child.");
        if (_view == 0 && _first is { Released: null } first)
        {
            if (first.Publication.Process != _process || _livingFirst is null || !ReadLiving(_livingFirst))
                throw new NotSupportedException("source-Player-idle-first-person-model-current-factory-unbound");
            return new(_process, first.Publication.Factory, first.Publication.NativeRoot);
        }
        var actor = _actorProcessQuery?.Result?.Actor.Reference ??
            throw new InvalidOperationException("Player model cache lost its actual source Player identity.");
        var cache = _threadContext.ReadPlayerModelCache(actor);
        _idleAnimationCall = _idleAnimationCall with
        { ModelCacheReads = _idleAnimationCall.ModelCacheReads!.Append(cache).ToArray(), Changed = Next() };
        return cache.Matched ? cache.Model : readFallback();
    }
    internal FalloutIdleAnimationContext? ReadSelectedIdleAnimation(Func<FalloutIdleAnimationContext> readFirstPerson,
        Func<FalloutIdleAnimationContext?> readFallback)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        ArgumentNullException.ThrowIfNull(readFirstPerson); ArgumentNullException.ThrowIfNull(readFallback);
        if (!_writing || _idleAnimationCall is not { Returned: false, Failure: null })
            throw new InvalidOperationException("Player animation getter has no actual entered idle call.");
        // The source override is constructor-null until its independent writer
        // is owned. The first-person animation factory is a separate field and
        // is read independently of the selected model or common body.
        return _view == 0 ? readFirstPerson() : readFallback();
    }
    private void UpdateIdleAnimation(Guid main, long ordinal, Func<float>? readScaledTimer)
    {
        var entered = Next(); var fault = _reentry;
        _idleAnimationCall = new(main, ordinal, entered, entered, null, false, null, []);
        try
        {
            var result = (_updateIdleAnimation ?? throw new NotSupportedException("source-Player-current-process-idle-animation-update-unbound"))(
                readScaledTimer ?? (() => throw new NotSupportedException("source-Player-idle-current-Main-scaled-timer-unbound")));
            RequireCurrent(result);
            _idleAnimationCall = _idleAnimationCall with { Result = result, Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            if (_lastIdleAnimation?.Invoke() is { } result && _postActionsCall?.DetectionUpdate is { } detection &&
                SameProcess(result.Before, detection.Before) && result.Before.Changed == detection.Changed)
                _idleAnimationCall = _idleAnimationCall with { Result = result };
            _idleAnimationCall = _idleAnimationCall with { Failure = error.Message, Changed = Next() }; throw;
        }
        void RequireCurrent(FalloutProcessIdleAnimationUpdate result)
        {
            RequireThread();
            if (_reentry != fault || _postActionsCall?.DetectionUpdate is not { } detection ||
                !SameProcess(result.Before, detection.Before) || result.Before.Changed != detection.Changed ||
                !result.Trace.Returned || result.Trace.Failure is not null)
                throw new InvalidOperationException("Player idle child changed its current process epoch/lease or swallowed reentry.");
        }
    }
    private static bool SameProcess(FalloutProcessSceneQuery first, FalloutProcessSceneQuery second) =>
        first.Process == second.Process && first.Stack == second.Stack && first.ExecutableSha256 == second.ExecutableSha256 &&
        first.Actor == second.Actor && first.Epoch == second.Epoch && first.Level == second.Level &&
        first.GameplayOwnership == second.GameplayOwnership;
    private static void ValidateIdleAnimationCall(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.IdleAnimationCall is not { } call)
        {
            if (saved.ActorDistanceCall is { Returned: true })
                throw new InvalidDataException("Player continuation omitted its actually entered idle update.");
            return;
        }
        if (saved.ActorDistanceCall is not { Returned: true } distance ||
            call.Main != distance.Main || call.MainOrdinal != distance.MainOrdinal || call.Entered <= distance.Changed ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || call.Returned == (call.Failure is not null) ||
            call.Failure is not null && call.Failure != saved.Failure || call.Returned && call.Result is null ||
            call.ModelCacheReads is null || call.ModelCacheReads.Count > 2)
            throw new InvalidDataException("Player idle update lost its actual caller or partial failure.");
        if (call.Result is not { } result) return;
        foreach (var cache in call.ModelCacheReads)
            if (saved.ThreadContextEntry is not { } entry || cache.Source != entry.Source || cache.Process != entry.Process ||
                cache.Factory != entry.Factory || cache.Thread != entry.Thread || cache.Actor != result.Before.Actor.Reference ||
                cache.Matched || cache.CachedReference is not null || cache.Model is not null)
                throw new InvalidDataException("Player model cache lost its current-thread loader fields or admitted an unowned cache writer.");
        if (saved.PostActionsCall?.DetectionUpdate is not { } detection || !SameProcess(result.Before, detection.Before) ||
            result.Before.Changed != detection.Changed || result.Changed < result.Before.Changed || result.Trace is null ||
            result.Trace.Models is null || result.Trace.Models.Count > 2 ||
            result.Trace.Models.Any(node => node is not null &&
                (!ChildBelongsToParentProcessLineage(saved, node.Process) ||
                    node.Factory == Guid.Empty || node.NativeRoot == 0)) ||
            call.Returned && !result.Trace.Returned || result.Trace.Returned == (result.Trace.Failure is not null) ||
            !result.Trace.Returned && result.Trace.Failure != call.Failure ||
            result.BeforeFields != FalloutActorIdleAnimationFields.Constructed || result.AfterFields is null ||
            result.AfterFields.Stores < result.BeforeFields.Stores || result.AfterFields.Flag != 0 ||
            result.AfterFields.Action != -1 || result.AfterFields.CurrentIdle is not null)
            throw new InvalidDataException("Player idle receipt changed its exact source process, model factory or constructor fields.");
        if (result.Trace is { Step: "null-model-return" } &&
            (result.Trace.Models.Count != 1 || result.Trace.Models[0] is not null ||
                result.BeforeFields != result.AfterFields || result.Changed != result.Before.Changed ||
                result.Trace.Animation is not null || result.Trace.RadiusBits is not null || result.Trace.DistanceBits is not null ||
                result.Trace.DistanceSettingBits is not null || result.Trace.WaitSettingBits is not null || result.Trace.ResetWaitSettingBits is not null ||
                result.Trace.MainTimerBits is not null || result.Trace.IdleCalled || !result.Trace.Returned))
            throw new InvalidDataException("Source null-model return entered an independent animation, bound or timer child.");
        if (result.Trace.Step == "null-model-return" && call.ModelCacheReads.Count != 1)
            throw new InvalidDataException("Source null-model return skipped its independent current-thread model cache.");
        ValidateIdlePackageRead(result);
        var first = saved.FirstPersonHistory.Where(owner => owner.Stored < call.Entered).OrderBy(owner => owner.Stored).LastOrDefault();
        if (first is not null && (first.Released is null || first.Released > call.Changed))
        {
            var expected = new FalloutSourceSceneNode(first.Publication.Process, first.Publication.Factory, first.Publication.NativeRoot);
            if (result.Trace.Models.Any(node => node != expected))
                throw new InvalidDataException("High idle model did not read the actual retained first-person factory field.");
            if (call.ModelCacheReads.Count != 0)
                throw new InvalidDataException("Direct first-person model selection entered the independent thread/reference fallback.");
        }
        else if (result.Trace.Models.Any(node => node is not null))
            throw new InvalidDataException("High idle fallback model has no admitted native reference/TLS factory.");
    }
    private static void ValidateIdlePackageRead(FalloutProcessIdleAnimationUpdate result)
    {
        var trace = result.Trace;
        if (!trace.SelectedObjectReturned)
        {
            if (trace.SelectedObject is not null || trace.SelectedPackageRead is not null ||
                trace.AlternateObjectReturned || trace.AlternateObject is not null)
                throw new InvalidDataException("Idle update retained a package getter that was never returned.");
            return;
        }
        if (trace.SelectedPackageRead is not { } read || read.Factory == Guid.Empty ||
            read.Process != result.Before.Process || read.GameplayOwnership != result.Before.GameplayOwnership ||
            read.Actor != result.Before.Actor.Reference || read.Stack != result.Before.Stack || read.Result != trace.SelectedObject ||
            result.Before.IdlePrimaryPackage is not null)
            throw new InvalidDataException("Idle selected package lost its actual Common field factory, process lease or independent primary field.");
        if (read.Result is { } value && (value.Source is not { } source || source.Factory != read.Factory ||
            source.Process != read.Process || source.GameplayOwnership != read.GameplayOwnership || source.Actor != read.Actor ||
            source.Stack != read.Stack || source.Revision != read.Revision || string.IsNullOrWhiteSpace(source.Package.OwnerPlugin) ||
            source.Package.ObjectId == 0 || source.Sha256.Length != 64 || source.Sha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("Idle selected object lost its actual winning package record and assignment epoch.");
        if (read.ProcessPackage is { } slot)
        {
            if (slot.Process != result.Before || slot.Fields is null || slot.Fields.Fields is null || !slot.Fields.Available || !Enum.IsDefined(slot.Slot) ||
                slot.Fields.Slot != slot.Slot || slot.Fields.Source != FalloutProcessPackageState.Source(result.Before.ExecutableSha256) ||
                slot.Fields.Publication is null || slot.Fields.Publication.Factory != read.Factory ||
                slot.Fields.Publication.Process != read.Process || slot.Fields.Changed < 0 || read.Revision != (ulong)slot.Fields.Changed ||
                read.RegistryFactory is not { } registry || registry == Guid.Empty ||
                read.RegistrySource != FalloutPackageRuntimeRegistry.SourceFor(result.Before.ExecutableSha256) ||
                (slot.Fields.Fields.Package is null) != (read.Result is null) ||
                read.Result is { Source: { } actual } && (actual.Package != slot.Fields.Fields.Package || actual.Slot != slot.Slot ||
                    actual.RegistryFactory != registry || actual.RegistrySource != read.RegistrySource))
                throw new InvalidDataException("Idle package lost its actual selected slot or same winning registry metadata.");
        }
        else if (read.RegistryFactory is not null || read.RegistrySource is not null ||
            read.Result?.Source is { } authored && (authored.RegistryFactory is not null || authored.RegistrySource is not null || authored.Slot is not null))
            throw new InvalidDataException("Idle authored fixture metadata cannot claim a registry without its real Common slot.");
        if (trace.AlternateObjectReturned && trace.SelectedObject is not { Kind: 0x1c } ||
            !trace.AlternateObjectReturned && trace.AlternateObject is not null)
            throw new InvalidDataException("Idle alternate package was returned without its source selector.");
    }
}
