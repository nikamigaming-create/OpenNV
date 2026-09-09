using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutExteriorClimate
{
    internal static FalloutFormKey Resolve(FalloutPluginStack records, FalloutFormKey world)
    {
        var seen = new HashSet<FalloutFormKey>();
        while (seen.Add(world))
        {
            var record = records.GetEffective(world);
            if (record.Signature != "WRLD") throw new InvalidDataException("Climate world is not WRLD.");
            var fields = record.ReadSubrecords().ToArray();
            var parent = fields.SingleOrDefault(field => field.Signature == "WNAM").Data;
            var flags = fields.SingleOrDefault(field => field.Signature == "PNAM").Data;
            if (!flags.IsEmpty && flags.Length != 2) throw new InvalidDataException("WRLD parent flags require two bytes.");
            if (!parent.IsEmpty && flags.Length == 2 && (BinaryPrimitives.ReadUInt16LittleEndian(flags.Span) & 16) != 0)
            {
                if (parent.Length != 4) throw new InvalidDataException("WRLD parent requires one FormID.");
                world = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(parent.Span));
                continue;
            }
            var climate = fields.SingleOrDefault(field => field.Signature == "CNAM").Data;
            if (climate.IsEmpty) return records.RuntimeFormKey(0x15f);
            if (climate.Length != 4) throw new InvalidDataException("WRLD climate requires one FormID.");
            return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(climate.Span)) ?? records.RuntimeFormKey(0x15f);
        }
        throw new InvalidDataException("World climate inheritance contains a cycle.");
    }

    internal static FalloutFormKey SelectWeather(FalloutPluginStack records, FalloutFormKey climate,
        FalloutGlobalState? globals, FalloutSoundRandomState random)
    {
        var record = records.GetEffective(climate);
        if (record.Signature is not ("CLMT" or "REGN")) throw new InvalidDataException("Weather selection requires CLMT or REGN.");
        var data = record.ReadSubrecords().Single(field => field.Signature == (record.Signature == "CLMT" ? "WLST" : "RDWT")).Data;
        if (data.Length == 0 || data.Length % 12 != 0) throw new InvalidDataException("Climate weather list has an invalid extent.");
        var choices = new List<(FalloutFormKey Form, double Weight)>();
        for (var offset = 0; offset < data.Length; offset += 12)
        {
            var weather = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data.Span[offset..]));
            var global = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(offset + 8)..]));
            var weight = global is { } form ? (globals ?? throw new InvalidOperationException("Weather chance global has no owner.")).Get(form)
                : BinaryPrimitives.ReadInt32LittleEndian(data.Span[(offset + 4)..]);
            if (!double.IsFinite(weight) || weight < 0) throw new InvalidDataException("Weather chance is invalid.");
            if (weather is null || weight == 0) continue;
            _ = FalloutWeatherLighting.Read(records.GetEffective(weather.Value));
            choices.Add((weather.Value, weight));
        }
        if (choices.Count == 0) throw new InvalidDataException("Climate has no weather with a positive chance.");
        var remaining = random.NextUnitFloat() * choices.Sum(choice => choice.Weight);
        foreach (var choice in choices)
        {
            remaining -= choice.Weight;
            if (remaining < 0) return choice.Form;
        }
        return choices[^1].Form;
    }

    internal static FalloutFormKey? WeatherRegion(FalloutPluginStack records, FalloutCellDefinition cell, float x, float y)
    {
        var source = records.GetEffective(cell.FormKey);
        var list = source.ReadSubrecords().SingleOrDefault(field => field.Signature == "XCLR").Data;
        if (list.Length % 4 != 0) throw new InvalidDataException("CELL region list has an invalid extent.");
        var selected = new List<(FalloutFormKey Form, byte Priority)>();
        for (var offset = 0; offset < list.Length; offset += 4)
        {
            var region = records.GetEffective(source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(list.Span[offset..])));
            if (region.Signature != "REGN") throw new InvalidDataException("CELL region list contains another record type.");
            var fields = region.ReadSubrecords().ToArray();
            var weather = fields.Where(field => field.Signature == "RDAT" && field.Data.Length == 8 &&
                BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == 3).ToArray();
            if (weather.Length == 0) continue;
            if (weather.Length != 1) throw new InvalidDataException("Region repeats its weather declaration.");
            if (!fields.Where(field => field.Signature == "RPLD").Any(field => Contains(field.Data.Span, x, y))) continue;
            selected.Add((region.FormKey, weather[0].Data.Span[5]));
        }
        if (selected.Count == 0) return null;
        var priority = selected.Max(value => value.Priority);
        var highest = selected.Where(value => value.Priority == priority).ToArray();
        if (highest.Length != 1) throw new NotSupportedException("Overlapping weather regions share priority; their arbitration is unbound.");
        return highest[0].Form;
    }

    internal static bool ContainsRegion(FalloutPluginStack records, FalloutFormKey regionKey,
        FalloutFormKey? world, float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Region query position is non-finite.");
        var region = records.GetEffective(regionKey);
        if (region.Signature != "REGN") throw new InvalidDataException("Region query target is not REGN.");
        if (world is null || FalloutDialogueTopic.RequiredForm(region, "WNAM") != world) return false;
        return region.ReadSubrecords().Where(field => field.Signature == "RPLD").Any(field => Contains(field.Data.Span, x, y));
    }

    private static bool Contains(ReadOnlySpan<byte> polygon, float x, float y)
    {
        if (polygon.Length < 24 || polygon.Length % 8 != 0) throw new InvalidDataException("Region polygon has an invalid extent.");
        var inside = false;
        for (int index = 0, previous = polygon.Length - 8; index < polygon.Length; previous = index, index += 8)
        {
            var ax = BinaryPrimitives.ReadSingleLittleEndian(polygon[index..]);
            var ay = BinaryPrimitives.ReadSingleLittleEndian(polygon[(index + 4)..]);
            var bx = BinaryPrimitives.ReadSingleLittleEndian(polygon[previous..]);
            var by = BinaryPrimitives.ReadSingleLittleEndian(polygon[(previous + 4)..]);
            if (!float.IsFinite(ax) || !float.IsFinite(ay) || !float.IsFinite(bx) || !float.IsFinite(by))
                throw new InvalidDataException("Region polygon has a non-finite vertex.");
            if ((ay > y) != (by > y) && x < (double)(bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
        }
        return inside;
    }
}
