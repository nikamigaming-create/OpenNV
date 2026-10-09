using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    private static readonly FalloutSourceSkyImageSlot[] AllocationOrder =
        [FalloutSourceSkyImageSlot.CurrentPrimary, FalloutSourceSkyImageSlot.PreviousPrimary,
         FalloutSourceSkyImageSlot.CurrentSecondary, FalloutSourceSkyImageSlot.PreviousSecondary];
    private static readonly FalloutSourceSkyImageSlot[] ManagerOrder =
        [FalloutSourceSkyImageSlot.PreviousPrimary, FalloutSourceSkyImageSlot.CurrentPrimary,
         FalloutSourceSkyImageSlot.PreviousSecondary, FalloutSourceSkyImageSlot.CurrentSecondary];
    private FalloutSourceSkyImageModifier[] OrderedInstances() => ManagerOrder.Where(_instances.ContainsKey)
        .Select(slot => _instances[slot]).ToArray();
    private void ConstructImages()
    {
        // Source creation is lazy. Each completed allocation/registration is
        // retained individually if a later child fails; it is not a Boolean.
        for (var pair = 0; pair < 2; pair++)
        {
            for (var index = pair * 2; index < pair * 2 + 2; index++)
            {
                var slot = AllocationOrder[index];
                if (_instances.ContainsKey(slot)) continue;
                var instance = new FalloutSourceSkyImageModifier(Guid.NewGuid(), Identity, slot, null, 0, 0,
                    0, FalloutSourceSkyImageProgramKind.Null, null, null, Next());
                _instances.Add(slot, instance);
            }
            // Both selected originals register this pair before allocating
            // the next one. A failed registration retains only its real prefix.
            for (var ordinal = pair * 2; ordinal < pair * 2 + 2; ordinal++)
            {
                var slot = ManagerOrder[ordinal]; var instance = _instances[slot];
                if (instance.ManagerOrdinal is not null) continue;
                _images.RegisterSourceSkyInstance(this, instance.Identity, ordinal);
                _instances[slot] = instance with { ManagerOrdinal = ordinal, Changed = Next() };
            }
        }
        foreach (var slot in AllocationOrder)
            _instances[slot] = _instances[slot] with { Flags = _instances[slot].Flags | 1, Changed = Next() };
        foreach (var slot in AllocationOrder)
            _instances[slot] = _instances[slot] with { EnabledByte = 1, Changed = Next() };
    }
    private void UpdateResetImages()
    {
        ConstructImages();
        if (_modeFailure is not null) throw new NotSupportedException(_modeFailure);
        if (Mode is 0 or 1)
        {
            foreach (var slot in Enum.GetValues<FalloutSourceSkyImageSlot>()) StoreWeight(slot, 0);
            return;
        }
        if (_clockFailure is not null) throw new NotSupportedException(_clockFailure);
        var weights = ResetImageWeights();
        var blend = BitConverter.UInt32BitsToSingle(BlendBits);
        if (!float.IsFinite(blend)) throw new InvalidDataException("Sky image-instance blend has no finite source field.");
        // Both WTHR pointers have already been nulled by THIS reset call.
        // Every lookup therefore reaches the selected anonymous default. A
        // later weather picker/transition is an independent original owner.
        BindDefault(FalloutSourceSkyImageSlot.CurrentPrimary, Source.IsStandalone ?
            FalloutSkyStandaloneInterpolation.Primary(weights.PrimaryWeight, blend) : (float)(blend * (double)weights.PrimaryWeight));
        if (weights.Interpolated)
            BindDefault(FalloutSourceSkyImageSlot.CurrentSecondary, Source.IsStandalone ?
                FalloutSkyStandaloneInterpolation.Secondary(weights.PrimaryWeight, blend) : (float)((1.0 - weights.PrimaryWeight) * blend));
        else StoreWeight(FalloutSourceSkyImageSlot.CurrentSecondary, 0);
        StoreWeight(FalloutSourceSkyImageSlot.PreviousPrimary, 0);
        StoreWeight(FalloutSourceSkyImageSlot.PreviousSecondary, 0);
    }
    private void StoreWeight(FalloutSourceSkyImageSlot slot, float weight)
    {
        if (!float.IsFinite(weight)) throw new InvalidDataException("Source Sky weight became non-finite.");
        _instances[slot] = _instances[slot] with { WeightBits = BitConverter.SingleToUInt32Bits(weight), Changed = Next() };
    }
    private void BindDefault(FalloutSourceSkyImageSlot slot, float weight)
    {
        StoreWeight(slot, weight);
        _instances[slot] = _instances[slot] with
        {
            Program = FalloutSourceSkyImageProgramKind.AnonymousEngineDefault,
            Form = null,
            ProgramSha256 = FalloutSourceSkyImageProgram.DefaultIdentity(Source),
            Changed = Next()
        };
    }
    private FalloutSkyImageWeights ResetImageWeights()
    {
        if (Source.IsStandalone) return ReadStandaloneImageWeights();
        // Getter order matters: each dirty getter commits its own cached
        // Float32 and clears only that source flag, even if a later one fails.
        var start = ReadTimeCache(0x1000, 0); var sunriseEnd = ReadTimeCache(0x200, 1);
        var sunsetStart = ReadTimeCache(0x400, 2); var end = ReadTimeCache(0x2000, 3);
        var time = BitConverter.UInt32BitsToSingle(HourBits);
        var noon = BitConverter.UInt32BitsToSingle(Source.NoonBits);
        if (!float.IsFinite(time)) throw new InvalidDataException("Sky reset has a non-finite actual clock cell.");
        static FalloutSkyImageWeights Half(int primary, int before, int after, float value, float lower, float upper)
        {
            var half = (float)((upper - (double)lower) * 0.5);
            var middle = (float)(lower + (double)half);
            return value < middle ? new(primary, before, (float)(1.0 - (middle - (double)value) / half), true) :
                new(primary, after, (float)(1.0 - (value - (double)middle) / half), true);
        }
        if (time >= start && time < sunriseEnd) return Half(0, 3, 1, time, start, sunriseEnd);
        if (time > sunriseEnd && time < noon)
            return new(1, 4, (float)(1.0 - (noon - (double)time) / (noon - (double)sunriseEnd)), true);
        if (time > noon && time < sunsetStart)
            return new(1, 4, (float)(1.0 - (sunsetStart - (double)time) / (sunsetStart - (double)noon)), true);
        if (time >= sunsetStart && time < end) return Half(2, 1, 3, time, sunsetStart, end);
        if (time >= end || time < start) return new(3, 0, 1, false);
        // The original emits an assertion then uses its distinct day slot.
        // A finite replacement must expose that assertion rather than hide it.
        throw new NotSupportedException("source-Sky-image-time-assertion-side-effect-before-day-fallback-consumer-unowned");
    }
    private float ReadTimeCache(uint bit, int index)
    {
        uint Read() => index switch
        {
            0 => TimeCaches.SunriseStart,
            1 => TimeCaches.SunriseEnd,
            2 => TimeCaches.SunsetStart,
            3 => TimeCaches.SunsetEnd,
            _ => throw new InvalidDataException("Sky cache getter requested another source index.")
        };
        if ((Flags & bit) != 0)
        {
            // End getters preserve their source dirty flag while climate is
            // null. The extended start/end getters still use their originals.
            if (Climate is null && index is 1 or 2) return BitConverter.UInt32BitsToSingle(Read());
            if (Climate is null) throw new NotSupportedException("source-Sky-dirty-extended-time-getter-with-null-climate-unowned");
            var climate = FalloutClimateLighting.Read(_records.GetEffective(Climate.Value));
            var value = index switch
            {
                0 => MathF.Max(0, (float)(climate.SunriseStart - (double)_daytimeExtension)),
                1 => climate.SunriseEnd,
                2 => climate.SunsetStart,
                3 => MathF.Min(BitConverter.UInt32BitsToSingle(Source.LastHourBits),
                      (float)(climate.SunsetEnd + (double)_daytimeExtension)),
                _ => throw new InvalidDataException()
            };
            var bits = BitConverter.SingleToUInt32Bits(value);
            TimeCaches = index switch
            {
                0 => TimeCaches with { SunriseStart = bits },
                1 => TimeCaches with { SunriseEnd = bits },
                2 => TimeCaches with { SunsetStart = bits },
                3 => TimeCaches with { SunsetEnd = bits },
                _ => throw new InvalidDataException()
            };
            Flags &= ~bit; Next();
        }
        return BitConverter.UInt32BitsToSingle(Read());
    }
    internal IReadOnlyList<FalloutSourceSkyImageContribution> ImageContributions()
    {
        var result = new List<FalloutSourceSkyImageContribution>();
        foreach (var instance in OrderedInstances())
        {
            FalloutSourceSkyImageProgram.Validate(instance, Source, _records);
            FalloutImageSpaceModifier? program = instance.Form is { } form ? FalloutImageSpaceModifierReader.Read(_records.GetEffective(form)) : null;
            var failure = instance.Program == FalloutSourceSkyImageProgramKind.WinningImad ?
                "source-Sky-ImageSpaceModifierInstanceForm-native-program-clock-and-composition-consumer-unowned" : null;
            result.Add(new(instance, program, failure));
        }
        return result;
    }
    internal void MarkExteriorFactoryEntered()
    {
        RequireWriter();
        _modeFailure = "source-Sky-exterior-mode-writer-and-root-Moon-factory-consumers-unowned";
        _clockFailure = "source-Sky-frame-calendar-getter-store-after-actual-Player-presence-unowned";
        if (_precipitationBinding.Disposition == FalloutSkyChildDisposition.ConstructorNull)
            _precipitationBinding = new(FalloutSkyChildDisposition.Unowned, "source-Sky-exterior-Precipitation-field-factory-unowned", null);
        Next();
    }
}
