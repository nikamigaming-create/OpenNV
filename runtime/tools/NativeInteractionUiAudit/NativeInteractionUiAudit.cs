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
            if (args is not [var root, var cellId]) throw new ArgumentException("Expected owned root and CELL editor ID.");
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
            GD.Print("OPENNV_NATIVE_INTERACTION_UI_PASS sourceTemplates=true targetActions=true containerLists=true pickupState=true ordinaryInput=unverified pixels=unverified");
            menu.Free(); hud.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"OPENNV_NATIVE_INTERACTION_UI_FAIL {error}"); GetTree().Quit(1); }
    }
}
