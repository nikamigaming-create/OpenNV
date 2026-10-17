using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;

public partial class NativeRenderedMenuAudit
{
    private async Task TraitMenu(string baseRoot, string mod, string root, string[] dependencies)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Trait menu requires a native renderer.");
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        NativeOwnedTraitMenu? menu = null;
        RuntimeNativeTraitEntry? entry = null;
        Godot.Timer? clock = null;
        var previousPause = GetTree().Paused;
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var cell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var contract = FalloutNativeTraitFarewellResolver.Resolve(records, controls, cell);
            if (contract.MaximumTraits != 2 || contract.Traits.Count < 3)
                throw new NotSupportedException("This owned fixture requires the selected two-choice source limit and three traits.");
            if (!source.TryRead("menus/trait_menu.xml", null, out var xml, out var identity)) throw new FileNotFoundException("Trait menu");
            var hash = SHA256.HashData(xml);
            IReadOnlyList<FalloutNativeTraitIdentity>? accepted = null;
            Exception? failed = null;
            var ticks = 0;
            clock = new Godot.Timer { WaitTime = .01, Autostart = true };
            clock.Timeout += () => ticks++;
            AddChild(clock);
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ticks == 0) throw new InvalidDataException("Trait fixture has no advancing gameplay clock.");
            var activeEntry = new RuntimeNativeTraitEntry(); entry = activeEntry; AddChild(activeEntry);
            entry.Accepted += selection => { accepted = selection; activeEntry.ReleasePause(); };
            entry.Failed += error => failed = error;
            entry.Configure(records, contract, []);
            menu = entry.GetChildren().OfType<NativeOwnedTraitMenu>().Single();
            var pausedTicks = ticks;
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            JsonElement State() => JsonSerializer.SerializeToElement(menu.State, json);
            int Selected() => State().GetProperty("selected").GetArrayLength();
            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (failed is not null || menu.Error is not null) throw new InvalidDataException("Trait menu failed.", failed);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.IsEmpty()) throw new InvalidDataException("Trait menu has no native pixels.");
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
            if (!GetTree().Paused || ticks != pausedTicks) throw new InvalidDataException("Trait entry did not pause native gameplay.");
            if (baseline.All(value => value == 0)) throw new InvalidDataException("Trait menu is blank.");
            var rows = menu.GetChildren().OfType<NativeBitmapMenuButton>().Where(button => button.Name.ToString().StartsWith("Trait_", StringComparison.Ordinal)).ToArray();
            Click(rows[0]);
            var selectedPixels = await Pixels();
            if (Selected() != 1 || accepted is not null || baseline.AsSpan().SequenceEqual(selectedPixels))
                throw new InvalidDataException("Trait pointer selection did not change the source marker/draft.");
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            if (Selected() != 2 || accepted is not null) throw new InvalidDataException("Trait keyboard selection did not update the draft.");
            Key(Godot.Key.Down); Key(Godot.Key.Enter); await Pixels();
            if (Selected() != 2) throw new InvalidDataException("Trait menu exceeded its source limit.");
            Key(Godot.Key.R); await Pixels();
            if (Selected() != 0 || accepted is not null) throw new InvalidDataException("Trait Reset committed or retained selections.");
            rows[0].GrabFocus();
            var resetPixels = await Pixels();
            if (!baseline.AsSpan().SequenceEqual(resetPixels)) throw new InvalidDataException("Reset did not restore source pixels at matched focus.");
            Key(Godot.Key.Enter); await Pixels();
            if (ticks != pausedTicks) throw new InvalidDataException("Trait input advanced paused gameplay.");
            var selected = State().GetProperty("selected").EnumerateArray().Select(value => value.GetProperty("runtimeFormId").GetUInt32()).ToArray();
            Key(Godot.Key.A); await Pixels();
            if (accepted is null || !accepted.Select(value => value.RuntimeFormId).SequenceEqual(selected))
                throw new InvalidDataException("Source Done shortcut did not submit the draft.");
            for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GetTree().Paused || ticks == pausedTicks) throw new InvalidDataException("Done did not release native gameplay.");
            entry.ReleasePause(); entry.Free(); entry = null; menu = null;
            GetTree().Paused = true;
            entry = new RuntimeNativeTraitEntry(); AddChild(entry); entry.Configure(records, contract, []);
            entry.ReleasePause(); entry.Free(); entry = null;
            if (!GetTree().Paused) throw new InvalidDataException("Trait disposal resumed a previously paused tree.");
            GetTree().Paused = false;
            entry = new RuntimeNativeTraitEntry(); AddChild(entry); entry.Configure(records, contract, []);
            entry.Free(); entry = null;
            if (GetTree().Paused) throw new InvalidDataException("Trait tree exit leaked its pause.");
            var zero = new FalloutTraitMenuSelection(records, contract, []); zero.Reset();
            if (zero.Submit().Count != 0) throw new InvalidDataException("Trait menu rejected zero selections.");
            if (!source.TryRead("menus/trait_menu.xml", null, out var after, out _) || !hash.AsSpan().SequenceEqual(SHA256.HashData(after)))
                throw new InvalidDataException("Trait fixture changed owned XML.");
            GD.Print("OPENNV_NATIVE_TRAIT_MENU_PASS " + JsonSerializer.Serialize(new
            {
                identity,
                sourceXmlFontAtlas = true,
                sourcePerkDescriptionIcon = true,
                pointer = true,
                keyboard = true,
                sourceLimit = true,
                reset = true,
                done = true,
                resetPixelsRestored = true,
                zeroSelection = true,
                modalClock = true,
                inputWhilePaused = true,
                acceptedResume = true,
                priorPausePreserved = true,
                exitCleanup = true,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                boundary = "isolated-owned-trait-menu;campaign-native-timing-retail-and-XR-unverified"
            }));
        }
        finally { entry?.Free(); clock?.Free(); GetTree().Paused = previousPause; RuntimeLiveContentSource.Clear(); }
    }
}
