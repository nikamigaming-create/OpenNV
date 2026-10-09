namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceIniString(string Name, string Value, string Origin);

internal sealed partial class FalloutInstallationSettings
{
    internal FalloutSourceIniString SourceString(FalloutIniCollection collection, string name,
        IReadOnlyList<FalloutIniStringDefault> defaults)
    {
        var registered = NumericIni.Find(collection, name)?.Declaration ?? throw new NotSupportedException(
            "Selected string setting has no actual collection declaration.");
        if (registered.Kind != 's') throw new InvalidDataException("String read changed the registered INI type.");
        var rows = defaults.Where(row => row.Declaration.Collection == collection &&
            row.Declaration.Name.Equals(registered.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length != 1 || rows[0].Declaration != registered)
            throw new InvalidDataException("String default differs from the selected executable declaration.");
        var value = rows[0].Value; var origin = NumericIni.Source;
        foreach (var layer in NumericIni.Layers)
        {
            if (layer.Collection is { } actual && actual != collection) continue;
            foreach (var row in layer.Values)
                if ((row.Key + ":" + row.Section).Equals(registered.Name, StringComparison.OrdinalIgnoreCase))
                { value = row.Value; origin = layer.Source; }
        }
        if (value is null || value.Contains('\0') || string.IsNullOrWhiteSpace(origin))
            throw new InvalidDataException("Selected string setting lost its source value/provenance.");
        return new(registered.Name, value, origin);
    }
}
