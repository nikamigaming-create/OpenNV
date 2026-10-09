using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// The two Moon fields are independent of this reset child. The selected
// Precipitation constructor owns two null nodes and a separate scene parent.
internal sealed record FalloutSkyNativeChildSource(string EngineSha256, string SkyContract, string Contract)
{
    internal const string PrecipitationSetting = "bPrecipitation:Weather";
    internal const string CloudPropertyClass = "SkyShaderProperty";
    internal const uint CloudPropertyType = 3;
    internal const int CloudCapacity = 4;
    private const string Rules = "source-Sky-native-reset-children/v1;Clouds-four-geometry-and-staged-texture-slots;" +
        "geometry-property-type-three-dynamic-SkyShaderProperty;reset-staged-texture-null-before-secondary-property-texture-null-before-Float32-positive-zero;" +
        "primary-weather-texture-unchanged-by-reset;Precipitation-Main-bPrecipitation-Weather-byte-payload-gate;" +
        "enabled-constructor-first-node-second-node-parent-null-scalar-positive-zero;load-retains-actual-scene-parent;" +
        "reset-second-node-parent-detach-reference-release-null-before-first-node-parent-detach-reference-release-null;" +
        "Rain-Snow-node-writers-and-two-independent-Moon-factories-not-admitted-by-null-construction";

    internal static FalloutSkyNativeChildSource Read(FalloutSkyTransferDeclaration sky)
    {
        sky.Validate();
        var bytes = Encoding.UTF8.GetBytes(sky.EngineSha256 + ";" + sky.Contract + ";" + Rules);
        return new(sky.EngineSha256, sky.Contract, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    internal void Require(FalloutSkyTransferDeclaration sky)
    {
        if (this != Read(sky)) throw new InvalidDataException("Sky native child changed its selected source declaration.");
    }
    internal FalloutIniValue Precipitation(FalloutNumericIniSettings settings)
    {
        var value = settings.Find(FalloutIniCollection.Main, PrecipitationSetting) ??
            throw new NotSupportedException("Precipitation has no original Main INI declaration.");
        if (value.Declaration.Kind != 'b' || value.Number is not (0 or 1) || string.IsNullOrWhiteSpace(value.Origin))
            throw new InvalidDataException("Precipitation gate changed its actual typed byte payload or provenance.");
        return value;
    }
}
