using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ReferenceAccessContracts
{
    private static void VerifyInheritedOwnership()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-owner-inheritance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] Zone(uint id, uint owner) => Record("ECZN", id, Field("DATA", Join(BitConverter.GetBytes(owner), [0xff, 0, 0, 0])));
            byte[] Cell(uint id, uint? zone = null, uint? owner = 11) => Record("CELL", id, Field("DATA", [1]),
                owner is { } declared ? Field("XOWN", BitConverter.GetBytes(declared)) : [], Field("XRNK", BitConverter.GetBytes(3)),
                zone is { } assigned ? Field("XEZN", BitConverter.GetBytes(assigned)) : []);
            var source = Join(Header(), Record("NPC_", 7), Record("NPC_", 8), Record("FACT", 11), Record("FACT", 12),
                Record("TERM", 4), Record("DOOR", 1), Record("ACTI", 2), Record("FURN", 3), Item("MISC", 5, "OwnedItem"),
                Zone(0x60, 12), Zone(0x61, 0), Zone(0x1e, 12), Zone(0x62, 1),
                Cell(0x80), Group(0x80,
                    Reference("REFR", 0x90, 4, "CellTerminal"),
                    Reference("REFR", 0x91, 4, "LocalTerminal", Field("XOWN", BitConverter.GetBytes(8u))),
                    Reference("REFR", 0x92, 4, "ReferenceZone", Field("XEZN", BitConverter.GetBytes(0x60u))),
                    Reference("REFR", 0x93, 1, "Door"), Reference("REFR", 0x94, 2, "Activator"),
                    Reference("REFR", 0x95, 3, "Furniture"), Reference("ACHR", 0x96, 8, "Actor"),
                    Reference("REFR", 0x97, 5, "Pickup")),
                Cell(0x81, 0x60), Group(0x81, Reference("REFR", 0xa0, 4, "CellZone")),
                Cell(0x82, 0x61), Group(0x82, Reference("REFR", 0xa1, 4, "EmptyZone")),
                Cell(0x83, 0x1e), Group(0x83, Reference("REFR", 0xa2, 4, "DefaultZone")),
                Cell(0x84, 0x62), Group(0x84, Reference("REFR", 0xa3, 4, "InvalidZoneOwner"),
                    Reference("REFR", 0xa4, 4, "LocalBeforeInvalidZone", Field("XOWN", BitConverter.GetBytes(8u)))),
                Cell(0x85, 0x60, null), Group(0x85,
                    Reference("REFR", 0xa5, 4, "EmptyReferenceZone", Field("XEZN", BitConverter.GetBytes(0x61u))),
                    Reference("REFR", 0xa6, 4, "DefaultReferenceZone", Field("XEZN", BitConverter.GetBytes(0x1eu)))));
            File.WriteAllBytes(Path.Combine(directory, "Access.esm"), source);
            using var records = FalloutPluginStack.Load(directory, ["Access.esm"]);
            using var world = new FalloutReferenceWorld(records);
            Require(world.Ownership(Key(0x90)).Owner == Key(11) && world.Ownership(Key(0x91)).Owner == Key(8) &&
                world.Ownership(Key(0x92)).Owner == Key(12) && world.Ownership(Key(0xa0)).Owner == Key(12) &&
                world.Ownership(Key(0xa1)).Owner == Key(11) && world.Ownership(Key(0xa2)).Owner == Key(11) &&
                world.Ownership(Key(0xa5)).Owner == Key(12) && world.Ownership(Key(0xa6)).Owner == Key(12),
                "Effective ownership lost local/zone/cell precedence or used the default zone owner.");
            foreach (var id in new uint[] { 0x93, 0x94, 0x95, 0x96 })
                Require(world.Ownership(Key(id)).Owner is null, "An excluded reference inherited a cell owner.");
            Require(world.Ownership(Key(0x92)).FactionRank == 3,
                "The inherited owner incorrectly replaced the independently selected rank.");
            Reject(() => world.Ownership(Key(0xa3)));
            Require(world.Ownership(Key(0xa4)).Owner == Key(8),
                "A valid local owner unnecessarily consumed the invalid inherited owner.");
            var inventory = new FalloutPlayerInventory();
            world.Take(Key(0x97), inventory, 1, null);
            Require(inventory.Item(Key(5))!.Variants!.Single().Owner == Key(11),
                "Ordinary pickup discarded effective cell ownership.");
            world.MoveTo(Key(0x90), Key(0xa0));
            Require(world.Ownership(Key(0x90)).Owner == Key(12),
                "Effective ownership used the editor cell after authoritative relocation.");
            world.SetOwnership(Key(0x90), Key(8));
            var snapshots = world.Capture();
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!);
            Require(cold.Ownership(Key(0x90)).Owner == Key(8) && cold.Ownership(Key(0x92)).Owner == Key(12) &&
                JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshots),
                "Cold ownership changed a retained override, source inheritance or another snapshot.");
            Require(File.ReadAllBytes(Path.Combine(directory, "Access.esm")).SequenceEqual(source), "Owner query changed source bytes.");
            Console.WriteLine("OPENNV_EFFECTIVE_REFERENCE_OWNER_PASS localZoneCell=true referenceZone=true " +
                "defaultZoneExcluded=true actorDoorActivatorFurnitureExcluded=true independentRank=true " +
                "pickupOwner=true relocation=true invalidInheritedOwnerRefused=true localPriority=true " +
                "overrideCold=true sourceImmutable=true alarmAndHackingSuccess=unverified");
        }
        finally { Directory.Delete(directory, true); }
    }
}
