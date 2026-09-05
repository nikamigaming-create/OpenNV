using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class Synthetic
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-npc-appearance-");
        try
        {
            var records = new List<byte[]> {
                Npc(0x100, 0, 0x101), Npc(0x101, 0, 0, female: true, race: 0x201, armor: 0x401, hair: 0x301),
                Npc(0x102, 1, 0x101), Npc(0x103, 64, 0x101), Npc(0x104, 256, 0x101),
                Npc(0x105, 1, 0x106), Npc(0x106, 1, 0x105), Npc(0x107, 64, 0x700),
                Npc(0x108, 0, 0, extraArmor: 0x402), Npc(0x109, 0, 0, armor: 0x701),
                Npc(0x10a, 0, 0, armor: 0x702), Npc(0x10b, 0, 0, armor: 0x403),
                Npc(0x10c, 0, 0, head: 0x304),
                Race(0x200, "raceA"), Race(0x201, "raceB"),
                Record("HAIR", 0x300, Field("MODL", Z("hairA.nif")), Field("ICON", Z("hairA.dds")), Field("DATA", [0])),
                Record("HAIR", 0x301, Field("MODL", Z("hairB.nif")), Field("ICON", Z("hairB.dds")), Field("DATA", [8])),
                Record("EYES", 0x310, Field("ICON", Z("eye.dds"))),
                Head(0x302, 0x303), Head(0x303, 0), Head(0x304, 0x305), Head(0x305, 0x304),
                Armor(0x400, "base.nif", 4, 0x410), Armor(0x401, "template.nif", 4, 0),
                Armor(0x402, "conflict.nif", 4, 0),
                Record("ARMO", 0x403, Field("BMDT", Combine(U32(4), U32(0))), Field("MOD3", Z("female-only.nif"))),
                Record("FLST", 0x410, Field("LNAM", U32(0x411)), Field("LNAM", U32(0x412))),
                Record("ARMA", 0x411, Field("BMDT", Combine(U32(8), U32(0))), Field("MODL", Z("glove-left.nif")), Field("MOD3", Z("glove-left-f.nif"))),
                Record("ARMA", 0x412, Field("BMDT", Combine(U32(16), U32(0))), Field("MODL", Z("glove-right.nif")), Field("MOD3", Z("glove-right-f.nif"))),
                Record("TXST", 0x500, Field("TX00", Z("cloth.dds")), Field("TX01", Z("cloth_n.dds"))),
                Record("ALCH", 0x600), Record("LVLN", 0x700), Leveled(0x701, 0x400), Leveled(0x702, 0x600),
            };
            File.WriteAllBytes(Path.Combine(directory.FullName, "base.esm"), Combine(Header(null), Combine(records.ToArray())));
            File.WriteAllBytes(Path.Combine(directory.FullName, "override.esp"), Combine(Header("base.esm"),
                Armor(0x400, "winning.nif", 4, 0x410), Record("TXST", 0x500, Field("TX00", Z("winning.dds")))));
            using var stack = FalloutPluginStack.Load(directory.FullName, ["base.esm", "override.esp"]);
            var appearance = FalloutNpcAppearanceResolver.Resolve(stack, Key(0x100));
            FaceMaterialSynthetic.Run(stack, appearance);
            var hairPart = appearance.Models.Single(part => part.Role == "hair");
            Require(FalloutNpcAppearanceHairShape.Select(appearance, hairPart) == "NoHat",
                "Bare head selected the hat variant.");
            var hat = appearance.Armor[0] with { Model = appearance.Armor[0].Model with { BipedSlots = 0x400 } };
            Require(FalloutNpcAppearanceHairShape.Select(appearance with { Armor = [hat] }, hairPart) == "Hat",
                "Equipped hat did not select its hair variant.");
            Require(FalloutNpcAppearanceHairShape.Select(appearance with { Armor = [hat], EquippedArmor = [] }, hairPart) == "NoHat",
                "An unequipped inventory hat changed hair presentation.");
            Require(FalloutNpcAppearanceHairColor.Resolve(stack, appearance, hairPart) ==
                new System.Numerics.Vector3(1 / 255.0f, 2 / 255.0f, 3 / 255.0f), "HCLR RGB excludes unused fourth byte");
            Require(FalloutNpcAppearanceHairColor.Resolve(stack, appearance, hairPart with { Source = Key(0x301) }) ==
                System.Numerics.Vector3.One, "fixed-colour HAIR ignores NPC tint");
            Require(FalloutNpcAppearanceHairColor.Resolve(stack, appearance, appearance.Models.First(part => part.Role == "head-addon")) ==
                FalloutNpcAppearanceHairColor.Decode(appearance.HairColorBytes), "hair-flagged head addon receives NPC tint");
            Throws(() => FalloutNpcAppearanceHairColor.Decode([1, 2, 3]), "truncated HCLR");
            Throws(() => FalloutNpcAppearanceHairColor.Decode([1, 2, 3, 4, 5]), "oversized HCLR");
            Require(appearance.CanConstruct && appearance.Race == Key(0x200) && !appearance.Female, "unused template must not replace traits");
            Require(appearance.Models.Single(part => part.Role == "armor").ModelPath == "meshes/winning.nif", "winning armor override");
            Require(appearance.Models.Count(part => part.Role == "armor-addon") == 2 &&
                !appearance.Models.Any(part => part.Role is "body" or "hand-left" or "hand-right"), "BIPL addons replace source slots");
            Require(appearance.Models.Count(part => part.Role == "head-addon") == 2, "recursive HDPT addons");
            var texture = appearance.Models.Single(part => part.Role == "armor").AlternateTextures.Single();
            Require(texture.ShapeName == "BodyShape" && texture.ShapeIndex == 2 && texture.TextureSet == Key(0x500) &&
                texture.Textures["TX00"] == "textures/winning.dds", "shape/index/adjusted winning texture binding");
            Require(appearance.FaceGen.SymmetricGeometry[0] == 0x100 % 251 && appearance.RaceParts.Any(part => part.Role == "ears" && part.ModelPath is null), "raw FaceGen and texture-only race part");
            var traits = FalloutNpcAppearanceResolver.Resolve(stack, Key(0x102));
            Require(traits.Female && traits.Race == Key(0x201) && traits.ModelOwner == Key(0x102), "independent traits inheritance");
            var model = FalloutNpcAppearanceResolver.Resolve(stack, Key(0x103));
            Require(!model.Female && model.Race == Key(0x200) && model.Hair == Key(0x301) && model.FaceGen.Source == Key(0x101), "independent model inheritance");
            Require(FalloutNpcAppearanceResolver.Resolve(stack, Key(0x104)).EquippedArmor.Single() == Key(0x401), "inventory inheritance");
            Throws(() => FalloutNpcAppearanceResolver.Resolve(stack, Key(0x105)), "template cycle");
            Throws(() => FalloutNpcAppearanceResolver.Resolve(stack, Key(0x107)), "leveled actor selection");
            Require(!FalloutNpcAppearanceResolver.Resolve(stack, Key(0x108)).CanConstruct, "competing armor remains unresolved");
            Require(!FalloutNpcAppearanceResolver.Resolve(stack, Key(0x109)).CanConstruct, "leveled armor remains unresolved");
            Require(FalloutNpcAppearanceResolver.Resolve(stack, Key(0x10a)).CanConstruct, "non-armor leveled inventory does not change appearance");
            Require(!FalloutNpcAppearanceResolver.Resolve(stack, Key(0x10b)).CanConstruct, "absent sex-specific equipped model stays visible");
            Throws(() => FalloutNpcAppearanceResolver.Resolve(stack, Key(0x10c)), "head part cycle");
            var explicitEquipment = FalloutNpcAppearanceResolver.Resolve(stack, Key(0x108), equippedArmor: [Key(0x402)]);
            Require(explicitEquipment.CanConstruct && explicitEquipment.EquippedArmor.Single() == Key(0x402), "authoritative explicit equipment selection");
            Console.WriteLine("OPENNV_NPC_APPEARANCE_CONTRACT_OK winningOverrides=true templateGroups=true completeParts=true armorAddons=true slotConflictsVisible=true rawFaceGen=true");
        }
        finally { directory.Delete(recursive: true); }
    }

    private static byte[] Npc(uint id, ushort templateFlags, uint template, bool female = false, uint race = 0x200,
        uint armor = 0x400, uint hair = 0x300, uint head = 0x302, uint extraArmor = 0)
    {
        var acbs = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(acbs, female ? 1u : 0u);
        BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), templateFlags);
        var fields = new List<byte[]> { Field("ACBS", acbs), Field("RNAM", U32(race)), Field("MODL", Z("skeleton.nif")),
            Field("TPLT", U32(template)), Field("HNAM", U32(hair)), Field("ENAM", U32(0x310)),
            Field("PNAM", U32(head)), Field("HCLR", [1, 2, 3, 4]), Field("LNAM", F32(0.25f)),
            Field("CNTO", Combine(U32(armor), U32(1))),
            Field("FGGS", Enumerable.Repeat((byte)(id % 251), 200).ToArray()), Field("FGGA", new byte[120]), Field("FGTS", new byte[200]) };
        if (extraArmor != 0) fields.Add(Field("CNTO", Combine(U32(extraArmor), U32(1))));
        return Record("NPC_", id, fields.ToArray());
    }

    private static byte[] Race(uint id, string label)
    {
        var data = new byte[36];
        for (var offset = 16; offset < 32; offset += 4) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), 1);
        var fields = new List<byte[]> { Field("DATA", data), Field("NAM0", []) };
        foreach (var sex in new[] { "MNAM", "FNAM" })
        {
            fields.Add(Field(sex, []));
            foreach (var part in new uint[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                fields.Add(Field("INDX", U32(part)));
                if (part != 1) fields.Add(Field("MODL", Z($"{label}/{sex}/head{part}.nif")));
                fields.Add(Field("ICON", Z($"{label}/{sex}/head{part}.dds")));
            }
        }
        fields.Add(Field("NAM1", []));
        foreach (var sex in new[] { "MNAM", "FNAM" })
        {
            fields.Add(Field(sex, []));
            foreach (var part in new uint[] { 0, 1, 2, 3 })
            {
                fields.Add(Field("INDX", U32(part)));
                fields.Add(Field("MODL", Z($"{label}/{sex}/body{part}." + (part == 3 ? "egt" : "nif"))));
            }
        }
        fields.Add(Field("HNAM", U32(0x300))); fields.Add(Field("ENAM", U32(0x310)));
        foreach (var sex in new[] { "MNAM", "FNAM" })
        {
            fields.Add(Field(sex, [])); fields.Add(Field("FGGS", new byte[200]));
            fields.Add(Field("FGGA", new byte[120])); fields.Add(Field("FGTS", new byte[200]));
        }
        return Record("RACE", id, fields.ToArray());
    }

    private static byte[] Armor(uint id, string path, uint mask, uint addonList) => Record("ARMO", id,
        Field("BMDT", Combine(U32(mask), U32(0))), Field("MODL", Z(path)), Field("MOD3", Z("female/" + path)),
        Field("BIPL", U32(addonList)), Field("MODS", Combine(U32(1), U32(9), Encoding.UTF8.GetBytes("BodyShape"), U32(0x500), U32(2))));
    private static byte[] Head(uint id, uint extra) => Record("HDPT", id, Field("MODL", Z($"head-addon-{id}.nif")),
        extra == 0 ? [] : Field("HNAM", U32(extra)));
    private static byte[] Leveled(uint id, uint item) => Record("LVLI", id, Field("LVLO", Combine(U32(1), U32(item), U32(1))));
    private static byte[] Header(string? master)
    {
        var header = Record("TES4", 0, Field("HEDR", Combine(F32(1.34f), U32(40), U32(0x1000))),
            master is null ? [] : Combine(Field("MAST", Z(master)), Field("DATA", new byte[8])));
        if (master is null) BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 1);
        return header;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var payload = Combine(fields);
        return Combine(Encoding.ASCII.GetBytes(signature), U32((uint)payload.Length), U32(0), U32(id), new byte[8], payload);
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var length = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(length, checked((ushort)data.Length));
        return Combine(Encoding.ASCII.GetBytes(signature), length, data);
    }
    private static byte[] U32(uint value) { var data = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); return data; }
    private static byte[] F32(float value) { var data = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(data, value); return data; }
    private static byte[] Z(string text) => Encoding.UTF8.GetBytes(text + '\0');
    private static byte[] Combine(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static FalloutFormKey Key(uint id) => new("base.esm", id);
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Accepted invalid " + name);
    }
}
