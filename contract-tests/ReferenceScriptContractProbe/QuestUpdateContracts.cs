using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class QuestUpdateContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-quest-updates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Updates.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Updates.esm"]);
            CheckProgress(records);
            CheckObjectives(records);
            CheckDispatch(records);
            CheckHud();
            Console.WriteLine("OPENNV_QUEST_UPDATE_CONTRACT_PASS allDeclared=true displayRetained=true questRetained=true sourceOrder=true completedQuestSilent=true deferredCancellation=true ordinaryMessagesRetained=true loadingQueueRetained=true coldRequest=true fallback=true invalidPrefix=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static void CheckProgress(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records);
        quests.EnterStage(Key(0x100), 7);
        var progress = quests.ProgressRevision;
        var mutation = quests.Revision;
        for (var tick = 1; tick <= 100; tick++) quests.SetVariable(Key(0x100), 1, tick);
        quests.EnterStage(Key(0x100), 7);
        Require(quests.ProgressRevision == progress && quests.Revision > mutation,
            "Recurring timer writes or a repeated stage counted as bot gameplay progress.");
        quests.EnterStage(Key(0x100), 3);
        Require(quests.ProgressRevision == ++progress && quests.Stage(Key(0x100)) == 7 && quests.StageDone(Key(0x100), 3),
            "A newly entered lower stage lost semantic progress or changed highest-stage behavior.");
        quests.ApplyObjective(Key(0x100), 30, true, true);
        Require(quests.ProgressRevision == ++progress, "An objective change did not publish semantic progress.");
        quests.ApplyObjective(Key(0x100), 30, true, true);
        Require(quests.ProgressRevision == progress, "An unchanged objective renewed stalled gameplay.");
        quests.ForceActive(Key(0x100));
        Require(quests.ProgressRevision == ++progress, "Actual quest selection did not publish progress.");
        quests.ForceActive(Key(0x100));
        Require(quests.ProgressRevision == progress, "Repeated quest selection renewed stalled gameplay.");
        quests.Complete(Key(0x100));
        Require(quests.ProgressRevision == ++progress, "Quest completion did not publish progress.");
        quests.Complete(Key(0x100));
        Require(quests.ProgressRevision == progress, "Repeated quest completion renewed stalled gameplay.");
        var cold = new FalloutQuestState(records);
        cold.Restore(quests.Capture());
        Require(cold.ProgressRevision > 0 && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(quests.Capture()),
            "Cold restoration lost semantic quest state or had no fresh progress generation.");
        var restoredProgress = cold.ProgressRevision;
        cold.SetVariable(Key(0x100), 1, 101);
        Require(cold.ProgressRevision == restoredProgress, "A cold timer write counted as gameplay progress.");
        Console.WriteLine("OPENNV_QUEST_PROGRESS_CONTRACT_PASS enteredStage=true objectives=true timerExcluded=true reentryExcluded=true cold=true");
    }

    private static void CheckObjectives(FalloutPluginStack records)
    {
        var queue = new FalloutHudNotifications();
        var quests = new FalloutQuestState(records, queue);
        quests.EnterStage(Key(0x100), 7); quests.ForceActive(Key(0x100));
        quests.ApplyObjective(Key(0x100), 30, true, true);
        quests.ApplyObjective(Key(0x100), 20, true, true);
        quests.ApplyObjective(Key(0x100), 40, false, true);
        var before = quests.Capture().Single();
        var ordinal = queue.Capture().LastOrdinal;
        quests.CompleteAllObjectives(Key(0x100));
        var after = quests.Capture().Single();
        Require(after.Stage == before.Stage && after.Completed == before.Completed && after.Running == before.Running &&
            after.Active == before.Active && after.EnteredStages.SequenceEqual(before.EnteredStages) &&
            after.Objectives!.All(value => value.Completed) &&
            after.Objectives!.Select(value => value.Displayed).SequenceEqual(before.Objectives!.Select(value => value.Displayed)),
            "Completing objectives changed quest progress, activity or objective display state.");
        Require(queue.Capture().Pending.Where(value => value.Ordinal > ordinal).Select(value => value.Event.ObjectiveIndex)
            .SequenceEqual(new uint?[] { 30, 20 }), "Completion reminders lost declaration order or included hidden/already complete objectives.");
        var revision = quests.Revision; var notices = JsonSerializer.Serialize(queue.Capture());
        quests.CompleteAllObjectives(Key(0x100));
        Require(quests.Revision == revision && JsonSerializer.Serialize(queue.Capture()) == notices, "Repeated completion replayed reminders.");
        var cold = new FalloutQuestState(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(quests.Capture()), "Cold quest restoration lost objective or quest state.");

        var silent = new FalloutHudNotifications(); var completed = new FalloutQuestState(records, silent);
        completed.ApplyObjective(Key(0x100), 30, true, true);
        completed.Complete(Key(0x100)); var prior = silent.Capture().LastOrdinal;
        completed.CompleteAllObjectives(Key(0x100));
        Require(silent.Capture().LastOrdinal == prior && completed.Objective(Key(0x100), 30).Completed,
            "An already completed quest published a completion reminder.");
        var empty = new FalloutQuestState(records, silent);
        empty.CompleteAllObjectives(Key(0x101));
        Require(empty.Capture().Single().Objectives!.Count == 0 && !empty.IsCompleted(Key(0x101)), "Empty quest completion invented objectives or quest completion.");
    }

    private static void CheckDispatch(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var inventory = new FalloutPlayerInventory(); var quests = new FalloutQuestState(records, inventory.Notifications);
        var executor = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected objective effect.")));
        void Run(string body) => executor.ExecuteProgram(records.GetEffective(Key(0x100)), records.GetEffective(Key(0x200)),
            FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
        foreach (var invalid in new[] { "CompleteAllObjectives", "CompleteAllObjectives UpdatesQuest 1", "CompleteAllObjectives WrongType", "KQU 1", "UpdatesQuest.KQU" })
            Reject(() => Run(invalid + "\nset trace to 99"));
        Require(quests.Variable(Key(0x100), 1) == 0 && !inventory.Notifications.Capture().QuestUpdateCancellationRequested,
            "Invalid command changed a local or requested HUD cancellation.");
        quests.ApplyObjective(Key(0x100), 30, true, true);
        Run("CompleteAllObjectives UpdatesQuest\nKillQuestUpdates\nset trace to 1");
        Require(quests.Variable(Key(0x100), 1) == 1 && quests.Capture().Single().Objectives!.All(value => value.Completed) &&
            inventory.Notifications.Capture().QuestUpdateCancellationRequested && inventory.Notifications.Capture().Pending.Count == 2,
            "Shared command dispatch lost completion, deferred cancellation or its source suffix.");
        Require(inventory.Notifications.ConsumeQuestUpdateCancellation(false) && inventory.Notifications.Capture().Pending.Count == 0,
            "Shared command did not reach the authoritative HUD queue.");
        Run("KQU"); Require(inventory.Notifications.Capture().QuestUpdateCancellationRequested, "Short cancellation alias was not dispatched.");

        var fallbackInventory = new FalloutPlayerInventory(); var fallbackState = new FalloutQuestState(records, fallbackInventory.Notifications);
        var fallback = new FalloutQuestScripts(records, fallbackState, new HashSet<FalloutFormKey>(), fallbackInventory, defaultProcessingDelay: 0);
        fallback.Advance(0);
        Require(fallback.Capture().Instances.Single().Error is null && fallbackState.Variable(Key(0x100), 1) == 2 &&
            fallbackState.Capture().Single().Objectives!.All(value => value.Completed) && fallbackInventory.Notifications.Capture().QuestUpdateCancellationRequested,
            "Fallback quest command dispatch diverged from shared execution.");
        var prefixState = new FalloutQuestState(records);
        var prefixExecutor = new FalloutReferenceScripts(records, world, prefixState, new((_, _) => false, _ => { }));
        Reject(() => prefixExecutor.ExecuteProgram(records.GetEffective(Key(0x100)), records.GetEffective(Key(0x200)),
            FalloutGameModeProgram.Read("begin GameMode\nCompleteAllObjectives UpdatesQuest\nUnownedCommand\nset trace to 99\nend"), 0));
        Require(prefixState.Capture().Single().Objectives!.All(value => value.Completed) && prefixState.Variable(Key(0x100), 1) == 0,
            "Later failure rolled back completion or ran its source suffix.");
    }

    private static void CheckHud()
    {
        var queue = new FalloutHudNotifications();
        var item = new FalloutHudEvent(FalloutHudEventKind.ItemAdded, Key(0x300), 1);
        var message = new FalloutHudEvent(FalloutHudEventKind.Message, Key(0x301), 0);
        var objective = new FalloutHudEvent(FalloutHudEventKind.ObjectiveCompleted, Key(0x100), 0, ObjectiveIndex: 30);
        queue.Publish([item, objective, message]); queue.Advance(0, _ => 10); queue.Advance(2, _ => 10);
        var current = queue.Current;
        queue.RequestQuestUpdateCancellation(); queue.Publish([objective, item]);
        Require(queue.Capture().Pending.Count == 4, "Request synchronously consumed notices.");
        Require(queue.ConsumeQuestUpdateCancellation(false) && queue.Current == current && queue.Elapsed == 2 &&
            queue.Capture().Pending.Select(value => value.Event).SequenceEqual(new[] { message, item }),
            "Cancellation changed a current ordinary notice, its clock or surviving queue order.");
        Require(!queue.ConsumeQuestUpdateCancellation(false), "Consumed request ran twice.");
        var currentObjective = new FalloutHudNotifications(); currentObjective.Publish([objective, message]);
        currentObjective.Advance(0, _ => 10); currentObjective.Advance(3, _ => 10); currentObjective.RequestQuestUpdateCancellation();
        var saved = JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>(JsonSerializer.Serialize(currentObjective.Capture()))!;
        var cold = new FalloutHudNotifications(); cold.Restore(saved);
        Require(cold.ConsumeQuestUpdateCancellation(true) && cold.Current == saved.Current && cold.Elapsed == saved.Elapsed &&
            cold.Capture().Pending.SequenceEqual(saved.Pending), "Loading cancelled a retained quest notice or lost its clock.");
        cold.RequestQuestUpdateCancellation();
        Require(cold.ConsumeQuestUpdateCancellation(false) && cold.Current is null && cold.Elapsed == 0 && cold.Capture().Pending.Single().Event == message,
            "Ordinary HUD cancellation retained the active quest notice or removed an ordinary message.");
        cold.Advance(0, _ => 10); Require(cold.Current!.Event == message, "Cancellation blocked the next ordinary message.");
        var legacy = JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>("{\"LastOrdinal\":0,\"Current\":null,\"Elapsed\":0,\"Pending\":[]}")!;
        Require(!legacy.QuestUpdateCancellationRequested, "Legacy HUD snapshot invented a cancellation request.");
    }

    private static byte[] Fixture()
    {
        var header = new byte[20]; header[16] = 1; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
        var objectives = new uint[] { 30, 10, 20, 40 }.SelectMany(index =>
            Join(Field("QOBJ", BitConverter.GetBytes(index)), Field("NNAM", Text("Synthetic objective " + index)))).ToArray();
        return Join(Record("TES4", 0, Field("HEDR", new byte[12])),
            Record("QUST", 0x100, Field("EDID", Text("UpdatesQuest")), Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x200u)), objectives),
            Record("QUST", 0x101, Field("DATA", [0, 0])), Record("ACTI", 0x300, Field("EDID", Text("WrongType"))),
            Record("SCPT", 0x200, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("trace")),
                Field("SCRO", BitConverter.GetBytes(0x100u)), Field("SCRO", BitConverter.GetBytes(0x300u)),
                Field("SCTX", Text("short trace\nbegin GameMode\nCompleteAllObjectives UpdatesQuest\nKQU\nset trace to 2\nend"))));
    }
    private static FalloutFormKey Key(uint id) => new("Updates.esm", id);
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported quest command was accepted.");
    }
}
