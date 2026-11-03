using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutEatPackage? _creatureEatPackage;
    private FalloutActorEatPackageProgress? _creatureEatProgress;
    private FalloutAmbientIdleTree? _creatureEatIdles;
    private bool _creatureEatRetiring;

    private float CreatureEatHour() => _aiClock?.ScheduleTime().Hour ??
        throw new NotSupportedException("Creature Eat requires its authoritative package clock.");

    private void BeginCreatureEat(FalloutPluginRecord record, FalloutEatPackage source, bool restoring)
    {
        var actor = Appearance.Reference!.Value;
        var world = _aiWorld!;
        var state = _aiState!;
        if (restoring)
        {
            _ = world.ActorPackageStartLocation(actor) ??
                throw new InvalidDataException("Restored Creature Eat has no retained start location.");
            _creatureEatProgress = state.PackageMotion!.EatPackage!;
        }
        else
        {
            if (!IsInsideTree()) throw new NotSupportedException("Creature Eat needs its live package-start pose.");
            var firstAdmission = IsFirstResidentProcedureAdmission(record);
            if (!_initialPackageSelected && state.PackageAssignment is { Done: false } assignment && assignment.Package == record.FormKey && !firstAdmission)
                throw new InvalidDataException("Saved active Creature Eat has no retained producer continuation.");
            if (firstAdmission && FalloutPackageSchedule.Read(record).Hour == -1)
                throw new NotSupportedException("Deferred Creature Eat with unspecified start hour has no retained source assignment clock.");
            world.CaptureActorPackageStartLocation(actor, Skeleton.UnitsToMetres);
            _creatureEatProgress = new(source.Scope(world, actor).Cell, Creature: new(CreatureEatHour()));
        }
        source.Validate(_aiRecords!, world, actor, _creatureEatProgress);
        _creatureEatPackage = source;
        _creatureEatRetiring = false;
    }

    private void AdvanceCreatureEat(double delta)
    {
        var source = _creatureEatPackage!;
        var progress = _creatureEatProgress!;
        var producer = progress.Creature!;
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        source.Validate(_aiRecords!, world, actor, progress);
        if (!world.SameTravelRegion(world.Placement(actor).Cell, progress.Cell))
            throw new NotSupportedException("Creature Eat current-location continuation requires its resident source region.");
        if (!producer.Arrived)
        {
            var center = source.Scope(world, actor).Center!;
            var authored = GetParent<Node3D>().ToGlobal(new Vector3(center[0], center[2], -center[1]) * Skeleton.UnitsToMetres);
            var destination = Combat!.ProjectPackageDestination(authored);
            var arrived = Combat.AdvancePackageMotion(_aiPackage!, destination, source.Location.Radius * Skeleton.UnitsToMetres,
                source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true,
                navigationSha256: world.TravelNavigation(world.Placement(actor).Cell).SourceSha256);
            RetainCreatureEat(progress with { Creature = producer with { Arrived = arrived } });
            // Travel's arrival advances the procedure; it does not end PACK.
            return;
        }
        if (!EatIdleActive)
            Combat!.AdvancePackageMotion(_aiPackage!, GlobalPosition, 0, false, delta, source.WeaponDrawn);
        if (producer.UsedItem is null)
        {
            var item = FalloutCreatureEatItem.Select(_aiRecords!, source, _aiState!.SoundRandom.NextBounded);
            RetainCreatureEat(progress with { Creature = producer with { UsedItem = item } });
            return;
        }
        if (producer.RetrySeconds > 0)
        {
            RetainCreatureEat(progress with { Creature = producer with { RetrySeconds = producer.RetrySeconds - (float)delta } });
            return;
        }
        FalloutPluginRecord? idle;
        var replay = (_aiState!.AmbientIdle ??= new()).Replay;
        using (world.EnterUsedItem(producer.UsedItem.Item))
            idle = (_creatureEatIdles ??= new(_aiRecords!, Appearance.SkeletonPath)).Select(condition =>
                condition.Function == (ushort)OpenNV.Runtime.Content.Definitions.FalloutConditionFunction.GetSleeping &&
                    condition.RunOn == 0 && condition.Argument1 == 0 && condition.Argument2 == 0
                    ? 0 : condition.Kind == OpenNV.Runtime.Content.Definitions.FalloutConditionFunction.GetRandomPercent
                        ? FalloutSharedUnsignedRandom.Process.NextBounded(100) : PackageCondition(condition), replay.CanSelect);
        if (idle is not null) RequestCreatureIdle(_aiRecords!, idle, "eat");
        // Source producer refusal also consumes its ordinary retry sample.
        RetainCreatureEat(progress with { Creature = producer with { RetrySeconds = 1 + 5 * _aiState!.SoundRandom.NextUnitFloat() } });
    }

    private void RetainCreatureEat(FalloutActorEatPackageProgress progress)
    {
        _creatureEatProgress = progress;
        Combat!.SetEatPackageProgress(progress);
        if (_aiState!.ProcedureCaptureBlocker == CreatureEatCaptureBlocker) _aiState.ProcedureCaptureBlocker = null;
    }
}
