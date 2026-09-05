using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorPackageCommand(int Line, string ActorEditorId, bool Reset);

internal static partial class FalloutActorPackageCommands
{
    internal static IReadOnlyList<FalloutActorPackageCommand> Read(string source)
    {
        var result = new List<FalloutActorPackageCommand>();
        var depth = 0;
        var index = 0;
        foreach (var line in FalloutDialogueTopic.CodeLines(source))
        {
            if (Regex.IsMatch(line, @"^if\b", RegexOptions.IgnoreCase)) depth++;
            if (Regex.IsMatch(line, @"^endif\b", RegexOptions.IgnoreCase)) depth--;
            if (Regex.IsMatch(line, @"\b(resetai|evp|evaluatepackage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var match = Command().Match(line);
                if (!match.Success || depth != 0)
                    throw new NotSupportedException($"Actor package command needs a bound expression/condition owner: {line}");
                result.Add(new(index, match.Groups["actor"].Value,
                    match.Groups["operation"].Value.Equals("resetai", StringComparison.OrdinalIgnoreCase)));
            }
            index++;
        }
        return result;
    }

    [GeneratedRegex(@"^(?<actor>[A-Za-z0-9_]+)\s*\.\s*(?<operation>resetai|evp|evaluatepackage)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Command();
}
