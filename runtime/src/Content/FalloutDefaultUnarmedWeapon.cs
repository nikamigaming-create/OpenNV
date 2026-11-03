using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

// The engine's empty weapon-process selection refers to this winning source
// weapon. It is not an inventory grant and has no physical item instance.
internal static class FalloutDefaultUnarmedWeapon
{
    internal static FalloutFormKey Form(FalloutPluginStack records) => records.RuntimeFormKey(FalloutEngineForms.DefaultUnarmedWeapon);

    internal static FalloutFormKey Require(FalloutPluginStack records)
    {
        var form = Form(records);
        if (!records.TryGetEffective(form, out var weapon) || weapon.Signature != "WEAP")
            throw new NotSupportedException("Engine unarmed fallback requires its winning weapon source.");
        return form;
    }
}
