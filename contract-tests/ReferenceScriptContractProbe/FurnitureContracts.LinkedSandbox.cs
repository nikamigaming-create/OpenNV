using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static void LinkedSandboxContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-linked-sandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var package = new byte[12]; package[4] = 12;
            var location = Join(BitConverter.GetBytes(6), new byte[4], BitConverter.GetBytes(256));
            byte[] Reference(string type, uint id, uint basis, uint? linked = null) => Record(type, id,
                Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new float[] { 5, 6, 7, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()),
                linked is { } target ? Field("XLKR", BitConverter.GetBytes(target)) : []);
            var body = Join(Reference("ACHR", 0x900, 0x700, 0x905), Reference("ACHR", 0x90a, 0x700), Reference("REFR", 0x905, 0x3b));
            var group = new byte[24 + body.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
            body.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory, "LinkedSandbox.esm"), Join(Header(),
                Record("NPC_", 0x700, Field("ACBS", new byte[24])), Record("STAT", 0x3b),
                Record("CELL", 0x800, Field("DATA", [1])), group,
                Record("PACK", 0x414, Field("EDID", Text("LinkedSandbox")), Field("PKDT", package), Field("PLDT", location))));
            using var records = FalloutPluginStack.Load(directory, ["LinkedSandbox.esm"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Key(uint id) => new("LinkedSandbox.esm", id);
            var source = FalloutSandboxPackage.Read(records.GetEffective(Key(0x414)));
            var first = source.Location(records, world, Key(0x900));
            Require(source.LocationType == 6 && source.Radius == 256 && source.Reference is null && first.Location.SequenceEqual(new float[] { 5, 6, 7 }),
                "Linked Sandbox lost its declaration/radius or used a placeholder center.");
            world.SetPlacement(Key(0x905), world.Placement(Key(0x905)) with { Position = [8, 9, 10] });
            Require(source.Location(records, world, Key(0x900)).Location.SequenceEqual(new float[] { 8, 9, 10 }),
                "Linked Sandbox ignored the linked reference's actual placement owner.");
            Reject(() => source.Location(records, world, Key(0x90a)));
            Console.WriteLine("OPENNV_LINKED_SANDBOX_CONTRACT_PASS sourceLink=true radius=true actualPlacement=true missingLinkRefused=true");
        }
        finally { Directory.Delete(directory, true); }
    }
}
