using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class ConversationContracts
{
    // Full-reader authored INFO contracts. The callbacks below are test ledgers,
    // never claimed as retail script execution or campaign quest progress.
    private static void ResultOnlyInfoContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-result-only-info-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory, "Dialogue.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Record("QUST", 0x700, Field("DATA", [1, 10])), Record("NPC_", 0x900, Field("FULL", Text("Authored speaker"))),
                Topic(0x800, "Silent entry", Info(0x910, 0x700, 4, 0, Field("NAME", U32(0x802)),
                    Field("TCLT", U32(0x802)), Field("TCLT", U32(0x803)),
                    Field("SCTX", Text("authored begin ledger")), Field("NEXT", []), Field("SCTX", Text("authored end ledger")))),
                Topic(0x801, "Filtered silent", Info(0x911, 0x700, 4, 0, Condition(72, 0x999, 1)), Info(0x912, 0x700, 1, 1)),
                Topic(0x802, "Silent finish", Info(0x920, 0x700, 1, 0)),
                TopLevel(0x803, "Spoken finish", 100, Info(0x930, 0x700, 1, 1)),
                Topic(0x804, "Linked silent", Info(0x940, 0x700, 0, 0, Field("NAME", U32(0x803)),
                    Field("TCFU", U32(0x941)), Field("TCFU", U32(0x942)))),
                Topic(0x805, "Link targets", Info(0x941, 0x700, 0, 0, Condition(72, 0x999, 1)),
                    Info(0x942, 0x700, 8, 0, Field("TCFU", U32(0x943)))),
                Topic(0x806, "Spoken link", Info(0x943, 0x700, 1, 1)),
                Topic(0x807, "Silent cycle", Info(0x950, 0x700, 0, 0, Condition(71, 0, 1), Field("TCFU", U32(0x951)))),
                Topic(0x808, "Cycle continuation", Info(0x951, 0x700, 0, 0, Field("TCFU", U32(0x950)), Field("TCFU", U32(0x952))),
                    Info(0x952, 0x700, 1, 0)),
                Topic(0x809, "Failed silent end", Info(0x960, 0x700, 4, 0, Field("NAME", U32(0x802)), Field("TCLT", U32(0x802)))),
                Topic(0x80a, "GOODBYE", 2, 5, [InfoOfType(1, 0x970, 0x700, 0, 0)], type: 1)));
            using var records = FalloutPluginStack.Load(directory, ["Dialogue.esm"]);
            var quests = new FalloutQuestState(records);
            var calls = new List<(uint Info, bool Begin)>(); var said = new HashSet<FalloutFormKey>();
            float Unknown(FalloutCondition condition) => throw new NotSupportedException("Unbound authored query " + condition.Function);
            FalloutConversation Create(Action<FalloutDialogueInfo, bool>? result = null, Func<FalloutCondition, float>? evaluate = null,
                Action? admission = null) => new(records, quests, evaluate ?? Unknown,
                    result ?? ((info, begin) => calls.Add((info.Record.FormKey.ObjectId, begin))), said, requireGameplayAdmission: admission);
            FalloutConversation entered = null!;
            entered = Create((info, begin) =>
            {
                calls.Add((info.Record.FormKey.ObjectId, begin));
                Require(said.Contains(info.Record.FormKey), "SayOnce was not committed before the real Begin/End callbacks.");
                Require(entered.AddedTopics.Count == 0, "AddedTopics committed before End returned.");
            });
            entered.Start(Key(0x900), Key(0x800));
            Require(entered.Phase == "choices" && entered.Response is null && entered.PendingResponse is null &&
                entered.ResponseIndex == 0 && entered.ResponseGeneration == 0 && entered.ResultOnlyCompletions == 1 &&
                calls.SequenceEqual([(0x910u, true), (0x910u, false)]) && entered.AddedTopics.SequenceEqual([Key(0x802)]) &&
                entered.Choices.Select(choice => choice.Topic).SequenceEqual([Key(0x802), Key(0x803)]),
                "Eligible result-only INFO lost effect/link order or invented a voice publication.");
            Reject(() => entered.EnterResponse()); Reject(() => entered.CompleteResponse());
            var filtered = Create(); calls.Clear(); filtered.Start(Key(0x900), Key(0x801));
            Require(filtered.Info?.Record.FormKey == Key(0x912) && !said.Contains(Key(0x911)) &&
                filtered.ResultOnlyCompletions == 0 && calls.SequenceEqual([(0x912u, true)]),
                "Filtered zero-response INFO mutated SayOnce/results or displaced its eligible fallback.");
            calls.Clear(); FalloutConversation linked = null!;
            linked = Create((info, begin) =>
            {
                calls.Add((info.Record.FormKey.ObjectId, begin));
                if (info.Record.FormKey == Key(0x942))
                    Require(linked.AddedTopics.Contains(Key(0x803)), "Previous AddedTopics did not commit before entering a follow-up.");
            });
            linked.Start(Key(0x900), Key(0x804));
            Require(linked.Phase == "speaking" && linked.Info?.Record.FormKey == Key(0x943) && linked.ResultOnlyCompletions == 2 &&
                linked.ResponseGeneration == 1 && calls.SequenceEqual([(0x940u, true), (0x940u, false),
                    (0x942u, true), (0x942u, false), (0x943u, true)]),
                "Silent follow-ups/immediate End did not reach the actual next spoken INFO exactly once.");
            linked.CompleteResponse(); Require(linked.Phase == "closed" && calls.Count == 6, "Voiced End replayed the silent link prefix.");
            calls.Clear(); var cycles = 0;
            var cycle = Create((info, begin) => { calls.Add((info.Record.FormKey.ObjectId, begin)); if (begin && info.Record.FormKey == Key(0x950)) cycles++; },
                condition => condition.Function == 71 ? cycles < 3 ? 1 : 0 : Unknown(condition));
            cycle.Start(Key(0x900), Key(0x807));
            Require(cycle.Phase == "continuing" && cycle.Info?.Record.FormKey == Key(0x951) && cycle.ResultOnlyCompletions == 2 &&
                cycle.Response is null && cycle.ResponseGeneration == 0 && cycles == 1,
                "A silent cycle recursed, fabricated voice completion or lost its completed predecessor.");
            Reject(() => cycle.CaptureContinuation());
            cycle.ContinueResultOnly(); Require(cycle.Phase == "continuing" && cycles == 2 && cycle.ResultOnlyCompletions == 4,
                "Silent continuation did not reevaluate and commit the next actual effect pair.");
            cycle.ContinueResultOnly(); Require(cycle.Phase == "closed" && cycles == 3 && cycle.ResultOnlyCompletions == 7 &&
                calls.Count == 14 && cycle.ResponseGeneration == 0, "Silent cycle ignored changed eligibility or failed to settle without speech.");
            Reject(() => cycle.ContinueResultOnly());
            calls.Clear(); var failed = Create((info, begin) =>
            { calls.Add((info.Record.FormKey.ObjectId, begin)); if (!begin) throw new NotSupportedException("Authored End failure."); });
            Reject(() => failed.Start(Key(0x900), Key(0x809)));
            Require(failed.Phase == "failed" && failed.BeginResultsEntered && failed.EndResultsEntered &&
                failed.ResultOnlyCompletions == 0 && failed.ResponseGeneration == 0 && failed.AddedTopics.Count == 0 &&
                said.Contains(Key(0x960)) && calls.SequenceEqual([(0x960u, true), (0x960u, false)]),
                "Failed End lost its real prefix or published AddedTopics/voice/silent completion.");
            Reject(() => failed.CaptureContinuation()); Reject(() => failed.Start(Key(0x900), Key(0x809)));
            said.Remove(Key(0x960)); calls.Clear(); var admitted = true;
            var faulted = Create((info, begin) => { calls.Add((info.Record.FormKey.ObjectId, begin)); admitted = false; },
                admission: () => { if (!admitted) throw new InvalidOperationException("Authored World admission closed."); });
            Reject(() => faulted.Start(Key(0x900), Key(0x809)));
            Require(faulted.Phase == "failed" && faulted.BeginResultsEntered && !faulted.EndResultsEntered &&
                faulted.ResultOnlyCompletions == 0 && faulted.AddedTopics.Count == 0 && said.Contains(Key(0x960)) && calls.Count == 1,
                "A genuine admission fault after Begin executed the End or presentation suffix.");
            calls.Clear(); var goodbye = Create(); goodbye.Start(Key(0x900), Key(0x80a));
            Require(goodbye.Phase == "closed" && goodbye.ResultOnlyCompletions == 1 && goodbye.ResponseGeneration == 0 &&
                calls.SequenceEqual([(0x970u, true), (0x970u, false)]), "Result-only built-in Goodbye did not settle through the real dialogue owner.");
            var empty = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x920)));
            Reject(() => FalloutDialogueTopic.RequireFlags(empty, conversation: true));
            Reject(() => FalloutDialogueTopic.RequireFlags(empty, conversation: true, npcConversation: true, allowResultOnly: true));
            Reject(() => FalloutDialogueTopic.RequireFlags(empty, conversation: true, radio: true, allowResultOnly: true));
            var view = new FalloutDialogueInputPublication(); view.Publish("choices", 0);
            var stale = view.RebuildRows(); var rows = view.RebuildRows();
            Require(!view.TrySubmit(stale, false) && view.TrySubmit(rows, false) && !view.TrySubmit(rows, false),
                "Silent choices lost their actual native row lifetime or accepted repeated input.");
            view.Publish("continuing", 0); rows = view.RebuildRows();
            Require(!view.TrySubmit(rows, false) && !view.TrySubmit(rows, true), "Pending silent effects accepted player delivery.");
            Reject(() => view.Publish("speaking", 0));
            Console.WriteLine("OPENNV_RESULT_ONLY_INFO_PASS authoredReader=true eligibility=true orderedEffects=true addedTopicsSayOnce=true silentLinks=true cycleYield=true failedPrefix=true voiceNotInvented=true");
        }
        finally { Directory.Delete(directory, true); }
        CompiledScriptContracts.RunResultOnlyInfo();
    }
}
