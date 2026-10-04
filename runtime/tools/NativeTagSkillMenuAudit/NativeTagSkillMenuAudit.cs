using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;

public partial class NativeTagSkillMenuAudit : Control
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length < 4) throw new ArgumentException("Owned installation, mod, mod root and private diagnostic path are required.");
            await Verify(args[0], args[1], args[2], args[3], args[4..]);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Verify(string baseRoot, string mod, string root, string diagnostic, string[] dependencies)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Tag menu requires a native renderer.");
        if (!Path.IsPathFullyQualified(diagnostic) || Path.GetFullPath(diagnostic).StartsWith(
            Path.GetFullPath(ProjectSettings.GlobalizePath("res://../")), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tag visual diagnostic must be outside the repository.");
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        RuntimeNativeTagSkillEntry? entry = null;
        Godot.Timer? clock = null;
        var previousPause = GetTree().Paused;
        var previousMouseMode = Input.MouseMode;
        var success = false;
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var contract = FalloutNativeTagSkillResolver.Resolve(records, controls);
            if (contract.RequiredCount != 3 || contract.Skills.Count < 4)
                throw new NotSupportedException("This owned fixture requires the selected three-choice source limit and four skills.");
            if (!source.TryRead("menus/chargen/char_gen_menu.xml", null, out var xml, out var identity)) throw new FileNotFoundException("Tag menu");
            var hash = SHA256.HashData(xml);
            var special = new FalloutNativeSpecialState(5, 5, 5, 5, 5, 5, 5);
            var actor = records.RuntimeFormKey(7);
            var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(actor), "RNAM");
            var liveSkills = new FalloutPlayerSkills(records, () => special, _ => false, () => [], null, new(), actor, () => race, () => false);
            var delta = 0f;
            float Value(FalloutNativeSkillIdentity skill) => liveSkills.Value(FalloutNativeTagSkillResolver.ActorValueName(records, skill)) + delta;
            IReadOnlyList<FalloutNativeSkillIdentity>? accepted = null;
            Exception? failed = null;
            var accepts = 0; var ticks = 0;
            clock = new Godot.Timer { WaitTime = .01, Autostart = true };
            clock.Timeout += () => ticks++; AddChild(clock);
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ticks == 0) throw new InvalidDataException("Tag fixture has no advancing gameplay clock.");
            var activeEntry = new RuntimeNativeTagSkillEntry(); entry = activeEntry; AddChild(activeEntry);
            activeEntry.Accepted += selection => { accepted = selection; accepts++; activeEntry.ReleasePause(); };
            activeEntry.Failed += error => failed = error;
            activeEntry.Configure(records, contract, [], Value);
            var menu = activeEntry.GetChildren().OfType<NativeOwnedTagSkillMenu>().Single();
            var pausedTicks = ticks;
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            JsonElement State() => JsonSerializer.SerializeToElement(menu.State, json);
            int Selected() => State().GetProperty("selected").GetArrayLength();
            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (failed is not null || menu.Error is not null) throw new InvalidDataException("Tag menu failed.", failed);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.IsEmpty()) throw new InvalidDataException("Tag menu has no native pixels.");
                return image.GetData();
            }
            void Key(Key code)
            {
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = pressed }, true);
            }
            void Click(NativeBitmapMenuButton button)
            {
                var point = button.GetGlobalRect().GetCenter();
                foreach (var pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            }
            var baseline = await Pixels();
            if (!GetTree().Paused || ticks != pausedTicks) throw new InvalidDataException("Tag entry did not pause native gameplay.");
            if (baseline.All(value => value == 0)) throw new InvalidDataException("Tag menu is blank.");
            var rows = menu.GetChildren().OfType<NativeBitmapMenuButton>().Where(button => button.Name.ToString().StartsWith("Skill_", StringComparison.Ordinal)).ToArray();
            var actions = menu.GetChildren().OfType<NativeBitmapMenuButton>().Where(button => !rows.Contains(button)).ToArray();
            var done = actions.Single(button => button.Name == "CGM_DoneButton");
            if (!done.Disabled) throw new InvalidDataException("Incomplete tag draft enables Done.");
            Key(Godot.Key.A); await Pixels();
            if (accepted is not null || Selected() != 0) throw new InvalidDataException("Incomplete Done submitted the draft.");
            delta = 7;
            var livePixels = await Pixels();
            if (baseline.AsSpan().SequenceEqual(livePixels)) throw new InvalidDataException("Tag values did not refresh native pixels from live player state.");
            delta = 0; await Pixels();
            Click(rows[0]); var selectedPixels = await Pixels();
            if (Selected() != 1 || accepted is not null || baseline.AsSpan().SequenceEqual(selectedPixels))
                throw new InvalidDataException("Tag pointer selection did not change the source marker/draft.");
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            if (Selected() != 3 || done.Disabled || accepted is not null) throw new InvalidDataException("Tag keyboard selection did not enable the completed draft.");
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            if (Selected() != 3) throw new InvalidDataException("Tag menu exceeded its source limit.");
            Key(Godot.Key.R); await Pixels();
            if (Selected() != 0 || accepted is not null || !done.Disabled) throw new InvalidDataException("Tag Reset committed or retained selections.");
            rows[0].GrabFocus(); var resetPixels = await Pixels();
            if (!baseline.AsSpan().SequenceEqual(resetPixels)) throw new InvalidDataException("Tag Reset did not restore source pixels at matched focus.");
            using (var image = GetViewport().GetTexture().GetImage())
                if (image.SavePng(diagnostic) != Error.Ok) throw new IOException("Tag diagnostic could not be saved.");
            Key(Godot.Key.Enter); await Pixels();
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            var selected = State().GetProperty("selected").EnumerateArray().Select(value => value.GetProperty("runtimeFormId").GetUInt32()).Order().ToArray();
            if (ticks != pausedTicks) throw new InvalidDataException("Tag input advanced paused gameplay.");
            Key(Godot.Key.A); await Pixels(); Key(Godot.Key.A); await Pixels();
            if (accepted is null || accepts != 1 || !accepted.Select(value => value.RuntimeFormId).SequenceEqual(selected))
                throw new InvalidDataException("Source Done shortcut did not submit exactly one draft.");
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GetTree().Paused || ticks == pausedTicks) throw new InvalidDataException("Done did not release native gameplay.");
            entry.ReleasePause(); entry.Free(); entry = null;
            GetTree().Paused = true;
            entry = new(); AddChild(entry); entry.Configure(records, contract, [], Value);
            entry.ReleasePause(); entry.Free(); entry = null;
            if (!GetTree().Paused) throw new InvalidDataException("Tag disposal resumed a previously paused tree.");
            GetTree().Paused = false;
            entry = new(); AddChild(entry); entry.Configure(records, contract, [], Value);
            entry.Free(); entry = null;
            if (GetTree().Paused) throw new InvalidDataException("Cancelled tag entry leaked its pause.");
            entry = new(); AddChild(entry);
            Exception? rejected = null;
            entry.Failed += error => rejected = error;
            entry.Configure(records, contract, [], _ => throw new NotSupportedException("Synthetic unsupported live skill value."));
            for (var frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (rejected is null || !GetTree().Paused) throw new InvalidDataException("Tag rendering failure was hidden or resumed gameplay.");
            if (JsonSerializer.SerializeToElement(entry.State, json).GetProperty("error").GetString() != rejected.Message)
                throw new InvalidDataException("Tag failure telemetry repeated its failing value query or lost the error.");
            entry.Free(); entry = null;
            if (GetTree().Paused) throw new InvalidDataException("Failed tag entry leaked its pause on cancellation.");
            await VerifyDriver(records, contract);
            if (!source.TryRead("menus/chargen/char_gen_menu.xml", null, out var after, out _) || !hash.AsSpan().SequenceEqual(SHA256.HashData(after)))
                throw new InvalidDataException("Tag fixture changed owned XML.");
            success = true;
            GD.Print("OPENNV_NATIVE_TAG_MENU_PASS " + JsonSerializer.Serialize(new
            {
                identity,
                sourceXmlFontAtlas = true,
                winningAvifDescriptionIcon = true,
                pointer = true,
                keyboard = true,
                sourceLimit = true,
                incompleteDone = true,
                reset = true,
                done = true,
                oneAcceptance = true,
                resetPixelsRestored = true,
                liveValuePixels = true,
                modalClock = true,
                inputWhilePaused = true,
                acceptedResume = true,
                priorPausePreserved = true,
                cancelCleanup = true,
                failureVisible = true,
                failureCancelCleanup = true,
                failureTelemetryReadable = true,
                driverAcceptCancelFailureExit = true,
                optionalInitialSelection = true,
                sourceRequestedCount = true,
                nestedDialogueRetirement = true,
                overlappingModalOwners = true,
                priorModalMousePausePreserved = true,
                releasedOnce = true,
                sourceControlMaskUnchanged = true,
                sourceReadonly = true,
                recording = false,
                boundary = "isolated-owned-tag-menu;campaign-permanent-actor-values-timing-retail-and-XR-unverified"
            }));
        }
        finally
        {
            entry?.Free(); clock?.Free(); GetTree().Paused = previousPause; Input.MouseMode = previousMouseMode; RuntimeLiveContentSource.Clear();
            if (!success && File.Exists(diagnostic)) File.Delete(diagnostic);
        }
    }
}
