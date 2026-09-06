using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeLoveTesterAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            if (OS.GetCmdlineUserArgs() is not [var root, var cell, var output])
                throw new ArgumentException("LoveTester audit requires owned installation, source cell ID and private output directory.");
            Directory.CreateDirectory(output);
            RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var scene = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cell, 16)));
            var contract = FalloutNativeVigorResolver.Resolve(records, scene);
            var layer = new CanvasLayer();
            var menu = new NativeOwnedLoveTesterMenu(contract, contract.Initial, records)
            {
                LayoutMode = 1,
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            };
            FalloutNativeSpecialState? accepted = null; menu.Accepted += value => accepted = value;
            layer.AddChild(menu); AddChild(layer);
            var animation = menu.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var pages = new List<object>();
            async Task Snapshot(int page)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var targets = menu.Targets;
                if (targets.Any(target => !target.Center.IsFinite() || !target.Bounds.Size.IsFinite()))
                    throw new InvalidDataException("Source Vigor target projection is non-finite.");
                pages.Add(new
                {
                    page,
                    sequence = animation.ActiveSequence,
                    sourceTime = animation.SourceTimeSeconds,
                    points = menu.GetMeta("opennv_love_tester_remaining").AsInt32(),
                    targets = targets.Select(target => new
                    {
                        target.Geometry,
                        x = target.Center.X,
                        y = target.Center.Y,
                        width = target.Bounds.Size.X,
                        height = target.Bounds.Size.Y,
                        target.InFront
                    })
                });
                File.WriteAllText(Path.Combine(output, "pages.json"), JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true }));
                if (DisplayServer.GetName() != "headless" && page is 0 or 1 or 8)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"page-{page}.png"));
                    if (error != Error.Ok) throw new IOException("LoveTester audit image write failed.");
                }
            }
            void Click(string name)
            {
                var target = menu.Targets.Single(target => target.Geometry == name);
                if (!target.InFront || !new Rect2(Vector2.Zero, menu.Size).HasPoint(target.Center) || target.Bounds.Size.X <= 0 || target.Bounds.Size.Y <= 0)
                    throw new InvalidDataException($"Source Vigor target is outside its view: {name} {target.Center}; sequence={animation.ActiveSequence} time={animation.SourceTimeSeconds} processing={animation.IsProcessing()} menuSize={menu.Size}.");
                menu._GuiInput(new InputEventMouseButton { Position = target.Center, ButtonIndex = MouseButton.Left, Pressed = true });
            }
            void FinishTurn()
            {
                animation.SeekSourceTime(animation.SequenceRange(animation.ActiveSequence!).StopTime);
                animation.SetProcess(false); menu._Process(0);
            }
            var openingDeadline = Time.GetTicksMsec() + 10000;
            while (menu.GetMeta("opennv_love_tester_turning").AsBool() && Time.GetTicksMsec() < openingDeadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (menu.GetMeta("opennv_love_tester_turning").AsBool() || menu.GetMeta("opennv_love_tester_page").AsInt32() != 1)
                throw new InvalidDataException("The owned opening sequence did not finish on the first attribute.");
            var initialPoints = menu.GetMeta("opennv_love_tester_remaining").AsInt32();
            menu._UnhandledInput(new InputEventKey { Keycode = Key.Up, Pressed = true });
            if (menu.GetMeta("opennv_love_tester_remaining").AsInt32() != initialPoints - 1)
                throw new InvalidDataException("Attribute Up did not allocate a point.");
            menu._UnhandledInput(new InputEventKey { Keycode = Key.Down, Pressed = true });
            if (menu.GetMeta("opennv_love_tester_remaining").AsInt32() != initialPoints)
                throw new InvalidDataException("Attribute Down did not restore a point.");
            for (var page = 1; page <= contract.Initial.Values.Count; page++)
            {
                if (menu.GetMeta("opennv_love_tester_page").AsInt32() != page) throw new InvalidDataException("Source next-page click did not advance.");
                await Snapshot(page);
                menu._UnhandledInput(new InputEventKey { Keycode = Key.Up, Pressed = true });
                Click("P1_RT_Btn:0"); FinishTurn();
            }
            await Snapshot(8);
            if (menu.GetMeta("opennv_love_tester_remaining").AsInt32() != 0) throw new InvalidDataException("Source controls did not allocate the authored total.");
            Click("Index_StrengthDecrease_Btn:0");
            Click("P1_RT_Btn:0");
            if (accepted is not null) throw new InvalidDataException("Incomplete allocation was accepted.");
            Click("Index_StrengthIncrease_Btn:0");
            for (var page = 7; page >= 0; page--)
            {
                Click("P1_LT_Btn:0"); FinishTurn();
                if (menu.GetMeta("opennv_love_tester_page").AsInt32() != page) throw new InvalidDataException($"Source reverse-page click did not advance to {page}: actual={menu.GetMeta("opennv_love_tester_page")} picked={menu.GetMeta("opennv_love_tester_pointer_geometry")} sequence={animation.ActiveSequence} time={animation.SourceTimeSeconds} turning={menu.GetMeta("opennv_love_tester_turning")} target={menu.Targets.Single(target => target.Geometry == "P1_LT_Btn:0")}.");
                await Snapshot(page);
            }
            for (var page = 1; page <= 8; page++) { Click(page == 1 ? "LookInside_Btn:0" : "P1_RT_Btn:0"); FinishTurn(); }
            Click("P1_RT_Btn:0");
            if (accepted is null) throw new InvalidDataException("Complete source allocation was not accepted.");
            FalloutNativeVigorResolver.Validate(contract, accepted);
            File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(new { pages, accepted, parity = "unverified" }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("OPENNV_LOVE_TESTER_AUDIT_PASS originalModels=true sourceControls=true forwardPages=8 reversePages=8 allocationBounds=true parity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
