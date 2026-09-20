using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorTemplateChoice(FalloutFormKey List, string SourceSha256, FalloutFormKey? Actor);
internal sealed record FalloutActorTemplateSnapshot(int Level, ulong RandomState, IReadOnlyList<FalloutActorTemplateChoice> Choices);
internal sealed class FalloutActorNotSelectedException : Exception
{
    internal FalloutActorNotSelectedException(FalloutFormKey list) : base($"Actor list {list} selected no actor.") { }
}

// The encounter chooses each list once for this reference. Every template
// group, presentation owner and cold save observes that same choice.
internal sealed class FalloutActorTemplateSelection
{
    private readonly Dictionary<FalloutFormKey, FalloutActorTemplateChoice> _choices = [];
    private readonly FalloutSoundRandomState _random;
    private readonly Func<FalloutFormKey, float>? _global;
    private readonly bool _restored;
    internal int Level { get; }
    internal bool Absent => _choices.Values.Any(choice => choice.Actor is null);

    internal FalloutActorTemplateSelection(int level, ulong seed, Func<FalloutFormKey, float>? global = null)
    {
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        Level = level; _random = new(seed); _global = global;
    }

    internal FalloutActorTemplateSelection(FalloutActorTemplateSnapshot snapshot)
        : this(snapshot.Level, snapshot.RandomState)
    {
        _restored = true;
        if (snapshot.Choices is null) throw new InvalidDataException("Saved actor choices are absent.");
        foreach (var choice in snapshot.Choices)
            if (choice.SourceSha256 is not { Length: 64 } || !choice.SourceSha256.All(Uri.IsHexDigit) ||
                !_choices.TryAdd(choice.List, choice)) throw new InvalidDataException("Saved actor choices are invalid or duplicated.");
    }

    internal void ResolveAll(FalloutPluginStack records, FalloutFormKey actor)
    {
        try
        {
            for (ushort group = 1; group <= 512; group *= 2)
                _ = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor), group, this);
        }
        catch (FalloutActorNotSelectedException) { }
    }

    internal FalloutFormKey Choose(FalloutPluginRecord list)
    {
        if (list.Signature is not ("LVLN" or "LVLC")) throw new InvalidDataException("Actor selection owner is not a leveled actor list.");
        var hash = Convert.ToHexString(SHA256.HashData(list.ReadData())).ToLowerInvariant();
        if (_choices.TryGetValue(list.FormKey, out var retained))
        {
            if (retained.SourceSha256 != hash) throw new InvalidDataException($"Saved actor list {list.FormKey} differs from its winning source.");
            if (retained.Actor is { } actor && !list.ReadSubrecords().Where(field => field.Signature == "LVLO").Any(field =>
                field.Data.Length == 12 && BinaryPrimitives.ReadUInt16LittleEndian(field.Data.Span) <= Level &&
                list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[4..])) == actor))
                throw new InvalidDataException($"Saved actor choice is not eligible in {list.FormKey}.");
            return retained.Actor ?? throw new FalloutActorNotSelectedException(list.FormKey);
        }
        if (_restored) throw new InvalidDataException($"Saved actor selection has no choice for {list.FormKey}.");
        var fields = list.ReadSubrecords().ToArray();
        var chanceField = fields.Single(field => field.Signature == "LVLD").Data;
        var flagsField = fields.Single(field => field.Signature == "LVLF").Data;
        if (chanceField.Length != 1 || flagsField.Length != 1 || (flagsField.Span[0] & ~3) != 0)
            throw new InvalidDataException("Leveled actor flags have an invalid extent or value.");
        float chance = chanceField.Span[0];
        var global = fields.SingleOrDefault(field => field.Signature == "LVLG").Data;
        if (!global.IsEmpty)
        {
            if (global.Length != 4) throw new InvalidDataException("Leveled actor global extent is invalid.");
            var key = list.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(global.Span));
            if (key is { } value) chance = (_global ?? throw new NotSupportedException("Actor chance-global owner is absent."))(value);
        }
        if (!float.IsFinite(chance) || chance is < 0 or > 100) throw new InvalidDataException("Actor chance-none is outside 0..100.");
        if (fields.Any(field => field.Signature == "COED")) throw new NotSupportedException("Leveled actor entry extra-data ownership is unbound.");
        var entries = fields.Where(field => field.Signature == "LVLO").Select(field =>
        {
            var data = field.Data.Span;
            if (data.Length != 12 || BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) != 1)
                throw new NotSupportedException("Leveled actor entry extent/count is unbound.");
            return (Level: BinaryPrimitives.ReadUInt16LittleEndian(data),
                Actor: list.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..])));
        }).Where(entry => entry.Level <= Level).ToArray();
        if (entries.Length != 0 && (flagsField.Span[0] & 1) == 0)
            entries = entries.Where(entry => entry.Level == entries.Max(candidate => candidate.Level)).ToArray();
        FalloutFormKey? selected = entries.Length == 0 || chance > 0 && _random.NextBounded(100) < chance
            ? null : entries[checked((int)_random.NextBounded((uint)entries.Length))].Actor;
        _choices.Add(list.FormKey, new(list.FormKey, hash, selected));
        return selected ?? throw new FalloutActorNotSelectedException(list.FormKey);
    }

    internal FalloutActorTemplateSnapshot Capture() => new(Level, _random.State,
        _choices.Values.OrderBy(choice => choice.List.ToString(), StringComparer.Ordinal).ToArray());
}
