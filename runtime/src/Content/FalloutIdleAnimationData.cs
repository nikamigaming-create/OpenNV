using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>The independent loop selection and replay-delay fields of an IDLE.</summary>
internal sealed record FalloutIdleAnimationData(byte Group, byte LoopMinimum, byte LoopMaximum,
    ushort ReplayDelaySeconds, byte Flags)
{
    internal static FalloutIdleAnimationData Read(FalloutPluginRecord record)
    {
        if (record.Signature != "IDLE") throw new InvalidDataException("Idle timing requires an IDLE record.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (fields.Length != 1) throw new InvalidDataException($"IDLE {record.FormKey} requires one timing DATA field.");
        return Read(fields[0].Data.Span);
    }

    internal static FalloutIdleAnimationData Read(ReadOnlySpan<byte> data)
    {
        if (data.Length is not (6 or 8)) throw new InvalidDataException("IDLE DATA requires its six- or eight-byte layout.");
        return new(data[0], data[1], data[2], BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
            data.Length == 8 ? data[6] : (byte)0);
    }

    internal byte SelectAdditionalLoops(Func<uint, uint> nextBounded)
    {
        if (LoopMinimum == 0 || LoopMaximum == 0) return 0;
        if (LoopMinimum == byte.MaxValue) return byte.MaxValue;
        if (LoopMaximum <= LoopMinimum) return (byte)(LoopMaximum - 1);
        var width = (uint)(LoopMaximum - LoopMinimum);
        var choice = nextBounded(width);
        if (choice >= width) throw new InvalidDataException("Idle random selection exceeded its exclusive bound.");
        return (byte)(LoopMinimum + choice - 1);
    }
}

/// <summary>Actor-wide cooldown membership survives interruption of a selected animation.</summary>
internal sealed class FalloutIdleReplayState
{
    private readonly Dictionary<FalloutFormKey, float> _remaining = [];
    internal IReadOnlyDictionary<FalloutFormKey, float> Remaining => _remaining;
    internal bool CanSelect(FalloutFormKey idle) => !_remaining.ContainsKey(idle);

    internal void Started(FalloutFormKey idle, ushort delaySeconds)
    {
        if (delaySeconds == 0) return;
        _remaining[idle] = MathF.Max(_remaining.GetValueOrDefault(idle), delaySeconds);
    }

    internal void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var idle in _remaining.Keys.ToArray())
        {
            var next = _remaining[idle] - seconds;
            if (next <= 0) _remaining.Remove(idle);
            else _remaining[idle] = next;
        }
    }
}
