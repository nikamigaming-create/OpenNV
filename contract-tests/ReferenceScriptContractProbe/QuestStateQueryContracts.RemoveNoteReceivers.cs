using System.Text.Json;

internal static partial class QuestStateQueryContracts
{
    internal static void RunRemoveNoteReceivers(string? selected = null)
    {
        RunNoteQueryReceivers(selected);
        foreach (var shared in selected == "fallback" ? new[] { false } : selected == "shared" ? new[] { true } : new[] { false, true })
        {
            foreach (var receiver in new[] { "", "player.", "PlayerRef.", "PlacedReceiver." })
            {
                var body = "set prefix to prefix + 1\n" + receiver + "RemoveNote CollectedNote\nset result to GetHasNote CollectedNote\nset suffix to suffix + 1";
                using var live = new Fixture(body, shared, allowNoteEffects: true);
                live.World.PlayerNotes.Restore(new([live.Note, live.Note]));
                live.Tick(); live.Expect(1, 1);
                Require(live.World.PlayerNotes.Notes.SequenceEqual(new[] { live.Note }), "Qualified removal did not remove the first identity only.");
                using var cold = new Fixture(body, shared, initialize: false, allowNoteEffects: true);
                cold.World.PlayerNotes.Restore(RoundTrip(live.World.PlayerNotes.Capture()));
                cold.Quests.Restore(RoundTrip(live.Quests.Capture().ToArray()));
                cold.Scripts.Restore(RoundTrip(live.Scripts.Capture()));
                cold.Tick(); cold.Expect(0, 2);
                var revision = cold.World.PlayerNotes.Revision;
                cold.Tick(); cold.Expect(0, 3);
                Require(cold.World.PlayerNotes.Revision == revision && cold.Inventory.Items.Count == 0 &&
                    cold.Inventory.Notifications.Capture().Pending.Count == 0, "Absent qualified removal mutated unrelated state or feedback.");
            }
            foreach (var request in new[] { "MissingReceiver.RemoveNote CollectedNote", "BadGlobal.RemoveNote CollectedNote",
                "CollectedNote.RemoveNote CollectedNote", "player.RemoveNote BadGlobal", "player.RemoveNote MissingNote",
                "player.extra.RemoveNote CollectedNote", "player.RemoveNote", "player.RemoveNote CollectedNote 1" })
                Refused(request);
            Refused("player.RemoveNote CollectedNote", bindPlayer: false);
            Refused("player.RemoveNote CollectedNote", bindNote: false);
            using var skipped = new Fixture("if 0\nMissingReceiver.RemoveNote MissingNote\nendif\nset suffix to 1", shared, allowNoteEffects: true);
            skipped.World.PlayerNotes.Restore(null); skipped.Tick();
            Require(skipped.Error is null && skipped.Value(3) == 1 && !skipped.World.PlayerNotes.Known,
                "Unselected qualified removal resolved bindings or unknown membership.");
            using var unknown = new Fixture("player.RemoveNote CollectedNote\nset suffix to 1", shared, allowNoteEffects: true);
            unknown.World.PlayerNotes.Restore(null); unknown.Tick();
            Require(unknown.Error?.Contains("no authoritative collected-note membership", StringComparison.Ordinal) == true &&
                unknown.Value(3) == 0 && !unknown.World.PlayerNotes.Known, "Qualified removal inferred unknown membership.");
            using var failed = new Fixture("set prefix to prefix + 1\nplayer.RemoveNote CollectedNote\nUnknownAfterRemoval\nset suffix to 1", shared, allowNoteEffects: true);
            failed.World.PlayerNotes.Restore(new([failed.Note, failed.Note])); failed.Tick();
            Require(failed.Error is not null && failed.Value(1) == 1 && failed.Value(3) == 0 && failed.World.PlayerNotes.Notes.Count == 1,
                "Later failure did not retain the qualified removal prefix.");
            var stopped = JsonSerializer.Serialize(failed.Scripts.Capture());
            failed.Tick();
            Require(JsonSerializer.Serialize(failed.Scripts.Capture()) == stopped && failed.World.PlayerNotes.Notes.Count == 1,
                "Warm retry replayed a qualified removal.");
            using var failedCold = new Fixture(failed.Body, shared, initialize: false, allowNoteEffects: true);
            failedCold.World.PlayerNotes.Restore(RoundTrip(failed.World.PlayerNotes.Capture()));
            failedCold.Quests.Restore(RoundTrip(failed.Quests.Capture().ToArray()));
            failedCold.Scripts.Restore(RoundTrip(failed.Scripts.Capture())); failedCold.Tick();
            Require(JsonSerializer.Serialize(failedCold.Scripts.Capture()) == stopped && failedCold.World.PlayerNotes.Notes.Count == 1,
                "Cold retry cleared the retained failure or replayed removal.");

            void Refused(string request, bool bindPlayer = true, bool bindNote = true)
            {
                using var fixture = new Fixture("set prefix to 1\n" + request + "\nset suffix to 1", shared,
                    bindNote: bindNote, allowNoteEffects: true, bindPlayer: bindPlayer);
                fixture.World.PlayerNotes.AddMembership(fixture.Note); fixture.Tick();
                Require(fixture.Error is not null && fixture.Value(1) == 1 && fixture.Value(3) == 0 && fixture.World.PlayerNotes.Contains(fixture.Note),
                    "Bad receiver/NOTE removal mutated membership or ran the suffix.");
            }
        }
        Console.WriteLine($"OPENNV_REMOVE_NOTE_RECEIVER_PASS executors={selected ?? "fallback-and-shared"} staticCompiled=true firstIdentity=true absentAndUnknown=true invalidBindingsRefused=true shortCircuit=true warmCold=true retainedPrefix=true gameplay=unverified");
    }

    internal static void RunNoteQueryReceivers(string? selected = null)
    {
        foreach (var shared in selected == "fallback" ? new[] { false } : selected == "shared" ? new[] { true } : new[] { false, true })
        {
            foreach (var receiver in new[] { "player", "PlayerRef", "PlacedReceiver" })
            {
                var body = $"set prefix to prefix + 1\nset result to ( {receiver}.GetHasNote CollectedNote )\nset suffix to suffix + 1";
                using var live = new Fixture(body, shared);
                live.Tick(); live.Expect(0, 1);
                live.World.PlayerNotes.AddMembership(live.Note);
                var revision = live.World.PlayerNotes.Revision;
                live.Tick(); live.Expect(1, 2);
                Require(live.World.PlayerNotes.Revision == revision && live.Inventory.Items.Count == 0 &&
                    live.Inventory.Notifications.Capture().Pending.Count == 0, "Qualified query mutated membership/inventory/HUD.");
                using var cold = new Fixture(body, shared, initialize: false);
                cold.World.PlayerNotes.Restore(RoundTrip(live.World.PlayerNotes.Capture()));
                cold.Quests.Restore(RoundTrip(live.Quests.Capture().ToArray()));
                cold.Scripts.Restore(RoundTrip(live.Scripts.Capture())); cold.Tick(); cold.Expect(1, 3);
            }
            foreach (var request in new[] { "MissingReceiver.GetHasNote CollectedNote", "BadGlobal.GetHasNote CollectedNote",
                "CollectedNote.GetHasNote CollectedNote", "player.GetHasNote BadGlobal", "player.GetHasNote MissingNote",
                "player.extra.GetHasNote CollectedNote", "prefix.GetHasNote CollectedNote" })
            {
                using var invalid = new Fixture("set prefix to 1\nset result to ( " + request + " )\nset suffix to 1", shared);
                invalid.World.PlayerNotes.AddMembership(invalid.Note); invalid.Tick();
                Require(invalid.Error is not null && invalid.Value(3) == 0 && invalid.World.PlayerNotes.Notes.Count == 1,
                    "Bad/static-unbound query bypassed compiled identity or changed membership.");
            }
            foreach (var binding in new[] { "player", "note" })
            {
                using var invalid = new Fixture("set result to player.GetHasNote CollectedNote\nset suffix to 1", shared,
                    bindPlayer: binding != "player", bindNote: binding != "note"); invalid.Tick();
                Require(invalid.Error is not null && invalid.Value(3) == 0, "Qualified query bypassed absent SCRO binding.");
            }
            using var skipped = new Fixture("if 0 && MissingReceiver.GetHasNote MissingNote\nset result to 99\nendif\n" +
                "if 1 || BadGlobal.GetHasNote BadGlobal\nset suffix to 1\nendif", shared);
            skipped.World.PlayerNotes.Restore(null); skipped.Tick();
            Require(skipped.Error is null && skipped.Value(3) == 1 && !skipped.World.PlayerNotes.Known,
                "Short-circuited qualified query resolved receiver/NOTE or guessed membership.");
            using var unknown = new Fixture("set result to player.GetHasNote CollectedNote\nset suffix to 1", shared);
            unknown.World.PlayerNotes.Restore(null); unknown.Tick();
            Require(unknown.Error is not null && unknown.Value(3) == 0 && !unknown.World.PlayerNotes.Known,
                "Qualified query turned unknown history into false membership.");
        }
        Console.WriteLine($"OPENNV_NOTE_QUERY_RECEIVER_PASS executors={selected ?? "fallback-and-shared"} groupedExpressions=true staticCompiled=true globalMembership=true deferredBinding=true invalidAndUnknownRefused=true cold=true gameplay=unverified");
    }
}
