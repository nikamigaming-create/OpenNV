using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

HudNotificationsProbe.Run();
QuestScriptClockProbe.Run();
QuestObjectiveProbe.Run();
ScriptExpressionProbe.Run();
QuestScriptExecutionProbe.Run();
ActivationProgramProbe.Run();

if (args is ["--audit-material-emittance", var materialRoot, var materialCell, var materialHour])
{
    PlacedLightProbe.OwnedMaterials(materialRoot, materialCell, float.Parse(materialHour, System.Globalization.CultureInfo.InvariantCulture));
    return;
}

if (args is ["--audit-stage-globals", var stageRoot])
{
    RuntimeLiveContentSource.Configure(stageRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var graph = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
    var globals = FalloutGlobalState.Read(records);
    foreach (var stage in graph.Quests.Values.SelectMany(stages => stages.Values))
    {
        var writes = FalloutStageGlobalProgram.Read(records, stage).Prepare(globals);
        if (writes.Count != 0) Console.WriteLine(JsonSerializer.Serialize(new { stage.Quest, stage.Stage, writes }));
        foreach (var write in writes) globals.Set(write.Form, write.Value);
    }
    Console.WriteLine("OPENNV_OWNED_STAGE_GLOBALS_PASS scope=compiled-stage-bindings-and-shared-storage parity=unverified");
    return;
}

if (args is ["--audit-script-initialization", var initializationRoot])
{
    RuntimeLiveContentSource.Configure(initializationRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var initial = new FalloutQuestScriptInitialization(records,
        FalloutInstallationSettings.Read(content).Number("MAIN", "fQuestScriptDelayTime"));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        initial.DefaultDelay, initial.EmbeddedQuestScripts, initial.Initializations,
        definitions = initial.Definitions.Values.Select(definition => new
        {
            script = records.RuntimeFormId(definition.Script), quest = definition.Quest,
            definition.InitializationOrdinal, definition.ProcessingDelay, definition.InitialPhase,
            phaseBytes = Convert.ToHexString(BitConverter.GetBytes(definition.InitialPhase)),
        }),
    }));
    return;
}

if (args is ["--audit-placed-lights", var lightRoot, var lightCell])
{
    PlacedLightProbe.Owned(lightRoot, lightCell);
    return;
}
if (args is ["--audit-placed-lights", var timeLightRoot, var timeLightCell, var timeLightHour])
{
    PlacedLightProbe.Owned(timeLightRoot, timeLightCell, float.Parse(timeLightHour, System.Globalization.CultureInfo.InvariantCulture));
    return;
}

if (args is ["--audit-quest-scripts", var sourceRoot])
{
    RuntimeLiveContentSource.Configure(sourceRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var quests = new FalloutQuestState(records);
    var inventory = new FalloutPlayerInventory();
    var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory);
    for (var frame = 0; frame < 360; frame++) scripts.Advance(1f / 60);
    var expectedInventory = JsonSerializer.Serialize(inventory.Items);
    var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
    var savedScripts = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
    var restoredQuests = new FalloutQuestState(records);
    restoredQuests.Restore(savedQuests);
    var restoredInventory = new FalloutPlayerInventory();
    var requests = inventory.Items.Select(item => new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray();
    restoredInventory.Restore(FalloutCampaignInventoryResolver.Resolve(records, requests, null), []);
    var restoredScripts = new FalloutQuestScripts(records, restoredQuests, new HashSet<FalloutFormKey>(), restoredInventory);
    restoredScripts.Restore(savedScripts);
    restoredScripts.Advance(0, gameMode: false);
    Require(JsonSerializer.Serialize(restoredInventory.Items) == expectedInventory, "Owned script grants changed across a cold restore.");
    Require(restoredScripts.Capture().Messages.SequenceEqual(savedScripts.Messages), "Owned pending messages changed across a cold restore.");
    Console.WriteLine(JsonSerializer.Serialize(scripts.State));
    return;
}

var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["given"] = 0, ["count"] = 0 };
var effects = new List<string>();
var sourceProgram = FalloutGameModeProgram.Read("scn Synthetic\nshort given\nbegin GameMode\nif (given == 0)\nset count to 2 + 3 * 4\nShowMessage Synthetic\nset given to 1\nelseif given == 1\nset count to count + 1\nelse\nUnknown unreachable\nendif\nend");
void ExecuteProgram() => sourceProgram.Execute(name => variables[name], (name, value) => variables[name] = value,
    (name, arguments) => effects.Add(name + ":" + string.Join(',', arguments)));
ExecuteProgram(); ExecuteProgram();
Require(variables["given"] == 1 && variables["count"] == 15 && effects.SequenceEqual(["ShowMessage:Synthetic"]),
    "GameMode source branches, arithmetic, source command arguments or once-only variable behavior differ.");
Require(FalloutGameModeProgram.Evaluate(FalloutGameModeProgram.Tokens("0 && missing || (5 > 2)"), _ => throw new Exception("Short circuit evaluated an inactive operand.")) == 1,
    "Script short circuit evaluated an inactive operand.");
var invalidScriptRejected = false;
try { FalloutGameModeProgram.Read("begin GameMode\nif 1\nShowMessage Synthetic\nend"); }
catch (InvalidDataException) { invalidScriptRejected = true; }
Require(invalidScriptRejected, "An unclosed script condition was admitted.");

const uint compressedFlag = FalloutPluginRecord.CompressedFlag;
var fixtureRoot = Path.Combine(Path.GetTempPath(), $"opennv-plugin-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(fixtureRoot);
try
{
    var extendedValue = Encoding.ASCII.GetBytes("extended-editor-id");
    var armorData = new byte[12];
    BinaryPrimitives.WriteInt32LittleEndian(armorData, 25);
    BinaryPrimitives.WriteSingleLittleEndian(armorData.AsSpan(8), 3.5f);
    var winningArmorData = (byte[])armorData.Clone();
    BinaryPrimitives.WriteInt32LittleEndian(winningArmorData, 40);
    var weaponData = new byte[15];
    BinaryPrimitives.WriteInt32LittleEndian(weaponData, 75);
    BinaryPrimitives.WriteSingleLittleEndian(weaponData.AsSpan(8), 2.0f);
    BinaryPrimitives.WriteUInt16LittleEndian(weaponData.AsSpan(12), 18);
    weaponData[14] = 6;
    var weaponDnam = new byte[204];
    BinaryPrimitives.WriteUInt32LittleEndian(weaponDnam, 7);
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Master.esm"), Combine(
        Record("TES4", 0, 0, Subrecord("HEDR", [0, 0, 0, 0])),
        Group("STAT", 0, Combine(
            Record("STAT", 0x00000010, 0, ExtendedSubrecord("EDID", extendedValue)),
            Record("MISC", 0x00000020, 0, Subrecord("EDID", ZString("Removed"))),
            CompressedRecord("STAT", 0x00000040, Subrecord("EDID", ZString("Compressed")), true),
            CompressedRecord("CONT", 0x00000041, Subrecord("EDID", ZString("Checksummed")), false))),
        Record("ARMO", 0x00000060, 0, Combine(
            Subrecord("EDID", ZString("SyntheticArmor")),
            Subrecord("DATA", armorData))),
        Record("WEAP", 0x00000061, 0, Combine(
            Subrecord("EDID", ZString("SyntheticWeapon")),
            Subrecord("DATA", weaponData),
            Subrecord("NAM0", UInt32(0x00000062)),
            Subrecord("DNAM", weaponDnam))),
        Record("AMMO", 0x00000062, 0, Subrecord("EDID", ZString("SyntheticAmmo"))),
        Record("WTHR", 0x00000050, 0, BinarySubrecord(
            [5, (byte)'I', (byte)'A', (byte)'D'], [1]))));

    File.WriteAllBytes(Path.Combine(fixtureRoot, "Patch.esp"), Combine(
        Record("TES4", 0, 0, Combine(
            Subrecord("HEDR", [0, 0, 0, 0]),
            Subrecord("MAST", ZString("master.ESM")))),
        Group("STAT", 0, Group("CELL", 6, Combine(
            Record("STAT", 0x00000010, 0, Subrecord("EDID", ZString("Winner"))),
            Record("MISC", 0x00000020, FalloutPluginRecord.DeletedFlag, []),
            Record("STAT", 0x01000030, 0, Subrecord("EDID", ZString("PatchNew")))))),
        Record("ARMO", 0x00000060, 0, Combine(
            Subrecord("EDID", ZString("SyntheticArmor")),
            Subrecord("DATA", winningArmorData)))));

    using var stack = FalloutPluginStack.Load(fixtureRoot, ["Master.esm", "Patch.esp"]);
    var variableData = new byte[24];
    BinaryPrimitives.WriteUInt32LittleEndian(variableData, 1);
    var questScriptHeader = new byte[20];
    BinaryPrimitives.WriteUInt32LittleEndian(questScriptHeader.AsSpan(4), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(questScriptHeader.AsSpan(8), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(questScriptHeader.AsSpan(12), 1);
    questScriptHeader[16] = 1;
    byte[] Script(uint form, string body) => Record("SCPT", form, 0, Combine(
        Subrecord("SCHR", questScriptHeader),
        Subrecord("SCTX", Encoding.ASCII.GetBytes("scn Synthetic\nshort given\nbegin GameMode\n" + body + "\nend")),
        Subrecord("SLSD", variableData), Subrecord("SCVR", ZString("given")),
        Subrecord("SCRO", UInt32(0x14)), Subrecord("SCRO", UInt32(0x60)), Subrecord("SCRO", UInt32(0x01000079)),
        Subrecord("SCRO", UInt32(0x01000080))));
    byte[] Quest(uint form, uint script, byte flags = 0x11, float delay = 0) => Record("QUST", form, 0, Combine(
        Subrecord("EDID", ZString("SyntheticQuest" + form)),
        Subrecord("DATA", Combine([flags, 1, 0, 0], BitConverter.GetBytes(delay))), Subrecord("SCRI", UInt32(script))));
    byte[] GlobalStage(uint form, string source, bool bound = true, uint? referenceCount = null) => Record("QUST", form, 0, Combine(
        Subrecord("EDID", ZString("GlobalStage" + form)), Subrecord("DATA", new byte[8]),
        Subrecord("INDX", new byte[2]), Subrecord("QSDT", [0]),
        Subrecord("SCHR", Combine(new byte[4], UInt32(referenceCount ?? (bound ? 1u : 0u)), new byte[12])),
        Subrecord("SCTX", Encoding.ASCII.GetBytes(source)), bound ? Subrecord("SCRO", UInt32(0x01000080)) : []));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Scripts.esp"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))),
        Quest(0x01000070, 0x01000073), Quest(0x01000071, 0x01000074), Quest(0x01000072, 0x01000075),
        Quest(0x01000076, 0x01000077, flags: 1, delay: 0.125f),
        Script(0x01000077, "set given to 1"),
        Script(0x01000073, "if given == 0\nShowMessage SyntheticMessage\nPlayer.AddItem SyntheticArmor 2\nset SyntheticGlobal to SyntheticGlobal + 0.1\nset given to 1\nendif"),
        Script(0x01000074, "set given to 1\nset SyntheticGlobal to 999\nPlayer.AddItem SyntheticArmor 4\nShowMessage SyntheticMessage\nUnsupportedReachedCommand"),
        Script(0x01000075, "Player.AddItem SyntheticArmor 1.5"),
        Record("GLOB", 0x01000080, 0, Combine(Subrecord("EDID", ZString("SyntheticGlobal")),
            Subrecord("FNAM", [(byte)'s']), Subrecord("FLTV", BitConverter.GetBytes(1.25f)))),
        GlobalStage(0x01000081, "set SyntheticGlobal to SyntheticGlobal + 0.1\nset SyntheticGlobal to SyntheticGlobal + 0.1"),
        GlobalStage(0x01000082, "set SyntheticGlobal to 9", bound: false),
        GlobalStage(0x01000083, "if 0\nset SyntheticGlobal to 9\nendif"),
        GlobalStage(0x01000084, "set SyntheticGlobal to 9\nset SyntheticGlobal to 1e40"),
        GlobalStage(0x01000085, "set SyntheticGlobal to 9", referenceCount: 2),
        Record("MESG", 0x01000079, 0, Combine(Subrecord("EDID", ZString("SyntheticMessage")),
            Subrecord("FULL", ZString("Synthetic title")), Subrecord("DESC", ZString("Synthetic body")),
            Subrecord("DNAM", UInt32(1)), Subrecord("INAM", UInt32(0)), Subrecord("ITXT", ZString("Synthetic choice"))))));
    using (var scriptStack = FalloutPluginStack.Load(fixtureRoot, ["Master.esm", "Scripts.esp"]))
    {
        var stageGlobals = FalloutGlobalState.Read(scriptStack);
        var globalGraph = FalloutOpeningPlayerControlResolver.Resolve(scriptStack,
            Enumerable.Range(0x81, 5).Select(id => "GlobalStage" + (0x01000000u + (uint)id)).ToArray());
        FalloutStageGlobalProgram GlobalProgram(int id) => FalloutStageGlobalProgram.Read(scriptStack,
            globalGraph.Stage("GlobalStage" + (0x01000000u + (uint)id), 0));
        var prepared = GlobalProgram(0x81).Prepare(stageGlobals);
        var globalKey = new FalloutFormKey("Scripts.esp", 0x80);
        Require(prepared.Count == 2 && prepared[0].Value == 1.35f && prepared[1].Value == 1.45f &&
            prepared.Select(write => write.Line).SequenceEqual([0, 1]) && prepared.All(write => write.Form == globalKey) &&
            stageGlobals.Get(globalKey) == 1.25f, "Stage globals lost ordered Float32 arithmetic or mutated during preparation.");
        ExpectFailure(() => GlobalProgram(0x82), "compiled reference binding");
        ExpectFailure(() => GlobalProgram(0x83), "unconditional");
        ExpectFailure(() => GlobalProgram(0x84).Prepare(stageGlobals), "Float32 storage");
        ExpectFailure(() => GlobalProgram(0x85), "reference count");
        Require(stageGlobals.Get(globalKey) == 1.25f, "Rejected stage globals partially committed state.");
        foreach (var write in prepared) stageGlobals.Set(write.Form, write.Value);
        var coldStageGlobals = FalloutGlobalState.Read(scriptStack);
        coldStageGlobals.Restore(JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(stageGlobals.Capture()))!);
        Require(coldStageGlobals.Get(globalKey) == 1.45f, "Cold globals lost a result-script write.");
        Console.WriteLine("OPENNV_STAGE_GLOBALS_PASS references=true sourceOrder=true float32=true failureAtomic=true coldStorage=true");
        var questState = new FalloutQuestState(scriptStack);
        var playerInventory = new FalloutPlayerInventory();
        var globals = FalloutGlobalState.Read(scriptStack);
        var scriptRuntime = new FalloutQuestScripts(scriptStack, questState, new HashSet<FalloutFormKey>(), playerInventory, globals, defaultProcessingDelay: 5);
        scriptRuntime.Advance(5);
        scriptRuntime.Advance(0);
        Require(playerInventory.Items is [{ Count: 2 }] &&
            questState.Variable(new("Scripts.esp", 0x70), 1) == 1 && questState.Variable(new("Scripts.esp", 0x71), 1) == 0,
            "Script effects duplicated, fractional counts were admitted, or an unsupported command partially committed.");
        Require(globals.Get(new("Scripts.esp", 0x80)) == 1.35f,
            "Global script writes lost Float32 storage, repeated the grant, or committed a rejected transaction.");
        Require(scriptRuntime.TryTakeMessage(out var message) && message is { Title: "Synthetic title", Text: "Synthetic body" } &&
            message.Buttons.SequenceEqual(["Synthetic choice"]) && !scriptRuntime.TryTakeMessage(out _),
            "Source message identity or transactional queue differs.");
        var beforeMenu = scriptRuntime.Capture();
        Require(beforeMenu.Instances.Single(instance => instance.Quest.ObjectId == 0x76).Clock is
            { Remaining: 0.125f, Invocations: 1 } && questState.Variable(new("Scripts.esp", 0x76), 1) == 1,
            "An unrelated quest flag suppressed the source processing delay.");
        scriptRuntime.Advance(2.25, gameMode: false);
        var duringMenu = scriptRuntime.Capture();
        Require(duringMenu.Instances.Single(instance => instance.Quest.ObjectId == 0x70).Clock is
            { Remaining: 4, Elapsed: 0, Invocations: 2 } &&
            duringMenu.Instances.Select(instance => instance.Executions).SequenceEqual(beforeMenu.Instances.Select(instance => instance.Executions)),
            "Modal time froze the quest countdown or executed a GameMode block.");
        scriptRuntime.Advance(3.25, gameMode: false);
        Require(scriptRuntime.Capture().Instances.Single(instance => instance.Quest.ObjectId == 0x70).Clock is
            { Remaining: 0.75f, Elapsed: 3.25f, Invocations: 2 } && globals.Get(new("Scripts.esp", 0x80)) == 1.35f,
            "A due modal invocation lost overshoot or committed GameMode effects.");
        var snapshotPath = Path.Combine(fixtureRoot, "quest-state.json");
        questState.SetVariable(new("Scripts.esp", 0x70), 1, 1.0000000000000002);
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(questState.Capture()));
        var coldQuestState = new FalloutQuestState(scriptStack);
        _ = coldQuestState.Variable(new("Scripts.esp", 0x70), 1);
        coldQuestState.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(File.ReadAllText(snapshotPath))!);
        Require(coldQuestState.Variable(new("Scripts.esp", 0x70), 1) == 1.0000000000000002, "Quest Float64 values lost precision across save/restore.");
        var coldInventory = new FalloutPlayerInventory();
        coldInventory.Restore(FalloutCampaignInventoryResolver.Resolve(scriptStack,
            playerInventory.Items.Select(item => new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray(), null), []);
        var coldGlobals = FalloutGlobalState.Read(scriptStack);
        coldGlobals.Restore(JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(globals.Capture()))!);
        var coldScripts = new FalloutQuestScripts(scriptStack, coldQuestState, new HashSet<FalloutFormKey>(), coldInventory, coldGlobals, defaultProcessingDelay: 5);
        coldScripts.Restore(JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scriptRuntime.Capture()))!);
        coldScripts.Advance(0, gameMode: false);
        scriptRuntime.Advance(0, gameMode: false);
        Require(JsonSerializer.Serialize(coldScripts.Capture()) == JsonSerializer.Serialize(scriptRuntime.Capture()),
            "Cold restore lost a partially elapsed quest script clock.");
        Require(coldInventory.Items is [{ Count: 2 }] && !coldScripts.TryTakeMessage(out _), "Cold restore awarded or announced a completed grant again.");
        Require(coldGlobals.Capture().Values.SequenceEqual(globals.Capture().Values), "Cold script restore changed shared globals.");
    }
    var objectHeader = (byte[])questScriptHeader.Clone();
    objectHeader[16] = 0;
    byte[] ClockScript(uint form, string name, byte[] header) => Record("SCPT", form, 0,
        Combine(Subrecord("EDID", ZString(name)), Subrecord("SCHR", header),
            Subrecord("SCTX", ZString("begin GameMode\nend"))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Timing.esm"), Combine(
        Record("TES4", 0, 0, Subrecord("HEDR", [0, 0, 0, 0])),
        ClockScript(0x350, "First", questScriptHeader), ClockScript(0x550, "ObjectScript", objectHeader),
        ClockScript(0x450, "Second", questScriptHeader), ClockScript(0x650, "Orphan", questScriptHeader),
        Quest(0x33, 0x350, flags: 1, delay: 2), Quest(0x32, 0x350, flags: 1, delay: 9),
        Record("QUST", 0x10, 0, Combine(Subrecord("DATA", [1, 0]), Subrecord("SCRI", UInt32(0x450)))),
        Record("TERM", 0x400, 0, Combine(Subrecord("SCHR", questScriptHeader), Subrecord("SCHR", questScriptHeader)))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "TimingPatch.esp"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Timing.esm"))),
        ClockScript(0x350, "WinningFirst", questScriptHeader),
        ClockScript(0x01000750, "UnattachedNew", questScriptHeader),
        Record("TERM", 0x400, 0, Subrecord("SCHR", questScriptHeader))));
    using (var timingStack = FalloutPluginStack.Load(fixtureRoot, ["Timing.esm", "TimingPatch.esp"]))
    {
        var initialization = new FalloutQuestScriptInitialization(timingStack, 5);
        var definitions = initialization.Definitions.Values.ToArray();
        Require(initialization.EmbeddedQuestScripts == 1 && initialization.Initializations == 5 &&
            definitions.Select(definition => definition.Script.ObjectId).SequenceEqual([0x750u, 0x650u, 0x450u, 0x350u]) &&
            definitions.Select(definition => definition.InitialPhase).SequenceEqual([2.5f, 1.25f, 3.75f, 0f]),
            "Initialization ignored winning embedded declarations, inactive scripts, or first registration order.");
        Require(definitions[^1].Quest == new FalloutFormKey("Timing.esm", 0x33) && definitions[^1].ProcessingDelay == 2,
            "A shared definition did not retain its last source quest binding.");
        Require(initialization.QuestOrder.Select(record => record.FormKey.ObjectId).SequenceEqual([0x10u, 0x32u, 0x33u]) &&
            FalloutQuestScriptInitialization.ProcessingDelay(initialization.QuestOrder[0]) == 0,
            "Quest order or the legacy DATA delay differs.");
        var timed = new FalloutQuestScripts(timingStack, new(timingStack), new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 5);
        timed.Advance(0, gameMode: false);
        timed.Advance(0.75, gameMode: false);
        var shared = timed.Capture().Instances.Where(instance => instance.Script.ObjectId == 0x350).ToArray();
        Require(shared.Length == 2 && shared.All(instance => instance.Clock is { Remaining: 0.5f, Elapsed: 1.5f, Invocations: 1 }) &&
            shared.All(instance => instance.Executions == 0), "Two quests invented separate definition clocks or ran GameMode in a menu.");
        var fresh = new FalloutQuestScripts(timingStack, new(timingStack), new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 5);
        var untouched = JsonSerializer.Serialize(fresh.Capture());
        var corrupted = timed.Capture() with
        {
            Instances = timed.Capture().Instances.Select(instance => instance.Quest.ObjectId == 0x32
                ? instance with { Remaining = 0.25, Clock = instance.Clock! with { Remaining = 0.25f } } : instance).ToArray(),
        };
        ExpectFailure(() => fresh.Restore(corrupted), "shared script clocks disagree");
        Require(JsonSerializer.Serialize(fresh.Capture()) == untouched, "Rejected shared clocks partially restored the owner.");
        fresh.Restore(JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(timed.Capture()))!);
        fresh.Advance(0.25, gameMode: false);
        timed.Advance(0.25, gameMode: false);
        Require(JsonSerializer.Serialize(fresh.Capture()) == JsonSerializer.Serialize(timed.Capture()),
            "Shared definition cadence changed after a cold restore.");
        Console.WriteLine("OPENNV_SCRIPT_INITIALIZATION_PASS sourceOrder=true embedded=true sharedDefinition=true coldRestore=true");
    }
    Require(stack.Plugins.Count == 2, "Plugin count differs.");
    Require(stack.Plugins[1].Plugin.Masters.SequenceEqual(["Master.esm"]), "Master casing was not canonicalized.");
    Require(stack.Plugins.All(item => item.Sha256.Length == 64 && item.Bytes > 0), "Plugin provenance is incomplete.");

    var campaignInventory = FalloutCampaignInventoryResolver.Resolve(
        stack,
        [
            new FalloutCampaignInventoryRequest(0x00000060, "SyntheticArmor", "ARMO", 1),
            new FalloutCampaignInventoryRequest(0x00000061, "SyntheticWeapon", "WEAP", 1),
            new FalloutCampaignInventoryRequest(0x00000062, "SyntheticAmmo", "AMMO", 24),
        ],
        new FalloutCampaignWeaponRequest(0x00000061, 0x00000062, 18, 6, 4, 7));
    Require(
        campaignInventory.Items.Count == 3 &&
        campaignInventory.Items[0].FormKey == new FalloutFormKey("Master.esm", 0x60) &&
        campaignInventory.Items[0].Value == 40 &&
        campaignInventory.Items[0].Weight == 3.5f &&
        campaignInventory.EquippedWeapon is { Damage: 18, ClipSize: 6, AmmoInMagazine: 4, AnimationType: 7 } &&
        campaignInventory.EquippedWeapon.Ammo == new FalloutFormKey("Master.esm", 0x62),
        "Live campaign inventory/equipment resolution failed.");
    ExpectFailure(
        () => FalloutCampaignInventoryResolver.Resolve(
            stack,
            [new FalloutCampaignInventoryRequest(0x00000061, "SyntheticWeapon", "WEAP", 1)],
            new FalloutCampaignWeaponRequest(0x00000061, 0x00000062, 19, 6, 4, 7)),
        "differs from the live winning record");
    ExpectFailure(
        () => FalloutCampaignInventoryResolver.Resolve(
            stack,
            [new FalloutCampaignInventoryRequest(0x02000060, "SyntheticArmor", "ARMO", 1)],
            null),
        "inactive load-order index");

    var baseLayer = Path.Combine(fixtureRoot, "base-layer");
    var modLayer = Path.Combine(fixtureRoot, "mod-layer");
    Directory.CreateDirectory(baseLayer);
    Directory.CreateDirectory(modLayer);
    var layeredMasterPath = Path.Combine(baseLayer, "Master.esm");
    var layeredPatchPath = Path.Combine(modLayer, "Patch.esp");
    File.Copy(Path.Combine(fixtureRoot, "Master.esm"), layeredMasterPath);
    File.Copy(Path.Combine(fixtureRoot, "Patch.esp"), layeredPatchPath);
    var masterInfo = new FileInfo(layeredMasterPath);
    var patchInfo = new FileInfo(layeredPatchPath);
    var registeredMasterSha = new string('a', 64);
    using var layeredStack = FalloutPluginStack.Load([
        new FalloutPluginSource(
            "Master.esm",
            layeredMasterPath,
            masterInfo.Length,
            new DateTimeOffset(masterInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
            registeredMasterSha),
        new FalloutPluginSource(
            "Patch.esp",
            layeredPatchPath,
            patchInfo.Length,
            new DateTimeOffset(patchInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds())]);
    Require(
        layeredStack.Plugins[0].Sha256 == registeredMasterSha &&
        layeredStack.Plugins[1].Plugin.Path == layeredPatchPath &&
        ReadEditorId(layeredStack.GetEffective(new FalloutFormKey("Master.esm", 0x10))) == "Winner",
        "Layered absolute-path stack or registered provenance failed.");
    ExpectFailure(
        () => FalloutPluginStack.Load([
            new FalloutPluginSource(
                "Master.esm",
                layeredMasterPath,
                masterInfo.Length + 1,
                new DateTimeOffset(masterInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds())]),
        "changed after registration");

    File.WriteAllBytes(Path.Combine(fixtureRoot, "Middle.esm"), Record(
        "TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Target.esm"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))),
        Record("STAT", 0x01000801, 0, Subrecord("EDID", ZString("Target")))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Inject.esp"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))),
        Record("STAT", 0x02000801, 0, Subrecord("EDID", ZString("Injected")))));
    using var injectedStack = FalloutPluginStack.Load(
        fixtureRoot,
        ["Master.esm", "Middle.esm", "Target.esm", "Inject.esp"]);
    Require(
        ReadEditorId(injectedStack.GetEffective(new FalloutFormKey("Target.esm", 0x801))) == "Injected",
        "Earlier-load-slot injected record resolution failed.");
    ExpectFailure(
        () => FalloutPluginStack.Load(
            fixtureRoot,
            ["Master.esm", "Inject.esp", "Target.esm"]),
        "no safe configured injection target");
    File.WriteAllBytes(Path.Combine(fixtureRoot, "LowInject.esp"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))),
        Record("STAT", 0x02000001, 0, Subrecord("EDID", ZString("Unsafe")))));
    ExpectFailure(
        () => FalloutPluginStack.Load(
            fixtureRoot,
            ["Master.esm", "Middle.esm", "Target.esm", "LowInject.esp"]),
        "no safe configured injection target");

    File.WriteAllBytes(Path.Combine(fixtureRoot, "Fallout3.esm"), Combine(
        Record("TES4", 0, 0, []),
        Record("REFR", 0x01000802, 0, Subrecord("EDID", ZString("Fallout3SelfNamespace")))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Anchorage.esm"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Fallout3.esm"))),
        Record("REFR", 0x02000803, 0, Subrecord("EDID", ZString("AnchorageSelfNamespace")))));
    using var fallout3SelfNamespaces = FalloutPluginStack.Load(
        fixtureRoot,
        ["Fallout3.esm", "Anchorage.esm"]);
    Require(
        ReadEditorId(fallout3SelfNamespaces.GetEffective(new FalloutFormKey("Fallout3.esm", 0x802))) ==
        "Fallout3SelfNamespace" &&
        ReadEditorId(fallout3SelfNamespaces.GetEffective(new FalloutFormKey("Anchorage.esm", 0x803))) ==
        "AnchorageSelfNamespace",
        "Fallout 3 ESM self-namespace resolution failed.");

    var masterKey = new FalloutFormKey("Master.esm", 0x10);
    var removedKey = new FalloutFormKey("Master.esm", 0x20);
    var patchKey = new FalloutFormKey("Patch.esp", 0x30);
    Require(ReadEditorId(stack.GetEffective(masterKey)) == "Winner", "Last-wins override failed.");
    Require(!stack.TryGetEffective(removedKey, out _), "Deleted winner remained effective.");
    Require(stack.TryGetWinner(removedKey, out var deleted) && deleted.IsDeleted, "Deletion provenance was lost.");
    Require(stack.RuntimeFormId(patchKey) == 0x01000030, "Runtime FormID adjustment failed.");
    Require(stack.Plugins[1].Plugin.AdjustOptionalFormId(0) is null, "Optional null FormID failed.");

    var extended = stack.Plugins[0].Plugin.Records.Single(record => record.RawFormId == 0x10);
    Require(extended.ReadSubrecords().Single().Data.Span.SequenceEqual(extendedValue), "XXXX size failed.");
    Require(ReadEditorId(stack.Plugins[0].Plugin.Records.Single(record => record.RawFormId == 0x40)) == "Compressed", "Invalid-checksum zlib compatibility failed.");
    Require(ReadEditorId(stack.Plugins[0].Plugin.Records.Single(record => record.RawFormId == 0x41)) == "Checksummed", "Checksummed zlib failed.");
    Require(stack.Plugins[0].Plugin.Records.Single(record => record.RawFormId == 0x10).Groups.Count == 1, "GRUP context failed.");
    Require(stack.Plugins[1].Plugin.Records.Single(record => record.RawFormId == 0x01000030).Groups.Count == 2, "Recursive GRUP context failed.");
    Require(stack.Plugins[0].Plugin.Records.Single(record => record.RawFormId == 0x50).ReadSubrecords().Single().Signature == "5IAD", "Binary IAD failed.");
    Require(stack.EffectiveRecords("STAT").Select(record => stack.RuntimeFormId(record.FormKey)).SequenceEqual([0x00000010u, 0x00000040u, 0x01000030u]), "Effective order differs.");

    ExpectFailure(() => stack.Plugins[1].Plugin.AdjustFormId(0x02000001), "undeclared local namespace");
    ExpectFailure(() => FalloutPluginStack.Load(fixtureRoot, ["Patch.esp", "Master.esm"]), "earlier in load order");
    ExpectFailure(() => FalloutPluginStack.Load(fixtureRoot, ["Master.esm", "master.ESM"]), "duplicate name");

    File.WriteAllBytes(Path.Combine(fixtureRoot, "BadType.esp"), Combine(
        Record("TES4", 0, 0, Subrecord("MAST", ZString("Master.esm"))),
        Record("MISC", 0x00000010, 0, Subrecord("EDID", ZString("WrongType")))));
    ExpectFailure(() => FalloutPluginStack.Load(fixtureRoot, ["Master.esm", "BadType.esp"]), "changes record type");

    File.WriteAllBytes(Path.Combine(fixtureRoot, "Malformed.esm"), Combine(
        Record("TES4", 0, 0, []),
        Record("STAT", 1, compressedFlag, Combine(UInt32(16), [1, 2, 3, 4, 5, 6]))));
    using var malformed = FalloutPlugin.Open(Path.Combine(fixtureRoot, "Malformed.esm"));
    ExpectFailure(() => malformed.Records.Single(record => record.RawFormId == 1).ReadData(), "invalid zlib data");

    File.WriteAllBytes(Path.Combine(fixtureRoot, "Dangling.esm"), Combine(
        Record("TES4", 0, 0, []),
        Record("STAT", 1, 0, Subrecord("XXXX", UInt32(70_000)))));
    using var dangling = FalloutPlugin.Open(Path.Combine(fixtureRoot, "Dangling.esm"));
    ExpectFailure(
        () => dangling.Records.Single(record => record.RawFormId == 1).ReadSubrecords().ToArray(),
        "dangling XXXX");

    var oversizedGroup = Group("STAT", 0, []);
    BinaryPrimitives.WriteUInt32LittleEndian(oversizedGroup.AsSpan(4), 25);
    File.WriteAllBytes(Path.Combine(fixtureRoot, "BadBounds.esm"), Combine(
        Record("TES4", 0, 0, []), oversizedGroup));
    ExpectFailure(
        () => FalloutPlugin.Open(Path.Combine(fixtureRoot, "BadBounds.esm")),
        "exceeds its parent");

    var transform = new byte[sizeof(float) * 6];
    BinaryPrimitives.WriteSingleLittleEndian(transform.AsSpan(0), 10.0f);
    BinaryPrimitives.WriteSingleLittleEndian(transform.AsSpan(sizeof(float)), 20.0f);
    BinaryPrimitives.WriteSingleLittleEndian(transform.AsSpan(sizeof(float) * 2), 30.0f);
    var scale = new byte[sizeof(float)];
    BinaryPrimitives.WriteSingleLittleEndian(scale, 1.25f);
    var lightData = new byte[32];
    BinaryPrimitives.WriteInt32LittleEndian(lightData, -1);
    BinaryPrimitives.WriteUInt32LittleEndian(lightData.AsSpan(4), 256);
    lightData[8] = 100;
    lightData[9] = 80;
    lightData[10] = 40;
    BinaryPrimitives.WriteSingleLittleEndian(lightData.AsSpan(16), 1.0f);
    BinaryPrimitives.WriteSingleLittleEndian(lightData.AsSpan(20), 90.0f);
    var lightIntensity = new byte[sizeof(float)];
    BinaryPrimitives.WriteSingleLittleEndian(lightIntensity, 1.5f);
    var lightRadius = new byte[sizeof(float)];
    BinaryPrimitives.WriteSingleLittleEndian(lightRadius, -96.0f);
    var sourceTeleport = Teleport(
        0x127,
        [100.0f, 200.0f, 300.0f],
        [0.0f, 0.0f, 1.25f]);
    var returnTeleport = Teleport(
        0x126,
        [10.0f, 20.0f, 30.0f],
        [0.0f, 0.0f, -1.25f]);
    var landNormals = new byte[33 * 33 * 3];
    for (var index = 0; index < 33 * 33; ++index)
        landNormals[index * 3 + 2] = 127;
    var landHeights = new byte[sizeof(float) + 33 * 33 + 3];
    var landOpacity = new byte[8];
    BinaryPrimitives.WriteSingleLittleEndian(landOpacity.AsSpan(sizeof(uint)), 0.5f);
    var vigorPrimitive = new byte[32];
    BinaryPrimitives.WriteSingleLittleEndian(vigorPrimitive, 100.0f);
    BinaryPrimitives.WriteSingleLittleEndian(vigorPrimitive.AsSpan(4), 80.0f);
    BinaryPrimitives.WriteSingleLittleEndian(vigorPrimitive.AsSpan(8), 60.0f);
    BinaryPrimitives.WriteUInt32LittleEndian(vigorPrimitive.AsSpan(28), 1);
    var farewellPrimitive = new byte[32];
    BinaryPrimitives.WriteSingleLittleEndian(farewellPrimitive, 120.0f);
    BinaryPrimitives.WriteSingleLittleEndian(farewellPrimitive.AsSpan(4), 90.0f);
    BinaryPrimitives.WriteSingleLittleEndian(farewellPrimitive.AsSpan(8), 70.0f);
    BinaryPrimitives.WriteUInt32LittleEndian(farewellPrimitive.AsSpan(28), 1);
    byte[] ClockGlobal(uint form, float value) => Record("GLOB", form, 0, Combine(
        Subrecord("EDID", ZString("SyntheticClock" + form)), Subrecord("FNAM", [(byte)'s']),
        Subrecord("FLTV", BitConverter.GetBytes(value))));
    var weatherColors = Enumerable.Range(0, 240).Select(value => (byte)(value % 251)).ToArray();
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Cell.esm"), Combine(
        Record("TES4", 0, 0, []),
        ClockGlobal(0x35, 2210), ClockGlobal(0x36, 11), ClockGlobal(0x37, 31),
        ClockGlobal(0x38, 23.99f), ClockGlobal(0x39, 10), ClockGlobal(0x3a, 60),
        Record("WTHR", 0x15e, 0, Subrecord("NAM0", weatherColors)),
        Record("CLMT", 0x15f, 0, Subrecord("TNAM", [24, 48, 96, 120, 0, 0])),
        Record("REGN", 0x990, 0, Subrecord("EDID", ZString("SyntheticRegion"))),
        Record("CELL", 0x100, 0, Combine(
            Subrecord("EDID", ZString("SyntheticCell")),
            Subrecord("DATA", [1]),
            Subrecord("XCLL", new byte[40]),
            Subrecord("LTMP", UInt32(0)),
            Subrecord("LNAM", UInt32(0x9f)))),
        Record("STAT", 0x110, 0, Combine(
            Subrecord("EDID", ZString("SyntheticBase")),
            Subrecord("MODL", ZString("clutter/test.nif")))),
        Record("LIGH", 0x111, 0, Combine(
            Subrecord("EDID", ZString("SyntheticLight")),
            Subrecord("DATA", lightData),
            Subrecord("FNAM", lightIntensity))),
        Record("STAT", 0x112, 0, Subrecord("EDID", ZString("XMarkerHeading"))),
        Record("DOOR", 0x113, 0, Subrecord("EDID", ZString("SyntheticInteriorDoor"))),
        Record("DOOR", 0x114, 0, Subrecord("EDID", ZString("SyntheticExteriorDoor"))),
        Record("ARMO", 0x115, 0, Combine(
            Subrecord("EDID", ZString("SyntheticPipBoy")),
            Subrecord("DATA", armorData))),
        Record("ARMO", 0x116, 0, Combine(
            Subrecord("EDID", ZString("SyntheticPipBoyGlove")),
            Subrecord("DATA", armorData))),
        Record("ARMO", 0x117, 0, Combine(
            Subrecord("EDID", ZString("SyntheticVaultSuit")),
            Subrecord("DATA", armorData))),
        Record("ARMO", 0x118, 0, Combine(
            Subrecord("EDID", ZString("SyntheticFarewellAid")),
            Subrecord("DATA", armorData))),
        Record("ARMO", 0x119, 0, Combine(
            Subrecord("EDID", ZString("SyntheticFarewellTool")),
            Subrecord("DATA", armorData))),
        Record("ARMO", 0x11a, 0, Combine(
            Subrecord("EDID", ZString("SyntheticFarewellWeapon")),
            Subrecord("DATA", armorData))),
        Record("NPC_", 0x180, 0, Combine(
            Subrecord("EDID", ZString("Player")),
            Subrecord("ACBS", new byte[24]),
            Subrecord("DATA", Combine(UInt32(100), [5, 5, 5, 5, 5, 5, 5])),
            Subrecord("RNAM", UInt32(0x181)),
            Subrecord("HNAM", UInt32(0x182)),
            Subrecord("ENAM", UInt32(0x184)))),
        Record("RACE", 0x181, 0, Combine(
            Subrecord("EDID", ZString("SyntheticRace")),
            Subrecord("FULL", ZString("Synthetic Race")),
            Subrecord("DATA", Combine(new byte[32], UInt32(1))),
            Subrecord("DNAM", Combine(UInt32(0x182), UInt32(0x183))),
            Subrecord("HNAM", Combine(UInt32(0x182), UInt32(0x183))),
            Subrecord("ENAM", UInt32(0x184)))),
        Record("HAIR", 0x182, 0, Combine(
            Subrecord("EDID", ZString("SyntheticMaleHair")),
            Subrecord("FULL", ZString("Synthetic Male Hair")),
            Subrecord("DATA", [0x05]))),
        Record("HAIR", 0x183, 0, Combine(
            Subrecord("EDID", ZString("SyntheticFemaleHair")),
            Subrecord("FULL", ZString("Synthetic Female Hair")),
            Subrecord("DATA", [0x03]))),
        Record("EYES", 0x184, 0, Combine(Subrecord("EDID", ZString("SyntheticEyes")),
            Subrecord("FULL", ZString("Synthetic Eyes")), Subrecord("DATA", [1]))),
        Record("RACE", 0x7fff12, 0, Combine(
            Subrecord("EDID", ZString("AnotherPlayableRace")), Subrecord("FULL", ZString("Another Race")),
            Subrecord("DATA", Combine(new byte[32], UInt32(1))),
            Subrecord("DNAM", Combine(UInt32(0x7fff10), UInt32(0x7fff10))),
            Subrecord("HNAM", UInt32(0x7fff10)), Subrecord("ENAM", UInt32(0x7fff11)))),
        Record("HAIR", 0x7fff10, 0, Combine(Subrecord("EDID", ZString("AnotherHair")),
            Subrecord("FULL", ZString("Another Hair")), Subrecord("DATA", [1]))),
        Record("EYES", 0x7fff11, 0, Combine(Subrecord("EDID", ZString("AnotherEyes")),
            Subrecord("FULL", ZString("Another Eyes")), Subrecord("DATA", [1]))),
        Record("SCPT", 0x185, 0, Combine(
            Subrecord("EDID", ZString("SyntheticVigorTriggerScript")),
            Subrecord("SCTX", ZString(
                "begin onTriggerEnter player\r\n" +
                "if IsActionRef player == 1\r\nSetStage VCG01 60\r\nendif\r\nEnd\r\n")))),
        Record("SCPT", 0x186, 0, Combine(
            Subrecord("EDID", ZString("SyntheticVigorTesterScript")),
            Subrecord("SCTX", ZString(
                "BEGIN OnActivate\r\n" +
                "if(GetStage VCG01 == 60)\r\n" +
                "ShowLoveTesterMenuParams 40;\r\nSetStage VCG01 65\r\nendif\r\nEND\r\n")))),
        Record("ACTI", 0x187, 0, Combine(
            Subrecord("EDID", ZString("VCG01VigorTesterTrigger")),
            Subrecord("SCRI", UInt32(0x185)))),
        Record("ACTI", 0x188, 0, Combine(
            Subrecord("EDID", ZString("VCG01VigorTester")),
            Subrecord("SCRI", UInt32(0x186)))),
        Record("AVIF", 0x190, 0, Combine(Subrecord("EDID", ZString("AVBarter")), Subrecord("FULL", ZString("Barter")), Subrecord("ANAM", ZString("Barter")))),
        Record("AVIF", 0x191, 0, Combine(Subrecord("EDID", ZString("AVEnergyWeapons")), Subrecord("FULL", ZString("Energy Weapons")), Subrecord("ANAM", ZString("Energy Weapons")))),
        Record("AVIF", 0x192, 0, Combine(Subrecord("EDID", ZString("AVExplosives")), Subrecord("FULL", ZString("Explosives")), Subrecord("ANAM", ZString("Explosives")))),
        Record("AVIF", 0x193, 0, Combine(Subrecord("EDID", ZString("AVLockpick")), Subrecord("FULL", ZString("Lockpick")), Subrecord("ANAM", ZString("Lockpick")))),
        Record("AVIF", 0x194, 0, Combine(Subrecord("EDID", ZString("AVMedicine")), Subrecord("FULL", ZString("Medicine")), Subrecord("ANAM", ZString("Medicine")))),
        Record("AVIF", 0x195, 0, Combine(Subrecord("EDID", ZString("AVMeleeWeapons")), Subrecord("FULL", ZString("Melee Weapons")), Subrecord("ANAM", ZString("Melee Weapons")))),
        Record("AVIF", 0x196, 0, Combine(Subrecord("EDID", ZString("AVRepair")), Subrecord("FULL", ZString("Repair")), Subrecord("ANAM", ZString("Repair")))),
        Record("AVIF", 0x197, 0, Combine(Subrecord("EDID", ZString("AVScience")), Subrecord("FULL", ZString("Science")), Subrecord("ANAM", ZString("Science")))),
        Record("AVIF", 0x198, 0, Combine(Subrecord("EDID", ZString("AVSmallGuns")), Subrecord("FULL", ZString("Guns")), Subrecord("ANAM", ZString("Guns")))),
        Record("AVIF", 0x199, 0, Combine(Subrecord("EDID", ZString("AVSneak")), Subrecord("FULL", ZString("Sneak")), Subrecord("ANAM", ZString("Sneak")))),
        Record("AVIF", 0x19a, 0, Combine(Subrecord("EDID", ZString("AVSpeech")), Subrecord("FULL", ZString("Speech")), Subrecord("ANAM", ZString("Speech")))),
        Record("AVIF", 0x19b, 0, Combine(Subrecord("EDID", ZString("AVThrowing")), Subrecord("FULL", ZString("Survival")), Subrecord("ANAM", ZString("Survival")))),
        Record("AVIF", 0x19c, 0, Combine(Subrecord("EDID", ZString("AVUnarmed")), Subrecord("FULL", ZString("Unarmed")), Subrecord("ANAM", ZString("Unarmed")))),
        Record("PERK", 0x19d, 0, Combine(
            Subrecord("EDID", ZString("SyntheticTraitOne")),
            Subrecord("FULL", ZString("Synthetic Trait One")),
            Subrecord("DATA", new byte[] { 1, 1, 1, 1, 0 }))),
        Record("PERK", 0x19e, 0, Combine(
            Subrecord("EDID", ZString("SyntheticTraitTwo")),
            Subrecord("FULL", ZString("Synthetic Trait Two")),
            Subrecord("DATA", new byte[] { 1, 1, 1, 1, 0 }))),
        Record("SCPT", 0x19f, 0, Combine(
            Subrecord("EDID", ZString("GSDocMitchellExitTriggerScript")),
            Subrecord("SCTX", ZString(
                "begin onTriggerEnter player\r\n" +
                "if GetStage VCG01 == 110\r\nSetStage VCG01 115\r\nendif\r\nend\r\n")))),
        Record("ACTI", 0x1a0, 0, Combine(
            Subrecord("EDID", ZString("GSDocMitchellExitTrigger")),
            Subrecord("SCRI", UInt32(0x19f)))),
        Record("SCPT", 0x1a1, 0, Combine(
            Subrecord("EDID", ZString("VGenericTimerScript")),
            Subrecord("SCTX", ZString(
                "if nEvent == 3\r\nSetStage VCG01 200\r\nendif\r\n")))),
        Record("QUST", 0x1a2, 0, Combine(
            Subrecord("EDID", ZString("VGenericTimer")),
            Subrecord("DATA", new byte[8]),
            Subrecord("SCRI", UInt32(0x1a1)))),
        Record("QUST", 0x140, 0, Combine(
            Subrecord("EDID", ZString("VCG00")),
            Subrecord("DATA", new byte[8]),
            Subrecord("SCRI", UInt32(0x144)),
            Subrecord("INDX", [0, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "DisablePlayerControls 1 1\r\n" +
                "DisablePlayerControls 0 0 0 0 1 0 0\r\n" +
                "PlayBink \"SyntheticIntro.bik\" 1 1 0 1\r\n" +
                "; player.moveto CommentedOutMarkerREF\r\n" +
                "if GetQuestCompleted CG04 == 0\r\n" +
                "player.moveto SyntheticPlayerStartREF\r\n" +
                "else\r\n" +
                "player.moveto SyntheticAlternateStartREF\r\n" +
                "endif\r\n" +
                "SetStage VCG00 90\r\n")),
            Subrecord("SCRO", UInt32(0x122)),
            Subrecord("SCRO", UInt32(0x142)),
            Subrecord("INDX", [90, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG00.fTimer to 3\r\n")),
            Subrecord("INDX", [95, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; immediate timer continuation\r\n")),
            Subrecord("INDX", [100, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "EnablePlayerControls 1 1 1 1 0 1 1\r\n" +
                "SetStage VCG01 0\r\n")))),
        Record("PACK", 0x141, 0, Combine(
            Subrecord("EDID", ZString("SyntheticDistractorTravel")),
            Subrecord("PLDT", Combine(UInt32(0), UInt32(0x123), UInt32(0))))),
        Record("QUST", 0x142, 0, Combine(
            Subrecord("EDID", ZString("CG04")),
            Subrecord("DATA", new byte[8]))),
        Record("QUST", 0x143, 0, Combine(
            Subrecord("EDID", ZString("VCG01")),
            Subrecord("DATA", new byte[8]),
            Subrecord("SCRI", UInt32(0x145)),
            Subrecord("INDX", [0, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "EnablePlayerControls 0 0 0 0 1\r\n" +
                "DisablePlayerControls 1 1 1 1 0 1 1\r\n" +
                "set VCG01.fTimer to 0.2\r\n")),
            Subrecord("INDX", [1, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 2.8\r\n")),
            Subrecord("INDX", [3, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("DocMitchellREF.SayTo player VCG01Intro\r\n")),
            Subrecord("INDX", [5, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 3.25\r\n")),
            Subrecord("INDX", [7, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 0\r\n")),
            Subrecord("INDX", [8, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("DocMitchellREF.SayTo player VCG01Intro\r\n")),
            Subrecord("INDX", [10, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "GetPlayerName\r\n" +
                "set VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [15, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; post-name stop\r\n")),
            Subrecord("INDX", [55, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "DisablePlayerControls 0 1 1 0 0 0 1\r\n" +
                "EnablePlayerControls 1 0 0 1 1 1 0\r\n")),
            Subrecord("INDX", [60, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; Vigor tester activation\r\n")),
            Subrecord("INDX", [65, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [70, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; Vigor reaction complete\r\n")),
            Subrecord("INDX", [80, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; psychological evaluation\r\n")),
            Subrecord("INDX", [85, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [90, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("SetTagSkills 3 1;\r\nset VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [95, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; tag skills accepted\r\n")),
            Subrecord("INDX", [98, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [100, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("set VCG01.fTimer to 0\r\n")),
            Subrecord("INDX", [102, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("ShowTraitMenu\r\nset VCG01.fTimer to 1\r\n")),
            Subrecord("INDX", [105, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; farewell transition\r\n")),
            Subrecord("INDX", [110, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString(
                "DisablePlayerControls 1 1 1 1 1 1 1\r\n" +
                "EnablePlayerControls 1 0 0 1 1 1 0\r\n")),
            Subrecord("INDX", [115, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("; farewell conversation\r\n")),
            Subrecord("INDX", [200, 0]),
            Subrecord("QSDT", [0]),
            Subrecord("SCTX", ZString("StopQuest VCG01\r\n")))),
        Record("SCPT", 0x144, 0, Combine(
            Subrecord("EDID", ZString("VCG00ScriptNV")),
            Subrecord("SCTX", ZString(
                "if getstage VCG00 == 90\r\n" +
                "  SetStage VCG00 95\r\n" +
                "elseif getstage VCG00 == 95\r\n" +
                "  SetStage VCG00 100\r\n" +
                "endif\r\n")))),
        Record("SCPT", 0x145, 0, Combine(
            Subrecord("EDID", ZString("VCG01SCRIPT")),
            Subrecord("SCTX", ZString(
                "if getstage VCG01 == 0\r\n" +
                "  SetStage VCG01 1\r\n" +
                "elseif getstage VCG01 == 1\r\n" +
                "  SetStage VCG01 3\r\n" +
                "elseif getstage VCG01 == 5\r\n" +
                "  SetStage VCG01 7\r\n" +
                "elseif getstage VCG01 == 7\r\n" +
                "  SetStage VCG01 8\r\n" +
                "elseif getstage VCG01 == 10\r\n" +
                "  SetStage VCG01 15\r\n" +
                "elseif getstage VCG01 == 55\r\n" +
                "  SetStage VCG01 60\r\n" +
                "elseif getstage VCG01 == 65\r\n" +
                "  SetStage VCG01 70\r\n" +
                "elseif getstage VCG01 == 85\r\n" +
                "  SetStage VCG01 90\r\n" +
                "elseif getstage VCG01 == 90\r\n" +
                "  SetStage VCG01 95\r\n" +
                "elseif getstage VCG01 == 98\r\n" +
                "  SetStage VCG01 100\r\n" +
                "elseif getstage VCG01 == 100\r\n" +
                "  SetStage VCG01 102\r\n" +
                "elseif getstage VCG01 == 102\r\n" +
                "  SetStage VCG01 105\r\n" +
                "endif\r\n")))),
        Record("DIAL", 0x146, 0, Combine(
            Subrecord("EDID", ZString("VCG01Intro")),
            Subrecord("DATA", [0, 0, 0, 0]))),
        GroupFormId(0x146, 7, Combine(
            Record("INFO", 0x147, 0, Combine(
                Subrecord("QSTI", UInt32(0x143)),
                Subrecord("SCTX", ZString("SetStage VCG01 5\r\n")))),
            Record("INFO", 0x148, 0, Combine(
                Subrecord("QSTI", UInt32(0x143)),
                Subrecord("SCTX", ZString(
                    "SetStage VCG01 10\r\n" +
                    "player.additem SyntheticPipBoy 1\r\n" +
                    "player.additem SyntheticPipBoyGlove 1\r\n" +
                    "player.additem SyntheticVaultSuit 1\r\n" +
                    "player.equipitem SyntheticPipBoy\r\n" +
                    "player.equipitem SyntheticPipBoyGlove\r\n" +
                    "player.equipitem SyntheticVaultSuit\r\n")))))),
        Record("INFO", 0x18b, 0, Combine(
            Subrecord("QSTI", UInt32(0x143)),
            Subrecord("SCTX", ZString("SetStage VCG01 85\r\n")))),
        Record("INFO", 0x1a4, 0, Combine(
            Subrecord("QSTI", UInt32(0x143)),
            Subrecord("SCTX", ZString(
                "; Basic starting equipment for all players\r\n" +
                "player.additem SyntheticFarewellAid 1\r\n" +
                "if IsPlayerTagSkill Barter\r\n" +
                "player.additem SyntheticFarewellTool 2\r\n" +
                "else\r\nplayer.additem SyntheticFarewellTool 1\r\nendif\r\n" +
                "if IsPlayerTagSkill Guns\r\n" +
                "player.additem SyntheticFarewellWeapon 2\r\n" +
                "else\r\nplayer.additem SyntheticFarewellWeapon 1\r\nendif\r\n")))),
        Record("INFO", 0x1a5, 0, Combine(
            Subrecord("QSTI", UInt32(0x143)),
            Subrecord("SCTX", ZString(
                "set VGenericTimer.fTimer to 0.1\r\n" +
                "set VGenericTimer.nEvent to 3\r\n" +
                "StartQuest VGenericTimer\r\n")))),
        Record("WRLD", 0x150, 0, Subrecord("EDID", ZString("SyntheticWorld"))),
        Record("LTEX", 0x160, 0, Combine(
            Subrecord("EDID", ZString("SyntheticLand")),
            Subrecord("TNAM", UInt32(0x161)))),
        Record("TXST", 0x161, 0, Combine(
            Subrecord("EDID", ZString("SyntheticLandTextureSet")),
            Subrecord("TX00", ZString("landscape\\synthetic.dds")))),
        GroupFormId(0x100, 6, Combine(
            Record("REFR", 0x120, 0, Combine(
                Subrecord("NAME", UInt32(0x110)),
                Subrecord("DATA", transform),
                Subrecord("XSCL", scale))),
            Record("REFR", 0x121, 0, Combine(
                Subrecord("NAME", UInt32(0x111)),
                Subrecord("XEMI", UInt32(0x111)),
                Subrecord("DATA", transform),
                Subrecord("XRDS", lightRadius))),
            Record("REFR", 0x122, 0x0000_0400, Combine(
                Subrecord("EDID", ZString("SyntheticPlayerStartREF")),
                Subrecord("NAME", UInt32(0x112)),
                Subrecord("DATA", transform))),
            Record("REFR", 0x123, 0x0000_0400, Combine(
                Subrecord("EDID", ZString("SyntheticPackageMarkerREF")),
                Subrecord("NAME", UInt32(0x112)),
                Subrecord("DATA", transform))),
            Record("REFR", 0x126, 0x0000_0400, Combine(
                Subrecord("EDID", ZString("SyntheticInteriorDoorREF")),
                Subrecord("NAME", UInt32(0x113)),
                Subrecord("XTEL", sourceTeleport),
                Subrecord("DATA", transform))),
            Record("REFR", 0x189, 0, Combine(
                Subrecord("NAME", UInt32(0x187)),
                Subrecord("XPRM", vigorPrimitive),
                Subrecord("DATA", transform))),
            Record("REFR", 0x18a, 0, Combine(
                Subrecord("EDID", ZString("SyntheticVigorTesterREF")),
                Subrecord("NAME", UInt32(0x188)),
                Subrecord("DATA", transform))),
            Record("REFR", 0x1a3, 0, Combine(
                Subrecord("EDID", ZString("SyntheticFarewellTriggerREF")),
                Subrecord("NAME", UInt32(0x1a0)),
                Subrecord("XPRM", farewellPrimitive),
                Subrecord("DATA", transform))))),
        GroupFormId(0x150, 1, Combine(
            Record("CELL", 0x101, 0, Combine(
                Subrecord("DATA", [2]),
                Subrecord("XCLC", new byte[12]),
                Subrecord("LTMP", UInt32(0)),
                Subrecord("LNAM", UInt32(0x9f)))),
            GroupFormId(0x101, 6, Combine(
                Record("REFR", 0x127, 0x0000_0400, Combine(
                    Subrecord("EDID", ZString("SyntheticExteriorDoorREF")),
                    Subrecord("NAME", UInt32(0x114)),
                    Subrecord("XTEL", returnTeleport),
                    Subrecord("DATA", transform))),
                Record("LAND", 0x170, 0, Combine(
                    Subrecord("DATA", UInt32(1)),
                    Subrecord("VNML", landNormals),
                    Subrecord("VHGT", landHeights),
                    Subrecord("BTXT", LayerHeader(0x160, 0, 0xffff)),
                    Subrecord("BTXT", LayerHeader(0x160, 1, 0xffff)),
                    Subrecord("BTXT", LayerHeader(0x160, 2, 0xffff)),
                    Subrecord("BTXT", LayerHeader(0x160, 3, 0xffff)),
                    Subrecord("ATXT", LayerHeader(0, 0, 0)),
                    Subrecord("VTXT", landOpacity))))),
            Record("CELL", 0x102, 0, Combine(
                Subrecord("DATA", [0]),
                Subrecord("XCLC", Combine(UInt32(1), UInt32(0))),
                Subrecord("LTMP", UInt32(0)),
                Subrecord("LNAM", UInt32(0x9f)))),
            GroupFormId(0x102, 6, Record("LAND", 0x172, 0, Combine(
                Subrecord("DATA", UInt32(1)),
                Subrecord("VNML", landNormals),
                Subrecord("VHGT", landHeights),
                Subrecord("BTXT", LayerHeader(0x160, 0, 0xffff)),
                Subrecord("BTXT", LayerHeader(0x160, 1, 0xffff)),
                Subrecord("BTXT", LayerHeader(0x160, 2, 0xffff)))))))));
    using var cellStack = FalloutPluginStack.Load(fixtureRoot, ["Cell.esm"]);
    var syntheticCellForVigor = FalloutCellSceneReader.Read(
        cellStack,
        new FalloutFormKey("Cell.esm", 0x100));
    var syntheticRaceSex = FalloutNativeRaceSexResolver.Resolve(cellStack);
    Require(
        syntheticRaceSex.Initial == syntheticRaceSex.Male &&
        syntheticRaceSex.Male.HairEditorId == "SyntheticMaleHair" &&
        syntheticRaceSex.Female.HairEditorId == "SyntheticFemaleHair" &&
        syntheticRaceSex.Male.EyesEditorId == "SyntheticEyes",
        "Live Player/RACE sex-specific identity resolution failed.");
    var changedRace = syntheticRaceSex.Select(0x7fff12, true, syntheticRaceSex.Initial);
    FalloutNativeRaceSexResolver.Validate(syntheticRaceSex, changedRace);
    Require(changedRace.RaceEditorId == "AnotherPlayableRace" && changedRace.HairEditorId == "AnotherHair" &&
        changedRace.EyesEditorId == "AnotherEyes", "Playable race selection was restricted to Player defaults.");
    ExpectFailure(() => FalloutNativeRaceSexResolver.Validate(syntheticRaceSex,
        changedRace with { HairRuntimeFormId = syntheticRaceSex.Male.HairRuntimeFormId, HairEditorId = syntheticRaceSex.Male.HairEditorId }),
        "differs from the live Player/RACE graph");
    ExpectFailure(() => FalloutNativeRaceSexResolver.Validate(syntheticRaceSex, syntheticRaceSex.Male with { Female = true }),
        "differs from the live Player/RACE graph");
    var syntheticVigor = FalloutNativeVigorResolver.Resolve(cellStack, syntheticCellForVigor);
    var syntheticSpecial = syntheticVigor.Initial.WithValue(0, 10);
    FalloutNativeVigorResolver.Validate(syntheticVigor, syntheticSpecial);
    ExpectFailure(
        () => FalloutNativeVigorResolver.Validate(syntheticVigor, syntheticVigor.Initial),
        "differs from the live Vigor allocation contract");
    Require(
        syntheticVigor.TriggerFromStage == 55 &&
        syntheticVigor.TesterStage == 60 &&
        syntheticVigor.CompletedStage == 65 &&
        syntheticVigor.RequiredTotal == 40 &&
        syntheticVigor.TriggerDimensionsGameUnits.SequenceEqual([100.0f, 80.0f, 60.0f]),
        "Live Vigor trigger/tester contract resolution failed.");
    var controlGraph = FalloutOpeningPlayerControlResolver.Resolve(cellStack, ["VCG00", "VCG01"]);
    var syntheticTagSkills = FalloutNativeTagSkillResolver.Resolve(cellStack, controlGraph);
    var syntheticTags = syntheticTagSkills.Skills.Take(syntheticTagSkills.RequiredCount).ToArray();
    FalloutNativeTagSkillResolver.Validate(syntheticTagSkills, syntheticTags);
    ExpectFailure(
        () => FalloutNativeTagSkillResolver.Validate(
            syntheticTagSkills,
            syntheticTags.Take(2).ToArray()),
        "differ from the live SetTagSkills/AVIF contract");
    Require(
        syntheticTagSkills.Skills.Count == 13 &&
        syntheticTagSkills.RequiredCount == 3 &&
        syntheticTagSkills.Skills.Any(value =>
            value.EditorId == "AVThrowing" && value.DisplayName == "Survival"),
        "Live SetTagSkills/AVIF contract resolution failed.");
    var syntheticTraitFarewell = FalloutNativeTraitFarewellResolver.Resolve(
        cellStack,
        controlGraph,
        syntheticCellForVigor);
    var syntheticTraits = syntheticTraitFarewell.Traits.Take(2).ToArray();
    FalloutNativeTraitFarewellResolver.ValidateTraits(syntheticTraitFarewell, syntheticTraits);
    Require(
        syntheticTraitFarewell.Traits.Count == 2 &&
        syntheticTraitFarewell.MaximumTraits == 2 &&
        syntheticTraitFarewell.TraitMenuStage == 102 &&
        syntheticTraitFarewell.ExitTriggerFromStage == 110 &&
        syntheticTraitFarewell.FarewellStage == 115 &&
        syntheticTraitFarewell.CompletedStage == 200 &&
        syntheticTraitFarewell.CompletionDelaySeconds == 0.1f &&
        syntheticTraitFarewell.ExitTriggerDimensionsGameUnits.SequenceEqual(
            [120.0f, 90.0f, 70.0f]),
        "Live trait/farewell contract resolution failed.");
    var arbitraryQuest = new FalloutFormKey("Synthetic.esm", 0x900);
    var arbitraryControls = new FalloutOpeningControlGraph(new Dictionary<string, IReadOnlyDictionary<short, FalloutOpeningControlStage>>
    {
        ["ArbitraryQuest"] = new Dictionary<short, FalloutOpeningControlStage>
        {
            [12] = new(arbitraryQuest, "ArbitraryQuest", 12, "SomeActor.SayTo player SomeTopic", []),
            [87] = new(arbitraryQuest, "ArbitraryQuest", 87, "", []),
        },
    });
    var waits = FalloutOpeningStageTransitionResolver.AddDialogueWaits(arbitraryControls, new([]));
    var sourceDialogue = new FalloutOpeningStageMachine(waits, arbitraryControls, "ArbitraryQuest", 12);
    Require(sourceDialogue.PendingBlockers.Contains("sayto"), "Script-discovered speech did not block.");
    sourceDialogue.CompleteDialogueResult("ArbitraryQuest", 87);
    Require(sourceDialogue.Stage == 87 && sourceDialogue.PendingBlockers.Count == 0,
        "Executed INFO destination was replaced by a predicted stage.");
    var enteredStages = new List<short>();
    while (sourceDialogue.TryTakeEnteredStage(out var entered)) enteredStages.Add(entered!.Stage);
    Require(enteredStages.SequenceEqual(new short[] { 12, 87 }), "Entered source-stage order was lost.");
    var completedSpeech = new FalloutOpeningStageMachine(waits, arbitraryControls, "ArbitraryQuest", 12);
    Require(completedSpeech.TryTakeEnteredStage(out _), "Initial speech stage was not published.");
    completedSpeech.CompleteDialogueSpeech();
    Require(completedSpeech.Stage == 12 && completedSpeech.PendingBlockers.Count == 0 &&
        !completedSpeech.TryTakeEnteredStage(out _), "Speech completion replayed the stage or retained its wait.");
    completedSpeech.EnterSourceStage("ArbitraryQuest", 87);
    Require(completedSpeech.Stage == 87, "Completed speech blocked a later ordinary activation result.");
    var transitionGraph = FalloutOpeningStageTransitionResolver.Resolve(cellStack, controlGraph);
    transitionGraph = FalloutOpeningStageTransitionResolver.AddDialogueResults(
        cellStack,
        controlGraph,
        transitionGraph,
        "VCG01",
        [3, 8]);
    var vcg00StageZeroControls = controlGraph.Stage("VCG00", 0).Commands.Aggregate(
        FalloutPlayerControlState.AllEnabled,
        (state, command) => command.Apply(state));
    var vcg01StageZeroControls = controlGraph.Stage("VCG01", 0).Commands.Aggregate(
        FalloutPlayerControlState.AllEnabled,
        (state, command) => command.Apply(state));
    var vcg01Stage55Controls = controlGraph.Stage("VCG01", 55).Commands.Aggregate(
        vcg01StageZeroControls,
        (state, command) => command.Apply(state));
    Require(
        controlGraph.Quests.Count == 2 &&
        !vcg00StageZeroControls.Movement && !vcg00StageZeroControls.PipBoy &&
        vcg00StageZeroControls.Fighting && !vcg00StageZeroControls.Looking &&
        !vcg01StageZeroControls.Movement && !vcg01StageZeroControls.Fighting &&
        vcg01StageZeroControls.Looking &&
        vcg01Stage55Controls.Movement && vcg01Stage55Controls.Looking &&
        !vcg01Stage55Controls.PipBoy && !vcg01Stage55Controls.Fighting,
        "Native opening player-control graph resolution failed.");
    Require(
        transitionGraph.From("VCG00", 0).Single(value => value.Kind == "stage-script") is
        {
            ToQuestEditorId: "VCG00",
            ToStage: 90,
        } introEdge && introEdge.Blockers.SequenceEqual(["playbink"]) &&
        transitionGraph.From("VCG00", 90).Single(value => value.Kind == "timer") is
        {
            ToStage: 95,
            DelaySeconds: 3.0f,
        } &&
        transitionGraph.From("VCG00", 95).Single(value => value.Kind == "timer").ToStage == 100 &&
        transitionGraph.From("VCG00", 100).Single(value => value.Kind == "stage-script") is
        {
            ToQuestEditorId: "VCG01",
            ToStage: 0,
        } &&
        transitionGraph.From("VCG01", 3).Single(value => value.Kind == "dialogue-result") is
        {
            ToQuestEditorId: "VCG01",
            ToStage: 5,
        } dialogueEdge && dialogueEdge.Blockers.SequenceEqual(["sayto"]),
        "Native opening stage/timer transition graph resolution failed.");
    var stageMachine = new FalloutOpeningStageMachine(
        transitionGraph,
        controlGraph,
        "VCG00",
        0);
    Require(
        stageMachine.QuestEditorId == "VCG00" && stageMachine.Stage == 0 &&
        stageMachine.PendingBlockers.SequenceEqual(["playbink"]) &&
        !stageMachine.ControlState.Movement && !stageMachine.ControlState.Looking,
        "Native opening stage machine did not stop at the intro movie blocker.");
    Require(
        stageMachine.CompleteBlocker("playbink") &&
        stageMachine.QuestEditorId == "VCG00" && stageMachine.Stage == 90 &&
        stageMachine.TimerSeconds == 3.0f,
        "Native opening stage machine did not enter the post-intro timer.");
    Require(
        stageMachine.AdvanceTime(3.0f) &&
        stageMachine.QuestEditorId == "VCG01" && stageMachine.Stage == 0 &&
        stageMachine.TimerSeconds == 0.2f &&
        !stageMachine.ControlState.Movement && stageMachine.ControlState.Looking,
        "Native opening stage machine did not enter the live Doc opening timer.");
    Require(
        stageMachine.AdvanceTime(0.2f) &&
        stageMachine.QuestEditorId == "VCG01" && stageMachine.Stage == 1 &&
        stageMachine.TimerSeconds == 2.8f &&
        stageMachine.AdvanceTime(2.8f) &&
        stageMachine.QuestEditorId == "VCG01" && stageMachine.Stage == 3 &&
        stageMachine.TimerSeconds is null &&
        stageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
        !stageMachine.ControlState.Movement && stageMachine.ControlState.Looking,
        "Native opening stage machine did not stop at the first dialogue-dependent stage.");
    Require(
        stageMachine.CompleteBlocker("sayto") &&
        stageMachine.Stage == 5 && stageMachine.TimerSeconds == 3.25f &&
        stageMachine.AdvanceTime(3.25f) &&
        stageMachine.Stage == 8 && stageMachine.TimerSeconds is null &&
        stageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
        stageMachine.CompleteBlocker("sayto") &&
        stageMachine.Stage == 10 && stageMachine.TimerSeconds is null &&
        stageMachine.PendingBlockers.SequenceEqual(["getplayername"]) &&
        stageMachine.CompleteBlocker("getplayername") &&
        stageMachine.TimerSeconds == 1.0f &&
        stageMachine.AdvanceTime(1.0f) && stageMachine.Stage == 15,
        "Native opening stage machine did not execute the first dialogue result.");
    var syntheticVigorMachine = new FalloutOpeningStageMachine(
        transitionGraph,
        controlGraph,
        "VCG01",
        55);
    syntheticVigorMachine.EnterSourceStage("VCG01", syntheticVigor.TesterStage);
    syntheticVigorMachine.EnterSourceStage("VCG01", syntheticVigor.CompletedStage);
    Require(
        syntheticVigorMachine.Stage == 65 &&
        syntheticVigorMachine.PendingBlockers.Count == 0,
        "Native Vigor source-trigger stage entry failed.");
    var syntheticTagMachine = new FalloutOpeningStageMachine(
        transitionGraph,
        controlGraph,
        "VCG01",
        syntheticTagSkills.PsychStage);
    syntheticTagMachine.EnterSourceStage("VCG01", syntheticTagSkills.PsychCompletedStage);
    Require(
        syntheticTagMachine.TimerSeconds == 1.0f &&
        syntheticTagMachine.AdvanceTime(1.0f) &&
        syntheticTagMachine.Stage == syntheticTagSkills.TagMenuStage &&
        syntheticTagMachine.PendingBlockers.SequenceEqual(["settagskills"]) &&
        syntheticTagMachine.CompleteBlocker("settagskills") &&
        syntheticTagMachine.TimerSeconds == 1.0f &&
        syntheticTagMachine.AdvanceTime(1.0f) &&
        syntheticTagMachine.Stage == 95,
        "Native psych-to-tag-skill source stages did not complete.");
    var syntheticTraitMachine = new FalloutOpeningStageMachine(
        transitionGraph,
        controlGraph,
        "VCG01",
        98);
    Require(
        syntheticTraitMachine.TimerSeconds == 1.0f &&
        syntheticTraitMachine.AdvanceTime(1.0f) &&
        syntheticTraitMachine.Stage == 102 &&
        syntheticTraitMachine.PendingBlockers.SequenceEqual(["showtraitmenu"]) &&
        syntheticTraitMachine.CompleteBlocker("showtraitmenu") &&
        syntheticTraitMachine.AdvanceTime(1.0f) &&
        syntheticTraitMachine.Stage == 105,
        "Native trait-menu stage graph did not complete.");
    var syntheticOpeningGrant = FalloutOpeningInventoryGrantResolver.Resolve(
        cellStack,
        controlGraph,
        "VCG01");
    Require(
        syntheticOpeningGrant.Inventory.Items.Count == 3 &&
        syntheticOpeningGrant.Inventory.Items.All(value =>
            value.RecordType == "ARMO" && value.Count == 1) &&
        syntheticOpeningGrant.EquippedRuntimeFormIds.Count == 3,
        "Native opening INFO inventory grants were not resolved.");
    var syntheticCompletedGrant = FalloutNativeTraitFarewellResolver.ResolveGrant(
        syntheticTraitFarewell,
        syntheticOpeningGrant,
        syntheticTags);
    Require(
        syntheticCompletedGrant.Inventory.Items.Single(value =>
            value.EditorId == "SyntheticFarewellAid").Count == 1 &&
        syntheticCompletedGrant.Inventory.Items.Single(value =>
            value.EditorId == "SyntheticFarewellTool").Count == 2 &&
        syntheticCompletedGrant.Inventory.Items.Single(value =>
            value.EditorId == "SyntheticFarewellWeapon").Count == 1,
        "Native farewell conditional loadout differs.");
    const string syntheticSaveCompatibilityId = "standalone:synthetic-stack";
    var campaignGlobals = FalloutGlobalState.Read(cellStack);
    var campaignCalendar = new FalloutCalendar([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "synthetic-calendar");
    var campaignTime = new FalloutGameTime(campaignGlobals, FalloutGameTimeBindings.Read(cellStack), campaignCalendar);
    campaignTime.InitializeNewGame();
    campaignTime.AdvanceSimulation(0.5f);
    var campaignSky = new FalloutSkyLightingState(cellStack, 0.5f);
    var syntheticRegion = cellStack.RuntimeFormKey(0x990);
    var nightColor = campaignSky.RegionEmittance(syntheticRegion, 23);
    Require(nightColor.Zip(new[] { 108f / 255, 109f / 255, 110f / 255 }).All(pair => MathF.Abs(pair.First - pair.Second) < 1e-6f), "Default region weather did not use its source night sunlight.");
    var noonColor = campaignSky.RegionEmittance(syntheticRegion, 12);
    Require(noonColor.Zip(new[] { 112f / 255, 113f / 255, 114f / 255 }).All(pair => MathF.Abs(pair.First - pair.Second) < 1e-6f), "Sky noon ignored the fifth source colour sample.");
    var emittanceHour = 23f;
    var externalEmittance = new FalloutExternalEmittance(cellStack, syntheticRegion,
        region => campaignSky.RegionEmittance(region, emittanceHour));
    Require(externalEmittance.Sample().SequenceEqual(nightColor), "Reference emittance did not sample shared night state.");
    emittanceHour = 12;
    Require(externalEmittance.Sample().SequenceEqual(noonColor), "Reference emittance retained a stale clock sample.");
    ExpectFailure(() => new FalloutExternalEmittance(cellStack, syntheticRegion, null), "REGN emittance owner");
    ExpectFailure(() => new FalloutExternalEmittance(cellStack, syntheticRegion, _ => [float.NaN, 0, 0]).Sample(), "invalid RGB");
    var sunriseColor = campaignSky.RegionEmittance(syntheticRegion, 5.75f);
    Require(sunriseColor.Zip(new[] { 96f / 255, 97f / 255, 98f / 255 }).All(pair => MathF.Abs(pair.First - pair.Second) < 1e-6f), "Sky sunrise midpoint did not select the first source colour sample.");
    var skyJson = JsonSerializer.Serialize(campaignSky.Capture());
    var coldSky = new FalloutSkyLightingState(cellStack, 0.5f);
    coldSky.Restore(JsonSerializer.Deserialize<FalloutSkyLightingSnapshot>(skyJson)!);
    Require(coldSky.RegionEmittance(syntheticRegion, 23).SequenceEqual(nightColor), "Cold sky restore changed region emittance.");
    ExpectFailure(() => coldSky.Restore(new(campaignSky.Climate.Form,
        [new(syntheticRegion, cellStack.RuntimeFormKey(0x110))])), "does not resolve to WTHR");
    Require(JsonSerializer.Serialize(coldSky.Capture()) == skyJson, "Rejected sky restore changed authoritative state.");
    var syntheticCampaignState = FalloutNativeCampaignSave.Capture(
        syntheticSaveCompatibilityId,
        syntheticCellForVigor.Cell.FormKey,
        syntheticCompletedGrant,
        "Synthetic Courier",
        syntheticRaceSex.Female,
        syntheticVigor,
        syntheticSpecial,
        syntheticTagSkills,
        syntheticTags,
        syntheticTraitFarewell,
        syntheticTraits,
        stageMachine.ControlState,
        [1.0f, 2.0f, 3.0f],
        [0.0f, 0.0f, 0.0f, 1.0f],
        globals: campaignGlobals.Capture(), gameTime: campaignTime.Capture(), skyLighting: campaignSky.Capture());
    var syntheticSavePath = Path.Combine(fixtureRoot, "native-campaign-save.json");
    var centeredState = syntheticCampaignState with
    {
        Schema = FalloutNativeCampaignSave.CapsuleCenteredSchema,
        PlayerPosition = [1.0f, 2.5f, 3.0f],
        Globals = null,
        GameTime = null,
        SkyLighting = null,
    };
    FalloutNativeCampaignSave.Write(syntheticSavePath, centeredState);
    var centeredRestore = FalloutNativeCampaignSave.Read(syntheticSavePath,
        syntheticSaveCompatibilityId, cellStack, syntheticVigor, syntheticTagSkills,
        syntheticOpeningGrant, syntheticTraitFarewell);
    Require(FalloutNativeCampaignSave.RestorePlayerPosition(centeredRestore.State, 0.5f)
            .SequenceEqual(syntheticCampaignState.PlayerPosition) &&
        FalloutNativeCampaignSave.RestorePlayerPosition(syntheticCampaignState, 0.5f)
            .SequenceEqual(syntheticCampaignState.PlayerPosition),
        "Legacy capsule-center saves and current foot-root saves must restore the same physical pose.");
    var upgradedState = FalloutNativeCampaignSave.WithWorldState(centeredRestore.State,
        centeredRestore.State.ActiveCell,
        FalloutNativeCampaignSave.RestorePlayerPosition(centeredRestore.State, 0.5f),
        centeredRestore.State.PlayerRotation);
    Require(upgradedState.Schema == FalloutNativeCampaignSave.QuestScriptsSchema &&
        FalloutNativeCampaignSave.RestorePlayerPosition(upgradedState, 0.5f)
            .SequenceEqual(syntheticCampaignState.PlayerPosition),
        "Saving a restored legacy player must upgrade its anchor without applying the offset twice.");
    FalloutNativeCampaignSave.Write(syntheticSavePath, syntheticCampaignState);
    var validSaveBytes = File.ReadAllBytes(syntheticSavePath);
    var missingScriptClock = syntheticCampaignState with
    {
        Quests = [],
        Scripts = new([new(new("Master.esm", 0x70), new("Master.esm", 0x73), 0, 1, null)], []),
    };
    ExpectFailure(() => FalloutNativeCampaignSave.Write(syntheticSavePath, missingScriptClock),
        "no elapsed/cadence clock owner");
    Require(File.ReadAllBytes(syntheticSavePath).SequenceEqual(validSaveBytes),
        "Rejected script clocks replaced a valid campaign save.");
    var missingClockPath = Path.Combine(fixtureRoot, "missing-script-clock-save.json");
    File.WriteAllText(missingClockPath, JsonSerializer.Serialize(missingScriptClock));
    ExpectFailure(() => FalloutNativeCampaignSave.Read(missingClockPath, syntheticSaveCompatibilityId,
        cellStack, syntheticVigor, syntheticTagSkills, syntheticOpeningGrant, syntheticTraitFarewell),
        "no elapsed/cadence clock owner");
    var syntheticRestore = FalloutNativeCampaignSave.Read(
        syntheticSavePath,
        syntheticSaveCompatibilityId,
        cellStack,
        syntheticVigor,
        syntheticTagSkills,
        syntheticOpeningGrant,
        syntheticTraitFarewell);
    var campaignColdGlobals = FalloutGlobalState.Read(cellStack);
    campaignColdGlobals.Restore(syntheticRestore.State.Globals!);
    var campaignColdTime = new FalloutGameTime(campaignColdGlobals, FalloutGameTimeBindings.Read(cellStack), campaignCalendar);
    campaignColdTime.Restore(syntheticRestore.State.GameTime!);
    for (var tick = 0; tick < 31; tick++)
    {
        campaignTime.AdvanceSimulation(1f / 60);
        campaignColdTime.AdvanceSimulation(1f / 60);
    }
    Require(syntheticRestore.State.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
        campaignColdGlobals.Capture().Values.SequenceEqual(campaignGlobals.Capture().Values) &&
        campaignColdTime.Capture() == campaignTime.Capture(),
        "Cold campaign Continue changed clock phase or Float32 globals after the same simulation ticks.");
    ExpectFailure(() => FalloutNativeCampaignSave.Write(syntheticSavePath,
        syntheticCampaignState with { GameTime = null }), "save state is invalid");
    Require(
        syntheticRestore.State.Stage == FalloutNativeCampaignSave.CompletedOpeningStage &&
        syntheticRestore.State.ActiveCell == syntheticCellForVigor.Cell.FormKey &&
        syntheticRestore.State.PlayerName == "Synthetic Courier" &&
        syntheticRestore.State.Character == syntheticRaceSex.Female &&
        syntheticRestore.State.Special == syntheticSpecial &&
        syntheticRestore.State.TagSkills.SequenceEqual(syntheticTags) &&
        syntheticRestore.State.Traits.SequenceEqual(syntheticTraits) &&
        FalloutNativeCampaignSave.RestorePlayerControls(syntheticRestore.State) ==
            stageMachine.ControlState &&
        syntheticRestore.Inventory.Items.Select(value => value.RuntimeFormId)
            .SequenceEqual(syntheticCompletedGrant.Inventory.Items
                .OrderBy(value => value.RuntimeFormId)
                .Select(value => value.RuntimeFormId)) &&
        Directory.GetFiles(fixtureRoot, "*.tmp").Length == 0,
        "Native opening campaign save did not cold-restore atomically.");
    var syntheticExteriorCell = new FalloutFormKey("Cell.esm", 0x100);
    var movedCampaignState = FalloutNativeCampaignSave.WithWorldState(
        syntheticRestore.State,
        syntheticExteriorCell,
        [4.0f, 5.0f, 6.0f],
        [0.0f, 0.0f, 0.0f, 1.0f]);
    FalloutNativeCampaignSave.Write(syntheticSavePath, movedCampaignState);
    var movedRestore = FalloutNativeCampaignSave.Read(
        syntheticSavePath,
        syntheticSaveCompatibilityId,
        cellStack,
        syntheticVigor,
        syntheticTagSkills,
        syntheticOpeningGrant,
        syntheticTraitFarewell);
    Require(
        movedRestore.State.ActiveCell == syntheticExteriorCell &&
        movedRestore.State.PlayerPosition.SequenceEqual([4.0f, 5.0f, 6.0f]),
        "Native campaign world state did not cold-restore its active CELL and player transform.");
    var staleCharacterState = syntheticCampaignState with
    {
        Character = syntheticCampaignState.Character with
        {
            HairRuntimeFormId = 0x00000185,
            HairEditorId = "StaleHair",
        },
    };
    FalloutNativeCampaignSave.Write(syntheticSavePath, staleCharacterState);
    ExpectFailure(
        () => FalloutNativeCampaignSave.Read(
            syntheticSavePath,
            syntheticSaveCompatibilityId,
            cellStack,
            syntheticVigor,
            syntheticTagSkills,
            syntheticOpeningGrant,
            syntheticTraitFarewell),
        "differs from the live Player/RACE graph");
    var cell = syntheticCellForVigor;
    using var eagerCellStack = FalloutPluginStack.Load(
        [new FalloutPluginSource("Cell.esm", Path.Combine(fixtureRoot, "Cell.esm"))],
        loadAllSignatureIndexesForAudit: true,
        out _);
    var eagerCell = FalloutCellSceneReader.Read(
        eagerCellStack,
        new FalloutFormKey("Cell.esm", 0x100));
    Require(
        eagerCellStack.WinnerRecordCount == cellStack.WinnerRecordCount &&
        eagerCellStack.EffectiveRecordCount == cellStack.EffectiveRecordCount &&
        eagerCell.References.Select(reference => reference.FormKey)
            .SequenceEqual(cell.References.Select(reference => reference.FormKey)) &&
        eagerCell.BaseObjects.Keys.OrderBy(key => eagerCellStack.RuntimeFormId(key))
            .SequenceEqual(cell.BaseObjects.Keys.OrderBy(key => cellStack.RuntimeFormId(key))),
        "Demand signature/CELL indexing differs from the eager winner graph.");
    Require(
        cell.Cell.EditorId == "SyntheticCell" &&
        cell.References.Count == 8 &&
        cell.References[0].Position.SequenceEqual([10.0f, 20.0f, 30.0f]) &&
        cell.References[0].Scale == 1.25f &&
        cell.BaseObjects[cell.References[0].Base].ModelPath == "meshes\\clutter\\test.nif" &&
        cell.References[1].RadiusAdjustmentGameUnits == -96.0f &&
        cell.BaseObjects[cell.References[1].Base].Light is
        {
            RadiusGameUnits: 256,
            Intensity: 1.5f,
            Flags: 0,
        },
        "Native CELL/reference/MODL decoding failed.");
    var syntheticDoor = FalloutDoorTransitionResolver.ResolveInteriorExits(cellStack, cell).Single();
    Require(
        syntheticDoor.SourceDoor.FormKey == new FalloutFormKey("Cell.esm", 0x126) &&
        syntheticDoor.DestinationDoor.FormKey == new FalloutFormKey("Cell.esm", 0x127) &&
        syntheticDoor.DestinationScene.Cell.FormKey == new FalloutFormKey("Cell.esm", 0x101) &&
        syntheticDoor.DestinationScene.Cell.Coordinates == (0, 0) &&
        syntheticDoor.DestinationWorldspace == new FalloutFormKey("Cell.esm", 0x150) &&
        syntheticDoor.DestinationWorldspaceEditorId == "SyntheticWorld" &&
        syntheticDoor.SourceDoor.Teleport!.Position.SequenceEqual([100.0f, 200.0f, 300.0f]) &&
        syntheticDoor.DestinationDoor.Teleport!.Door == syntheticDoor.SourceDoor.FormKey,
        "Native XTEL/CELL/WRLD reciprocal door transition differs.");
    var syntheticLandscape = FalloutLandscapeTransportResolver.Resolve(cellStack, syntheticDoor);
    Require(
        syntheticLandscape.ActiveCell == new FalloutFormKey("Cell.esm", 0x101) &&
        syntheticLandscape.Landscape == new FalloutFormKey("Cell.esm", 0x170) &&
        syntheticLandscape.ActiveCoordinates == (0, 0) &&
        syntheticLandscape.BaseLayers.Count == 4 &&
        syntheticLandscape.AlphaLayers.Single().UsesQuadrantDefault &&
        syntheticLandscape.Textures.Count == 1,
        "Native active-set LAND/LTEX/TXST transport differs.");
    ExpectFailure(
        () => FalloutLandscapeTransportResolver.Resolve(
            cellStack,
            syntheticDoor with
            {
                SourceDoor = syntheticDoor.SourceDoor with
                {
                    Teleport = syntheticDoor.SourceDoor.Teleport! with
                    {
                        Position = [5000.0f, 200.0f, 300.0f],
                    },
                },
            }),
        "must author one BTXT for each quadrant");
    ExpectFailure(
        () => FalloutDoorTransitionResolver.Resolve(
            cellStack,
            cell with
            {
                References = cell.References.Select(reference =>
                    reference.FormKey == syntheticDoor.SourceDoor.FormKey
                        ? reference with
                        {
                            Teleport = reference.Teleport! with { Flags = 1 },
                        }
                        : reference).ToArray(),
            },
            syntheticDoor.SourceDoor.FormKey),
        "is not an active persistent portal");
    var syntheticStart = FalloutNewGamePlayerStartResolver.Resolve(cellStack, cell);
    Require(
        syntheticStart.Reference.FormKey == new FalloutFormKey("Cell.esm", 0x122) &&
        syntheticStart.Reference.EditorId == "SyntheticPlayerStartREF" &&
        syntheticStart.Quest == new FalloutFormKey("Cell.esm", 0x140) &&
        syntheticStart.Stage == 0 &&
        syntheticStart.Candidates.Count == 2 &&
        syntheticStart.Candidates.Single(value =>
            value.Reference.FormKey == new FalloutFormKey("Cell.esm", 0x123))
            .DirectPackageLocationCount == 1,
        "Native New Game QUST/SCRO/REFR/PACK player-start selection differs.");
    var duplicateStart = syntheticStart.Reference with
    {
        FormKey = new FalloutFormKey("Cell.esm", 0x124),
    };
    ExpectFailure(
        () => FalloutNewGamePlayerStartResolver.Resolve(
            cellStack,
            cell with { References = cell.References.Append(duplicateStart).ToArray() }),
        "resolves to 2 CELL references");
    ExpectFailure(
        () => FalloutNewGamePlayerStartResolver.Resolve(
            cellStack,
            cell with
            {
                References = cell.References.Select(reference =>
                    reference.FormKey == syntheticStart.Reference.FormKey
                        ? reference with { EnableParent = new FalloutFormKey("Cell.esm", 0x125) }
                        : reference).ToArray(),
            }),
        "outside the evidenced active persistent reference contract");
    var syntheticLightBase = cell.BaseObjects[cell.References[1].Base];
    var syntheticLight = FalloutPlacedLightResolver.Resolve(cell.References[1], syntheticLightBase, cellStack);
    Require(syntheticLight.RadiusGameUnits == 160.0f,
        "Native LIGH base plus REFR XRDS radius adjustment differs.");
    Require(cell.References[1].Emittance == new FalloutFormKey("Cell.esm", 0x111) &&
        syntheticLight.ShaderColorRgb.Zip(new[] { 10000f / 65025, 6400f / 65025, 1600f / 65025 })
            .All(pair => MathF.Abs(pair.First - pair.Second) < 1e-7f) && syntheticLight.Intensity == 1.5f,
        "XEMI must modulate RGB without replacing radius or multiplying the emittance record's dimmer.");
    var untintedLight = FalloutPlacedLightResolver.Resolve(cell.References[1] with { Emittance = null }, syntheticLightBase);
    Require(untintedLight.ShaderColorRgb[0] > syntheticLight.ShaderColorRgb[0] && untintedLight.Intensity == syntheticLight.Intensity,
        "A reference without XEMI must retain its base color and intensity.");
    ExpectFailure(() => FalloutPlacedLightResolver.Resolve(cell.References[1], syntheticLightBase), "no XEMI source resolver");
    ExpectFailure(() => FalloutPlacedLightResolver.Resolve(cell.References[1] with { Emittance = new("Cell.esm", 0x110) },
        syntheticLightBase, cellStack), "STAT emittance owner");
    ExpectFailure(
        () => FalloutPlacedLightResolver.Resolve(
            cell.References[1], syntheticLightBase with
            {
                Light = syntheticLightBase.Light! with { Flags = 0x0000_0008 },
            }),
        "outside the evidenced static point-light contract");
    ExpectFailure(
        () => FalloutPlacedLightResolver.Resolve(
            cell.References[1] with
            {
                EnableParent = new FalloutFormKey("Cell.esm", 0x130),
            },
            syntheticLightBase),
        "unresolved enable parent");

    Require(stack.WinnerRecordCount == 9 && stack.EffectiveRecordCount == 8, "Winner/deletion counts differ.");
    Console.WriteLine($"OPENNV_FALLOUT_PLUGIN_RUNTIME_PROBE_PASS plugins={stack.Plugins.Count} effective={stack.EffectiveRecordCount} winner={stack.GetEffective(masterKey).Plugin.Name}");
}
finally
{
    Directory.Delete(fixtureRoot, recursive: true);
}

if (args.Length > 0)
{
    string[] officialNames = [
        "FalloutNV.esm",
        "DeadMoney.esm",
        "HonestHearts.esm",
        "OldWorldBlues.esm",
        "LonesomeRoad.esm",
        "TribalPack.esm",
        "MercenaryPack.esm",
        "ClassicPack.esm",
        "CaravanPack.esm",
        "GunRunnersArsenal.esm",
    ];
    var manifestMode = args[0] == "--source-stack";
    if (manifestMode && args.Length != 2)
        throw new ArgumentException("--source-stack requires exactly one manifest path.");
    var names = manifestMode
        ? []
        : args.Length > 1
            ? args[1..]
            : officialNames.All(name => File.Exists(Path.Combine(args[0], name)))
                ? officialNames
                : ["FalloutNV.esm"];
    using var owned = manifestMode
        ? FalloutPluginStack.Load(ReadManifestPluginSources(args[1]))
        : FalloutPluginStack.Load(args[0], names);
    var cells = owned.EffectiveRecords("CELL");
    if (names.Contains("GunRunnersArsenal.esm", StringComparer.OrdinalIgnoreCase))
    {
        var injectedReference = owned.GetEffective(
            new FalloutFormKey("HonestHearts.esm", 0x801));
        var injectedBaseRaw = BinaryPrimitives.ReadUInt32LittleEndian(
            injectedReference.ReadSubrecords().Single(row => row.Signature == "NAME").Data.Span);
        Require(
            injectedReference.Plugin.Name == "GunRunnersArsenal.esm" &&
            injectedReference.RawFormId == 0x02000801 &&
            injectedReference.Plugin.AdjustFormId(injectedBaseRaw) ==
                new FalloutFormKey("HonestHearts.esm", 0x800),
            "Official GRA injected reference namespace differs.");
        Console.WriteLine(
            "OPENNV_FALLOUT_PLUGIN_INJECTION_PASS " +
            $"winner={injectedReference.Plugin.Name} key={injectedReference.FormKey} " +
            $"base={injectedReference.Plugin.AdjustFormId(injectedBaseRaw)}");
    }
    var compressedRecords = 0;
    long subrecords = 0;
    foreach (var context in owned.Plugins)
    {
        foreach (var record in context.Plugin.Records)
        {
            if (record.IsCompressed)
                compressedRecords++;
            subrecords += record.ReadSubrecords().LongCount();
        }
    }
    Console.WriteLine($"OPENNV_FALLOUT_PLUGIN_OWNED_INPUT_PASS plugins={owned.Plugins.Count} cells={cells.Count} records={owned.EffectiveRecordCount} baseSha256={owned.Plugins[0].Sha256}");
    Console.WriteLine($"OPENNV_FALLOUT_PLUGIN_PAYLOAD_PASS compressed={compressedRecords} subrecords={subrecords}");
    if (owned.Plugins[0].Plugin.Name.Equals("FalloutNV.esm", StringComparison.OrdinalIgnoreCase))
    {
        var openingControls = FalloutOpeningPlayerControlResolver.Resolve(
            owned,
            ["VCG00", "VCG01"]);
        var liveRaceSex = FalloutNativeRaceSexResolver.Resolve(owned);
        var liveOpeningCell = FalloutCellSceneReader.Read(
            owned,
            new FalloutFormKey("FalloutNV.esm", 0x103df9));
        var liveVigor = FalloutNativeVigorResolver.Resolve(owned, liveOpeningCell);
        var liveSpecial = liveVigor.Initial.WithValue(0, 10);
        FalloutNativeVigorResolver.Validate(liveVigor, liveSpecial);
        var liveTagSkills = FalloutNativeTagSkillResolver.Resolve(owned, openingControls);
        var liveTags = liveTagSkills.Skills.Take(liveTagSkills.RequiredCount).ToArray();
        FalloutNativeTagSkillResolver.Validate(liveTagSkills, liveTags);
        var liveTraitFarewell = FalloutNativeTraitFarewellResolver.Resolve(
            owned,
            openingControls,
            liveOpeningCell);
        var liveTraits = liveTraitFarewell.Traits.Take(2).ToArray();
        FalloutNativeTraitFarewellResolver.ValidateTraits(liveTraitFarewell, liveTraits);
        Require(
            liveRaceSex.Male.RaceRuntimeFormId == liveRaceSex.Female.RaceRuntimeFormId &&
            liveRaceSex.Male.EyesRuntimeFormId == liveRaceSex.Female.EyesRuntimeFormId,
            "Owned Player/RACE identity contract is inconsistent.");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_RACESEX_PASS " +
            $"player={liveRaceSex.Player} race={liveRaceSex.Male.RaceEditorId}/" +
            $"{liveRaceSex.Male.RaceRuntimeFormId:x8} " +
            $"maleHair={liveRaceSex.Male.HairEditorId}/{liveRaceSex.Male.HairRuntimeFormId:x8} " +
            $"femaleHair={liveRaceSex.Female.HairEditorId}/{liveRaceSex.Female.HairRuntimeFormId:x8} " +
            $"eyes={liveRaceSex.Male.EyesEditorId}/{liveRaceSex.Male.EyesRuntimeFormId:x8} " +
            "source=winning-npc-race-hair-eyes writes=0");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_VIGOR_PASS " +
            $"trigger={liveVigor.TriggerReference.FormKey} stage={liveVigor.TriggerFromStage}->" +
            $"{liveVigor.TesterStage} tester={liveVigor.TesterReference.FormKey} " +
            $"complete={liveVigor.CompletedStage} total={liveVigor.RequiredTotal} " +
            $"initial={string.Join(',', liveVigor.Initial.Values)} " +
            "source=winning-player-refr-acti-scpt-xprm writes=0");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_TAG_SKILLS_PASS " +
            $"choices={liveTagSkills.Skills.Count} required={liveTagSkills.RequiredCount} " +
            $"skills={string.Join(',', liveTagSkills.Skills.Select(value =>
                $"{value.RuntimeFormId:x8}/{value.EditorId}/{value.DisplayName}"))} " +
            "source=winning-qust-info-avif writes=0");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_TRAIT_FAREWELL_PASS " +
            $"traits={liveTraitFarewell.Traits.Count} maximum={liveTraitFarewell.MaximumTraits} " +
            $"selected={string.Join(',', liveTraits.Select(value => value.EditorId))} " +
            $"trigger={liveTraitFarewell.ExitTriggerReference.FormKey} " +
            $"stage={liveTraitFarewell.TraitMenuStage}->" +
            $"{liveTraitFarewell.ExitTriggerFromStage}->" +
            $"{liveTraitFarewell.FarewellStage}->{liveTraitFarewell.CompletedStage} " +
            $"delay={liveTraitFarewell.CompletionDelaySeconds:R} " +
            "source=winning-perk-refr-acti-scpt-info-qust writes=0");
        var stage200Grant = FalloutOpeningInventoryGrantResolver.Resolve(
            owned,
            openingControls,
            "VCG01");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_CAMPAIGN_INVENTORY_PASS " +
            $"items={stage200Grant.Inventory.Items.Count} " +
            $"equipped={stage200Grant.EquippedRuntimeFormIds.Count} " +
            $"resolved={string.Join(',', stage200Grant.Inventory.Items.Select(item =>
                $"{item.RuntimeFormId:x8}/{item.EditorId}/{item.RecordType}/{item.Value}/{item.Weight:R}"))} " +
            "source=winning-qust-info-sctx-records writes=0");
        var liveCompletedGrant = FalloutNativeTraitFarewellResolver.ResolveGrant(
            liveTraitFarewell,
            stage200Grant,
            liveTags);
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_FAREWELL_GRANT_PASS " +
            $"items={liveCompletedGrant.Inventory.Items.Count} " +
            $"resolved={string.Join(',', liveCompletedGrant.Inventory.Items.Select(item =>
                $"{item.RuntimeFormId:x8}/{item.EditorId}/{item.RecordType}/{item.Count}"))} " +
            "source=winning-info-tag-branches writes=0");
        var liveControlState = FalloutPlayerControlState.AllEnabled;
        foreach (var stage in new[]
                 {
                     openingControls.Stage("VCG00", 0),
                     openingControls.Stage("VCG00", 100),
                     openingControls.Stage("VCG01", 0),
                 })
            liveControlState = stage.Commands.Aggregate(
                liveControlState,
                (state, command) => command.Apply(state));
        Require(
            !liveControlState.Movement && !liveControlState.PipBoy &&
            !liveControlState.Fighting && liveControlState.Looking,
            "Owned VCG00-to-VCG01 initial player controls differ.");
        liveControlState = openingControls.Stage("VCG01", 55).Commands.Aggregate(
            liveControlState,
            (state, command) => command.Apply(state));
        Require(
            liveControlState.Movement && !liveControlState.PipBoy &&
            !liveControlState.Fighting && liveControlState.PointOfView &&
            liveControlState.Looking && liveControlState.RolloverText &&
            !liveControlState.Sneaking,
            "Owned VCG01 stage-55 player controls differ.");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_OPENING_CONTROLS_PASS " +
            $"quests={openingControls.Quests.Count} stage=VCG01:55 " +
            $"movement={liveControlState.Movement} pipBoy={liveControlState.PipBoy} " +
            $"fighting={liveControlState.Fighting} looking={liveControlState.Looking} " +
            "source=winning-qust-sctx writes=0");
        var openingTransitions = FalloutOpeningStageTransitionResolver.Resolve(
            owned,
            openingControls);
        openingTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
            owned,
            openingControls,
            openingTransitions,
            "VCG01",
            [3, 8, 15, 25, 35, 40, 50]);
        openingTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
            owned,
            openingControls,
            openingTransitions,
            "VCG01",
            [70]);
        openingTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
            owned,
            openingControls,
            openingTransitions,
            "VCG01",
            [79]);
        openingTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
            owned,
            openingControls,
            openingTransitions,
            "VCG01",
            [95]);
        openingTransitions = FalloutOpeningStageTransitionResolver.AddDialogueResults(
            owned,
            openingControls,
            openingTransitions,
            "VCG01",
            [105]);
        var liveIntroEdge = openingTransitions.From("VCG00", 0)
            .Single(value => value.Kind == "stage-script");
        Require(
            liveIntroEdge.ToQuestEditorId == "VCG00" && liveIntroEdge.ToStage == 90 &&
            liveIntroEdge.Blockers.SequenceEqual(["playbink"]) &&
            openingTransitions.From("VCG00", 90).Single(value => value.Kind == "timer") is
            {
                ToStage: 95,
                DelaySeconds: 3.0f,
            } &&
            openingTransitions.From("VCG00", 95).Single(value => value.Kind == "timer")
                .ToStage == 100 &&
            openingTransitions.From("VCG00", 100).Single(value => value.Kind == "stage-script") is
            {
                ToQuestEditorId: "VCG01",
                ToStage: 0,
            } &&
            openingTransitions.From("VCG01", 0).Single(value => value.Kind == "timer") is
            {
                ToStage: 1,
                DelaySeconds: 0.2f,
            },
            "Owned opening stage/timer transition graph differs.");
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_OPENING_TRANSITIONS_PASS " +
            $"edges={openingTransitions.Transitions.Count} " +
            $"entry={liveIntroEdge.FromQuestEditorId}:{liveIntroEdge.FromStage}->" +
            $"{liveIntroEdge.ToQuestEditorId}:{liveIntroEdge.ToStage} " +
            $"blockers={string.Join(',', liveIntroEdge.Blockers)} " +
            "source=winning-qust-scpt-dial-info writes=0");
        var liveStageMachine = new FalloutOpeningStageMachine(
            openingTransitions,
            openingControls,
            "VCG00",
            0);
        Require(
            liveStageMachine.PendingBlockers.SequenceEqual(["playbink"]) &&
            liveStageMachine.CompleteBlocker("playbink") &&
            liveStageMachine.Stage == 90 && liveStageMachine.TimerSeconds == 3.0f &&
            liveStageMachine.AdvanceTime(3.0f) &&
            liveStageMachine.QuestEditorId == "VCG01" && liveStageMachine.Stage == 0 &&
            liveStageMachine.TimerSeconds == 0.2f &&
            liveStageMachine.AdvanceTime(0.2f) &&
            liveStageMachine.Stage == 1 && liveStageMachine.TimerSeconds == 2.8f &&
            liveStageMachine.AdvanceTime(2.8f) &&
            liveStageMachine.Stage == 3 && liveStageMachine.TimerSeconds is null &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            !liveStageMachine.ControlState.Movement &&
            liveStageMachine.ControlState.Looking,
            "Owned opening stage machine did not reach the first dialogue-dependent stage.");
        Require(
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 5 && liveStageMachine.TimerSeconds == 3.25f &&
            liveStageMachine.AdvanceTime(3.25f) &&
            liveStageMachine.Stage == 8 && liveStageMachine.TimerSeconds is null &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 10 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["getplayername"]) &&
            liveStageMachine.CompleteBlocker("getplayername") &&
            liveStageMachine.TimerSeconds == 1.0f &&
            liveStageMachine.AdvanceTime(1.0f) &&
            liveStageMachine.Stage == 15 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 25 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 30 && liveStageMachine.TimerSeconds == 3.0f &&
            liveStageMachine.AdvanceTime(3.0f) &&
            liveStageMachine.Stage == 35 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 36 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["showracemenu"]) &&
            liveStageMachine.CompleteBlocker("showracemenu") &&
            liveStageMachine.Stage == 40 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 45 && liveStageMachine.TimerSeconds == 3.0f &&
            liveStageMachine.AdvanceTime(3.0f) &&
            liveStageMachine.Stage == 50 &&
            liveStageMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveStageMachine.CompleteBlocker("sayto") &&
            liveStageMachine.Stage == 55 && liveStageMachine.TimerSeconds is null &&
            liveStageMachine.PendingBlockers.Count == 0 &&
            liveStageMachine.ControlState.Movement &&
            liveStageMachine.ControlState.Looking,
            "Owned opening stage machine did not reach source-controlled free movement.");
        var liveVigorMachine = new FalloutOpeningStageMachine(
            openingTransitions,
            openingControls,
            "VCG01",
            liveVigor.TriggerFromStage);
        liveVigorMachine.EnterSourceStage("VCG01", liveVigor.TesterStage);
        liveVigorMachine.EnterSourceStage("VCG01", liveVigor.CompletedStage);
        Require(
            liveVigorMachine.Stage == 65 && liveVigorMachine.TimerSeconds == 1.0f &&
            liveVigorMachine.AdvanceTime(1.0f) && liveVigorMachine.Stage == 70 &&
            liveVigorMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveVigorMachine.CompleteBlocker("sayto") && liveVigorMachine.Stage == 79 &&
            liveVigorMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveVigorMachine.CompleteBlocker("sayto") && liveVigorMachine.Stage == 80,
            "Owned native Vigor/psych dialogue stages did not reach stage 80.");
        liveVigorMachine.EnterSourceStage("VCG01", liveTagSkills.PsychCompletedStage);
        Require(
            liveVigorMachine.AdvanceTime(1.0f) &&
            liveVigorMachine.Stage == liveTagSkills.TagMenuStage &&
            liveVigorMachine.PendingBlockers.SequenceEqual(["settagskills"]) &&
            liveVigorMachine.CompleteBlocker("settagskills") &&
            liveVigorMachine.AdvanceTime(1.0f) && liveVigorMachine.Stage == 95,
            "Owned native psych/tag-skill stages did not reach stage 95.");
        Require(
            liveVigorMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveVigorMachine.CompleteBlocker("sayto") &&
            liveVigorMachine.Stage == 98 &&
            liveVigorMachine.AdvanceTime(1.0f) &&
            liveVigorMachine.Stage == liveTraitFarewell.TraitMenuStage &&
            liveVigorMachine.PendingBlockers.SequenceEqual(["showtraitmenu"]) &&
            liveVigorMachine.CompleteBlocker("showtraitmenu") &&
            liveVigorMachine.AdvanceTime(1.0f) &&
            liveVigorMachine.Stage == 105 &&
            liveVigorMachine.PendingBlockers.SequenceEqual(["sayto"]) &&
            liveVigorMachine.CompleteBlocker("sayto") &&
            liveVigorMachine.Stage == liveTraitFarewell.ExitTriggerFromStage,
            "Owned native trait/farewell stages did not reach the Doc exit trigger.");
        liveVigorMachine.EnterSourceStage("VCG01", liveTraitFarewell.FarewellStage);
        liveVigorMachine.EnterSourceStage("VCG01", liveTraitFarewell.CompletedStage);
        Console.WriteLine(
            "OPENNV_FALLOUT_LIVE_OPENING_STAGE_MACHINE_PASS " +
            $"stage={liveStageMachine.QuestEditorId}:{liveStageMachine.Stage} " +
            $"movement={liveStageMachine.ControlState.Movement} " +
            $"looking={liveStageMachine.ControlState.Looking} " +
            "source=winning-qust-scpt-dial-info writes=0");
        var completedControls = openingControls.Stage("VCG01", 110).Commands.Aggregate(
            liveStageMachine.ControlState,
            (state, command) => command.Apply(state));
        var liveSaveCompatibilityId = $"standalone-live:{owned.Plugins[0].Sha256}";
        var liveSavePath = Path.Combine(
            Path.GetTempPath(),
            $"opennv-native-campaign-{Guid.NewGuid():N}.json");
        try
        {
            var liveSave = FalloutNativeCampaignSave.Capture(
                liveSaveCompatibilityId,
                new FalloutFormKey("FalloutNV.esm", 0x103df9),
                liveCompletedGrant,
                "Live Courier",
                liveRaceSex.Female,
                liveVigor,
                liveSpecial,
                liveTagSkills,
                liveTags,
                liveTraitFarewell,
                liveTraits,
                completedControls,
                [12.5f, 2.0f, -8.25f],
                [0.0f, 0.0f, 0.0f, 1.0f]);
            FalloutNativeCampaignSave.Write(liveSavePath, liveSave);
            var liveRestore = FalloutNativeCampaignSave.Read(
                liveSavePath,
                liveSaveCompatibilityId,
                owned,
                liveVigor,
                liveTagSkills,
                stage200Grant,
                liveTraitFarewell);
            Require(
                liveRestore.State.Stage == FalloutNativeCampaignSave.CompletedOpeningStage &&
                liveRestore.State.PlayerName == "Live Courier" &&
                liveRestore.State.Character == liveRaceSex.Female &&
                liveRestore.State.Special == liveSpecial &&
                liveRestore.State.TagSkills.SequenceEqual(liveTags) &&
                liveRestore.State.Traits.SequenceEqual(liveTraits) &&
                liveRestore.Inventory.Items.Count == liveCompletedGrant.Inventory.Items.Count &&
                FalloutNativeCampaignSave.RestorePlayerControls(liveRestore.State) ==
                    completedControls,
                "Owned native campaign save did not cold-restore against live records.");
        }
        finally
        {
            if (File.Exists(liveSavePath))
                File.Delete(liveSavePath);
        }
        Require(!File.Exists(liveSavePath), "Owned native campaign save cleanup failed.");
        Console.WriteLine(
            $"OPENNV_FALLOUT_LIVE_COLD_RELOAD_PASS stage=VCG01:200 " +
            $"items={liveCompletedGrant.Inventory.Items.Count} " +
            "character=restored special=restored tags=restored traits=restored " +
            "transform=restored controls=restored " +
            "source=winning-records " +
            "cache=none writes=save-only cleanup=complete");
    }
    var docMitchell = FalloutCellSceneReader.Read(
        owned,
        new FalloutFormKey("FalloutNV.esm", 0x103df9));
    var playerMarkers = docMitchell.References.Where(reference =>
        docMitchell.BaseObjects[reference.Base].EditorId.Equals(
            "XMarkerHeading", StringComparison.OrdinalIgnoreCase)).ToArray();
    Console.WriteLine(
        $"OPENNV_FALLOUT_CELL_RUNTIME_PASS cell={docMitchell.Cell.FormKey} " +
        $"editorId={docMitchell.Cell.EditorId} references={docMitchell.References.Count} " +
        $"models={docMitchell.BaseObjects.Values.Count(value => value.ModelPath is not null)} " +
        $"playerMarkers={playerMarkers.Length}");
    var ownedDoorTransition = FalloutDoorTransitionResolver.ResolveInteriorExits(
        owned, docMitchell).Single();
    Require(
        ownedDoorTransition.SourceDoor.FormKey == new FalloutFormKey("FalloutNV.esm", 0x103e61) &&
        ownedDoorTransition.DestinationDoor.FormKey == new FalloutFormKey("FalloutNV.esm", 0x103e69) &&
        ownedDoorTransition.DestinationScene.Cell.FormKey ==
            new FalloutFormKey("FalloutNV.esm", 0x0846ea) &&
        ownedDoorTransition.DestinationWorldspace == new FalloutFormKey("FalloutNV.esm", 0x0da726),
        "Owned Doc-house XTEL/CELL/WRLD transition differs.");
    Console.WriteLine(
        $"OPENNV_FALLOUT_DOOR_TRANSITION_PASS source={ownedDoorTransition.SourceDoor.FormKey} " +
        $"destination={ownedDoorTransition.DestinationDoor.FormKey} " +
        $"cell={ownedDoorTransition.DestinationScene.Cell.FormKey} " +
        $"coordinates={ownedDoorTransition.DestinationScene.Cell.Coordinates} " +
        $"world={ownedDoorTransition.DestinationWorldspace} " +
        $"worldEditorId={ownedDoorTransition.DestinationWorldspaceEditorId}");
    const float exteriorCellSide = 4096.0f;
    var entryPosition = ownedDoorTransition.SourceDoor.Teleport!.Position;
    var activeCoordinates = (
        X: (int)MathF.Floor(entryPosition[0] / exteriorCellSide),
        Y: (int)MathF.Floor(entryPosition[1] / exteriorCellSide));
    var activeCells = owned.EffectiveRecords("CELL").Where(record =>
    {
        var worldGroup = record.Groups.LastOrDefault(value => value.Type == 1);
        if (worldGroup.Type != 1 ||
            record.Plugin.AdjustFormId(worldGroup.LabelAsUInt32) !=
            ownedDoorTransition.DestinationWorldspace)
            return false;
        var xclc = record.ReadSubrecords().SingleOrDefault(value => value.Signature == "XCLC");
        return xclc.Data.Length >= sizeof(int) * 2 &&
            BinaryPrimitives.ReadInt32LittleEndian(xclc.Data.Span) == activeCoordinates.X &&
            BinaryPrimitives.ReadInt32LittleEndian(xclc.Data.Span[sizeof(int)..]) == activeCoordinates.Y;
    }).ToArray();
    Require(activeCells.Length == 1, "Owned XTEL active exterior grid CELL is ambiguous.");
    var activeCell = activeCells[0];
    var activeLand = owned.EffectiveRecords("LAND").Where(record =>
        FalloutCellSceneReader.ParentCell(record) == activeCell.FormKey).ToArray();
    Console.WriteLine(
        $"OPENNV_FALLOUT_LAND_ACTIVE_CELL coordinates={activeCoordinates} cell={activeCell.FormKey} " +
        $"land={string.Join(',', activeLand.Select(value => value.FormKey))} " +
        $"subrecords={string.Join(',', activeLand.SelectMany(value => value.ReadSubrecords())
            .GroupBy(value => value.Signature).Select(value => $"{value.Key}:{value.Count()}"))}");
    var ownedLandscape = FalloutLandscapeTransportResolver.Resolve(owned, ownedDoorTransition);
    Require(
        ownedLandscape.ActiveCoordinates == (-18, 0) &&
        ownedLandscape.ActiveCell == new FalloutFormKey("FalloutNV.esm", 0x0daebb) &&
        ownedLandscape.Landscape == new FalloutFormKey("FalloutNV.esm", 0x0db00e) &&
        ownedLandscape.BaseLayers.Count == 4 &&
        ownedLandscape.AlphaLayers.Count == 23 &&
        ownedLandscape.Heights.Length == 33 * 33,
        "Owned XTEL active-set LAND transport differs.");
    Console.WriteLine(
        $"OPENNV_FALLOUT_LAND_TRANSPORT_PASS activeCell={ownedLandscape.ActiveCell} " +
        $"coordinates={ownedLandscape.ActiveCoordinates} land={ownedLandscape.Landscape} " +
        $"flags=0x{ownedLandscape.Flags:x8} vertices={ownedLandscape.Heights.Length} " +
        $"baseLayers={ownedLandscape.BaseLayers.Count} alphaLayers={ownedLandscape.AlphaLayers.Count} " +
        $"textures={ownedLandscape.Textures.Count} defaults=" +
        $"{ownedLandscape.AlphaLayers.Count(value => value.UsesQuadrantDefault)} " +
        $"dds={string.Join(',', ownedLandscape.Textures.Values.Select(value => value.DiffusePath))}");
    foreach (var marker in playerMarkers)
    {
        var record = owned.GetEffective(marker.FormKey);
        Console.WriteLine(
            $"OPENNV_FALLOUT_PLAYER_MARKER form={marker.FormKey} editorId={ReadOptionalEditorId(record)} " +
            $"flags=0x{marker.Flags:x8} " +
            $"position={string.Join('/', marker.Position.Select(value => value.ToString("R")))} " +
            $"rotation={string.Join('/', marker.RotationRadians.Select(value => value.ToString("R")))} " +
            $"subrecords={string.Join(',', record.ReadSubrecords().Select(value => value.Signature))}");
        var runtimeFormId = owned.RuntimeFormId(marker.FormKey);
        foreach (var owner in owned.EffectiveRecords().Where(candidate =>
                     candidate.Signature is "QUST" or "PACK"))
        {
            foreach (var subrecord in owner.ReadSubrecords())
            {
                var offsets = FindUInt32(subrecord.Data.Span, runtimeFormId).ToArray();
                if (offsets.Length > 0)
                    Console.WriteLine(
                        $"OPENNV_FALLOUT_PLAYER_MARKER_LINK marker={marker.FormKey} " +
                        $"owner={owner.Signature}/{owner.FormKey} editorId={ReadOptionalEditorId(owner)} " +
                        $"subrecord={subrecord.Signature} " +
                        $"offsets={string.Join(',', offsets)} bytes={Convert.ToHexString(subrecord.Data.Span)}");
            }
        }
    }
    var ownedStart = FalloutNewGamePlayerStartResolver.Resolve(owned, docMitchell);
    Require(
        ownedStart.Reference.FormKey == new FalloutFormKey("FalloutNV.esm", 0x103e6b) &&
        ownedStart.Reference.EditorId == "VCG01PlayerStartMarkerREF" &&
        ownedStart.Quest == new FalloutFormKey("FalloutNV.esm", 0x102037) &&
        ownedStart.Stage == 0 &&
        ownedStart.Candidates.Count == 7 &&
        ownedStart.Candidates.Count(value => value.DirectPackageLocationCount > 0) == 5 &&
        ownedStart.Candidates.Sum(value => value.DirectPackageLocationCount) == 6,
        "Owned New Game QUST/SCRO/REFR/PACK player-start selection differs.");
    Console.WriteLine(
        $"OPENNV_FALLOUT_NEW_GAME_PLAYER_START_PASS reference={ownedStart.Reference.FormKey} " +
        $"editorId={ownedStart.Reference.EditorId} quest={ownedStart.Quest} stage={ownedStart.Stage} " +
        $"candidates={ownedStart.Candidates.Count} " +
        $"packageLinked={ownedStart.Candidates.Count(value => value.DirectPackageLocationCount > 0)} " +
        $"packageTargets={ownedStart.Candidates.Sum(value => value.DirectPackageLocationCount)}");
    var ownedLights = docMitchell.References
        .Select(reference => (Reference: reference, Base: docMitchell.BaseObjects[reference.Base]))
        .Where(value => value.Base.Light is not null)
        .ToArray();
    var resolvedLights = ownedLights
        .Select(value => FalloutPlacedLightResolver.Resolve(value.Reference, value.Base, owned))
        .ToArray();
    Console.WriteLine(
        $"OPENNV_FALLOUT_CELL_LIGHT_AUDIT lights={ownedLights.Length} enabled=" +
        $"{ownedLights.Count(value => !FalloutCellSceneReader.IsInitiallyDisabled(value.Reference))} " +
        $"enableParents={ownedLights.Count(value => value.Reference.EnableParent is not null)} " +
        "flags=" + string.Join(',', ownedLights.GroupBy(value => value.Base.Light!.Flags)
            .OrderBy(group => group.Key).Select(group => $"0x{group.Key:x8}:{group.Count()}")) + " " +
        "durations=" + string.Join(',', ownedLights.GroupBy(value => value.Base.Light!.Duration)
            .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}")) + " " +
        "periods=" + string.Join(',', ownedLights.GroupBy(value => value.Base.Light!.Period)
            .OrderBy(group => group.Key).Select(group => $"{group.Key:R}:{group.Count()}")) + " " +
        "contracts=" + string.Join(',', ownedLights.GroupBy(value => (
                value.Base.Light!.Falloff, value.Base.Light.FieldOfViewDegrees,
                value.Base.Light.NearClip, value.Base.Light.Intensity,
                value.Base.Light.ColorAlpha))
            .Select(group => $"{group.Key.Falloff:R}/{group.Key.FieldOfViewDegrees:R}/" +
                $"{group.Key.NearClip}/{group.Key.Intensity:R}/{group.Key.ColorAlpha}:{group.Count()}")) + " " +
        $"resolvedRadius={resolvedLights.Min(value => value.RadiusGameUnits):R}.." +
        $"{resolvedLights.Max(value => value.RadiusGameUnits):R} " +
        "radii=" + string.Join(',', ownedLights.GroupBy(value => (
                Base: value.Base.Light!.RadiusGameUnits,
                Adjustment: value.Reference.RadiusAdjustmentGameUnits))
            .OrderBy(group => group.Key.Base).ThenBy(group => group.Key.Adjustment)
            .Select(group => $"{group.Key.Base}/{group.Key.Adjustment:R}:{group.Count()}")));

    var saloonCellRecord = cells.Single(record =>
        ReadOptionalEditorId(record) == "GSProspectorSaloonInterior");
    var saloon = FalloutCellSceneReader.Read(owned, saloonCellRecord.FormKey);
    var saloonDoorTransitions = FalloutDoorTransitionResolver.ResolveInteriorExits(owned, saloon);
    Require(
        saloon.Cell.EditorId == "GSProspectorSaloonInterior" &&
        saloonDoorTransitions.Count == 4 &&
        saloonDoorTransitions.All(transition =>
            transition.DestinationWorldspace == ownedDoorTransition.DestinationWorldspace),
        "Owned Prospector Saloon CELL/XTEL identity differs.");
    ReportLiveCellCoverage("doc-interior", docMitchell);
    ReportLiveCellCoverage("doc-exterior", ownedDoorTransition.DestinationScene);
    ReportLiveCellCoverage("saloon-interior", saloon);
    foreach (var exterior in saloonDoorTransitions
                 .Select(transition => transition.DestinationScene)
                 .DistinctBy(scene => scene.Cell.FormKey))
        ReportLiveCellCoverage("saloon-exterior", exterior);
}

static void ReportLiveCellCoverage(string role, FalloutCellScene scene)
{
    var enabled = scene.References
        .Where(reference => !FalloutCellSceneReader.IsInitiallyDisabled(reference))
        .ToArray();
    var actors = enabled.Where(reference =>
        scene.BaseObjects[reference.Base].Signature is "NPC_" or "CREA").ToArray();
    var modelReferences = enabled.Count(reference =>
        scene.BaseObjects[reference.Base].ModelPath is not null);
    var lights = enabled.Count(reference =>
        scene.BaseObjects[reference.Base].Light is not null);
    var unresolvedPresentation = enabled.Where(reference =>
        scene.BaseObjects[reference.Base].ModelPath is null &&
        scene.BaseObjects[reference.Base].Light is null).ToArray();
    var signatures = scene.References
        .GroupBy(reference => scene.BaseObjects[reference.Base].Signature)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => $"{group.Key}:{group.Count()}");
    var actorRows = actors.Select(reference =>
    {
        var actor = scene.BaseObjects[reference.Base];
        return $"{reference.FormKey}/{reference.EditorId}/{actor.FormKey}/{actor.EditorId}";
    }).Order(StringComparer.Ordinal).ToArray();
    var actorIdentitySha256 = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', actorRows)))).ToLowerInvariant();
    Console.WriteLine(
        $"OPENNV_FALLOUT_LIVE_CELL_COVERAGE role={role} cell={scene.Cell.FormKey} " +
        $"editorId={scene.Cell.EditorId} references={scene.References.Count} enabled={enabled.Length} " +
        $"models={modelReferences} lights={lights} actors={actors.Length} " +
        $"unresolvedPresentation={unresolvedPresentation.Length} " +
        $"signatures={string.Join(',', signatures)} actorIdentitySha256={actorIdentitySha256}");
}

static string ReadEditorId(FalloutPluginRecord record)
{
    var data = record.ReadSubrecords().Single(subrecord => subrecord.Signature == "EDID").Data.Span;
    var end = data.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end >= 0 ? data[..end] : data);
}

static IReadOnlyList<FalloutPluginSource> ReadManifestPluginSources(string manifestPath)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(manifestPath)));
    var root = document.RootElement;
    var roots = root.GetProperty("roots").EnumerateArray().ToDictionary(
        value => value.GetProperty("id").GetString()!,
        value => Path.GetFullPath(value.GetProperty("root").GetString()!),
        StringComparer.Ordinal);
    return root.GetProperty("plugins").EnumerateArray().Select(value =>
    {
        var name = value.GetProperty("file").GetString()!;
        return new FalloutPluginSource(
            name,
            Path.Combine(roots[value.GetProperty("rootId").GetString()!], name),
            value.GetProperty("bytes").GetInt64(),
            value.GetProperty("mtimeMs").GetInt64());
    }).ToArray();
}

static string ReadOptionalEditorId(FalloutPluginRecord record)
{
    var editor = record.ReadSubrecords().FirstOrDefault(value => value.Signature == "EDID");
    if (editor.Data.IsEmpty)
        return "<none>";
    var data = editor.Data.Span;
    var end = data.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end >= 0 ? data[..end] : data);
}

static IReadOnlyList<int> FindUInt32(ReadOnlySpan<byte> data, uint value)
{
    var result = new List<int>();
    for (var offset = 0; offset <= data.Length - sizeof(uint); offset++)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) == value)
            result.Add(offset);
    }
    return result;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ExpectFailure(Action action, string messageFragment)
{
    try
    {
        action();
        throw new InvalidOperationException($"Expected failure containing '{messageFragment}' was not raised.");
    }
    catch (Exception error) when (error.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
    {
    }
}

static byte[] Record(string signature, uint formId, uint flags, byte[] data)
{
    var header = new byte[24];
    Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)data.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), formId);
    return Combine(header, data);
}

static byte[] Teleport(uint destination, float[] position, float[] rotation)
{
    var data = new byte[32];
    BinaryPrimitives.WriteUInt32LittleEndian(data, destination);
    for (var index = 0; index < 3; ++index)
    {
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(sizeof(uint) + index * sizeof(float)), position[index]);
        BinaryPrimitives.WriteSingleLittleEndian(
            data.AsSpan(sizeof(uint) + (index + 3) * sizeof(float)), rotation[index]);
    }
    return data;
}

static byte[] LayerHeader(uint texture, byte quadrant, ushort layer)
{
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(data, texture);
    data[sizeof(uint)] = quadrant;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sizeof(uint) + 2), layer);
    return data;
}

static byte[] Group(string label, int type, byte[] data)
{
    var header = new byte[24];
    Encoding.ASCII.GetBytes("GRUP").CopyTo(header, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)(header.Length + data.Length)));
    Encoding.ASCII.GetBytes(label).CopyTo(header, 8);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), type);
    return Combine(header, data);
}

static byte[] GroupFormId(uint label, int type, byte[] data)
{
    var header = new byte[24];
    Encoding.ASCII.GetBytes("GRUP").CopyTo(header, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)(header.Length + data.Length)));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), label);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), type);
    return Combine(header, data);
}

static byte[] CompressedRecord(string signature, uint formId, byte[] data, bool corruptChecksum)
{
    using var destination = new MemoryStream();
    using (var compressor = new ZLibStream(destination, CompressionLevel.SmallestSize, leaveOpen: true))
        compressor.Write(data);
    var payload = destination.ToArray();
    if (corruptChecksum)
        payload[^1] ^= 0xff;
    return Record(signature, formId, compressedFlag, Combine(UInt32(checked((uint)data.Length)), payload));
}

static byte[] ExtendedSubrecord(string signature, byte[] data) => Combine(
    Subrecord("XXXX", UInt32(checked((uint)data.Length))),
    Subrecord(signature, data, 0));

static byte[] Subrecord(string signature, byte[] data, ushort? declaredSize = null) =>
    BinarySubrecord(Encoding.ASCII.GetBytes(signature), data, declaredSize);

static byte[] BinarySubrecord(byte[] signature, byte[] data, ushort? declaredSize = null)
{
    var header = new byte[6];
    signature.CopyTo(header, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), declaredSize ?? checked((ushort)data.Length));
    return Combine(header, data);
}

static byte[] ZString(string value) => [.. Encoding.ASCII.GetBytes(value), 0];

static byte[] UInt32(uint value)
{
    var data = new byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(data, value);
    return data;
}

static byte[] Combine(params byte[][] values)
{
    var result = new byte[values.Sum(value => value.Length)];
    var offset = 0;
    foreach (var value in values)
    {
        value.CopyTo(result, offset);
        offset += value.Length;
    }
    return result;
}
