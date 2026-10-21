using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class GrabEventContracts
{
    internal static void Run()
    {
        var key = new FalloutFormKey("Grab.esm", 1);
        var reference = new FalloutReferenceSnapshot(key, new("Grab.esm", 2), new("Grab.esm", 3),
            null, null, new Dictionary<uint, double>(), null, GrabEvents: new(key, 3, 1, 2));
        FalloutReferenceSnapshot.Validate([reference]);
        FalloutReferenceSnapshot.Validate([reference with { GrabEvents = new(key, 3, 0, 0) }]);
        foreach (var invalid in new FalloutReferenceGrabEvents[] { new(key, 0, 0, 0), new(key, 2, -1, 0), new(key, 2, 3, 0), new(key, 2, 1, 1), new(key, long.MaxValue, 0, 0), new(new("Grab.esm", 4), 2, 1, 0) })
        {
            var refused = false;
            try { FalloutReferenceSnapshot.Validate([reference with { GrabEvents = invalid }]); }
            catch (InvalidDataException) { refused = true; }
            Require(refused, "Invalid pending grab/release marks entered saved reference state.");
        }
        // This fixture exercises only schema ownership, not campaign admission.
        var previous = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(new
        {
            Schema = FalloutNativeCampaignSave.BeforeGrabEventsSchema,
            References = new[] { reference with { GrabEvents = null } },
        }))!;
        var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous);
        Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema && ReferenceEquals(upgraded.References, previous.References),
            "Upgrading the preceding save schema changed its retained reference owners.");
        var foreign = previous with { References = [reference] };
        var rejected = false;
        try { _ = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(foreign); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "An earlier campaign schema admitted new grab/release owners.");
        var current = foreign with { Schema = FalloutNativeCampaignSave.ExpectedSchema };
        Require(ReferenceEquals(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current), current),
            "Current grab/release state was replaced during schema admission.");
        Console.WriteLine("Grab/release save contracts: pending/consumed revisions, malformed marks, prior-schema preservation and foreign-owner refusal PASS.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static void VerifyDispatch(FalloutPluginStack records, FalloutCellScene cell)
    {
        var reference = new FalloutFormKey("Base.esm", 0x923);
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        world.MarkPlayerGrab(reference, true); world.MarkPlayerGrab(reference, false);
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
        var scripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false, _ => { }));
        var batch = cold.Get(reference).GrabEvents!;
        var foreignRefused = false;
        try { cold.ConsumeGrabEvents(reference, batch with { Reference = new("Base.esm", 0x900) }); }
        catch (InvalidDataException) { foreignRefused = true; }
        Require(foreignRefused && cold.Get(reference).GrabEvents == batch, "A foreign event batch consumed pending grab/release marks.");
        var results = scripts.DispatchFrame(reference,
            [new("OnRelease", new("Base.esm", 0x14)), new("OnGrab", new("Base.esm", 0x14)), new("GameMode")], 0);
        Require(results.All(result => result.Error is null && result.Blocks == 1) &&
            cold.Get(reference).Read(1) == 10 && cold.Get(reference).Read(2) == 110,
            "Grab/release delivery did not execute actual script effects in declaration order.");
        cold.ConsumeGrabEvents(reference, batch);
        _ = scripts.Dispatch(reference, "GameMode");
        Require(cold.Get(reference).Read(1) == 10 && cold.Get(reference).Read(2) == 10 && !cold.Get(reference).GrabEvents!.HasPending,
            "Consumed grab/release effects replayed on a later script frame.");
        cold.UnloadCell(cell.Cell.FormKey);
        cold.MarkPlayerGrab(reference, false);
        Require(cold.Get(reference).GrabEvents!.Release > 0, "Unloaded presentation discarded a physical release mark.");
        Console.WriteLine("Grab/release script contracts: cold pending marks, declaration-order effects, consumed-prefix isolation and unloaded release retention PASS.");
    }
}
