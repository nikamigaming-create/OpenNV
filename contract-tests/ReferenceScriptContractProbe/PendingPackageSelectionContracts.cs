using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class PendingPackageSelectionContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Pose.esm", id);
        string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
        using var world = new FalloutReferenceWorld(records);
        var actor = world.Get(Key(0x900));
        actor.Animation.Change("meshes/fixture/mtidle.kf", new('a', 64)); actor.Animation.Advance(6.25);
        var receipt = new FalloutActorPendingPackageSelection(Key(0x10), Hash(0x10), Key(0x25), Hash(0x25),
            [1, 0, 0, 0, 1, 0, 0, 0, 1, 3, 4, 5], 0x123456789abcdef0, 0, new(1, 10, 3, 9), -1, 7,
            new(false, true, false, false, false, false, 11), new(2, "POCA", Key(0x20), Hash(0x20)), null,
            "Prior source binding fault.", Key(0x20), Hash(0x20), null, "Prior source idle fault.");
        actor.ProcedureCaptureBlocker = FalloutActorPendingPackageSelection.CaptureBlocker;
        Require(world.PendingProcedureCaptureCount == 1, "Missing queued selection receipt became saveable.");
        actor.CanCapturePendingPackageSelection = () => true;
        actor.CapturePendingPackageSelection = () => receipt.Copy();
        var captured = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        Require(world.PendingProcedureCaptureCount == 0 && captured.Single().PendingPackageSelection is not null,
            "Queued selection did not retain its explicit save owner.");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(captured);
        var resumed = cold.Get(actor.Reference);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(captured) &&
            resumed.PendingPackageSelection!.RandomState == receipt.RandomState && resumed.Animation.ElapsedSeconds == 6.25 &&
            resumed.PendingPackageSelection.Error == receipt.Error && resumed.PendingPackageSelection.IdleError == receipt.IdleError,
            "Cold selection changed its source errors, pose, RNG, activity or base clock.");
        cold.UnloadedPackages = new(records, cold, new(records), null, null,
            (_, _) => throw new InvalidDataException("Unloaded queued selection ran source results."), () => 1);
        Require(cold.CurrentPackage(actor.Reference) == receipt.Package && cold.PendingPackageEventCount == 0,
            "Unloaded selected-result query reevaluated or began a procedure.");
        var events = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Restoring queued selection replayed source events."));
        events.RestoreRetirement(receipt.Retirement);
        Require(events.Active is null && events.Revision == 2 && events.LastEvent == "POCA",
            "Queued selection restored an active assignment or lost consumed retirement.");
        var nullReceipt = receipt with { Package = null, PackageSha256 = null };
        (receipt with { Error = null }).Validate(records, actor);
        var nullSelection = captured.Select(value => value with { PendingPackageSelection = nullReceipt }).ToArray();
        using var nullCold = new FalloutReferenceWorld(records); nullCold.Restore(nullSelection);
        nullCold.UnloadedPackages = new(records, nullCold, new(records), null, null,
            (_, _) => throw new InvalidDataException("Explicit null selection began source results."), () => 1);
        Require(nullCold.CurrentPackage(actor.Reference) is null && nullCold.PendingProcedureCaptureCount == 0 &&
            JsonSerializer.Serialize(nullCold.Capture()) == JsonSerializer.Serialize(nullSelection),
            "Explicit selected-null result was confused with missing selection.");
        foreach (var invalid in new[]
        {
            receipt with { PrioritySha256 = new('0', 64) }, receipt with { PackageSha256 = new('0', 64) },
            receipt with { Package = Key(0x20), PackageSha256 = Hash(0x20) },
            receipt with { Package = null }, receipt with { RandomState = 5, Pose = new float[12] },
            receipt with { FailedPackageSha256 = new('0', 64) }, receipt with { PollRemaining = -1 },
            receipt with { Activity = receipt.Activity with { Revision = -1 } },
            receipt with { Retirement = receipt.Retirement with { LastEvent = "POBA" } }
        }) AtomicReject(records, captured.Select(value => value with { PendingPackageSelection = invalid }).ToArray());
        AtomicReject(records, captured.Select(value => value with
        { Engagement = new(Key(0x902), "pursue", Position: [3, 4, 5], Rotation: [0, 0, 0, 1]) }).ToArray());
        var activity = new FalloutActorActivityState(); var alerted = true; long alertRevision = 13;
        activity.BindAlerted(() => alerted, value => { alerted = value; alertRevision++; }, () => alertRevision);
        var flags = receipt.Activity with { Alerted = true, WeaponDrawn = true, Running = true, InCombat = true, Revision = 21 };
        activity.Restore(flags);
        Require(activity.Capture() == flags && alertRevision == 13, "Activity restore replayed its shared alert transition.");
        activity.SetAlerted(false);
        Require(activity.Revision == 23 && alertRevision == 14 && !activity.Alerted,
            "Cold activity lost the independent world revision contribution.");
        var missingAlert = new FalloutActorActivityState();
        missingAlert.BindAlerted(() => false, _ => throw new InvalidDataException("Restore wrote a missing shared alert."), () => 0);
        Reject(() => missingAlert.Restore(flags));
        Console.WriteLine("OPENNV_PENDING_PACKAGE_SELECTION_CONTRACT_PASS selectedAndNull=true sourcePriorityHash=true noPredicateReplay=true consumedRetirement=true rngClockActivity=true sourceErrorsRetained=true mixedPoseRefused=true atomicSourceDrift=true native=unverified");
    }

    private static void AtomicReject(FalloutPluginStack records, FalloutReferenceSnapshot[] states)
    {
        using var rejected = new FalloutReferenceWorld(records);
        Reject(() => rejected.Restore(states));
        Require(rejected.InstanceCount == 0, "Rejected pending selection mutated the shared world.");
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid pending package state was admitted.");
    }
}
