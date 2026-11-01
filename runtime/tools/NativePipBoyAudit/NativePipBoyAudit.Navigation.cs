using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativePipBoyAudit
{
    private async Task ExerciseNavigation(NativeOwnedPipBoyMenu menu, FalloutPluginStack records, FalloutPlayerInventory inventory)
    {
        // Disposable inventory only: exercise more real owned entries than fit.
        foreach (var item in records.EffectiveRecords("WEAP").Where(item =>
            item.ReadSubrecords().Any(field => field.Signature == "FULL") &&
            FalloutInventoryAccess.CanTransfer(records, item, false)).Take(12))
            inventory.Add(records, item.FormKey, 1, 1, true);
        menu.Select(FalloutPipBoyPage.Items, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var initial = Rows(menu);
        var down = menu.GetChildren().OfType<BaseButton>().Single(button => button.Name == "scrollbar_vert_down");
        if (!down.Visible || down.Size.X <= 0 || down.Size.Y <= 0) throw new InvalidDataException("Long inventory has no clickable source scrollbar.");
        down.EmitSignal(BaseButton.SignalName.Pressed);
        if (Offset(menu) != 1 || Rows(menu).SequenceEqual(initial)) throw new InvalidDataException("Inventory scrollbar did not expose the next owned item.");
        using var wheel = new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true };
        for (var i = 0; i < 10; i++) menu._Input(wheel);
        if (Offset(menu) <= 8 || Rows(menu).Length == 0 || menu.Error is not null)
            throw new InvalidDataException("Inventory entries beyond the first page remain inaccessible.");

        menu.Select(FalloutPipBoyPage.Data, 1);
        var map = menu.GetChildren().OfType<NativeOwnedWorldMap>().Single();
        map.ZoomAt(map.Size / 2, 1);
        using var press = new InputEventMouseButton { Position = map.Size / 2, ButtonIndex = MouseButton.Left, Pressed = true };
        using var drag = new InputEventMouseMotion { Position = map.Size / 2 + new Vector2(30, 20), Relative = new(30, 20), ButtonMask = MouseButtonMask.Left };
        using var release = new InputEventMouseButton { Position = drag.Position, ButtonIndex = MouseButton.Left };
        map._GuiInput(press); map._GuiInput(drag); map._GuiInput(release);
        var expected = map.CaptureView();
        menu.Select(FalloutPipBoyPage.Data, 1);
        map = menu.GetChildren().OfType<NativeOwnedWorldMap>().Single();
        if (map.CaptureView() != expected) throw new InvalidDataException("Map refresh reset pan or zoom.");
        var zoom = menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == "Zoom in");
        zoom.EmitSignal(BaseButton.SignalName.Pressed);
        if (map.CaptureView().Zoom <= expected.Zoom) throw new InvalidDataException("Map zoom action did not change the actual view.");

        menu.Select(FalloutPipBoyPage.Data, 0);
        var localMap = menu.GetChildren().OfType<NativeOwnedLocalMap>().Single();
        var camera = localMap.GetChildren().OfType<SubViewport>().Single().GetCamera3D();
        var span = camera.Size;
        menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == "Zoom out").EmitSignal(BaseButton.SignalName.Pressed);
        var zoomedOut = camera.Size;
        if (zoomedOut <= span) throw new InvalidDataException("Local-map zoom out did not increase the resident-world camera span.");
        menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == "Zoom in").EmitSignal(BaseButton.SignalName.Pressed);
        if (camera.Size >= zoomedOut || !Mathf.IsEqualApprox(camera.Size, span))
            throw new InvalidDataException("Local-map zoom in did not restore the source maximum-zoom camera span.");
        menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == "Zoom in").EmitSignal(BaseButton.SignalName.Pressed);
        if (!Mathf.IsEqualApprox(camera.Size, span)) throw new InvalidDataException("Local-map zoom exceeded the source maximum.");

        // This fixture has no station owner. Scrolling a failed page must not
        // throw through the device input route, and page keys remain usable.
        menu.Select(FalloutPipBoyPage.Data, 4);
        if (menu.Error is null) throw new InvalidDataException("Missing radio owner unexpectedly succeeded.");
        menu._Input(wheel);
        using var stats = new InputEventKey { PhysicalKeycode = Key.F1, Pressed = true };
        menu._Input(stats);
        if (menu.Error is not null) throw new InvalidDataException("Page failure trapped the Pip-Boy input route.");

        var aid = FalloutDialogueTopic.Find(records, "ALCH", "Stimpak");
        inventory.Add(records, aid.FormKey, 1, 1, true);
        menu.Select(FalloutPipBoyPage.Items, 2);
        // This fixture deliberately has no aid owner. The production action
        // failure must stay visible without poisoning page rendering/input.
        var use = menu.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == "Use");
        use.EmitSignal(BaseButton.SignalName.Pressed);
        if (menu.ActionError is null || menu.Error is not null) throw new InvalidDataException("Action failure poisoned the whole Pip-Boy.");
        menu.Select(FalloutPipBoyPage.Stats, 0);
        if (menu.Error is not null || menu.ActionError is not null || Rows(menu).Length != 0 && Offset(menu) != 0)
            throw new InvalidDataException("Pip-Boy could not navigate after a failed action.");
        GD.Print("OPENNV_PIPBOY_NAVIGATION_PASS longOwnedInventory=true clickableScroll=true wheel=true retainedMapView=true explicitZoom=true localMapZoom=true failedPageRecovery=true failedActionRecovery=true");

        static int Offset(NativeOwnedPipBoyMenu value) => JsonSerializer.SerializeToElement(value.State).GetProperty("listOffset").GetInt32();
        static string[] Rows(NativeOwnedPipBoyMenu value) => value.GetChildren().OfType<NativeBitmapMenuButton>()
            .Where(button => button.Visible).Select(button => button.Text).ToArray();
    }
}
