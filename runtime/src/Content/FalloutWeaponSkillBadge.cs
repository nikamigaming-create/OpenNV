using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>The selected weapon's source skill badge, independent of its attack or ammunition family.</summary>
internal sealed record FalloutWeaponSkillBadge(int ActorValue, int Requirement, string? Icon)
{
    internal static FalloutWeaponSkillBadge Read(FalloutPluginStack records, FalloutFormKey weapon)
    {
        var source = records.GetEffective(weapon);
        if (source.Signature != "WEAP") throw new InvalidDataException("Weapon skill badge requires WEAP.");
        return Read(source.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span);
    }

    internal static FalloutWeaponSkillBadge Read(ReadOnlySpan<byte> data)
    {
        if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length) || data.Length < 204)
            throw new NotSupportedException("Weapon skill badge requires its source minimum-skill suffix.");
        var actorValue = BinaryPrimitives.ReadInt32LittleEndian(data[104..]);
        var requirement = BinaryPrimitives.ReadInt32LittleEndian(data[200..]);
        return new(actorValue, requirement, IconFor(actorValue));
    }

    internal static string? IconFor(int actorValue) => actorValue switch
    {
        33 => @"Interface\Icons\TypeIcons\weap_skill_icon_big_guns.dds",
        34 => @"Interface\Icons\TypeIcons\weap_skill_icon_energy.dds",
        35 => @"Interface\Icons\TypeIcons\weap_skill_icon_explosives.dds",
        38 => @"Interface\Icons\TypeIcons\weap_skill_icon_melee.dds",
        41 => @"Interface\Icons\TypeIcons\weap_skill_icon_sm_arms.dds",
        45 => @"Interface\Icons\TypeIcons\weap_skill_icon_unarmed.dds",
        _ => null
    };
}
