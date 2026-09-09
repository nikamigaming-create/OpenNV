using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

var directory = Path.Combine(Path.GetTempPath(), "opennv-reference-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header()
        .Concat(Script(1)).Concat(Record("SCPT", 0x501, Local(1, "shared")))
        .Concat(Record("SCPT", 0x502, Local(1, "count"), Local(1, "conflictingName")))
        .Concat(Record("SCPT", 0x504, Local(7, "counter", 1, 0xA5), Local(7, "counter", 1, 0x5A)))
        .Concat(Record("SCPT", 0x505, Local(7, "counter", 1), Local(7, "counter", 0)))
        .Concat(Record("SCPT", 0x506, Local(7, "counter"), Local(8, "counter")))
        .Concat(Record("SCPT", 0x503, Field("SCTX", Text("begin OnActivate\nend"))))
        .Concat(Record("QUST", 0x600, Field("EDID", Text("TestQuest")), Field("SCRI", BitConverter.GetBytes(0x501u))))
        .Concat(Record("ACTI", 0x700, Field("EDID", Text("ModelLessActivator")), Field("SCRI", BitConverter.GetBytes(0x500u))))
        .Concat(Record("ACTI", 0x701, Field("SCRI", BitConverter.GetBytes(0x503u))))
        .Concat(Record("DOOR", 0x702))
        .Concat(Cell(0x800, Reference(0x900, "FirstREF"), Reference(0x901, "SecondREF")))
        .Concat(Cell(0x801, Reference(0x902, "PeerREF")))
        .Concat(Cell(0x802, Reference(0x903, "EmptyActivationREF", 0x701), Reference(0x904, "PlainDoorREF", 0x702),
            EnableChild(0x905, 0x904, 1), EnableChild(0x906, 0x905, 2), EnableChild(0x907, 0x908, 0), EnableChild(0x908, 0x907, 0))).ToArray());
    File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Header("Base.esm").Concat(Script(2)).ToArray());
    using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
    CellReviewContracts.Run(records);
    Reject(() => FalloutScriptLocals.Read(records.GetEffective(Key(0x502))));
    var paddedLocals = FalloutScriptLocals.Read(records.GetEffective(Key(0x504)));
    Require(paddedLocals.Count == 1 && paddedLocals["COUNTER"] == 7, "Unused compiler padding changed local slot identity.");
    Reject(() => FalloutScriptLocals.Read(records.GetEffective(Key(0x505))));
    Reject(() => FalloutScriptLocals.Read(records.GetEffective(Key(0x506))));
    var firstCell = FalloutCellSceneReader.Read(records, Key(0x800));
    var secondCell = FalloutCellSceneReader.Read(records, Key(0x801));
    using var world = new FalloutReferenceWorld(records);
    var first = world.LoadCell(firstCell);
    var peer = world.LoadCell(secondCell).Single();
    Require(world.InstanceCount == 3 && firstCell.BaseObjects.Values.All(value => value.ModelPath is null), "Model-less reference lifetime failed.");
    Require(ReferenceEquals(first[0].Script, first[1].Script) && ReferenceEquals(first[0].Script, peer.Script), "Script definitions are not reused.");
    var quests = new FalloutQuestState(records);
    var effects = new List<FalloutReferenceScriptEffect>();
    var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
    {
        effects.Add(effect);
        if (effect.Kind == FalloutReferenceEffectKind.SetStage) quests.EnterStage(effect.Target!.Value, effect.Stage);
    }));
    Require(scripts.Dispatch(Key(0x900), "OnTriggerEnter", Key(0x14)).Blocks == 2, "Filtered and unfiltered source event order failed.");
    Require(first[0].Read(1) == 2 && first[1].Read(1) == 0 && peer.Read(1) == 1 && quests.Variable(Key(0x600), 1) == 2,
        "Winning override, per-instance isolation, cross-cell reference write or ordered quest write failed.");
    Require(scripts.Dispatch(Key(0x901), "OnTriggerEnter", Key(0x902)).Blocks == 2 && first[1].Read(1) == 0 && first[1].Read(2) == 5,
        "Mismatched event filter ran or matching blocks lost source order.");
    first[0].Write(2, 1.0000000000000002);
    for (var iteration = 0; iteration < 30; ++iteration)
    {
        scripts.UnloadCell(Key(0x800)); world.UnloadCell(Key(0x800));
        Reject(() => scripts.Dispatch(Key(0x900), "GameMode"));
        world.LoadCell(firstCell);
    }
    Require(world.InstanceCount == 3 && world.ScriptDefinitionCount == 1 && first[0].Read(2) == 1.0000000000000002,
        "Cell teardown grew or reset mutable/reference state.");
    using (var spatial = new FalloutReferenceWorld(records))
    {
        spatial.LoadCell(firstCell);
        spatial.Get(Key(0x900)).DeletePending = true;
        // A neighboring spatial grid can overlap the same persistent source references.
        spatial.LoadCell(secondCell with { References = secondCell.References.Concat(firstCell.References).ToArray() });
        Require(spatial.Get(Key(0x900)).DeletePending && !spatial.Get(Key(0x900)).Deleted,
            "Overlapping residency committed deletion while the source reference was still loaded.");
        spatial.UnloadCell(firstCell.Cell.FormKey);
        Require(spatial.IsResident(Key(0x900)) && spatial.ResidentInstances.Count() == 3 &&
            spatial.Get(Key(0x900)).Cell == firstCell.Cell.FormKey && !spatial.Get(Key(0x900)).Deleted,
            "Spatial residency changed source ancestry, duplicated events or deleted a still-resident reference.");
        spatial.UnloadCell(secondCell.Cell.FormKey);
        Require(!spatial.IsResident(Key(0x900)) && spatial.Get(Key(0x900)).Deleted,
            "Last spatial unload did not commit the retained tombstone.");
    }
    first[0].Write(1, 6);
    var failure = scripts.Dispatch(Key(0x900), "GameMode");
    Require(failure.Error?.Contains("MissingOperation", StringComparison.Ordinal) == true && first[0].Read(1) == 7,
        "Reached unsupported command lost the executed prefix or explicit failure.");
    Require(scripts.Dispatch(Key(0x901), "GameMode").Error is null, "One instance's failure poisoned another instance of the same script.");
    var soundRandom = world.Get(Key(0x900)).SoundRandom;
    soundRandom.Restore(47);
    _ = soundRandom.NextBounded(10);
    Require(world.Get(Key(0x901)).Capture().SoundRandomState is null,
        "Using one reference's sound random state initialized another reference.");
    var saved = JsonSerializer.Serialize(world.Capture());
    var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!;
    using var restored = new FalloutReferenceWorld(records);
    restored.Restore(snapshots);
    restored.LoadCell(firstCell); restored.LoadCell(secondCell);
    Require(JsonSerializer.Serialize(restored.Capture()) == saved, "Cold state changed local Float64 bits or fault identity.");
    Require(restored.Get(Key(0x900)).SoundRandom.NextBounded(1000) == soundRandom.NextBounded(1000),
        "Cold restoration changed the next sound variation choice.");
    var coldScripts = new FalloutReferenceScripts(records, restored, quests, new((_, _) => false, _ => { }));
    Require(coldScripts.Dispatch(Key(0x900), "GameMode").Error == failure.Error && restored.Get(Key(0x900)).Read(1) == 7,
        "Cold restoration reran a failed block or discarded its error.");
    using var rejected = new FalloutReferenceWorld(records);
    Reject(() => rejected.Restore([snapshots[0], snapshots[0]]));
    Require(rejected.InstanceCount == 0, "Failed restore partially published reference state.");
    Reject(() => rejected.Restore([snapshots[0] with { Variables = new Dictionary<uint, double> { [1] = double.NaN, [2] = 0 } }]));
    using var originalRecords = FalloutPluginStack.Load(directory, ["Base.esm"]);
    using var wrongSource = new FalloutReferenceWorld(originalRecords);
    Reject(() => wrongSource.Restore(snapshots));
    Require(wrongSource.InstanceCount == 0, "Changed winning script source was admitted during restore.");
    world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x802)));
    Require(scripts.Activate(Key(0x903), Key(0x14)) is { Blocks: 1, Error: null } && effects.Count == 0,
        "An empty authored OnActivate failed to suppress default activation.");
    Require(scripts.Activate(Key(0x904), Key(0x14)) is { Blocks: 0, Error: null } &&
        effects.Single().Kind == FalloutReferenceEffectKind.DefaultActivate, "An unscripted object lost its default action.");
    effects.Clear();
    var failingDefault = new FalloutReferenceScripts(records, world, quests,
        new((_, _) => false, _ => throw new NotSupportedException("Transient native action failure")));
    Require(failingDefault.Activate(Key(0x904), Key(0x14)).Error is not null && world.Get(Key(0x904)).ScriptError is null,
        "Failed native activation poisoned an absent source program.");
    Require(scripts.Activate(Key(0x904), Key(0x14)).Error is null, "Native activation could not be retried.");
    effects.Clear();
    var capabilityAvailable = false;
    var retrying = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ =>
    { if (!capabilityAvailable) throw new NotSupportedException("Missing runtime capability"); }));
    Require(retrying.Activate(Key(0x902), Key(0x14)).Error is not null, "Unsupported scripted activation did not fail closed.");
    capabilityAvailable = true;
    Require(retrying.Dispatch(Key(0x902), "GameMode").Error is not null, "A failed interaction retried automatically on the frame clock.");
    Require(retrying.Activate(Key(0x902), Key(0x14)).Error is null && world.Get(Key(0x902)).ScriptError is null,
        "A fresh player activation could not retry its failed source program.");
    Require(scripts.Activate(Key(0x902), Key(0x14)) is { Blocks: 1, Error: null },
        "Activation filtered its ignored header argument or rejected instance-local writes.");
    Require(effects.Select(effect => effect.Kind).SequenceEqual(new[] { FalloutReferenceEffectKind.SetStage,
        FalloutReferenceEffectKind.SpecialMenu, FalloutReferenceEffectKind.DefaultActivate }) && effects[1].Value == 42 &&
        peer.Read(2) == 42 && quests.StageDone(Key(0x600), 19),
        "Synchronous effect/query order, source menu argument or default activation bypass failed.");
    var contacts = new FalloutTriggerContacts();
    var contactFrame = contacts.Advance([Key(0x14), Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerEnter", ActionReference: { ObjectId: 0x14 } } &&
        contactFrame.Single(value => value.Name == "OnTrigger").TriggerReferences!.SetEquals([Key(0x14)]),
        "Simultaneous contacts were admitted in one frame.");
    contactFrame = contacts.Advance([Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerEnter", ActionReference: { ObjectId: 0x902 } } &&
        !contactFrame.Any(value => value.Name == "OnTriggerLeave"), "Leave did not defer behind an enter.");
    contactFrame = contacts.Advance([Key(0x902)]);
    Require(contactFrame[0] is { Name: "OnTriggerLeave", ActionReference: { ObjectId: 0x14 } }, "Deferred departure was lost.");
    Require(contacts.Advance([]).Single() is { Name: "OnTriggerLeave", ActionReference: { ObjectId: 0x902 } } &&
        contacts.Advance([]).Count == 0, "Trigger exit repeated or retained a stale occupant.");
    Reject(() => contacts.Advance([Key(0x14), Key(0x14)]));
    Require(scripts.DispatchFrame(Key(0x902), [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { Key(0x14) })], 0)
        .Single() is { Blocks: 1, Error: null } && peer.Read(2) == 43, "OnTrigger lost membership or manufactured an action reference.");
    PrimitiveContracts(records);
    Require(world.IsEnabled(Key(0x904)) && !world.IsEnabled(Key(0x905)) && !world.IsEnabled(Key(0x906)),
        "Initial opposite/recursive enable state or XESP padding was decoded incorrectly.");
    Require(world.SetEnabled(Key(0x904), false) && world.IsEnabled(Key(0x904)), "Script disable applied before the world update.");
    world.AdvanceEnableChanges(0, new(1.2f, 2), _ => false);
    Require(world.IsEnabled(Key(0x905)) && world.IsEnabled(Key(0x906)),
        "Parent change did not propagate through source enable relationships.");
    Require(!world.SetEnabled(Key(0x905), false) && world.IsEnabled(Key(0x905)), "A child independently overrode its enable parent.");
    using (var fades = new FalloutReferenceWorld(records))
    {
        var reference = Key(0x904);
        var times = new FalloutReferenceFadeSettings(2, 4);
        fades.SetEnabled(reference, false);
        fades.AdvanceEnableChanges(0, times, _ => true);
        fades.SetEnabled(reference, true, true);
        Require(!fades.IsEnabled(reference), "Enable bypassed the request queue.");
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .05f, "Fade-in lost its source rate or initial opacity.");
        fades.AdvanceEnableChanges(20, times, _ => true);
        Require(fades.Get(reference).Opacity == .15f, "A long frame bypassed the opacity step ceiling.");
        fades.SetEnabled(reference, false, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .125f, "Fading disable lost collision/enabled state before opacity completion.");
        using var restoredFade = new FalloutReferenceWorld(records);
        restoredFade.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fades.Capture()))!);
        for (var i = 0; i < 8; i++)
        {
            fades.AdvanceEnableChanges(.1, times, _ => true);
            restoredFade.AdvanceEnableChanges(.1, times, _ => true);
        }
        Require(!fades.IsEnabled(reference) && JsonSerializer.Serialize(fades.Capture()) == JsonSerializer.Serialize(restoredFade.Capture()),
            "Pending fade did not complete identically after cold restoration.");
        fades.SetEnabled(reference, true, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        fades.SetEnabled(reference, false, true);
        fades.SetEnabled(reference, true, true);
        fades.AdvanceEnableChanges(.1, times, _ => true);
        Require(fades.IsEnabled(reference) && fades.Get(reference).Opacity == .1f, "Enable did not reverse the pending fade from current opacity.");
        fades.SetEnabled(reference, false, true);
        fades.AdvanceEnableChanges(0, times, _ => false);
        Require(!fades.IsEnabled(reference), "An unloaded fade node held an enable request forever.");
        Console.WriteLine("OPENNV_REFERENCE_FADE_CONTRACT_PASS queued=true rates=true stepCeiling=true collisionLifetime=true reversal=true coldPending=true absentNode=true");
    }
    var results = new FalloutMessageResults();
    var request = results.Begin(Key(0x800), Key(0x900));
    Require(results.Take(Key(0x900)) == -1 && results.Select(request, 3) && results.Take(Key(0x902)) == -1,
        "A message result was available before input or consumed by another reference.");
    var coldResults = new FalloutMessageResults();
    coldResults.Restore(JsonSerializer.Deserialize<FalloutMessageResultsSnapshot>(JsonSerializer.Serialize(results.Capture()))!);
    Require(coldResults.Take(Key(0x900)) == 3 && coldResults.Take(Key(0x900)) == -1, "Cold message result was lost or not consumed once.");
    var replacement = results.Begin(Key(0x800), Key(0x902));
    Require(!results.Select(request, 0) && results.Take(Key(0x900)) == -1 && results.Select(replacement, 2) && results.Take(Key(0x902)) == 2,
        "A replaced message callback corrupted the current result slot.");
    Console.WriteLine("OPENNV_MESSAGE_RESULT_CONTRACT_PASS callerIsolation=true consumedOnce=true replaced=true staleCallback=true coldResult=true");
    Reject(() => world.IsEnabled(Key(0x907)));
    using var enabledCold = new FalloutReferenceWorld(records);
    enabledCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
    Require(!enabledCold.IsEnabled(Key(0x904)) && enabledCold.IsEnabled(Key(0x906)), "Cold state lost parent enable changes.");
    Console.WriteLine("OPENNV_REFERENCE_SCRIPT_CONTRACT_PASS modelLess=true winningOverride=true instanceIsolation=true eventFilters=true sourceOrder=true crossCell=true teardown=true coldState=true explicitFailure=true sourceDriftRejected=true");
    Console.WriteLine("OPENNV_REFERENCE_INTERACTION_CONTRACT_PASS activationDefault=true synchronousEffects=true localWrites=true stageDone=true menuArgument=true deferredContacts=true triggerActionRef=true noInventedPrimitive=true");
    Console.WriteLine("OPENNV_REFERENCE_ENABLE_CONTRACT_PASS sourceParent=true opposite=true nested=true padding=true childNoOp=true cycleRejected=true coldState=true");
}
finally
{
    File.Delete(Path.Combine(directory, "Base.esm"));
    File.Delete(Path.Combine(directory, "Patch.esp"));
    Directory.Delete(directory);
}

ConversationContracts.Run();
ActorSourceContracts.Run();
ActorDamageContracts.Run();
PlayerSkillContracts.Run();
if (args is [var voiceRoot, "--voices"]) OwnedDialogueVoiceProbe.Run(voiceRoot);
StageAndInventoryContracts.Run();
WeaponHandlingContracts.Run();
WeaponFiringContracts.Run();
var disabledControls = new FalloutPlayerControlState(false, false, false, false, false, false, false);
Require(new FalloutPlayerControlCommand(true, []).Apply(disabledControls) == FalloutPlayerControlState.AllEnabled,
    "EnablePlayerControls without arguments did not enable all controls.");
Require(new FalloutPlayerControlCommand(true, [true, true, true, true, true, true]).Apply(disabledControls).Sneaking,
    "Omitted EnablePlayerControls flag lost its source default.");
Require(new FalloutPlayerControlCommand(false, []).Apply(FalloutPlayerControlState.AllEnabled) ==
    new FalloutPlayerControlState(false, false, false, false, true, true, true), "DisablePlayerControls source defaults changed.");

if (args is [var ownedRoot])
{
    OwnedOpeningProgramProbe.Run(ownedRoot);
    OwnedReferenceInteractionProbe.Run(ownedRoot);
    var questionnaireHistory = OwnedConversationProbe.Run(ownedRoot);
    OwnedInventoryProbe.Run(ownedRoot);
    OwnedFarewellProbe.Run(ownedRoot, questionnaireHistory);
}

static FalloutFormKey Key(uint id) => new("Base.esm", id);
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
    throw new InvalidDataException("Invalid state/event was admitted.");
}
static byte[] Script(int increment)
{
    var source = $"scn ObjectScript\nshort count\nfloat timer\nbegin OnTriggerEnter player\n" +
        $"set count to count + {increment}\nset PeerREF.count to PeerREF.count + 1\nset TestQuest.shared to TestQuest.shared + count\nend\n" +
        "begin OnTriggerEnter\nset timer to timer + 1\nend\nbegin OnTriggerEnter PeerREF\nset timer to timer + 4\nend\n" +
        "begin GameMode\nif count >= 6\nset count to count + 1\nMissingOperation\nendif\nend\n" +
        $"begin OnActivate PeerREF\nset timer to {40 + increment}\nSetStage TestQuest 19\n" +
        "if GetStageDone TestQuest 19 == 1\nShowLoveTesterMenuParams timer\nendif\nActivate\nend\n" +
        "begin OnTrigger player\nif IsActionRef player == 0\nset timer to timer + 1\nendif\nend";
    return Record("SCPT", 0x500, Field("EDID", Text("ObjectScript")), Local(1, "count"), Local(1, "count"), Local(2, "timer"),
        Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x902u)),
        Field("SCRO", BitConverter.GetBytes(0x600u)), Field("SCTX", Text(source)));
}
static byte[] Local(uint index, string name, byte flags = 0, byte padding = 0)
{
    var data = Enumerable.Repeat(padding, 24).ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(data, index);
    data[16] = flags;
    return Field("SLSD", data).Concat(Field("SCVR", Text(name))).ToArray();
}
static byte[] Reference(uint id, string name, uint baseId = 0x700) => Record("REFR", id, Field("EDID", Text(name)),
    Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new byte[24]));
static byte[] EnableChild(uint id, uint parent, byte flags)
{
    var data = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(data, parent);
    data[4] = flags; data[5] = 0xdb; data[6] = 0x18; // Observed nonzero unused bytes must not become flags.
    return Record("REFR", id, Field("NAME", BitConverter.GetBytes(0x702u)), Field("DATA", new byte[24]), Field("XESP", data));
}
static void PrimitiveContracts(FalloutPluginStack records)
{
    Require(FalloutReferencePrimitive.Read(records.GetEffective(Key(0x900))) is null, "A reference without XPRM acquired a primitive.");
    // Byte layout and invalid extents are exercised by the native physics audit;
    // this scalar probe also checks that no primitive is invented for model-less refs.
}
static byte[] Cell(uint id, params byte[][] references)
{
    var body = references.SelectMany(bytes => bytes).ToArray();
    var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
    BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); body.CopyTo(group, 24);
    return Record("CELL", id, Field("DATA", [1])).Concat(group).ToArray();
}
static byte[] Header(string? master = null)
{
    var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
    return master is null ? Record("TES4", 0, Field("HEDR", data)) :
        Record("TES4", 0, Field("HEDR", data), Field("MAST", Text(master)), Field("DATA", new byte[8]));
}
static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
static byte[] Field(string signature, byte[] data)
{
    var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
}
static byte[] Record(string signature, uint id, params byte[][] fields)
{
    var data = fields.SelectMany(bytes => bytes).ToArray();
    var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
}
