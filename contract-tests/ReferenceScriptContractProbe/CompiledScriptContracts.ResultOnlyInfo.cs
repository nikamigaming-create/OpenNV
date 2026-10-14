using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    // Real full-reader/compiled-executor contracts with authored numeric quest
    // locals. These prove a C# owner and DTO cold cycle, not native Save/Continue.
    internal static void RunResultOnlyInfo()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-result-only-" + Guid.NewGuid().ToString("N"));
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
            byte[] Topic(uint id, string prompt, byte[] info, byte type = 0)
            {
                var group = new byte[24 + info.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
                UInt(group, 4, (uint)group.Length); UInt(group, 8, id); UInt(group, 12, 7); info.CopyTo(group, 24);
                return Join(Record("DIAL", id, Field("EDID", Text(type == 1 ? "GOODBYE" : "AuthoredSilentTopic" + id)),
                    Field("FULL", Text(prompt)), Field("DATA", [type, 0])), group);
            }
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, Join(Tes4(), Script(0x70, Instruction(0x1d), [], "float value\nfloat suffix", quest: true),
                Record("QUST", 0x60, Field("EDID", Text("AuthoredSilentQuest")), Field("DATA", [1, 10]), Field("SCRI", U32(0x70))),
                Record("NPC_", 0x90, Field("FULL", Text("Authored speaker"))),
                Topic(0x800, "Silent choices", Info(0x910, 4, 0, Set('f', 1, " 5", 1), Set('f', 2, " 7", 1),
                    Field("NAME", U32(0x802)), Field("TCLT", U32(0x801)), Field("TCLT", U32(0x802)), Field("TCLT", U32(0x806)))),
                Topic(0x801, "Silent finish", Info(0x911, 1, 0, Set('f', 1, " 9", 1), Set('f', 2, " 11", 1))),
                Topic(0x802, "Spoken finish", Info(0x912, 1, 1, Set('f', 1, " 13", 1), Set('f', 2, " 17", 1))),
                Topic(0x803, "Immediate silent", Info(0x913, 9, 0, Set('f', 1, " 19", 1), Set('f', 2, " 21", 1))),
                Topic(0x804, "Failed silent End", Info(0x914, 4, 0, Set('f', 2, " 29", 1),
                    Join(Set('f', 1, " 23", 1), Instruction(0x2f03), Set('f', 2, " 99", 1)), Field("NAME", U32(0x802)))),
                Topic(0x805, "Failed silent Begin", Info(0x915, 4, 0,
                    Join(Set('f', 1, " 31", 1), Instruction(0x2f03), Set('f', 2, " 99", 1)), Set('f', 2, " 77", 1))),
                Topic(0x806, "GOODBYE", Record("INFO", 0x916, Field("DATA", [1, 0, 0, 0]), Field("QSTI", U32(0x60)),
                    Scope(Set('f', 1, " 9", 1)), Field("NEXT", []), Scope(Set('f', 2, " 11", 1))), type: 1)));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
            FalloutReferenceScripts Scripts(FalloutReferenceWorld owner, FalloutQuestState state) => new(records, owner, state,
                new((_, _) => false, _ => throw new InvalidDataException("Authored numeric result escaped into a gameplay effect.")));
            var scripts = Scripts(world, quests); var receipts = new List<FalloutScriptResultReceipt>();
            var said = new HashSet<FalloutFormKey>();
            FalloutConversation Create(FalloutReferenceScripts execution, FalloutQuestState state,
                List<FalloutScriptResultReceipt> observed, HashSet<FalloutFormKey> once) => new(records, state,
                condition => throw new NotSupportedException("Authored unexpected condition " + condition.Function),
                (_, _) => throw new InvalidDataException("Compiled result-only INFO used diagnostic callbacks."), once,
                ownedResults: (info, begin) => { var receipt = execution.ExecuteResultOwned(info, Key(0x60), begin); observed.Add(receipt); return receipt; },
                resultCaller: () => Key(0x60));
            T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
                ?? throw new InvalidDataException("Authored JSON value vanished.");
            var conversation = Create(scripts, quests, receipts, said); conversation.Start(Key(0x90), Key(0x800));
            Require(conversation.Phase == "choices" && conversation.ResponseIndex == 0 && conversation.ResponseGeneration == 0 &&
                conversation.PendingResponse is null && conversation.ResultOnlyCompletions == 1 && receipts.Count == 2 &&
                receipts.All(receipt => receipt.Authority == FalloutScriptResultAuthority.CompiledVanilla && receipt.CommittedSteps == 1) &&
                quests.Variable(Key(0x60), 1) == 5 && quests.Variable(Key(0x60), 2) == 7 && said.SetEquals([Key(0x910)]),
                "Silent INFO failed to commit actual compiled Begin/End without fabricating voice authority.");
            var saved = RoundTrip(conversation.CaptureContinuation()); saved.RequireSource(records, Key(0x60));
            // Invoke the real aggregate shape boundary; no synthetic complete
            // campaign or native participant residency is manufactured here.
            var shape = typeof(FalloutNativeCampaignSave).GetMethod("ValidateConversationShape", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidDataException("Campaign conversation shape boundary vanished.");
            void RequireShape(FalloutConversationSnapshot state)
            {
                try { shape.Invoke(null, [new FalloutPlayerConversationSnapshot(Key(0x90), Key(0x60), state)]); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidDataException inner)
                { throw new InvalidDataException("Aggregate rejected the authored conversation shape.", inner); }
            }
            RequireShape(saved); Reject(() => RequireShape(saved with { ResultOnlyCompletions = 0 }));
            Reject(() => (saved with { ResultOnlyCompletions = 0 }).RequireSource(records, Key(0x60)));
            Reject(() => (saved with { InfoSha256 = new string('0', 64) }).RequireSource(records, Key(0x60)));
            Reject(() => (saved with { End = saved.Begin }).RequireSource(records, Key(0x60)));
            Reject(() => (saved with { Begin = saved.Begin! with { Completed = false } }).RequireSource(records, Key(0x60)));
            Reject(() => saved.RequireSource(records, Key(0x90)));
            Reject(() => (saved with { Choices = saved.Choices.Reverse().ToArray() }).RequireSource(records, Key(0x60)));
            var questSnapshot = RoundTrip(quests.Capture()); var onceSnapshot = RoundTrip(said.ToArray());
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var coldWorld = new FalloutReferenceWorld(records); var coldQuests = new FalloutQuestState(records);
                coldQuests.Restore(RoundTrip(questSnapshot)); var coldScripts = Scripts(coldWorld, coldQuests);
                var coldReceipts = new List<FalloutScriptResultReceipt>(); var coldSaid = new HashSet<FalloutFormKey>(RoundTrip(onceSnapshot));
                var resumed = Create(coldScripts, coldQuests, coldReceipts, coldSaid); resumed.RestoreContinuation(RoundTrip(saved), Key(0x60));
                Require(coldReceipts.Count == 0 && resumed.Phase == "choices" && resumed.ResponseGeneration == 0 &&
                    resumed.ResultOnlyCompletions == 1 && resumed.Choices.SequenceEqual(conversation.Choices) &&
                    resumed.AddedTopics.SequenceEqual([Key(0x802)]) && coldQuests.Variable(Key(0x60), 1) == 5 &&
                    coldQuests.Variable(Key(0x60), 2) == 7 && coldSaid.SetEquals(onceSnapshot),
                    "Cold silent choices replayed compiled effects/SayOnce or fabricated response authority.");
                saved = RoundTrip(resumed.CaptureContinuation()); RequireShape(saved);
                if (cycle != 2) continue;
                resumed.Choose(Key(0x806)); Require(resumed.Phase == "closed" && resumed.ResponseGeneration == 0 &&
                    resumed.ResultOnlyCompletions == 2 && coldReceipts.Count == 2 &&
                    coldQuests.Variable(Key(0x60), 1) == 9 && coldQuests.Variable(Key(0x60), 2) == 11,
                    "Cold silent choice did not execute once through its fresh actual result executor.");
                var closed = RoundTrip(resumed.CaptureContinuation()); closed.RequireSource(records, Key(0x60));
                Reject(() => resumed.RestoreContinuation(closed, Key(0x60)));
            }
            var immediate = Create(scripts, quests, receipts, said); var before = receipts.Count;
            immediate.Start(Key(0x90), Key(0x803));
            Require(immediate.Phase == "closed" && immediate.ResultOnlyCompletions == 1 && immediate.ResponseGeneration == 0 &&
                receipts.Count == before + 2 && quests.Variable(Key(0x60), 1) == 19 && quests.Variable(Key(0x60), 2) == 21,
                "Immediate silent INFO omitted or repeated an actual compiled result.");
            var failed = Create(scripts, quests, receipts, said); Reject(() => failed.Start(Key(0x90), Key(0x804)));
            Require(failed.Phase == "failed" && failed.BeginResultsEntered && failed.EndResultsEntered &&
                failed.ResultOnlyCompletions == 0 && failed.ResponseGeneration == 0 && failed.AddedTopics.Count == 0 &&
                quests.Variable(Key(0x60), 1) == 23 && quests.Variable(Key(0x60), 2) == 29 && said.Contains(Key(0x914)) &&
                world.ScriptManualSaves.CompiledResultCaptureBlocker is not null,
                "Unknown silent End behavior lost its committed numeric prefix or admitted an unexecuted suffix.");
            Reject(() => failed.CaptureContinuation()); Reject(() => failed.Start(Key(0x90), Key(0x804)));
            // Use a genuinely fresh world/executor after the retained failed
            // instruction prefix rather than clearing the failed VM owner.
            using var beginWorld = new FalloutReferenceWorld(records); var beginQuests = new FalloutQuestState(records);
            var beginReceipts = new List<FalloutScriptResultReceipt>(); var beginSaid = new HashSet<FalloutFormKey>();
            var failedBegin = Create(Scripts(beginWorld, beginQuests), beginQuests, beginReceipts, beginSaid);
            Reject(() => failedBegin.Start(Key(0x90), Key(0x805)));
            Require(failedBegin.Phase == "failed" && failedBegin.BeginResultsEntered && !failedBegin.EndResultsEntered &&
                failedBegin.ResultOnlyCompletions == 0 && failedBegin.ResponseGeneration == 0 && beginReceipts.Count == 0 &&
                beginQuests.Variable(Key(0x60), 1) == 31 && beginQuests.Variable(Key(0x60), 2) == 0 && beginSaid.Contains(Key(0x915)),
                "Unknown silent Begin behavior reached End/voice or discarded its genuine failed prefix.");
            Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Authored source was mutated during result-only execution/cold validation.");
            Console.WriteLine("OPENNV_COMPILED_RESULT_ONLY_INFO_PASS actualCompiledEffects=true sourceReceipts=true shapeBoundary=true coldChoices3=true noReplay=true failedInstructionPrefix=true nativeCampaignSaveContinue=unexecuted");
        }
        finally { Directory.Delete(directory, true); }
    }
}
