using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// This adapter owns quota completion and a genuinely empty resident search.
// A food source is not permission to execute an unowned acquisition action.
internal sealed record FalloutFindFoodPackage(FalloutFindPackage Declaration)
{
    internal FalloutFormKey Form => Declaration.Source.Form;
    internal bool Running => (Declaration.Data.Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Declaration.Data.Flags & 0x800000) != 0;
    internal int Count => Declaration.Target.CountOrDistance;

    internal bool QuotaSatisfied(FalloutPlayerInventory inventory, Func<FalloutFormKey, bool> isFood) =>
        inventory.Items.Where(item => item.Count > 0 && isFood(item.FormKey)).Sum(item => (long)item.Count) >= Count;

    internal static FalloutFindFoodPackage Read(FalloutPluginRecord record)
    {
        var declaration = FalloutFindPackage.Read(record);
        const uint supported = 0x400 | 0x1000 | 0x2000 | 0x800000 | FalloutScriptPackage.AllowFallsFlag |
            FalloutScriptPackage.HeadTrackingOffFlag | FalloutScriptPackage.WeaponsUnequippedFlag;
        if (declaration.Source.LocationDeclaration is not { Type: 2, Radius: > 0 } ||
            declaration.Target is not { Type: 2, RawTarget: 18, CountOrDistance: > 0, AuxiliaryBits: 0 } ||
            (declaration.Data.Flags & ~supported) != 0 || (declaration.Data.Flags & 0x400) == 0 ||
            declaration.Data.BehaviorFlags != 0 || declaration.Data.SpecificFlags is not (null or 0 or 0x200) ||
            declaration.Source.Idles.Count != 0 || record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTD2"))
            throw new NotSupportedException("Find Food requires its current-location once-per-day quota/empty-search owner.");
        return new(declaration);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor, FalloutFindFoodProgress progress)
    {
        progress.Validate();
        if (records.GetEffective(world.Get(actor).Base).Signature != "CREA" ||
            Declaration.Location(records, world, actor).Cell != progress.Cell)
            throw new InvalidDataException("Find Food continuation differs from its Creature or retained source region.");
    }
}
