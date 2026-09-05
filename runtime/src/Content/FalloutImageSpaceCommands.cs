using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImageSpaceCommand(int Line, bool Apply, string EditorId);

internal static partial class FalloutImageSpaceCommands
{
    internal static IReadOnlyList<FalloutImageSpaceCommand> Read(string script)
    {
        var commands = new List<FalloutImageSpaceCommand>();
        var depth = 0;
        var index = 0;
        foreach (var line in FalloutDialogueTopic.CodeLines(script))
        {
            if (Regex.IsMatch(line, @"^if\b", RegexOptions.IgnoreCase)) depth++;
            if (Regex.IsMatch(line, @"^endif\b", RegexOptions.IgnoreCase)) depth--;
            if (Regex.IsMatch(line, @"\b(ApplyImageSpaceModifier|RemoveImageSpaceModifier|imod|rimod)\b", RegexOptions.IgnoreCase))
            {
                var match = Command().Match(line);
                if (!match.Success || depth != 0)
                    throw new NotSupportedException($"Image-space command needs a bound expression/condition owner: {line}");
                var operation = match.Groups["operation"].Value;
                commands.Add(new(index, operation.Equals("imod", StringComparison.OrdinalIgnoreCase) ||
                    operation.Equals("ApplyImageSpaceModifier", StringComparison.OrdinalIgnoreCase), match.Groups["modifier"].Value));
            }
            index++;
        }
        return commands;
    }

    // The trailing source '*' is absent from the compiled one-reference call;
    // it is not a crossfade or an extra numeric argument. Owned SCDA/SCRO audits
    // verify the same opcode/argument shape with and without this annotation.
    [GeneratedRegex(@"^(?<operation>ApplyImageSpaceModifier|RemoveImageSpaceModifier|imod|rimod)\s+(?<modifier>[A-Za-z0-9_]+)\s*\*?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Command();
}
