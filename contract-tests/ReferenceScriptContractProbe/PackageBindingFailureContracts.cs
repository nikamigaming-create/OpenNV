using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PackageBindingFailureContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Actors.esm", id);
        using var world = new FalloutReferenceWorld(records);
        var actor = world.Get(Key(0x90c));
        var effects = new List<string>();
        var events = new FalloutPackageEvents((_, kind) => effects.Add(kind));
        events.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f5))));
        events.Change(null);
        var retirement = FalloutPackageRetirement.Capture(records, events);
        var package = records.GetEffective(Key(0x8f6));
        var failure = new FalloutActorPackageBindingFailure(package.FormKey,
            Convert.ToHexString(SHA256.HashData(package.ReadData())), "Stopped before package begin.",
            [1, 2, 3], [1, 0, 0, 0, 1, 0, 0, 0, 1], true, 0x123456789abcdef0, 3.25,
            new FalloutScheduleTime(1, 10, 3, 9), retirement);
        actor.Animation.Change("meshes/synthetic/mtforward.kf", new string('a', 64));
        actor.Animation.Advance(6.125);
        actor.ProcedureCaptureBlocker = failure.Error;
        Check(world.PendingProcedureCaptureCount == 1, "Unowned initialization did not block saving.");
        actor.CanCapturePackageBindingFailure = () => true;
        actor.CapturePackageBindingFailure = () => failure.Copy();
        Check(world.PendingProcedureCaptureCount == 0 && world.StoppedPackageBindingCount == 1,
            "Stopped binding did not retain a separately visible save owner.");
        var captured = world.Capture();
        FalloutReferenceSnapshot.Validate(captured);
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(captured))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        var resumed = cold.Get(actor.Reference);
        var retained = resumed.PackageBindingFailure!;
        Check(retained.Package == package.FormKey && retained.Error == failure.Error &&
            retained.Position.SequenceEqual(failure.Position) && retained.Basis.SequenceEqual(failure.Basis) &&
            retained.AiRandomState == failure.AiRandomState && retained.PollRemaining == 3.25 &&
            retained.ScheduleTime == failure.ScheduleTime && resumed.Animation.ElapsedSeconds == 6.125 &&
            cold.StoppedPackageBindingCount == 1 && cold.PendingProcedureCaptureCount == 0,
            "Cold stopped binding lost its source, fault, pose, random, poll or animation continuation.");
        var lifecycle = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Cold restore replayed source effects."));
        lifecycle.RestoreRetirement(retained.Retirement);
        Check(lifecycle.Active is null && !lifecycle.Done && lifecycle.Revision == events.Revision &&
            lifecycle.LastEvent == "POCA" && lifecycle.LastPackage == Key(0x8f5) && effects.SequenceEqual(["POBA", "POCA"]),
            "Cold predecessor retirement restarted a package or replayed its consumed effects.");
        cold.UnloadedPackages = new(records, cold, new(records), null, null,
            (_, _) => throw new InvalidDataException("Stopped unloaded query ran package results."), () => 1);
        Check(cold.CurrentPackage(actor.Reference) == package.FormKey && cold.PendingPackageEventCount == 0,
            "Unloaded stopped binding lost source selection or began the failed package.");
        foreach (var invalid in new[]
        {
            failure with { PackageSha256 = new string('0', 64) },
            failure with { Retirement = retirement with { LastPackageSha256 = new string('0', 64) } },
            failure with { Position = [float.NaN, 2, 3] },
            failure with { Basis = new float[9] },
            failure with { Basis = [1, 0, 0, 0, 1, 0, 0, 1, 1] },
            failure with { PollRemaining = -1 },
            failure with { ScheduleTime = new(1, 10, 3, 24) },
            failure with { Retirement = retirement with { LastEvent = "POBA" } }
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(saved.Select(value => value with { PackageBindingFailure = invalid }).ToArray()));
            Check(rejected.Capture().Count == 0, "Invalid stopped binding partially mutated the cold world.");
        }
        using var nonNpc = new FalloutReferenceWorld(records);
        Reject(() => failure.Validate(records, nonNpc.Get(Key(0x90b))));
        Reject(() => FalloutReferenceSnapshot.Validate(saved.Select(value => value with
        { PackageAssignment = new(package.FormKey, failure.PackageSha256, false) }).ToArray()));
        Reject(() => FalloutReferenceSnapshot.Validate(saved.Select(value => value with { Animation = null }).ToArray()));
        Reject(() => lifecycle.RestoreRetirement(retirement));
        events.Change(FalloutScriptPackage.Read(package));
        Reject(() => FalloutPackageRetirement.Capture(records, events));
        actor.CanCapturePackageBindingFailure = () => false;
        Check(world.PendingProcedureCaptureCount == 1, "Active continuation was admitted as a stopped failure.");
        Reject(() => world.Capture());
        Console.WriteLine("OPENNV_PACKAGE_BINDING_FAILURE_CONTRACT_PASS faultVisible=true poseClockRandomPoll=true consumedRetirement=true noBeginReplay=true unloadedIdentity=true sourceDriftAtomic=true activeContinuationRefused=true fixture=synthetic-stopped-initialization");
    }

    private static void Check(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Invalid stopped package binding was admitted.");
    }
}
