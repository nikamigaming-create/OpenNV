using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void CompressedEmptyControl(string directory)
    {
        using var encoded = new MemoryStream();
        using (var compressor = new ZLibStream(encoded, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(Array.Empty<byte>());
        var framed = encoded.ToArray();
        var nonFinal = framed.ToArray();
        nonFinal[2] &= 0xfe;
        var invalidWindow = framed.ToArray();
        invalidWindow[0] = 0x88;
        invalidWindow[1] = (byte)((31 - (invalidWindow[0] << 8) % 31) % 31);
        var sourceRoot = Path.Combine(directory, "compressed-empty-inputs"); Directory.CreateDirectory(sourceRoot);
        var path = Path.Combine(sourceRoot, "Source.esm");
        File.WriteAllBytes(path, Join(Header(),
            Record("FUTR", 0x400, FalloutPluginRecord.CompressedFlag, Join(new byte[4], framed)),
            Record("FUTR", 0x401, FalloutPluginRecord.CompressedFlag, Join(new byte[4], framed[..2])),
            Record("FUTR", 0x402, FalloutPluginRecord.CompressedFlag, Join(new byte[4], framed[..2], framed[^4..])),
            Record("FUTR", 0x403, FalloutPluginRecord.CompressedFlag, Join(new byte[4], invalidWindow)),
            Record("FUTR", 0x404, FalloutPluginRecord.CompressedFlag, Join(new byte[4], nonFinal))));
        var original = SHA256.HashData(File.ReadAllBytes(path));
        using (var records = FalloutPluginStack.Load(sourceRoot, ["Source.esm"]))
        {
            Require(records.GetEffective(new("Source.esm", 0x400)).ReadData().Length == 0,
                "A genuine framed compressed empty record was refused.");
            foreach (var id in new uint[] { 0x401, 0x402, 0x403, 0x404 })
                Reject(() => records.GetEffective(new("Source.esm", id)).ReadData(), "invalid");
            var output = Path.Combine(directory, "compressed-empty-report");
            Require(QuestGraphAudit.Run(records, "authored-compressed-empty-control", output) == 1,
                "Malformed compressed envelopes became an empty successful denominator.");
            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "summary.json")));
            Require(report.RootElement.GetProperty("selection").GetProperty("unreadWinningRecords").GetInt32() == 4 &&
                !report.RootElement.GetProperty("selection").GetProperty("originalProgramDenominatorKnown").GetBoolean(),
                "Actual failed compression lost unknown original program coverage.");
        }
        Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compression admission changed source bytes.");
        Console.WriteLine("OPENNV_COMPRESSED_EMPTY_RECORD_CONTRACT_PASS framedEmpty=true absentTruncatedEnvelope=refused invalidWindow=refused nonFinalBlock=refused sourceUnchanged=true");
    }
}
