using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class ActorSourceContracts
{
    private static void ActorDisplayNameContracts(string directory)
    {
        byte[] Actor(uint id, ushort groups, uint template, params byte[][] extra)
        {
            var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), groups);
            return Record("CREA", id, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(template)),
                Field("EDID", Text("EditorOnly" + id)), Join(extra));
        }
        var path = Path.Combine(directory, "Names.esm");
        File.WriteAllBytes(path, Join(Header(),
            Actor(0x100, 0, 0, Field("FULL", Text("Inherited name"))),
            Actor(0x101, 128, 0x100, Field("FULL", Text("Ignored local name"))),
            Actor(0x102, 64, 0x100, Field("FULL", Text("Local name"))),
            Actor(0x103, 0, 0), Actor(0x104, 128, 0x103, Field("FULL", Text("Must not fall back"))),
            Actor(0x105, 128, 0x101), Actor(0x106, 0, 0, Field("FULL", Text("Other name"))),
            Actor(0x107, 128, 0x110),
            Record("LVLC", 0x110, Field("LVLD", [0]), Field("LVLF", [1]),
                Field("LVLO", Entry(0x100)), Field("LVLO", Entry(0x106))),
            Actor(0x108, 0, 0, Field("FULL", Text("One")), Field("FULL", Text("Two"))),
            Cell(0x800, Record("ACRE", 0x900, Field("NAME", BitConverter.GetBytes(0x107u)), Field("DATA", new byte[24])))));
        using var records = FalloutPluginStack.Load(directory, ["Names.esm"]);
        FalloutFormKey Key(uint id) => new("Names.esm", id);
        string Name(uint id, FalloutActorTemplateSelection? selected = null) =>
            FalloutRecordDisplayName.ReadActor(records, records.GetEffective(Key(id)), selected);
        Check(Name(0x101) == "Inherited name" && Name(0x105) == "Inherited name" && Name(0x102) == "Local name",
            "Actor FULL inheritance used traits/model groups or ignored the BaseData template chain.");
        Check(Name(0x103) == "" && Name(0x104) == "", "Missing inherited FULL fell back to local FULL or editor identity.");
        Reject(() => Name(0x108));
        using var world = new FalloutReferenceWorld(records);
        var selection = world.InitializeActorTemplates(Key(0x900), 1);
        var before = JsonSerializer.Serialize(selection.Capture());
        var name = world.ActorDisplayName(Key(0x900));
        Check(name is "Inherited name" or "Other name", "Retained actor name did not use its selected source actor.");
        Check(world.ActorDisplayName(Key(0x900)) == name && before == JsonSerializer.Serialize(selection.Capture()),
            "Reading an actor name rerolled or advanced its actual encounter selection.");
        var cold = new FalloutActorTemplateSelection(JsonSerializer.Deserialize<FalloutActorTemplateSnapshot>(before)!);
        Check(Name(0x107, cold) == name && before == JsonSerializer.Serialize(cold.Capture()),
            "Cold name resolution changed the actual selected template or consumed random state.");
        static byte[] Entry(uint form)
        {
            var data = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), form);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1); return data;
        }
    }
}
