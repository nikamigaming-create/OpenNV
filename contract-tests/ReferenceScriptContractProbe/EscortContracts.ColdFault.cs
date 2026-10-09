using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        instance.PackageMotion = motion with
        {
            Package = package.FormKey,
            PackageSha256 = Convert.ToHexString(SHA256.HashData(package.ReadData())),
            Escort = new(true, true)
        };
        var pendingSave = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using (var pendingCold = new FalloutReferenceWorld(records))
        {
            pendingCold.Restore(pendingSave); pendingCold.LoadCell(cell);
            var pendingScripts = new FalloutReferenceScripts(records, pendingCold, quests, new((_, _) => false,
                _ => throw new InvalidOperationException("Cold pending package fault replayed a native effect.")));
            var pendingReceipt = pendingCold.PackageEvents.SnapshotPending(actor);
            Require(pendingReceipt.Count == 1 && pendingCold.ActorValue(actor, "Variable05") == 1 &&
                pendingScripts.DispatchFrame(actor, pendingReceipt.Events, 0).All(result => result.Error == instance.ScriptError),
                "Cold pending package marks lost their retained fault or replayed its applied prefix.");
            pendingCold.PackageEvents.Consume(pendingReceipt);
            Require(pendingCold.PendingPackageEventCount == 0 && pendingCold.ActorValue(actor, "Variable05") == 1,
                "Consumed cold pending fault retained an event or repeated its prefix.");
        }
        var receipt = world.PackageEvents.SnapshotPending(actor);
        Require(scripts.DispatchFrame(actor, receipt.Events, 0).All(result => result.Error == instance.ScriptError),
            "Attached-event admission discarded an unscripted actor's package fault.");
        world.PackageEvents.Consume(receipt);
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved); cold.LoadCell(cell);
        var restored = cold.Get(actor);
        Require(restored.ScriptError == instance.ScriptError && restored.PackageMotion?.Escort is { TargetAcquired: true, Complete: true } &&
            cold.PendingPackageEventCount == 0 && cold.ActorValue(actor, "Variable05") == 1,
            "Cold completed escort erased its package fault, prefix or consumed event state.");
        var coldScripts = new FalloutReferenceScripts(records, cold, quests, new((_, _) => false,
            _ => throw new InvalidOperationException("Cold package completion replayed a native effect.")));
        var replayed = 0;
        var lifecycle = new FalloutPackageEvents((source, kind) =>
        {
            replayed++;
            if (source.EventPrograms.GetValueOrDefault(kind) is { } program) coldScripts.ExecutePackageEvent(program, actor);
        });
        lifecycle.Restore(declaration, restored.PackageMotion!.Escort!.Complete);
        lifecycle.Complete(); lifecycle.Complete();
        Require(replayed == 0 && cold.ActorValue(actor, "Variable05") == 1 &&
            coldScripts.Dispatch(actor, "GameMode").Error == instance.ScriptError,
            "Cold completed escort replayed its failed prefix or released its retained fault.");

        foreach (var kind in new[] { "POBA", "POCA", "POEA" })
        {
            var fault = $"Package {kind} {package.FormKey}: retained source failure";
            using var phase = new FalloutReferenceWorld(records);
            phase.Restore(saved.Select(snapshot => snapshot with { ScriptError = fault }).ToArray());
            Require(phase.Get(actor).ScriptError == fault, "Cold restore erased a package lifecycle phase's fault.");
        }
        foreach (var legacy in new[] { "Parse: unsupported source token", "OnActivate: transient native default action", "Package lookup: legacy native action" })
        {
            using var recovered = new FalloutReferenceWorld(records);
            recovered.Restore(saved.Select(snapshot => snapshot with { ScriptError = legacy }).ToArray());
            Require(recovered.Get(actor).ScriptError is null && recovered.ActorValue(actor, "Variable05") == 1,
                "Package fault retention disabled unrelated legacy no-script recovery or reset its actor values.");
        }
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
