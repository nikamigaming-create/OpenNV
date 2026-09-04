using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal enum ParityEngine : byte
{
    Retail = 1,
    OpenNv = 2,
}

internal enum ParityCategory : ushort
{
    Execution = 1,
    World = 2,
    Camera = 3,
    Actor = 4,
    Animation = 5,
    Quest = 6,
    Dialogue = 7,
    Effect = 8,
    Audio = 9,
    Ui = 10,
    Material = 11,
    Renderer = 12,
    Input = 13,
}

internal enum ParityValueKind : byte
{
    Bytes = 1,
    Int64 = 2,
    UInt64 = 3,
    Float64 = 4,
    Utf8 = 5,
}

internal sealed record ParityTelemetryField(
    ParityCategory Category,
    ulong StableId,
    ParityValueKind Kind,
    byte[] Value)
{
    internal static ParityTelemetryField Bytes(
        ParityCategory category,
        ulong stableId,
        ReadOnlySpan<byte> value) =>
        new(category, stableId, ParityValueKind.Bytes, value.ToArray());

    internal static ParityTelemetryField Int64(
        ParityCategory category,
        ulong stableId,
        long value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return new(category, stableId, ParityValueKind.Int64, bytes);
    }

    internal static ParityTelemetryField UInt64(
        ParityCategory category,
        ulong stableId,
        ulong value)
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return new(category, stableId, ParityValueKind.UInt64, bytes);
    }

    internal static ParityTelemetryField Float64(
        ParityCategory category,
        ulong stableId,
        double value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        return new(category, stableId, ParityValueKind.Float64, bytes);
    }

    internal static ParityTelemetryField Utf8(
        ParityCategory category,
        ulong stableId,
        string value) =>
        new(category, stableId, ParityValueKind.Utf8, Encoding.UTF8.GetBytes(value));
}

internal sealed record ParityTelemetryFrame(
    ParityEngine Engine,
    ulong Sequence,
    long SimulationTick,
    long MonotonicNanoseconds,
    ulong EventOrdinal,
    string StateKey,
    IReadOnlyList<ParityTelemetryField> Fields);

internal static class ParityStableId
{
    internal static ulong FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parity field name is required.", nameof(name));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}

internal static class ParityTelemetryCodec
{
    private static readonly byte[] Magic = "ONVPTL01"u8.ToArray();
    private const ushort Version = 1;
    private const int HashBytes = 32;
    private const int MaximumStateKeyBytes = 4096;
    private const int MaximumFieldBytes = 16 * 1024 * 1024;
    private const int MaximumFields = 65535;

    internal static byte[] Encode(ParityTelemetryFrame frame)
    {
        var state = EncodeCanonicalState(frame.StateKey, frame.Fields);
        var hash = SHA256.HashData(state);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)frame.Engine);
        writer.Write((byte)0);
        writer.Write(frame.Sequence);
        writer.Write(frame.SimulationTick);
        writer.Write(frame.MonotonicNanoseconds);
        writer.Write(frame.EventOrdinal);
        writer.Write(state.Length);
        writer.Write(hash);
        writer.Write(state);
        writer.Flush();
        return stream.ToArray();
    }

    internal static ParityTelemetryFrame Decode(ReadOnlySpan<byte> packet)
    {
        using var stream = new MemoryStream(packet.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) ||
            reader.ReadUInt16() != Version)
            throw new InvalidDataException("Parity telemetry header is unsupported.");
        var engine = (ParityEngine)reader.ReadByte();
        if (!Enum.IsDefined(engine) || reader.ReadByte() != 0)
            throw new InvalidDataException("Parity telemetry engine or reserved byte is invalid.");
        var sequence = reader.ReadUInt64();
        var simulationTick = reader.ReadInt64();
        var monotonicNanoseconds = reader.ReadInt64();
        var eventOrdinal = reader.ReadUInt64();
        var stateLength = reader.ReadInt32();
        if (stateLength < 0 || stateLength > MaximumFieldBytes * 4)
            throw new InvalidDataException("Parity telemetry state length is invalid.");
        var expectedHash = reader.ReadBytes(HashBytes);
        var state = reader.ReadBytes(stateLength);
        if (state.Length != stateLength || stream.Position != stream.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(state)))
            throw new InvalidDataException("Parity telemetry packet is truncated or hash-invalid.");
        var (stateKey, fields) = DecodeCanonicalState(state);
        return new ParityTelemetryFrame(
            engine,
            sequence,
            simulationTick,
            monotonicNanoseconds,
            eventOrdinal,
            stateKey,
            fields);
    }

    internal static byte[] EncodeCanonicalState(
        string stateKey,
        IReadOnlyList<ParityTelemetryField> fields)
    {
        var key = Encoding.UTF8.GetBytes(stateKey);
        if (key.Length == 0 || key.Length > MaximumStateKeyBytes || fields.Count > MaximumFields)
            throw new InvalidDataException("Parity telemetry state identity is invalid.");
        var ordered = fields
            .OrderBy(field => field.Category)
            .ThenBy(field => field.StableId)
            .ToArray();
        if (ordered.Select(field => (field.Category, field.StableId)).Distinct().Count() !=
            ordered.Length)
            throw new InvalidDataException("Parity telemetry field identities are not unique.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)key.Length);
        writer.Write(key);
        writer.Write((ushort)ordered.Length);
        foreach (var field in ordered)
        {
            if (!Enum.IsDefined(field.Category) || !Enum.IsDefined(field.Kind) ||
                field.Value.Length > MaximumFieldBytes ||
                field.Kind is ParityValueKind.Int64 or ParityValueKind.UInt64 or ParityValueKind.Float64 &&
                field.Value.Length != sizeof(long))
                throw new InvalidDataException("Parity telemetry field is invalid.");
            writer.Write((ushort)field.Category);
            writer.Write((byte)field.Kind);
            writer.Write((byte)0);
            writer.Write(field.StableId);
            writer.Write(field.Value.Length);
            writer.Write(field.Value);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static (string StateKey, IReadOnlyList<ParityTelemetryField> Fields)
        DecodeCanonicalState(ReadOnlySpan<byte> state)
    {
        using var stream = new MemoryStream(state.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var keyLength = reader.ReadUInt16();
        if (keyLength == 0 || keyLength > MaximumStateKeyBytes)
            throw new InvalidDataException("Parity telemetry state key is invalid.");
        var keyBytes = reader.ReadBytes(keyLength);
        if (keyBytes.Length != keyLength)
            throw new InvalidDataException("Parity telemetry state key is truncated.");
        var stateKey = new UTF8Encoding(false, true).GetString(keyBytes);
        var count = reader.ReadUInt16();
        var fields = new List<ParityTelemetryField>(count);
        (ParityCategory Category, ulong StableId)? prior = null;
        for (var index = 0; index < count; index++)
        {
            var category = (ParityCategory)reader.ReadUInt16();
            var kind = (ParityValueKind)reader.ReadByte();
            if (!Enum.IsDefined(category) || !Enum.IsDefined(kind) || reader.ReadByte() != 0)
                throw new InvalidDataException("Parity telemetry field header is invalid.");
            var stableId = reader.ReadUInt64();
            var length = reader.ReadInt32();
            if (length < 0 || length > MaximumFieldBytes ||
                kind is ParityValueKind.Int64 or ParityValueKind.UInt64 or ParityValueKind.Float64 &&
                length != sizeof(long))
                throw new InvalidDataException("Parity telemetry field length is invalid.");
            var value = reader.ReadBytes(length);
            if (value.Length != length)
                throw new InvalidDataException("Parity telemetry field is truncated.");
            var identity = (category, stableId);
            if (prior is not null && Compare(prior.Value, identity) >= 0)
                throw new InvalidDataException("Parity telemetry fields are not canonical.");
            prior = identity;
            fields.Add(new ParityTelemetryField(category, stableId, kind, value));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Parity telemetry state contains trailing bytes.");
        return (stateKey, fields);
    }

    private static int Compare(
        (ParityCategory Category, ulong StableId) left,
        (ParityCategory Category, ulong StableId) right)
    {
        var category = left.Category.CompareTo(right.Category);
        return category != 0 ? category : left.StableId.CompareTo(right.StableId);
    }
}
