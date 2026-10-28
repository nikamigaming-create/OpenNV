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
    private readonly Dictionary<FalloutFormKey, uint> _furnitureMarkerFlags = [];
    private readonly Dictionary<FalloutFormKey, string> _baseSignatures = [];
    private readonly HashSet<FalloutFormKey> _sandboxPrimitiveReferences = [];
    private readonly Dictionary<FalloutFormKey, bool> _actorFoodSources = [];

    internal bool IsActorFood(FalloutFormKey form)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_actorFoodSources.TryGetValue(form, out var food))
            _actorFoodSources.Add(form, food = FalloutActorFood.IsFood(records, form));
        return food;
    }

    private string BaseSignature(FalloutFormKey baseForm)
    {
        if (!_baseSignatures.TryGetValue(baseForm, out var sig))
            _baseSignatures[baseForm] = sig = records.GetEffective(baseForm).Signature;
        return sig;
    }

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
        foreach (var instance in ResidentSnapshot())
        {
            if (instance.Reference == actor || instance.Reference == records.RuntimeFormKey(0x14) ||
                instance.Deleted || instance.DeletePending || instance.Taken || !IsEnabled(instance.Reference)) continue;
            if (_sandboxPrimitiveReferences.Contains(instance.Reference)) continue;
            string signature;
            try { signature = BaseSignature(instance.Base); }
            catch (KeyNotFoundException error)
            {
                // CreateInstance admits an engine primitive with no ESM base
                // when its placed reference owns XPRM. Such a trigger is not
                // an actor, food, idle marker or furniture candidate.
                if (records.GetEffective(instance.Reference).ReadSubrecords().Any(field => field.Signature == "XPRM"))
                {
                    _sandboxPrimitiveReferences.Add(instance.Reference);
                    continue;
                }
                // Missing winning content must remain a visible package
                // divergence, rather than escaping the actor's failure owner
                // on every physics frame without identifying the reference.
                throw new InvalidDataException($"Sandbox candidate {instance.Reference} has no winning base {instance.Base}.", error);
            }
            var activity = signature switch
            {
                "NPC_" or "CREA" => FalloutSandboxActivity.Dialogue,
                "IDLM" => FalloutSandboxActivity.IdleMarker,
                "INGR" or "ALCH" when package.Activities.Allows(FalloutSandboxActivity.Eat) && IsActorFood(instance.Base)
                    => FalloutSandboxActivity.Eat,
                "FURN" => FurnitureActivity(instance.Base, package.Activities),
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
                item.Count > 0 && IsActorFood(item.FormKey)))
                result.Add(new(FalloutSandboxActivity.Eat, null, null, true));
        }
        if (package.Activities.Allows(FalloutSandboxActivity.Wander))
            result.Add(new(FalloutSandboxActivity.Wander, null, null, false));
        return result;
    }

    private FalloutSandboxActivity? FurnitureActivity(FalloutFormKey furnitureKey, FalloutSandboxActivities activities)
    {
        if (!activities.Allows(FalloutSandboxActivity.Sleep) && !activities.Allows(FalloutSandboxActivity.Furniture))
            return null;
        if (!_furnitureMarkerFlags.TryGetValue(furnitureKey, out var flags))
        {
            var furniture = records.GetEffective(furnitureKey);
            var fields = furniture.ReadSubrecords().Where(field => field.Signature == "MNAM").ToArray();
            if (fields.Length != 1 || fields[0].Data.Length != 4)
                throw new InvalidDataException("Sandbox furniture has an invalid marker declaration.");
            flags = BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span);
            _furnitureMarkerFlags[furnitureKey] = flags;
        }
        if ((flags & 0x80000000) != 0 && activities.Allows(FalloutSandboxActivity.Sleep))
            return FalloutSandboxActivity.Sleep;
        if (activities.Allows(FalloutSandboxActivity.Furniture) &&
            ((flags & 0x40000000) != 0 || (flags & 0xc0000000) == 0))
            return FalloutSandboxActivity.Furniture;
        return null;
    }
}
