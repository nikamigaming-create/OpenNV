using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class EscortContracts
{
    private static void CheckColdPackageFault(FalloutPluginStack records, FalloutActorPackageMotion motion)
    {
        var actor = new FalloutFormKey("Base.esm", 0x90);
        var cell = FalloutCellSceneReader.Read(records, new("Base.esm", 0x80));
        var package = records.GetEffective(new("Base.esm", 9));
        var declaration = FalloutScriptPackage.Read(package);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        var instance = world.Get(actor);
        Require(instance.Script is null, "Package fault fixture unexpectedly has an attached actor script.");
        var quests = new FalloutQuestState(records);
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidOperationException("Package fault fixture unexpectedly emitted a native effect.")));
        world.PackageEvents.Mark(actor, package.FormKey, FalloutReferencePackageEventKind.Done);
        try
        {
            scripts.ExecutePackageEvent(declaration.EventPrograms["POEA"], actor);
            throw new InvalidOperationException("The unsupported package completion unexpectedly succeeded.");
        }
        catch (NotSupportedException error)
        {
            // This is the persisted PACK-result fault representation emitted
            // by the native actor owner, independent of an attached SCPT.
            instance.ScriptError = $"Package POEA {package.FormKey}: {error.Message}";
        }
        Require(world.ActorValue(actor, "Variable05") == 1, "Failed package completion lost its applied prefix.");
        var retainedFailure = world.Capture();
        using var partialCold = new FalloutReferenceWorld(records);
        partialCold.Restore(retainedFailure); partialCold.LoadCell(cell);
        var partialScripts = new FalloutReferenceScripts(records, partialCold, quests, new((_, _) => false,
            _ => throw new InvalidOperationException("Cold failed source result replayed a native effect.")));
        Require(partialCold.Get(actor).ScriptError == instance.ScriptError && partialCold.ActorValue(actor, "Variable05") == 1 &&
            partialScripts.Dispatch(actor, "GameMode").Error == instance.ScriptError,
            "Cold failure state erased its real prefix or retried the failed source owner.");

        instance.PackageMotion = motion with
        {
            Package = package.FormKey,
            PackageSha256 = Convert.ToHexString(SHA256.HashData(package.ReadData())),
            Escort = new(true, true)
        };
        MissingCompletedResult(() => world.Capture());
        using var refusedCold = new FalloutReferenceWorld(records);
        MissingCompletedResult(() => refusedCold.Restore([instance.Capture()]));
        Require(refusedCold.InstanceCount == 0 && world.ActorValue(actor, "Variable05") == 1,
            "Incomplete result authority changed a cold owner or erased a committed actor-value prefix.");
        var receipt = world.PackageEvents.SnapshotPending(actor);
        Require(scripts.DispatchFrame(actor, receipt.Events, 0).All(result => result.Error == instance.ScriptError),
            "A failed package result entered attached effects after its retained failure.");
        world.PackageEvents.Consume(receipt);
        MissingCompletedResult(() => world.Capture());
        Require(world.PendingPackageEventCount == 0 && world.ActorValue(actor, "Variable05") == 1,
            "Consuming the pending event repeated its prefix or manufactured a completed result receipt.");
    }

    private static void MissingCompletedResult(Action action)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message == "Consumed package result has no completed source-range execution receipt.")
        { return; }
        throw new InvalidDataException("An uncompleted source result was promoted to completed package authority.");
    }

    private static byte[] FaultFixture()
    {
        var source = Package(9, 200);
        // This fixture exercises the explicit source-diagnostic owner.
        // Dummy bytes cannot authorize a compiled package result prefix.
        var header = new byte[20];
        var result = Join(Field("POEA", []), Field("SCHR", header),
            Field("SCTX", Encoding.ASCII.GetBytes("ModAV Variable05 1\nUnsupportedPackageCommand\0")));
        var actorData = new byte[24]; actorData[8] = 1;
        var reference = Record("ACRE", 0x90, Field("NAME", BitConverter.GetBytes(0x81u)), Field("DATA", new byte[24]));
        var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BitConverter.GetBytes((uint)group.Length).CopyTo(group, 4); BitConverter.GetBytes(0x80u).CopyTo(group, 8);
        BitConverter.GetBytes(6u).CopyTo(group, 12); reference.CopyTo(group, 24);
        return Join(Record("PACK", 9, source[24..], result),
            Record("CREA", 0x81, Field("ACBS", actorData), Field("DATA", new byte[17])),
            Record("CELL", 0x80, Field("DATA", [1])), group);
    }
}
