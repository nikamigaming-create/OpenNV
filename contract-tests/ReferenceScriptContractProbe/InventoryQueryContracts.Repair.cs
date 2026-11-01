using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryQueryContracts
{
    private static void RepairAdmissionQueries()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-repair-admission-");
        try
        {
            byte[] RepairWeapon(uint id, uint type = 3, uint skill = 41, byte flags = 0, bool target = false, uint repairList = 0x210)
            {
                var data = new byte[204];
                BinaryPrimitives.WriteUInt32LittleEndian(data, type); data[12] = flags;
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(104), skill);
                return Record("WEAP", id, Field("EDID", Text("RepairWeapon" + id)), Field("DATA", new byte[15]),
                    Field("DNAM", data), Field("REPL", U32(target ? repairList : 0)));
            }
            byte[] RepairArmor(uint id, byte flags, uint equip)
            {
                var data = new byte[8]; data[0] = 1; data[4] = flags;
                return Record("ARMO", id, Field("EDID", Text("RepairArmor" + id)), Field("DATA", new byte[12]),
                    Field("BMDT", data), Field("ETYP", U32(equip)));
            }
            byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Repair.esm"), Join(Header(),
                RepairWeapon(0x200, target: true), RepairWeapon(0x201), RepairWeapon(0x202), RepairWeapon(0x203, type: 4),
                RepairWeapon(0x204, skill: 44), RepairWeapon(0x205, flags: 0x80),
                RepairWeapon(0x206, target: true, repairList: 0x212),
                Record("FLST", 0x210, Field("LNAM", U32(0)), Field("LNAM", U32(0x201)), Field("LNAM", U32(0x211))),
                Record("FLST", 0x211, Field("LNAM", U32(0x202))),
                Record("FLST", 0x212, Field("LNAM", U32(0x201))),
                RepairArmor(0x220, 0, 0), RepairArmor(0x221, 0, 0), RepairArmor(0x222, 8, 0),
                RepairArmor(0x223, 0, 1), RepairArmor(0x224, 0x40, 0),
                Setting(0x300, "fRepairMin", 1), Setting(0x301, "fRepairMax", 3), Setting(0x302, "fRepairScavengeMult", .5f)));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Repair.esm"]);
            FalloutFormKey Key(uint id) => new("Repair.esm", id);
            bool Allows(uint target, uint donor, bool jury) => FalloutRepairAdmission.Allows(records, Key(target), Key(donor), null, () => jury);
            Check(Allows(0x200, 0x200, false) && Allows(0x200, 0x201, false) && !Allows(0x200, 0x202, false),
                "Normal repair recursed a nested FLST, stopped at a null entry or lost same/direct-form admission.");
            Check(Allows(0x200, 0x202, true) && !Allows(0x200, 0x203, true) && !Allows(0x200, 0x204, true) &&
                !Allows(0x200, 0x205, true) && !Allows(0x200, 0x220, true),
                "Jury Rigging ignored exact weapon type/skill, donor playability or record kind.");
            Check(Allows(0x220, 0x221, true) && !Allows(0x220, 0x221, false) && !Allows(0x220, 0x222, true) &&
                !Allows(0x220, 0x223, true) && !Allows(0x220, 0x224, true),
                "Jury Rigging ignored armor class, equip category or donor playability.");
            using (var mutable = new FalloutReferenceWorld(records))
            {
                mutable.FormLists.Add(Key(0x212), Key(0x203));
                Check(FalloutRepairAdmission.Allows(records, Key(0x206), Key(0x203), mutable.FormLists, () => false),
                    "Normal repair ignored mutable direct list entries or incorrectly imposed the Jury category restriction.");
            }
            var inventory = new FalloutPlayerInventory(7);
            inventory.Add(records, Key(0x200), 1, 1, true, extra: new(1, Condition: .2f));
            inventory.Add(records, Key(0x202), 2, 1, true, extra: new(2, Condition: .3f));
            Check(inventory.RepairMaterials(records, Key(0x200), 0, juryRigging: () => false).Count == 0 &&
                inventory.RepairMaterials(records, Key(0x200), 0, juryRigging: () => true).Single().Extra.Count == 2,
                "Repair donor instance list did not use the current source perk result.");
            var before = JsonSerializer.Serialize(inventory.Capture()); var revision = inventory.Revision;
            inventory.PrepareChange = (_, _) => new(() => throw new InvalidOperationException("fixture repair refusal"), () => { });
            Reject(() => inventory.Repair(records, Key(0x200), 0, Key(0x202), 0, 50, revision, juryRigging: () => true));
            Check(inventory.Revision == revision && JsonSerializer.Serialize(inventory.Capture()) == before,
                "Refused perk repair retained donor removal, condition, instance or RNG publication.");
            inventory.PrepareChange = null;
            inventory.Repair(records, Key(0x200), 0, Key(0x202), 0, 50, revision, juryRigging: () => true);
            Check(inventory.Item(Key(0x202))!.Count == 1 && Math.Abs(inventory.Instances(Key(0x200))[0].Extra.Condition!.Value - .6f) < .00001f,
                "Perk repair did not consume one donor or retain the source GMST condition result.");
            var donors = new FalloutPlayerInventory(8);
            donors.Add(records, Key(0x200), 1, 1, true, extra: new(1, Condition: .2f));
            donors.Add(records, Key(0x200), 1, 1, true, extra: new(1, Condition: .3f));
            donors.Equip(records, Key(0x200));
            donors.Add(records, Key(0x200), 1, 1, true, extra: new(1, Condition: .4f, WeaponMods: 1, Instance: Guid.NewGuid()));
            var donorBefore = JsonSerializer.Serialize(donors.Capture());
            Check(donors.RepairMaterials(records, Key(0x200), 1, juryRigging: () => false).Count == 0 &&
                donors.RepairMaterials(records, Key(0x200), 2, juryRigging: () => false).Single().Variant == 1,
                "Repair consumed a worn/modified same-base donor or rejected a modified target's ordinary donor.");
            Reject(() => donors.Repair(records, Key(0x200), 1, Key(0x200), 2, 50, donors.Revision, juryRigging: () => false));
            Check(JsonSerializer.Serialize(donors.Capture()) == donorBefore, "Refused modified donor repair mutated equipment or inventory.");
            var saved = inventory.Capture(); var cold = new FalloutPlayerInventory();
            cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            Check(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved), "Repair condition/instance state did not survive cold restore.");
        }
        finally { directory.Delete(true); }
    }
}
