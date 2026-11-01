using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class QuestStagePersistenceContracts
{
    private static void FailureStageContracts(string directory)
    {
        var path = Path.Combine(directory, "Failure.esm");
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var condition = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(condition.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(condition.AsSpan(8),
            (ushort)OpenNV.Runtime.Content.Definitions.FalloutConditionFunction.GetQuestCompleted);
        BinaryPrimitives.WriteUInt32LittleEndian(condition.AsSpan(12), 0x54);
        var source = Record("TES4", 0, Field("HEDR", header))
            .Concat(Quest(0x50, "Failed", 2,
                "if GetQuestCompleted Failed == 1\nset Failed.value to Failed.value + 1\nendif\nObserveFailureState"))
            .Concat(Quest(0x51, "Already", 2, "set Already.value to Already.value + 1"))
            .Concat(Quest(0x52, "Both", 3, "set Both.value to Both.value + 1"))
            .Concat(Quest(0x53, "Partial", 2,
                "set Partial.value to Partial.value + 1\nUnknownReachedCommand\nset Partial.value to Partial.value + 100"))
            .Concat(Quest(0x54, "Filtered", 2, "set Filtered.value to Filtered.value + 1", condition))
            .Concat(Quest(0x55, "Completes", 1, "set Completes.value to Completes.value + 1"))
            .Concat(Quest(0x56, "Other", 0, "set Other.value to Other.value + 1"))
            .Concat(Quest(0x57, "Repeated", 2, "set Repeated.value to Repeated.value + 1", repeated: true))
            .Concat(Script(100)).ToArray();
        try
        {
            File.WriteAllBytes(path, source);
            using var records = FalloutPluginStack.Load(directory, ["Failure.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var observed = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Unexpected failure fixture effect."),
                Command: (_, _, command, _) =>
                {
                    if (!command.Equals("ObserveFailureState", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Reached failure fixture command: " + command);
                    Require(quests.IsFailed(Form(0x50)) && quests.IsCompleted(Form(0x50)) &&
                        quests.IsRunning(Form(0x50)) && quests.ActiveQuest is null,
                        "Failure flags, running state or active-selection retirement followed the result script.");
                    ++observed;
                }));
            var stages = new FalloutQuestStages(records, quests, scripts.StageSteps, _ => 0);
            quests.ForceActive(Form(0x50)); stages.Enter(Form(0x50), 10);
            Require(observed == 1 && quests.Variable(Form(0x50), 1) == 1 && !stages.HasUnfinishedResults,
                "Eligible failure stage did not execute its branch against the committed completed/failed prefix.");
            var saved = Copy(quests.Capture().ToArray());
            var results = Copy(stages.CaptureResults().ToArray());
            var cold = new FalloutQuestState(records); cold.Restore(saved);
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldScripts = new FalloutReferenceScripts(records, coldWorld, cold, new((_, _) => false,
                _ => throw new InvalidDataException("Cold failure replayed an effect."),
                Command: (_, _, _, _) => throw new InvalidDataException("Cold failure replayed a result command.")));
            var coldStages = new FalloutQuestStages(records, cold, coldScripts.StageSteps, _ => 0);
            coldStages.RestoreResults(results); coldStages.Enter(Form(0x50), 10);
            Require(cold.IsFailed(Form(0x50)) && cold.IsCompleted(Form(0x50)) && cold.IsRunning(Form(0x50)) &&
                cold.Variable(Form(0x50), 1) == 1 && cold.ActiveQuest is null,
                "Cold failure lost its independent flags or replayed a completed result.");
            var empty = new FalloutQuestState(records);
            Reject(() => empty.Restore([saved[0] with { Completed = false, Failed = true }]));
            Require(empty.Capture().Count == 0 && empty.Revision == 0,
                "Malformed failed-but-uncompleted state partially restored its owner.");

            quests.Complete(Form(0x51)); quests.ForceActive(Form(0x51)); stages.Enter(Form(0x51), 10);
            Require(!quests.IsFailed(Form(0x51)) && quests.Variable(Form(0x51), 1) == 1 && quests.ActiveQuest is null,
                "Failure retroactively changed a completed quest or skipped its independent result/selection handling.");
            quests.ForceActive(Form(0x52)); stages.Enter(Form(0x52), 10);
            Require(quests.IsCompleted(Form(0x52)) && !quests.IsFailed(Form(0x52)) && quests.ActiveQuest is null,
                "Combined entry flags lost native completion-before-failure precedence.");
            quests.ForceActive(Form(0x54)); stages.Enter(Form(0x54), 10);
            Require(!quests.IsCompleted(Form(0x54)) && !quests.IsFailed(Form(0x54)) &&
                quests.Variable(Form(0x54), 1) == 0 && quests.ActiveQuest == Form(0x54),
                "An ineligible log entry changed completion, failure, result effects or active selection.");
            quests.ForceActive(Form(0x55)); stages.Enter(Form(0x55), 10);
            Require(quests.IsCompleted(Form(0x55)) && quests.ActiveQuest is null,
                "Ordinary completion-stage selection was not retired before its result.");
            stages.Enter(Form(0x57), 10); stages.Enter(Form(0x57), 10);
            Require(quests.IsFailed(Form(0x57)) && quests.Variable(Form(0x57), 1) == 2,
                "Authored repeat-stage permission was lost after quest failure.");
            PartialFailure(path, directory);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("OPENNV_QUEST_FAILURE_STAGE_CONTRACT_PASS sourceFlags=true conditions=true resultOrdering=true activeSelection=true runningPreserved=true coldNoReplay=true malformedAtomic=true completedGuard=true combinedFlags=true presentation=unbound");
    }

    private static void PartialFailure(string path, string directory)
    {
        using var records = FalloutPluginStack.Load(directory, [Path.GetFileName(path)]);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        FalloutReferenceScripts Scripts(FalloutReferenceWorld owner, FalloutQuestState state) => new(records, owner, state,
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected partial-stage effect."),
                Command: (_, _, command, _) => throw new NotSupportedException("Reached: " + command)));
        var scripts = Scripts(world, quests);
        var stages = new FalloutQuestStages(records, quests, scripts.StageSteps, _ => 0);
        quests.ForceActive(Form(0x56));
        var failure = Reject(() => stages.Enter(Form(0x53), 10));
        Require(quests.IsFailed(Form(0x53)) && quests.IsCompleted(Form(0x53)) && quests.IsRunning(Form(0x53)) &&
            quests.Variable(Form(0x53), 1) == 1 && quests.ActiveQuest == Form(0x56),
            "Failed result discarded its reached prefix or retired a different active quest.");
        var cold = new FalloutQuestState(records); cold.Restore(Copy(quests.Capture().ToArray()));
        using var coldWorld = new FalloutReferenceWorld(records);
        var coldStages = new FalloutQuestStages(records, cold, Scripts(coldWorld, cold).StageSteps, _ => 0);
        coldStages.RestoreResults(Copy(stages.CaptureResults().ToArray()));
        Require(Reject(() => coldStages.Enter(Form(0x53), 10)) == failure &&
            cold.Variable(Form(0x53), 1) == 1 && cold.IsFailed(Form(0x53)) && cold.ActiveQuest == Form(0x56),
            "Cold failed result replayed rewards, cleared its error receipt or changed another quest selection.");
    }

    private static FalloutFormKey Form(uint id) => new("Failure.esm", id);
    private static byte[] Quest(uint id, string name, byte flags, string text, byte[]? condition = null, bool repeated = false)
    {
        var data = new byte[8]; if (repeated) data[0] = 8;
        var fields = new List<byte[]> { Field("EDID", Text(name)), Field("SCRI", BitConverter.GetBytes(100u)),
            Field("DATA", data), Field("INDX", BitConverter.GetBytes((short)10)), Field("QSDT", [flags]) };
        if (condition is not null) fields.Add(Field("CTDA", condition));
        fields.Add(Field("SCTX", Text(text))); fields.Add(Field("SCRO", BitConverter.GetBytes(id)));
        return Record("QUST", id, fields.ToArray());
    }
}
