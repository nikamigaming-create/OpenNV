using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Tools;

public sealed partial class ClassicPortraitAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Provide owned New Vegas Data and a private visual diagnostic directory.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var contract = FalloutNativeRaceSexResolver.Resolve(records);
            var entry = new RuntimeNativeRaceSexEntry(); AddChild(entry);
            entry.Configure(contract, contract.Female, records);
            entry.EnableEnvision(ClassicPortraitMode.Illustrated);
            Directory.CreateDirectory(args[1]);
            async Task Capture(string name)
            {
                for (var frame = 0; frame < 6; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
                using var pixels = GetViewport().GetTexture().GetImage();
                if (pixels.SavePng(Path.Combine(args[1], name + ".png")) != Error.Ok) throw new IOException("Portrait capture failed.");
            }
            await Capture("illustrated");
            var unchanged = JsonSerializer.Serialize(entry.Creation.Selection);
            var originalActor = entry.Portrait.Actor;
            var button = entry.FindChild("Envision", true, false) as Button ?? throw new InvalidOperationException("Envision control is absent.");
            void ClickEnvision()
            {
                var point = button.GetGlobalRect().GetCenter();
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
            ClickEnvision();
            if (entry.PortraitMode != ClassicPortraitMode.Live3D || entry.Portrait.Actor != originalActor ||
                JsonSerializer.Serialize(entry.Creation.Selection) != unchanged)
                throw new InvalidOperationException("Envision replaced or changed the character.");
            await Capture("live3d");
            using (var raw = entry.Portrait.View.GetTexture().GetImage()) raw.SavePng(Path.Combine(args[1], "raw-live3d.png"));
            var control = entry.Creation.Controls.Controls.First(row => row.Page == 12);
            var range = entry.Creation.Limits(control); var value = entry.Creation.Value(control);
            entry.Creation.SetControl(control, value < range.Maximum ? value + 1 : value - 1);
            entry.Creation.SetHairComponent(0, entry.Creation.Selection.Face!.HairColor[0] == 0 ? 30 : 0);
            await Capture("edited-live3d");
            var edited = JsonSerializer.Serialize(entry.Creation.Selection);
            ClickEnvision();
            await Capture("edited-illustrated");
            if (edited != JsonSerializer.Serialize(entry.Creation.Selection) || entry.PortraitMode != ClassicPortraitMode.Illustrated)
                throw new InvalidOperationException("Edited character identity changed during projection.");
            var path = Path.Combine(args[1], "temporary-appearance.json");
            try
            {
                new ClassicAppearanceDraft(ClassicAppearanceDraft.CurrentSchema, "fallout-1", "fixture-profile", content.StackId, entry.Creation.Selection).Save(path);
                var restored = ClassicAppearanceDraft.Read(path, "fallout-1", "fixture-profile", content.StackId)!;
                var reopened = new FalloutNativeCharacterCreation(records, contract, restored.Character, FalloutInstallationSettings.Read(content));
                if (!reopened.Appearance().FaceGen.SymmetricGeometry.SequenceEqual(entry.Creation.Appearance().FaceGen.SymmetricGeometry) ||
                    JsonSerializer.Serialize(reopened.Selection) != edited)
                    throw new InvalidOperationException("Cold appearance restoration changed the edited character.");
                try { _ = ClassicAppearanceDraft.Read(path, "fallout-2", "fixture-profile", content.StackId); throw new Exception("Cross-campaign appearance was accepted."); }
                catch (InvalidDataException) { }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            GD.Print("OPENNV_CLASSIC_PORTRAIT_AUDIT_PASS sameActor=true sameFaceBytes=true customEdits=true coldAppearanceRestore=true campaignIsolation=true likeness=unverified classicGameplayHandoff=unbound");
            entry.ReleasePause(); RemoveChild(entry); entry.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
