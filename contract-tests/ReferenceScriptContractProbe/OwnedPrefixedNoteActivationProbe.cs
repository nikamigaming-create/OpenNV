using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPrefixedNoteActivationProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = records.GetEffective(new("FalloutNV.esm", 0xe44ae));
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, reference) ?? throw new InvalidDataException("Owned activation has no script.");
        Require(reference.Signature == "REFR" && basis.Signature == "MISC" && basis.FormKey == new FalloutFormKey("FalloutNV.esm", 0xe44aa) &&
            script.FormKey == new FalloutFormKey("FalloutNV.esm", 0xe44b6), "Selected source chain changed.");
        var fields = script.ReadSubrecords().ToArray();
        Require(Hash(script.ReadData()) == "493E9E27257FF4407B2ABA91B5B3D0AB228868C978BD5030416218A386D99FF8" &&
            Hash(fields.Single(value => value.Signature == "SCTX").Data.Span) == "47A7F2BB6D61F5BFAB6E0F9A49331F4120CB22779EC9C58B848C04FE5CEF1274" &&
            Hash(fields.Single(value => value.Signature == "SCDA").Data.Span) == "78C1503879D11022EACA3F8218943D62F99F0EF3FF5BCCF8DB8754B210735E23",
            "Actual complete source/compiled activation differs from the qualified record.");
        var bindings = new FalloutScriptBindings(records, reference, script, fields);
        var player = bindings.Reference("player");
        var note = bindings.Form("HVRangerNote").FormKey;
        var firstTrap = bindings.Reference("HVRangerGrenadeTrap1");
        Require(records.GetEffective(note).Signature == "NOTE", "Actual compiled note binding is not NOTE.");
        var originals = new[] { reference, basis, script, records.GetEffective(note), records.GetEffective(firstTrap) }
            .Select(value => (Record: value, Hash: Hash(value.ReadData()))).ToArray();
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, world.Get(reference.FormKey).Cell);
        cell = cell with { References = cell.References.Where(value => value.FormKey == reference.FormKey).ToArray() };
        Require(cell.References.Count == 1, "Actual CELL lacks the selected source reference.");
        world.LoadCell(cell);
        var inventory = new FalloutPlayerInventory();
        var attempts = new List<FalloutReferenceScriptEffect>();
        const string boundary = "Controlled fixture has no native trap activation/event host.";
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, effect =>
        {
            attempts.Add(effect);
            if (effect.Kind == FalloutReferenceEffectKind.AddNote)
                world.AcquirePlayerNote(effect.Target!.Value, inventory.Notifications);
            else if (effect.Kind == FalloutReferenceEffectKind.DefaultActivate)
                throw new NotSupportedException(boundary);
            else throw new InvalidDataException("Unexpected effect before the native trap boundary.");
        }));
        var before = world.Get(reference.FormKey).DeletePending;
        var result = scripts.Activate(reference.FormKey, player);
        Require(result.Error == "onActivate: " + boundary && result.Blocks == 0 &&
            world.PlayerNotes.Contains(note) && inventory.Notifications.Capture().Pending.Count == 1 && inventory.Items.Count == 0,
            result.Error ?? "Owned activation did not preserve its completed note prefix and exact next boundary.");
        Require(attempts.Count == 2 && attempts[0].Kind == FalloutReferenceEffectKind.AddNote && attempts[0].Target == note &&
            attempts[1].Kind == FalloutReferenceEffectKind.DefaultActivate && attempts[1].Target == firstTrap &&
            attempts[1].Argument == player && world.Get(reference.FormKey).DeletePending == before,
            "Authored trap order/receiver changed or unexecuted retirement tail ran.");
        var saved = JsonSerializer.Serialize(world.Capture());
        var notice = JsonSerializer.Serialize(inventory.Notifications.Capture());
        Require(scripts.Activate(reference.FormKey, player).Error == result.Error && attempts.Count == 2 &&
            JsonSerializer.Serialize(world.Capture()) == saved && JsonSerializer.Serialize(inventory.Notifications.Capture()) == notice,
            "Warm activation replayed the consumed prefix or cleared its fault.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!);
        cold.PlayerNotes.Restore(JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(JsonSerializer.Serialize(world.PlayerNotes.Capture()))!);
        cold.LoadCell(cell);
        var coldScripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Retained cold activation replayed an effect.")));
        Require(coldScripts.Activate(reference.FormKey, player).Error == result.Error && cold.PlayerNotes.Contains(note) &&
            JsonSerializer.Serialize(cold.Capture()) == saved, "Cold activation lost membership/fault or replayed the authored tail.");
        Require(originals.All(value => value.Hash == Hash(value.Record.ReadData())), "Owned source changed.");
        Console.WriteLine($"OPENNV_OWNED_PREFIXED_NOTE_PASS actualOnActivate=true completeSourceUnchanged=true note={note} " +
            $"nextTrap={firstTrap} nativeTrapHost=fixture-unbound warmColdRetained=true noReplay=true " +
            "trapOutcomesRetirementOrdinaryPickupCampaignAndRetail=unverified");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
