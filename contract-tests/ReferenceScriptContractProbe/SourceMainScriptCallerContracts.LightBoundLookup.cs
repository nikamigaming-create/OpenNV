using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLightBoundNameLookup()
    {
        var nif = FalloutNifFile.Read(Fixture());
        Require(nif.ReadExtraDataName(1) == "Unrelated", "Base extra-data name lookup decoded or replaced a derived payload.");
        Reject(() => nif.ReadObject(1));
        var bound = FalloutDetectionBounds.Read(nif, 0);
        Require(bound.BoundBlock == 2 && bound.Minimum.SequenceEqual(new float[] { -3, -3, -3 }) &&
            bound.Maximum.SequenceEqual(new float[] { 5, 7, 9 }),
            "BBX lookup rejected an unrelated derived extra or lost the selected real bound.");
        Reject(() => FalloutDetectionBounds.Read(FalloutNifFile.Read(Fixture(unknownName: 2)), 0));
        Reject(() => FalloutNifFile.Read(Fixture(unknownName: 99)).ReadExtraDataName(1));
        Console.WriteLine("OPENNV_LIGHT_BBX_NAME_LOOKUP_PASS baseNameOnly=true unrelatedPayloadStillUnsupported=true selectedTypedBound=true malformedNameRefused=true");

        static byte[] Fixture(int unknownName = 1)
        {
            (string Type, byte[] Data)[] blocks =
            [
                ("NiNode", Bytes(w =>
                {
                    w.Write(0); w.Write(2); w.Write(1); w.Write(2); w.Write(-1); w.Write(0U);
                    foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(value);
                    w.Write(1f); w.Write(0); w.Write(-1); w.Write(0); w.Write(0);
                })),
                ("UnownedDerivedExtra", Bytes(w => { w.Write(unknownName); w.Write(0xdeadbeefU); })),
                ("BSBound", Bytes(w => { w.Write(2); foreach (var value in new float[] { 1, 2, 3, 4, 5, 6 }) w.Write(value); }))
            ];
            return Bytes(w =>
            {
                w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
                w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
                w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
                w.Write((ushort)blocks.Length); foreach (var block in blocks) Text(w, block.Type);
                for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
                foreach (var block in blocks) w.Write(block.Data.Length);
                string[] names = ["Root", "Unrelated", "BBX"];
                w.Write(names.Length); w.Write(9U); foreach (var name in names) Text(w, name);
                w.Write(0U); foreach (var block in blocks) w.Write(block.Data); w.Write(1U); w.Write(0);
            });
        }
        static byte[] Bytes(Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
        static void Text(BinaryWriter writer, string value) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
    }
}
