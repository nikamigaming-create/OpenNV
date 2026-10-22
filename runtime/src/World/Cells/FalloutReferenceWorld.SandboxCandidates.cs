using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Collection precedes eligibility and weighted choice. A potential source is
// not permission to use furniture, eat an item or start a conversation.
internal sealed record FalloutSandboxCandidateSource(FalloutSandboxActivity Activity,
    FalloutFormKey? Reference, FalloutFormKey? Base, bool Inventory);

internal sealed partial class FalloutReferenceWorld
{
    internal IReadOnlyList<FalloutSandboxCandidateSource> SandboxCandidateSources(FalloutFormKey actor,
        FalloutSandboxPackage package, int level, FalloutGlobalState? globals, float unitsToMetres)
    {
        var location = package.Location(records, this, actor);
        var radius = package.Radius > 0 ? package.Radius : records.NumericSettings.Float("fSandBoxSearchRadius");
        if (!float.IsFinite(radius) || radius < 0) throw new InvalidDataException("Sandbox search radius is invalid.");
        var center = new Vector3(location.Location[0], location.Location[1], location.Location[2]);
        var region = TravelRegion(location.Cell);
        var result = new List<FalloutSandboxCandidateSource>();
        // Snapshot residency: Get/Inventory may materialize additional state,
        // but that must not change this enumeration underneath the search.
        foreach (var instance in ResidentInstances.ToArray())
        {
            if (instance.Reference == actor || instance.Reference == records.RuntimeFormKey(0x14) ||
                instance.Deleted || instance.DeletePending || instance.Taken || !IsEnabled(instance.Reference)) continue;
            var basis = records.GetEffective(instance.Base);
            var activity = basis.Signature switch
            {
                "NPC_" or "CREA" => FalloutSandboxActivity.Dialogue,
                "IDLM" => FalloutSandboxActivity.IdleMarker,
                "INGR" or "ALCH" when FalloutActorFood.IsFood(records, instance.Base) => FalloutSandboxActivity.Eat,
                "FURN" => FurnitureActivity(basis, package.Activities),
                _ => (FalloutSandboxActivity?)null
            };
            if (activity is not { } kind || !package.Activities.Allows(kind)) continue;
            var placement = SpatialPlacement(instance.Reference, null, unitsToMetres);
            if (TravelRegion(placement.Cell) != region) continue;
            var point = new Vector3(placement.Position[0], placement.Position[1], placement.Position[2]);
            var candidateRadius = kind == FalloutSandboxActivity.Dialogue
                ? radius + records.NumericSettings.Float("fSandBoxExtraDialogueRange") : radius;
            if (!float.IsFinite(candidateRadius) || candidateRadius < 0)
                throw new InvalidDataException("Sandbox candidate search radius is invalid.");
            if (point.DistanceSquaredTo(center) > candidateRadius * candidateRadius) continue;
            result.Add(new(kind, instance.Reference, instance.Base, false));
        }
        if (package.Activities.Allows(FalloutSandboxActivity.Eat))
        {
            if (Inventory(actor, level, globals).Contents.Items.Any(item =>
                item.Count > 0 && FalloutActorFood.IsFood(records, item.FormKey)))
                result.Add(new(FalloutSandboxActivity.Eat, null, null, true));
        }
        if (package.Activities.Allows(FalloutSandboxActivity.Wander))
            result.Add(new(FalloutSandboxActivity.Wander, null, null, false));
        return result;
    }

    private static FalloutSandboxActivity? FurnitureActivity(FalloutPluginRecord furniture, FalloutSandboxActivities activities)
    {
        var fields = furniture.ReadSubrecords().Where(field => field.Signature == "MNAM").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Sandbox furniture has an invalid marker declaration.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span);
        if ((flags & 0x80000000) != 0 && activities.Allows(FalloutSandboxActivity.Sleep))
            return FalloutSandboxActivity.Sleep;
        if (activities.Allows(FalloutSandboxActivity.Furniture) &&
            ((flags & 0x40000000) != 0 || (flags & 0xc0000000) == 0))
            return FalloutSandboxActivity.Furniture;
        return null;
    }
}
