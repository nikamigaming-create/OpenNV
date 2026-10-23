using System.Text.Json;

internal static partial class QuestStateQueryContracts
{
    internal static void RunNoteReceivers(string? selected = null)
    {
        foreach (var shared in selected == "fallback" ? new[] { false } : selected == "shared" ? new[] { true } : new[] { false, true })
        {
            foreach (var receiver in new[] { "", "player.", "PlayerRef.", "PlacedReceiver." })
            {
                var body = "set prefix to prefix + 1\n" + receiver + "AddNote CollectedNote\nset result to GetHasNote CollectedNote\nset suffix to suffix + 1";
                using var live = new Fixture(body, shared, allowNoteEffects: true);
                live.Tick(); live.Expect(1, 1);
                live.Tick(); live.Expect(1, 2);
                Require(live.World.PlayerNotes.Notes.Count == 1 && live.Inventory.Items.Count == 0 &&
                    live.Inventory.Notifications.Capture().Pending.Count == 1, "Receiver redirected acquisition or replayed duplicate feedback.");
                using var cold = new Fixture(body, shared, initialize: false, allowNoteEffects: true);
                cold.World.PlayerNotes.Restore(RoundTrip(live.World.PlayerNotes.Capture()));
                cold.Quests.Restore(RoundTrip(live.Quests.Capture().ToArray()));
                cold.Scripts.Restore(RoundTrip(live.Scripts.Capture()));
                var notice = JsonSerializer.Serialize(cold.Inventory.Notifications.Capture());
                cold.Tick(); cold.Expect(1, 3);
                Require(cold.World.PlayerNotes.Notes.Count == 1 && JsonSerializer.Serialize(cold.Inventory.Notifications.Capture()) == notice,
                    "Cold duplicate acquisition changed membership or emitted feedback.");
            }
            foreach (var request in new[] { "MissingReceiver.AddNote CollectedNote", "BadGlobal.AddNote CollectedNote",
                "CollectedNote.AddNote CollectedNote", "player.AddNote BadGlobal", "player.AddNote MissingNote", "player.extra.AddNote CollectedNote",
                "player.AddNote", "player.AddNote CollectedNote CollectedNote" })
                Failed(request, bindPlayer: true, bindNote: true);
            Failed("player.AddNote CollectedNote", bindPlayer: false, bindNote: true);
            Failed("player.AddNote CollectedNote", bindPlayer: true, bindNote: false);
            using var skipped = new Fixture("if 0\nMissingReceiver.AddNote MissingNote\nendif\nset suffix to 1", shared, allowNoteEffects: true);
            skipped.Tick();
            Require(skipped.Error is null && skipped.Value(3) == 1 && skipped.World.PlayerNotes.Notes.Count == 0,
                "Unselected note command resolved receiver/NOTE bindings.");

            void Failed(string request, bool bindPlayer, bool bindNote)
            {
                var body = "set prefix to prefix + 1\n" + request + "\nset suffix to 1";
                using var live = new Fixture(body, shared, bindNote: bindNote, allowNoteEffects: true, bindPlayer: bindPlayer);
                try { live.Tick(); } catch (NotSupportedException) { }
                Require(live.Error is not null && live.Value(1) == 1 && live.Value(3) == 0 &&
                    live.World.PlayerNotes.Notes.Count == 0 && live.Inventory.Notifications.Capture().Pending.Count == 0,
                    "Invalid receiver/NOTE mutated acquisition or failed to retain the prefix.");
                var state = JsonSerializer.Serialize(live.Quests.Capture());
                var error = live.Error;
                live.Tick();
                Require(live.Error == error && JsonSerializer.Serialize(live.Quests.Capture()) == state, "Retained receiver failure replayed prefix.");
                using var cold = new Fixture(body, shared, initialize: false, bindNote: bindNote, allowNoteEffects: true, bindPlayer: bindPlayer);
                cold.Quests.Restore(RoundTrip(live.Quests.Capture().ToArray()));
                cold.Scripts.Restore(RoundTrip(live.Scripts.Capture()));
                cold.Tick();
                Require(cold.Error == error && cold.Value(1) == 1 && cold.Value(3) == 0 && cold.World.PlayerNotes.Notes.Count == 0,
                    "Cold receiver failure replayed or cleared the consumed prefix.");
            }
        }
        Console.WriteLine($"OPENNV_NOTE_RECEIVER_CONTRACT_PASS executors={selected ?? "fallback-and-shared"} compiledReceivers=true globalPlayerEffect=true invalidBindingsRefused=true cold=true noReplay=true ordinaryActivation=unverified");
    }
}
