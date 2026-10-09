using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutSandboxPublishedReference(FalloutFormKey Reference, FalloutFormKey Cell,
    float[] Position, bool HasModel, ulong Publication)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 || Publication <= 0 ||
            Position is not { Length: 3 } || Position.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Sandbox candidate has no actual native source publication.");
    }
}

// These are distinct source owners, not defaults inferred from loaded records.
// In particular a source list is the living CELL traversal, not RuntimeFormId,
// distance or filesystem order. The cached UInt32 timer is not the game calendar.
internal interface IFalloutSandboxSourceProducers
{
    string EngineSha256 { get; }
    FalloutSandboxTimerSample Timer();
    IReadOnlyList<FalloutFormKey> References(FalloutFormKey actor, FalloutSandboxArea area, float radius);
    FalloutSandboxActionContext Context(FalloutFormKey actor);
    float ActionElapsedSeconds(FalloutFormKey actor);
    bool IgnoredBySandbox(FalloutFormKey reference);
    bool GeneralActorAdmission(FalloutFormKey actor, FalloutFormKey reference);
    bool OwnershipAdmission(FalloutFormKey actor, FalloutFormKey reference);
    bool MarkerCompatible(FalloutFormKey actor, FalloutFormKey reference, FalloutIdleCollection source);
    bool MarkerOccupationAdmission(FalloutFormKey actor, FalloutFormKey reference, byte markerIndex);
    bool DialogueAdmission(FalloutFormKey actor, FalloutFormKey reference);
    bool EligibleInventoryFood(FalloutFormKey actor);
}
