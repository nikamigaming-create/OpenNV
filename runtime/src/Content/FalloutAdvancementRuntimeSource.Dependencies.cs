using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutAdvancementRuntimeSource
{
    private sealed record DependencyRead(FalloutSkillPointRate? Rate, string Images, string Configuration, string Contract,
        IReadOnlyList<string> UnreviewedImages);

    private DependencyRead ReadDependencies(FalloutContentLayers layers, string engine)
    {
        var images = new StringBuilder(); var configuration = new StringBuilder(); var declarations = new StringBuilder();
        FalloutSkillPointRate? replacement = null;
        var unreviewed = new List<string>();
        // NVSE discovers loose DLLs independently of display/plugin names.
        // Resolve their actual selected winners, then choose reviewed consumers
        // by image identity. Renaming an admitted module cannot change its rate.
        var rateModules = 0; var optionModules = 0;
        foreach (var logical in layers.ResourcePathsUnder("NVSE/Plugins")
            .Where(path => path.LastIndexOf('\\') == "NVSE\\Plugins".Length &&
                Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var path = layers.ResolveFile(logical) ?? throw new FileNotFoundException("Selected advancement module disappeared.", logical);
            var dependency = LeaseImage(path, dll: true);
            images.Append(logical.ToLowerInvariant()).Append('\0').Append(dependency.Sha256).Append('\n');
            switch (dependency.Sha256)
            {
                case "874fcf6c101f005fbbedee6dbe0d4c40a0d537c53f06925e4cf09ee66e4e94ea":
                    if (++rateModules != 1 || engine != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
                        throw new NotSupportedException("Selected rate dependency has an ambiguous or unreviewed engine installation contract.");
                    // The reviewed Load consumer installs this rate independently
                    // of GMST values and contains no option gate for this hook.
                    replacement = new(FalloutSkillPointOperand.GameSetting("iLevelUpSkillPointsBase"),
                        FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Ceiling);
                    declarations.Append("unconditional-load-rate-replacement;base-setting+ceil(integer-intelligence/2)");
                    ObserveConfiguration(layers, "NVSE/Plugins/TTW.ini", configuration);
                    break;
                case "c19007b47f39df2ce07cfc1add1a27090302cf35e1673ba35bee61cb5800db95":
                    if (++optionModules != 1 || engine != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57")
                        throw new NotSupportedException("Selected advancement options have an ambiguous or unreviewed engine contract.");
                    if (layers.ResolveFile("NVSE/Plugins/nvse_stewie_tweaks_custom.ini") is not null)
                        throw new NotSupportedException("Advancement custom option layering has no admitted dependency consumer.");
                    var settings = ObserveConfiguration(layers, "NVSE/Plugins/nvse_stewie_tweaks.ini", configuration);
                    // Exact-image zero-default Boolean registration. Alternate
                    // formulas/reallocation require their independent consumers.
                    if (ReadOption(settings, "Tweaks", "bModifySkillPointsEarned", defaultValue: false) ||
                        ReadOption(settings, "Tweaks", "bReallocateSkillPointsOnLevelup", defaultValue: false))
                        throw new NotSupportedException("Selected advancement modifier/reallocation option has no admitted runtime consumer.");
                    declarations.Append("\0zero-default-modification/reallocation-disabled");
                    break;
                default:
                    // Unknown native interfaces remain unknown; a byte hash does
                    // not prove them unable to affect advancement. Their genuine
                    // execution is a separate required activity/compatibility owner.
                    unreviewed.Add(dependency.Sha256);
                    declarations.Append("\0native-module-effects-unowned:").Append(dependency.Sha256);
                    break;
            }
        }
        return new(replacement, images.ToString(), configuration.ToString(), declarations.ToString(), unreviewed.AsReadOnly());
    }

    private byte[]? ObserveConfiguration(FalloutContentLayers layers, string logical, StringBuilder receipt)
    {
        var path = layers.ResolveFile(logical);
        if (path is null) { receipt.Append(logical.ToLowerInvariant()).Append("\0absent\n"); return null; }
        var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (lease.Length > 4 * 1024 * 1024) throw new InvalidDataException("Advancement configuration exceeds its bounded source extent.");
            var bytes = new byte[checked((int)lease.Length)]; lease.ReadExactly(bytes); _leases.Add(lease);
            receipt.Append(logical.ToLowerInvariant()).Append('\0').Append(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()).Append('\n');
            return bytes;
        }
        catch { lease.Dispose(); throw; }
    }

    private static bool ReadOption(byte[]? bytes, string requiredSection, string key, bool defaultValue)
    {
        if (bytes is null) return defaultValue;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error)
        {
            throw new NotSupportedException("Advancement option source needs an admitted character encoding.", error);
        }
        if (text.IndexOf('\0') >= 0) throw new InvalidDataException("Advancement option source contains an embedded terminator.");
        var section = ""; string? value = null;
        foreach (var original in text.TrimStart('\ufeff').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = original.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                if (end < 0 || line[(end + 1)..].TrimStart() is { Length: > 0 } suffix && suffix[0] is not (';' or '#'))
                    throw new InvalidDataException("Advancement option source has a malformed section.");
                section = line[1..end].Trim(); continue;
            }
            var separator = line.IndexOf('=');
            if (separator < 0 || !section.Equals(requiredSection, StringComparison.OrdinalIgnoreCase) ||
                !line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (value is not null) throw new NotSupportedException("Duplicate advancement option needs its original layering consumer.");
            value = line[(separator + 1)..].Trim();
            var comment = value.IndexOfAny([';', '#']);
            if (comment >= 0) value = value[..comment].TrimEnd();
        }
        if (value is null) return defaultValue;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException("Advancement Boolean option is not an admitted integer.");
        return number != 0;
    }
}
