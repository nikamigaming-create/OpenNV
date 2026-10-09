using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMovieCommand(
    string FileName,
    bool Interruptible,
    bool MuteWorldAudio,
    bool PauseMusic,
    bool Letterboxed)
{
    internal static FalloutMovieCommand FromArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 1 or > 5 || arguments[0].Length < 3 ||
            arguments[0][0] != '"' || arguments[0][^1] != '"')
            throw new InvalidDataException("PlayBink requires one literal filename and up to four flags.");
        var file = arguments[0][1..^1];
        if (file.IndexOfAny(['"', '\0', '\r', '\n']) >= 0)
            throw new InvalidDataException("PlayBink filename contains an unowned literal boundary.");
        var flags = new[] { false, true, true, true };
        for (var index = 1; index < arguments.Count; ++index)
        {
            if (!int.TryParse(arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag))
                throw new InvalidDataException("PlayBink flag is outside exact Int32 storage.");
            flags[index - 1] = flag != 0;
        }
        return new(file, flags[0], flags[1], flags[2], flags[3]);
    }

    // PlayBink's four optional flags are independent. MuteWorldAudio does not
    // mute the movie's own soundtrack. Source: GECK PlayBink command contract.
    internal static IReadOnlyList<FalloutMovieCommand> FromScript(string source)
    {
        var result = new List<FalloutMovieCommand>();
        foreach (var line in source.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (!Regex.IsMatch(line, @"^\s*PlayBink\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;
            var match = Regex.Match(line,
                "^\\s*PlayBink\\s+\"(?<file>[^\"]+)\"(?<flags>(?:\\s+[-+]?\\d+){0,4})\\s*(?:;.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                throw new NotSupportedException("PlayBink requires a quoted filename and up to four integer flags.");
            var flags = new[] { false, true, true, true };
            var values = match.Groups["flags"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < values.Length; ++index)
                flags[index] = int.Parse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture) != 0;
            result.Add(new FalloutMovieCommand(match.Groups["file"].Value,
                flags[0], flags[1], flags[2], flags[3]));
        }
        return result;
    }
}
