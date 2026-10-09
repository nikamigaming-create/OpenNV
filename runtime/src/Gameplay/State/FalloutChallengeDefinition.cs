using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutChallengeDefinition(FalloutPluginRecord Record, uint Type, int Threshold,
    uint Flags, uint Interval, ushort Value1, ushort Value2, ushort Value3, ushort Reserved,
    FalloutFormKey? PrimaryFilter, FalloutFormKey? SecondaryFilter, FalloutFormKey? Script,
    string Name, string Description, string? Icon)
{
    internal bool StartDisabled => (Flags & 1) != 0;
    internal bool Recurring => (Flags & 2) != 0;
    internal string Sha256 => Convert.ToHexString(SHA256.HashData(Record.ReadData())).ToLowerInvariant();
    internal static FalloutChallengeDefinition Read(FalloutPluginRecord record)
    {
        if (record.Signature != "CHAL") throw new InvalidDataException("Challenge target is not CHAL.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 24) throw new InvalidDataException("Challenge DATA must contain 24 bytes.");
        string Text(string name)
        {
            var field = fields.SingleOrDefault(field => field.Signature == name).Data;
            return field.IsEmpty ? "" : FalloutDialogueTopic.Text(field.Span);
        }
        FalloutFormKey? Link(string name)
        {
            var field = fields.SingleOrDefault(field => field.Signature == name).Data;
            if (field.Length is not (0 or 4)) throw new InvalidDataException("Challenge form link has an invalid extent.");
            return field.IsEmpty ? null : record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Span));
        }
        // Preserve signed consumer views and every physical word. Unknown
        // event families and flag writes are not inferred from field presence.
        return new(record, BinaryPrimitives.ReadUInt32LittleEndian(data),
            BinaryPrimitives.ReadInt32LittleEndian(data[4..]), BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]), BinaryPrimitives.ReadUInt16LittleEndian(data[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[18..]), BinaryPrimitives.ReadUInt16LittleEndian(data[20..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[22..]), Link("SNAM"), Link("XNAM"), Link("SCRI"),
            Text("FULL"), Text("DESC"), fields.Any(field => field.Signature == "ICON") ? Text("ICON") : null);
    }
}
