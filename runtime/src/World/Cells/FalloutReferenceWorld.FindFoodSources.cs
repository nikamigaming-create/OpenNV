using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutFindFoodSource(FalloutFormKey Reference, FalloutFormKey Item, int Count,
    bool Inventory, long? InventoryRevision);

// Sources are not Find eligibility or a chosen target. In particular, the
// player, nonresident cells, access, combat and retail category matching have
// independent owners that this resident scan cannot establish.
internal sealed record FalloutFindFoodSourceScan(IReadOnlyList<FalloutFindFoodSource> Sources,
    IReadOnlyList<FalloutFormKey> UninitializedInventories)
{
    internal bool PlayerInspected => false;
    internal bool NonresidentSourcesInspected => false;
}

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutFindFoodSourceScan FindFoodSources(FalloutFormKey actor, FalloutFindPackage package, float unitsToMetres)
    {
        if (package.Target is not { Type: 2, RawTarget: 18 })
            throw new NotSupportedException("Find source collection requires its Food-category target owner.");
        var center = package.Location(records, this, actor);
        var radius = package.Source.LocationRadius;
        if (radius <= 0) throw new NotSupportedException("Find Food needs its zero-radius search semantics.");
        var region = TravelRegion(center.Cell);
        var found = new List<FalloutFindFoodSource>();
        var uninitialized = new List<FalloutFormKey>();
        foreach (var state in ResidentSnapshot())
        {
            if (state.Reference == actor || state.Reference == records.RuntimeFormKey(0x14) ||
                state.Deleted || state.DeletePending || state.Taken || state.Destroyed || state.Templates?.Absent == true ||
                !IsEnabled(state.Reference)) continue;
            string signature;
            try { signature = BaseSignature(state.Base); }
            catch (KeyNotFoundException error)
            {
                if (records.GetEffective(state.Reference).ReadSubrecords().Any(field => field.Signature == "XPRM")) continue;
                throw new InvalidDataException($"Find source {state.Reference} has no winning base {state.Base}.", error);
            }
            if (signature is not ("ALCH" or "INGR" or "NPC_" or "CREA" or "CONT")) continue;
            var placement = SpatialPlacement(state.Reference, null, unitsToMetres);
            if (TravelRegion(placement.Cell) != region) continue;
            var distanceSquared = Enumerable.Range(0, 3).Sum(index =>
                Math.Pow((double)placement.Position[index] - center.Position[index], 2));
            if (distanceSquared > (double)radius * radius) continue;
            if (signature is "ALCH" or "INGR")
            {
                if (IsActorFood(state.Base))
                {
                    var counts = records.GetEffective(state.Reference).ReadSubrecords().Where(field => field.Signature == "XCNT").ToArray();
                    if (counts.Length > 1 || counts.Length == 1 && counts[0].Data.Length != 4)
                        throw new InvalidDataException($"Find source {state.Reference} has an invalid placed count.");
                    var count = counts.Length == 0 ? 1 : BinaryPrimitives.ReadInt32LittleEndian(counts[0].Data.Span);
                    if (count <= 0) throw new InvalidDataException($"Find source {state.Reference} has a nonpositive placed count.");
                    found.Add(new(state.Reference, state.Base, count, false, null));
                }
                continue;
            }
            // Looking must not create an inventory or draw a leveled-list roll.
            // Its absence is not evidence that the holder carries nothing.
            if (state.Inventory is not { } inventory) { uninitialized.Add(state.Reference); continue; }
            foreach (var item in inventory.Contents.Items)
                if (item.Count > 0 && IsActorFood(item.FormKey))
                    found.Add(new(state.Reference, item.FormKey, item.Count, true, inventory.Contents.Revision));
        }
        return new(found, uninitialized);
    }
}
