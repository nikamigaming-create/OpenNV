using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class SayToContracts
{
    internal static void Run()
    {
        var commands = FalloutDialogueTopic.SayToCommands("Actor.SayTo player Topic 1\nActor.SayTo player Topic 0\nActor.SayTo player Topic -2\nActor.SayTo player Topic");
        Require(commands.Select(item => item.ForceSubtitles).SequenceEqual([true, false, false, false]), "SayTo lost its optional positive subtitle flag.");
        Reject(() => FalloutDialogueTopic.SayToCommands("Actor.SayTo player Topic 1 1"));
        Reject(() => FalloutSayToCommand.SubtitleFlag(.5)); Reject(() => FalloutSayToCommand.SubtitleFlag(double.NaN));
        Reject(() => FalloutSayToCommand.SubtitleFlag((double)int.MaxValue + 1));
        var directory = Path.Combine(Path.GetTempPath(), "opennv-sayto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string program = "short count\nshort talking\nshort flag\nbegin GameMode\nif count == 0\nSayTo player Topic flag\nset talking to 1\nendif\nend\n" +
                "begin SayToDone Other\nset count to 1000\nend\nbegin SayToDone Topic\nset talking to 0\nset count to count + 1\nend\n" +
                "begin SayToDone\nset count to count + 10\nend";
            File.WriteAllBytes(Path.Combine(directory, "Dialogue.esm"), Join(Record("TES4", 0),
                Record("DIAL", 0x200, Field("EDID", Text("Topic"))), Record("DIAL", 0x201, Field("EDID", Text("Other"))),
                Record("SCPT", 0x400, Local(1, "count"), Local(2, "talking"), Local(3, "flag"),
                    Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCRO", BitConverter.GetBytes(0x200u)), Field("SCRO", BitConverter.GetBytes(0x201u)), Field("SCTX", Text(program))),
                Record("ACTI", 0x300, Field("SCRI", BitConverter.GetBytes(0x400u))),
                Group(0x800, 6, Join(Record("CELL", 0x800, Field("DATA", [1])),
                    Record("REFR", 0x900, Field("NAME", BitConverter.GetBytes(0x300u)), Field("DATA", new byte[24]))))));
            using var records = FalloutPluginStack.Load(directory, ["Dialogue.esm"]);
            using var world = new FalloutReferenceWorld(records); world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var effects = new List<FalloutReferenceScriptEffect>(); var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
            world.Get(Key(0x900)).Write(3, 5);
            var sent = scripts.Dispatch(Key(0x900), "GameMode");
            Require(sent.Error is null && effects.Single() is { Kind: FalloutReferenceEffectKind.SayTo, ForceSubtitles: true } &&
                effects[0].Topic == Key(0x200) && world.Get(Key(0x900)).Read(2) == 1, "Source flag/local selection or the executed suffix was lost.");
            var done = scripts.DispatchFrame(Key(0x900), [new("SayToDone", Topic: Key(0x200)), new("GameMode")], 0);
            Require(done.All(item => item.Error is null) && done.Single(item => item.Event == "SayToDone").Blocks == 2 &&
                world.Get(Key(0x900)).Read(1) == 11 && world.Get(Key(0x900)).Read(2) == 0 && effects.Count == 2,
                "SayToDone lost typed topic filtering, unfiltered blocks or authored event order.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(saved); coldWorld.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var cold = new FalloutReferenceScripts(records, coldWorld, quests, new((_, _) => false, effects.Add));
            cold.Dispatch(Key(0x900), "GameMode");
            Require(effects.Count == 2 && coldWorld.Get(Key(0x900)).Read(1) == 11, "Completed actor locals did not retain cold.");
            Reject(() => scripts.Dispatch(Key(0x900), "SayToDone"));
            Reject(() => scripts.Dispatch(Key(0x900), "SayToDone", topic: Key(0x300)));
            Reject(() => scripts.Dispatch(Key(0x900), "SayToDone", actor: Key(0x200)));
            Require(world.Get(Key(0x900)).Read(1) == 11, "Invalid event admission mutated actor locals.");
            void Execute(string body) => scripts.ExecuteProgram(records.GetEffective(Key(0x900)), records.GetEffective(Key(0x400)),
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Execute("SayTo player Topic 0"); Require(!effects[^1].ForceSubtitles, "Zero flag forced subtitles.");
            Execute("SayTo player Topic"); Require(!effects[^1].ForceSubtitles, "Omitted flag forced subtitles.");
            Execute("SayTo player Topic -1"); Require(!effects[^1].ForceSubtitles, "Negative flag forced subtitles.");
            Execute("Say Topic 1"); Require(effects[^1] is { Kind: FalloutReferenceEffectKind.Say, ForceSubtitles: true, Argument: null }, "Say invented a listener or lost its forced subtitle.");
            Execute("Say Topic"); Require(!effects[^1].ForceSubtitles, "Omitted Say flag forced subtitles.");
            Execute("Say Topic -2"); Require(!effects[^1].ForceSubtitles, "Negative Say flag forced subtitles.");
            var count = effects.Count;
            Reject(() => Execute("SayTo player Topic .5\nset count to 99"));
            Reject(() => Execute("SayTo player Topic 1 1\nset count to 99"));
            Reject(() => Execute("Say Topic .5\nset count to 99"));
            Reject(() => Execute("Say Topic 1 player\nset count to 99"));
            Reject(() => Execute("SayTo player 768 1\nset count to 99"));
            Require(effects.Count == count && world.Get(Key(0x900)).Read(1) == 11, "Rejected command applied an effect or ran its suffix.");
            EmptyCompletions(records);
            Console.WriteLine("OPENNV_SAYTO_CONTRACT_PASS optionalSubtitle=true typedTopicEvent=true authoredOrder=true prefix=true coldLocals=true emptyDeferred=true topicCoalescing=true failureLatch=true fourthArgumentUnbound=true");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void EmptyCompletions(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var completions = new FalloutSpeechCompletionEvents();
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, effect =>
        {
            Require(effect.Kind == FalloutReferenceEffectKind.SayTo && effect.Topic is not null, "Empty fixture lost typed speech.");
            completions.Mark(effect.Target!.Value, effect.Topic!.Value);
        }));
        var sent = scripts.Dispatch(Key(0x900), "GameMode");
        Require(sent.Error is null && completions.Active && world.Get(Key(0x900)).Read(2) == 1 &&
            world.Get(Key(0x900)).Read(1) == 0, "Empty completion ran inline before the calling script's suffix.");
        completions.Mark(Key(0x900), Key(0x200));
        completions.Mark(Key(0x900), Key(0x201));
        var deliveries = 0;
        completions.Drain((_, _) => ++deliveries, () => false);
        Require(deliveries == 0 && completions.Active, "Paused completion retired pending source events.");
        completions.Drain((actor, topics) =>
        {
            Require(topics.SetEquals([Key(0x200), Key(0x201)]), "Empty registrations lost or duplicated source topics.");
            var result = scripts.DispatchFrame(actor, [new("SayToDone", Topics: topics)], 0).Single();
            Require(result.Error is null && result.Blocks == 3, "Coalesced completion did not execute each authored matching block once.");
            ++deliveries;
            completions.Mark(actor, Key(0x200));
        });
        Require(deliveries == 1 && completions.Active && world.Get(Key(0x900)).Read(1) == 1011 &&
            world.Get(Key(0x900)).Read(2) == 0, "Completion lost source order, ran an unfiltered block twice or delivered a new request recursively.");
        completions.Drain((actor, topics) =>
        {
            var result = scripts.DispatchFrame(actor, [new("SayToDone", Topics: topics)], 0).Single();
            Require(result.Error is null && result.Blocks == 2, "Deferred next-frame topic admitted another filter.");
            ++deliveries;
        });
        completions.Drain((_, _) => ++deliveries);
        Require(deliveries == 2 && !completions.Active && world.Get(Key(0x900)).Read(1) == 1022, "Empty completion replayed after successful delivery.");
        void Invalid(FalloutReferenceScriptEvent admission) => Reject(() => scripts.DispatchFrame(Key(0x900), [admission], 0));
        Invalid(new("SayToDone", Topics: new HashSet<FalloutFormKey>()));
        Invalid(new("SayToDone", Topic: Key(0x200), Topics: new HashSet<FalloutFormKey> { Key(0x200) }));
        Invalid(new("SayToDone", Topics: new HashSet<FalloutFormKey> { Key(0x300) }));
        Invalid(new("GameMode", Topics: new HashSet<FalloutFormKey> { Key(0x200) }));
        Require(world.Get(Key(0x900)).Read(1) == 1022, "Rejected topic registration mutated source locals.");
        completions.Mark(Key(0x900), Key(0x200));
        var prefix = 0;
        Reject(() => completions.Drain((_, _) => { ++prefix; throw new NotSupportedException("Source completion prefix failed."); }));
        Reject(() => completions.Drain((_, _) => ++prefix));
        Reject(() => completions.Mark(Key(0x900), Key(0x201)));
        Require(prefix == 1 && completions.Active && completions.Error == "Source completion prefix failed.", "Failed completion lost its pending prefix or replayed it.");
        var recursive = new FalloutSpeechCompletionEvents(); recursive.Mark(Key(0x900), Key(0x200));
        Reject(() => recursive.Drain((_, _) => recursive.Drain((_, _) => { })));
        Require(recursive.Active && recursive.Error is not null, "Recursive completion dispatch was accepted or lost its failure.");
    }
    private static FalloutFormKey Key(uint id) => new("Dialogue.esm", id);
    private static byte[] Local(uint index, string name) { var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index); return Join(Field("SLSD", data), Field("SCVR", Text(name))); }
    private static byte[] Record(string signature, uint id, params byte[][] fields) { var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id); return Join(header, data); }
    private static byte[] Group(uint id, uint type, byte[] data) { var header = new byte[24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(header, 0); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length + 24); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), id); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), type); return Join(header, data); }
    private static byte[] Field(string signature, byte[] data) { var header = new byte[6]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)data.Length); return Join(header, data); }
    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text + "\0");
    private static byte[] Join(params byte[][] data) => data.SelectMany(item => item).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FalloutPluginFormatException) { return; } throw new InvalidDataException("Invalid SayTo input was admitted."); }
}
