using System.Globalization;

namespace OpenNV.Runtime.Content;

internal enum FalloutIniCollection { Main, Prefs, Renderer }

// Full source identity and canonical type survive caller case folding. The
// renderer collection is retained independently, even where files are shared.
internal sealed record FalloutIniDeclaration(string Name, FalloutIniCollection Collection, uint Payload)
{
    internal char Kind => Name.Length == 0 ? '\0' : Name[0];
    internal double? NumericDefault => Kind switch
    {
        'b' or 'i' => unchecked((int)Payload),
        'u' => Payload,
        'f' => BitConverter.Int32BitsToSingle(unchecked((int)Payload)),
        _ => null,
    };
}

// A null collection is an explicit profile overlay over registered owners.
// File rows never create declarations, regardless of their text or prefix.
internal sealed record FalloutIniLayer(string Source, FalloutIniCollection? Collection,
    IReadOnlyList<FalloutInstallationSetting> Values);
internal sealed record FalloutIniValue(FalloutIniDeclaration Declaration, double? Number, string Origin);

internal sealed partial class FalloutNumericIniSettings
{
    private readonly IReadOnlyDictionary<FalloutIniCollection, IReadOnlyDictionary<string, FalloutIniValue>> _collections;
    internal string Source { get; }
    internal IReadOnlyList<FalloutIniLayer> Layers { get; }

    internal FalloutNumericIniSettings(IReadOnlyList<FalloutIniDeclaration> declarations,
        IReadOnlyList<FalloutIniLayer> layers, string source)
    {
        Source = source;
        Layers = Array.AsReadOnly(layers.Select(layer => layer with { Values = Array.AsReadOnly(layer.Values.ToArray()) }).ToArray());
        var collections = Enum.GetValues<FalloutIniCollection>().ToDictionary(kind => kind,
            _ => new Dictionary<string, FalloutIniValue>(StringComparer.OrdinalIgnoreCase));
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.Name) || !collections.ContainsKey(declaration.Collection))
                throw new InvalidDataException("INI declaration has an invalid identity or collection.");
            var number = declaration.NumericDefault;
            if (number is { } value && !double.IsFinite(value) || declaration.Kind == 'b' && declaration.Payload > 1)
                throw new InvalidDataException($"INI declaration has an invalid numeric payload: {declaration.Name}.");
            if (!collections[declaration.Collection].TryAdd(declaration.Name, new(declaration, number, source)))
                throw new InvalidDataException($"INI collection contains duplicate declarations: {declaration.Name}.");
        }
        foreach (var layer in Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Source) || layer.Collection is { } collection && !collections.ContainsKey(collection))
                throw new InvalidDataException("INI layer has no admitted provenance or collection.");
            foreach (var row in layer.Values)
            {
                var identity = row.Key + ":" + row.Section;
                foreach (var (kind, entries) in collections)
                {
                    if (layer.Collection is { } selected && selected != kind || !entries.TryGetValue(identity, out var found)) continue;
                    var number = Parse(found.Declaration, row.Value);
                    entries[found.Declaration.Name] = found with { Number = number, Origin = layer.Source };
                }
            }
        }
        _collections = collections.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyDictionary<string, FalloutIniValue>)new System.Collections.ObjectModel.ReadOnlyDictionary<string, FalloutIniValue>(pair.Value));
    }

    internal double Get(string name)
    {
        // NVSE's real lookup has no section grammar precheck. A nonnumeric
        // preferences declaration is a found result and blocks the main one.
        return Resolve(name)?.Number ?? -1;
    }

    internal FalloutIniValue? Find(FalloutIniCollection collection, string name) => _collections[collection].GetValueOrDefault(name);
    internal IReadOnlyList<FalloutIniValue> Declarations => _collections.Values.SelectMany(collection => collection.Values).ToArray();

    private static double? Parse(FalloutIniDeclaration declaration, string text)
    {
        const NumberStyles integer = NumberStyles.Integer;
        double? number = declaration.Kind switch
        {
            'b' => int.Parse(text, integer, CultureInfo.InvariantCulture) == 0 ? 0 : 1,
            'i' => int.Parse(text, integer, CultureInfo.InvariantCulture),
            'u' => uint.Parse(text, integer, CultureInfo.InvariantCulture),
            'f' => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => null,
        };
        if (number is { } value && !double.IsFinite(value))
            throw new InvalidDataException($"INI override is non-finite: {declaration.Name}.");
        return number;
    }
}
