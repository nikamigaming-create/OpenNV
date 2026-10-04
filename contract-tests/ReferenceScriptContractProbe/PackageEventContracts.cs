using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class PackageEventContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-package-results-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var acbs = new byte[24]; acbs[8] = 1;
            var references = Record("ACRE", 0x90, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x80); UInt(group, 12, 6); references.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory, "Results.esm"), Join(Header(),
                Record("CREA", 1, Field("ACBS", acbs), Field("DATA", new byte[17]), Field("SCRI", BitConverter.GetBytes(0x50u))),
                Record("SCPT", 0x50, Local(1, "counter"), Local(2, "caller", true),
                    Field("SCTX", Text("short counter\nref caller\nbegin GameMode\nend"))),
                Record("QUST", 0x60, Field("EDID", Text("PackageQuest"))),
                Record("DIAL", 0x61, Field("EDID", Text("MasterTopic"))),
                Record("IDLE", 0x70, Field("EDID", Text("EventIdle"))),
                Record("CELL", 0x80, Field("DATA", [1])), group));
            File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Join(Header("Results.esm"),
                Package(0x01000100, Event("POBA", "set counter to counter + 1\nset caller to GetSelf\nSetAV Variable05 7\nSetStage PackageQuest 16", 0x60),
                    Event("POEA", "if counter == 1\nset counter to counter + 1\nendif\nSetStage PackageQuest 20", 0x60)),
                Package(0x01000101, Event("POBA", "SetStage PackageQuest 20", 0x60),
                    Event("POEA", "set counter to counter + 1\nSetStage PackageQuest 30")),
                Package(0x01000102, Event("POEA", "set counter to counter + 1", topic: 0x60)),
                Record("DIAL", 0x61, Field("EDID", Text("WinningMasterTopic"))),
                Record("DIAL", 0x01000103, Field("EDID", Text("PatchTopic"))),
                Package(0x01000110,
                    Event("POBA", "set counter to counter + 1\nSetStage PackageQuest 40", 0x60, 0x61, 0x70),
                    Event("POEA", "set counter to counter + 1\nSetStage PackageQuest 42", 0x60, 0x01000103),
                    Event("POCA", "set counter to counter + 1")),
                Package(0x01000111, Event("POBA", "set counter to counter + 1", topic: 0x61))));
            using var records = FalloutPluginStack.Load(directory, ["Results.esm", "Override.esp"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var effects = new List<FalloutReferenceScriptEffect>();
            var topicStages = new List<short>();
            var order = new List<string>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                effects.Add(effect);
                if (effect.Kind == FalloutReferenceEffectKind.PackageEventTopic)
                {
                    topicStages.Add(quests.Stage(Key(0x60)));
                    order.Add("topic");
                    return;
                }
                if (effect.Kind != FalloutReferenceEffectKind.SetStage) throw new InvalidOperationException("Unexpected package result effect.");
                quests.EnterStage(effect.Target!.Value, effect.Stage);
                order.Add("script");
            }));
            FalloutScriptPackage Source(uint id) => FalloutScriptPackage.Read(records.GetEffective(new("Override.esp", id)));
            var lifecycle = new FalloutPackageEvents((package, kind) =>
            {
                if (package.EventPrograms.GetValueOrDefault(kind) is { } program) scripts.ExecutePackageEvent(program, Key(0x90));
            });
            lifecycle.Change(Source(0x100)); lifecycle.Change(Source(0x100));
            Require(world.Get(Key(0x90)).Read(1) == 1 && world.Get(Key(0x90)).Read(2) == records.RuntimeFormId(Key(0x90)) &&
                world.ActorValue(Key(0x90), "Variable05") == 7 && quests.Stage(Key(0x60)) == 16 &&
                effects.Single().Source == Key(0x90), "Package results lost actor locals, calling reference, actor values or master-adjusted stage binding.");
            lifecycle.Complete(); lifecycle.Complete();
            Require(world.Get(Key(0x90)).Read(1) == 2 && quests.Stage(Key(0x60)) == 20 && effects.Count == 2,
                "Package completion failed conditional execution or repeated its result.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!); cold.LoadCell(cell);
            Require(cold.Get(Key(0x90)).Read(1) == 2 && cold.Get(Key(0x90)).Read(2) == records.RuntimeFormId(Key(0x90)) &&
                cold.ActorValue(Key(0x90), "Variable05") == 7, "Package result state did not retain in the existing world snapshot.");
            lifecycle.Change(Source(0x101));
            Reject(lifecycle.Complete);
            Require(lifecycle.Error is not null && !lifecycle.Done && world.Get(Key(0x90)).Read(1) == 3 && quests.Stage(Key(0x60)) == 20,
                "An event borrowed another event's compiled references or discarded its successful prefix.");
            Reject(lifecycle.Complete); Reject(() => lifecycle.Change(null));
            Require(world.Get(Key(0x90)).Read(1) == 3, "A failed package event replayed its committed prefix.");
            var topic = Source(0x102).EventPrograms["POEA"];
            Reject(() => scripts.ExecutePackageEvent(topic, Key(0x90)));
            Require(world.Get(Key(0x90)).Read(1) == 4, "An unsupported topic prevented its preceding source script.");
            var malformed = topic with { Fields = topic.Fields.Where(field => field.Signature != "SCHR").ToArray() };
            Reject(() => scripts.ExecutePackageEvent(malformed, Key(0x90)));
            Reject(() => scripts.ExecutePackageEvent(topic, Key(0x80)));
            Require(world.Get(Key(0x90)).Read(1) == 4, "Malformed metadata or a nonactor caller mutated package result state.");
            var topicSource = Source(0x110);
            var topicLifecycle = new FalloutPackageEvents((package, kind) =>
            {
                if (package.EventPrograms.GetValueOrDefault(kind) is { } program) scripts.ExecutePackageEvent(program, Key(0x90));
                if (package.Events.GetValueOrDefault(kind) is not null) order.Add("idle");
            });
            var beforeTopics = effects.Count;
            order.Clear();
            Require(topicSource.EventPrograms["POBA"].Topic == Key(0x61) && effects.Count == beforeTopics,
                "A declared topic ran early or lost its declaring master.");
            topicLifecycle.Change(topicSource); topicLifecycle.Change(topicSource);
            var request = effects.Last();
            Require(world.Get(Key(0x90)).Read(1) == 5 && topicStages.SequenceEqual([(short)40]) &&
                order.SequenceEqual(["script", "topic", "idle"]) && request.Kind == FalloutReferenceEffectKind.PackageEventTopic &&
                request.Source == Key(0x90) && request.Target == Key(0x90) && request.Argument == topicSource.Form &&
                request.Topic == Key(0x61) && request.PackageEvent == "POBA" && records.GetEffective(request.Topic.Value).Plugin.Name == "Override.esp",
                "Package topic lost committed results, actual caller, source identity, winning topic or event/idle order.");
            topicLifecycle.Complete(); topicLifecycle.Complete();
            Require(world.Get(Key(0x90)).Read(1) == 6 && topicStages.SequenceEqual([(short)40, (short)42]) &&
                effects.Last().Topic == new FalloutFormKey("Override.esp", 0x103) && effects.Last().PackageEvent == "POEA",
                "End topic lost its separate event scope or repeated its result/request.");
            topicLifecycle.Change(null);
            Require(world.Get(Key(0x90)).Read(1) == 7 && topicStages.Count == 2, "A zero change-topic started speech.");
            var restoredEvents = new FalloutPackageEvents((package, kind) => scripts.ExecutePackageEvent(package.EventPrograms[kind], Key(0x90)));
            restoredEvents.Restore(topicSource, false);
            Require(world.Get(Key(0x90)).Read(1) == 7 && topicStages.Count == 2, "Restoration replayed the begin topic.");
            restoredEvents.Complete();
            Require(world.Get(Key(0x90)).Read(1) == 8 && topicStages.Count == 3 && topicStages.Last() == 42,
                "Restored package failed its next genuine end topic.");
            var unbound = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new NotSupportedException("Fixture has no speech owner.")));
            var failedTopic = new FalloutPackageEvents((package, kind) => unbound.ExecutePackageEvent(package.EventPrograms[kind], Key(0x90)));
            Reject(() => failedTopic.Change(Source(0x111)));
            Reject(() => failedTopic.Change(null));
            Require(failedTopic.Error is not null && world.Get(Key(0x90)).Read(1) == 9,
                "Failed speech owner discarded or replayed the committed result prefix.");
            var duplicate = topicSource.EventPrograms["POBA"] with
            { Fields = topicSource.EventPrograms["POBA"].Fields.Append(new("TNAM", BitConverter.GetBytes(0u))).ToArray() };
            Reject(() => scripts.ExecutePackageEvent(duplicate, Key(0x90)));
            Require(world.Get(Key(0x90)).Read(1) == 10 && topicStages.Count == 3,
                "Duplicate topic metadata admitted speech or skipped its preceding script.");
            Reject(() => scripts.ExecutePackageEvent(duplicate with { Kind = "Unknown" }, Key(0x90)));
            Require(world.Get(Key(0x90)).Read(1) == 10, "An untyped event mutated its source result.");
            Console.WriteLine("OPENNV_PACKAGE_EVENT_RESULTS_PASS actorScope=true ownCompiledScope=true conditional=true stage=true prefixLatch=true coldValues=true invalidAtomic=true topics=source-bound eventOrder=true winner=true actorColdLifecycle=unverified nativeVoice=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Results.esm", id);
    private static byte[] Package(uint id, params byte[][] events)
    {
        var data = new byte[12]; data[4] = 6;
        return Record("PACK", id, Field("EDID", Text("Fixture" + id)), Field("PKDT", data), Join(events));
    }
    private static byte[] Event(string kind, string source, uint? reference = null, uint topic = 0, uint idle = 0)
    {
        var header = new byte[20]; UInt(header, 4, reference is null ? 0u : 1u); UInt(header, 8, 1);
        return Join(Field(kind, []), Field("SCHR", header), Field("SCDA", [0]), Field("SCTX", Text(source)),
            reference is { } form ? Field("SCRO", BitConverter.GetBytes(form)) : [], Field("TNAM", BitConverter.GetBytes(topic)),
            idle == 0 ? [] : Field("INAM", BitConverter.GetBytes(idle)));
    }
    private static byte[] Local(uint id, string name, bool reference = false)
    {
        var data = new byte[24]; UInt(data, 0, id); data[16] = reference ? (byte)1 : (byte)0;
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported package result was accepted.");
    }
}
