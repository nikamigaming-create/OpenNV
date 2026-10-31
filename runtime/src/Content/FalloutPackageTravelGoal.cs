using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// Movement transports an owned package goal. It never substitutes a Travel
// PACK for a parent procedure or completes its interaction at a cell boundary.
internal sealed record FalloutPackageTravelGoal(FalloutTravelPackage Movement, FalloutActorSleepProgress? Sleep = null,
    FalloutActorEatPackageProgress? Eat = null, FalloutFormKey? Furniture = null)
{
    internal FalloutFormKey Form => Movement.Form;
    internal int LocationType => Sleep is null && Eat is null ? Movement.LocationType : 1;
    internal FalloutFormKey? Reference => Sleep?.Bed ?? Movement.Reference;
    internal int Radius => Movement.Radius;
    internal bool Running => Movement.Running;
    internal bool Sneaking => Movement.Sneaking;
    internal bool AllowFalls => Movement.AllowFalls;
    internal bool ContinueIfPlayerNear => Movement.ContinueIfPlayerNear;
    internal bool MustReach => Movement.MustReach;
    internal bool WeaponDrawn => Movement.WeaponDrawn;
    internal bool ContinueCombat => Movement.ContinueCombat;
    internal bool NoCombatAlert => Movement.NoCombatAlert;
    internal bool HoldsForPlayer(bool near) => Sleep is null && Eat is null && Movement.HoldsForPlayer(near);
    internal bool RetainsSelection(bool complete, bool near, bool forced) => Sleep is null && Eat is null && Movement.RetainsSelection(complete, near, forced);
    internal void RequireCombatContinuation(bool engaged, bool complete) => Movement.RequireCombatContinuation(engaged, complete);

    internal static FalloutPackageTravelGoal Read(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutPluginRecord package, FalloutActorSleepProgress? sleep = null,
        FalloutActorEatPackageProgress? eat = null)
    {
        if (eat is not null)
        {
            if (sleep is not null) throw new InvalidDataException("A movement leg cannot have two parent procedures.");
            var eating = FalloutEatPackage.Read(package);
            eating.Validate(records, world, actor, eat);
            return new(new(package.FormKey, eating.Flags, 1, eat.Seat ?? eating.Location.Form, 0), Eat: eat);
        }
        if (sleep is null)
        {
            var movement = FalloutTravelPackage.Read(package, ownsIdleCollection: true);
            if (movement.LocationType == 0 && movement.Reference is { } reference &&
                records.GetEffective(world.Get(reference).Base).Signature == "FURN")
            {
                if (movement.Radius != 0) throw new NotSupportedException("Furniture Travel requires its location-radius owner.");
                return new(movement, Furniture: reference);
            }
            return new(movement);
        }
        var source = FalloutSleepPackage.Read(package);
        source.Validate(records, world, actor, sleep);
        // These are the original Sleep flags and source bed. Movement only
        // consumes their shared locomotion/portal policy, not a synthetic PACK.
        return new(new(package.FormKey, source.Flags, 1, sleep.Bed, 0), sleep);
    }

    internal FalloutTravelProgress Start(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        float[]? currentLocation = null)
    {
        if (Furniture is { } furniture)
        {
            var state = world.Get(furniture);
            if (state.Deleted || state.DeletePending || records.GetEffective(state.Base).Signature != "FURN")
                throw new InvalidDataException("Furniture Travel destination is no longer an existing source furniture reference.");
            var target = world.Placement(furniture);
            target.Validate();
            return new(target.Cell, (float[])target.Position.Clone(), false);
        }
        if (Eat is { } eat)
        {
            var source = FalloutEatPackage.Read(records.GetEffective(Form));
            source.Validate(records, world, actor, eat);
            var (cell, center) = source.Scope(world, actor);
            if (eat.Seat is { } seat) center = world.Placement(seat).Position;
            return new(cell, center is null ? throw new NotSupportedException("Eat needs its whole-cell destination selection before movement.") :
                (float[])center.Clone(), false);
        }
        if (Sleep is null) return Movement.Start(records, world, actor, currentLocation);
        FalloutSleepPackage.Read(records.GetEffective(Form)).Validate(records, world, actor, Sleep);
        var placement = world.Placement(Sleep.Bed);
        return new(placement.Cell, (float[])placement.Position.Clone(), false);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor, FalloutTravelProgress progress)
    {
        if (Sleep is null && Eat is null && Furniture is null) { Movement.Validate(records, world, actor, progress); return; }
        if (Sleep?.Furniture == true || Eat?.Furniture == true)
            throw new InvalidDataException("Physical resting/eating retained a cell-approach route.");
        var source = Start(records, world, actor);
        progress.Validate();
        if (progress.Cell != source.Cell || !progress.Location.SequenceEqual(source.Location) || progress.Complete)
            throw new InvalidDataException("Parent-procedure travel differs from its source destination or completed the assignment.");
        FalloutTravelPackage.ValidateNativeNpcRoute(records, world, actor, progress);
    }
}
