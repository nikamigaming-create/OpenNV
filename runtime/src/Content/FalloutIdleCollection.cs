using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdleCollection(FalloutFormKey Form, byte IdleFlags, float IdleTimer, IReadOnlyList<FalloutFormKey> Idles)
{
    internal bool RunInSequence => (IdleFlags & 1) != 0;
    internal bool DoOnce => (IdleFlags & 4) != 0;
    internal static FalloutIdleCollection Read(FalloutPluginRecord record)
    {
        if (record.Signature != "IDLM") throw new InvalidDataException("Patrol idle source is not IDLM.");
        var rows = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Field(string name, int size)
        {
            var found = rows.Where(row => row.Signature == name).ToArray();
            return found.Length == 1 && found[0].Data.Length == size ? found[0].Data : throw new InvalidDataException($"IDLM has invalid {name}.");
        }
        var flags = Field("IDLF", 1).Span[0]; var count = Field("IDLC", 1).Span[0];
        var timer = BinaryPrimitives.ReadSingleLittleEndian(Field("IDLT", 4).Span);
        if ((flags & ~5) != 0) throw new NotSupportedException("Idle marker flags are unbound.");
        if (!float.IsFinite(timer) || timer < 0) throw new InvalidDataException("Idle marker timer is invalid.");
        var list = count == 0 && !rows.Any(row => row.Signature == "IDLA") ? ReadOnlyMemory<byte>.Empty : Field("IDLA", count * 4);
        return new(record.FormKey, flags, timer, Enumerable.Range(0, count).Select(index =>
            record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(list.Span[(index * 4)..]))).ToArray());
    }
}
