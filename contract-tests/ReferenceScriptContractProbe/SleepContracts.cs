using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static byte[] SleepFixture(uint id, int location, uint target, int radius = 0, uint flags = 0)
    {
        var data = new byte[12]; BitConverter.GetBytes(flags).CopyTo(data, 0); data[4] = 4;
        return Record("PACK", id, Field("EDID", Text("Sleep" + id)), Field("PKDT", data),
            Field("PLDT", Join(BitConverter.GetBytes(location), BitConverter.GetBytes(target), BitConverter.GetBytes(radius))));
    }

    private static void SleepContracts(FalloutPluginStack records, FalloutCellScene cell)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        FalloutFormKey Key(uint id) => new("Seats.esm", id);
        var actor = Key(0x900); var bed = Key(0x908);
        var package = records.GetEffective(new("Find.esp", 0x406));
        var source = FalloutSleepPackage.Read(package);
        var flaggedRecord = records.GetEffective(new("Find.esp", 0x411));
        var flagged = FalloutSleepPackage.Read(flaggedRecord);
        var lifecycle = FalloutScriptPackage.Read(flaggedRecord);
        Require(flagged.Flags == 0x88, "Sleep erased its authored start/end door actions.");
        Reject(() => FalloutSleepPackage.Read(records.GetEffective(new("Find.esp", 0x412))));
        foreach (var kind in new[] { "POBA", "POEA", "POCA" }) world.RequirePackageDoorActions(actor, lifecycle, kind);
        Require(world.Get(Key(0x90c)).LockState is null && world.Get(Key(0x90d)).LockState is null,
            "Empty package door actions created a lock on a lockless source door.");
        // Unlocked is not lockless. The linked side and runtime overrides must
        // participate in admission, and refusal must preserve their state.
        world.LockReference(Key(0x90d), 50); world.UnlockReference(Key(0x90d));
        var retainedLock = world.Get(Key(0x90d)).LockState;
        foreach (var kind in new[] { "POBA", "POEA", "POCA" }) Reject(() => world.RequirePackageDoorActions(actor, lifecycle, kind));
        Require(world.Get(Key(0x90d)).LockState == retainedLock && world.Get(Key(0x90c)).LockState is null,
            "Refused package door work changed a real linked lock.");
        Console.WriteLine("OPENNV_SLEEP_EMPTY_DOOR_WORK_CONTRACT_PASS flagsRetained=true noLockCreated=true unlockedLockRefused=true linkedOverride=true allLifecyclePhases=true refusalNonMutating=true");
        var progress = new FalloutActorSleepProgress(cell.Cell.FormKey, bed, false);
        source.Validate(records, world, actor, progress);
        Require(source.Location.Form == cell.Cell.FormKey && source.Candidates(records, world, actor).Single().FormKey == bed,
            "In-cell Sleep lost adjusted source CELL or admitted a chair.");
        Require(records.TryReadSourceIndex<FalloutSleepCellSource>(out var graph, scope: cell.Cell.FormKey),
            "Sleep did not reuse its immutable source bed graph.");
        source.Validate(records, world, actor, progress);
        Require(records.TryReadSourceIndex<FalloutSleepCellSource>(out var reused, scope: cell.Cell.FormKey) && ReferenceEquals(graph, reused),
            "Selected Sleep validation rebuilt the CELL source graph.");
        world.Get(bed).Enabled = false;
        Require(source.Candidates(records, world, actor).Count == 0, "Sleep admitted a disabled bed.");
        Reject(() => source.Validate(records, world, actor, progress));
        world.Get(bed).Enabled = true;
        var reference = FalloutSleepPackage.Read(records.GetEffective(new("Find.esp", 0x407)));
        world.SetPlacement(bed, new(cell.Cell.FormKey, [100, 0, 0], [0, 0, 0]));
        Require(reference.Candidates(records, world, actor).Count == 0 && source.Candidates(records, world, actor).Count == 1,
            "Sleep used stale reference placement or applied a radius to InCell scope.");
        Reject(() => reference.Validate(records, world, actor, progress));
        world.SetPlacement(bed, new(cell.Cell.FormKey, [1, 0, 0], [0, 0, 0]));
        world.SetActorPackageStartLocation(actor, new(cell.Cell.FormKey, [0, 0, 0]));
        foreach (var id in new uint[] { 0x407, 0x408, 0x409 })
            FalloutSleepPackage.Read(records.GetEffective(new("Find.esp", id))).Validate(records, world, actor, progress);
        foreach (var id in new uint[] { 0x40a, 0x40b }) Reject(() => FalloutSleepPackage.Read(records.GetEffective(new("Find.esp", id))));
        Reject(() => source.Validate(records, world, actor, progress with { Bed = Key(0x901) }));
        Reject(() => source.Validate(records, world, actor, progress with { Cell = Key(0x801) }));
        var goal = FalloutPackageTravelGoal.Read(records, world, actor, package, progress);
        Require(goal.Form == package.FormKey && goal.Sleep == progress && goal.Start(records, world, actor).Location.SequenceEqual([1f, 0, 0]),
            "Sleep travel replaced its source package or lost the authoritative bed destination.");
        Reject(() => goal.Validate(records, world, actor, goal.Start(records, world, actor) with { Complete = true }));

        var hash = new string('A', 64); const string resource = "meshes/fixture-bed-loop.kf";
        var assignment = new FalloutActorPackageAssignment(package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), false);
        var motion = new FalloutActorPackageMotion(package.FormKey, assignment.Sha256, resource, hash, .25, false,
            [0, 0, 0], [0, 0, 0, 1], Sleep: progress);
        motion.Validate();
        Reject(() => (motion with { Sleep = progress with { Furniture = true }, Travel = goal.Start(records, world, actor) }).Validate());
        world.Get(actor).PackageAssignment = assignment; world.Get(actor).PackageMotion = motion;
        using (var coldIntent = new FalloutReferenceWorld(records))
        {
            coldIntent.Restore(world.Capture());
            Require(coldIntent.Get(actor).PackageMotion?.Sleep == progress && coldIntent.Get(actor).PackageAssignment?.Done == false,
                "Cold pending Sleep consumed arrival or lost its source bed intent.");
        }
        var furniture = records.GetEffective(world.Get(bed).Base); var idle = records.GetEffective(Key(0x712));
        float[] pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 2, 3];
        var seat = new FalloutFurnitureSeat(furniture.FormKey, 0, 1, new(new(0, 0, 0), 0, 1, 1), [0, 0, 0], 0, true);
        var physical = new FalloutActorFurnitureContinuation(assignment, 1, "POBA", package.FormKey, assignment.Sha256,
            3, 0, pose, package.FormKey, null, 1, 1, null, null, bed,
            FalloutActorFurnitureContinuation.RecordHash(furniture), "meshes/fixture-bed.nif", hash, seat, pose,
            new(idle.FormKey, FalloutActorFurnitureContinuation.RecordHash(idle), resource, hash), SleepPackage: true);
        world.Get(actor).Animation.Restore(new(resource, hash, .25, false));
        world.Get(actor).PackageMotion = motion with { Sleep = progress with { Furniture = true } };
        foreach (var phase in new[] { 1, 2, 3, 4 })
        {
            world.Get(actor).FurnitureContinuation = physical with { Phase = phase,
                Approach = phase == 1 ? pose : null, ApproachFloor = phase == 1 ? [1, 2, 3] : null,
                PendingPackage = phase == 4 ? new("Find.esp", 0x400) : null, PendingSelectionAccepted = phase == 4 };
            world.Get(actor).ProcedureCaptureBlocker = FalloutActorFurnitureContinuation.CaptureBlocker;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(world.Capture());
            Require(cold.Get(actor).PackageAssignment?.Done == false && cold.OwnsFurnitureSeat(bed, 0, actor) &&
                cold.GetSleeping(actor) == (phase == 1 ? 0 : phase), "Sleep capture consumed POEA, lost its physical phase or released the reserved bed.");
        }
        Reject(() => (physical with { Assignment = assignment with { Done = true } }).Validate());
        Console.WriteLine("OPENNV_SLEEP_CONTRACT_PASS sourceLocation=true cachedCell=true mutableBed=true pendingCold=true physicalPhases=true reservation=true scheduleOwnsCompletion=true unsupportedRefused=true");
    }
}
