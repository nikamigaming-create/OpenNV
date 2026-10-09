using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private IFalloutSandboxSourceProducers? _sandboxSourceProducers;
    private Func<FalloutFormKey, FalloutSandboxPublishedReference?>? _sandboxPublishedReference;
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _sandboxMarkers = [];

    internal void BindSandboxSourceProducers(IFalloutSandboxSourceProducers source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (_sandboxSourceProducers is not null) throw new InvalidOperationException("Sandbox source producers already have a lifetime.");
        if (source.EngineSha256 != CampaignPlayerRuntimeSource.Receipt.EngineSha256)
            throw new InvalidDataException("Sandbox producer differs from the selected executable lifetime.");
        _sandboxSourceProducers = source;
    }

    internal void BindSandboxReferencePublication(Func<FalloutFormKey, FalloutSandboxPublishedReference?> reader)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(reader);
        if (_sandboxPublishedReference is not null) throw new InvalidOperationException("Sandbox native publication already has a lifetime.");
        _sandboxPublishedReference = reader;
    }

    internal void RetireSandboxReferencePublication(Func<FalloutFormKey, FalloutSandboxPublishedReference?> reader)
    {
        if (ReferenceEquals(_sandboxPublishedReference, reader)) _sandboxPublishedReference = null;
    }

    internal void RequireSandboxReferencePublication(Func<FalloutFormKey, FalloutSandboxPublishedReference?> reader)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(_sandboxPublishedReference, reader))
            throw new InvalidOperationException("Sandbox publication lease no longer belongs to this actual source presenter.");
    }

    private IFalloutSandboxSourceProducers SandboxSourceProducers
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sandboxSourceProducers ?? throw new NotSupportedException(
                "Sandbox source CELL list/exterior traversal and per-reference admission producers are not constructed.");
        }
    }

    internal FalloutSandboxTimerSample SandboxTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_sandboxSourceProducers ?? throw new NotSupportedException("Sandbox cached UInt32 timer writer/lifetime is unowned.")).Timer();
    }
    internal FalloutSandboxActionContext SandboxContext(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_sandboxSourceProducers ?? throw new NotSupportedException("Sandbox calendar window and independent dialogue-process scalar producers are unowned.")).Context(actor);
    }
    internal FalloutSandboxPublishedReference RequireSandboxPublication(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sample = (_sandboxPublishedReference ?? throw new NotSupportedException(
            "Sandbox discovery has no actual resident reference publication reader."))(reference) ??
            throw new NotSupportedException("Sandbox selected reference has no current published native model.");
        sample.Validate();
        if (sample.Reference != reference || !sample.HasModel || !IsResident(reference) || !IsEnabled(reference))
            throw new NotSupportedException("Sandbox selected native publication is unavailable or has changed source identity.");
        return sample;
    }

    internal FalloutSandboxDiscovery DiscoverSandbox(FalloutFormKey actor, FalloutSandboxPackage source, FalloutSandboxArea area)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); area.Validate();
        var caller = Actor(actor);
        if (!IsEnabled(actor) || caller.Deleted || caller.DeletePending || caller.Destroyed || caller.Taken)
            throw new NotSupportedException("Sandbox source actor is unavailable.");
        _ = FalloutSandboxActionSource.Read(CampaignPlayerRuntimeSource.Receipt);
        var producer = SandboxSourceProducers;
        var radius = area.Radius > 0 ? area.Radius : records.NumericSettings.Float("fSandBoxSearchRadius");
        var extraDialogue = records.NumericSettings.Float("fSandBoxExtraDialogueRange");
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(extraDialogue) || radius + extraDialogue < 0)
            throw new InvalidDataException("Sandbox search/dialogue radius is invalid.");
        var origin = area.Center ?? RequireSandboxPublication(actor).Position;
        var cells = FalloutCellSceneReader.ReadDefinition(records, area.Cell);
        var candidates = new List<FalloutSandboxCandidate>();
        var ordered = producer.References(actor, area, Math.Max(radius, radius + extraDialogue));
        if (ordered is null || ordered.Count != ordered.Distinct().Count())
            throw new InvalidDataException("Sandbox source CELL iterator is absent or duplicates a live reference.");
        foreach (var reference in ordered)
        {
            var state = Get(reference);
            if (state.Deleted || state.DeletePending || state.Destroyed || state.Taken || !IsEnabled(reference) || !IsResident(reference)) continue;
            var baseRecord = records.GetEffective(state.Base);
            var publication = (_sandboxPublishedReference ?? throw new NotSupportedException(
                "Sandbox discovery has no actual resident native publication reader."))(reference);
            if (publication is null || !publication.HasModel)
            {
                if (baseRecord.Signature is "NPC_" or "CREA" || baseRecord.ReadSubrecords().Any(row =>
                    row.Signature == "MODL" && !string.IsNullOrWhiteSpace(FalloutDialogueTopic.Text(row.Data.Span))))
                    throw new NotSupportedException("Sandbox source reference declares a body/model but its actual native publication is unavailable.");
                continue;
            }
            publication.Validate();
            if (publication.Reference != reference) throw new InvalidDataException("Sandbox native publication changed its reference identity.");
            FalloutSandboxAction? action = baseRecord.Signature switch
            {
                "FURN" => FalloutFurnitureSource.ReadKind(baseRecord) == FalloutPlayerFurnitureKind.Sleeping
                    ? FalloutSandboxAction.Sleeping : FalloutSandboxAction.Furniture,
                "ALCH" or "INGR" => FalloutSandboxAction.Eating,
                "IDLM" => FalloutSandboxAction.IdleMarker,
                "NPC_" or "CREA" => FalloutSandboxAction.Dialogue,
                _ => null,
            };
            if (action is null || FalloutSandboxActionSource.Vetoed(source, action.Value) ||
                action == FalloutSandboxAction.Dialogue && (reference == actor || records.RuntimeFormId(reference) == 0x14)) continue;
            var candidateCell = FalloutCellSceneReader.ReadDefinition(records, publication.Cell);
            if (cells.Worldspace is null ? publication.Cell != area.Cell : candidateCell.Worldspace != cells.Worldspace) continue;
            var limit = action == FalloutSandboxAction.Dialogue ? radius + extraDialogue : radius;
            var x = publication.Position[0] - (double)origin[0];
            var y = publication.Position[1] - (double)origin[1];
            var z = publication.Position[2] - (double)origin[2];
            if (x * x + y * y + z * z > (double)limit * limit) continue;
            if (producer.IgnoredBySandbox(reference) || !producer.GeneralActorAdmission(actor, reference)) continue;
            if (Ownership(reference).Owner is not null && !producer.OwnershipAdmission(actor, reference)) continue;
            if (action == FalloutSandboxAction.IdleMarker &&
                (_sandboxMarkers.TryGetValue(reference, out var occupant) && occupant != actor ||
                !producer.MarkerOccupationAdmission(actor, reference, 127) ||
                !producer.MarkerCompatible(actor, reference, FalloutIdleCollection.Read(baseRecord)))) continue;
            if (action == FalloutSandboxAction.Dialogue && !producer.DialogueAdmission(actor, reference)) continue;
            candidates.Add(new(reference, (int)action.Value, 1, state.Base,
                FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(reference)).ToLowerInvariant(),
                FalloutActorFurnitureContinuation.RecordHash(baseRecord).ToLowerInvariant()));
        }
        // Inventory eating and wandering are distinct null entries after the
        // actual placed-reference traversal. Source food eligibility is not an
        // arbitrary nonempty inventory test.
        if (!source.NoEating && producer.EligibleInventoryFood(actor)) candidates.Add(new(null, (int)FalloutSandboxAction.Eating, 1));
        if (!source.NoWandering) candidates.Add(new(null, (int)FalloutSandboxAction.Wandering, 1));
        var result = new FalloutSandboxDiscovery(candidates,
            new(candidates.Any(value => value.Action == 2), candidates.Any(value => value.Action == 0), candidates.Any(value => value.Action == 1)));
        result.Validate(); return result;
    }

    internal bool ReserveSandboxMarker(FalloutFormKey marker, FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (records.GetEffective(Get(marker).Base).Signature != "IDLM") throw new InvalidDataException("Sandbox reservation is not a source IDLM.");
        _ = Actor(actor); _ = RequireSandboxPublication(marker);
        if (_sandboxMarkers.TryGetValue(marker, out var existing)) return existing == actor;
        if (!SandboxSourceProducers.MarkerOccupationAdmission(actor, marker, 127)) return false;
        _sandboxMarkers.Add(marker, actor); return true;
    }

    internal void RequireSandboxCandidateSource(FalloutSandboxCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); candidate.Validate();
        if (candidate.Reference is not { } reference) return;
        candidate.RequireSourceIdentity();
        var instance = Get(reference);
        if (instance.Base != candidate.Base ||
            !FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(reference)).Equals(candidate.ReferenceSha256, StringComparison.OrdinalIgnoreCase) ||
            !FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(instance.Base)).Equals(candidate.BaseSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sandbox cached reference/base differs from its retained winning source bytes.");
    }

    internal void ReleaseSandboxMarker(FalloutFormKey marker, FalloutFormKey actor)
    {
        if (_sandboxMarkers.TryGetValue(marker, out var existing) && existing == actor) _sandboxMarkers.Remove(marker);
    }

    private void RetireSandboxSourceProducers()
    {
        _sandboxPublishedReference = null; _sandboxSourceProducers = null;
        if (_sandboxMarkers.Count != 0)
            throw new NotSupportedException("Sandbox source retirement retains actual unclosed marker action owners.");
    }
}
