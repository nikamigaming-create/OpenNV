using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Tools;

public sealed partial class ClassicStudioAudit : Node
{
    public override async void _Ready()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-classic-studio-" + Guid.NewGuid().ToString("N"));
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is not (4 or 5) || args.Length == 5 && args[4] != "--walk")
                throw new ArgumentException("Provide classic install, New Vegas Data, campaign, private output directory and optional --walk.");
            var campaign = args[2]; var save = Path.Combine(temporary, "campaign.json");
            if (campaign == "fallout-1")
            {
                var browser = new ClassicWorldBrowser(); AddChild(browser);
                browser.Configure(Fallout1OwnedContentSource.LoadInstall(args[0]), save, args[1]);
            }
            else if (campaign == "fallout-2")
            {
                var host = new Fo2CharacterStartHost(); host.Configure(args[0], save, args[1]); AddChild(host);
            }
            else throw new ArgumentException("Studio audit campaign must be classic Fallout.");
            async Task Settle()
            {
                for (var frame = 0; frame < 6; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            void Click(Control control)
            {
                var point = control.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        Position = point,
                        GlobalPosition = point,
                        ButtonIndex = MouseButton.Left,
                        Pressed = pressed
                    }, true);
            }
            void Key(Godot.Key key)
            {
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            }
            await Settle();
            if (args.Length == 5)
            {
                var choose = FindChild("ClassicCharacterSelection", true, false) as Button
                    ?? throw new InvalidOperationException("Source character selection is absent.");
                Click(choose); await Settle();
                var selection = FindChild("SaveCharacterChoice", true, false) as Button
                    ?? throw new InvalidOperationException("Source premade acceptance is absent.");
                Click(selection); await Settle();
                var begin = FindChild("ClassicEnterWorld", true, false) as Button
                    ?? throw new InvalidOperationException("Native campaign entry is absent.");
                if (begin.Disabled) throw new InvalidOperationException("Source character was not accepted.");
                var preview = FindChildren("*", "", true, false).OfType<ClassicWorldPreview>().Single();
                Click(begin); await Settle();
                var controller = FindChildren("*", "", true, false).OfType<ClassicPlayerController>().Single();
                var player = controller.Player;
                var world = controller.GetParent<ClassicWorldPreview>();
                if (preview.SourceSha256 == player.Level.Sha256 &&
                    preview.GetMeta("source_elevation").AsInt32() == player.Elevation && !ReferenceEquals(preview, world))
                    throw new InvalidOperationException("Begin duplicated its matching source preview world.");
                var origin = player.Tile;
                var blocked = world.Navigation.Blocked.Order().ToArray();
                var candidates = ClassicHexGrid.Neighbors(origin).Where(player.Walkable.Contains)
                    .Select(tile => new { Tile = tile, Point = world.Camera.UnprojectPosition(Fo1HexMath.Center(tile)) })
                    .Where(row => GetViewport().GetVisibleRect().Grow(-16).HasPoint(row.Point) &&
                        row.Point.Y < GetViewport().GetVisibleRect().Size.Y - controller.Hud.Size.Y * controller.Hud.Scale.Y &&
                        ClassicWorldPicking.Item(world, player, row.Point) is null).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException("No visible unobstructed adjacent source floor for ordinary pointer movement.");
                var destination = candidates[0];
                GetViewport().PushInput(new InputEventMouseMotion { Position = destination.Point, GlobalPosition = destination.Point }, true);
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        Position = destination.Point,
                        GlobalPosition = destination.Point,
                        ButtonIndex = MouseButton.Left,
                        Pressed = pressed
                    }, true);
                var until = Time.GetTicksMsec() + 8000;
                while (player.Tile != destination.Tile || player.Moving)
                {
                    if (Time.GetTicksMsec() >= until) throw new InvalidOperationException("Ordinary floor click did not complete its source hex movement: " + player.Status);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                if (!blocked.SequenceEqual(world.Navigation.Blocked.Order()) || player.CompletedSteps == 0)
                    throw new InvalidOperationException("Movement changed the source blocking field or skipped the native step owner.");
                await Settle(); Directory.CreateDirectory(args[3]);
                using (var image = GetViewport().GetTexture().GetImage())
                    if (image.SavePng(Path.Combine(args[3], campaign + "-walk.png")) != Error.Ok)
                        throw new IOException("Native walk image could not be saved.");
                GD.Print($"OPENNV_CLASSIC_NATIVE_WALK_PASS campaign={campaign} sourceStart=true sourcePremade=true pointer=true from={origin} to={player.Tile} steps={player.CompletedSteps} blocking=unchanged presentationFailures={world.Unresolved.Count} campaignComplete=false");
                GetTree().Quit(); return;
            }
            var open = FindChild("ClassicCharacterStudio", true, false) as Button ?? throw new InvalidOperationException("World appearance button is missing.");
            Click(open); await Settle();
            var entry = GetTree().Root.FindChild("NativeRaceSexEntry", true, false) as RuntimeNativeRaceSexEntry
                ?? throw new InvalidOperationException("World input did not open the shared creator.");
            if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
            var expectedMode = campaign == "fallout-1" ? ClassicPortraitMode.Illustrated : ClassicPortraitMode.Live3D;
            if (entry.PortraitMode != expectedMode || !GetTree().Paused)
                throw new InvalidOperationException("Classic creator style or pause ownership is wrong.");
            entry.SelectPage(12); Key(Godot.Key.Left); await Settle();
            var selected = JsonSerializer.Serialize(entry.Creation.Selection);
            Directory.CreateDirectory(args[3]);
            using (var image = GetViewport().GetTexture().GetImage())
                if (image.SavePng(Path.Combine(args[3], campaign + "-studio.png")) != Error.Ok) throw new IOException("Studio capture failed.");
            entry.SelectPage(3);
            var done = entry.FindChild("RSM_next_button", true, false) as BaseButton ?? throw new InvalidOperationException("Done is missing.");
            done.GrabFocus(); Key(Godot.Key.Enter); await Settle();
            var confirmation = entry.GetChildren().OfType<NativeOwnedMessageMenu>().Single();
            // The source Yes choice is the second original confirmation button.
            var choices = confirmation.GetChildren().OfType<NativeBitmapMenuButton>().ToArray();
            if (choices.Length != 2) throw new InvalidOperationException("Character confirmation is incomplete.");
            choices[1].GrabFocus(); Key(Godot.Key.Enter); await Settle();
            if (GetTree().Paused || File.Exists(save)) throw new InvalidOperationException("Appearance acceptance changed campaign progression or left it paused.");
            if (!File.Exists(save + ".appearance.json")) throw new InvalidOperationException("Appearance acceptance did not save the draft.");
            Click(open); await Settle();
            var reopened = GetTree().Root.FindChild("NativeRaceSexEntry", true, false) as RuntimeNativeRaceSexEntry
                ?? throw new InvalidOperationException("The saved creator did not reopen.");
            if (reopened.Error is not null || JsonSerializer.Serialize(reopened.Creation.Selection) != selected || reopened.PortraitMode != expectedMode)
                throw new InvalidOperationException("Reopening changed the chosen classic character or its campaign's default presentation.");
            var back = reopened.FindChild("AppearanceBack", true, false) as Button ?? throw new InvalidOperationException("Back is missing.");
            Click(back); await Settle();
            if (GetTree().Paused) throw new InvalidOperationException("Returning to the world did not release the creator pause.");
            GD.Print($"OPENNV_CLASSIC_STUDIO_AUDIT_PASS campaign={campaign} worldButtonMouse=true sourceEditKeyboard=true sourceAcceptKeyboard=true appearanceSaved=true reopenedIdentical=true backMouse=true gameplaySaveWrites=0 likeness=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(temporary).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Studio test cleanup escaped the temporary directory.");
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }
}
