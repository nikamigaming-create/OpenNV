namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNumericIniSettings
{
    // Native consumers must distinguish missing/type-invalid settings from
    // NVSE's numeric failure result, which is also a valid Float32 value.
    internal double Read(string name)
    {
        var value = Require(name);
        return value.Number ?? throw new InvalidDataException($"INI setting {value.Declaration.Name} is not numeric.");
    }

    internal float Float(string name)
    {
        var value = Require(name);
        if (value.Declaration.Kind != 'f')
            throw new InvalidDataException($"INI setting {value.Declaration.Name} is not a Float32.");
        return (float)(value.Number ?? throw new InvalidDataException($"INI setting {value.Declaration.Name} has no numeric payload."));
    }

    private FalloutIniValue Require(string name) => Resolve(name) ??
        throw new NotSupportedException($"Owned numeric INI setting is unbound: {name}.");

    private FalloutIniValue? Resolve(string name) =>
        _collections[FalloutIniCollection.Prefs].GetValueOrDefault(name) ??
        _collections[FalloutIniCollection.Main].GetValueOrDefault(name);
}
