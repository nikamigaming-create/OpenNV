using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private readonly Dictionary<string, List<FalloutPluginRecord>> _modelOwners = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _recordHashes = new(StringComparer.OrdinalIgnoreCase);
        private void ReadRecords()
        {
            progress?.Invoke("Enumerating every registered winning record, authored placement and model declaration.");
            var keys = records.Plugins.SelectMany(plugin => plugin.Plugin.Records).Where(record => record.Signature != "TES4")
                .Select(record => record.FormKey).Distinct(FalloutFormKeyComparer.Instance).OrderBy(records.RuntimeFormId).ToArray();
            if (keys.Length != records.WinnerRecordCount) throw new InvalidDataException("Winning-record denominator differs from registered identities.");
            using var rows = Writer("records");
            foreach (var key in keys)
            {
                if (!records.TryGetWinner(key, out var record)) throw new InvalidDataException("Registered record has no winner: " + key);
                var failures = new List<CatalogFailure>();
                var declarations = new List<CatalogDeclaration>();
                string hash = "", editor = "";
                try
                {
                    hash = Hash(record.ReadData());
                    _recordHashes.Add(key.ToString(), hash);
                    var fields = record.ReadSubrecords().ToArray();
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var field in fields)
                    {
                        var occurrence = counts.GetValueOrDefault(field.Signature); counts[field.Signature] = occurrence + 1;
                        if (field.Signature == "EDID") editor = Encoding.Latin1.GetString(field.Data.Span).TrimEnd('\0');
                        if (field.Signature is not ("MODL" or "MOD2" or "MOD3" or "MOD4" or "MOD5" or "MNAM" or "FNAM" or "NIFZ" or "KFFZ")) continue;
                        if (field.Data.IsEmpty || field.Data.Span[^1] != 0) continue; // Non-string use of a context-dependent tag.
                        foreach (var value in Encoding.Latin1.GetString(field.Data.Span).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!value.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) && !value.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) continue;
                            declarations.Add(new(field.Signature, occurrence, value, $"source-declaration:{record.Signature}/{field.Signature}"));
                            if (!record.IsDeleted && field.Signature is not ("NIFZ" or "KFFZ"))
                            {
                                var path = Canonical(value);
                                if (!path.StartsWith("meshes/", StringComparison.Ordinal)) path = "meshes/" + path;
                                if (!_modelOwners.TryGetValue(path, out var owners)) _modelOwners.Add(path, owners = []);
                                if (!owners.Any(owner => owner.FormKey == key)) owners.Add(record);
                            }
                        }
                    }
                    if (!record.IsDeleted && record.Signature is "ACHR" or "ACRE" or "REFR")
                    {
                        var name = fields.Single(field => field.Signature == "NAME").Data;
                        if (name.Length != 4) throw new InvalidDataException("Placed base NAME has an unsupported extent.");
                        var baseKey = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(name.Span)).ToString();
                        var cell = FalloutCellSceneReader.ParentCell(record);
                        if (!_references.TryGetValue(baseKey, out var references)) _references.Add(baseKey, references = []);
                        references.Add(new(key.ToString(), records.RuntimeFormId(key), cell?.ToString(),
                            cell is { } cellKey ? records.RuntimeFormId(cellKey) : null, (record.Flags & 0x800) != 0, BaseForm: baseKey));
                    }
                }
                catch (Exception error) when (SourceError(error)) { failures.Add(Failure("record", key.ToString(), error)); }
                Row(rows, new CatalogRecord(key.ToString(), records.RuntimeFormId(key), record.Signature, record.Plugin.Name,
                    hash, record.IsDeleted, editor.Length == 0 ? null : editor, declarations, failures));
                _failures.AddRange(failures); _recordCount++; if (record.IsDeleted) _deletedCount++;
                if (_recordCount % 10000 == 0) progress?.Invoke($"Catalog winning records {_recordCount}/{keys.Length}; explicit failures {_failures.Count}.");
            }
            foreach (var path in _modelOwners.Keys.Order(StringComparer.Ordinal)) ReadResource(path);
        }
    }
}
