using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class QuestStagePersistenceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-closed-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Base.esm");
        try
        {
            File.WriteAllBytes(path, Source(1));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var paused = false;
            var calls = new List<string>();
            FalloutQuestStages? stages = null;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.SetStage) throw new InvalidDataException("Unexpected fixture effect.");
                stages!.Enter(effect.Target!.Value, effect.Stage);
            }, Command: (_, _, command, _) =>
            {
                calls.Add(command);
                if (command != "Hold") throw new NotSupportedException("Original fixture owner: " + command);
                paused = true;
            }));
            stages = new(records, quests, scripts.StageSteps,
                _ => throw new InvalidDataException("Unexpected fixture condition."), () => !paused);
            stages.Enter(Key(0x20), 0);
            Require(stages.HasPendingResults && quests.Variable(Key(0x20), 1) == 1, "Pending stage did not retain its prefix.");
            Reject(() => stages.CaptureResults());
            paused = false;
            var failureException = CaptureFailure(stages.Continue);
            var failure = failureException.Message;
            Require(!stages.HasPendingResults && stages.HasUnfinishedResults && quests.Variable(Key(0x20), 1) == 11 &&
                quests.Variable(Key(0x21), 1) == 1 && calls.SequenceEqual(["Hold", "UnknownReachedCommand"]),
                "Closed nested failure changed its consumed effects or retained an active iterator.");
            var saved = Copy(stages.CaptureResults().ToArray());
            var driverFailure = stages.ClosedFailureFor(failureException);
            Require(driverFailure is { } retained && retained.Quest == Key(0x20) && retained.Stage == 0 && retained.Error == failure &&
                stages.ClosedFailureFor(new NotSupportedException(failure)) is null,
                "Driver source failure was inferred from equal text rather than the original failed invocation.");
            FalloutQuestStages.ValidateDriverFailure(saved, driverFailure);
            Reject(() => FalloutQuestStages.ValidateDriverFailure(saved, driverFailure! with { Stage = 10 }));
            Reject(() => FalloutQuestStages.ValidateDriverFailure(saved, driverFailure! with { Error = "Different fault." }));
            Require(saved.Length == 2 && saved.Single(item => item.Quest == Key(0x20)).Steps == 3 &&
                saved.Single(item => item.Quest == Key(0x21)).Steps == 1 && saved.All(item => !item.Completed && item.Error == failure),
                "Closed nested failure lost an exact step count or source error.");
            var questSnapshot = Copy(quests.Capture().ToArray());
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(questSnapshot);
            var restoredCalls = 0; var restoredConditions = 0;
            FalloutQuestStages? coldStages = null;
            var coldScripts = new FalloutReferenceScripts(records, coldWorld, coldQuests, new((_, _) => false, effect =>
            {
                ++restoredCalls;
                coldStages!.Enter(effect.Target!.Value, effect.Stage);
            }, Command: (_, _, _, _) => ++restoredCalls));
            coldStages = new(records, coldQuests, coldScripts.StageSteps, _ => { ++restoredConditions; return 0; });
            coldStages.RestoreResults(saved);
            Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(coldStages.CaptureResults()),
                "Cold result receipt changed its winning identity, exact progress or failure.");
            Require(Reject(() => coldStages.Enter(Key(0x20), 0)) == failure &&
                Reject(() => coldStages.Enter(Key(0x21), 0)) == failure && restoredCalls == 0 && restoredConditions == 0 &&
                JsonSerializer.Serialize(questSnapshot) == JsonSerializer.Serialize(coldQuests.Capture()),
                "Cold failed Enter replayed source prefix, predicates or consumed quest state.");
            coldStages.Continue();
            Require(restoredCalls == 0, "Cold closed journal invented a pending continuation.");
            Reject(() => coldStages.RestoreResults(saved));

            var first = saved[0];
            foreach (var invalid in new FalloutQuestStageResultSnapshot[][]
            {
                [first, first],
                [first with { QuestSha256 = new('0', 64) }],
                [first with { WinningPlugin = "Different.esm" }],
                [first with { Steps = -1 }],
                [first with { Completed = true }],
                [first with { Error = " " }],
                [first with { Error = null }],
                [first with { Stage = 10 }]
            })
            {
                var empty = new FalloutQuestStages(records, coldQuests, coldScripts.StageSteps,
                    _ => throw new InvalidDataException("Invalid restore executed a predicate."));
                Reject(() => empty.RestoreResults(invalid));
                Require(empty.CaptureResults().Count == 0 && !empty.HasPendingResults && !empty.HasUnfinishedResults,
                    "Invalid result receipt partially restored a progress/error latch.");
            }
            coldStages.Enter(Key(0x20), 10);
            var completed = coldStages.CaptureResults().Single(item => item.Stage == 10);
            Require(completed.Completed && completed.Error is null && completed.Steps == 1 &&
                coldQuests.Variable(Key(0x20), 1) == 1011 && restoredCalls == 0,
                "A separate authored stage changed the closed failure or failed to execute normally.");
            var afterCompleted = coldQuests.Variable(Key(0x20), 1);
            coldStages.Enter(Key(0x20), 10);
            Require(coldQuests.Variable(Key(0x20), 1) == afterCompleted,
                "Completed nonrepeatable source stage replayed its result.");

            var changedDirectory = Path.Combine(directory, "changed"); Directory.CreateDirectory(changedDirectory);
            var changedPath = Path.Combine(changedDirectory, "Base.esm");
            try
            {
                File.WriteAllBytes(changedPath, Source(2));
                using var changedRecords = FalloutPluginStack.Load(changedDirectory, ["Base.esm"]);
                var changedQuests = new FalloutQuestState(changedRecords); changedQuests.Restore(questSnapshot);
                var changedStages = new FalloutQuestStages(changedRecords, changedQuests,
                    (_, _, _) => throw new InvalidDataException("Source-drift restore executed a result."),
                    _ => throw new InvalidDataException("Source-drift restore executed a condition."));
                Reject(() => changedStages.RestoreResults(saved));
                Require(changedStages.CaptureResults().Count == 0, "Changed source restored a historical failure latch.");
            }
            finally { File.Delete(changedPath); Directory.Delete(changedDirectory); }
            IndependentPendingFailure(records);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
        Console.WriteLine("OPENNV_QUEST_STAGE_PERSISTENCE_CONTRACT_PASS syntheticFullReader=true nestedConsumedPrefix=true exactClosedFailure=true coldNoReplay=true pendingRefused=true malformedAtomic=true sourceDrift=true completedOnce=true untouchedPendingRetained=true");
    }

    private static void IndependentPendingFailure(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var paused = false;
        var calls = new List<string>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Unexpected independent-stage fixture effect."), Command: (_, _, command, _) =>
            {
                calls.Add(command);
                if (command == "Hold") { paused = true; return; }
                throw new NotSupportedException("Original independent-stage failure: " + command);
            }));
        var stages = new FalloutQuestStages(records, quests, scripts.StageSteps,
            _ => throw new InvalidDataException("Unexpected independent-stage fixture condition."), () => !paused);
        stages.Enter(Key(0x40), 0);
        paused = false;
        stages.Enter(Key(0x41), 0);
        Require(stages.HasPendingResults && quests.Variable(Key(0x40), 1) == 1 && quests.Variable(Key(0x41), 1) == 1 &&
            calls.SequenceEqual(["Hold", "Hold"]), "Independent source iterators did not suspend after their own prefixes.");
        Reject(() => stages.CaptureResults());
        paused = false;
        var failure = Reject(stages.Continue);
        Require(stages.HasPendingResults && stages.HasUnfinishedResults && quests.Variable(Key(0x40), 1) == 11 &&
            quests.Variable(Key(0x41), 1) == 1 && calls.SequenceEqual(["Hold", "Hold", "UnknownReachedCommand"]),
            "First closed failure lost, advanced or restarted an untouched pending iterator.");
        Reject(() => stages.CaptureResults());
        stages.Continue();
        var results = stages.CaptureResults();
        Require(!stages.HasPendingResults && quests.Variable(Key(0x40), 1) == 11 && quests.Variable(Key(0x41), 1) == 11 &&
            results.Single(item => item.Quest == Key(0x40)) is { Steps: 3, Completed: false, Error: not null } blocked &&
            blocked.Error == failure && results.Single(item => item.Quest == Key(0x41)) is { Steps: 3, Completed: true, Error: null } &&
            calls.SequenceEqual(["Hold", "Hold", "UnknownReachedCommand"]),
            "Independent continuation failed to resume its original suffix exactly once or replayed a failed prefix.");
        var before = JsonSerializer.Serialize(quests.Capture());
        var journal = JsonSerializer.Serialize(results);
        stages.Continue(); stages.Enter(Key(0x41), 0);
        Require(Reject(() => stages.Enter(Key(0x40), 0)) == failure && before == JsonSerializer.Serialize(quests.Capture()) &&
            journal == JsonSerializer.Serialize(stages.CaptureResults()) && calls.Count == 3,
            "Settled independent stages replayed their prefixes, suffixes or retained failure.");
    }

    private static byte[] Source(int firstIncrement)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return Record("TES4", 0, Field("HEDR", header))
            .Concat(Record("QUST", 0x20, Field("EDID", Text("First")), Field("SCRI", BitConverter.GetBytes(30u)),
                Field("DATA", new byte[8]), Field("INDX", BitConverter.GetBytes((short)0)), Field("QSDT", [0]),
                Field("SCTX", Text($"set First.value to First.value + {firstIncrement}\nHold\nset First.value to First.value + 10\nSetStage Second 0\nset First.value to First.value + 100")),
                Field("SCRO", BitConverter.GetBytes(0x20u)), Field("SCRO", BitConverter.GetBytes(0x21u)),
                Field("INDX", BitConverter.GetBytes((short)10)), Field("QSDT", [0]),
                Field("SCTX", Text("set First.value to First.value + 1000")), Field("SCRO", BitConverter.GetBytes(0x20u))))
            .Concat(Record("QUST", 0x21, Field("EDID", Text("Second")), Field("SCRI", BitConverter.GetBytes(31u)),
                Field("DATA", new byte[8]), Field("INDX", BitConverter.GetBytes((short)0)), Field("QSDT", [1]),
                Field("SCTX", Text("set Second.value to Second.value + 1\nUnknownReachedCommand")),
                Field("SCRO", BitConverter.GetBytes(0x21u))))
            .Concat(Script(30)).Concat(Script(31))
            .Concat(Record("QUST", 0x40, Field("EDID", Text("Blocked")), Field("SCRI", BitConverter.GetBytes(40u)),
                Field("DATA", new byte[8]), Field("INDX", BitConverter.GetBytes((short)0)), Field("QSDT", [0]),
                Field("SCTX", Text("set Blocked.value to Blocked.value + 1\nHold\nset Blocked.value to Blocked.value + 10\nUnknownReachedCommand\nset Blocked.value to Blocked.value + 1000")),
                Field("SCRO", BitConverter.GetBytes(0x40u))))
            .Concat(Record("QUST", 0x41, Field("EDID", Text("Independent")), Field("SCRI", BitConverter.GetBytes(41u)),
                Field("DATA", new byte[8]), Field("INDX", BitConverter.GetBytes((short)0)), Field("QSDT", [0]),
                Field("SCTX", Text("set Independent.value to Independent.value + 1\nHold\nset Independent.value to Independent.value + 10")),
                Field("SCRO", BitConverter.GetBytes(0x41u))))
            .Concat(Script(40)).Concat(Script(41)).ToArray();
    }

    private static byte[] Script(uint id)
    {
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
        return Record("SCPT", id, Field("SLSD", local), Field("SCVR", Text("value")));
    }
    private static FalloutFormKey Key(uint id) => new("Base.esm", id);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
    private static string Reject(Action action)
        => CaptureFailure(action).Message;
    private static Exception CaptureFailure(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException) { return error; }
        throw new InvalidDataException("Invalid closed stage input or unsupported continuation was accepted.");
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = fields.SelectMany(field => field).ToArray();
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
