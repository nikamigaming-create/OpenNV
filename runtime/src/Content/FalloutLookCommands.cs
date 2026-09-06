using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLookCommand(int Line, string? Actor, string? Target);

internal static partial class FalloutLookCommands
{
    internal static IReadOnlyList<FalloutLookCommand> Read(string source)
    {
        var result = new List<FalloutLookCommand>();
        var depth = 0;
        var lines = FalloutDialogueTopic.CodeLines(source).ToArray();
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            if (Regex.IsMatch(line, @"^if\b", RegexOptions.IgnoreCase)) depth++;
            if (Regex.IsMatch(line, @"^endif\b", RegexOptions.IgnoreCase)) depth--;
            if (!Regex.IsMatch(line, @"\b(?:look|stoplook)\b", RegexOptions.IgnoreCase)) continue;
            var match = Command().Match(line);
            if (!match.Success || depth != 0)
                throw new NotSupportedException($"Look command needs its source expression/control-flow owner: {line}");
            var stop = match.Groups["operation"].Value.Equals("stoplook", StringComparison.OrdinalIgnoreCase);
            var target = match.Groups["target"].Success ? match.Groups["target"].Value : null;
            if (!stop && target is null || stop && match.Groups["body"].Success)
                throw new InvalidDataException("Look/StopLook argument count is invalid.");
            if (match.Groups["body"].Success &&
                (!int.TryParse(match.Groups["body"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var body) || body != 0))
                throw new NotSupportedException("Nonzero Look mode requires the whole-body turn owner.");
            // StopLook declares no parameters. A trailing reference spelling
            // in source is ignored by that command's compiled argument table.
            result.Add(new(index, match.Groups["actor"].Success ? match.Groups["actor"].Value : null, stop ? null : target));
        }
        if (result.Count != 0 && lines.Any(line => Regex.IsMatch(line, @"^(?:while|loop|goto|return)\b", RegexOptions.IgnoreCase)))
            throw new NotSupportedException("Look result requires its loop/return control-flow owner.");
        return result;
    }

    [GeneratedRegex(@"^(?:(?<actor>[A-Za-z0-9_]+)\s*\.\s*)?(?<operation>look|stoplook)(?:\s+(?<target>[A-Za-z0-9_]+))?(?:\s+(?<body>[+-]?\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Command();
}
