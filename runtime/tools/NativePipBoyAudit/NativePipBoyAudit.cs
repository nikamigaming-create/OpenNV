using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

// Read a real checkpoint into disposable owners. Never writes that checkpoint.
public partial class NativePipBoyAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var cropped = NativeUiImage.Sample(new(0, 0, 123.25f, 133.4f), new(0, 0, 128, 128), 145, Vector2.Zero)!.Value;
            if (!cropped.Source.Size.IsEqualApprox(new(85, 92))) throw new InvalidOperationException("Zoomed status art was stretched instead of cropped.");
            var shifted = NativeUiImage.Sample(new(10, 20, 60, 40), new(100, 200, 128, 128), 200, new(20, 10))!.Value;
            if (!shifted.Source.Position.IsEqualApprox(new(110, 205)) || !shifted.Source.Size.IsEqualApprox(new(30, 20)))
                throw new InvalidOperationException("Atlas crop/zoom lost source coordinates.");
            var stretched = NativeUiImage.Sample(new(0, 0, 400, 200), new(0, 0, 128, 64), -1, Vector2.Zero)!.Value;
            if (stretched.Source.Size != new Vector2(128, 64) || NativeUiImage.Sample(new(0, 0, 1, 1), new(0, 0, 1, 1), 0, Vector2.Zero) is not null)
                throw new InvalidOperationException("Explicit UI stretch/hide semantics failed.");
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Expected owned Data and a native campaign checkpoint.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
            using var references = new FalloutReferenceWorld(records);
            references.Restore(saved.References!);
            var inventory = new FalloutPlayerInventory();
            var items = FalloutCampaignInventoryResolver.Resolve(records, saved.Inventory.Select(item =>
                new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray(), null);
            inventory.Restore(items, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var state = new FalloutPipBoyState(records, inventory); state.SetOpen(true);
            var menu = new NativeOwnedPipBoyMenu(records, state, inventory, quests, references, () => saved.Vitals!, () => saved.Special,
                saved.PlayerName, FalloutNativeTagSkillResolver.Resolve(records, FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"])).Skills, saved.TagSkills, saved.Traits,
                FalloutCellSceneReader.ParentWorldspace(records.GetEffective(saved.ActiveCell)),
                new(saved.PlayerPosition[0] / .0142875f, -saved.PlayerPosition[2] / .0142875f, saved.PlayerPosition[1] / .0142875f), 0);
            AddChild(menu);
            var failures = new List<string>();
            var selections = new[] { (FalloutPipBoyPage.Stats, 0), (FalloutPipBoyPage.Stats, 1),
                (FalloutPipBoyPage.Stats, 2), (FalloutPipBoyPage.Stats, 3),
                (FalloutPipBoyPage.Items, 0), (FalloutPipBoyPage.Items, 1), (FalloutPipBoyPage.Items, 2),
                (FalloutPipBoyPage.Items, 3), (FalloutPipBoyPage.Items, 4), (FalloutPipBoyPage.Data, 1), (FalloutPipBoyPage.Data, 2) };
            foreach (var (page, index) in selections)
            {
                menu.Select(page, index);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (menu.Error is { } error) { failures.Add($"{page}/{index}: {error}"); continue; }
                var buttons = menu.GetChildren().OfType<BaseButton>().Where(button => button.Visible).ToArray();
                if (buttons.Any(button => button.Size.X <= 0 || button.Size.Y <= 0)) failures.Add($"{page}/{index}: empty button target");
                GD.Print($"OPENNV_PIPBOY_PAGE_BOUND {page}/{index} buttons={buttons.Length}");
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            GD.Print("OPENNV_NATIVE_PIPBOY_AUDIT_PASS source-menus=true saved-items-quests-markers=true recording=off visual-and-input-acceptance=separate");
            menu.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
