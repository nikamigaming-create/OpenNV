using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Script changes affect the loaded winning form, not one actor or one reader's
// decoded copy. They live with the selected stack, outside campaign snapshots.
internal sealed class FalloutPerkParameters(FalloutPluginStack records)
{
    private sealed record Parameter(int Type, int EntryPoint, float? First, float? Second);
    private readonly object _sync = new();
    private readonly Dictionary<FalloutFormKey, Parameter[]> _source = [];
    private readonly Dictionary<(FalloutFormKey Form, uint Index, int Slot), float> _values = [];
    private readonly HashSet<FalloutFormKey> _changedForms = [];

    internal static uint Index(double value) => double.IsFinite(value) && value >= 0 &&
        value <= uint.MaxValue && value == Math.Truncate(value) ? (uint)value :
        throw new InvalidDataException("Perk entry index must be an unsigned integer.");

    internal int Count(FalloutFormKey form) { lock (_sync) return Read(form).Length; }
    internal int Type(FalloutFormKey form, uint index) { lock (_sync) return Entry(form, index)?.Type ?? 0; }
    internal int EntryPoint(FalloutFormKey form, uint index) { lock (_sync) return Entry(form, index)?.EntryPoint ?? -1; }
    internal bool HasOverrides(FalloutFormKey form) { lock (_sync) return _changedForms.Contains(form); }

    internal float Get(FalloutFormKey form, uint index, int slot = 0)
    {
        ValidateSlot(slot);
        lock (_sync)
        {
            var entry = Entry(form, index);
            return _values.GetValueOrDefault((form, index, slot), Number(entry, slot) ?? -1);
        }
    }

    internal bool Set(FalloutFormKey form, uint index, float value, int slot = 0)
    {
        ValidateSlot(slot);
        if (!float.IsFinite(value)) throw new InvalidDataException("Perk parameter must be finite.");
        lock (_sync)
        {
            var entry = Entry(form, index);
            if (Number(entry, slot) is null) return false;
            if (entry!.Type == 6)
            {
                if (value < 0 || value >= 256) throw new InvalidDataException("Quest perk stage exceeds byte storage.");
                value = MathF.Truncate(value);
            }
            _values[(form, index, slot)] = value;
            _changedForms.Add(form);
            return true;
        }
    }

    internal object State
    {
        get
        {
            lock (_sync) return new
            {
                overrides = _values.Select(pair => new
                {
                    form = pair.Key.Form.ToString(),
                    index = pair.Key.Index,
                    slot = pair.Key.Slot + 1,
                    value = pair.Value,
                }).ToArray(),
                persistence = "selected-stack-session-only",
            };
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
    }
    private static float? Number(Parameter? entry, int slot) => slot == 0 ? entry?.First : entry?.Second;
    private Parameter? Entry(FalloutFormKey form, uint index)
    {
        var entries = Read(form);
        return index < entries.Length ? entries[index] : null;
    }

    private Parameter[] Read(FalloutFormKey form)
    {
        if (_source.TryGetValue(form, out var cached)) return cached;
        var source = records.GetEffective(form);
        if (source.Signature != "PERK") throw new InvalidDataException("Perk parameter target is not PERK.");
        var fields = source.ReadSubrecords().ToArray();
        var result = new List<Parameter>();
        for (var index = 0; index < fields.Length; ++index)
        {
            if (fields[index].Signature != "PRKE") continue;
            var header = fields[index].Data.Span;
            if (header.Length != 3) throw new InvalidDataException("Perk parameter entry header extent is invalid.");
            var end = Array.FindIndex(fields, index + 1, field => field.Signature == "PRKF");
            if (end < 0 || fields[end].Data.Length != 0 || fields[(index + 1)..end].Any(field => field.Signature == "PRKE"))
                throw new InvalidDataException("Perk parameter entry is unterminated.");
            var group = fields[(index + 1)..end];
            var data = group.Single(field => field.Signature == "DATA").Data.Span;
            if (header[0] == 0 && data.Length == 8) result.Add(new(6, -1, data[4], null));
            else if (header[0] == 1 && data.Length == 4) result.Add(new(5, -1, null, null));
            else if (header[0] == 2 && data.Length == 3)
            {
                var type = group.Single(field => field.Signature == "EPFT").Data.Span;
                if (type.Length != 1 || type[0] > 4) throw new NotSupportedException("Perk parameter type is unbound.");
                float? first = null, second = null;
                if (type[0] is 1 or 2)
                {
                    var parameter = group.Single(field => field.Signature == "EPFD").Data.Span;
                    if (parameter.Length != type[0] * 4) throw new InvalidDataException("Perk numeric parameter extent is invalid.");
                    first = BinaryPrimitives.ReadSingleLittleEndian(parameter);
                    if (type[0] == 2) second = BinaryPrimitives.ReadSingleLittleEndian(parameter[4..]);
                    if (!float.IsFinite(first.Value) || second is { } other && !float.IsFinite(other))
                        throw new InvalidDataException("Perk source parameter must be finite.");
                }
                result.Add(new(type[0], data[0], first, second));
            }
            else throw new NotSupportedException("Perk parameter entry layout is unbound.");
            index = end;
        }
        var entries = result.ToArray();
        _source.Add(form, entries);
        return entries;
    }
}
