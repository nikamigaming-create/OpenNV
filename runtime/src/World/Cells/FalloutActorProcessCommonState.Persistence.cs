using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessCommonSnapshot Capture()
    {
        RequireNotBusy();
        return new(Schema, _source.Contract, _stack, _process, _sequence, _current.Values.ToArray(),
            _retired.ToArray(), _pending, _transfer, _cold);
    }
    private void Restore(FalloutProcessCommonSnapshot saved)
    {
        Validate(saved);
        if (saved.Contract != _source.Contract || saved.Stack != _stack || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold process common owner differs from its source or new process epoch.");
        foreach (var entry in saved.Current.Concat(saved.Retired).Concat(saved.Pending is null ? [] : [saved.Pending]))
            if (Identity(entry.Source.Reference) != entry.Source)
                throw new InvalidDataException("Cold common state lost an exact current/historical source actor/base winner/master.");
        foreach (var current in saved.Current)
        {
            _current.Add(current.Source.Reference, current);
        }
        _retired.AddRange(saved.Retired); _pending = saved.Pending; _transfer = saved.Transfer; _sequence = saved.Sequence;
        _cold = new(saved.CapturedProcess, _process, Next());
        // Native delegates/IDs never cross processes. Saved body declarations
        // remain source identity only and must match a fresh living binding.
    }
    internal void RequireActors(IReadOnlyList<FalloutActorProcessRegistration> actual)
    {
        RequireNotBusy();
        if (!_current.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(actual.Select(actor => actor.Source.Reference)))
            throw new InvalidDataException("Current common owner omitted a real constructed process.");
        foreach (var actor in actual)
        {
            var state = Require(actor.Source.Reference);
            if (state.Source != actor.Source || state.Epoch != actor.Epoch ||
                actor.Retired != (state.Phase == FalloutProcessCommonPhase.Retired) || !actor.Retired && state.Level != actor.Level)
                throw new InvalidDataException("Actual common owner disagrees with the source manager's actor/process epoch/class.");
        }
    }
    internal static void RequireBody(FalloutActorProcessBodyBinding body, FalloutFormKey actor)
    {
        if (body is null || body.Actor != actor || string.IsNullOrWhiteSpace(body.SkeletonPath) ||
            body.SkeletonSha256 is not { Length: 64 } || !body.SkeletonSha256.All(Uri.IsHexDigit) ||
            body.BodyPartSha256 is not { Length: 64 } || !body.BodyPartSha256.All(Uri.IsHexDigit) ||
            body.BodyPartSource.ObjectId == 0 || string.IsNullOrWhiteSpace(body.BodyPartSource.OwnerPlugin) ||
            string.IsNullOrWhiteSpace(body.Owner) || body.Parts is null || body.Parts.Any(part => part is null ||
                part.PartType >= 15 || part.SourceNode is null || part.Block < 0 || part.SourceNode.Length == 0 && part.Block is not null) ||
            body.Parts.Select(part => part.PartType).Distinct().Count() != body.Parts.Count ||
            body.HeadBlock < 0 || body.TorsoBlock < 0 || body.Bip01Block < 0 || body.BoneLodController < 0 ||
            string.IsNullOrEmpty(body.HeadTarget) && body.HeadBlock is not null ||
            string.IsNullOrEmpty(body.TorsoTarget) && body.TorsoBlock is not null || body.Bip01Block is null && body.BoneLodController is not null)
            throw new InvalidDataException("Actual High initialization lost a complete source node/controller lookup declaration.");
    }
    internal static void Validate(FalloutProcessCommonSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Contract is not { Length: 64 } || !saved.Contract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 ||
            saved.Current is null || saved.Retired is null || saved.Current.Any(entry => entry is null || entry.Source is null) ||
            saved.Retired.Any(entry => entry is null || entry.Source is null) || saved.Pending is { Source: null } ||
            saved.Current.Select(entry => entry.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Current.Count)
            throw new InvalidDataException("Cold common owner has no complete selected/current ownership snapshot.");
        var ownership = new HashSet<Guid>();
        foreach (var retired in saved.Retired)
        {
            var actual = saved.Current.SingleOrDefault(entry => entry.Source.Reference == retired.Source.Reference);
            if (actual is null || actual.Source != retired.Source || retired.Epoch > actual.Epoch ||
                retired.Changed > actual.Changed || retired.Phase is not (FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired))
                throw new InvalidDataException("Cold common history contains a foreign or still-living retired source process.");
        }
        foreach (var entry in saved.Current.Concat(saved.Retired).Concat(saved.Pending is null ? [] : [saved.Pending]))
        {
            entry.Source.Validate();
            if (entry.Epoch < 1 || !Enum.IsDefined(entry.Level) || !Enum.IsDefined(entry.Phase) || entry.Scalars is null ||
                entry.Changed < 1 || entry.Changed > saved.Sequence || entry.Boundary is not null && string.IsNullOrWhiteSpace(entry.Boundary) ||
                entry.Epoch == 1 && entry.Level != (entry.Source.EnginePlayer ? FalloutDetectionProcessLevel.High : FalloutDetectionProcessLevel.Low) ||
                entry.Scalars != FalloutProcessCommonScalars.Constructed)
                throw new InvalidDataException("Cold common field/class lacks its real source constructor or admitted writer.");
            if (entry.Gameplay is { } lease)
            {
                if (lease.Source != entry.Source || lease.Ownership == Guid.Empty || !ownership.Add(lease.Ownership) ||
                    lease.ProcessEpoch != entry.Epoch || lease.Retired || lease.Changed < 1 || lease.Changed > entry.Changed ||
                    (entry.Phase is FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired) &&
                        !ReferenceEquals(entry, saved.Pending))
                    throw new InvalidDataException("Cold common process duplicated or lost its actual gameplay lease.");
            }
            else if (entry.Phase is not (FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired))
                throw new InvalidDataException("Cold living common process omitted its real actor gameplay lease.");
            if (entry.Body is { } body) RequireBody(body, entry.Source.Reference);
            ValidateSourceBodyEntry(entry);
        }
        if (saved.Transfer is { } transfer)
        {
            var actor = saved.Current.SingleOrDefault(entry => entry.Source.Reference == transfer.Actor);
            if (actor is null || transfer.BeforeEpoch < 1 || transfer.NewEpoch != checked(transfer.BeforeEpoch + 1) ||
                transfer.Before != FalloutDetectionProcessLevel.Low || transfer.After != FalloutDetectionProcessLevel.High ||
                string.IsNullOrWhiteSpace(transfer.Owner) || transfer.GameplayOwnership == Guid.Empty || !transfer.OldOwnershipCleared ||
                transfer.Changed < 1 || transfer.Changed > saved.Sequence || transfer.ObservedBeforeCopy is null ||
                transfer.ObservedBeforeCopy.Actor != transfer.Actor || GameplayHash(transfer.ObservedBeforeCopy) != transfer.GameplaySha256 ||
                transfer.Published && !transfer.OldRetired || transfer.Initialized && !transfer.Published ||
                transfer.Failure is not null && string.IsNullOrWhiteSpace(transfer.Failure) ||
                (transfer.Published ? saved.Pending is not null ||
                    (actor.Phase == FalloutProcessCommonPhase.Retired ? actor.Epoch != checked(transfer.NewEpoch + 1) || actor.Gameplay is not null ||
                        !saved.Retired.Any(old => old.Source == actor.Source && old.Epoch == transfer.NewEpoch &&
                            old.Phase == FalloutProcessCommonPhase.Retired && old.Gameplay is null) :
                        actor.Epoch != transfer.NewEpoch || actor.Gameplay?.Ownership != transfer.GameplayOwnership) :
                    saved.Pending is not { } pending || pending.Source != actor.Source || pending.Epoch != transfer.NewEpoch ||
                    pending.Gameplay?.Ownership != transfer.GameplayOwnership || actor.Epoch != transfer.BeforeEpoch) ||
                transfer.OldRetired && !saved.Retired.Any(old => old.Source.Reference == transfer.Actor && old.Epoch == transfer.BeforeEpoch && old.Gameplay is null))
                throw new InvalidDataException("Cold common transfer lost its actual old/new leases, phase or consumed gameplay prefix.");
        }
        else if (saved.Pending is not null || saved.Retired.Any(entry => entry.Phase == FalloutProcessCommonPhase.OldRetired))
            throw new InvalidDataException("Cold common graph has transferred owners without their real requesting factory.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Cold common owner lost its genuine new-process handoff.");
    }
}
