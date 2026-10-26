using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

// These entities name the engine's control slots, not their default keys or
// localized action captions. Render from the selected profile's live binding.
internal static class FalloutMessageControlTokens
{
    private static readonly IReadOnlyDictionary<string, uint> Controls = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["sUActnForward"] = 0,
        ["sUActnBack"] = 1,
        ["sUActnSldleft"] = 2,
        ["sUActnSldright"] = 3,
        ["sUActnUse"] = 4,
        ["sUActnActivate"] = 5,
        ["sUActnBlock"] = 6,
        ["sUActnRdyitem"] = 7,
        ["sUActnCrouch"] = 8,
        ["sUActnRun"] = 9,
        ["sUActnToggleRun"] = 10,
        ["sUActnAutomove"] = 11,
        ["sUActnJump"] = 12,
        ["sUActnTogglepov"] = 13,
        ["sUActnMenumode"] = 14,
        ["sUActnVats"] = 16,
        ["sUActnGrab"] = 27,
    };
    private static readonly Regex Tokens = new(@"&-?sUActn[A-Za-z0-9_]*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Resolve(string text, Func<uint, string> bindingLabel)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(bindingLabel);
        return Tokens.Replace(text, match =>
        {
            if (!match.Value.EndsWith(';')) throw new InvalidDataException($"HUD control entity is unterminated: {match.Value}.");
            var name = match.Value[(match.Value[1] == '-' ? 2 : 1)..^1];
            if (!Controls.TryGetValue(name, out var control))
                throw new NotSupportedException($"HUD control entity has no engine slot owner: {name}.");
            var label = bindingLabel(control);
            if (string.IsNullOrWhiteSpace(label)) throw new NotSupportedException($"HUD control {name} has no bound input label.");
            // Keep the source's punctuation and prose. Retail's distinction
            // between plain/minus entities and controller glyphs is unverified.
            return "[" + label + "]";
        });
    }
}
