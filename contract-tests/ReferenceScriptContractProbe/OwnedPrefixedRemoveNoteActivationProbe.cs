using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPrefixedRemoveNoteActivationProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = records.GetEffective(new("OldWorldBlues.esm", 0x13573));
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, reference) ?? throw new InvalidDataException("Owned jukebox has no script.");
        Require(reference.Signature == "REFR" && basis.Signature == "ACTI" && basis.FormKey == new FalloutFormKey("OldWorldBlues.esm", 0x13574) &&
            script.FormKey == new FalloutFormKey("OldWorldBlues.esm", 0x13575), "Owned placed jukebox source chain changed.");
        var fields = script.ReadSubrecords().ToArray();
        Require(Hash(script.ReadData()) == "B4AAF962DCA7516D4FEBBA2517255C645C67AA960A8FBE2F8C138FB7A1A35932" &&
            Hash(fields.Single(value => value.Signature == "SCTX").Data.Span) == "A3EF5E56BA8AB1E0D18C0820233E3C826466BDB267B70B75B004F171BA755947" &&
            Hash(fields.Single(value => value.Signature == "SCDA").Data.Span) == "55064873D9B8BEC087A5C82961D654E6B53A5AD1A98BC0B8A821F95E464E4889",
            "Complete owned script/bindings/source changed.");
        var bindings = new FalloutScriptBindings(records, reference, script, fields);
        var player = bindings.Reference("player");
        var note = bindings.Form("NVDLC03HQBuddyJukeboxNOTE").FormKey;
        var unlocked = bindings.Variable("NVDLC03DialogueHQBuddies.bJukeboxUnlocked");
        var jukebox = bindings.Variable("NVDLC03HQBuddy.iJuke");
        var source = FalloutDialogueTopic.ScriptText(fields.Single(value => value.Signature == "SCTX").Data.Span);
        var eventName = FalloutGameModeProgram.ReadEvents(source).Single(value => value.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase)).Event;
        var originals = new[] { reference, basis, script, records.GetEffective(note), records.GetEffective(unlocked.Owner), records.GetEffective(jukebox.Owner) }
            .Select(value => (Record: value, Hash: Hash(value.ReadData()))).ToArray();
        foreach (var count in new[] { 0, 1, 2 }) Exercise(count, player);
        Exercise(1, reference.FormKey);
        Require(originals.All(value => value.Hash == Hash(value.Record.ReadData())), "Owned records changed.");
        Console.WriteLine($"OPENNV_OWNED_PREFIXED_REMOVE_NOTE_PASS reference={reference.FormKey} note={note} fullOriginalOnActivate=true " +
            "compiledBindings=true sourceGates=true questPrefix=true missingAndDuplicate=true warmColdNoReplay=true " +
            "messageHost=fixture-unbound completeJukeboxPhysicalInputCampaignAndRetail=unverified");

        void Exercise(int count, FalloutFormKey actor)
        {
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(reference.FormKey).Cell);
            Require(cell.Cell.FormKey == new FalloutFormKey("OldWorldBlues.esm", 0x30af), "Actual placed jukebox CELL changed.");
            cell = cell with { References = cell.References.Where(value => value.FormKey == reference.FormKey).ToArray() };
            Require(cell.References.Count == 1, "Actual CELL lacks selected reference.");
            world.LoadCell(cell);
            Require(world.IsEnabled(reference.FormKey), "Selected jukebox is not initially enabled.");
            // Explicit component preconditions, never a campaign-save edit.
            world.PlayerNotes.Restore(new(Enumerable.Repeat(note, count).ToArray()));
            var quests = new FalloutQuestState(records);
            var attempts = new List<FalloutReferenceScriptEffect>();
            const string boundary = "Controlled fixture has no native jukebox message host.";
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                attempts.Add(effect);
                if (effect.Kind == FalloutReferenceEffectKind.RemoveNote) world.RemovePlayerNote(effect.Target!.Value);
                else if (effect.Kind == FalloutReferenceEffectKind.Message) throw new NotSupportedException(boundary);
                else throw new InvalidDataException("Unexpected effect before owned message boundary.");
            }));
            var result = scripts.Activate(reference.FormKey, actor);
            if (actor != player)
            {
                Require(result.Error is null && result.Blocks == 1 && attempts.Count == 0 && world.PlayerNotes.Notes.Count == count && quests.Capture().Count == 0,
                    "Original actor guard executed a player-only branch.");
                return;
            }
            var removed = count > 0;
            Require(result.Error == eventName + ": " + boundary && result.Blocks == 0 && attempts.Count == (removed ? 2 : 1) &&
                attempts[^1].Kind == FalloutReferenceEffectKind.Message && (!removed || attempts[0].Kind == FalloutReferenceEffectKind.RemoveNote && attempts[0].Target == note) &&
                world.PlayerNotes.Notes.Count == Math.Max(0, count - 1) && quests.Variable(unlocked.Owner, unlocked.Index) == (removed ? 1 : 0) &&
                quests.Variable(jukebox.Owner, jukebox.Index) == (removed ? 1 : 0) && !world.Get(reference.FormKey).Destroyed,
                result.Error ?? "Original removal branch/prefix/tail boundary changed.");
            var saved = JsonSerializer.Serialize(world.Capture());
            var questState = JsonSerializer.Serialize(quests.Capture());
            var notes = JsonSerializer.Serialize(world.PlayerNotes.Capture());
            var attemptCount = attempts.Count;
            Require(scripts.Activate(reference.FormKey, player).Error == result.Error && attempts.Count == attemptCount &&
                JsonSerializer.Serialize(world.Capture()) == saved && JsonSerializer.Serialize(quests.Capture()) == questState &&
                JsonSerializer.Serialize(world.PlayerNotes.Capture()) == notes, "Warm retry replayed source prefix/removal.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!);
            cold.PlayerNotes.Restore(JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(notes));
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(questState)!);
            cold.LoadCell(cell);
            var coldScripts = new FalloutReferenceScripts(records, cold, coldQuests, new((_, _) => false,
                _ => throw new InvalidDataException("Cold retained invocation replayed an effect.")));
            Require(coldScripts.Activate(reference.FormKey, player).Error == result.Error && JsonSerializer.Serialize(cold.Capture()) == saved &&
                JsonSerializer.Serialize(coldQuests.Capture()) == questState && JsonSerializer.Serialize(cold.PlayerNotes.Capture()) == notes,
                "Cold retry cleared/replayed the failure or consumed a duplicate identity.");
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
