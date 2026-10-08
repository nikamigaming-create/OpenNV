using System.Numerics;

namespace OpenNV.Runtime.Content;

// XML system-color identities select owned INI components. A packed override
// is meaningful only when an installation/profile layer actually supplies it:
// the executable's zero-initialized packed storage is not a palette color.
internal static class FalloutUiSystemColors
{
    internal static Vector3 Read(FalloutInstallationSettings settings, string sourceIdentity)
    {
        if (!sourceIdentity.StartsWith("entity_", StringComparison.OrdinalIgnoreCase) || sourceIdentity.Length == 7)
            throw new NotSupportedException($"Owned system color has no source identity: {sourceIdentity}.");
        var owner = sourceIdentity[7..];
        if (owner.Equals("NoSystemColor", StringComparison.OrdinalIgnoreCase)) return Vector3.One;
        var packedSetting = owner.ToLowerInvariant() switch
        {
            "hudmain" or "mainmenu" => "uHUDColor",
            "pipboy" => "uPipboyColor",
            _ => null,
        };
        if (packedSetting is not null && settings.Contains("Interface", packedSetting))
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
