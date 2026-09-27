using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeInteractionUiAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is not (2 or 3)) throw new ArgumentException("Expected owned root, CELL editor ID, and optional diagnostic PNG path.");
            var root = args[0]; var cellId = args[1];
            RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", cellId).FormKey);
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var globals = FalloutGlobalState.Read(records);
            var player = new FalloutPlayerInventory();
            var item = cell.References.First(reference => cell.BaseObjects[reference.Base].Signature == "MISC" && world.CanActivate(reference.FormKey));
            world.Take(item.FormKey, player, 1, globals);
            if (world.CanActivate(item.FormKey) || player.Item(item.Base) is null) throw new InvalidDataException("Pickup did not move the source item.");
            var container = cell.References.First(reference => cell.BaseObjects[reference.Base].Signature == "CONT");
            var contents = world.Inventory(container.FormKey, 1, globals).Contents;
            var moved = contents.Items.FirstOrDefault();
            if (moved is not null)
            {
                var before = player.Item(moved.FormKey)?.Count ?? 0;
                contents.TransferTo(player, moved.FormKey, 1);
                if (player.Item(moved.FormKey)!.Count != before + 1 || (contents.Item(moved.FormKey)?.Count ?? 0) != moved.Count - 1)
                    throw new InvalidDataException("Container transfer did not conserve source item counts.");
            }
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(System.Text.Json.JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(System.Text.Json.JsonSerializer.Serialize(world.Capture()))!);
            cold.LoadCell(cell);
            if (cold.CanActivate(item.FormKey) ||
                System.Text.Json.JsonSerializer.Serialize(cold.Inventory(container.FormKey, 1, globals).Capture()) !=
                System.Text.Json.JsonSerializer.Serialize(world.Inventory(container.FormKey, 1, globals).Capture()))
                throw new InvalidDataException("Cold world restored a pickup or rerolled container contents.");
            var menu = new NativeOwnedContainerMenu(records, player, contents, "Player", "Container", () => { }, () => { });
            AddChild(menu);
            NativeHudTarget? target = null;
            var playerRecord = FalloutDialogueTopic.Find(records, "NPC_", "Player").FormKey;
            var vigor = FalloutNativeVigorResolver.Resolve(records, cell);
            var recipeInventory = new FalloutPlayerInventory();
            var perks = new List<FalloutFormKey>();
            var recipeSkills = new FalloutPlayerSkills(records, () => vigor.Initial, _ => false, () => [], globals,
                recipeInventory, playerRecord, () => FalloutDialogueSpeaker.Read(records, playerRecord).Race!.Value,
                () => false, () => perks);
            var sourceRecipeQueries = 0;
            foreach (var condition in records.EffectiveRecords("RCPE").SelectMany(FalloutCondition.Read)
                         .Where(condition => condition.Function is 382 or 449))
            {
                if (!FalloutInventoryConditions.TargetsPlayer(records, condition))
                    throw new NotSupportedException("Owned recipe fixture reached another actor's inventory condition.");
                float? Evaluate() => FalloutInventoryConditions.Evaluate(records, recipeInventory, recipeSkills.HasPerk, condition);
                if (Evaluate() != 0) throw new InvalidDataException("Absent recipe note/perk was accepted.");
                if (condition.Function == 449) perks.Add(condition.FormArgument1);
                else recipeInventory.Add(records, condition.FormArgument1, 1, 1, silent: true, globals);
                if (Evaluate() != 1) throw new InvalidDataException("Acquired recipe note/perk remained unavailable.");
                perks.Clear();
                if (condition.Function == 382) recipeInventory.Remove(condition.FormArgument1, 1, silent: true);
                if (Evaluate() != 0) throw new InvalidDataException("Removed recipe note/perk remained owned.");
                sourceRecipeQueries++;
            }
            GD.Print($"OPENNV_NATIVE_RECIPE_QUERIES_PASS sourceConditions={sourceRecipeQueries} livePerks=true liveNotes=true explicitPlayer=true");
            var vitals = new FalloutPlayerVitals(records, playerRecord, vigor.Initial);
            var damaged = vitals.State with { HitPoints = vitals.State.HitPoints - 10, ActionPoints = vitals.State.ActionPoints - 7 };
            var restoredVitals = new FalloutPlayerVitals(records, playerRecord, vigor.Initial,
                System.Text.Json.JsonSerializer.Deserialize<GameplayVitals>(System.Text.Json.JsonSerializer.Serialize(damaged))!);
            restoredVitals.SetSpecial(vigor.Initial);
            if (restoredVitals.State != damaged) throw new InvalidDataException("Cold stats or repeated SPECIAL restored missing HP/AP.");
            var hud = new NativeOwnedGameplayHud(records, "E", () => target, () => true, () => vitals.State); AddChild(hud);
            foreach (var action in new[] { "sTargetTypeOpenDoor", "sCloseButton", "sTargetTypeTake", "sSearch", "sTargetTypeTalk", "sTargetTypeSit", "sTargetTypeActivate", "sLocked" })
            {
                target = new(FalloutGameSettingStrings.Read(records, action), "Source reference", null);
                hud._Process(0);
                if (hud.Error is not null) throw new InvalidDataException(hud.Error);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (menu.Error is not null) throw new InvalidDataException(menu.Error);
            var quantityTransfers = new FalloutPlayerInventory();
            quantityTransfers.Add(records, item.Base, 12, 1, silent: true, globals);
            var changed = 0; var closed = false;
            menu.Free();
            menu = new(records, player, quantityTransfers, "Player", "Quantity fixture", () => closed = true, () => changed++);
            AddChild(menu);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var beforeQuantity = player.Item(item.Base)!.Count;
            BaseButton ContainerRow() => menu.GetChildren().OfType<BaseButton>().Last();
            ContainerRow().EmitSignal(BaseButton.SignalName.Pressed);
            var quantityMenu = menu.GetChildren().OfType<NativeOwnedQuantityMenu>().Single();
            if (quantityMenu.Error is not null) throw new InvalidDataException(quantityMenu.Error);
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Home });
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Right });
            if (quantityMenu.Quantity != 2) throw new InvalidDataException("Quantity controls did not select two.");
            if (args.Length == 3)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screenshot = GetViewport().GetTexture().GetImage();
                if (screenshot.SavePng(args[2]) != Error.Ok) throw new IOException("Quantity diagnostic PNG could not be written.");
            }
            // A container hotkey cannot bypass the active modal choice.
            menu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.A });
            if (quantityTransfers.Item(item.Base)!.Count != 12 || changed != 0) throw new InvalidDataException("Take All bypassed quantity modal.");
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
            if (closed || changed != 0 || quantityTransfers.Item(item.Base)!.Count != 12)
                throw new InvalidDataException("Quantity cancellation changed inventory or closed the container.");
            ContainerRow().EmitSignal(BaseButton.SignalName.Pressed);
            quantityMenu = menu.GetChildren().OfType<NativeOwnedQuantityMenu>().Single();
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Home });
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Right });
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Enter });
            if (changed != 1 || quantityTransfers.Item(item.Base)!.Count != 10 || player.Item(item.Base)!.Count != beforeQuantity + 2)
                throw new InvalidDataException("Quantity confirmation did not conserve selected counts.");
            var coldInventory = new FalloutPlayerInventory();
            var quantitySaved = quantityTransfers.Capture();
            coldInventory.Restore(quantitySaved.Inventory, quantitySaved.EquippedRuntimeFormIds, quantitySaved.InventoryRandomState);
            if (coldInventory.Item(item.Base)!.Count != 10) throw new InvalidDataException("Cold inventory lost partial transfer.");
            ContainerRow().EmitSignal(BaseButton.SignalName.Pressed);
            quantityMenu = menu.GetChildren().OfType<NativeOwnedQuantityMenu>().Single();
            quantityTransfers.Remove(item.Base, 8, silent: true);
            quantityMenu._Input(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Enter });
            if (changed != 1 || quantityTransfers.Item(item.Base)!.Count != 2 || player.Item(item.Base)!.Count != beforeQuantity + 2)
                throw new InvalidDataException("A stale quantity choice transferred unavailable items.");
            ContainerRow().EmitSignal(BaseButton.SignalName.Pressed);
            if (changed != 2 || quantityTransfers.Item(item.Base)!.Count != 1 || player.Item(item.Base)!.Count != beforeQuantity + 3 ||
                menu.GetChildren().OfType<NativeOwnedQuantityMenu>().Any())
                throw new InvalidDataException("A stack below the source threshold did not transfer one item.");
            GD.Print("OPENNV_NATIVE_QUANTITY_UI_PASS sourceTemplates=true cancel=true modal=true partialTransfer=true staleChoice=true smallStack=true coldCounts=true");
            GD.Print("OPENNV_NATIVE_INTERACTION_UI_PASS sourceTemplates=true targetActions=true containerLists=true pickupState=true ordinaryInput=unverified pixels=unverified");
            menu.Free(); hud.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"OPENNV_NATIVE_INTERACTION_UI_FAIL {error}"); GetTree().Quit(1); }
    }
}
