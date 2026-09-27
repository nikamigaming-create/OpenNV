using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutInventoryConditions
{
    internal static bool TargetsPlayer(FalloutPluginStack records, FalloutCondition condition) =>
        condition.RunOn == 0 || condition.RunOn == 2 &&
        condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) == records.RuntimeFormKey(0x14);

    internal static float? Evaluate(FalloutPluginStack records, FalloutPlayerInventory inventory,
        Func<FalloutFormKey, bool> hasPerk, FalloutCondition condition)
    {
        if (!TargetsPlayer(records, condition)) return null;
        return condition.Function switch
        {
            47 => inventory.Item(condition.FormArgument1)?.Count ?? 0,
            182 => inventory.Equipped.Contains(records.RuntimeFormId(condition.FormArgument1)) ? 1 : 0,
            382 => records.GetEffective(condition.FormArgument1).Signature != "NOTE"
                ? throw new InvalidDataException("GetHasNote argument is not NOTE.")
                : inventory.Item(condition.FormArgument1) is { Count: > 0 } ? 1 : 0,
            449 when condition.Argument2 == 0 => hasPerk(condition.FormArgument1) ? 1 : 0,
            _ => null,
        };
    }
}
