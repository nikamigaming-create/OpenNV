using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

[Flags]
internal enum FalloutSoundFlags : ushort
{
    RandomFrequencyShift = 0x0001,
    PlayAtRandom = 0x0002,
    EnvironmentIgnored = 0x0004,
    RandomLocation = 0x0008,
    Loop = 0x0010,
    MenuSound = 0x0020,
    TwoDimensional = 0x0040,
    Lfe360 = 0x0080,
    DialogueSound = 0x0100,
    EnvelopeFast = 0x0200,
    EnvelopeSlow = 0x0400,
    Radius2D = 0x0800,
    MuteWhenSubmerged = 0x1000,
    StartAtRandomPosition = 0x2000,
}

internal sealed record FalloutSoundRecord(
    FalloutFormKey FormKey,
    string EditorId,
    string LogicalPath,
    byte RandomChancePercent,
    byte MinimumAttenuation,
    byte MaximumAttenuation,
    sbyte FrequencyAdjustment,
    FalloutSoundFlags Flags,
    short StaticAttenuationHundredthsDb,
    byte StopTime,
    byte StartTime,
    IReadOnlyList<short> AttenuationCurve,
    short ReverbAttenuation,
    uint Priority,
    uint LoopStartSample,
    uint LoopEndSample)
{
    private const float HundredthsPerDb = 100.0f;
    private const float MinimumAttenuationUnits = 5.0f;
    private const float MaximumAttenuationUnits = 100.0f;
    private const int AttenuationCurvePointCount = 5;
    private const float AttenuationCurveMaximum = 100.0f;
    private const float AmplitudeDbMultiplier = 20.0f;
    private const float FrequencyPercentDenominator = 100.0f;

    internal bool HasExactFile => Path.HasExtension(LogicalPath);
    internal bool IsLooping => (Flags & FalloutSoundFlags.Loop) != 0;
    internal bool IsTwoDimensional =>
        (Flags & (FalloutSoundFlags.TwoDimensional | FalloutSoundFlags.MenuSound)) != 0;
    internal float StaticAttenuationDb => StaticAttenuationHundredthsDb / HundredthsPerDb;
    internal float MinimumDistanceGameUnits => MinimumAttenuation * MinimumAttenuationUnits;
    internal float MaximumDistanceGameUnits => MaximumAttenuation * MaximumAttenuationUnits;
    internal float FixedPitchScale => 1.0f + FrequencyAdjustment / FrequencyPercentDenominator;

    internal float AttenuationDbAtDistanceGameUnits(float distance)
    {
        if (!float.IsFinite(distance) || distance < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(distance),
                "SOUN listener distance must be finite and nonnegative.");
        ValidateAttenuationCurve();
        var minimum = MinimumDistanceGameUnits;
        var maximum = MaximumDistanceGameUnits;
        if (maximum <= minimum)
            throw new NotSupportedException(
                $"SOUN {FormKey} attenuation range is empty or reversed: {minimum}/{maximum}.");
        float gainPercent;
        if (distance <= minimum)
            gainPercent = AttenuationCurve[0];
        else if (distance >= maximum)
            gainPercent = AttenuationCurve[^1];
        else
        {
            var curvePosition = (distance - minimum) / (maximum - minimum) *
                (AttenuationCurvePointCount - 1);
            var lower = Math.Min((int)MathF.Floor(curvePosition), AttenuationCurvePointCount - 2);
            var fraction = curvePosition - lower;
            gainPercent = AttenuationCurve[lower] +
                (AttenuationCurve[lower + 1] - AttenuationCurve[lower]) * fraction;
        }
        return gainPercent == 0.0f
            ? float.NegativeInfinity
            : AmplitudeDbMultiplier * MathF.Log10(gainPercent / AttenuationCurveMaximum);
    }

    internal void ValidateAttenuationCurve()
    {
        if (AttenuationCurve.Count != AttenuationCurvePointCount ||
            AttenuationCurve.Any(value => value is < 0 or > (short)AttenuationCurveMaximum))
            throw new NotSupportedException(
                $"SOUN {FormKey} attenuation curve must contain five gain percentages from 0 through 100.");
    }
}

internal static class FalloutSoundPlaybackContract
{
    private const uint PercentRollUpperBound = 100U;
    private const short FullEnvironmentReverbSend = 0;
    private const short FullyAttenuatedReverbSend = 100;
    internal const FalloutSoundFlags SupportedThreeDimensionalFlags =
        FalloutSoundFlags.Loop |
        FalloutSoundFlags.EnvironmentIgnored |
        FalloutSoundFlags.DialogueSound |
        FalloutSoundFlags.MuteWhenSubmerged;

    internal static void ValidateThreeDimensional(FalloutSoundRecord descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.HasExactFile)
            throw Unsupported(descriptor, "folder-based random variant selection");
        if (descriptor.IsTwoDimensional)
            throw Unsupported(descriptor, "2D/menu routing in a 3D playback owner");
        var unsupportedFlags = descriptor.Flags & ~SupportedThreeDimensionalFlags;
        if ((unsupportedFlags & FalloutSoundFlags.RandomFrequencyShift) != 0)
            throw Unsupported(descriptor, "random frequency shifting");
        if ((unsupportedFlags & FalloutSoundFlags.PlayAtRandom) != 0)
            throw Unsupported(descriptor, "autonomous random playback scheduling");
        if ((unsupportedFlags & FalloutSoundFlags.RandomLocation) != 0)
            throw Unsupported(descriptor, "random playback location");
        if ((unsupportedFlags & FalloutSoundFlags.StartAtRandomPosition) != 0)
            throw Unsupported(descriptor, "random stream start position");
        if ((unsupportedFlags & (FalloutSoundFlags.EnvelopeFast | FalloutSoundFlags.EnvelopeSlow)) != 0)
            throw Unsupported(descriptor, "fast or slow loop-envelope behavior");
        if ((unsupportedFlags & FalloutSoundFlags.Lfe360) != 0)
            throw Unsupported(descriptor, "360-degree LFE routing");
        if ((unsupportedFlags & FalloutSoundFlags.Radius2D) != 0)
            throw Unsupported(descriptor, "2D-radius routing in a 3D playback owner");
        if (unsupportedFlags != 0)
            throw Unsupported(descriptor, $"sound flags 0x{(ushort)unsupportedFlags:x4}");
        if (descriptor.FixedPitchScale <= 0.0f)
            throw Unsupported(descriptor, "a nonpositive fixed frequency scale");
        if (descriptor.StopTime != 0 || descriptor.StartTime != 0)
            throw Unsupported(descriptor, "envelope or timed start/stop behavior");
        if ((descriptor.Flags & FalloutSoundFlags.EnvironmentIgnored) == 0 &&
            descriptor.ReverbAttenuation is not FullEnvironmentReverbSend and
                not FullyAttenuatedReverbSend)
            throw Unsupported(descriptor, "authored 3D reverb send attenuation");
        if (!descriptor.IsLooping &&
            (descriptor.LoopStartSample != 0 || descriptor.LoopEndSample != 0))
            throw Unsupported(descriptor, "loop points on a non-looping sound");
        var extension = Path.GetExtension(descriptor.LogicalPath);
        if (descriptor.IsLooping && descriptor.LoopEndSample != 0 &&
            (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
             descriptor.LoopEndSample <= descriptor.LoopStartSample ||
             descriptor.LoopEndSample > int.MaxValue))
            throw Unsupported(descriptor, "sample-indexed loop bounds for this codec");
        descriptor.ValidateAttenuationCurve();
        _ = descriptor.AttenuationDbAtDistanceGameUnits(descriptor.MinimumDistanceGameUnits);
    }

    internal static bool RequiresEnvironmentReverb(FalloutSoundRecord descriptor)
    {
        ValidateThreeDimensional(descriptor);
        return (descriptor.Flags & FalloutSoundFlags.EnvironmentIgnored) == 0 &&
            descriptor.ReverbAttenuation == FullEnvironmentReverbSend;
    }

    internal static void ValidateEnvironmentReverbAreaMask(
        FalloutSoundRecord descriptor,
        uint environmentReverbAreaMask)
    {
        var requiresEnvironmentReverb = RequiresEnvironmentReverb(descriptor);
        if (requiresEnvironmentReverb && environmentReverbAreaMask == 0U)
            throw Unsupported(descriptor,
                "a source-bound Area3D reverb bus mask for the current acoustic environment");
        if (!requiresEnvironmentReverb && environmentReverbAreaMask != 0U)
            throw Unsupported(descriptor,
                "an environment Area3D reverb send for a dry or environment-ignored sound");
    }

    internal static bool PassesRandomChance(
        FalloutSoundRecord descriptor,
        FalloutSoundRandomState random)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(random);
        return descriptor.RandomChancePercent == 0 ||
            random.NextBounded(PercentRollUpperBound) < descriptor.RandomChancePercent;
    }

    internal static NotSupportedException Unsupported(
        FalloutSoundRecord descriptor,
        string behavior) =>
        new($"SOUN {descriptor.FormKey} cannot play because OpenNV does not yet implement {behavior}.");
}

internal sealed class FalloutSoundRandomState
{
    private const ulong WeylIncrement = 0x9e3779b97f4a7c15UL;
    private const ulong FirstMixMultiplier = 0xbf58476d1ce4e5b9UL;
    private const ulong SecondMixMultiplier = 0x94d049bb133111ebUL;
    private const int FirstMixShift = 30;
    private const int SecondMixShift = 27;
    private const int FinalMixShift = 31;

    internal FalloutSoundRandomState(ulong state) => State = state;

    internal ulong State { get; private set; }

    internal float NextUnitFloat() => (float)(unchecked((uint)NextUInt64()) / 4294967296.0);

    internal uint NextBounded(uint exclusiveUpperBound)
    {
        if (exclusiveUpperBound == 0U)
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        var rejectionThreshold = unchecked((uint)(0U - exclusiveUpperBound)) % exclusiveUpperBound;
        while (true)
        {
            var candidate = unchecked((uint)NextUInt64());
            if (candidate >= rejectionThreshold)
                return candidate % exclusiveUpperBound;
        }
    }

    private ulong NextUInt64()
    {
        State = unchecked(State + WeylIncrement);
        var mixed = State;
        mixed = (mixed ^ (mixed >> FirstMixShift)) * FirstMixMultiplier;
        mixed = (mixed ^ (mixed >> SecondMixShift)) * SecondMixMultiplier;
        return mixed ^ (mixed >> FinalMixShift);
    }
}

internal static class FalloutSoundRecordReader
{
    private const int LegacyDataBytes = 12;
    private const int CurrentDataBytes = 36;
    private const int CurvePoints = 5;
    private const int CurveOffset = 12;
    private const int ReverbOffset = 22;
    private const int PriorityOffset = 24;
    private const int LoopStartOffset = 28;
    private const int LoopEndOffset = 32;
    private const int FlagsOffset = 4;
    private const int StaticAttenuationOffset = 8;
    private const int StopTimeOffset = 10;
    private const int StartTimeOffset = 11;
    private const int ObjectBoundsBytes = 12;
    private const byte MaximumRandomChancePercent = 100;
    private const ushort KnownFlagMask = 0x3fff;

    internal static FalloutSoundRecord Read(
        FalloutPluginStack stack,
        FalloutFormKey formKey) => Read(stack.GetEffective(formKey));

    internal static FalloutSoundRecord Read(FalloutPluginRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Signature == "SNDR")
            throw Error(record, "SNDR is not an admitted Fallout 3/New Vegas sound record layout");
        if (record.Signature != "SOUN")
            throw Error(record, $"expected SOUN, found {record.Signature}");

        var rows = record.ReadSubrecords().ToArray();
        EnsureKnownRows(record, rows);
        var editorId = DecodeText(record, Single(record, rows, "EDID").Data.Span, "EDID");
        var pathRow = Single(record, rows, "FNAM");
        var fileName = DecodeText(record, pathRow.Data.Span, "FNAM");
        if (string.IsNullOrWhiteSpace(fileName))
            throw Error(record, "FNAM is empty");
        var logicalPath = FalloutBsaArchive.CanonicalPath($"sound\\{fileName}");

        var randomRows = rows.Where(row => row.Signature == "RNAM").ToArray();
        if (randomRows.Length > 1 || randomRows.Length == 1 && randomRows[0].Data.Length != 1)
            throw Error(record, "RNAM must be absent or contain one byte");
        var randomChance = randomRows.Length == 0 ? (byte)0 : randomRows[0].Data.Span[0];
        if (randomChance > MaximumRandomChancePercent)
            throw Error(record, $"RNAM random chance exceeds {MaximumRandomChancePercent}: {randomChance}");

        var current = rows.Where(row => row.Signature == "SNDD").ToArray();
        var legacy = rows.Where(row => row.Signature == "SNDX").ToArray();
        if (current.Length + legacy.Length != 1)
            throw Error(record, "requires exactly one SNDX or SNDD");
        var data = current.Length == 1 ? current[0].Data.Span : legacy[0].Data.Span;
        if (data.Length != (current.Length == 1 ? CurrentDataBytes : LegacyDataBytes))
            throw Error(record, $"{(current.Length == 1 ? "SNDD" : "SNDX")} size is {data.Length}");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[FlagsOffset..]);
        if ((flags & ~KnownFlagMask) != 0)
            throw Error(record, $"sound flags contain unknown bits 0x{flags & ~KnownFlagMask:x4}");
        var curve = current.Length == 1
            ? ReadCurve(data[CurveOffset..])
            : ReadLegacyCurve(record, rows);
        var reverb = current.Length == 1
            ? BinaryPrimitives.ReadInt16LittleEndian(data[ReverbOffset..])
            : ReadOptionalInt16(record, rows, "GNAM");
        var priority = current.Length == 1
            ? BinaryPrimitives.ReadUInt32LittleEndian(data[PriorityOffset..])
            : ReadOptionalUInt32(record, rows, "HNAM");
        return new FalloutSoundRecord(
            record.FormKey,
            editorId,
            logicalPath,
            randomChance,
            data[0],
            data[1],
            unchecked((sbyte)data[2]),
            (FalloutSoundFlags)(ushort)flags,
            BinaryPrimitives.ReadInt16LittleEndian(data[StaticAttenuationOffset..]),
            data[StopTimeOffset],
            data[StartTimeOffset],
            curve,
            reverb,
            priority,
            current.Length == 1 ? BinaryPrimitives.ReadUInt32LittleEndian(data[LoopStartOffset..]) : 0,
            current.Length == 1 ? BinaryPrimitives.ReadUInt32LittleEndian(data[LoopEndOffset..]) : 0);
    }

    private static short[] ReadCurve(ReadOnlySpan<byte> data)
    {
        var result = new short[CurvePoints];
        for (var index = 0; index < result.Length; ++index)
            result[index] = BinaryPrimitives.ReadInt16LittleEndian(data[(index * sizeof(short))..]);
        return result;
    }

    private static short[] ReadLegacyCurve(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows)
    {
        var matches = rows.Where(row => row.Signature == "ANAM").ToArray();
        if (matches.Length != 1 || matches[0].Data.Length != CurvePoints * sizeof(short))
            throw Error(record, "legacy SOUN requires one ten-byte ANAM curve");
        return ReadCurve(matches[0].Data.Span);
    }

    private static short ReadOptionalInt16(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature)
    {
        var matches = rows.Where(row => row.Signature == signature).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && matches[0].Data.Length != sizeof(short))
            throw Error(record, $"{signature} must be absent or contain one Int16");
        return matches.Length == 0 ? (short)0 : BinaryPrimitives.ReadInt16LittleEndian(matches[0].Data.Span);
    }

    private static uint ReadOptionalUInt32(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature)
    {
        var matches = rows.Where(row => row.Signature == signature).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && matches[0].Data.Length != sizeof(uint))
            throw Error(record, $"{signature} must be absent or contain one UInt32");
        return matches.Length == 0 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(matches[0].Data.Span);
    }

    private static FalloutPluginSubrecord Single(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature)
    {
        var matches = rows.Where(row => row.Signature == signature).ToArray();
        if (matches.Length != 1)
            throw Error(record, $"requires exactly one {signature}");
        return matches[0];
    }

    private static void EnsureKnownRows(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
            { "EDID", "OBND", "FNAM", "RNAM", "SNDX", "SNDD", "ANAM", "GNAM", "HNAM" };
        var unknown = rows.Select(row => row.Signature).FirstOrDefault(signature => !known.Contains(signature));
        if (unknown is not null)
            throw Error(record, $"contains unsupported subrecord {unknown}");
        var bounds = rows.Where(row => row.Signature == "OBND").ToArray();
        if (bounds.Length > 1 || bounds.Length == 1 && bounds[0].Data.Length != ObjectBoundsBytes)
            throw Error(record, $"OBND must be absent or contain {ObjectBoundsBytes} bytes");
    }

    private static string DecodeText(
        FalloutPluginRecord record,
        ReadOnlySpan<byte> data,
        string signature)
    {
        var terminator = data.IndexOf((byte)0);
        if (terminator < 0 || terminator != data.Length - 1)
            throw Error(record, $"{signature} must have one terminal zero");
        return FalloutPlugin.DecodeZeroTerminated(data, $"{signature} text in {record.Plugin.Name}");
    }

    private static FalloutPluginFormatException Error(FalloutPluginRecord record, string detail) =>
        new($"{record.Plugin.Name} {record.Signature} {record.RawFormId:x8} {detail} at 0x{record.HeaderOffset:x}.");
}
