using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void QueuedSelectionCold(RuntimeNativeNpc warm, FalloutReferenceWorld world, FalloutPluginStack records,
        RuntimeLiveContentSource content, FalloutCellScene cell, FalloutQuestState quests, FalloutGameTime clock,
        FalloutGlobalState globals, Node3D fixture, Func<FalloutPlacedReference, Transform3D> placement)
    {
        var caller = warm.Appearance.Reference!.Value;
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var original = snapshots.Single(value => value.Reference == caller);
        if (original.PendingPackageSelection is not { } pending || original.PackageAssignment is not null ||
            world.PendingProcedureCaptureCount != 0)
            throw new InvalidDataException("Queued source selection has no complete explicit capture receipt.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(snapshots); cold.RestoreActorOverrides(world.CaptureActorOverrides()); cold.LoadCell(cell);
        var placed = cell.References.Single(value => value.FormKey == caller);
        var units = warm.Skeleton.UnitsToMetres;
        var resumed = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
            cold.EquippedArmor(caller, 1, globals), cold.InitializeActorTemplates(caller, 1, globals));
        using var resumedLifetime = new PackageFixtureLifetime(resumed);
        resumed.Transform = placement(placed); fixture.AddChild(resumed);
        resumed.SetProcess(false); resumed.SetPhysicsProcess(false);
        resumed.ConfigureAi(records, quests, cell, placement, clock: clock, globals: globals, world: cold);
        var recaptured = cold.Get(caller).Capture();
        if (resumed.CurrentPackage != pending.Package || resumed.Transform != warm.Transform ||
            JsonSerializer.Serialize(recaptured) != JsonSerializer.Serialize(original))
            throw new InvalidDataException("Cold queued selection changed source, error, RNG, activity, pose, base clock or consumed prefix.");
        fixture.RemoveChild(resumed); cold.UnloadCell(cell.Cell.FormKey);
        var retained = cold.Get(caller);
        if (retained.CapturePendingPackageSelection is not null || retained.CanCapturePendingPackageSelection is not null ||
            retained.PendingPackageSelection is null || cold.PendingProcedureCaptureCount != 0 ||
            JsonSerializer.Serialize(retained.Capture()) != JsonSerializer.Serialize(original))
            throw new InvalidDataException("Child-first queued-selection retirement lost its native receipt or retained a delegate.");
        GD.Print($"OPENNV_NATIVE_PENDING_PACKAGE_SELECTION_PASS actor={caller} package={pending.Package} " +
            $"runtimeMvid={typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId} " +
            "winningPriorityAndPackage=true coldSelectedResult=true noPredicateRedraw=true activityAndClock=true " +
            "sourceErrorsAndPrefix=true childFirstRetirement=true nativeProceduresNotInvented=true component=true campaign=false recording=false");
    }

    private async Task QueuedCorpseCold(RuntimeNativeNpc warm, FalloutReferenceWorld world, FalloutPluginStack records,
        FalloutCellScene cell, Node3D fixture, Func<FalloutReferenceWorld, RuntimeNativeNpc> assemble,
        IReadOnlyList<FalloutActorOverrides>? overrides, IReadOnlyList<FalloutFactionRelationSnapshot>? factions)
    {
        var caller = warm.Appearance.Reference!.Value;
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var original = snapshots.Single(value => value.Reference == caller);
        if (original.Injury?.Dead != true || original.Ragdoll is not { } ragdoll ||
            original.PendingPackageSelection is not { } pending || world.PendingProcedureCaptureCount != 0)
            throw new InvalidDataException("Native corpse failed to compose with its unchanged queued source selection.");
        fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
        if (world.Get(caller).CapturePendingPackageSelection is not null || world.Get(caller).CanCapturePendingPackageSelection is not null ||
            JsonSerializer.Serialize(world.Capture()) != JsonSerializer.Serialize(snapshots))
            throw new InvalidDataException("Queued corpse retirement discarded its selection, source fault or physical state.");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
        cold.RestoreActorOverrides(overrides); cold.RestoreFactionRelations(factions); cold.LoadCell(cell);
        var resumed = assemble(cold); using var resumedLifetime = new PackageFixtureLifetime(resumed);
        if (cold.PendingProcedureCaptureCount == 0)
            throw new InvalidDataException("Cold queued corpse waived its deferred physical capture owner.");
        var initialized = new TaskCompletionSource(); Callable.From(() => initialized.SetResult()).CallDeferred();
        await initialized.Task;
        if (resumed.Combat?.StoppedAiPoseCaptureReady != true || cold.PendingProcedureCaptureCount != 0)
            throw new InvalidDataException("Queued corpse failed to acquire its original complete physical owner.");
        var actual = cold.Get(caller).Capture(); RequireCorpsePose(actual.Ragdoll!, ragdoll);
        if (JsonSerializer.Serialize(actual with { Ragdoll = ragdoll }) != JsonSerializer.Serialize(original))
            throw new InvalidDataException("Cold queued corpse changed its selected result, stream, activity, clock, source faults or history.");
        foreach (var delta in new[] { 0d, .125, .25 })
        {
            resumed._Process(delta); resumed.Combat!._PhysicsProcess(delta);
            var after = cold.Get(caller).Capture();
            if (JsonSerializer.Serialize(after with { Ragdoll = ragdoll }) != JsonSerializer.Serialize(original))
                throw new InvalidDataException("Dead queued selection rebound, redrew or replayed source effects.");
        }
        GD.Print($"OPENNV_NATIVE_PENDING_CORPSE_SELECTION_PASS actor={caller} selected={pending.Package} bodies={ragdoll.Bodies.Count} " +
            $"runtimeMvid={typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId} " +
            "selectedResultAndSourceFault=true rngActivityClock=true childFirstRetirement=true completeNativeBodies=true " +
            "deferredCaptureGuard=true coldNoRebind=true componentDamageDeath=true wholeMovingCombatAndCorpseAttachments=unowned campaign=false recording=false");
    }
}
