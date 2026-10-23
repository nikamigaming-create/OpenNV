using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;

internal static class OwnedCampfireBranchProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var trigger = records.GetEffective(new("FalloutNV.esm", 0x15d9f4));
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(trigger, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, trigger) ?? throw new InvalidDataException("Owned trigger has no script.");
        Require(trigger.Signature == "REFR" && basis.Signature == "ACTI" && basis.FormKey == new FalloutFormKey("FalloutNV.esm", 0x15d9ef) &&
            script.FormKey == new FalloutFormKey("FalloutNV.esm", 0x15d9ed), "Owned campfire source chain differs from the qualified records.");
        var fields = script.ReadSubrecords().ToArray();
        var text = fields.Single(field => field.Signature == "SCTX").Data;
        var compiled = fields.Single(field => field.Signature == "SCDA").Data;
        Require(Hash(text.Span) == "93B169995D741E19142BE1DF8B6BD484136BC498CF831810BC082515D435A7CF" &&
            Hash(compiled.Span) == "1478C913B4D17B6655398B8E3DA8E44D2519C8E95BBCE8039BE09C1B861676DA",
            "Owned script differs from the qualified source/compiled control flow.");
        var bindings = new FalloutScriptBindings(records, trigger, script, fields);
        var quest = bindings.Form("VCG03").FormKey; var player = bindings.Reference("Player");
        var powder = bindings.Form("NVHealingPowder").FormKey; var sunny = bindings.Reference("SunnyREF");
        var start = bindings.Variable("VCG03.nHealingPowdersAtStart");
        Require(start.Owner == quest && player == records.RuntimeFormKey(0x14) &&
            powder == new FalloutFormKey("FalloutNV.esm", 0x136a1d), "Actual compiled reference/variable bindings changed.");
        var originals = new[] { trigger, basis, script, records.GetEffective(quest), records.GetEffective(powder), records.GetEffective(sunny) }
            .Select(record => (Record: record, Hash: Hash(record.ReadData()))).ToArray();
        var source = FalloutDialogueTopic.ScriptText(text.Span);
        var block = FalloutGameModeProgram.ReadEvents(source).Single();
        Require(block.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase) && block.Filter?.Equals("player", StringComparison.OrdinalIgnoreCase) == true,
            "Owned script lost its original filtered event.");
        var identity = block.Program.ProgramSha256;
        foreach (var (starting, inventory, expected) in new (int, int, string[])[]
        {
            (0, 1, ["SetObjectiveDisplayed VCG03 20 0", "SetObjectiveDisplayed VCG03 30 1", "SetObjectiveCompleted VCG03 30 1", "SunnyREF.StartConversation player", "Disable", "MarkForDelete"]),
            (1, 1, ["SetObjectiveCompleted VCG03 30 1", "SunnyREF.StartConversation player", "Disable", "MarkForDelete"]),
            (1, 0, [])
        })
        {
            var calls = new List<string>();
            block.Program.Execute(name =>
            {
                Require(bindings.Variable(name) == start, "Unexpected source variable query.");
                return starting;
            }, (_, _) => throw new InvalidDataException("Owned source unexpectedly assigned a local."),
            (name, arguments) => calls.Add(string.Join(" ", new[] { name }.Concat(arguments))), Function);
            Require(calls.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase) && block.Program.ProgramSha256 == identity,
                "Owned branch changed effect order, admitted the dead tail, or changed source identity.");

            FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
            {
                "vcg03.nhealingpowdersatstart" => null, // The evaluator probes functions before bound variables.
                "player.getitemcount" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == powder, "Wrong item-count owner."); return inventory;
                }),
                "getquestcompleted" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == quest, "Wrong quest completion owner."); return 0;
                }),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == quest && arguments[1].Number == 10,
                        "Ignored Else payload queried objective 30."); return 1;
                }),
                _ => throw new InvalidDataException("Dead tail or unknown source function queried: " + name)
            };
        }
        Require(originals.All(pair => pair.Hash == Hash(pair.Record.ReadData())), "Owned source bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_CAMPFIRE_BRANCH_PASS trigger={trigger.FormKey} script={script.FormKey} cases=3 " +
            "originalSourceAndBindings=true deadTailNeverQueried=true sourceOrder=true sourceReadonly=true " +
            "controlledFunctionInputs=true commandCallbacksOnly=true physicalTriggerCampaignAndRetail=unverified");
    }

    internal static void RunEvents(string game, string savePath)
    {
        const string boundary = "Controlled fixture has no native conversation publication.";
        var original = File.ReadAllBytes(savePath); var originalHash = Hash(original);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(original) ?? throw new InvalidDataException("Missing genuine save.");
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        Require(saved.Schema == FalloutNativeCampaignSave.ExpectedSchema && saved.SaveCompatibilityId == content.SaveCompatibilityId,
            "Owned event proof requires the current source-compatible campaign snapshot.");
        var key = new FalloutFormKey("FalloutNV.esm", 0x15d9f4);
        var placed = records.GetEffective(key);
        var script = FalloutScriptLocals.AttachedScript(records, placed) ?? throw new InvalidDataException("Missing original script.");
        var fields = script.ReadSubrecords().ToArray();
        Require(Hash(fields.Single(value => value.Signature == "SCTX").Data.Span) ==
            "93B169995D741E19142BE1DF8B6BD484136BC498CF831810BC082515D435A7CF", "Winning source changed.");
        var bindings = new FalloutScriptBindings(records, placed, script, fields);
        var quest = bindings.Form("VCG03").FormKey; var player = bindings.Reference("Player");
        var sunny = bindings.Reference("SunnyREF"); var powder = bindings.Form("NVHealingPowder").FormKey;
        var start = bindings.Variable("VCG03.nHealingPowdersAtStart");
        var input = saved.References!.Single(value => value.Reference == key);
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(placed) ?? throw new InvalidDataException("No source CELL."));
        // Explicit actor/reference-scoped fixture. Keep the actual source CELL,
        // reference declaration and saved pose, admitting no unrelated continuations.
        cell = cell with { References = cell.References.Where(value => value.FormKey == key).ToArray() };
        Require(cell.References.Count == 1, "Actual CELL lacks the selected trigger.");
        var sourceHash = Hash(script.ReadData());
        var savedItems = saved.Inventory.ToDictionary(item => item.RuntimeFormId);
        var sourceInventory = FalloutCampaignInventoryResolver.Resolve(records,
            saved.Inventory.Select(item => new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray(), null);
        sourceInventory = sourceInventory with
        {
            Items = sourceInventory.Items.Select(item => item with
            { Variants = savedItems[item.RuntimeFormId].Variants, UnequipLocked = savedItems[item.RuntimeFormId].UnequipLocked }).ToArray()
        };
        foreach (var path in new[] { "no-powder", "outer", "nested" })
        {
            using var world = new FalloutReferenceWorld(records);
            world.Restore([input]); world.LoadCell(cell);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var inventory = new FalloutPlayerInventory();
            inventory.Restore(sourceInventory, saved.EquippedRuntimeFormIds, saved.InventoryRandomState);
            var commands = new FalloutInventoryCommands(records, world, inventory, () => 1);
            var desired = path == "no-powder" ? 0 : 1;
            var beforeCount = checked((int)commands.ItemCount(player, powder));
            if (beforeCount != desired) commands.Execute(new(beforeCount < desired ? FalloutInventoryCommandKind.Add : FalloutInventoryCommandKind.Remove,
                player, powder, Count: Math.Abs(desired - beforeCount)));
            // Explicit controlled fixture inputs, applied only to disposable
            // shared owners, never to the original campaign file.
            quests.SetVariable(start.Owner, start.Index, path == "outer" ? 0 : 1);
            quests.ApplyObjective(quest, 10, true, path == "outer");
            quests.ApplyObjective(quest, 20, true, true);
            quests.ApplyObjective(quest, 30, true, false); quests.ApplyObjective(quest, 30, false, false);
            var effects = 0;
            var scripts = Create(world, quests, commands, () => effects++);
            var contact = new FalloutReferenceScriptEvent("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player });
            var before = Snapshot(world, quests, inventory);
            var wrong = scripts.DispatchFrame(key, [contact with { TriggerReferences = new HashSet<FalloutFormKey> { sunny } }], 0).Single();
            Require(wrong.Error is null && wrong.Blocks == 0 && effects == 0 && before == Snapshot(world, quests, inventory),
                "Unmatched contact ran the authored player block or mutated state.");
            try { _ = scripts.DispatchFrame(key, [contact, contact], 0); throw new InvalidOperationException("Duplicate event was admitted."); }
            catch (InvalidDataException error) when (error.Message == "A reference frame has absent or duplicate event admission.") { }
            Require(before == Snapshot(world, quests, inventory) && effects == 0, "Duplicate frame committed effects.");
            var result = scripts.DispatchFrame(key, [contact], .1).Single();
            if (path == "no-powder")
            {
                Require(result.Error is null && result.Blocks == 1 && effects == 0 && before == Snapshot(world, quests, inventory),
                    "Empty inventory branch failed or executed the dead tail.");
            }
            else
            {
                Require(result.Error == "OnTrigger: " + boundary && result.Blocks == 0 && effects == 1 &&
                    quests.Objective(quest, 30).Completed && quests.Objective(quest, 30).Displayed == (path == "outer") &&
                    quests.Objective(quest, 20).Displayed == (path != "outer") &&
                    world.Get(key).Enabled == input.Enabled && !world.Get(key).DeletePending && !world.Get(key).Deleted,
                    "Actual source prefix/order changed, or fixture falsely acknowledged native conversation.");
                var retained = world.Capture(); var questState = quests.Capture(); var itemState = inventory.Capture();
                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(retained))!); cold.LoadCell(cell);
                var coldQuests = new FalloutQuestState(records);
                coldQuests.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(questState))!);
                var coldItems = new FalloutPlayerInventory(); coldItems.Restore(itemState.Inventory, itemState.EquippedRuntimeFormIds, itemState.InventoryRandomState);
                var coldEffects = 0;
                var coldScripts = Create(cold, coldQuests, new(records, cold, coldItems, () => 1), () => coldEffects++);
                var coldBefore = Snapshot(cold, coldQuests, coldItems);
                var retry = coldScripts.DispatchFrame(key, [contact], .1).Single();
                Require(retry.Error == result.Error && retry.Blocks == 0 && coldEffects == 0 &&
                    coldBefore == Snapshot(cold, coldQuests, coldItems), "Cold event replayed its committed objective prefix.");
            }
            Console.WriteLine($"OPENNV_OWNED_CAMPFIRE_EVENT_CASE path={path} blocks={result.Blocks} " +
                $"nativeEffectRequests={effects} boundary={(result.Error is null ? "none" : "native-conversation-unverified")} " +
                "actualManagedOwners=true controlledFixtureInputs=true physicalContact=false campaignProgress=false");
        }
        Require(Hash(File.ReadAllBytes(savePath)) == originalHash && Hash(script.ReadData()) == sourceHash,
            "Event proof changed original save or source bytes.");
        Console.WriteLine("OPENNV_OWNED_CAMPFIRE_EVENT_PASS cases=3 wrongContactRejected=true duplicateAdmissionRefused=true " +
            "actualQuestInventoryOwners=true committedPrefixColdNoReplay=true nativeConversationNotAcknowledged=true sourceAndSaveReadonly=true " +
            "fullCampaignPhysicalContactCraftingRetail=unverified");

        FalloutReferenceScripts Create(FalloutReferenceWorld owner, FalloutQuestState state, FalloutInventoryCommands inventoryOwner, Action count) =>
            new(records, owner, state, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.Conversation && effect.Source == key && effect.Target == sunny && effect.Argument == player &&
                    state.Objective(quest, 30).Completed, "Unexpected effect or conversation before objective prefix.");
                count(); throw new NotSupportedException(boundary);
            }, Inventory: inventoryOwner));
        static string Snapshot(FalloutReferenceWorld owner, FalloutQuestState state, FalloutPlayerInventory items) =>
            JsonSerializer.Serialize(new { References = owner.Capture(), Quests = state.Capture(), Inventory = items.Capture() });
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
}
