using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeCharacterCreationAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2 || !Path.IsPathFullyQualified(args[1]) || Path.GetFullPath(args[1]).StartsWith(
                Path.GetFullPath(ProjectSettings.GlobalizePath("res://../")), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Owned data and a private evidence directory are required.");
            Directory.CreateDirectory(args[1]);
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var contract = FalloutNativeRaceSexResolver.Resolve(records);
            var entry = new RuntimeNativeRaceSexEntry(); AddChild(entry);
            entry.Configure(contract, contract.Initial, records);
            var warmup = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - warmup < 600)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            foreach (var page in Enumerable.Range(0, entry.Creation.Headers.Count))
            {
                entry.SelectPage(page);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
                using var pixels = GetViewport().GetTexture().GetImage();
                if (pixels.SavePng(Path.Combine(args[1], $"page-{page:00}.png")) != Error.Ok) throw new IOException("Creation capture failed.");
                GD.Print($"OPENNV_CREATION_PAGE_RENDERED page={page} label={entry.Creation.Header(page)} source=owned-menu ordinaryProgression=false parity=unverified");
            }
            File.WriteAllText(Path.Combine(args[1], "state.json"), System.Text.Json.JsonSerializer.Serialize(entry.State));
            void Key(Godot.Key key)
            {
                foreach (var pressed in new[] { true, false }) GetViewport().PushInput(
                    new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            }
            entry.SelectPage(12);
            var before = entry.Creation.Selection.Face!.SymmetricGeometry.ToArray();
            Key(Godot.Key.Left);
            if (before.SequenceEqual(entry.Creation.Selection.Face!.SymmetricGeometry)) throw new InvalidOperationException("The original shape slider did not edit player coefficients through input.");
            entry.SelectPage(6); Key(Godot.Key.Right);
            if (entry.Creation.HairPresetIndex != 1) throw new InvalidOperationException("Original palette input did not select the source setting.");
            Key(Godot.Key.Down);
            var red = entry.Creation.Selection.Face!.HairColor[0]; Key(red == 255 ? Godot.Key.Left : Godot.Key.Right);
            if (entry.Creation.Selection.Face!.HairColor[0] == red) throw new InvalidOperationException("Original RGB input did not change player colour.");
            var saved = System.Text.Json.JsonSerializer.Serialize(entry.Creation.Selection);
            var restored = System.Text.Json.JsonSerializer.Deserialize<FalloutNativeRaceSexSelection>(saved)!;
            var reopened = new FalloutNativeCharacterCreation(records, contract, restored, FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!));
            if (!reopened.Appearance().FaceGen.SymmetricGeometry.SequenceEqual(entry.Creation.Selection.Face!.SymmetricGeometry) ||
                !reopened.Appearance().HairColorBytes.SequenceEqual(entry.Creation.Selection.Face.HairColor))
                throw new InvalidOperationException("Reopened source appearance lost saved menu edits.");
            var edited = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - edited < 600) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
            using (var pixels = GetViewport().GetTexture().GetImage()) pixels.SavePng(Path.Combine(args[1], "edited.png"));
            GD.Print("OPENNV_CREATION_INPUT_STATE_PASS sourceShapeSlider=true sourcePalette=true sourceRgbSlider=true reopenedAppearance=true parity=unverified");
            entry.SelectPage(3);
            var done = entry.FindChild("RSM_next_button", true, false) as BaseButton
                ?? throw new InvalidOperationException("Original creation Done control is absent.");
            done.GrabFocus(); Key(Godot.Key.Enter);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
            var confirmation = entry.GetChildren().OfType<NativeOwnedMessageMenu>().Single();
            var choices = confirmation.GetChildren().OfType<NativeBitmapMenuButton>().ToArray();
            if (!confirmation.Visible || choices.Length != 2 || choices.Any(button => button.Disabled || button.Size.Y <= 0))
                throw new InvalidOperationException("Original creation confirmation lost its two usable choices.");
            using (var pixels = GetViewport().GetTexture().GetImage()) pixels.SavePng(Path.Combine(args[1], "confirmation.png"));
            var accepted = false;
            entry.Accepted += _ => accepted = true;
            choices.Single(button => button.Text == FalloutGameSettingStrings.Read(records, "sYes")).GrabFocus();
            Key(Godot.Key.Enter);
            if (!accepted) throw new InvalidOperationException("Original confirmation input did not accept the source character.");
            GD.Print("OPENNV_CREATION_CONFIRMATION_PASS originalDialog=true twoChoices=true keyboardAcceptance=true parity=unverified");
            GD.Print("OPENNV_CREATION_PAGE_AUDIT_PASS acceptedGameplay=false parity=unverified");
            entry.ReleasePause(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"OPENNV_CREATION_PAGE_AUDIT_FAIL {error}"); GetTree().Quit(1); }
    }
}
