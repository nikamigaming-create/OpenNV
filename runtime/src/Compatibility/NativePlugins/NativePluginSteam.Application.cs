using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginSteamApplication(string ManifestPath, string ManifestSha256,
    uint AppId, string InstallDirectory);

// Selection reads the real installed AppState relation. Neither display names,
// a recalled app number, an EXE filename nor an injected environment variable
// can select the account/application whose statistics may be changed.
internal static class NativePluginSteamApplications
{
    internal static (NativePluginSteamApplication? Application, FileStream? Lease) Read(string runtimeDirectory)
    {
        var installed = new DirectoryInfo(Path.GetFullPath(runtimeDirectory));
        if (installed.Parent?.Parent is not { } depot || !installed.Parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase))
            return (null, null); // Missing producer, not a platform/application absence fact.
        FileStream? retained = null; NativePluginSteamApplication? selected = null;
        try
        {
            foreach (var path in Directory.EnumerateFiles(depot.FullName, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length is < 1 or > 2 * 1024 * 1024) throw new InvalidDataException("Installed platform declaration exceeds its source extent.");
                var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
                var root = ReadKeyValues(new UTF8Encoding(false, true).GetString(bytes));
                if (!root.TryGetValue("AppState", out var value) || value is not Dictionary<string, object> state) continue;
                if (!state.TryGetValue("installdir", out var directory) || directory is not string relative || string.IsNullOrWhiteSpace(relative)) continue;
                if (Path.IsPathFullyQualified(relative) || relative.Split(['/', '\\']).Any(segment => segment is "." or ".."))
                    throw new NotSupportedException("Installed application path has no bounded declared common-root relation.");
                var actual = Path.GetFullPath(Path.Combine(installed.Parent.FullName, relative));
                if (!actual.Equals(installed.FullName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!state.TryGetValue("appid", out var number) || number is not string text ||
                    !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var app) || app == 0)
                    throw new InvalidDataException("Selected installed application omitted its genuine positive source AppState ID.");
                if (selected is not null) throw new NotSupportedException("More than one installed application claims the selected runtime directory.");
                selected = new(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), app, actual);
                retained = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!Convert.ToHexString(SHA256.HashData(retained)).Equals(selected.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Selected installed application changed before its source lease.");
                retained.Position = 0;
            }
            var result = (selected, retained); retained = null; return result;
        }
        finally { retained?.Dispose(); }
    }
    private static Dictionary<string, object> ReadKeyValues(string source)
    {
        var position = 0; var tokens = 0;
        var result = Block(false, 0); Skip();
        if (position != source.Length) throw new InvalidDataException("Installed declaration retained unaccounted trailing syntax.");
        return result;
        Dictionary<string, object> Block(bool nested, int depth)
        {
            if (depth > 16) throw new NotSupportedException("Installed declaration nesting exceeds its admitted layout.");
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (; ; )
            {
                Skip();
                if (position == source.Length)
                { if (nested) throw new InvalidDataException("Installed declaration object is unfinished."); return values; }
                if (source[position] == '}')
                { if (!nested) throw new InvalidDataException("Unexpected installed declaration object end."); position++; return values; }
                var key = Text(); Skip(); object value;
                if (position < source.Length && source[position] == '{') { position++; value = Block(true, depth + 1); }
                else value = Text();
                if (!values.TryAdd(key, value)) throw new NotSupportedException("Duplicate installed declaration key has no selected-winner consumer.");
            }
        }
        string Text()
        {
            Skip(); if (++tokens > 65536 || position >= source.Length || source[position++] != '"')
                throw new InvalidDataException("Installed declaration needs a complete quoted source key/value.");
            var value = new StringBuilder();
            while (position < source.Length)
            {
                var item = source[position++]; if (item == '"') return value.ToString();
                if (item == '\\')
                {
                    if (position >= source.Length) throw new InvalidDataException("Installed declaration escape is unfinished.");
                    item = source[position++] switch
                    {
                        '\\' => '\\',
                        '"' => '"',
                        'n' => '\n',
                        't' => '\t',
                        _ => throw new NotSupportedException("Installed declaration escape has no source grammar owner.")
                    };
                }
                if (item == '\0' || value.Length >= 16384) throw new InvalidDataException("Installed declaration string has an invalid source extent.");
                value.Append(item);
            }
            throw new InvalidDataException("Installed declaration string has no closing source quote.");
        }
        void Skip()
        {
            if (position == 0 && source.StartsWith('\ufeff')) position++;
            for (; ; )
            {
                while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
                if (position + 1 >= source.Length || source[position] != '/' || source[position + 1] != '/') return;
                position += 2; while (position < source.Length && source[position] is not ('\n' or '\r')) position++;
            }
        }
    }
}
