using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestObjectFlagSnapshot(FalloutFormKey Form, string RecordType,
    uint SourceFlags, string SourceSha256, bool QuestObject);

// The source header remains immutable. Script changes belong to the loaded
// game's shared form state and every inventory instance reads the same flag.
internal sealed class FalloutQuestObjectFlags(FalloutPluginStack records)
{
    private const uint QuestObjectMask = 0x400;
    private readonly Dictionary<FalloutFormKey, FalloutQuestObjectFlagSnapshot> _source = [];
    private readonly Dictionary<FalloutFormKey, bool> _changes = [];
    internal long Revision { get; private set; }

    internal bool IsQuestObject(FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        return _changes.TryGetValue(record.FormKey, out var value) ? value : (record.Flags & QuestObjectMask) != 0;
    }

    internal void Set(FalloutFormKey form, bool value)
    {
        var record = records.GetEffective(form);
        form = record.FormKey;
        RequireMutableType(record.Signature);
        if (IsQuestObject(form) == value) return;
        var source = Source(record);
        if (((source.SourceFlags & QuestObjectMask) != 0) == value) _changes.Remove(form);
        else _changes[form] = value;
        Revision++;
    }

    internal IReadOnlyList<FalloutQuestObjectFlagSnapshot> Capture() => _changes
        .OrderBy(pair => records.RuntimeFormId(pair.Key))
        .Select(pair => _source[pair.Key] with { QuestObject = pair.Value }).ToArray();

    internal static void ValidateSnapshot(IReadOnlyList<FalloutQuestObjectFlagSnapshot>? state)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in state ?? [])
        {
            if (row is null || row.Form.ObjectId == 0 || string.IsNullOrWhiteSpace(row.Form.OwnerPlugin) ||
                !forms.Add(row.Form.ToString()) || string.IsNullOrWhiteSpace(row.RecordType) || row.SourceSha256 is not { Length: 64 } ||
                row.SourceSha256.Any(character => !Uri.IsHexDigit(character)) ||
                ((row.SourceFlags & QuestObjectMask) != 0) == row.QuestObject)
                throw new InvalidDataException("Saved quest-object form state is invalid, redundant or duplicated.");
            RequireMutableType(row.RecordType);
        }
    }

    internal void Restore(IReadOnlyList<FalloutQuestObjectFlagSnapshot>? state)
    {
        ValidateSnapshot(state);
        var prepared = new List<FalloutQuestObjectFlagSnapshot>();
        foreach (var row in state ?? [])
        {
            var record = records.GetEffective(row.Form);
            var source = Source(record);
            if (source.RecordType != row.RecordType || source.SourceFlags != row.SourceFlags ||
                !source.SourceSha256.Equals(row.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved quest-object flags differ from the winning source record.");
            prepared.Add(source with { QuestObject = row.QuestObject });
        }
        // All source checks precede the first effective flag change.
        _changes.Clear();
        foreach (var row in prepared) _changes.Add(row.Form, row.QuestObject);
        Revision++;
    }

    private FalloutQuestObjectFlagSnapshot Source(FalloutPluginRecord record)
    {
        if (_source.TryGetValue(record.FormKey, out var value)) return value;
        RequireMutableType(record.Signature);
        value = new(record.FormKey, record.Signature, record.Flags,
            Convert.ToHexString(SHA256.HashData(record.ReadData())), (record.Flags & QuestObjectMask) != 0);
        _source.Add(record.FormKey, value);
        return value;
    }

    private static void RequireMutableType(string type)
    {
        if (type is "TACT" or "NPC_" or "CREA")
            throw new NotSupportedException("Actor quest-object changes require their process-priority and cleanup owners.");
        if (type is not ("ARMO" or "BOOK" or "MISC" or "WEAP" or "AMMO" or "KEYM" or "ALCH" or "IMOD"))
            throw new NotSupportedException($"Quest-object mutation for {type} has no source type contract.");
    }
}
