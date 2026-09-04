using System.Buffers.Binary;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal enum ParityDeltaKind
{
    MissingRetail,
    MissingOpenNv,
    TypeMismatch,
    ByteMismatch,
}

internal sealed record ParityFieldDelta(
    ParityCategory Category,
    ulong StableId,
    ParityDeltaKind Kind,
    int? FirstByteOffset,
    double? NumericDelta,
    ParityValueKind? RetailKind,
    string? RetailHex,
    ParityValueKind? OpenNvKind,
    string? OpenNvHex);

internal sealed record ParityComparison(
    bool ComparableState,
    bool ExactStateMatch,
    string? AlignmentFailure,
    int? FirstStateByteOffset,
    long SimulationTickDelta,
    long MonotonicNanosecondsDelta,
    long EventOrdinalDelta,
    IReadOnlyList<ParityFieldDelta> Deltas)
{
    internal bool Diverged => !ComparableState || !ExactStateMatch;
}

internal static class ParityFrameComparator
{
    internal static ParityComparison Compare(
        ParityTelemetryFrame retail,
        ParityTelemetryFrame openNv)
    {
        if (retail.Engine != ParityEngine.Retail || openNv.Engine != ParityEngine.OpenNv)
            throw new ArgumentException("Parity comparison requires retail on the left and OpenNV on the right.");
        var alignmentFailure = !retail.StateKey.Equals(openNv.StateKey, StringComparison.Ordinal)
            ? "State keys differ."
            : retail.EventOrdinal == 0 || openNv.EventOrdinal == 0
                ? "Authoritative event identity is unavailable."
                : retail.EventOrdinal != openNv.EventOrdinal
                    ? "Authoritative event ordinals differ."
                    : null;
        var comparable = alignmentFailure is null;
        var retailState = ParityTelemetryCodec.EncodeCanonicalState(retail.StateKey, retail.Fields);
        var openNvState = ParityTelemetryCodec.EncodeCanonicalState(openNv.StateKey, openNv.Fields);
        var exact = comparable && retailState.AsSpan().SequenceEqual(openNvState);
        var retailFields = retail.Fields.ToDictionary(field => (field.Category, field.StableId));
        var openNvFields = openNv.Fields.ToDictionary(field => (field.Category, field.StableId));
        var identities = retailFields.Keys.Concat(openNvFields.Keys).Distinct().Order().ToArray();
        var deltas = new List<ParityFieldDelta>();
        foreach (var identity in identities)
        {
            if (!retailFields.TryGetValue(identity, out var retailField))
            {
                deltas.Add(Delta(identity, ParityDeltaKind.MissingRetail, null, null,
                    null, openNvFields[identity]));
                continue;
            }
            if (!openNvFields.TryGetValue(identity, out var openNvField))
            {
                deltas.Add(Delta(identity, ParityDeltaKind.MissingOpenNv, null, null,
                    retailField, null));
                continue;
            }
            if (retailField.Kind != openNvField.Kind)
            {
                deltas.Add(Delta(identity, ParityDeltaKind.TypeMismatch, null, null,
                    retailField, openNvField));
                continue;
            }
            var byteOffset = FirstMismatch(retailField.Value, openNvField.Value);
            if (byteOffset is null)
                continue;
            deltas.Add(Delta(
                identity,
                ParityDeltaKind.ByteMismatch,
                byteOffset,
                NumericDelta(retailField, openNvField),
                retailField,
                openNvField));
        }
        return new ParityComparison(
            comparable,
            exact,
            alignmentFailure,
            FirstMismatch(retailState, openNvState),
            openNv.SimulationTick - retail.SimulationTick,
            openNv.MonotonicNanoseconds - retail.MonotonicNanoseconds,
            unchecked((long)(openNv.EventOrdinal - retail.EventOrdinal)),
            deltas);
    }

    private static ParityFieldDelta Delta(
        (ParityCategory Category, ulong StableId) identity,
        ParityDeltaKind kind,
        int? offset,
        double? numericDelta,
        ParityTelemetryField? retail,
        ParityTelemetryField? openNv) => new(
            identity.Category, identity.StableId, kind, offset,
            numericDelta is { } number && double.IsFinite(number) ? number : null,
            retail?.Kind, retail is null ? null : Convert.ToHexString(retail.Value),
            openNv?.Kind, openNv is null ? null : Convert.ToHexString(openNv.Value));

    internal static int? FirstMismatch(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var common = Math.Min(left.Length, right.Length);
        for (var index = 0; index < common; index++)
        {
            if (left[index] != right[index])
                return index;
        }
        return left.Length == right.Length ? null : common;
    }

    private static double? NumericDelta(
        ParityTelemetryField retail,
        ParityTelemetryField openNv) =>
        retail.Kind switch
        {
            ParityValueKind.Float32 =>
                BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(openNv.Value)) -
                (double)BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(retail.Value)),
            ParityValueKind.Int64 =>
                BinaryPrimitives.ReadInt64LittleEndian(openNv.Value) -
                (double)BinaryPrimitives.ReadInt64LittleEndian(retail.Value),
            ParityValueKind.UInt64 =>
                BinaryPrimitives.ReadUInt64LittleEndian(openNv.Value) -
                (double)BinaryPrimitives.ReadUInt64LittleEndian(retail.Value),
            ParityValueKind.Float64 =>
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(openNv.Value)) -
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(retail.Value)),
            _ => null,
        };
}
