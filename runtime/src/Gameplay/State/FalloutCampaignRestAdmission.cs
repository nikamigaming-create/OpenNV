using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// The campaign's proven data/state facts are read from their existing living
// owners. Other source predicates must be supplied independently; absence never
// becomes zero crime, zero radiation, an awake actor, or an owned world tick.
internal sealed class FalloutCampaignRestAdmission
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutReferenceWorld _references;
    private readonly FalloutPlayerVitals _vitals;
    private readonly Func<FalloutFormKey> _currentCell;
    private readonly Func<FalloutSleepWaitBed, FalloutRestObservation> _currentBedModel;
    private readonly Func<FalloutRestRequest, FalloutRestFact, FalloutRestObservation> _other;

    internal FalloutCampaignRestAdmission(FalloutPluginStack records, FalloutReferenceWorld references,
        FalloutPlayerVitals vitals, Func<FalloutFormKey> currentCell,
        Func<FalloutSleepWaitBed, FalloutRestObservation> currentBedModel,
        Func<FalloutRestRequest, FalloutRestFact, FalloutRestObservation> otherSourcePredicate)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(vitals); ArgumentNullException.ThrowIfNull(currentCell);
        ArgumentNullException.ThrowIfNull(currentBedModel); ArgumentNullException.ThrowIfNull(otherSourcePredicate);
        _records = records; _references = references; _vitals = vitals; _currentCell = currentCell;
        _currentBedModel = currentBedModel; _other = otherSourcePredicate;
    }

    internal FalloutRestObservation Observe(FalloutRestRequest request, FalloutRestFact fact)
    {
        request.Validate();
        if (!Enum.IsDefined(fact)) throw new ArgumentOutOfRangeException(nameof(fact));
        var observed = fact switch
        {
            FalloutRestFact.PlayerAlive => Alive(),
            FalloutRestFact.CellAllowsRest => FalloutRestLocation.Read(_records, _currentCell()).Observe(),
            FalloutRestFact.BedEnabled => _references.ObserveRestBedEnabled(request, _currentCell()),
            FalloutRestFact.BedOwnershipAllowsPlayer => _references.ObserveRestBedOwnership(request),
            FalloutRestFact.BedMarkerAvailable => _references.ObserveRestBedOccupancy(request, _currentBedModel),
            _ => _other(request, fact),
        } ?? throw new InvalidDataException("Campaign rest predicate returned no actual observation.");
        observed.Validate(); return observed;
    }

    private FalloutRestObservation Alive()
    {
        var state = _vitals.State;
        return new(state.ExactHitPoints > 0 ? FalloutRestFactState.Satisfied : FalloutRestFactState.Denied,
            "current-player-vitals:" + state.ExactHitPoints,
            state.ExactHitPoints > 0 ? null : "The actual player has no positive current hit points.");
    }
}
