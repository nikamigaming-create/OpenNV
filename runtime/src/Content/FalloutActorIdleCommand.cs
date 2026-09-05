using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorIdleCommand(string ActorEditorId, string IdleEditorId);

internal static partial class FalloutActorIdleCommands
{
    internal static IReadOnlyList<FalloutActorIdleCommand> Read(string script)
    {
        var result = new List<FalloutActorIdleCommand>();
        foreach (var line in FalloutDialogueTopic.CodeLines(script))
        {
            var match = Pattern().Match(line);
            if (match.Success)
                result.Add(new(match.Groups["actor"].Value, match.Groups["idle"].Value));
            else if (Regex.IsMatch(line, @"\bplayidle\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new NotSupportedException($"Unsupported PlayIdle command: {line}");
        }
        return result;
    }

    [GeneratedRegex(@"^(?<actor>[A-Za-z0-9_]+)\s*\.\s*playidle\s+(?<idle>[A-Za-z0-9_]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
