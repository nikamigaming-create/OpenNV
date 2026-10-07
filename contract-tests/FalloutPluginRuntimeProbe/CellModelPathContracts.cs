using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class CellModelPathContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-model-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            (string Signature, string Model, string Expected)[] cases =
            [
                ("TREE", "Shrub.SPT", "trees\\shrub.spt"),
                ("TREE", "Trees/Shrub.SPT", "trees\\shrub.spt"),
                ("TREE", "Data/Trees/Shrub.SPT", "trees\\shrub.spt"),
                ("TREE", "\\Trees\\Shrub.SPT", "trees\\shrub.spt"),
                ("TREE", "Plants/Tree.NIF", "meshes\\plants\\tree.nif"),
                ("STAT", "Data/Meshes/Rock.NIF", "meshes\\rock.nif"),
                ("STAT", "\\Architecture\\Rock.NIF", "meshes\\architecture\\rock.nif"),
                ("TREE", "Meshes/Custom.SPT", "meshes\\custom.spt")
            ];
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var records = new List<byte[]> { Record("TES4", 0, 0, Field("HEDR", header)), Record("CELL", 0x100, 0, Field("DATA", [1])) };
            var references = new List<byte[]>();
            for (var index = 0; index < cases.Length; index++)
            {
                var (signature, model, _) = cases[index];
                var form = checked((uint)(0x200 + index));
                records.Add(Record(signature, form, 0, Field("MODL", Encoding.UTF8.GetBytes(model + "\0"))));
                references.Add(Record("REFR", checked((uint)(0x300 + index)), index == 0 ? 0x8000u : 0u,
                    Field("NAME", BitConverter.GetBytes(form)), Field("DATA", new byte[24])));
            }
            var children = references.SelectMany(value => value).ToArray();
            var group = new byte[24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), checked((uint)(group.Length + children.Length)));
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x100);
            BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
            File.WriteAllBytes(Path.Combine(directory, "Models.esm"), records.SelectMany(value => value).Concat(group).Concat(children).ToArray());
            using var stack = FalloutPluginStack.Load(directory, ["Models.esm"]);
            var cell = FalloutCellSceneReader.Read(stack, new("Models.esm", 0x100));
            for (var index = 0; index < cases.Length; index++)
                if (cell.BaseObjects[new("Models.esm", checked((uint)(0x200 + index)))].ModelPath != cases[index].Expected)
                    throw new InvalidDataException("Winning model normalization lost its authored resource root or format.");
            if (cell.References.Count != cases.Length || cell.References.Single(reference => reference.FormKey.ObjectId == 0x300).Flags != 0x8000)
                throw new InvalidDataException("Model resource dispatch changed the source reference denominator or distant flag.");
            Console.WriteLine("OPENNV_CELL_MODEL_ROOT_CONTRACT_PASS sourceRoots=true speedTree=true nif=true placementsRetained=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Field(string signature, byte[] payload)
    {
        var header = new byte[6]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)payload.Length));
        return header.Concat(payload).ToArray();
    }
    private static byte[] Record(string signature, uint form, uint flags, params byte[][] fields)
    {
        var payload = fields.SelectMany(value => value).ToArray();
        var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), form);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 15);
        return header.Concat(payload).ToArray();
    }
}
