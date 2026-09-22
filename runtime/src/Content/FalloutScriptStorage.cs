using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Content;

// The selected source graph is read-only. Script-owned configuration is a
// separate user/profile overlay, while source defaults continue to come from
// the same winning loose/BSA resource namespace as every other live asset.
internal sealed class FalloutScriptStorage
{
    internal FalloutScriptIniStore Ini { get; }
    internal FalloutAuxiliaryStore Auxiliary { get; }

    internal FalloutScriptStorage(FalloutScriptIniStore ini, FalloutAuxiliaryStore? auxiliary = null)
    {
        Ini = ini ?? throw new ArgumentNullException(nameof(ini));
        Auxiliary = auxiliary ?? new();
    }

    internal static FalloutScriptStorage Open(RuntimeLiveContentSource source, string overlayRoot)
    {
        return new(new FalloutScriptIniStore(source, overlayRoot));
    }
}

internal enum FalloutAuxiliaryValueKind
{
    Float = 1,
    Form = 2,
    String = 4,
}

internal readonly record struct FalloutAuxiliaryValue
{
    internal FalloutAuxiliaryValueKind Kind { get; }
    internal double Number { get; }
    internal FalloutFormKey? Form { get; }
    internal string? Text { get; }

    private FalloutAuxiliaryValue(FalloutAuxiliaryValueKind kind, double number, FalloutFormKey? form, string? text)
    {
        if (!double.IsFinite(number)) throw new InvalidDataException("Auxiliary value is non-finite.");
        if (text is not null && (text.Contains('\0') || text.Length > FalloutScriptValue.MaximumStringLength))
            throw new InvalidDataException("Auxiliary string exceeds its extent or contains a null.");
        Kind = kind; Number = number; Form = form; Text = text;
    }

    internal static FalloutAuxiliaryValue Float(double value) => new(FalloutAuxiliaryValueKind.Float, value, null, null);
    internal static FalloutAuxiliaryValue FormValue(FalloutFormKey value) => new(FalloutAuxiliaryValueKind.Form, 0, value, null);
    internal static FalloutAuxiliaryValue String(string value) => new(FalloutAuxiliaryValueKind.String, 0, null,
        value ?? throw new ArgumentNullException(nameof(value)));
}

internal sealed record FalloutAuxiliaryValueSnapshot(
    FalloutAuxiliaryValueKind Kind, double Number = 0, FalloutFormKey? Form = null, string? Text = null);
internal sealed record FalloutAuxiliaryVariableSnapshot(
    FalloutFormKey Owner, string Name, string? PrivatePlugin, bool Temporary,
    IReadOnlyList<FalloutAuxiliaryValueSnapshot> Values);
internal sealed record FalloutAuxiliaryStoreSnapshot(IReadOnlyList<FalloutAuxiliaryVariableSnapshot> Variables)
{
    internal void Validate()
    {
        if (Variables is null) throw new InvalidDataException("Saved auxiliary variables are absent.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in Variables)
        {
            if (variable is null || variable.Temporary || variable.Values is null || variable.Values.Count == 0 ||
                !ValidFormKey(variable.Owner))
                throw new InvalidDataException("Saved auxiliary variable state is invalid.");
            var key = $"{variable.Owner}|{variable.Name}|{variable.PrivatePlugin}";
            if (!seen.Add(key)) throw new InvalidDataException("Saved auxiliary variables are duplicated.");
            var parsed = FalloutAuxiliaryStore.ParseName(variable.Name);
            if (parsed.Temporary || string.Equals(parsed.PrivatePlugin, null, StringComparison.Ordinal) !=
                string.IsNullOrWhiteSpace(variable.PrivatePlugin))
                throw new InvalidDataException("Saved auxiliary variable visibility or lifetime disagrees with its name.");
            if (!string.IsNullOrWhiteSpace(variable.PrivatePlugin) && variable.PrivatePlugin!.Any(char.IsControl))
                throw new InvalidDataException("Saved auxiliary private ownership is invalid.");
            foreach (var value in variable.Values)
            {
                if (value is null || !Enum.IsDefined(value.Kind) ||
                    value.Kind == FalloutAuxiliaryValueKind.Float && (!double.IsFinite(value.Number) || value.Form is not null || value.Text is not null) ||
                    value.Kind == FalloutAuxiliaryValueKind.Form && (value.Form is null || value.Text is not null) ||
                    value.Kind == FalloutAuxiliaryValueKind.String && (value.Text is null || value.Form is not null || value.Text.Contains('\0')))
                    throw new InvalidDataException("Saved auxiliary element is invalid.");
                if (value.Form is { } form && !ValidFormKey(form)) throw new InvalidDataException("Saved auxiliary form is invalid.");
            }
        }

        static bool ValidFormKey(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) &&
            key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
    }
}

// JIP auxiliary variables are object-owned arrays. Their first prefix selects
// temporary/permanent and public/private storage:
//   *_ temporary-public, _ permanent-public, * temporary-private, none permanent-private.
internal sealed class FalloutAuxiliaryStore
{
    private readonly record struct Key(FalloutFormKey Owner, string Name, string? PrivatePlugin, bool Temporary);
    private readonly Dictionary<Key, List<FalloutAuxiliaryValue>> _values = [];

    internal double GetFloat(FalloutFormKey owner, string callerPlugin, string name, int index = 0) =>
        Get(owner, callerPlugin, name, index) is { Kind: FalloutAuxiliaryValueKind.Float } value ? value.Number : 0;

    internal FalloutFormKey? GetForm(FalloutFormKey owner, string callerPlugin, string name, int index = 0) =>
        Get(owner, callerPlugin, name, index) is { Kind: FalloutAuxiliaryValueKind.Form } value ? value.Form : null;

    internal string GetString(FalloutFormKey owner, string callerPlugin, string name, int index = 0) =>
        Get(owner, callerPlugin, name, index) is { Kind: FalloutAuxiliaryValueKind.String } value ? value.Text! : string.Empty;

    internal int GetType(FalloutFormKey owner, string callerPlugin, string name, int index = 0) =>
        Get(owner, callerPlugin, name, index) is { } value ? (int)value.Kind : 0;

    internal bool SetFloat(FalloutFormKey owner, string callerPlugin, string name, double value, int index = 0) =>
        Set(owner, callerPlugin, name, FalloutAuxiliaryValue.Float(value), index);

    internal bool SetForm(FalloutFormKey owner, string callerPlugin, string name, FalloutFormKey value, int index = 0) =>
        Set(owner, callerPlugin, name, FalloutAuxiliaryValue.FormValue(value), index);

    internal bool SetString(FalloutFormKey owner, string callerPlugin, string name, string value, int index = 0) =>
        Set(owner, callerPlugin, name, FalloutAuxiliaryValue.String(value), index);

    internal int Erase(FalloutFormKey owner, string callerPlugin, string name, int index = -1)
    {
        var parsed = ParseName(name);
        var key = MakeKey(owner, callerPlugin, parsed);
        if (!_values.TryGetValue(key, out var values)) return -1;
        if (index < 0)
        {
            _values.Remove(key);
            return -1;
        }
        if (index >= values.Count) return -1;
        values.RemoveAt(index);
        if (values.Count == 0) _values.Remove(key);
        return values.Count;
    }

    internal FalloutAuxiliaryStoreSnapshot CapturePermanent() => new(
        _values.Where(pair => !pair.Key.Temporary)
            .OrderBy(pair => pair.Key.Owner.OwnerPlugin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Owner.ObjectId)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.PrivatePlugin, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FalloutAuxiliaryVariableSnapshot(pair.Key.Owner, RawName(pair.Key), pair.Key.PrivatePlugin, false,
                pair.Value.Select(value => new FalloutAuxiliaryValueSnapshot(value.Kind, value.Number, value.Form, value.Text)).ToArray()))
            .ToArray());

    internal void RestorePermanent(FalloutAuxiliaryStoreSnapshot? snapshot)
    {
        foreach (var key in _values.Keys.Where(key => !key.Temporary).ToArray()) _values.Remove(key);
        if (snapshot is null) return;
        snapshot.Validate();
        foreach (var variable in snapshot.Variables)
        {
            var parsed = ParseName(variable.Name);
            var key = MakeKey(variable.Owner, variable.PrivatePlugin ?? string.Empty, parsed);
            _values.Add(key, variable.Values.Select(value => value.Kind switch
            {
                FalloutAuxiliaryValueKind.Float => FalloutAuxiliaryValue.Float(value.Number),
                FalloutAuxiliaryValueKind.Form => FalloutAuxiliaryValue.FormValue(value.Form!.Value),
                FalloutAuxiliaryValueKind.String => FalloutAuxiliaryValue.String(value.Text!),
                _ => throw new InvalidDataException("Saved auxiliary element type is invalid."),
            }).ToList());
        }
    }

    internal void ResetForNewGame() => _values.Clear();

    internal object State => new
    {
        temporary = _values.Count(pair => pair.Key.Temporary),
        permanent = _values.Count(pair => !pair.Key.Temporary),
        elements = _values.Sum(pair => pair.Value.Count),
    };

    private FalloutAuxiliaryValue? Get(FalloutFormKey owner, string callerPlugin, string name, int index)
    {
        var parsed = ParseName(name);
        var key = MakeKey(owner, callerPlugin, parsed);
        if (!_values.TryGetValue(key, out var values)) return null;
        if (index < 0) index = values.Count - 1;
        return index >= 0 && index < values.Count ? values[index] : null;
    }

    private bool Set(FalloutFormKey owner, string callerPlugin, string name, FalloutAuxiliaryValue value, int index)
    {
        var parsed = ParseName(name);
        var key = MakeKey(owner, callerPlugin, parsed);
        if (!_values.TryGetValue(key, out var values))
        {
            if (index > 0) return false;
            values = [];
            _values.Add(key, values);
        }
        if (index < 0 || index == values.Count) values.Add(value);
        else if (index < values.Count) values[index] = value;
        else return false;
        return true;
    }

    private static string RawName(Key key) => key.Temporary
        ? key.PrivatePlugin is null ? "*_" + key.Name : "*" + key.Name
        : key.PrivatePlugin is null ? "_" + key.Name : key.Name;

    private static Key MakeKey(FalloutFormKey owner, string callerPlugin, ParsedName parsed)
    {
        if (string.IsNullOrWhiteSpace(owner.OwnerPlugin) || owner.ObjectId == 0)
            throw new InvalidDataException("Auxiliary variable owner has no form identity.");
        if (parsed.PrivatePlugin is not null && string.IsNullOrWhiteSpace(callerPlugin))
            throw new InvalidDataException("Private auxiliary variable has no calling plugin.");
        return new(owner, parsed.Name, parsed.PrivatePlugin is null ? null : callerPlugin.ToLowerInvariant(), parsed.Temporary);
    }

    internal readonly record struct ParsedName(string Name, string? PrivatePlugin, bool Temporary);

    internal static ParsedName ParseName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 79 || raw.Any(char.IsControl))
            throw new InvalidDataException("Auxiliary variable name is empty, oversized or contains a control character.");
        var temporary = raw.StartsWith('*');
        var publicName = raw.StartsWith("*_", StringComparison.Ordinal);
        var permanentPublic = !temporary && raw.StartsWith('_');
        var offset = publicName ? 2 : temporary || permanentPublic ? 1 : 0;
        var name = raw[offset..];
        if (name.Length == 0) throw new InvalidDataException("Auxiliary variable name has no key after its prefix.");
        return new(name.ToLowerInvariant(), publicName || permanentPublic ? null : "private", temporary);
    }
}

internal sealed class FalloutScriptIniStore
{
    private readonly Func<string, byte[]?> _sourceReader;
    private readonly string _overlayRoot;

    internal FalloutScriptIniStore(RuntimeLiveContentSource source, string overlayRoot)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sourceReader = logicalPath => source.TryRead(logicalPath, null, out var bytes, out _) ? bytes : null;
        if (string.IsNullOrWhiteSpace(overlayRoot))
            throw new ArgumentException("Script configuration needs a user/profile overlay root.", nameof(overlayRoot));
        _overlayRoot = Path.GetFullPath(overlayRoot);
    }

    // The deterministic contract probe uses this source-reader seam; the live
    // constructor above remains the only production entry point.
    internal FalloutScriptIniStore(Func<string, byte[]?> sourceReader, string overlayRoot)
    {
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        if (string.IsNullOrWhiteSpace(overlayRoot))
            throw new ArgumentException("Script configuration needs a user/profile overlay root.", nameof(overlayRoot));
        _overlayRoot = Path.GetFullPath(overlayRoot);
    }

    internal double GetFloat(string keyString, string? fileName, string callingPlugin)
    {
        var value = GetString(keyString, fileName, callingPlugin);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number) ? number : 0;
    }

    internal string GetString(string keyString, string? fileName, string callingPlugin)
    {
        var key = IniKey.Parse(keyString);
        var relative = FileName(fileName, callingPlugin);
        var overlay = ReadDocument(OverlayPath(relative));
        if (overlay.TryGetValue(key, out var value)) return value;
        return ReadSource(relative).TryGetValue(key, out value) ? value : string.Empty;
    }

    internal void SetFloat(string keyString, double value, string? fileName, string callingPlugin)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("INI float value is non-finite.");
        SetString(keyString, value.ToString("R", CultureInfo.InvariantCulture), fileName, callingPlugin);
    }

    internal void SetString(string keyString, string value, string? fileName, string callingPlugin)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = IniKey.Parse(keyString);
        var relative = FileName(fileName, callingPlugin);
        var path = OverlayPath(relative);
        var lines = File.Exists(path)
            ? File.ReadAllLines(path, Encoding.UTF8).ToList()
            : SourceLines(relative).ToList();
        var section = string.Empty;
        var replacement = -1;
        for (var index = 0; index < lines.Count; ++index)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('\uFEFF')) line = line[1..].TrimStart();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            if (!IniKey.Matches(line, section, key)) continue;
            replacement = index;
        }
        var lineValue = $"{key.Key}={value}";
        if (replacement >= 0)
            lines[replacement] = lineValue;
        else
        {
            var sectionIndex = -1;
            section = string.Empty;
            for (var index = 0; index < lines.Count; ++index)
            {
                var line = lines[index].Trim();
                if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
                section = line[1..^1].Trim();
                if (string.Equals(section, key.Section, StringComparison.OrdinalIgnoreCase))
                    sectionIndex = index;
            }
            if (sectionIndex < 0)
            {
                if (lines.Count != 0 && lines[^1].Length != 0) lines.Add(string.Empty);
                lines.Add($"[{key.Section}]");
                lines.Add(lineValue);
            }
            else
            {
                var insert = sectionIndex + 1;
                while (insert < lines.Count &&
                    !(lines[insert].TrimStart().StartsWith('[') && lines[insert].TrimEnd().EndsWith(']')))
                    ++insert;
                lines.Insert(insert, lineValue);
            }
        }
        WriteAtomic(path, lines);
    }

    private Dictionary<IniKey, string> ReadSource(string relative)
    {
        var bytes = _sourceReader(SourcePath(relative));
        if (bytes is null) return [];
        return Parse(Encoding.UTF8.GetString(bytes));
    }

    private IReadOnlyList<string> SourceLines(string relative)
    {
        var bytes = _sourceReader(SourcePath(relative));
        if (bytes is null) return [];
        return Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n').ToArray();
    }

    private Dictionary<IniKey, string> ReadDocument(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : [];

    private string SourcePath(string relative) => "Config/" + relative.Replace(Path.DirectorySeparatorChar, '/');

    private string OverlayPath(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_overlayRoot, relative));
        var root = Path.TrimEndingDirectorySeparator(_overlayRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Script configuration path escaped its user/profile overlay.");
        return path;
    }

    private static string FileName(string? fileName, string callingPlugin)
    {
        var selected = string.IsNullOrWhiteSpace(fileName)
            ? Path.GetFileNameWithoutExtension(callingPlugin) + ".ini"
            : fileName;
        if (string.IsNullOrWhiteSpace(selected) || Path.IsPathRooted(selected) || selected.Contains(':'))
            throw new InvalidDataException("INI filename must be a non-empty relative path.");
        selected = selected.Replace('\\', '/');
        var parts = selected.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException("INI filename contains an invalid relative path segment.");
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static Dictionary<IniKey, string> Parse(string text)
    {
        var values = new Dictionary<IniKey, string>(IniKeyComparer.Instance);
        var section = string.Empty;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('\uFEFF')) line = line[1..].TrimStart();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0 || section.Length == 0) continue;
            var key = line[..separator].Trim();
            if (key.Length == 0) continue;
            values[new(section, key)] = StripValueComment(line[(separator + 1)..].Trim());
        }
        return values;
    }

    private static string StripValueComment(string value)
    {
        var quoted = false;
        for (var index = 0; index < value.Length; ++index)
        {
            if (value[index] == '"') quoted = !quoted;
            if (!quoted && value[index] == ';') return value[..index].TrimEnd();
        }
        return value;
    }

    private static void WriteAtomic(string path, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, string.Join(Environment.NewLine, lines) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private readonly record struct IniKey(string Section, string Key)
    {
        internal static IniKey Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("INI key identity is empty.");
            var separators = new[] { ':', '\\', '/' };
            var separator = value.IndexOfAny(separators);
            if (separator <= 0 || separator == value.Length - 1 ||
                value[(separator + 1)..].IndexOfAny(separators) >= 0)
                throw new InvalidDataException("INI key identity must be Section:Key.");
            var section = value[..separator].Trim();
            var key = value[(separator + 1)..].Trim();
            if (section.Length == 0 || key.Length == 0) throw new InvalidDataException("INI key identity is incomplete.");
            return new(section, key);
        }

        internal static bool Matches(string line, string section, IniKey key)
        {
            if (!string.Equals(section, key.Section, StringComparison.OrdinalIgnoreCase)) return false;
            var separator = line.IndexOf('=');
            return separator > 0 && string.Equals(line[..separator].Trim(), key.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class IniKeyComparer : IEqualityComparer<IniKey>
    {
        internal static IniKeyComparer Instance { get; } = new();

        public bool Equals(IniKey left, IniKey right) =>
            string.Equals(left.Section, right.Section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Key, right.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(IniKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Section),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Key));
    }
}
