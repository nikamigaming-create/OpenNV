using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class QuestMenuContracts
{
    private const string Body = "short trace\nshort filter\nshort stop\nshort failure\n" +
        "begin MenuMode\nset trace to trace * 10 + 1\nset filter to 1001\nif stop == 1\nreturn\nendif\nend\n" +
        "begin MenuMode filter\nif failure == 1\nUnboundOperation\nendif\nset trace to trace * 10 + 2\nend\n" +
        "begin MenuMode 1001\nset trace to trace * 10 + 3\nend\n" +
        "begin MenuMode 1036\nset trace to 99\nend\n" +
        "begin GameMode\nset trace to trace * 10 + 4\nend\n" +
        "begin GameMode\nset trace to trace * 10 + 5\nend";

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-menu-blocks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Menus.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Menus.esm"]);
            foreach (var shared in new[] { false, true }) Check(records, shared);
            Require(FalloutGameModeProgram.WasRejectedByParser(Body, 7) &&
                !FalloutGameModeProgram.WasRejectedByParser("begin MenuMode\nend\nbegin MenuMode\nend", 7) &&
                !FalloutGameModeProgram.WasRejectedByParser(Body, 8), "Block migration changed unrelated parser admission.");
            var state = new FalloutQuestState(records);
            var scripts = Scripts(records, state);
            var current = scripts.Capture();
            var legacy = current with { Instances = [], ParserVersion = 7 };
            Reject(() => scripts.Restore(legacy));
            Require(JsonSerializer.Serialize(scripts.Capture()) == JsonSerializer.Serialize(current),
                "A rejected parser continuation changed current script ownership.");
            state.SetRunning(Key(0x100), false);
            var before = scripts.Capture(); scripts.Advance(1, gameMode: false, menus: [1001, 2]);
            Require(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(scripts.Capture()), "Stopped quest advanced a menu clock.");

            var context = new FalloutScriptMenus();
            context.Publish(false, [1001, 2]);
            Require(context.Query() == 1 && context.Query(2) == 1 && context.Query(1001) == 1 && context.Query(1036) == 0,
                "Menu queries lost specific or category identity.");
            using (context.Enter(1036)) Require(context.Query(1036) == 1 && context.Query(1001) == 0, "Nested menu context retained a replaced menu.");
            Require(context.Query(1001) == 1, "Menu context did not restore its caller.");
            Reject(() => context.Publish(true, [1001])); Require(context.Query(1001) == 1, "Invalid frame mutated menu identity.");
            Reject(() => context.Query(double.NaN)); context.Publish(true);
            Require(context.Query() == 0 && context.Query(1001) == 0, "Closed menu retained script-visible identity.");
            Console.WriteLine("OPENNV_QUEST_MENU_CONTRACT_PASS sharedClock=true sourceOrder=true dynamicFilter=true return=true failurePrefix=true coldFault=true queryOwner=true parserMigration=true stopped=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static void Check(FalloutPluginStack records, bool shared)
    {
        using var world = new FalloutReferenceWorld(records);
        var state = new FalloutQuestState(records);
        var scripts = Scripts(records, state, shared ? world : null);
        var executor = new FalloutReferenceScripts(records, world, state, new((_, _) => false,
            _ => throw new InvalidDataException("Unexpected menu effect.")));
        if (shared) scripts.Host = new((_, _) => throw new InvalidOperationException("Unexpected stage."), _ => 0, executor.ExecuteProgram);
        scripts.Advance(0, gameMode: false, menus: [1001, 2]);
        Require(state.Variable(Key(0x100), 1) == 123 && scripts.Capture().Instances.Single() is { Executions: 1, Error: null },
            "Source menu blocks lost order, dynamic filter, shared query or invocation ownership.");
        state.SetVariable(Key(0x100), 1, 0); scripts.Advance(0);
        Require(state.Variable(Key(0x100), 1) == 45, "Multiple GameMode blocks did not share source order.");
        state.SetVariable(Key(0x100), 1, 0); state.SetVariable(Key(0x100), 3, 1);
        scripts.Advance(0, gameMode: false, menus: [1001, 2]);
        Require(state.Variable(Key(0x100), 1) == 1, "Return did not stop later event blocks.");
        state.SetVariable(Key(0x100), 1, 0); state.SetVariable(Key(0x100), 3, 0); state.SetVariable(Key(0x100), 4, 1);
        scripts.Advance(0, gameMode: false, menus: [1001, 2]);
        var failed = scripts.Capture();
        Require(state.Variable(Key(0x100), 1) == 1 && failed.Instances.Single().Error is not null,
            "Failed menu block lost its prefix or executed later statements.");
        scripts.Advance(1, gameMode: false, menus: [1001, 2]);
        Require(JsonSerializer.Serialize(failed) == JsonSerializer.Serialize(scripts.Capture()), "Failed menu invocation retried.");
        var coldState = new FalloutQuestState(records); coldState.Restore(state.Capture());
        var cold = Scripts(records, coldState); cold.Restore(JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(failed))!);
        cold.Advance(1, gameMode: false, menus: [1001, 2]);
        Require(coldState.Variable(Key(0x100), 1) == 1 && cold.Capture().Instances.Single().Error == failed.Instances.Single().Error,
            "Cold restoration retried or discarded a reached menu failure.");
        var unknownState = new FalloutQuestState(records); var unknown = Scripts(records, unknownState);
        unknown.Advance(0, gameMode: false);
        Require(unknownState.Variable(Key(0x100), 1) == 1 && unknown.Capture().Instances.Single().Error is not null,
            "Missing native menu identity silently selected a filtered block.");
        var claimedState = new FalloutQuestState(records);
        var claimed = new FalloutQuestScripts(records, claimedState, new HashSet<FalloutFormKey> { Key(0x100) },
            new FalloutPlayerInventory(), defaultProcessingDelay: 0);
        var host = new FalloutQuestScriptHost((_, _) => throw new InvalidOperationException("Unexpected stage."), _ => 0);
        claimed.ExecuteClaimedMenu(Key(0x100), 1001, host);
        Require(claimedState.Variable(Key(0x100), 1) == 123 && claimed.Menus.Query() == 0 &&
            claimed.Capture().Instances.Single().Clock!.Invocations == 0,
            "Claimed menu handoff lost unfiltered blocks, source order or its caller context, or advanced the clock twice.");
        world.Menus.Publish(false, [1001]); world.Dispose();
        Require(world.Menus.Query() == 0, "World retirement retained a menu frame.");
    }

    private static FalloutQuestScripts Scripts(FalloutPluginStack records, FalloutQuestState state, FalloutReferenceWorld? world = null) =>
        new(records, state, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: world);
    private static FalloutFormKey Key(uint id) => new("Menus.esm", id);
    private static byte[] Fixture()
    {
        var header = new byte[20]; header[16] = 1; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 4);
        var script = Field("SCHR", header).Concat(Field("SCTX", Text(Body)));
        foreach (var (name, index) in new[] { "trace", "filter", "stop", "failure" }.Select((name, index) => (name, index + 1)))
        {
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, (uint)index);
            script = script.Concat(Field("SLSD", local)).Concat(Field("SCVR", Text(name)));
        }
        return Record("TES4", 0, Field("HEDR", new byte[12]))
            .Concat(Record("QUST", 0x100, Field("EDID", Text("MenuQuest")).Concat(Field("DATA", [1, 0])).Concat(Field("SCRI", BitConverter.GetBytes(0x200u))).ToArray()))
            .Concat(Record("SCPT", 0x200, script.ToArray())).ToArray();
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid menu state was accepted.");
    }
}
