using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Independent source-derived fatigue formulas. Creatures have no derived base;
// the authored NPC fatigue field participates only in the autocalculated path.
internal static class FalloutActorFatigueSource
{
    internal static float Player(FalloutPluginStack records, float endurance, int level) => Checked(
        Setting(records, "fAVDFatigueBase") + endurance * (double)Setting(records, "fAVDFatigueEnduranceMult") +
        Math.Max(0, level - 1) * (double)Setting(records, "fAVDFatigueLevelMult"));

    internal static float Actor(FalloutPluginStack records, FalloutFormKey actor, FalloutActorTemplateSelection? selection,
        float? permanentEndurance = null)
    {
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor), 2, selection);
        if (source.Signature == "CREA") return 0;
        if (source.Signature != "NPC_") throw new InvalidDataException("Fatigue source is not an actor.");
        var fields = source.ReadSubrecords().ToArray();
        var acbs = fields.Single(field => field.Signature == "ACBS").Data.Span;
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (acbs.Length != 24 || data.Length != 11) throw new InvalidDataException("NPC fatigue source extents are invalid.");
        var endurance = data[6];
        if ((BinaryPrimitives.ReadUInt32LittleEndian(acbs) & 0x10) == 0)
            return Checked(Setting(records, "fAVDNPCFatigueBase") + Math.Clamp(permanentEndurance ?? endurance, 1, 10) *
                (double)Setting(records, "fAVDNPCFatigueEnduranceMult"));
        var authored = BinaryPrimitives.ReadUInt16LittleEndian(acbs[4..]);
        if (authored == 0) return 0;
        var level = FalloutActorLevel.Resolve(acbs, selection?.Level);
        return Checked(Math.Max(0, Math.Truncate(authored + endurance * (double)Setting(records, "fAVDNPCFatigueEnduranceMult") +
            level * (double)Setting(records, "fAVDNPCFatigueLevelMult"))));
    }

    private static float Setting(FalloutPluginStack records, string name) =>
        FalloutGameSettingFloats.ReadRetained(records, name, nameof(FalloutActorFatigueSource));

    private static float Checked(double value) => double.IsFinite(value) && value >= 0 && value <= float.MaxValue
        ? (float)value : throw new InvalidDataException("Source fatigue base is invalid.");
}
