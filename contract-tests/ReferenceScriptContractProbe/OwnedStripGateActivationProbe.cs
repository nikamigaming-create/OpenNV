using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedStripGateActivationProbe
{
    internal static void Run(string game, string gateSelector, string actorSelector)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var gate = records.GetEffective(Select(gateSelector));
        var actor = records.GetEffective(Select(actorSelector));
        Require(gate.Signature == "REFR" && !gate.IsDeleted && actor.Signature == "ACHR" && !actor.IsDeleted,
            "Supply an actual owned gate REFR and an explicit genuine ACHR fixture participant.");
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(gate, "NAME"));
        var actorBase = records.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, gate) ?? throw new InvalidDataException("Gate has no attached source script.");
        Require(basis.Signature == "DOOR" && actorBase.Signature == "NPC_" &&
            script.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span.SequenceEqual("VStripGateScript\0"u8),
            "Selected owned source is not the qualified gate script or a genuine NPC actor.");
        var fields = script.ReadSubrecords().ToArray();
        var source = FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span);
        Require(source.Contains("Bot01.MoveToMarkerWithFade", StringComparison.OrdinalIgnoreCase) &&
            FalloutGameModeProgram.ReadEvents(source).Count(block => block.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase)) == 1,
            "Winning gate script lost its actual authored activation block.");
        var bindings = new FalloutScriptBindings(records, gate, script, fields);
        var gateName = FalloutDialogueTopic.Text(gate.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
        var (area, markerName, box) = gateName switch
        {
            "VStripDoorMiddle1" or "VStripDoorMiddle2" => (2, "VStripMark1", "VStripMQCodeBox"),
            "VStripDoorSouth" => (3, "VStripMark21", "VStripSouthCodeBox"),
            "VStripDoorNorth" => (1, "VStripMark41", "VStripNorthCodeBox"),
            _ => throw new InvalidDataException("Selected gate is not one of the source script's qualified branches.")
        };
        var marker = records.GetEffective(bindings.Reference(markerName));
        Require(marker.Signature == "REFR" && !marker.IsDeleted && bindings.VariableKind("Bot01") == FalloutScriptLocalKind.Form,
            "Source branch lost its compiled marker binding or typed actor local.");
        var cellKey = FalloutCellSceneReader.ParentCell(gate) ?? throw new InvalidDataException("Gate has no source CELL.");
        var cell = FalloutCellSceneReader.Read(records, cellKey);
        Require(cell.References.Single(value => value.FormKey == gate.FormKey).Teleport is not null,
            "Selected source gate is not a loading portal.");
        var sourceRecords = new Dictionary<FalloutFormKey, string>();
        foreach (var record in new[] { gate, basis, actor, actorBase, marker, script }) Remember(record);
        foreach (var field in fields.Where(field => field.Signature == "SCRO"))
        {
            Require(field.Data.Length == 4, "Source compiled reference has an invalid extent.");
            var key = script.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (records.RuntimeFormId(key) == 0x14) continue;
            var record = records.GetEffective(key); Remember(record);
            if (record.Signature is "QUST" or "REFR" or "ACHR" or "ACRE" && FalloutScriptLocals.AttachedScript(records, record) is { } attached)
                Remember(attached);
        }
        var pluginHashes = content.PluginSources.ToDictionary(value => value.AbsolutePath, value => FileHash(value.AbsolutePath));
        var player = records.RuntimeFormKey(0x14);
        foreach (var mode in new[] { "null-actor", "bound-actor", "no-population-move" })
        {
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            _ = world.Get(actor.FormKey); _ = world.Get(marker.FormKey);
            var globals = FalloutGlobalState.Read(records);
            var quests = new FalloutQuestState(records);
            var effects = new List<FalloutReferenceScriptEffect>();
            Write("VStreetFluffNPC.NPCNormal", mode == "no-population-move" ? 0 : 1);
            Write("Bot01", FalloutScriptValue.Form(mode == "null-actor" ? 0 : records.RuntimeFormId(actor.FormKey)));
            Write("VStripMQCodeBox.iPlayerInArea", -7); Write("VStreetFluffNPC.iPlayerInArea", -7);
            foreach (var name in new[] { "bMoved", "bSpawned", "iCount" }) Write(box + "." + name, 7);
            var beforeReferences = world.Capture();
            var beforeOverrides = JsonSerializer.Serialize(world.CaptureActorOverrides());
            var beforeQuests = JsonSerializer.Serialize(quests.Capture());
            var beforeGlobals = JsonSerializer.Serialize(globals.Capture());
            var beforeValues = JsonSerializer.Serialize(world.ScriptValues.Capture());
            var beforeActor = JsonSerializer.Serialize(world.Get(actor.FormKey).Capture());
            var beforeMarker = JsonSerializer.Serialize(world.Get(marker.FormKey).Capture());
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) =>
                throw new InvalidDataException("Gate branch unexpectedly queried furniture."), effect =>
                {
                    Require(mode == "no-population-move" && effect.Kind == FalloutReferenceEffectKind.DefaultActivate &&
                        effect.Source == gate.FormKey && effect.Target == gate.FormKey && effect.Argument == player,
                        "Source gate bypassed its branch or emitted an unexpected effect.");
                    Require(Read("VStripMQCodeBox.iPlayerInArea") == area && Read("VStreetFluffNPC.iPlayerInArea") == area &&
                        new[] { "bMoved", "bSpawned", "iCount" }.All(name => Read(box + "." + name) == 0),
                        "Source Activate preceded its authored shared-state writes.");
                    effects.Add(effect); // Observe the actual source effect; do not invoke a portal or invent a player session.
                }, Globals: globals));
            var result = scripts.Activate(gate.FormKey, player);
            if (mode == "no-population-move")
                Require(result.Error is null && result.Blocks == 1 && effects.Count == 1,
                    "Actual source no-move branch did not reach its ordered activation effect: " + result.Error);
            else
            {
                var expected = mode == "null-actor" ? "Script reference variable has no valid form identity." :
                    "Reached object-script command Bot01.MoveToMarkerWithFade (1 arguments) has no owner.";
                Require(result.Error == "OnActivate: " + expected && result.Blocks == 0 && effects.Count == 0 &&
                    world.Get(gate.FormKey).ScriptError == result.Error,
                    "Actual source gate stopped at a different boundary: " + result.Error);
                var normalized = world.Capture().Select(value => value.Reference == gate.FormKey ? value with { ScriptError = null } : value).ToArray();
                Require(JsonSerializer.Serialize(normalized) == JsonSerializer.Serialize(beforeReferences) &&
                    JsonSerializer.Serialize(quests.Capture()) == beforeQuests && JsonSerializer.Serialize(globals.Capture()) == beforeGlobals &&
                    JsonSerializer.Serialize(world.ScriptValues.Capture()) == beforeValues,
                    "Failed source move dispatched effects or changed its unexecuted prefix.");
            }
            Require(beforeOverrides == JsonSerializer.Serialize(world.CaptureActorOverrides()) &&
                beforeActor == JsonSerializer.Serialize(world.Get(actor.FormKey).Capture()) && beforeMarker == JsonSerializer.Serialize(world.Get(marker.FormKey).Capture()) &&
                !world.Get(gate.FormKey).DoorOpen && world.Get(gate.FormKey).DoorMotion is null,
                "Controlled source gate fixture moved an actor/marker or invented door motion.");
            var retained = world.Capture(); var retainedOverrides = world.CaptureActorOverrides();
            var retainedQuests = quests.Capture(); var retainedGlobals = globals.Capture();
            var retainedValues = world.ScriptValues.Capture();
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(retained))!);
            cold.RestoreActorOverrides(JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(retainedOverrides))!);
            cold.ScriptValues.Restore(JsonSerializer.Deserialize<FalloutScriptValueStoreSnapshot>(JsonSerializer.Serialize(retainedValues))!);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(retainedQuests);
            var coldGlobals = FalloutGlobalState.Read(records); coldGlobals.Restore(retainedGlobals);
            cold.LoadCell(cell);
            var coldBefore = JsonSerializer.Serialize(new { References = cold.Capture(), Overrides = cold.CaptureActorOverrides(),
                Quests = coldQuests.Capture(), Globals = coldGlobals.Capture(), Values = cold.ScriptValues.Capture() });
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(retained) &&
                JsonSerializer.Serialize(cold.CaptureActorOverrides()) == JsonSerializer.Serialize(retainedOverrides) &&
                JsonSerializer.Serialize(coldQuests.Capture()) == JsonSerializer.Serialize(retainedQuests) &&
                JsonSerializer.Serialize(coldGlobals.Capture()) == JsonSerializer.Serialize(retainedGlobals) &&
                JsonSerializer.Serialize(cold.ScriptValues.Capture()) == JsonSerializer.Serialize(retainedValues),
                "Cold source gate restoration changed its complete retained component state.");
            if (mode != "no-population-move")
            {
                var retryEffects = 0;
                var retry = new FalloutReferenceScripts(records, cold, coldQuests, new((_, _) =>
                    throw new InvalidDataException("Latched gate queried furniture."), _ => retryEffects++, Globals: coldGlobals));
                var retryResult = retry.Activate(gate.FormKey, player);
                Require(retryResult.Error == result.Error && retryEffects == 0 && coldBefore == JsonSerializer.Serialize(new
                    { References = cold.Capture(), Overrides = cold.CaptureActorOverrides(), Quests = coldQuests.Capture(),
                        Globals = coldGlobals.Capture(), Values = cold.ScriptValues.Capture() }),
                    "Cold gate retry replayed or erased its retained failure prefix.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-strip-gate-controlled-branch", mode,
                gate = gate.FormKey, gateName, script = script.FormKey, actor = actor.FormKey, actorCell = world.Get(actor.FormKey).Cell,
                marker = marker.FormKey, markerName, actorSelection = "explicit-owned-ACHR-fixture-input; no source initialization inferred",
                result.Error, result.Blocks, defaultActivationEffects = effects.Count, controlledFixtureInputs = true,
                ordinaryGameplay = false, ordinaryHost = false, nativePortalAndPixels = "unverified", coldState = true }));

            double Read(string name) { var slot = bindings.Variable(name); return world.ReadVariable(quests, slot.Owner, slot.Index); }
            void Write(string name, FalloutScriptValue value)
            {
                var slot = bindings.Variable(name);
                var previous = world.ReadVariable(quests, slot.Owner, slot.Index);
                var raw = world.ScriptValues.Write(bindings.VariableKind(name), previous, value, script.Plugin.Name, $"{slot.Owner}:{slot.Index}");
                world.WriteVariable(quests, slot.Owner, slot.Index, raw);
            }
        }
        Require(sourceRecords.All(pair => pair.Value == RecordHash(records.GetEffective(pair.Key))) &&
            pluginHashes.All(pair => pair.Value == FileHash(pair.Key)), "Owned gate probe changed source inputs.");
        Console.WriteLine("OPENNV_OWNED_STRIP_GATE_BRANCH_PASS cases=3 actualSourceProgram=true compiledBindings=true nullActorDistinguished=true missingCommandRetained=true activationAfterWrites=true cold=true controlledFixtureInputs=true ordinaryGameplay=false ordinaryHost=false nativePortalAndPixels=unverified sourceReadonly=true");

        void Remember(FalloutPluginRecord record) => sourceRecords[record.FormKey] = RecordHash(record);
    }

    private static FalloutFormKey Select(string selector)
    {
        var parts = selector.Split(':');
        return parts.Length == 2 && uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            ? new(parts[0], id) : throw new ArgumentException("Supply exact OwnerPlugin:hex identities.");
    }
    private static string RecordHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
    private static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
