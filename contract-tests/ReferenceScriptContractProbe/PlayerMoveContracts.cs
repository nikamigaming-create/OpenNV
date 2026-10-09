using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PlayerMoveContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-moves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[20]; header[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2);
            var source = "ref target\nshort prefix\nbegin GameMode\nset target to ArrivalREF\n" +
                "Player.MoveTo target (2) (-3) (4)\nset prefix to prefix + 1\nend";
            File.WriteAllBytes(Path.Combine(directory, "Moves.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("ACTI", 0x700),
                Record("QUST", 0x610, Field("EDID", Text("ConfiguredStartup")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x510u))),
                Record("SCPT", 0x510, Field("SCHR", LocalHeader(1)), Local(1, "prefix"),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x901u)),
                    Field("SCTX", Text("short prefix\nbegin MenuMode\nif prefix == 0\nDisablePlayerControls 1 0 0 0 0\nPlayer.MoveTo ArrivalREF\nset prefix to 1\nendif\nend"))),
                Record("QUST", 0x620, Field("EDID", Text("StageStartup")), Field("DATA", new byte[8]),
                    Field("INDX", new byte[2]), Field("QSDT", [0]), Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCRO", BitConverter.GetBytes(0x901u)), Field("SCTX", Text("BlockingPresentation\nPlayer.MoveTo ArrivalREF"))),
                Record("QUST", 0x630, Field("EDID", Text("FailedStartup")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x530u))),
                Record("SCPT", 0x530, Field("SCHR", LocalHeader(1)), Local(1, "prefix"),
                    Field("SCTX", Text("short prefix\nbegin MenuMode\nset prefix to prefix + 1\nUnboundOperation\nend"))),
                Record("QUST", 0x640, Field("EDID", Text("PackageStartup")), Field("DATA", new byte[8]),
                    Field("INDX", new byte[2]), Field("QSDT", [0]), Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCRO", BitConverter.GetBytes(0x710u)), Field("SCRO", BitConverter.GetBytes(0x901u)),
                    Field("SCRO", BitConverter.GetBytes(0x640u)),
                    Field("SCTX", Text("Player.AddScriptPackage BootstrapPose\nSetStage PackageStartup 5\nPlayer.MoveTo ArrivalREF")),
                    Field("INDX", BitConverter.GetBytes((short)5)), Field("QSDT", [0]),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text("Player.RemoveScriptPackage"))),
                Record("PACK", 0x710, Field("EDID", Text("BootstrapPose")), Field("PKDT", [0, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0])),
                Record("QUST", 0x600, Field("EDID", Text("MoveQuest")), Field("DATA", [1, 0, 0, 0, 0, 0, 0, 0]),
                    Field("SCRI", BitConverter.GetBytes(0x500u))),
                Record("SCPT", 0x500, Field("SCHR", header), Local(1, "target"), Local(2, "prefix"),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x901u)),
                    Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCTX", Text(source))), Cell(0x800, 0x900, "OriginREF", [1, 2, 3, 0, 0, 0]),
                Cell(0x801, 0x901, "ArrivalREF", [10, 20, 30, .1f, .2f, .3f])));
            using var records = FalloutPluginStack.Load(directory, ["Moves.esm"]);
            BootstrapContracts(records, directory);
            using var world = new FalloutReferenceWorld(records);
            Reject(() => world.QueuePlayerMoveTo(Key(0x600), Key(0x901), 2, -3, 4));
            Require(!world.PlayerMoves.Pending && world.PlayerMoves.SourcePending.Capture().Revision == 0,
                "Absent actual Main/raw factory mutated the Player allocation before admission.");
            var moves = new FalloutPlayerMoves();
            var first = new FalloutPlayerMove(Key(0x600), Key(0x901), 2, -3, 4);
            moves.Enqueue(first); var firstRequest = moves.SourcePending.Next!;
            Require(ReferenceEquals(moves.Next, first) && moves.SourcePending.SaveBlocker is not null,
                "A logical declaration certified the missing full raw allocation/native consumer.");
            Reject(moves.RequireSettled);
            Reject(() => moves.Enqueue(first with { X = float.PositiveInfinity }));
            Require(moves.SourcePending.Next == firstRequest, "Rejected nonfinite input changed the exact pending allocation.");
            var replacement = new FalloutPlayerMove(Key(0x600), Key(0x900), 8, 0, 0);
            moves.Enqueue(replacement); var secondRequest = moves.SourcePending.Next!;
            var current = moves.SourcePending.Capture();
            Require(ReferenceEquals(moves.Next, replacement) && current.Revision == 2 &&
                current.Replacement is { } overlap && overlap.Released == firstRequest.Identity && overlap.Stored == secondRequest.Identity,
                "Original single-slot replacement was incorrectly represented as FIFO movement.");
            Reject(() => moves.Complete(first));
            Require(moves.SourcePending.Next == secondRequest, "Superseded completion cleared the newer original allocation.");
            moves.Complete(replacement);
            var returned = moves.SourcePending.Capture();
            Require(!moves.Pending && returned.Completion?.Identity == secondRequest.Identity && returned.Replacement == current.Replacement,
                "Current value-owner null store lost its exact replacement/completion lifecycle.");
            var cold = new FalloutPlayerPendingSlot();
            cold.Restore(JsonSerializer.Deserialize<FalloutPlayerPendingSlotSnapshot>(JsonSerializer.Serialize(returned))!);
            Require(!cold.Pending && cold.Capture() == returned, "Cold completed slot invented a destination or replayed its null store.");
            moves.Enqueue(first); moves.Fail(first, new InvalidDataException("Unavailable destination presentation."));
            var failed = moves.SourcePending.Capture();
            Require(moves.Next is null && moves.Pending && moves.Error is not null && failed.Pending?.Move == first,
                "Failed movement forgot its actual request or became eligible for automatic retry.");
            Reject(moves.RequireSettled); Reject(() => moves.Enqueue(replacement));
            moves.Clear();
            Require(moves.Pending && moves.Error == failed.Error && moves.SourcePending.Capture() == failed,
                "Retirement fabricated a successful movement/null store or cleared the original failure.");
            Reject(() => moves.Complete(first));
            Console.WriteLine("OPENNV_PLAYER_MOVE_CONTRACT_PASS sourceFactoryAbsentRefused=true typedTarget=true offsets=true " +
                "singleSlotReplacement=true invalidAtomic=true failureRetained=true completedSlotCold=true retirementKeepsFailure=true " +
                "nativeTransferCompiledGameplayAndParity=UNEXECUTED");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static FalloutFormKey Key(uint id) => new("Moves.esm", id);
    private static byte[] LocalHeader(uint count)
    {
        var header = new byte[20]; header[16] = 1; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), count); return header;
    }

    private static void BootstrapContracts(FalloutPluginStack records, string directory)
    {
        var ini = Path.Combine(directory, "owned.ini");
        const string owned = "[General]\nSCharGenQuest=00000620\nSIntroMovie=owned-intro.bik\n";
        File.WriteAllText(ini, owned);
        var settings = FalloutInstallationSettings.ReadLayers([ini], [new("General", "SCharGenQuest", "00000610")]);
        Require(FalloutNewGameBootstrap.StartingQuest(records, settings).FormKey == Key(0x610) &&
            FalloutNewGameBootstrap.StartingQuest(records, FalloutInstallationSettings.ReadLayers([ini])).FormKey == Key(0x620) &&
            settings.Require("General", "SIntroMovie") == "owned-intro.bik" && File.ReadAllText(ini) == owned,
            "Selected startup settings changed another profile or the owned INI.");
        Reject(() => FalloutNewGameBootstrap.StartingQuest(records, FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "00000700")])));
        Reject(() => FalloutNewGameBootstrap.StartingQuest(records, FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "bad form")])));
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: world);
        var bootstrap = new FalloutNewGameBootstrap(records, settings, quests, scripts, world,
            (_, _, _, _) => throw new NotSupportedException("Unexpected startup command."),
            _ => throw new NotSupportedException("Unexpected startup effect."), () => true);
        Require(!quests.IsRunning(Key(0x610)), "Synthetic startup was already running.");
        Reject(bootstrap.Start);
        Reject(() => bootstrap.PreparePlacement());
        Require(!quests.IsRunning(Key(0x610)) && !bootstrap.PlacementPreparing && !world.PlayerMoves.Pending &&
            quests.Variable(Key(0x610), 1) == 0 && bootstrap.Controls == FalloutPlayerControlState.AllEnabled,
            "Missing original compiled startup changed controls, quest state or movement before admission.");
        Reject(bootstrap.Start);
        foreach (var id in new uint[] { 0x620, 0x640 })
        {
            using var stageWorld = new FalloutReferenceWorld(records);
            var stageQuests = new FalloutQuestState(records);
            var stageScripts = new FalloutQuestScripts(records, stageQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: 0, references: stageWorld);
            var calls = 0;
            var stage = new FalloutNewGameBootstrap(records,
                FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", id.ToString("X8"))]),
                stageQuests, stageScripts, stageWorld, (_, _, _, _) => calls++, _ => calls++, () => true);
            Reject(stage.Start);
            Require(stageQuests.IsRunning(Key(id)) && !stageWorld.PlayerMoves.Pending && calls == 0 &&
                !stageQuests.StageDone(Key(id), 5) && stage.CaptureStageResults().Any(row => row.Error is not null),
                "Original missing compiled stage ran diagnostic text, nested package effects or a source suffix.");
            var failedResults = stage.CaptureStageResults().ToArray();
            stage.Advance(0, [4]); stage.Advance(1, [4]);
            Require(calls == 0 && !stageWorld.PlayerMoves.Pending && stage.CaptureStageResults().Any(row => row.Error is not null),
                "Repeated startup cleared its actual compiled refusal or replayed the failed result prefix.");
            Require(stage.CaptureStageResults().SequenceEqual(failedResults), "Closed failed result was mutated by later startup polling.");
        }
        using var failedWorld = new FalloutReferenceWorld(records);
        var failedQuests = new FalloutQuestState(records);
        var failedScripts = new FalloutQuestScripts(records, failedQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
            defaultProcessingDelay: 0, references: failedWorld);
        var failed = new FalloutNewGameBootstrap(records,
            FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "00000630")]), failedQuests,
            failedScripts, failedWorld, (_, _, _, _) => throw new InvalidOperationException("Diagnostic startup must not run."),
            _ => throw new InvalidOperationException("Diagnostic startup must not apply an effect."), () => true);
        Reject(failed.Start); Reject(failed.Start);
        Require(!failedQuests.IsRunning(Key(0x630)) && failedQuests.Variable(Key(0x630), 1) == 0 && !failedWorld.PlayerMoves.Pending,
            "Absent original SCDA replayed diagnostic local mutations or fabricated player movement.");
        var controlGraph = new FalloutOpeningControlGraph(new Dictionary<string, IReadOnlyDictionary<short, FalloutOpeningControlStage>>
        {
            ["FailedStartup"] = new Dictionary<short, FalloutOpeningControlStage>
            {
                [0] = new(Key(0x630), "FailedStartup", 0, "if 1\nSetStage FailedStartup 1\nelse\nSetStage FailedStartup 2\nendif", [])
            }
        });
        Require(FalloutOpeningStageTransitionResolver.Resolve(records, controlGraph, executeGameMode: true).Transitions.Count == 0,
            "Executed source stages were rejected or predicted from competing conditional destinations.");
        Console.WriteLine("OPENNV_NEW_GAME_BOOTSTRAP_CONTRACT_PASS configuredQuest=true profileIsolation=true ownedIniReadOnly=true " +
            "compiledScriptMissingRefused=true compiledStageMissingRefused=true noDiagnosticEffects=true failurePrefix=true " +
            "ordinaryCompiledStartupAndParity=UNEXECUTED");
    }
    private static byte[] Cell(uint id, uint reference, string name, float[] transform)
    {
        var body = Record("REFR", reference, Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(0x700u)),
            Field("DATA", transform.SelectMany(BitConverter.GetBytes).ToArray()));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24);
        return Join(Record("CELL", id, Field("DATA", [1])), group);
    }
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Unsupported player movement was accepted.");
    }
}
