using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Original script message lists, read on demand. Stable handles survive saved INT variables.</summary>
internal sealed class ClassicNativeScriptMessages(Func<string, byte[]> read) : IClassicIntMessageSource
{
    private IReadOnlyList<string>? _scripts;
    private readonly Dictionary<int, Dictionary<int, string>> _lists = [];

    public int Handle(int list, int id)
    {
        // The negative handle space is private C# state, not a retail address.
        // Both fields have an explicit extent; never alias unknown messages.
        if (list is <= 0 or > 32767 || id is < 0 or > 65535)
            throw new NotSupportedException($"Script message list/id is outside the supported source domain: {list}:{id}.");
        _ = Text(list, id);
        return unchecked((int)(0x80000000u | (uint)list << 16 | (uint)id));
    }

    public ClassicIntMessageEffect Resolve(int handle, int? objectHandle, int? color)
    {
        if (handle >= 0) throw new InvalidDataException("Script message handle has no source identity.");
        var list = (handle >> 16) & 0x7fff; var id = handle & 0xffff;
        if (list == 0) throw new InvalidDataException("Script message handle has no script list.");
        return new(list, id, handle, objectHandle, color) { Text = Text(list, id) };
    }

    private string Text(int list, int id)
    {
        if (!_lists.TryGetValue(list, out var messages))
        {
            _scripts ??= Fallout1NativeLists.Read(read("scripts/scripts.lst"));
            if (list <= 0 || list > _scripts.Count) throw new InvalidDataException($"Script message list {list} exceeds scripts.lst.");
            var name = Path.ChangeExtension(_scripts[list - 1], ".msg");
            var path = ClassicMapCatalog.Canonical("text/english/dialog/" + name);
            messages = ReadMessages(read(path)); _lists.Add(list, messages);
        }
        return messages.TryGetValue(id, out var text) ? text : throw new InvalidDataException($"Source script message is absent: {list}:{id}.");
    }

    internal static Dictionary<int, string> ReadMessages(byte[] bytes)
    {
        var messages = new Dictionary<int, string>();
        foreach (Match match in Regex.Matches(Encoding.Latin1.GetString(bytes), @"^\s*\{(\d+)\}\s*\{[^}]*\}\s*\{([^}]*)\}", RegexOptions.Multiline))
        {
            var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            // Duplicate source IDs replace earlier entries in the loaded list.
            messages[id] = match.Groups[2].Value.Replace("\r", "", StringComparison.Ordinal);
        }
        if (messages.Count == 0) throw new InvalidDataException("Source script message list has no readable entries.");
        return messages;
    }
}
