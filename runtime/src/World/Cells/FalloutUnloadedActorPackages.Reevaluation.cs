using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutUnloadedActorPackages
{
    private readonly Dictionary<FalloutFormKey, long> _bindingScriptPackageRevisions = [];
    private readonly Dictionary<FalloutFormKey, long> _boundScriptPackageRevisions = [];
    private double _pollRemaining;
    private long _questRevision = -1;
    private FalloutScheduleTime? _scheduleTime;

    internal void EvaluatePackages(FalloutFormKey actor, bool reset)
    {
        var state = world.Get(actor);
        FalloutReferencePackageEvents.RequireActor(records, actor);
        if (state.ScriptError is { } error) throw new NotSupportedException(error);
        if (!world.IsEnabled(actor) || state.Deleted || state.DeletePending) return;
        state.PendingPackageSelection = null;
        state.SelectionFailure = null;
        state.PackageBindingFailure = null;
        _ = SelectUnloadedPackage(actor, reevaluateScript: true);
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _pollRemaining -= seconds;
        var moment = clock?.ScheduleTime();
        if (moment is { } time) moment = time with { Hour = MathF.Floor(time.Hour) };
        if (_pollRemaining > 0 && _questRevision == quests.Revision && _scheduleTime == moment) return;
        _pollRemaining = 10; _questRevision = quests.Revision; _scheduleTime = moment;
        foreach (var actor in _actors.Keys.Concat(world.ScriptPackageActors).Distinct().ToArray())
        {
            if (world.IsResident(actor) || !world.IsEnabled(actor)) continue;
            var state = world.Get(actor);
            if (state.Deleted || state.DeletePending || state.ScriptError is not null) continue;
            try { _ = SelectUnloadedPackage(actor, reevaluateScript: true); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
                FileNotFoundException or KeyNotFoundException or OverflowException)
            {
                state.ScriptError ??= $"Package selection {actor}: {error.Message}";
            }
        }
    }

    private long BeginScriptPackageEvent(FalloutFormKey actor, FalloutScriptPackage package, string kind)
    {
        if (kind == "POBA")
            _boundScriptPackageRevisions[actor] = world.BeginActorScriptPackage(actor, package.Form,
                _bindingScriptPackageRevisions.GetValueOrDefault(actor));
        return _boundScriptPackageRevisions.GetValueOrDefault(actor);
    }

    private void CompleteScriptPackageEvent(FalloutFormKey actor, FalloutScriptPackage package, string kind, long startedRevision)
    {
        if (kind == "POCA" && startedRevision != 0)
            world.RetireActorScriptPackage(actor, package.Form, startedRevision);
    }
}
