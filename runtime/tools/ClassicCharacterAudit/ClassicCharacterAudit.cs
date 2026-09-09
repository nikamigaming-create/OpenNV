using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Tools;

public sealed partial class ClassicCharacterAudit : Node
{
    public override async void _Ready()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-character-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is not (3 or 4)) throw new ArgumentException("Provide classic install, New Vegas Data, campaign and optionally a private capture directory.");
            var campaign = args[2]; var save = Path.Combine(temporary, "campaign.json");
            Func<string, byte[]> read; string profile; IDisposable? disposable = null;
            if (campaign == "fallout-1")
            {
                var source = Fallout1OwnedContentSource.LoadInstall(args[0]); read = path => source.Read(path).Bytes; profile = source.ProfileId;
            }
            else if (campaign == "fallout-2")
            {
                var source = Fo2NativeOwnedSource.LoadInstall(args[0]); disposable = source; read = path => source.Read(path, out _); profile = source.ProfileId;
            }
            else throw new ArgumentException("Expected a classic campaign.");
            using var lifetime = disposable;
            var premades = ClassicPremadeReader.Load(campaign, read);
            foreach (var premade in premades)
                GD.Print($"OPENNV_CLASSIC_PREMADE id={premade.Id} name={premade.Character.Name} sex={premade.Character.Female} age={premade.Character.Age} special={string.Join(',', premade.Character.Special)} tags={string.Join(',', premade.Character.TaggedSkills)} skillBonuses={string.Join(',', premade.Character.SkillBonuses)} source={premade.GcdSha256}");
            if (DisplayServer.GetName() == "headless") { GetTree().Quit(); return; }
            if (campaign == "fallout-1")
            {
                var browser = new ClassicWorldBrowser(); AddChild(browser);
                browser.Configure(Fallout1OwnedContentSource.LoadInstall(args[0]), save, args[1]);
            }
            else
            {
                var host = new Fo2CharacterStartHost(); host.Configure(args[0], save, args[1]); AddChild(host);
            }
            async Task Settle()
            {
                for (var frame = 0; frame < 6; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            void Click(Control control)
            {
                var point = control.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                foreach (var pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton
                { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            }
            void Key(Godot.Key key)
            {
                foreach (var pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey
                { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            }
            Button Button(string name) => GetTree().Root.FindChild(name, true, false) as Button ?? throw new InvalidOperationException($"Missing {name} button.");
            ClassicCharacterPicker Picker() => GetTree().Root.FindChild("ClassicCharacterPicker", true, false) as ClassicCharacterPicker ?? throw new InvalidOperationException("Mouse input did not open the classic character picker.");
            async Task Capture(string suffix)
            {
                if (args.Length != 4) return;
                await Settle(); Directory.CreateDirectory(args[3]);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(Path.Combine(args[3], campaign + suffix + ".png")) != Error.Ok) throw new IOException("Character capture failed.");
            }
            await Settle(); Click(Button("ClassicCharacterSelection")); await Settle();
            var picker = Picker();
            for (var index = 0; index < 3; index++)
            {
                if (JsonSerializer.Serialize(picker.Character) != JsonSerializer.Serialize(premades[index].Character)) throw new InvalidOperationException("Picker has wrong source character.");
                if (index != 2) { Click(Button("NextCharacter")); await Settle(); }
            }
            await Capture("-original-picker");
            Click(Button("SaveCharacterChoice")); await Settle();
            if (GetTree().Paused || File.Exists(save)) throw new InvalidOperationException("Selecting a character changed progression or left the world paused.");
            var saved = ClassicCharacterDraft.Read(save + ".character.json", campaign, profile, premades);
            if (saved?.PremadeId != "diplomat") throw new InvalidOperationException("The selected premade did not persist.");
            Click(Button("ClassicCharacterSelection")); await Settle(); picker = Picker();
            if (picker.Character.Name != premades[2].Character.Name) throw new InvalidOperationException("The saved premade did not reopen.");
            if (campaign == "fallout-1") { Click(Button("PreviousCharacter")); await Settle(); }
            Click(Button("CustomCharacter")); await Settle();
            var name = picker.FindChild("ClassicCharacterName", true, false) as LineEdit ?? throw new InvalidOperationException("Custom name control is missing.");
            name.GrabFocus(); name.SelectAll();
            foreach (var character in "Avery") GetViewport().PushInput(new InputEventKey { Unicode = character, Pressed = true }, true);
            await Settle();
            if (picker.Character.Name != "Avery") throw new InvalidOperationException("Ordinary character-name editing failed.");
            Click(Button("EditCharacterAppearance")); await Settle();
            var entry = GetTree().Root.FindChild("NativeRaceSexEntry", true, false) as RuntimeNativeRaceSexEntry ?? throw new InvalidOperationException("Custom picker did not open Reflectron.");
            entry.SelectPage(12); Key(Godot.Key.Left); await Settle();
            var approved = JsonSerializer.Serialize(entry.Creation.Selection);
            entry.SelectPage(3);
            var done = entry.FindChild("RSM_next_button", true, false) as BaseButton ?? throw new InvalidOperationException("Done is absent.");
            done.GrabFocus(); Key(Godot.Key.Enter); await Settle();
            var confirmation = entry.GetChildren().OfType<NativeOwnedMessageMenu>().Single();
            confirmation.GetChildren().OfType<NativeBitmapMenuButton>().Last().GrabFocus(); Key(Godot.Key.Enter); await Settle();
            if (JsonSerializer.Serialize(picker.Appearance?.Character) != approved || picker.Portrait is null)
                throw new InvalidOperationException("Approved face did not enter the classic picker unchanged.");
            var mode = campaign == "fallout-1" ? ClassicPortraitMode.Illustrated : ClassicPortraitMode.Live3D;
            if (picker.PortraitMode != mode) throw new InvalidOperationException("Campaign portrait policy differs.");
            if (campaign == "fallout-1")
            {
                var actor = picker.Portrait.Actor.GetInstanceId();
                Click(Button("CharacterEnvision")); await Settle();
                if (picker.PortraitMode != ClassicPortraitMode.Live3D || picker.Portrait.Actor.GetInstanceId() != actor || JsonSerializer.Serialize(picker.Appearance!.Character) != approved)
                    throw new InvalidOperationException("Envision replaced the approved character.");
                Click(Button("CharacterEnvision")); await Settle();
            }
            await Capture("-custom-picker");
            Click(Button("SaveCharacterChoice")); await Settle();
            saved = ClassicCharacterDraft.Read(save + ".character.json", campaign, profile, premades);
            if (saved?.PremadeId is not null || saved?.Character.Name != "Avery" || JsonSerializer.Serialize(saved?.Appearance?.Character) != approved)
                throw new InvalidOperationException("Custom selection did not preserve the approved appearance.");
            if (saved is null) throw new InvalidOperationException("Custom selection was not written.");
            try { saved.Validate(campaign == "fallout-1" ? "fallout-2" : "fallout-1", profile, premades); throw new InvalidOperationException("Cross-campaign choice was admitted."); }
            catch (InvalidDataException) { }
            Click(Button("ClassicCharacterSelection")); await Settle(); picker = Picker();
            if (JsonSerializer.Serialize(picker.Appearance?.Character) != approved || picker.Portrait is null || picker.PortraitMode != mode)
                throw new InvalidOperationException("Cold picker reconstruction changed the approved character.");
            Click(Button("BackToWorld")); await Settle();
            if (GetTree().Paused || File.Exists(save)) throw new InvalidOperationException("Picker left the world paused or wrote a gameplay save.");
            GD.Print($"OPENNV_CLASSIC_CHARACTER_AUDIT_PASS campaign={campaign} premades=3 sourceChoiceSaved=true customPickerFaceExact=true sameActorEnvision=true coldRestoreExact=true campaignIsolation=true ordinaryInput=true campaignProgression=false");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(temporary).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Character audit cleanup escaped its directory.");
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }
}
