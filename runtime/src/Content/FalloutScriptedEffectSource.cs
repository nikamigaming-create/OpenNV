using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal enum FalloutScriptedEffectOrigin { ConstantSpell, Ingestible, DeliveredSpell }

internal sealed record FalloutScriptedEffectSource(FalloutAbilityScript Script, FalloutScriptedEffectOrigin Origin,
    string ItemWinner, string ItemSha256, string EffectWinner, string EffectSha256,
    uint Flags, uint DeclaredDuration, uint DurationBits, IReadOnlyList<FalloutCondition> Conditions)
{
    internal bool Constant => Origin == FalloutScriptedEffectOrigin.ConstantSpell;
    internal bool NoDuration => (Flags & 0x80) != 0;
    internal float Duration => BitConverter.UInt32BitsToSingle(DurationBits);
    internal string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))).ToLowerInvariant();

    internal static FalloutScriptedEffectSource Read(FalloutPluginStack records, FalloutAbilityScript declared)
    {
        var item = records.GetEffective(declared.Spell);
        var effect = records.GetEffective(declared.Effect);
        var script = records.GetEffective(declared.Script);
        if (item.IsDeleted || effect.IsDeleted || script.IsDeleted || effect.Signature != "MGEF" || script.Signature != "SCPT")
            throw new InvalidDataException("Scripted magic effect has no complete live winning source graph.");
        var fields = item.ReadSubrecords().ToArray();
        var origin = item.Signature switch
        {
            "SPEL" => SpellOrigin(fields),
            "ALCH" => FalloutScriptedEffectOrigin.Ingestible,
            "ENCH" => throw new NotSupportedException("Scripted enchantment still requires its genuine equipped-item application owner."),
            _ => throw new InvalidDataException("Scripted effect item is neither SPEL, ALCH nor ENCH."),
        };
        var groups = fields.Select((field, index) => (field, index)).Where(row => row.field.Signature == "EFID").ToArray();
        if ((uint)declared.EffectOrdinal >= groups.Length)
            throw new InvalidDataException("Scripted effect ordinal is outside its winning item.");
        var start = groups[declared.EffectOrdinal].index;
        var end = declared.EffectOrdinal + 1 < groups.Length ? groups[declared.EffectOrdinal + 1].index : fields.Length;
        var id = fields[start].Data.Span;
        if (id.Length != 4 || item.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(id)) != declared.Effect)
            throw new InvalidDataException("Scripted effect ordinal changed its source EFID identity.");
        var group = fields[(start + 1)..end];
        if (group.Any(field => field.Signature is not ("EFIT" or "CTDA")))
            throw new NotSupportedException("Scripted magic item has an unowned effect extension.");
        var efit = group.Single(field => field.Signature == "EFIT").Data.Span;
        var definition = effect.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (efit.Length != 20 || definition.Length != 72)
            throw new NotSupportedException("Scripted magic effect requires source EFIT(20)/DATA(72). ");
        if (BinaryPrimitives.ReadUInt32LittleEndian(definition[64..]) != 1 ||
            effect.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(definition[8..])) != declared.Script)
            throw new InvalidDataException("Scripted effect does not own this winning archetype/SCPT link.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(efit[4..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(efit[12..]) != 0)
            throw new NotSupportedException("Scripted effect requires its area/projectile/target delivery owner.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(definition);
        var declaredDuration = BinaryPrimitives.ReadUInt32LittleEndian(efit[8..]);
        // The original duration constructor converts its signed 32-bit source
        // value into a retained Float32. No host double timer replaces it.
        var duration = (flags & 0x80) != 0 ? 0 : (float)unchecked((int)declaredDuration);
        if (duration < 0 || !float.IsFinite(duration))
            throw new NotSupportedException("Scripted effect duration has no nonnegative original Float32 domain.");
        if (origin == FalloutScriptedEffectOrigin.ConstantSpell && declaredDuration != 0)
            throw new NotSupportedException("Constant effect has a nonzero duration requiring its source constructor consumer.");
        var conditions = group.Where(field => field.Signature == "CTDA")
            .Select(field => FalloutCondition.Read(item, field.Data.Span)).ToArray();
        if (!conditions.SequenceEqual(declared.Conditions))
            throw new InvalidDataException("Scripted effect condition scope differs from its exact item ordinal.");
        return new(declared, origin, item.Plugin.Name, Hash(item), effect.Plugin.Name, Hash(effect),
            flags, declaredDuration, BitConverter.SingleToUInt32Bits(duration), conditions);
    }

    internal static FalloutAbilityScript FromIngestible(FalloutPluginStack records, FalloutIngestible item, FalloutIngestibleEffect effect)
    {
        if (effect.Archetype != 1 || effect.Index < 0 || effect.Index >= item.Effects.Count ||
            !ReferenceEquals(item.Effects[effect.Index], effect))
            throw new InvalidDataException("Ingestible script application differs from its source effect occurrence.");
        var winner = records.GetEffective(effect.Form);
        var data = winner.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 72 || BinaryPrimitives.ReadUInt32LittleEndian(data[64..]) != 1)
            throw new InvalidDataException("Ingestible script effect has no actual DATA/SCPT producer.");
        var script = winner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[8..])) ??
            throw new InvalidDataException("Ingestible script effect has a null SCPT link.");
        var definition = new FalloutAbilityScript(item.Form, effect.Form, script, effect.Conditions, effect.Index);
        _ = Read(records, definition);
        return definition;
    }

    internal void RequireSource(FalloutPluginStack records)
    {
        if (Read(records, Script).Identity != Identity)
            throw new InvalidDataException("Active effect changed its exact item/MGEF/SCPT/condition source.");
    }

    private static FalloutScriptedEffectOrigin SpellOrigin(FalloutPluginSubrecord[] fields)
    {
        var data = fields.Single(field => field.Signature == "SPIT").Data.Span;
        if (data.Length != 16) throw new NotSupportedException("Scripted spell requires its source SPIT(16).");
        return BinaryPrimitives.ReadUInt32LittleEndian(data) switch
        {
            4 => FalloutScriptedEffectOrigin.ConstantSpell,
            0 => FalloutScriptedEffectOrigin.DeliveredSpell,
            _ => throw new NotSupportedException("Spell class requires its disease/power/addiction/application producer."),
        };
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
}
