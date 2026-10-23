using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class QuestStateQueryContracts
{
    internal static void RunNoteRemoval(string? selected = null)
    {
        foreach (var shared in selected == "fallback" ? new[] { false } : selected == "shared" ? new[] { true } : new[] { false, true })
        {
            const string body = "set prefix to prefix + 1\nRemoveNote CollectedNote\n" +
                "set result to GetHasNote CollectedNote\nset suffix to suffix + 1";
            using var fixture = new Fixture(body, shared, allowNoteEffects: true);
            fixture.World.PlayerNotes.Restore(new([fixture.Note, fixture.Note]));
            fixture.Tick(); fixture.Expect(1, 1);
            Require(fixture.World.PlayerNotes.Notes.SequenceEqual(new[] { fixture.Note }), "RemoveNote did not consume exactly the first identity.");
            var callbackRan = false;
            fixture.World.RemovePlayerNote(new("questqueries.ESM", fixture.Note.ObjectId), identity =>
            {
                callbackRan = true;
                Require(identity == fixture.Note && fixture.World.PlayerNotes.Contains(fixture.Note),
                    "Selection invalidation did not receive canonical identity before membership removal.");
            });
            Require(callbackRan && !fixture.World.PlayerNotes.Contains(fixture.Note), "Canonical removal did not publish its list change.");
            fixture.World.PlayerNotes.AddMembership(fixture.Note);
            using var cold = new Fixture(body, shared, initialize: false, allowNoteEffects: true);
            cold.World.PlayerNotes.Restore(RoundTrip(fixture.World.PlayerNotes.Capture()));
            cold.Quests.Restore(RoundTrip(fixture.Quests.Capture().ToArray()));
            cold.Scripts.Restore(RoundTrip(fixture.Scripts.Capture()));
            cold.Tick(); cold.Expect(0, 2);
            var revision = cold.World.PlayerNotes.Revision;
            cold.Tick(); cold.Expect(0, 3);
            Require(cold.World.PlayerNotes.Revision == revision && cold.Inventory.Items.Count == 0 &&
                cold.Inventory.Notifications.Capture().Pending.Count == 0, "Absent removal changed membership, inventory or HUD feedback.");

            using var failed = new Fixture("set prefix to prefix + 1\nRemoveNote CollectedNote\nUnknownAfterRemoval\nset suffix to 1",
                shared, allowNoteEffects: true);
            failed.World.PlayerNotes.Restore(new([failed.Note, failed.Note]));
            failed.Tick();
            Require(failed.Error is not null && failed.Value(1) == 1 && failed.Value(3) == 0 && failed.World.PlayerNotes.Notes.Count == 1,
                "A later refusal lost the completed removal prefix.");
            var state = JsonSerializer.Serialize(failed.Scripts.Capture());
            failed.Tick();
            Require(JsonSerializer.Serialize(failed.Scripts.Capture()) == state && failed.World.PlayerNotes.Notes.Count == 1,
                "Retained failure replayed removal or cleared its source error.");
            using var failedCold = new Fixture(failed.Body, shared, initialize: false, allowNoteEffects: true);
            failedCold.World.PlayerNotes.Restore(RoundTrip(failed.World.PlayerNotes.Capture()));
            failedCold.Quests.Restore(RoundTrip(failed.Quests.Capture().ToArray()));
            failedCold.Scripts.Restore(RoundTrip(failed.Scripts.Capture()));
            failedCold.Tick();
            Require(JsonSerializer.Serialize(failedCold.Scripts.Capture()) == state && failedCold.World.PlayerNotes.Notes.Count == 1,
                "Cold retry replayed the removal prefix or changed its retained refusal.");

            using var unknown = new Fixture(body, shared, allowNoteEffects: true);
            unknown.World.PlayerNotes.Restore(null); unknown.Tick();
            Require(unknown.Error?.Contains("no authoritative collected-note membership", StringComparison.Ordinal) == true &&
                unknown.Value(1) == 1 && unknown.Value(3) == 0 && !unknown.World.PlayerNotes.Known,
                "Removal guessed legacy history or ran its suffix.");
            foreach (var target in new[] { "BadGlobal", "TargetQuest", "MissingNote", "", "CollectedNote 1" })
            {
                using var invalid = new Fixture("set prefix to 1\nRemoveNote " + target + "\nset suffix to 1", shared, allowNoteEffects: true);
                invalid.World.PlayerNotes.AddMembership(invalid.Note); invalid.Tick();
                Require(invalid.Error is not null && invalid.Value(3) == 0 && invalid.World.PlayerNotes.Contains(invalid.Note),
                    "Malformed removal mutated notes or executed the suffix.");
            }
            using var unbound = new Fixture(body, shared, bindNote: false, allowNoteEffects: true);
            unbound.Tick(); Require(unbound.Error is not null && unbound.Value(3) == 0, "Removal bypassed compiled NOTE binding.");
            using var skipped = new Fixture("if 0\nRemoveNote MissingNote\nendif\nset suffix to 1", shared, allowNoteEffects: true);
            skipped.World.PlayerNotes.Restore(null); skipped.Tick();
            Require(skipped.Error is null && skipped.Value(3) == 1 && !skipped.World.PlayerNotes.Known,
                "Dead removal reached an undeclared identity or unknown history.");
        }
        Console.WriteLine($"OPENNV_REMOVE_NOTE_CONTRACT_PASS executor={selected ?? "both"} firstIdentity=true absentNoOp=true " +
            "cold=true retainedError=true typedCompiledBinding=true hudUnchanged=true ordinaryGameplay=false retailParity=unverified");
    }
}
