using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SpeechCompletionContracts
{
    private const string Locals = "short talking\nshort completions\nref caller\nref action\nshort resultPrefix\n";
    private const string Source = Locals + "begin GameMode\nset completions to completions + 100\nend\n" +
        "begin SayToDone Other\nset completions to 1000\nend\nbegin SayToDone Topic\n" +
        "set talking to 0\nset completions to completions + 1\nset caller to GetSelf\nset action to GetActionRef\nend\n" +
        "begin SayToDone\nset completions to completions + 10\nend";

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-speech-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            OutputContracts(directory);
            WriteFixture(directory);
            using var records = FalloutPluginStack.Load(directory, ["Speech.esm", "SpeechPatch.esp"]);
            RetainedCompletion(records, directory);
            InvalidAdmission(records);
            FailedSource(records);
            DetectionPersistence(records);
            DetectionEventStateContracts();
            DetectionLifetimeContracts(records);
            Console.WriteLine("OPENNV_SPEECH_COMPLETION_CONTRACT_PASS retainedOnly=true offCellSource=true typedReceipt=true " +
                "winningScript=true sourceOnce=true ordinaryResidency=true endResultFailure=true completionFailure=true coldLocals=true noReplay=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void OutputContracts(string directory)
    {
        var game = Path.Combine(directory, "owned-game"); var mod = Path.Combine(directory, "owned-mod");
        var dependency = Path.Combine(directory, "owned-dependency");
        foreach (var input in new[] { game, mod, dependency })
            Reject(() => OwnedSpeechCompletionProbe.OutputPath(Path.Combine(input, "audit.json"), game, mod, [dependency]));
        var existing = Path.Combine(directory, "existing.json"); File.WriteAllText(existing, "retained private result");
        Reject(() => OwnedSpeechCompletionProbe.OutputPath(existing, game, mod, [dependency]));
        Reject(() => OwnedSpeechCompletionProbe.OutputPath(Path.Combine(directory, "result.txt"), game, mod, [dependency]));
        var fresh = Path.Combine(directory, "fresh.json");
        Require(OwnedSpeechCompletionProbe.OutputPath(fresh, game, mod, [dependency]) == Path.GetFullPath(fresh) &&
            !File.Exists(fresh) && File.ReadAllText(existing) == "retained private result",
            "Owned completion output validation wrote an input/existing file or created a result before source proof.");
    }

    private static void RetainedCompletion(FalloutPluginStack records, string directory)
    {
        using var world = new FalloutReferenceWorld(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = Scripts(records, world, effects);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var actor = world.Retained(Key(0x900));
        Require(scripts.Dispatch(actor.Reference, "GameMode").Error is null && actor.Read(2) == 100,
            "Resident ordinary GameMode lost its existing admission.");
        actor.Write(1, 1); actor.Write(2, 0);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x801))); world.UnloadCell(Key(0x800));
        Reject(() => scripts.Dispatch(actor.Reference, "GameMode"));
        Reject(() => scripts.Activate(actor.Reference, Key(0x14)));
        Require(ReferenceEquals(actor, world.Retained(actor.Reference)) && !world.IsResident(actor.Reference),
            "Unloading a cell replaced the existing reference state.");
        var topics = new HashSet<FalloutFormKey> { Key(0x200) };
        var receipt = new FalloutSpeechCompletionReceipt(actor.Reference, topics, Key(0x600), 1);
        topics.Add(Key(0x201));
        var owner = new FalloutSpeechCompletionEvents(); var deliveries = 0; var settled = 0;
        owner.Complete(receipt, pending =>
        {
            Require(owner.Active && ReferenceEquals(owner.Dispatching, receipt) && pending.Topics.SetEquals([Key(0x200)]),
                "Finished speech lost its immutable generation receipt while dispatching.");
            scripts.ExecuteResult(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x600))), actor.Reference, false);
            Deliver(scripts, pending, 2); ++deliveries;
        }, () =>
        {
            Require(!owner.Active && owner.Dispatching is null && actor.Read(1) == 0 && deliveries == 1,
                "Settled observer ran before receipt retirement and committed source locals.");
            ++settled;
        });
        owner.Drain(_ => throw new InvalidDataException("Finished voice replayed on the next frame."));
        Reject(() => owner.Complete(receipt, _ => ++deliveries, () => ++settled));
        Require(!owner.Active && owner.Error is null && owner.Dispatching is null && deliveries == 1 && settled == 1 &&
            actor.Read(1) == 0 && actor.Read(2) == 11 && actor.Read(3) == records.RuntimeFormId(actor.Reference) &&
            actor.Read(4) == 0 && actor.Read(5) == 1 && effects.Count == 0,
            "Off-cell completion lost source filtering/order, null action, committed results or once-only writes.");
        var saved = RoundTrip(world.Capture());
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(saved);
            var restored = Scripts(records, cold, effects);
            Require(cold.Retained(actor.Reference).Read(1) == 0 && cold.Retained(actor.Reference).Read(2) == 11 &&
                cold.Retained(actor.Reference).Read(5) == 1 && !new FalloutSpeechCompletionEvents().Active,
                "Settled completion locals did not restore independently of voice/presentation lifetime.");
            Reject(() => restored.Dispatch(actor.Reference, "GameMode"));
            cold.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            Require(restored.Dispatch(actor.Reference, "GameMode").Error is null && cold.Retained(actor.Reference).Read(2) == 111,
                "Cold resident GameMode was broadened, disabled or replayed speech completion.");
        }
        File.WriteAllBytes(Path.Combine(directory, "SpeechDrift.esp"), Join(Header("Speech.esm"),
            Script(0x400, Source + "\n; changed winning source")));
        using var drifted = FalloutPluginStack.Load(directory, ["Speech.esm", "SpeechPatch.esp", "SpeechDrift.esp"]);
        using var rejected = new FalloutReferenceWorld(drifted);
        Reject(() => rejected.Restore(saved));
        Require(rejected.InstanceCount == 0, "Speech completion bypassed cold winning-script hash validation.");
    }

    private static void InvalidAdmission(FalloutPluginStack records)
    {
        using (var absent = new FalloutReferenceWorld(records))
        {
            var scripts = Scripts(records, absent, []);
            Reject(() => scripts.DispatchSpeechCompletion(Receipt(Key(0x900))));
            Require(absent.InstanceCount == 0, "Completion created an absent reference or presentation proxy.");
        }
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800))); world.UnloadCell(Key(0x800));
        var source = Scripts(records, world, []); var before = JsonSerializer.Serialize(world.Capture());
        foreach (var receipt in new[]
        {
            new FalloutSpeechCompletionReceipt(Key(0x903), new HashSet<FalloutFormKey> { Key(0x200) }, Key(0x600), 1),
            new FalloutSpeechCompletionReceipt(Key(0x999), new HashSet<FalloutFormKey> { Key(0x200) }, Key(0x600), 1),
            new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x300) }, Key(0x600), 1),
            new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x999) }, Key(0x600), 1),
            new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x201) }, Key(0x600), 1),
            new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x200) }, Key(0x999), 1),
            new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x200) }, Key(0x300), 1),
        }) Reject(() => source.DispatchSpeechCompletion(receipt));
        Reject(() => new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey>(), Key(0x600), 1));
        Reject(() => new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x200) }, Key(0x600), 0));
        Reject(() => new FalloutSpeechCompletionReceipt(Key(0x900), new HashSet<FalloutFormKey> { Key(0x200) }, generation: 1));
        Require(before == JsonSerializer.Serialize(world.Capture()), "Invalid speech source admission changed retained state.");
        var actor = world.Retained(Key(0x900)); actor.Deleted = true;
        Reject(() => source.DispatchSpeechCompletion(Receipt(actor.Reference))); actor.Deleted = false;
        actor.DeletePending = true; Reject(() => source.DispatchSpeechCompletion(Receipt(actor.Reference))); actor.DeletePending = false;
        var failed = new FalloutSpeechCompletionEvents(); var receiptWithoutOwner = Receipt(actor.Reference);
        Reject(() => failed.Complete(receiptWithoutOwner, _ => throw new NotSupportedException("Source event owner is absent.")));
        Reject(() => failed.Complete(receiptWithoutOwner, pending => Deliver(source, pending)));
        Require(failed.Active && ReferenceEquals(failed.Dispatching, receiptWithoutOwner) && actor.Read(2) == 0,
            "Absent completion owner dropped its receipt or later replayed it.");
        foreach (var reference in new[] { Key(0x901), Key(0x902) })
            Deliver(source, Receipt(reference), 2);
        var empty = new FalloutSpeechCompletionEvents(); empty.Mark(actor.Reference, Key(0x200));
        empty.Drain(pending => Deliver(source, pending, 2));
        Require(!empty.Active && actor.Read(2) == 11, "Empty speech lost retained topic-only source dispatch.");
    }

    private static void FailedSource(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        world.UnloadCell(Key(0x800)); var scripts = Scripts(records, world, []);
        var actor = world.Retained(Key(0x900)); actor.Write(1, 1);
        var endReceipt = Receipt(actor.Reference, 0x602); var endOwner = new FalloutSpeechCompletionEvents(); var settled = 0;
        void FinishEnd() { endOwner.Complete(endReceipt, pending =>
            {
                scripts.ExecuteResult(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x602))), actor.Reference, false);
                Deliver(scripts, pending);
            }, () => ++settled); }
        Reject(FinishEnd); Reject(FinishEnd);
        Require(endOwner.Active && ReferenceEquals(endOwner.Dispatching, endReceipt) && actor.Read(5) == 1 && actor.Read(1) == 1 &&
            actor.Read(2) == 0 && settled == 0, "Failed INFO end results cleared/replayed the prefix or announced settled speech.");
        var failedActor = world.Retained(Key(0x910)); failedActor.Write(1, 1);
        var receipt = Receipt(failedActor.Reference); var owner = new FalloutSpeechCompletionEvents();
        void FinishEvent() { owner.Complete(receipt, pending => Deliver(scripts, pending), () => ++settled); }
        Reject(FinishEvent); Reject(FinishEvent); Reject(() => owner.Drain(_ => ++settled));
        Require(owner.Active && owner.Error is not null && ReferenceEquals(owner.Dispatching, receipt) &&
            failedActor.Read(2) == 1 && failedActor.Read(1) == 1 && failedActor.ScriptError?.StartsWith("SayToDone:") == true && settled == 0,
            "Failed completion suffix lost its consumed write, source error or generation, or replayed/announced completion.");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(RoundTrip(world.Capture()));
        var coldScripts = Scripts(records, cold, []); var stopped = coldScripts.DispatchSpeechCompletion(receipt);
        Require(stopped.Blocks == 0 && stopped.Error == failedActor.ScriptError && cold.Retained(failedActor.Reference).Read(2) == 1,
            "Cold stopped source cleared its error or replayed a historical completion prefix.");
        var invalidSource = scripts.DispatchSpeechCompletion(Receipt(Key(0x920)));
        Require(invalidSource.Error is not null && invalidSource.Blocks == 0 && world.Retained(Key(0x920)).Read(2) == 0,
            "Missing source program was silently acknowledged.");
    }

    private static FalloutSpeechCompletionReceipt Receipt(FalloutFormKey speaker, uint info = 0x600) =>
        new(speaker, new HashSet<FalloutFormKey> { Key(0x200) }, Key(info), 1);
    private static FalloutReferenceScripts Scripts(FalloutPluginStack records, FalloutReferenceWorld world,
        List<FalloutReferenceScriptEffect> effects) => new(records, world, new(records), new((_, _) => false, effects.Add));
    private static void Deliver(FalloutReferenceScripts scripts, FalloutSpeechCompletionReceipt receipt, int? blocks = null)
    {
        var result = scripts.DispatchSpeechCompletion(receipt);
        if (result.Error is not null) throw new NotSupportedException(result.Error);
        Require(blocks is null || result.Blocks == blocks, "Completion changed matching source block admission.");
    }
    private static void WriteFixture(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, "Speech.esm"), Join(Header(),
            Record("DIAL", 0x200, Field("EDID", Text("Topic"))), Record("DIAL", 0x201, Field("EDID", Text("Other"))),
            Record("QUST", 0x500), Script(0x400, Source.Replace("set completions to completions + 10", "set completions to completions + 20", StringComparison.Ordinal)),
            Script(0x410, Locals + "begin SayToDone Topic\nset completions to completions + 1\nMissingSpeechSuffix\nset talking to 0\nend"),
            Record("SCPT", 0x420, Declarations()),
            Script(0x430, DetectionSource),
            Record("GMST", 0x700, Field("EDID", Text("fDetectionEventExpireTime")), Field("DATA", BitConverter.GetBytes(10f))),
            Base("CREA", 0x300, 0x400), Base("NPC_", 0x301, 0x400), Base("TACT", 0x302, 0x400), Base("ACTI", 0x303, 0x400),
            Base("CREA", 0x310, 0x410), Base("CREA", 0x320, 0x420),
            Base("CREA", 0x330, 0x430),
            Group(0x200, 7, Info(0x600, "set Speaker.resultPrefix to Speaker.resultPrefix + 1"),
                Info(0x602, "set Speaker.resultPrefix to Speaker.resultPrefix + 1\nMissingResultSuffix\nset Speaker.talking to 0")),
            Record("CELL", 0x800, Field("DATA", [1])), Record("CELL", 0x801, Field("DATA", [1])),
            Group(0x800, 6, Reference("ACRE", 0x900, 0x300, "Speaker"), Reference("ACHR", 0x901, 0x301),
                Reference("REFR", 0x902, 0x302), Reference("REFR", 0x903, 0x303), Reference("ACRE", 0x910, 0x310),
                Reference("ACRE", 0x920, 0x320), Reference("ACRE", 0x930, 0x330))));
        File.WriteAllBytes(Path.Combine(directory, "SpeechPatch.esp"), Join(Header("Speech.esm"), Script(0x400, Source)));
    }
    private static byte[] Base(string kind, uint id, uint script) => Record(kind, id, Field("SCRI", BitConverter.GetBytes(script)));
    private static byte[] Reference(string kind, uint id, uint basis, string? name = null) => Record(kind, id,
        Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]), name is null ? [] : Field("EDID", Text(name)));
    private static byte[] Info(uint id, string end) => Record("INFO", id, Field("DATA", [1, 0, 0]),
        Field("QSTI", BitConverter.GetBytes(0x500u)), Field("NEXT", []), Field("SCRO", BitConverter.GetBytes(0x900u)), Field("SCTX", Text(end)));
    private static byte[] Script(uint id, string source) => Record("SCPT", id, Declarations(), Field("SCRO", BitConverter.GetBytes(0x200u)),
        Field("SCRO", BitConverter.GetBytes(0x201u)), Field("SCRO", BitConverter.GetBytes(0x900u)), Field("SCTX", Text(source)));
    private static byte[] Declarations() => Join(Local(1, "talking"), Local(2, "completions"), Local(3, "caller", true), Local(4, "action", true), Local(5, "resultPrefix"));
    private static byte[] Local(uint index, string name, bool reference = false)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index); if (reference) data[16] = 1;
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
        return Join(header, data);
    }
    private static byte[] Group(uint label, uint type, params byte[][] contents)
    {
        var data = Join(contents); var header = new byte[24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length + 24);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), label); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), type);
        return Join(header, data);
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var header = new byte[6]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)data.Length)); return Join(header, data);
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static FalloutFormKey Key(uint id) => new("Speech.esm", id);
    private static FalloutReferenceSnapshot[] RoundTrip(IReadOnlyList<FalloutReferenceSnapshot> snapshots) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Invalid or stopped speech completion was accepted.");
    }
}
