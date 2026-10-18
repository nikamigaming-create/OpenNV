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
        var exitCode = 0;
        var inputOnly = OS.GetCmdlineUserArgs().SequenceEqual(new[] { "--input-lifetime" });
        var catalogOnly = OS.GetCmdlineUserArgs() is ["--radio-catalog", _];
        try
        {
            if (OS.GetCmdlineUserArgs() is ["--radio-catalog", var data]) InspectRadioCatalog(data);
            else if (inputOnly) await ExerciseNativePointerCancellation();
            else
            {
                await ExerciseOwnedPages();
                await ExerciseNativePointerCancellation();
            }
        }
        catch (Exception error) { GD.PushError(error.ToString()); exitCode = 1; }
        try
        {
            RuntimeLiveContentSource.Clear();
            // Page changes queue old controls for deletion. Let them leave the tree
            // and release the completed audit's managed resource wrappers while
            // Godot's native object registry is still alive.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GC.Collect(); GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (Exception error) { GD.PushError("OPENNV_PIPBOY_AUDIT_CLEANUP_FAILED " + error); exitCode = 1; }
        if (exitCode == 0)
            GD.Print(catalogOnly ? "OPENNV_RADIO_CATALOG_COMPLETE playback=unverified" :
                inputOnly ? "OPENNV_NATIVE_PIPBOY_INPUT_LIFETIME_AUDIT_PASS cleanup=complete recording=off" :
                "OPENNV_NATIVE_PIPBOY_AUDIT_PASS source-menus=true saved-items-quests-markers=true pointer-cancellation=true cleanup=complete recording=off visual-and-input-acceptance=separate");
        GetTree().Quit(exitCode);
    }

    private async Task ExerciseOwnedPages()
    {
        NativeOwnedPipBoyMenu? menu = null;
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
            var content = RuntimeLiveContentSource.Current!;
            var childRoot = System.Xml.Linq.XElement.Parse("<menu name='Probe'><image name='divider'><alpha><copy src='child()' trait='alpha'/></alpha><rect name='meter'><alpha>123</alpha></rect></image></menu>");
            var childTree = new NativeOwnedMenuTree(childRoot);
            if (childTree.Number(childRoot.Element("image")!, "alpha") != 123)
                throw new InvalidDataException("Unnamed child reference did not resolve its unique named tile.");
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var sourceMarkers = await Task.Run(() => FalloutWorldMap.SourceMarkers(records));
            if (!ReferenceEquals(sourceMarkers, FalloutWorldMap.SourceMarkers(records)))
                throw new InvalidDataException("Prepared map declarations were rebuilt on the UI thread.");
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
            using var references = new FalloutReferenceWorld(records);
            references.Restore(saved.References!);
            var inventory = new FalloutPlayerInventory();
            var items = FalloutCampaignInventoryResolver.Resolve(records, saved.Inventory.Select(item =>
                new FalloutCampaignInventoryRequest(item.RuntimeFormId, item.EditorId, item.RecordType, item.Count)).ToArray(), null);
            inventory.Restore(items, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var state = new FalloutPipBoyState(records, inventory); state.SetOpen(true);
            menu = new NativeOwnedPipBoyMenu(records, state, inventory, quests, references, () => saved.Vitals!, () => saved.Special,
                saved.PlayerName, FalloutNativeTagSkillResolver.Resolve(records, FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"])).Skills, saved.TagSkills, saved.Traits,
                FalloutCellSceneReader.ParentWorldspace(records.GetEffective(saved.ActiveCell)),
                new(saved.PlayerPosition[0] / .0142875f, -saved.PlayerPosition[2] / .0142875f, saved.PlayerPosition[1] / .0142875f), 0)
            { LocalMap = color => new NativeOwnedLocalMap(GetViewport().World3D, Vector3.Zero, false, color, 0) };
            AddChild(menu);
            var failures = new List<string>();
            var selections = new[] { (FalloutPipBoyPage.Stats, 0), (FalloutPipBoyPage.Stats, 1),
                (FalloutPipBoyPage.Stats, 2), (FalloutPipBoyPage.Stats, 3), (FalloutPipBoyPage.Stats, 4),
                (FalloutPipBoyPage.Items, 0), (FalloutPipBoyPage.Items, 1), (FalloutPipBoyPage.Items, 2),
                (FalloutPipBoyPage.Items, 3), (FalloutPipBoyPage.Items, 4), (FalloutPipBoyPage.Data, 1), (FalloutPipBoyPage.Data, 2),
                (FalloutPipBoyPage.Data, 3), (FalloutPipBoyPage.Data, 0) };
            foreach (var (page, index) in selections)
            {
                menu.Select(page, index);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (menu.Error is { } error) { failures.Add($"{page}/{index}: {error}"); continue; }
                var children = menu.GetChildren();
                var buttons = children.OfType<BaseButton>().Where(button => button.Visible).ToArray();
                if (buttons.Any(button => button.Size.X <= 0 || button.Size.Y <= 0)) failures.Add($"{page}/{index}: empty button target");
                GD.Print($"OPENNV_PIPBOY_PAGE_BOUND {page}/{index} buttons={buttons.Length} refreshMs={JsonSerializer.SerializeToElement(menu.State).GetProperty("refreshMilliseconds").GetDouble():F3}");
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            await ExerciseNavigation(menu, records, inventory);
        }
        finally
        {
            if (IsInstanceValid(menu)) menu!.Free();
            menu?.Dispose();
        }
    }
}
