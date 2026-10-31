using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWeatherImageSpace(FalloutFormKey Weather, FalloutImageSpaceModifier?[] Slots)
{
    internal static FalloutWeatherImageSpace Read(FalloutPluginStack records, FalloutPluginRecord weather)
    {
        if (weather.Signature != "WTHR") throw new InvalidDataException("Weather image space requires WTHR.");
        var slots = new FalloutImageSpaceModifier?[6];
        var seen = new HashSet<int>();
        foreach (var field in weather.ReadSubrecords())
        {
            // The plugin reader canonicalizes the binary slot byte to a
            // decimal prefix, just as it does for IMAD scalar channels.
            if (field.Signature.Length != 4 || field.Signature[0] is < '0' or > '5' || field.Signature[1..] != "IAD") continue;
            var slot = field.Signature[0] - '0';
            if (!seen.Add(slot) || field.Data.Length != 4)
                throw new InvalidDataException("Weather image-space slot is duplicated or malformed.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            if (id == 0) continue;
            var source = FalloutImageSpaceModifierReader.Read(records.GetEffective(weather.Plugin.AdjustFormId(id)));
            if (source.Animated)
                throw new NotSupportedException($"Weather modifier {source.Form} requires its independent instance-age owner.");
            slots[slot] = source;
        }
        return new(weather.FormKey, slots);
    }

    internal IReadOnlyList<FalloutWeatherImageModifier> Sample(FalloutClimateLighting climate, float hour, float extension)
    {
        var weights = Weights(climate, hour, extension);
        var result = new List<FalloutWeatherImageModifier>(2);
        Add(weights.First, weights.FirstWeight); Add(weights.Second, weights.SecondWeight);
        return result;
        void Add(int slot, float strength)
        {
            if (strength <= 0) return;
            // An empty slot selects the engine's default modifier; an explicit
            // unresolved source reference has already failed in Read.
            result.Add(new(slot, Slots[slot], strength));
        }
    }

    private static FalloutWeatherTimeWeights Weights(FalloutClimateLighting climate, float hour, float extension)
    {
        if (!float.IsFinite(hour) || hour < 0 || hour > 24 || !float.IsFinite(extension) || extension < 0)
            throw new InvalidDataException("Weather image-space time is invalid.");
        var start = MathF.Max(0, climate.SunriseStart - extension);
        var end = MathF.Min(23.99f, climate.SunsetEnd + extension);
        if (!(start < climate.SunriseEnd && climate.SunriseEnd < 12 && 12 < climate.SunsetStart && climate.SunsetStart < end))
            throw new NotSupportedException("Climate image-space intervals are not ordered.");
        static FalloutWeatherTimeWeights Blend(int first, int second, float weight) => new(first, second, weight, 1 - weight);
        if (hour >= start && hour < climate.SunriseEnd)
        {
            var half = (climate.SunriseEnd - start) * .5f;
            var middle = start + half;
            return hour < middle ? Blend(0, 3, 1 - (middle - hour) / half) : Blend(0, 1, 1 - (hour - middle) / half);
        }
        if (hour >= climate.SunriseEnd && hour <= 12)
            // IMAD uses these source/percentage pairings independently of sky
            // color interpolation: slot 1, not slot 4, dominates at noon.
            return Blend(1, 4, (hour - climate.SunriseEnd) / (12 - climate.SunriseEnd));
        if (hour >= 12 && hour <= climate.SunsetStart)
            return Blend(4, 1, (hour - 12) / (climate.SunsetStart - 12));
        if (hour >= climate.SunsetStart && hour < end)
        {
            var half = (end - climate.SunsetStart) * .5f;
            var middle = climate.SunsetStart + half;
            return hour < middle ? Blend(2, 1, 1 - (middle - hour) / half) : Blend(2, 3, 1 - (hour - middle) / half);
        }
        return Blend(3, 3, 1);
    }
}
