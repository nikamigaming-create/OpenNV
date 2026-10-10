using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunDialogueContinuations()
    {
        var publication = new FalloutDialogueInputPublication();
        publication.Publish("speaking", 1); var held = publication.Current;
        publication.Publish("speaking", 2);
        Require(!publication.TrySubmit(held, true), "An earlier held skip targeted the later source response.");
        var current = publication.Current;
        Require(publication.TrySubmit(current, true) && !publication.TrySubmit(current, true),
            "Current native delivery repeated after its committed callback prefix.");
        publication.Publish("choices", 2); var oldRows = publication.RebuildRows();
        var newRows = publication.RebuildRows();
        Require(!publication.TrySubmit(oldRows, false) && publication.TrySubmit(newRows, false),
            "A replaced choice row acted on the current source menu.");
        publication.Publish("speaking", 3); publication.Retire();
        Require(!publication.TrySubmit(publication.Current, true), "Retired source view accepted native delivery.");

        var directory = Path.Combine(Path.GetTempPath(), "opennv-dialogue-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] Scope(byte[] code) => Join(Field("SCHR", Header(code, 1)), Field("SCDA", code), Field("SCRO", U32(0x60)));
            byte[] Info(uint id, byte flags, int responses, byte[] begin, byte[] end, params byte[][] extra)
            {
                var fields = new List<byte[]> { Field("DATA", [0, 0, flags, 0]), Field("QSTI", U32(0x60)) };
                for (var index = 0; index < responses; ++index)
                {
                    var response = new byte[24]; response[12] = checked((byte)(index + 1));
                    fields.Add(Field("TRDT", response)); fields.Add(Field("NAM1", Text("Authored response " + index)));
                }
                fields.AddRange(extra); fields.Add(Scope(begin)); fields.Add(Field("NEXT", [])); fields.Add(Scope(end));
                return Record("INFO", id, Join(fields.ToArray()));
            }
            byte[] Topic(uint id, string prompt, byte[] info)
            {
                var group = new byte[24 + info.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
                UInt(group, 4, (uint)group.Length); UInt(group, 8, id); UInt(group, 12, 7); info.CopyTo(group, 24);
                return Join(Record("DIAL", id, Field("EDID", Text("AuthoredTopic" + id)), Field("FULL", Text(prompt)), Field("DATA", [0, 0])), group);
            }
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, Join(Tes4(), Script(0x70, Instruction(0x1d), [], "float value\nfloat suffix", quest: true),
                Record("QUST", 0x60, Field("EDID", Text("AuthoredDialogueQuest")), Field("DATA", [1, 10]), Field("SCRI", U32(0x70))),
                Record("NPC_", 0x90, Field("FULL", Text("Authored speaker"))),
                Topic(0x800, "Initial", Info(0x910, 0, 2, Set('f', 1, " 5", 1), Set('f', 2, " 7", 1), Field("TCLT", U32(0x801)))),
                Topic(0x801, "Finish", Info(0x911, 1, 1, Set('f', 1, " 9", 1), Set('f', 2, " 11", 1))),
                Topic(0x802, "Immediate", Info(0x912, 9, 2, Set('f', 1, " 13", 1), Set('f', 2, " 17", 1))),
                Topic(0x803, "Failure", Info(0x913, 1, 1, [],
                    Join(Set('f', 1, " 23", 1), Instruction(0x2f03), Set('f', 2, " 99", 1))))));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Authored numeric result escaped into a gameplay effect.")));
            var receipts = new List<FalloutScriptResultReceipt>();
            FalloutConversation Create(FalloutReferenceScripts execution, FalloutQuestState state, List<FalloutScriptResultReceipt> observed) =>
                new(records, state, condition => throw new NotSupportedException("Authored unexpected condition " + condition.Function),
                    (_, _) => throw new InvalidDataException("Compiled dialogue used a diagnostic result callback."),
                    ownedResults: (info, begin) =>
                    {
                        var receipt = execution.ExecuteResultOwned(info, Key(0x60), begin);
                        observed.Add(receipt); return receipt;
                    }, resultCaller: () => Key(0x60));
            var conversation = Create(scripts, quests, receipts);
            conversation.Start(Key(0x90), Key(0x800));
            Require(receipts.Count == 1 && receipts[0].CommittedSteps == 1 && quests.Variable(Key(0x60), 1) == 5,
                "Actual shared compiled Begin did not commit before native response entry.");
            Reject(() => conversation.CaptureContinuation());
            var first = conversation.EnterResponse(); conversation.CompleteResponse(first);
            var next = conversation.EnterResponse();
            Reject(() => conversation.CompleteResponse(first));
            Require(receipts.Count == 1 && next.Generation > first.Generation && conversation.ResponseIndex == 1,
                "A stale source completion replayed results or consumed the next response.");
            conversation.CompleteResponse(next);
            Require(receipts.Count == 2 && quests.Variable(Key(0x60), 2) == 7 && conversation.Phase == "choices",
                "Final response did not commit the actual source End before choices.");
            var saved = Copy(conversation.CaptureContinuation());
            saved.RequireSource(records, Key(0x60));
            Reject(() => (saved with { InfoSha256 = new string('0', 64) }).RequireSource(records, Key(0x60)));
            Reject(() => (saved with { End = saved.Begin }).RequireSource(records, Key(0x60)));
            Reject(() => (saved with { Choices = [saved.Choices[0] with { Text = "Invented prompt" }] }).RequireSource(records, Key(0x60)));
            using var coldWorld = new FalloutReferenceWorld(records); var coldQuests = new FalloutQuestState(records);
            coldQuests.Restore(Copy(quests.Capture()));
            var coldScripts = new FalloutReferenceScripts(records, coldWorld, coldQuests, new((_, _) => false,
                _ => throw new InvalidDataException("Cold authored result escaped its numeric owner.")));
            var coldReceipts = new List<FalloutScriptResultReceipt>(); var resumed = Create(coldScripts, coldQuests, coldReceipts);
            resumed.RestoreContinuation(saved, Key(0x60));
            Require(coldReceipts.Count == 0 && resumed.Phase == "choices" && resumed.Choices.SequenceEqual(conversation.Choices) &&
                coldQuests.Variable(Key(0x60), 1) == 5 && coldQuests.Variable(Key(0x60), 2) == 7,
                "Cold source choices replayed Begin/End or replaced their committed state.");
            resumed.Choose(Key(0x801)); var final = resumed.EnterResponse(); resumed.CompleteResponse(final);
            Reject(() => resumed.CompleteResponse(final));
            Require(resumed.Phase == "closed" && coldReceipts.Count == 2 && coldQuests.Variable(Key(0x60), 2) == 11,
                "Cold choice did not execute once through the current shared compiled executor.");
            var immediate = Create(scripts, quests, receipts); immediate.Start(Key(0x90), Key(0x802));
            var before = receipts.Count; var line = immediate.EnterResponse(); immediate.CompleteResponse(line);
            line = immediate.EnterResponse(); immediate.CompleteResponse(line);
            Require(immediate.Phase == "closed" && receipts.Count == before && quests.Variable(Key(0x60), 2) == 17,
                "Immediate source results replayed at response completion.");
            var callbackFault = new FalloutConversation(records, quests,
                condition => throw new NotSupportedException("Authored callback condition " + condition.Function),
                (_, _) => throw new InvalidDataException("Callback fixture used diagnostic results."),
                ownedResults: (info, begin) =>
                {
                    var receipt = scripts.ExecuteResultOwned(info, Key(0x60), begin);
                    if (!begin) throw new IOException("Authored post-commit callback failure.");
                    return receipt;
                }, resultCaller: () => Key(0x60));
            callbackFault.Start(Key(0x90), Key(0x801)); var attempted = callbackFault.EnterResponse();
            try { callbackFault.CompleteResponse(attempted); throw new InvalidDataException("Callback failure was swallowed."); }
            catch (IOException) { }
            Reject(() => callbackFault.CompleteResponse(attempted)); Reject(() => callbackFault.CaptureContinuation());
            Require(callbackFault.Phase == "failed" && callbackFault.EndResultsEntered && quests.Variable(Key(0x60), 2) == 11 &&
                callbackFault.Error == "Authored post-commit callback failure.",
                "Ordinary callback failure lost the real committed result or permitted replay/cold admission.");
            var failed = Create(scripts, quests, receipts); failed.Start(Key(0x90), Key(0x803));
            var failing = failed.EnterResponse(); Reject(() => failed.CompleteResponse(failing));
            Reject(() => failed.CompleteResponse(failing)); Reject(() => failed.CaptureContinuation());
            Require(failed.Phase == "failed" && failed.EndResultsEntered && quests.Variable(Key(0x60), 1) == 23 &&
                quests.Variable(Key(0x60), 2) == 11 && world.ScriptManualSaves.CompiledResultCaptureBlocker is not null,
                "Result failure lost its committed instruction prefix or admitted a retry/cold capture.");
            Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Authored source changed during execution/continuation.");
            Console.WriteLine("OPENNV_DIALOGUE_CONTINUATION_PASS sourceScopes=true actualCompiledResults=true responseGeneration=true staleRows=true onceOnly=true coldChoicesNoReplay=true committedFailurePrefix=true activeAudioUnowned=true");
        }
        finally { Directory.Delete(directory, true); }
    }
}
