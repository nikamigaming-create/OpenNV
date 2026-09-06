namespace OpenNV.Runtime.Content;

// REFR.XEMI resolves through the winning record graph. Region colours sample
// authoritative sky/time state; neither renderer nor reference owns weather.
internal sealed class FalloutExternalEmittance
{
    private readonly Func<float[]> _sample;
    internal FalloutFormKey Source { get; }

    internal FalloutExternalEmittance(FalloutPluginStack records, FalloutFormKey source,
        Func<FalloutFormKey, float[]>? regionEmittance)
    {
        Source = source;
        var record = records.GetEffective(source);
        if (record.Signature == "LIGH")
        {
            var color = FalloutPlacedLightResolver.NormalizeLightColor(FalloutCellSceneReader.ReadLight(record).ColorRgb);
            _sample = () => color;
        }
        else if (record.Signature == "REGN" && regionEmittance is not null)
            _sample = () => regionEmittance(source);
        else throw new NotSupportedException($"External emittance {source} needs a {record.Signature} emittance owner.");
    }

    internal float[] Sample()
    {
        var color = _sample();
        if (color.Length != 3 || color.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidDataException($"External emittance {Source} has invalid RGB.");
        return color;
    }
}
