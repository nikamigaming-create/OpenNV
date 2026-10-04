using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptSoundContracts
{
    private static void QuestContinuations(string directory)
    {
        const string source = "short prefix\nshort suffix\nshort gate\nshort done\nbegin GameMode\n" +
            "if done == 0\nset prefix to prefix + 1\nif gate == 1\nEmitter.PlaySound3D Cue\n" +
            "set suffix to suffix + 1\nelse\nset suffix to 99\nendif\n" +
            "if GetStageDone SoundQuest 30 == 0\nset done to 1\nendif\nendif\nend";
        const string error = "Reached native script command Emitter.PlaySound3D (1 arguments) has no owner.";
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 4); header[16] = 1;
        var path = Path.Combine(directory, "Continuation.esm");
        File.WriteAllBytes(path, Join(Record("TES4", 0), Sound(0x100, "Cue", "fx/cue.wav", 0),
            Record("REFR", 0x200, Field("EDID", Text("Emitter"))),
            Record("SCPT", 0x601, Field("SCHR", header), Local(1, "prefix"), Local(2, "suffix"), Local(3, "gate"), Local(4, "done"),
                Field("SCRO", BitConverter.GetBytes(0x100u)), Field("SCRO", BitConverter.GetBytes(0x200u)),
                Field("SCRO", BitConverter.GetBytes(0x600u)), Field("SCTX", Text(source))),
            Record("QUST", 0x600, Field("EDID", Text("SoundQuest")), Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u)))));
        using var records = FalloutPluginStack.Load(directory, ["Continuation.esm"]);
        var quest = new FalloutFormKey("Continuation.esm", 0x600);
        var quests = new FalloutQuestState(records); quests.SetVariable(quest, 3, 1);
        var original = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
        original.Host = new((_, _) => throw new InvalidDataException("Continuation fixture invented a stage change."),
            _ => throw new InvalidDataException("Continuation fixture invented an actor query."), ExecuteProgram: (owner, script, program, _) =>
        {
            var bindings = new FalloutScriptBindings(records, owner, script, script.ReadSubrecords());
            program.Execute(name => quests.Variable(quest, bindings.Variable(name).Index),
                (name, value) => quests.SetVariable(quest, bindings.Variable(name).Index, value), (_, _) => throw new NotSupportedException(error));
        });
        original.Advance(0);
        Require(quests.Variable(quest, 1) == 1 && quests.Variable(quest, 2) == 0 && original.Capture().Instances.Single().Error == error,
            "Synthetic legacy owner did not stop after the consumed prefix.");
        var captured = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(original.Capture()))!;
        Require(captured.Instances.Single().PendingCommand is { Statement: >= 0, GameMode: true }, "New missing-command failure lost its source cursor.");
        foreach (var legacy in new[] { true, false })
        {
            using var world = new FalloutReferenceWorld(records);
            var restoredQuests = new FalloutQuestState(records); restoredQuests.Restore(quests.Capture()); restoredQuests.SetVariable(quest, 3, 0);
            var restored = new FalloutQuestScripts(records, restoredQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: world);
            var saved = legacy ? captured with { Instances = captured.Instances.Select(value => value with { PendingCommand = null }).ToArray() } : captured;
            restored.Restore(saved);
            restored.Advance(0);
            Require(restored.Capture().Instances.Single().Error == error && restoredQuests.Variable(quest, 1) == 1,
                "Unbound presentation cleared the old fault or repeated its prefix.");
            var prepared = new List<FalloutScriptSoundRequest>();
            using var binding = world.Sounds.Bind(sound => [sound.LogicalPath], request =>
            { prepared.Add(request); return new Voice().Prepare(request); }, true);
            restored.Advance(0);
            var result = restored.Capture().Instances.Single();
            Require(result.Error is null && result.Executions == 1 && result.Clock!.Invocations == 1 &&
                result.Continuations is [{ Error: error, Disposition: "tail-completed-prefix-retained" }] &&
                restoredQuests.Variable(quest, 1) == 1 && restoredQuests.Variable(quest, 2) == 1 && restoredQuests.Variable(quest, 4) == 1 &&
                prepared is [{ Reference.ObjectId: 0x200, Source.FormKey.ObjectId: 0x100 }],
                "Cold continuation repeated a consumed assignment, reevaluated its guard, lost history or skipped its tail.");
            var finished = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(restored.Capture()))!;
            using var nextWorld = new FalloutReferenceWorld(records);
            var nextQuests = new FalloutQuestState(records); nextQuests.Restore(restoredQuests.Capture());
            var next = new FalloutQuestScripts(records, nextQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: nextWorld);
            next.Restore(finished); next.Advance(0);
            Require(next.Capture().Instances.Single().Continuations!.Count == 1 && nextWorld.Sounds.LastRequest is null &&
                nextQuests.Variable(quest, 1) == 1 && nextQuests.Variable(quest, 2) == 1,
                "Cold completed continuation discarded history or replayed transient audio/prefix.");
        }
        var changed = captured with { Instances = captured.Instances.Select(value => value with
            { PendingCommand = value.PendingCommand! with { SourceSha256 = new string('f', 64) } }).ToArray() };
        Reject(() => new FalloutQuestScripts(records, new(records), new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0).Restore(changed));
        Console.WriteLine("OPENNV_QUEST_SOUND_CONTINUATION_CONTRACT_PASS legacyUniqueSite=true capturedSite=true hashValidation=true boundAdmission=true prefixRetained=true branchRetained=true suffixOnce=true clockOnce=true historicalFaultPersisted=true coldNoReplay=true");
    }
}
