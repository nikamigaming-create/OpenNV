using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseCompiledOpeningState()
    {
        GD.Print($"OPENNV_NATIVE_COMPILED_OPENING_ASSEMBLY mvid={GetType().Module.ModuleVersionId} location={GetType().Assembly.Location}");
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-opening-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RuntimeNativePlayer? player = null;
        NativeOpeningResultFixture? driver = null;
        try
        {
            var path = Path.Combine(directory, "NativeBytecode.esm");
            byte[] Stage(short stage, byte[] code) => AuthorityJoin(AuthorityField("INDX", BitConverter.GetBytes(stage)),
                AuthorityField("QSDT", [0]), AuthorityField("SCHR", AuthorityHeader(code, 1, 0, 0)),
                AuthorityField("SCDA", code), AuthorityField("SCRO", BitConverter.GetBytes(0x20u)),
                AuthorityField("SCTX", [65, 0, 66, 0]));
            var nested = AuthorityInstruction(0x1039, AuthorityJoin(BitConverter.GetBytes((ushort)2),
                [(byte)'r'], BitConverter.GetBytes((ushort)1), [(byte)'n'], BitConverter.GetBytes(20)));
            var first = AuthorityJoin(AuthorityInstruction(0x1061), AuthoritySet(1, " 31"), nested);
            var second = AuthorityJoin(AuthorityInstruction(0x1060), AuthoritySet(2, " 47"));
            var failure = AuthorityJoin(AuthorityInstruction(0x1061), AuthorityInstruction(0x2f03), AuthorityInstruction(0x1060));
            var untaken = AuthorityJoin(AuthorityInstruction(0x16, AuthorityJoin(BitConverter.GetBytes((ushort)1),
                BitConverter.GetBytes((ushort)2), Encoding.ASCII.GetBytes(" 0"))), AuthorityInstruction(0x1061), AuthorityInstruction(0x19));
            var malformed = AuthorityJoin(AuthorityInstruction(0x1060), [0x15, 0, 16, 0, 0, 0]);
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var questHeader = new byte[8]; questHeader[0] = 8; // Authored deliberate repeat requests are permitted.
            var standalone = AuthorityInstruction(0x1d);
            File.WriteAllBytes(path, AuthorityJoin(AuthorityRecord("TES4", 0, AuthorityField("HEDR", header)),
                AuthorityRecord("SCPT", 0x30, AuthorityField("SCHR", AuthorityHeader(standalone, 0, 2, 1)),
                    AuthorityField("SCDA", standalone), AuthorityLocal(1, "prefix"), AuthorityLocal(2, "nested")),
                AuthorityRecord("QUST", 0x20, AuthorityField("EDID", Encoding.ASCII.GetBytes("AuthoredOpening\0")),
                    AuthorityField("DATA", questHeader), AuthorityField("SCRI", BitConverter.GetBytes(0x30u)),
                    Stage(10, first), Stage(20, second), Stage(40, untaken), Stage(50, failure), Stage(60, malformed))));
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["NativeBytecode.esm"]);
            var catalog = FalloutOpeningPlayerControlResolver.ResolveForExecution(records, ["AuthoredOpening"]);
            var quests = new FalloutQuestState(records);
            using var world = new FalloutReferenceWorld(records);
            var actualEffects = 0;
            player = new(); player.Configure(RuntimeConfiguration.Load(), Transform3D.Identity,
                FalloutCameraProjection.FromReferenceFov(75, 1)); AddChild(player);
            player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
            driver = Bind(quests, world, FalloutPlayerControlState.AllEnabled, null);
            var warm = driver;
            // This is one explicit declared component request, matching the
            // fresh non-bootstrap Configure path. It is not campaign input.
            Set(warm, "_initialStageResultRequest", (AuthorityKey(0x20), (short)10));
            Invoke(warm, "ApplyEnteredActorCommands");
            var count = actualEffects;
            Invoke(warm, "Synchronize"); Invoke(warm, "Synchronize");
            AuthorityRequire(count == 2 && actualEffects == count && warm.Stage == 20 && player.SourceControls == FalloutPlayerControlState.AllEnabled &&
                quests.Variable(AuthorityKey(0x20), 1) == 31 && quests.Variable(AuthorityKey(0x20), 2) == 47 && warm.TimerSeconds is null,
                "Opening prediction, nested-stage observation or attachment repeated/changed original results.");
            var stageOwner = (FalloutQuestStages)Get(warm, "_stageResults")!;
            var savedStages = JsonSerializer.Deserialize<FalloutQuestStageResultSnapshot[]>(JsonSerializer.Serialize(stageOwner.CaptureResults()))!;
            var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
            var savedControls = player.SourceControls;
            warm.Free(); AuthorityRequire(!GodotObject.IsInstanceValid(warm), "Original opening node survived Free."); driver = null;
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
            using var coldWorld = new FalloutReferenceWorld(records);
            driver = Bind(coldQuests, coldWorld, savedControls, savedStages);
            var cold = driver;
            Invoke(cold, "Synchronize"); Invoke(cold, "Synchronize");
            AuthorityRequire(actualEffects == count && cold.Stage == 20 && player.SourceControls == savedControls &&
                JsonSerializer.Serialize(savedQuests) == JsonSerializer.Serialize(coldQuests.Capture()) &&
                JsonSerializer.Serialize(savedStages) == JsonSerializer.Serialize(((FalloutQuestStages)Get(cold, "_stageResults")!).CaptureResults()),
                "Cold opening attachment replayed a repeat-enabled stage or invented control/result state.");
            // An actual new SetStage request is different from observation:
            // the authored repeat flag must still permit this real invocation.
            cold.RequestSourceStage(AuthorityKey(0x20), 10);
            AuthorityRequire(actualEffects == count + 2 && cold.Stage == 20, "Deliberate original repeated stage lost its real shared invocation.");
            cold.RequestSourceStage(AuthorityKey(0x20), 40);
            AuthorityRequire(actualEffects == count + 2 && cold.Stage == 40 && player.SourceControls == savedControls,
                "Untaken original branch applied predicted diagnostic controls.");
            AuthorityReject(() => cold.RequestSourceStage(AuthorityKey(0x20), 50));
            var prefixEffects = actualEffects;
            AuthorityRequire(prefixEffects == count + 3 && cold.Stage == 50 &&
                player.SourceControls == new FalloutPlayerControlCommand(false, []).Apply(savedControls),
                "Reached original failure lost its actual control prefix or applied an unsupported suffix.");
            AuthorityReject(() => cold.RequestSourceStage(AuthorityKey(0x20), 50));
            AuthorityRequire(actualEffects == prefixEffects && ((FalloutQuestStages)Get(cold, "_stageResults")!).CaptureResults()
                .Single(value => value.Stage == 50) is { Completed: false, Steps: 1, Error: not null },
                "Failed original stage replayed its effects or lost its closed committed prefix.");
            var prefixControls = player.SourceControls;
            AuthorityReject(() => cold.RequestSourceStage(AuthorityKey(0x20), 60));
            AuthorityRequire(actualEffects == prefixEffects && player.SourceControls == prefixControls && cold.Stage == 60 &&
                ((FalloutQuestStages)Get(cold, "_stageResults")!).CaptureResults().Single(value => value.Stage == 60)
                    is { Completed: false, Steps: 0, Error: not null },
                "Structural body refusal applied a decoded prefix or erased genuine entered stage state.");
            AuthorityRequire(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Opening fixture changed source bytes.");
            cold.Free(); AuthorityRequire(!GodotObject.IsInstanceValid(cold), "Cold opening node survived original-owner Free."); driver = null;
            var retiringPlayer = player; retiringPlayer.Free();
            AuthorityRequire(!GodotObject.IsInstanceValid(retiringPlayer), "Actual player node survived original-owner Free."); player = null;
            GD.Print("OPENNV_NATIVE_COMPILED_OPENING_STATE_PASS actualPlayerMask=true originalNestedStage=true " +
                "attachNoReplay=true coldNoReplay=true deliberateRepeat=true untakenControls=false closedFailurePrefix=true structuralBodyAtomic=true " +
                "noSctxPrediction=true noInventedTimer=true sourceUnchanged=true ordinaryCampaign=false");

            NativeOpeningResultFixture Bind(FalloutQuestState selectedQuests, FalloutReferenceWorld selectedWorld,
                FalloutPlayerControlState selectedControls, IReadOnlyList<FalloutQuestStageResultSnapshot>? history)
            {
                var value = new NativeOpeningResultFixture();
                var scripts = new FalloutQuestScripts(records, selectedQuests, new HashSet<FalloutFormKey>(), new(),
                    defaultProcessingDelay: .1f, references: selectedWorld);
                Set(value, "_player", player!); Set(value, "_pluginStack", records); Set(value, "_controls", catalog);
                Set(value, "_sourceQuestEditorId", "AuthoredOpening"); Set(value, "_sourceControls", selectedControls);
                Set(value, "_quests", selectedQuests); Set(value, "_scripts", scripts);
                var executor = new FalloutReferenceScripts(records, selectedWorld, selectedQuests,
                    new((_, _) => throw new InvalidDataException("Opening fixture invented a furniture query."), effect =>
                    {
                        if (effect.Kind == FalloutReferenceEffectKind.PlayerControls)
                        { value.ApplySourcePlayerControls(effect); ++actualEffects; }
                        else if (effect.Kind == FalloutReferenceEffectKind.SetStage) value.RequestSourceStage(effect.Target!.Value, effect.Stage);
                        else throw new InvalidDataException("Opening fixture invented an effect owner.");
                    }));
                var stages = new FalloutQuestStages(records, selectedQuests, executor.StageSteps,
                    _ => throw new InvalidDataException("Unconditioned opening fixture evaluated a predicate."));
                if (history is not null) stages.RestoreResults(history);
                Set(value, "_stageResults", stages); AddChild(value);
                return value;
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(driver)) driver!.Free();
            if (GodotObject.IsInstanceValid(player)) player!.Free();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly BindingFlags OpeningFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(RuntimeNativeOpeningStageDriver owner, string field, object value) =>
        (typeof(RuntimeNativeOpeningStageDriver).GetField(field, OpeningFlags) ?? throw new MissingFieldException(field)).SetValue(owner, value);
    private static object? Get(RuntimeNativeOpeningStageDriver owner, string field) =>
        (typeof(RuntimeNativeOpeningStageDriver).GetField(field, OpeningFlags) ?? throw new MissingFieldException(field)).GetValue(owner);
    private static void Invoke(RuntimeNativeOpeningStageDriver owner, string method)
    {
        try { (typeof(RuntimeNativeOpeningStageDriver).GetMethod(method, OpeningFlags) ?? throw new MissingMethodException(method)).Invoke(owner, null); }
        catch (TargetInvocationException error) when (error.InnerException is { } actual)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
}

internal partial class NativeOpeningResultFixture : RuntimeNativeOpeningStageDriver
{
    public override void _Ready() { }
}
