using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

// Winning source declarations only. Placement, execution and rendered light
// behavior need independent joins and are not inferred from these records.
internal static class LightSourceGraph
{
    private sealed record Row(string Source, string Winner, bool Deleted, string Sha256,
        uint Flags, int Duration, uint Radius, float Falloff, float FieldOfView,
        uint NearClip, float Period, float Intensity, byte[] Rgb, byte Alpha, string? Model);

    internal static int Run(FalloutPluginStack records)
    {
        var sources = records.EffectiveRecords("LIGH").ToArray();
        var rows = new List<Row>(sources.Length);
        var failures = new List<object>();
        foreach (var source in sources)
        {
            try
            {
                var light = FalloutCellSceneReader.ReadLight(source);
                var models = source.ReadSubrecords().Where(field => field.Signature == "MODL").ToArray();
                if (models.Length > 1) throw new InvalidDataException("Light has multiple source models.");
                var model = models.Length == 0 ? null : FalloutDialogueTopic.Text(models[0].Data.Span);
                rows.Add(new(source.FormKey.ToString(), source.Plugin.Name, (source.Flags & 0x20) != 0,
                    Convert.ToHexString(SHA256.HashData(source.ReadData())), light.Flags, light.Duration,
                    light.RadiusGameUnits, light.Falloff, light.FieldOfViewDegrees, light.NearClip,
                    light.Period, light.Intensity, light.ColorRgb, light.ColorAlpha, model));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = source.FormKey.ToString(), error = error.Message }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-light-source-graph/v1", denominator = sources.Length, decoded = rows.Count,
            flags = rows.GroupBy(row => row.Flags).OrderBy(group => group.Key)
                .Select(group => new { value = group.Key, count = group.Count(), withModel = group.Count(row => row.Model is not null) }),
            failures, rows,
            boundary = "Winning light declarations only; placed references, model resolution, runtime execution and retail behavior are uninspected."
        }));
        return failures.Count == 0 ? 0 : 1;
    }
}
