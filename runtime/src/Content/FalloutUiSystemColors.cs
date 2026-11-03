using System.Numerics;

namespace OpenNV.Runtime.Content;

// Hard palette identities are source constructor declarations. They cannot
// inherit legacy component rows or a different soft palette's packed override.
// HUDMain/Pipboy retain their existing separate soft-setting bindings.
internal static class FalloutUiSystemColors
{
    internal static Vector3 Read(FalloutInstallationSettings settings, string sourceIdentity)
    {
        if (!sourceIdentity.StartsWith("entity_", StringComparison.OrdinalIgnoreCase) || sourceIdentity.Length == 7)
            throw new NotSupportedException($"Owned system color has no source identity: {sourceIdentity}.");
        var owner = sourceIdentity[7..];
        if (owner.Equals("NoSystemColor", StringComparison.OrdinalIgnoreCase)) return Vector3.One;
        if (settings.HardSystemColors.TryGetValue(owner, out var hard))
            return new((hard >> 24) / 255f, ((hard >> 16) & 255) / 255f, ((hard >> 8) & 255) / 255f);
        var packedSetting = owner.ToLowerInvariant() switch
        {
            "hudmain" => "uHUDColor",
            "pipboy" => "uPipboyColor",
            _ => null,
        };
        if (packedSetting is null)
            throw new NotSupportedException($"Owned system color has no source palette declaration: {sourceIdentity}.");
        if (settings.Contains("Interface", packedSetting))
        {
            var packed = settings.Unsigned("Interface", packedSetting);
            return new((packed >> 24) / 255f, ((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f);
        }
        float Component(string channel)
        {
            var key = "iSystemColor" + owner + channel;
            var value = settings.Number("Interface", key);
            if (!float.IsFinite(value) || value is < 0 or > 255 || value != MathF.Truncate(value))
                throw new InvalidDataException($"Owned system color [{sourceIdentity}] {key} is not a byte component.");
            return value / 255;
        }
        return new(Component("Red"), Component("Green"), Component("Blue"));
    }
}
