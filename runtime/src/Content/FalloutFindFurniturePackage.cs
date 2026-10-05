using System.Buffers.Binary;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFindFurniturePackage(FalloutFormKey Form, uint Flags, FalloutFormKey Location,
    int Radius, int TargetType, FalloutFormKey? Target)
{
    internal bool Running => (Flags & 0x2000) != 0;

    internal static FalloutFindFurniturePackage Read(FalloutPluginRecord record)
    {
        var source = FalloutScriptPackage.Read(record);
        if (source.Procedure != 0 || source.LocationType != 0 || source.LocationReference is null)
            throw new NotSupportedException("Find Furniture requires its near-reference search location owner.");
        var fields = record.ReadSubrecords().ToArray();
        var data = FalloutPackageData.Read(record);
        var flags = data.Flags;
        const uint supported = 2 | 4 | 0x400 | 0x1000 | 0x2000;
        if ((flags & ~supported) != 0 ||
            (flags & 0x1000) == 0 && data.BehaviorFlags != 0 || data.SpecificFlags is not (null or 0) ||
            fields.Any(field => field.Signature is "PLD2" or "PTD2"))
            throw new NotSupportedException("Find Furniture has additional unowned behavior or search inputs.");
        var targets = fields.Where(field => field.Signature == "PTDT").ToArray();
        if (targets.Length != 1 || targets[0].Data.Length != 16)
            throw new InvalidDataException("Find Furniture requires one complete target declaration.");
        var target = targets[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(target);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(target[4..]);
        if (BinaryPrimitives.ReadInt32LittleEndian(target[8..]) != 0)
            throw new NotSupportedException("Find Furniture target count/distance needs its duration owner.");
        FalloutFormKey? key = type switch
        {
            0 or 1 => record.Plugin.AdjustOptionalFormId(id) ?? throw new InvalidDataException("Find Furniture target is absent."),
            2 when id == 11 => null,
            _ => throw new NotSupportedException("Find target requires its non-furniture interaction owner."),
        };
        return new(record.FormKey, flags, source.LocationReference.Value, source.LocationRadius, type, key);
    }

    internal IReadOnlyList<FalloutPlacedReference> Candidates(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutCellScene cell)
    {
        var actorState = world.Get(actor);
        if (records.GetEffective(actorState.Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Find Furniture caller is not an actor.");
        var location = world.Placement(Location);
        if (location.Cell != cell.Cell.FormKey)
            throw new NotSupportedException("Find Furniture search location requires its other-cell route owner.");
        return cell.References.Where(reference => Eligible(reference)).ToArray();

        bool Eligible(FalloutPlacedReference reference)
        {
            if (cell.BaseObjects[reference.Base].Signature != "FURN" || !world.CanActivate(reference.FormKey) ||
                TargetType == 0 && Target != reference.FormKey || TargetType == 1 && Target != reference.Base) return false;
            var placement = world.Placement(reference.FormKey);
            if (placement.Cell != location.Cell || Enumerable.Range(0, 3).Sum(index =>
                Math.Pow((double)placement.Position[index] - location.Position[index], 2)) > (double)Radius * Radius) return false;
            var furniture = records.GetEffective(reference.Base);
            var markers = furniture.ReadSubrecords().Single(field => field.Signature == "MNAM").Data;
            if (markers.Length != 4) throw new InvalidDataException("Find Furniture has invalid source marker flags.");
            if ((BinaryPrimitives.ReadUInt32LittleEndian(markers.Span) & 0xc0000000u) != 0x40000000u) return false;
            var ownership = world.Ownership(reference.FormKey);
            if (ownership.Global is not null)
                throw new NotSupportedException("Find Furniture global ownership needs its access-condition owner.");
            if (ownership.Owner is not { } owner) return true;
            return records.GetEffective(owner).Signature switch
            {
                "NPC_" or "CREA" => owner == actorState.Base,
                "ACHR" or "ACRE" => owner == actor,
                "FACT" => world.ActorFactions(actor).GetValueOrDefault(owner, (sbyte)-1) >= Math.Max(0, ownership.FactionRank ?? 0),
                _ => throw new InvalidDataException("Find Furniture has invalid source ownership."),
            };
        }
    }
}
