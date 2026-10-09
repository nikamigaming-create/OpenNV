using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

internal static class RadioConversationContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-radio-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var range = new byte[16]; range[4] = 1;
            File.WriteAllBytes(Path.Combine(directory, "Radio.esm"), Join(Record("TES4", 0, 0),
                Record("VTYP", 20, 0, Field("EDID", Text("Voice"))), Record("RACE", 40, 0),
                Record("NPC_", 10, 0, Field("ACBS", new byte[24]), Field("VTCK", BitConverter.GetBytes(20u)), Field("RNAM", BitConverter.GetBytes(40u))),
                Record("TACT", 11, 0x20000, Field("VNAM", BitConverter.GetBytes(20u))),
                Record("QUST", 1101, 0, Field("EDID", Text("RadioQuest")), Field("DATA", [1, 20])),
                Record("CELL", 800, 0, Field("DATA", [1])),
                Group(800, 6, Record("REFR", 900, 0, Field("NAME", BitConverter.GetBytes(11u)), Field("DATA", new byte[24]), Field("XRDO", range))),
                Record("DIAL", 200, 0, Field("EDID", Text("RadioHello")), Field("DATA", [7, 0])),
                Group(200, 7, Info(301, 0, 8), Info(302, 1, 9)),
                Record("DIAL", 201, 0, Field("EDID", Text("OrdinaryTopic")), Field("DATA", [1, 0]))));
            using var records = FalloutPluginStack.Load(directory, ["Radio.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records); var phase = 0;
            var radio = new FalloutRadioConversation(records, world, quests,
                (_, condition) => throw new InvalidDataException($"Unexpected condition {condition.Function}."),
                _ => phase, new HashSet<FalloutFormKey>());
            Reject(() => radio.Start(Key(900), Key(201)));
            Require(radio.Station is null && !radio.Active, "Invalid radio topic changed the transmitter.");
            radio.Start(Key(900));
            Reject(() => radio.CaptureFinishedState());
            Require(radio.Info?.Record.FormKey == Key(301) && radio.VoiceIdentity().Actor == Key(10),
                "Radio lost station predicate identity, default topic or remote voice actor.");
            CheckActiveCold(records, world, quests, radio.CaptureActiveState());
            Reject(() => radio.Start(Key(900)));
            Require(radio.Info?.Record.FormKey == Key(301) && radio.CompletedLines == 0, "Rejected interruption restarted a source line.");
            phase = 1; radio.CompleteLine();
            Require(radio.Info?.Record.FormKey == Key(302) && radio.CompletedLines == 1, "Radio reselected against stale source state.");
            radio.CompleteLine();
            Require(!radio.Active && radio.CompletedLines == 2 && !world.GetBroadcastState(Key(900)), "Goodbye changed mode or lost completion.");
            CheckFinishedCold(records, world, quests, radio.CaptureFinishedState());
            Reject(() => radio.CompleteLine());
            phase = 0; radio.Start(Key(900)); phase = 2; radio.CompleteLine();
            Require(!radio.Active && radio.CompletedLines == 3 && !world.GetBroadcastState(Key(900)),
                "An exhausted, fully evaluated link set faulted instead of ending its original radio request.");
            Reject(() => radio.CompleteLine());
            world.SetBroadcastState(Key(900), 1);
            Reject(() => radio.Start(Key(900)));
            Require(!radio.Active && world.GetBroadcastState(Key(900)), "Missing continuous owner changed mode.");
            Console.WriteLine("OPENNV_RADIO_CONVERSATION_PASS fullReader=true stationPredicates=true remoteVoice=true defaultTopic=true " +
                "sourceLinks=true freshConditions=true goodbye=true endedCold=true sourceBound=true noReplay=true activeCursorCold=true finishedOnlyCaptureRefused=true " +
                "modePreserved=true interruptionAtomic=true continuousRefused=true");
        }
        finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }
    private static void CheckActiveCold(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutActiveRadioConversationSnapshot snapshot)
    {
        var saved = JsonSerializer.Deserialize<FalloutActiveRadioConversationSnapshot>(JsonSerializer.Serialize(snapshot))!;
        var restoring = true;
        var cold = new FalloutRadioConversation(records, world, quests,
            (_, _) => throw new InvalidDataException("Cold radio replayed a source condition."),
            _ => restoring ? throw new InvalidDataException("Cold radio reselected the current line.") : 1,
            new HashSet<FalloutFormKey>(), _ => throw new InvalidDataException("Cold radio consumed source RNG."));
        cold.RestoreActiveState(saved);
        Require(cold.Info?.Record.FormKey == Key(301) && cold.CompletedLines == 0 &&
            JsonSerializer.Serialize(cold.CaptureActiveState()) == JsonSerializer.Serialize(saved),
            "Cold radio changed its selected line or completion history.");
        Reject(() => cold.RestoreActiveState(saved));
        restoring = false; cold.CompleteLine();
        Require(cold.Info?.Record.FormKey == Key(302) && cold.CompletedLines == 1,
            "Cold radio failed to evaluate the next source link against changed quest state.");
        var samples = new FalloutPcmPlaybackSnapshot(120, 32000, 1, 48000,
            new(FalloutSoundLoopMode.None, 0, 0), 33.25, 0, true, false);
        var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(301)));
        var results = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Empty result dispatched an effect.")));
        var caller = world.DialogueSubject(Key(900));
        var beginReceipt = results.ExecuteResultOwned(info, caller, true);
        var endReceipt = results.ExecuteResultOwned(info, caller, false);
        Require(beginReceipt.Authority == FalloutScriptResultAuthority.None && beginReceipt.Invocation == 0 &&
            endReceipt.Authority == FalloutScriptResultAuthority.None && world.ScriptManualSaves.EnteredInvocations == 0,
            "Source absence invented an executed instruction or lease.");
        var voice = new FalloutActiveRadioVoiceSnapshot(saved, 1, 0,
            new(Key(10), Key(10), Key(20), "Voice", Key(301), "Radio.esm", 1,
                "sound/voice/radio.esm/voice/source_0000012d_1.ogg", "sound/voice/radio.esm/voice/source_0000012d_1.lip"),
            new('a', 64), null, new('b', 64), samples, false, false, beginReceipt, endReceipt);
        var history = new FalloutNativeFinishedSpeechSnapshot([new(Key(900), 1, 0)], 0, 0, 0, 0, 0, null,
            SettledHistory: new FalloutSpeechCompletionEvents().CaptureHistory(), FinishedRadio: [], ActiveRadio: [voice]);
        RuntimeNativeSpeech.ValidateFinishedState(records, world, history);
        world.SetBroadcastState(Key(900), 1);
        RuntimeNativeSpeech.ValidateFinishedState(records, world, history);
        world.SetBroadcastState(Key(900), 0);
        void RejectVoice(FalloutActiveRadioVoiceSnapshot invalid) => Reject(() =>
            RuntimeNativeSpeech.ValidateFinishedState(records, world, history with { ActiveRadio = [invalid] }));
        RejectVoice(voice with { BeginResults = null });
        RejectVoice(voice with { EndResults = null });
        RejectVoice(voice with { BeginResults = beginReceipt with { Caller = Key(10) } });
        RejectVoice(voice with { BeginResults = endReceipt });
        RejectVoice(voice with { BeginResults = beginReceipt with { Invocation = 1 } });
        RejectVoice(voice with { BeginResults = beginReceipt with { Completed = false } });
        var minimal = new FalloutNativeCampaignState(FalloutNativeCampaignSave.ExpectedSchema, "", default, "", 0, "",
            null!, null!, [], [], [], [], [], [], [], FinishedSpeech: history);
        FalloutNativeCampaignSave.ValidateResultAuthorityVersion(minimal);
        Reject(() => FalloutNativeCampaignSave.ValidateResultAuthorityVersion(minimal with
        { Schema = "opennv-native-fnv-campaign-save/v49" }));
        Reject(() => FalloutNativeCampaignSave.ValidateResultAuthorityVersion(minimal with
        { Schema = "opennv-native-fnv-campaign-save/v49", FinishedSpeech = history with
            { ActiveRadio = [voice with { BeginResults = null, EndResults = null }] } }));
        Reject(() => FalloutNativeCampaignSave.ValidateResultAuthorityVersion(minimal with
        { Schema = "opennv-native-fnv-campaign-save/v49", FinishedSpeech = history with { ActiveRadio = [] } }));
        RejectVoice(voice with { Generation = 2 });
        RejectVoice(voice with { ResponseIndex = 1 });
        RejectVoice(voice with { Binding = voice.Binding with { Actor = Key(11) } });
        RejectVoice(voice with { AudioSha256 = "missing" });
        RejectVoice(voice with { Samples = samples with { Position = double.NaN } });
        RejectVoice(voice with { Samples = samples with { Position = samples.Frames } });
        RejectVoice(voice with { Samples = samples with { Playing = false } });
        RejectVoice(voice with { Samples = samples with { Playing = false, StartPending = true } });
        RejectVoice(voice with { Samples = samples with { Loops = 1 } });
        RejectVoice(voice with { Samples = samples with { Releasing = true } });
        RejectVoice(voice with { Advance = true });
        RejectVoice(voice with { Conversation = saved with { InfoSha256 = new('f', 64) } });
        RejectVoice(voice with { Conversation = saved with { Info = Key(201) } });
        RuntimeNativeSpeech.ValidateFinishedState(records, world, history with { ActiveRadio =
            [voice with { Samples = samples with { Playing = false, Position = 0, StartPending = true } }] });
        RuntimeNativeSpeech.ValidateFinishedState(records, world, history with { ActiveRadio =
            [voice with { Samples = samples with { Playing = false, Position = samples.Frames }, Advance = true }] });
        Reject(() => RuntimeNativeSpeech.ValidateFinishedState(records, world, history with { ActiveRadio = [voice, voice] }));
    }
    private static void CheckFinishedCold(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutFinishedRadioConversationSnapshot snapshot)
    {
        var restored = JsonSerializer.Deserialize<FalloutFinishedRadioConversationSnapshot>(JsonSerializer.Serialize(snapshot))!;
        var restoring = true;
        var cold = new FalloutRadioConversation(records, world, quests,
            (_, _) => throw new InvalidDataException("Ended restoration replayed a source condition."),
            _ => restoring ? throw new InvalidDataException("Ended restoration reselected INFO.") : 0,
            new HashSet<FalloutFormKey>(), _ => throw new InvalidDataException("Ended restoration consumed RNG."));
        cold.RestoreFinishedState(restored);
        Require(!cold.Active && cold.Info is null && cold.CompletedLines == 2 &&
            JsonSerializer.Serialize(cold.CaptureFinishedState()) == JsonSerializer.Serialize(snapshot),
            "Ended radio lost its exact station/topic/history or restarted audio.");
        Reject(() => cold.RestoreFinishedState(restored));
        restoring = false; cold.Start(Key(900)); cold.CompleteLine();
        Require(cold.Active && cold.CompletedLines == 3, "A later source Start lost cumulative cold radio history.");
        Reject(() => cold.CaptureFinishedState());
        foreach (var invalid in new[]
        {
            restored with { CompletedLines = -1 }, restored with { CompletedLines = long.MaxValue },
            restored with { ReferenceSha256 = new('0', 64) }, restored with { BaseSha256 = new('0', 64) },
            restored with { TopicSha256 = new('0', 64) }, restored with { Topic = Key(201) },
            restored with { Station = restored.Station with { Continuous = true } }
        })
        {
            var rejected = new FalloutRadioConversation(records, world, quests, (_, _) => 0, _ => 0, new HashSet<FalloutFormKey>());
            Reject(() => rejected.RestoreFinishedState(invalid));
            Require(rejected.Station is null && rejected.Topic is null && !rejected.Active && rejected.CompletedLines == 0,
                "Rejected ended radio partially changed its source/history.");
        }
    }
    private static byte[] Info(uint id, int phase, byte flags)
    {
        var response = new byte[16]; response[12] = 1;
        var identity = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(identity.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(identity.AsSpan(8), 72); BinaryPrimitives.WriteUInt32LittleEndian(identity.AsSpan(12), 11);
        var condition = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(condition.AsSpan(4), phase);
        BinaryPrimitives.WriteUInt16LittleEndian(condition.AsSpan(8), 58); BinaryPrimitives.WriteUInt32LittleEndian(condition.AsSpan(12), 1101);
        return Record("INFO", id, 0, Field("DATA", [7, 0, flags, 0]), Field("QSTI", BitConverter.GetBytes(1101u)),
            Field("TRDT", response), Field("NAM1", Text("Announcement")), Field("CTDA", identity), Field("CTDA", condition),
            Field("TCLT", BitConverter.GetBytes(200u)), Field("ANAM", BitConverter.GetBytes(10u)));
    }
    private static FalloutFormKey Key(uint id) => new("Radio.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Missing radio owner was admitted.");
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Group(uint label, int type, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), type); payload.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string name, byte[] bytes)
    {
        var result = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)bytes.Length)); bytes.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, uint flags, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); payload.CopyTo(result, 24); return result;
    }
}
