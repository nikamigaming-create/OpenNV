using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class ConversationContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-conversation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "Dialogue.esm");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var data = Join(Record("TES4", 0, Field("HEDR", header)),
                Record("QUST", 0x700, Field("DATA", [1, 10])), Record("QUST", 0x701, Field("DATA", [0, 90])),
                Record("QUST", 0x702, Field("DATA", [1, 80]), Condition(71, 0x777, 1),
                    Field("INDX", BitConverter.GetBytes((short)5)), Condition(65535, 0, 1)),
                Record("QUST", 0x703, Field("DATA", [0, 95])),
                Topic(0x800, "Greeting",
                    Info(0x910, 0x700, 4, 2, Field("TCFU", U32(0x920)), Field("TCFU", U32(0x921)), Field("TCFU", U32(0x922))),
                    Info(0x911, 0x700, 1, 1), Info(0x912, 0x701, 1, 1)),
                Topic(0x801, "Follow ups",
                    Info(0x920, 0x700, 0, 1, Condition(72, 0x999, 1)),
                    Info(0x921, 0x700, 0, 1, Field("TCLT", U32(0x802)), Field("TCLT", U32(0x803)), Field("TCLT", U32(0x809))),
                    Info(0x922, 0x700, 1, 1)),
                Topic(0x802, "Base prompt", Info(0x930, 0x700, 1, 1, Field("RNAM", Text("Response prompt")))),
                Topic(0x803, "Filtered", Info(0x931, 0x700, 1, 1, Condition(72, 0x999, 1))),
                Topic(0x804, "AND speaker filtering", Info(0x940, 0x700, 1, 1, Condition(161, 0, 1), Condition(72, 0x999, 1)),
                    Info(0x941, 0x700, 1, 1)),
                Topic(0x805, "OR must remain ordered", Info(0x950, 0x700, 1, 1, Condition(161, 0, 1, flags: 1), Condition(72, 0x999, 1))),
                Topic(0x806, "Random must remain ordered", Info(0x960, 0x700, 1, 1, Condition(77, 0, 1), Condition(72, 0x999, 1))),
                Topic(0x807, "Explicit speaker", Info(0x970, 0x700, 1, 1, Field("ANAM", U32(0x999))), Info(0x971, 0x700, 1, 1)),
                Topic(0x808, "Invalid follow", Info(0x980, 0x700, 0, 1, Field("TCFU", U32(0x700)))),
                Topic(0x809, "Empty authored topic"),
                TopLevel(0x80a, "Lower priority", 20, Info(0x990, 0x700, 0, 1)),
                TopLevel(0x80b, "Higher priority", 80, Info(0x991, 0x700, 0, 1, Field("TCLT", U32(0x802)))),
                TopLevel(0x80c, "Other speaker", 90, Info(0x992, 0x700, 1, 1, Condition(72, 0x999, 1))),
                TopLevel(0x80d, "Stopped quest", 95, Info(0x993, 0x701, 1, 1)),
                Topic(0x80e, "Added leaf", Info(0x994, 0x700, 0, 1, Field("NAME", U32(0x802)))),
                Record("DIAL", 0x80f, Field("DATA", [1])),
                Topic(0x810, "GOODBYE", 2, 5, [InfoOfType(1, 0x995, 0x700, 0, 1, Condition(72, 0x999, 1)),
                    InfoOfType(1, 0x996, 0x700, 0, 1)], type: 1),
                Topic(0x811, "Explicit order", Info(0x997, 0x700, 0, 1, Field("TCLT", U32(0x80a)), Field("TCLT", U32(0x80b)))),
                Topic(0x812, "Filtered empty INFO", Info(0x998, 0x700, 4, 0, Condition(72, 0x999, 1),
                    Field("SCTX", Text("begin result")), Field("NEXT", []), Field("SCTX", Text("end result")), Field("TCLT", U32(0x802))),
                    Info(0x999, 0x700, 1, 1)),
                Topic(0x813, "Selected empty INFO", Info(0x99a, 0x700, 4, 0, Field("SCTX", Text("reached result")))),
                Topic(0x814, "Orphan response text", Info(0x99b, 0x700, 0, 0, Field("NAM1", Text("orphan")))),
                Topic(0x815, "Duplicate response number", Info(0x99c, 0x700, 0, 1,
                    Field("TRDT", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0]), Field("NAM1", Text("duplicate")))),
                Topic(0x816, "Scripted topic", InfoOfType(1, 0x9a0, 0x703, 128, 1),
                    Info(0x9a1, 0x702, 0, 1), Info(0x9a2, 0x700, 0, 1)),
                Topic(0x817, "Scripted conversation", 0, 50, [InfoOfType(1, 0x9a3, 0x700, 0, 1)], type: 1));
            File.WriteAllBytes(file, data);
            using var records = FalloutPluginStack.Load(directory, ["Dialogue.esm"]);
            var quests = new FalloutQuestState(records);
            Require(FalloutDialogueTopic.Priority(records, Key(0x80f)) == 50 && !FalloutDialogueTopic.DefaultTopics(records).Contains(Key(0x80f)),
                "Legacy one-byte DIAL metadata did not retain its default priority/non-top-level state.");
            var scopedStage = new FalloutCondition(records.GetEffective(Key(0x910)), 0, 0, 58, 0x700, 0, 1, 0);
            Require(quests.Evaluate(scopedStage) == 0 && quests.Evaluate(scopedStage with { Function = 56 }) == 1,
                "Quest predicates changed ownership when the listener scope was selected.");
            Require(FalloutDialogueTopic.Read(records, Key(0x809)).Infos.Count == 0, "An empty topic acquired an invented response.");
            var empty = FalloutDialogueTopic.Read(records, Key(0x812)).Infos[0];
            Require(empty.Responses.Count == 0 && empty.Flags == 4 && empty.Conditions.Count == 1 &&
                empty.BeginScript == "begin result" && empty.EndScript == "end result" && empty.Choices.SequenceEqual([Key(0x802)]),
                "Optional INFO responses lost source condition, script, flag or link ownership.");
            Reject(() => FalloutDialogueTopic.Read(records, Key(0x814)));
            Reject(() => FalloutDialogueTopic.Read(records, Key(0x815)));
            var calls = new List<(uint Info, bool Begin)>();
            float Unknown(FalloutCondition condition) => throw new NotSupportedException($"Unbound synthetic query {condition.Function}.");
            FalloutConversation Create(Action<FalloutDialogueInfo, bool>? result = null) => new(records, quests, Unknown,
                result ?? ((info, begin) => calls.Add((info.Record.FormKey.ObjectId, begin))));
            var conversation = Create();
            var filteredEmpty = Create(); filteredEmpty.Start(Key(0x900), Key(0x812));
            Require(filteredEmpty.Info!.Record.FormKey == Key(0x999) && calls.SequenceEqual([(0x999u, true)]),
                "An ineligible empty INFO prevented selection or executed its result.");
            calls.Clear();
            var selectedEmpty = Create();
            Reject(() => selectedEmpty.Start(Key(0x900), Key(0x813)));
            Require(selectedEmpty.Phase == "failed" && calls.Count == 0 && selectedEmpty.Response is null &&
                selectedEmpty.Error!.Contains("zero-response", StringComparison.Ordinal),
                "An unowned selected empty INFO ran effects or fabricated speech.");
            conversation.Start(Key(0x900), Key(0x800));
            Require(conversation.Info!.Record.FormKey == Key(0x910) && calls.SequenceEqual([(0x910u, true)]), "Greeting priority/start state or begin result changed.");
            conversation.CompleteResponse();
            Require(conversation.ResponseIndex == 1 && calls.Count == 1, "An intermediate line ran end results.");
            conversation.CompleteResponse();
            Require(conversation.Info!.Record.FormKey == Key(0x921) && calls.SequenceEqual([(0x910u, true), (0x910u, false), (0x921u, true)]),
                "Follow-up order/conditions or result ordering changed.");
            conversation.CompleteResponse();
            Require(conversation.Choices.Single() == new FalloutConversationChoice(Key(0x802), Key(0x930), "Response prompt"),
                "Choice filtering or response prompt override failed.");
            Reject(() => conversation.Choose(Key(0x803)));
            Require(conversation.Phase == "choices" && conversation.Error is null, "Invalid input damaged a valid conversation.");
            conversation.Choose(Key(0x802)); conversation.CompleteResponse();
            Require(conversation.Phase == "closed" && calls.Count == 6, "Goodbye or exactly-once results failed.");
            conversation.Start(Key(0x900), Key(0x800));
            Require(conversation.Info!.Record.FormKey == Key(0x911), "Say-once state did not survive closing the menu.");
            conversation.CompleteResponse();
            quests.SetRunning(Key(0x701), true);
            var priority = Create(); priority.Start(Key(0x900), Key(0x800));
            Require(priority.Info!.Record.FormKey == Key(0x912), "Running quest priority did not select the winning response.");
            quests.SetRunning(Key(0x701), false);
            var and = Create(); and.Start(Key(0x900), Key(0x804));
            Require(and.Info!.Record.FormKey == Key(0x941), "Pure wrong-speaker conjunction did not exclude unrelated queries.");
            Reject(() => Create().Start(Key(0x900), Key(0x805)));
            Reject(() => Create().Start(Key(0x900), Key(0x806)));
            var explicitSpeaker = Create(); explicitSpeaker.Start(Key(0x900), Key(0x807));
            Require(explicitSpeaker.Info!.Record.FormKey == Key(0x971), "Explicit source speaker was ignored.");
            var invalidFollow = Create(); invalidFollow.Start(Key(0x900), Key(0x808));
            Reject(invalidFollow.CompleteResponse);
            var defaults = Create(); defaults.Start(Key(0x900), Key(0x80a)); defaults.CompleteResponse();
            Require(defaults.Choices.Select(choice => choice.Topic).SequenceEqual([Key(0x80b), Key(0x80a), Key(0x810)]) &&
                defaults.Choices[^1].Info == Key(0x996),
                "Unlinked response did not rebuild eligible top-level topics in source priority order.");
            defaults.Choose(Key(0x80b)); defaults.CompleteResponse();
            Require(defaults.Choices.Single().Topic == Key(0x802), "Explicit links did not replace the default topic list.");
            var added = Create(); added.Start(Key(0x900), Key(0x80e)); added.CompleteResponse();
            Require(added.Choices.Select(choice => choice.Topic).SequenceEqual([Key(0x80b), Key(0x802), Key(0x80a), Key(0x810)]),
                "Added topic was not combined with eligible default topics in source priority order.");
            added.Choose(Key(0x810)); added.CompleteResponse();
            Require(added.Phase == "closed", "Built-in GOODBYE did not finish after its actor's final response.");
            var linked = Create(); linked.Start(Key(0x900), Key(0x811)); linked.CompleteResponse();
            Require(linked.Choices.Select(choice => choice.Topic).SequenceEqual([Key(0x80a), Key(0x80b)]),
                "Topic priority reordered the source's explicit TCLT links.");
            var executed = 0;
            var failed = Create((_, _) => { executed++; throw new NotSupportedException("Unbound result effect."); });
            Reject(() => failed.Start(Key(0x900), Key(0x800)));
            Reject(() => failed.Start(Key(0x900), Key(0x800)));
            Require(executed == 1 && failed.Phase == "failed", "A failed begin effect repeated or published a response.");
            var changed = Create(); changed.Start(Key(0x900), Key(0x800));
            changed.CompleteResponse(); changed.CompleteResponse(); changed.CompleteResponse();
            quests.SetRunning(Key(0x700), false);
            Reject(() => changed.Choose(Key(0x802)));
            Require(changed.Phase == "failed", "A stale offered choice bypassed current eligibility.");
            ScriptedSelection(records);
            Console.WriteLine("OPENNV_CONVERSATION_CONTRACT_PASS responseOrder=true exactlyOnce=true followUps=true sourceChoices=true topLevelReturn=true topicPriority=true addedTopics=true prompts=true questPriority=true sayOnce=true orderedConditions=true changedEligibility=true explicitFailure=true");
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    private static void ScriptedSelection(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records);
        var selection = new FalloutDialogueQuestSelection(records, quests);
        var member = false;
        var queries = 0;
        float Evaluate(FalloutCondition condition)
        {
            Require(condition.Function == 71 && condition.FormArgument1 == Key(0x777),
                "Scripted selection evaluated a stage condition or lost its source quest argument.");
            queries++; return member ? 1 : 0;
        }
        var topic = FalloutDialogueTopic.Read(records, Key(0x816));
        FalloutDialogueInfo? Select() => selection.Select(topic, Key(0x900), new HashSet<FalloutFormKey>(),
            quest => quests.Stage(quest), Evaluate);
        Require(Select()!.Record.FormKey == Key(0x9a2) && queries == 1,
            "Scripted topic admitted an inactive/quest-ineligible response or assumed conversation type.");
        member = true;
        Require(Select()!.Record.FormKey == Key(0x9a1) && queries == 2,
            "Cached quest headers hid live speaker conditions or source quest priority.");
        quests.SetRunning(Key(0x702), false);
        Require(Select()!.Record.FormKey == Key(0x9a2), "Stopped quest stayed eligible after selection.");
        quests.SetRunning(Key(0x700), false);
        Require(Select() is null, "Empty scripted topic selection fabricated a response.");
        var cold = new FalloutQuestState(records); cold.Restore(quests.Capture());
        var coldSelection = new FalloutDialogueQuestSelection(records, cold);
        Require(coldSelection.Select(topic, Key(0x900), new HashSet<FalloutFormKey>(), quest => cold.Stage(quest), Evaluate) is null,
            "Cold scripted selection lost live running-quest state.");
        quests.SetRunning(Key(0x700), true);
        Require(selection.Select(FalloutDialogueTopic.Read(records, Key(0x817)), Key(0x900), new HashSet<FalloutFormKey>(),
            quest => quests.Stage(quest), Evaluate)!.Record.FormKey == Key(0x9a3), "Conversation DIAL lost its source response type.");
        quests.SetRunning(Key(0x703), true);
        Reject(() => Select());
        Console.WriteLine("OPENNV_SCRIPTED_TOPIC_SELECTION_PASS sourceType=true activeQuests=true questConditions=true priority=true liveChanges=true coldRunningState=true invalidEligibleResponseRejected=true");
    }

    private static FalloutFormKey Key(uint id) => new("Dialogue.esm", id);
    private static void Require(bool pass, string error) { if (!pass) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid conversation transition was admitted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] data) => data.SelectMany(value => value).ToArray();
    private static byte[] Condition(ushort function, uint argument, float comparison, byte flags = 0)
    {
        var data = new byte[28]; data[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), argument);
        return Field("CTDA", data);
    }
    private static byte[] Info(uint id, uint quest, byte flags, int responses, params byte[][] extra)
        => InfoOfType(0, id, quest, flags, responses, extra);
    private static byte[] InfoOfType(byte type, uint id, uint quest, byte flags, int responses, params byte[][] extra)
    {
        var fields = new List<byte[]> { Field("DATA", [type, 0, flags, 0]), Field("QSTI", U32(quest)) };
        for (var index = 1; index <= responses; index++)
        {
            var response = new byte[24]; response[12] = (byte)index;
            fields.Add(Field("TRDT", response)); fields.Add(Field("NAM1", Text($"Response {index}")));
        }
        fields.AddRange(extra);
        return Record("INFO", id, Join(fields.ToArray()));
    }
    private static byte[] Topic(uint id, string prompt, params byte[][] infos)
        => Topic(id, prompt, 0, 50, infos);
    private static byte[] TopLevel(uint id, string prompt, float priority, params byte[][] infos)
        => Topic(id, prompt, 2, priority, infos);
    private static byte[] Topic(uint id, string prompt, byte flags, float priority, byte[][] infos, byte type = 0)
    {
        var body = Join(infos); var group = new byte[24 + body.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 7); body.CopyTo(group, 24);
        return Join(Record("DIAL", id, Field("EDID", Text(prompt)), Field("FULL", Text(prompt)), Field("DATA", [type, flags]), Field("PNAM", BitConverter.GetBytes(priority))), group);
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
}
