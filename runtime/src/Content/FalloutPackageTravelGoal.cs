using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// Movement transports an owned package goal. It never substitutes a Travel
// PACK for Sleep or completes Sleep when its destination cell is entered.
internal sealed record FalloutPackageTravelGoal(FalloutTravelPackage Movement, FalloutActorSleepProgress? Sleep = null)
{
    internal FalloutFormKey Form => Movement.Form;
    internal int LocationType => Sleep is null ? Movement.LocationType : 1;
    internal FalloutFormKey? Reference => Sleep?.Bed ?? Movement.Reference;
    internal int Radius => Movement.Radius;
    internal bool Running => Movement.Running;
    internal bool Sneaking => Movement.Sneaking;
    internal bool ContinueIfPlayerNear => Movement.ContinueIfPlayerNear;
    internal bool MustReach => Movement.MustReach;
    internal bool WeaponDrawn => Movement.WeaponDrawn;
    internal bool ContinueCombat => Movement.ContinueCombat;
    internal bool NoCombatAlert => Movement.NoCombatAlert;
    internal bool HoldsForPlayer(bool near) => Sleep is null && Movement.HoldsForPlayer(near);
    internal bool RetainsSelection(bool complete, bool near, bool forced) => Sleep is null && Movement.RetainsSelection(complete, near, forced);
    internal void RequireCombatContinuation(bool engaged, bool complete) => Movement.RequireCombatContinuation(engaged, complete);

    internal static FalloutPackageTravelGoal Read(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutPluginRecord package, FalloutActorSleepProgress? sleep = null)
    {
        if (sleep is null) return new(FalloutTravelPackage.Read(package, ownsIdleCollection: true));
        var source = FalloutSleepPackage.Read(package);
        source.Validate(records, world, actor, sleep);
        // These are the original Sleep flags and source bed. Movement only
        // consumes their shared locomotion/portal policy, not a synthetic PACK.
        return new(new(package.FormKey, source.Flags, 1, sleep.Bed, 0), sleep);
    }

    internal FalloutTravelProgress Start(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        float[]? currentLocation = null)
    {
        if (Sleep is null) return Movement.Start(records, world, actor, currentLocation);
        FalloutSleepPackage.Read(records.GetEffective(Form)).Validate(records, world, actor, Sleep);
        var placement = world.Placement(Sleep.Bed);
        return new(placement.Cell, (float[])placement.Position.Clone(), false);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor, FalloutTravelProgress progress)
    {
        if (Sleep is null) { Movement.Validate(records, world, actor, progress); return; }
        if (Sleep.Furniture) throw new InvalidDataException("Physical Sleep retained a cell-approach route.");
        var source = Start(records, world, actor);
        progress.Validate();
        if (progress.Cell != source.Cell || !progress.Location.SequenceEqual(source.Location) || progress.Complete)
            throw new InvalidDataException("Sleep travel differs from its selected source bed or completed the parent package.");
        FalloutTravelPackage.ValidateNativeNpcRoute(records, world, actor, progress);
    }
}
