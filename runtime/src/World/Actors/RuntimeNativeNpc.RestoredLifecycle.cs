using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private PackageSelection ReconcileRestoredPackageSelection(PackageSelection selection, bool forced)
    {
        var events = _packageEvents;
        if (_aiPackage is not null || events?.Active is not { } outgoing ||
            outgoing.Form == selection.Record?.FormKey) return selection;
        var world = _aiWorld ?? throw new NotSupportedException("Restored package selection has no shared world owner.");
        var records = _aiStack ?? throw new NotSupportedException("Restored package selection has no source owner.");
        var actor = Appearance.Reference!.Value;
        var motion = world.Get(actor).PackageMotion;
        if (outgoing.Procedure == 6 && motion is { Travel: { } travel } && motion.Package == outgoing.Form)
        {
            var source = records.GetEffective(outgoing.Form);
            var goal = FalloutPackageTravelGoal.Read(records, world, actor, source);
            goal.Validate(records, world, actor, travel);
            if (goal.RetainsSelection(travel.Complete,
                    !forced && !travel.Complete && !goal.MustReach && goal.ContinueIfPlayerNear && Combat!.PackagePlayerInSameCell(), forced))
                return new(source, outgoing, Forced: forced);
        }
        // Cold binding can restore an accepted source lifecycle before it has
        // a native procedure object. A changed schedule still retires that
        // lifecycle through its normal event owner, just as live replacement
        // does. Keep the retained motion pose for the combat child's Ready;
        // neither an incoming procedure nor a save may silently erase it.
        events.Change(null);
        Combat?.RetirePackageMotionNavigation(outgoing.Form);
        GD.Print($"OPENNV_NATIVE_RESTORED_PACKAGE_CHANGE reference={actor} previous={outgoing.Form} next={selection.Record?.FormKey} owner=accepted-source-selection");
        return selection;
    }
}
