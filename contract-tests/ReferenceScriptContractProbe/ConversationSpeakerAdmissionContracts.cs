using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

// First-party complete plugin bytes. Missing/deleted owners deliberately remain
// present on authored lines; no retail record or dialogue text is embedded.
internal static class ConversationSpeakerAdmissionContracts
{
    private const string Actors = "Speakers.esm", Other = "OtherMaster.esm", Override = "DialogueOverride.esp";
    private static FalloutFormKey Key(uint id) => new(Actors, id);

    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-dialogue-speaker-admission-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, Actors), Base());
            File.WriteAllBytes(Path.Combine(directory.FullName, Other), Header());
            using (var records = FalloutPluginStack.Load(directory.FullName, [Actors, Other]))
            {
                var quests = new FalloutQuestState(records);
                var calls = new List<FalloutFormKey>();
                FalloutConversation Conversation(HashSet<FalloutFormKey>? said = null) => new(records, quests, Unknown,
                    (info, _) => calls.Add(info.Record.FormKey), said);
                void Select(uint topic, uint info, HashSet<FalloutFormKey>? said = null)
                {
                    var conversation = Conversation(said); conversation.Start(Key(0x900), Key(topic));
                    Require(conversation.Info!.Record.FormKey == Key(info) && conversation.Phase == "speaking",
                        "Source speaker admission selected another INFO or fabricated a transition.");
                }

                var missing = Key(0x8ff);
                Require(!records.TryGetWinner(missing, out _) && records.GetEffective(Key(0x901)).Signature == "NPC_",
                    "Missing quest or independent actual authored speaker was replaced with an invented owner.");
                Select(0xa00, 0xb00);
                Require(calls.SequenceEqual([Key(0xb00)]), "Unrelated missing-quest line ran source results.");
                Select(0xa01, 0xb03, [Key(0xb02)]);
                Select(0xa02, 0xb05);
                Select(0xa08, 0xb14);
                quests.SetRunning(Key(0x801), false); Select(0xa08, 0xb15); quests.SetRunning(Key(0x801), true);

                // No global missing/deleted-owner filter is permitted. These
                // lines remain potentially eligible and must fault before any
                // result, with their original distinct owner diagnosis.
                foreach (var topic in new uint[] { 0xa03, 0xa04, 0xa05, 0xa0a })
                    Failure<KeyNotFoundException>(() => Conversation(), topic, "No record exists for Speakers.esm:0008ff", calls);
                Failure<InvalidDataException>(() => Conversation(), 0xa06, "Dialogue quest is not QUST", calls);
                Failure<KeyNotFoundException>(() => Conversation(), 0xa07, "winning record for Speakers.esm:000802 is deleted", calls);
                Failure<NotSupportedException>(() => Conversation(), 0xa09, "Unbound authored condition 69", calls);
                var traitQueries = 0;
                float KnownFalseTrait(FalloutCondition condition)
                {
                    if (condition.Function != 69) return Unknown(condition);
                    traitQueries++; return 0;
                }
                Failure<KeyNotFoundException>(() => new(records, quests, KnownFalseTrait,
                    (info, _) => calls.Add(info.Record.FormKey)), 0xa0b, "No record exists for Speakers.esm:0008ff", calls);
                Require(traitQueries == 0, "A random-query line hoisted its false trait before the ordered owner.");
                float KnownTrueTrait(FalloutCondition condition)
                {
                    if (condition.Function != 69) return Unknown(condition);
                    traitQueries++; return 1;
                }
                Failure<KeyNotFoundException>(() => new(records, quests, KnownTrueTrait,
                    (info, _) => calls.Add(info.Record.FormKey)), 0xa0c, "No record exists for Speakers.esm:0008ff", calls);
                Require(traitQueries == 0, "An OR alternative was evaluated or split before its reached quest owner.");
                var missingInfo = FalloutDialogueTopic.Read(records, Key(0xa00)).Infos.Single(info => info.Record.FormKey == Key(0xb01));
                Require(missingInfo.Quest == missing && missingInfo.Conditions.Count == 2,
                    "Ineligible source INFO disappeared from the decoded topic or lost its unowned quest/condition.");
            }

            // Local MAST order intentionally differs from absolute load order:
            // raw index1 means Speakers.esm, while loaded index1 is OtherMaster.
            var overridePath = Path.Combine(directory.FullName, Override);
            void WriteOverride(params byte[][] records) => File.WriteAllBytes(overridePath, Join(Header(Other, Actors), Join(records)));
            FalloutPluginStack Stack() => FalloutPluginStack.Load(directory.FullName, [Actors, Other, Override]);

            WriteOverride(Group(0x01000a00, Info(0x02000b20, 0x01000800, 4, Field("PNAM", U32(0)),
                Condition(72, 0x01000900, 1))));
            using (var records = Stack())
            {
                var topic = FalloutDialogueTopic.Read(records, Key(0xa00));
                var inserted = new FalloutFormKey(Override, 0xb20);
                Require(topic.Infos.Select(info => info.Record.FormKey).SequenceEqual([inserted, Key(0xb00), Key(0xb01)]),
                    "PNAM insertion or original source INFO order changed.");
                var first = topic.Infos[0];
                Require(first.Quest == Key(0x800) && FalloutCondition.Read(first.Record).Single().FormArgument1 == Key(0x900),
                    "Quest/speaker references used absolute load order instead of the declaring MAST table.");
                var quests = new FalloutQuestState(records); var said = new HashSet<FalloutFormKey>();
                var effects = new List<FalloutFormKey>();
                FalloutConversation Conversation() => new(records, quests, Unknown, (info, _) => effects.Add(info.Record.FormKey), said);
                var initial = Conversation(); initial.Start(Key(0x900), Key(0xa00));
                Require(initial.Info!.Record.FormKey == inserted && said.SetEquals([inserted]),
                    "Equal quest priority ignored source PNAM order or lost SayOnce ownership.");
                var again = Conversation(); again.Start(Key(0x900), Key(0xa00));
                Require(again.Info!.Record.FormKey == Key(0xb00) && effects.SequenceEqual([inserted, Key(0xb00)]),
                    "SayOnce reselected a consumed INFO or dropped the unrelated missing source row.");
            }

            WriteOverride(Record("QUST", 0x01000800, 0, Field("DATA", [1, 100])));
            using (var records = Stack())
            {
                var conversation = new FalloutConversation(records, new(records), Unknown, (_, _) => { });
                conversation.Start(Key(0x900), Key(0xa08));
                Require(conversation.Info!.Record.FormKey == Key(0xb15) && records.GetEffective(Key(0x800)).Plugin.Name == Override,
                    "Winning quest-priority override was ignored or rebound to another master.");
            }

            WriteOverride(Group(0x01000a00, Info(0x01000b01, 0x010008ff, 0, Condition(72, 0x01000900, 1))));
            using (var records = Stack())
            {
                var topic = FalloutDialogueTopic.Read(records, Key(0xa00));
                Require(topic.Infos.Single(info => info.Record.FormKey == Key(0xb01)).Record.Plugin.Name == Override,
                    "Winning INFO override was not retained under its original identity.");
                var calls = new List<FalloutFormKey>();
                Failure<KeyNotFoundException>(() => new(records, new(records), Unknown, (info, _) => calls.Add(info.Record.FormKey)),
                    0xa00, "No record exists for Speakers.esm:0008ff", calls);
            }

            WriteOverride(Group(0x01000a00, Record("INFO", 0x01000b01, 0x20)));
            using (var records = Stack())
            {
                Require(!records.TryGetEffective(Key(0xb01), out _) && records.TryGetWinner(Key(0xb01), out var deleted) && deleted.IsDeleted,
                    "Deleted winning INFO fell back to an earlier record.");
                var topic = FalloutDialogueTopic.Read(records, Key(0xa00));
                Require(topic.Infos.All(info => info.Record.FormKey != Key(0xb01)), "Deleted INFO remained a selection candidate.");
                var conversation = new FalloutConversation(records, new(records), Unknown, (_, _) => { });
                conversation.Start(Key(0x900), Key(0xa00));
                Require(conversation.Info!.Record.FormKey == Key(0xb00), "Legitimate INFO deletion changed the eligible sibling.");
            }

            WriteOverride(Group(0x01000a00, Info(0x02000b20, 0x01000800, 0, Field("PNAM", U32(0x01000fff)))));
            using (var records = Stack())
                Reject<NotSupportedException>(() => FalloutDialogueTopic.Read(records, Key(0xa00)), "PNAM precedes an unavailable INFO");
            WriteOverride(Group(0x01000a00, Info(0x02000b20, 0x030008ff, 0, Condition(72, 0x01000901, 1))));
            using (var records = Stack())
                Reject<FalloutPluginFormatException>(() => FalloutDialogueTopic.Read(records, Key(0xa00)), "undeclared local namespace");

            Console.WriteLine("OPENNV_CONVERSATION_SPEAKER_ADMISSION_PASS unrelatedMissingQuest=true sourceInfoRetained=true " +
                "eligibleAbsentDeletedWrongTypeRefused=true unknownSpeakerRefused=true orRandomRunOnRetained=true randomTraitNotHoisted=true " +
                "orAlternativeRetained=true questPriority=true pnam=true sayOnce=true winningOverrides=true mastIdentity=true invalidNamespaceRefused=true " +
                "resultsBeforeFault=0 nativeCampaignRetail=unverified");
        }
        finally { directory.Delete(recursive: true); }
    }

    private static float Unknown(FalloutCondition condition) =>
        throw new NotSupportedException($"Unbound authored condition {condition.Function}.");
    private static void Failure<T>(Func<FalloutConversation> create, uint topic, string diagnosis, List<FalloutFormKey> effects)
        where T : Exception
    {
        var before = effects.Count; var conversation = create();
        Reject<T>(() => conversation.Start(Key(0x900), Key(topic)), diagnosis);
        Require(conversation.Phase == "failed" && conversation.Info is null && conversation.Response is null &&
            effects.Count == before && conversation.Error!.Contains(diagnosis, StringComparison.Ordinal),
            "An unowned potentially eligible dialogue line published speech or consumed a result.");
        Reject<InvalidOperationException>(() => conversation.Start(Key(0x900), Key(topic)), "already active or faulted");
        Require(effects.Count == before, "Repeating a faulted conversation reran its result prefix.");
    }
    private static void Reject<T>(Action action, string diagnosis) where T : Exception
    {
        try { action(); }
        catch (Exception error) when (error is T && error.Message.Contains(diagnosis, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException($"Expected {typeof(T).Name} source refusal: {diagnosis}");
    }
    private static void Require(bool pass, string diagnosis)
    { if (!pass) throw new InvalidDataException(diagnosis); }

    private static byte[] Base() => Join(Header(),
        Record("NPC_", 0x900, 0, Field("EDID", Text("AuthoredSpeaker"))),
        Record("NPC_", 0x901, 0, Field("EDID", Text("IndependentSpeaker"))),
        Record("QUST", 0x800, 0, Field("DATA", [1, 10])), Record("QUST", 0x801, 0, Field("DATA", [1, 80])),
        Record("QUST", 0x802, 0x20, Field("DATA", [1, 90])),
        Topic(0xa00, Info(0xb00, 0x800, 0, Condition(72, 0x900, 1)),
            Info(0xb01, 0x8ff, 0, Condition(65000, 0, 1), Condition(72, 0x901, 1))),
        Topic(0xa01, Info(0xb02, 0x8ff, 4, Condition(72, 0x900, 1)), Info(0xb03, 0x800, 0)),
        Topic(0xa02, Info(0xb04, 0x8ff, 0, Field("ANAM", U32(0x901)), Condition(65000, 0, 1)), Info(0xb05, 0x800, 0)),
        Topic(0xa03, Info(0xb06, 0x8ff, 0, Condition(77, 0, 1), Condition(72, 0x901, 1)), Info(0xb08, 0x800, 0)),
        Topic(0xa04, Info(0xb07, 0x8ff, 0, Condition(65000, 0, 1, flags: 1), Condition(72, 0x901, 1)), Info(0xb09, 0x800, 0)),
        Topic(0xa05, Info(0xb10, 0x8ff, 0, Condition(72, 0x901, 1, runOn: 1))),
        Topic(0xa06, Info(0xb11, 0x901, 0, Condition(72, 0x900, 1))),
        Topic(0xa07, Info(0xb12, 0x802, 0, Condition(72, 0x900, 1))),
        Topic(0xa08, Info(0xb14, 0x801, 0), Info(0xb15, 0x800, 0)),
        Topic(0xa09, Info(0xb16, 0x800, 0, Condition(69, 0, 1))),
        Topic(0xa0a, Info(0xb17, 0x8ff, 0, Condition(72, 0x900, 1))),
        Topic(0xa0b, Info(0xb18, 0x8ff, 0, Condition(77, 0, 1), Condition(69, 0, 1))),
        Topic(0xa0c, Info(0xb19, 0x8ff, 0, Condition(72, 0x901, 1, flags: 1), Condition(69, 0, 1))));
    private static byte[] Header(params string[] masters)
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, 0, [Field("HEDR", data), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
    }
    private static byte[] Topic(uint id, params byte[][] infos) => Join(
        Record("DIAL", id, 0, Field("EDID", Text($"AuthoredTopic{id:x}")), Field("DATA", [0, 0])), Group(id, infos));
    private static byte[] Group(uint parent, params byte[][] records)
    {
        var body = Join(records); var bytes = new byte[24 + body.Length];
        "GRUP"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), parent); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 7);
        body.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Info(uint id, uint quest, byte flags, params byte[][] fields)
    {
        var response = new byte[24]; response[12] = 1;
        return Record("INFO", id, 0, [Field("DATA", [0, 0, flags, 0]), Field("QSTI", U32(quest)),
            Field("TRDT", response), Field("NAM1", Text("Authored response")), .. fields]);
    }
    private static byte[] Condition(ushort function, uint argument, float comparison, byte flags = 0, uint runOn = 0)
    {
        var bytes = new byte[28]; bytes[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), comparison); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), argument); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), runOn);
        return Field("CTDA", bytes);
    }
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var body = Join(fields); var bytes = new byte[24 + body.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)body.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); body.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] bytes)
    {
        var result = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)bytes.Length)); bytes.CopyTo(result, 6); return result;
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] bytes) => bytes.SelectMany(value => value).ToArray();
}
