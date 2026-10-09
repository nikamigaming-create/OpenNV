using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutMoonRole { Masser, Secunda }

// A declaration of the independently selected original constructors and their
// consumers. Native scene/resource publication is a separate, living owner.
internal sealed record FalloutMoonSource(string EngineSha256, string SkyContract, string Contract)
{
    private const string Rules = "source-two-Moons/v1;two-independent-climate-bits-seven-and-six;" +
        "climate-low-six-bits-phase-length;phase-cache-constructor-zero;" +
        "eight-phase-string-cells-full-three-wan-half-wan-one-wan-empty-one-wax-half-wax-three-wax;" +
        "Masser-and-Secunda-prefixes;constructor-five-live-Float32-GMSTs-and-one-UInt32-size;" +
        "constructor-angle-positive-zero-last-hour-Float32-maximum-pending-zero;" +
        "two-generated-four-vertex-two-triangle-quads-with-independent-colour-UV-and-property-owners;" +
        "actual-climate-dirty-bit-sixty-four-before-factories;captured-Sky-climate-pointer-not-logical-weather-selection;" +
        "source-quad-distance-five-hundred-twelve-and-negative-half-pi-X-rotation;" +
        "primary-property-six-shadow-property-seven;source-parent-shadow-child-before-primary-child;" +
        "source-Menu-query-returns-false-in-both-selected-originals;" +
        "first-update-last-hour-zero-angle-ninety;hour-wrap-add-twenty-four;speed-times-sixty-times-hour-difference;" +
        "phase-days-unsigned-truncation-remainder-length-times-eight-divide-length;zero-length-leaves-cache;" +
        "pending-two-loads-immediately-pending-one-waits-until-both-fades-nonpositive;" +
        "failed-empty-phase-load-keeps-last-sampler-and-clears-geometry-has-texture;" +
        "pending-cleared-only-after-actual-native-texture-operation-return;" +
        "mode-two-or-three-required-for-parent-transform-and-draw-fields;" +
        "Moon-factory-does-not-admit-Sky-mode-weather-frame-shader-constant-or-TLS-producers";

    internal const string ShadowTexture = "textures/sky/MoonShadow.dds";
    internal const float QuadDistance = 512f;
    internal bool IsStandalone => FalloutSourceMainFamily.IsFallout3(EngineSha256);
    internal static FalloutMoonSource Read(FalloutSkyTransferDeclaration sky)
    {
        sky.Validate();
        return new(sky.EngineSha256, sky.Contract, Hash(sky.EngineSha256 + ";" + sky.Contract + ";" + Rules));
    }
    internal void Require(FalloutSkyTransferDeclaration sky)
    {
        if (this != Read(sky)) throw new InvalidDataException("Moon changed its selected original Sky/constructor declaration.");
    }
    internal string? Texture(FalloutMoonRole role, int phase)
    {
        if (!Enum.IsDefined(role) || phase is < 0 or > 7) throw new InvalidDataException("Moon texture has another source role/phase cell.");
        string?[] phases = ["full", "three_wan", "half_wan", "one_wan", null, "one_wax", "half_wax", "three_wax"];
        return phases[phase] is { } suffix ? $"textures/sky/{role}_{suffix}.dds" : null;
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
