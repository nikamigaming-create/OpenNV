using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task ScreenBlood(string baseRoot, string mod, string root, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        SubViewport? view = null;
        var paused = GetTree().Paused;
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Owned blood fixture requires a quest script.");
            var questHash = SHA256.HashData(quest.ReadData()); var scriptHash = SHA256.HashData(script.ReadData());
            var commands = new[] { quest, script }.SelectMany(record => record.ReadSubrecords().Where(field => field.Signature == "SCTX"))
                .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span))).Where(IsBlood).ToArray();
            if (commands.Length == 0) throw new InvalidDataException("Owned blood fixture has no source TriggerScreenBlood command.");
            var command = commands[0];
            var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records), new((_, _) => false,
                _ => throw new InvalidDataException("Blood fixture invented an unrelated presentation effect.")));
            view = new() { Size = new(320, 180), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            view.AddChild(new ColorRect { Size = view.Size, Color = new(.6f, .6f, .6f, 1) });
            var presenter = new NativeOwnedScreenBlood(world.ScreenBlood, source, world.Menus); view.AddChild(presenter);
            GetTree().Paused = true; world.Menus.Publish(false, [1013]);
            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var pixels = view.GetTexture().GetImage();
                if (pixels.IsEmpty()) throw new InvalidOperationException("Blood fixture has no native viewport pixels.");
                return pixels.GetData();
            }
            var before = await Pixels();
            var maximum = records.NumericSettings.IntegerBits("iBloodSplatterMaxCount");
            if (maximum is 0 or > 100) throw new NotSupportedException("This isolated visual audit requires a bounded nonzero source drop cap.");
            var program = FalloutGameModeProgram.Read("begin GameMode\n" + command + "\nend");
            executor.ExecuteProgram(quest, script, program, 0);
            var first = world.ScreenBlood.LastRequest ?? throw new InvalidOperationException("Owned source blood command did not prepare an effect.");
            if (first.Duration <= 0) throw new NotSupportedException("This visual fixture requires a positive owned lifetime.");
            // Repeated source requests are isolated component evidence. They
            // neither advance this quest nor stand in for ordinary gameplay.
            while (world.ScreenBlood.ActiveDrops < maximum)
            {
                var prior = world.ScreenBlood.ActiveDrops; executor.ExecuteProgram(quest, script, program, 0);
                if (world.ScreenBlood.ActiveDrops == prior) throw new InvalidOperationException("Owned blood command stopped before its source cap.");
            }
            var active = await Pixels();
            var changed = before.Where((value, index) => value != active[index]).Count();
            if (changed == 0 || presenter.ActiveGroups == 0 || world.ScreenBlood.LastError is not null)
                throw new InvalidOperationException("Owned blood effect did not change actual native pixels.");
            var retained = world.ScreenBlood.ActiveDrops;
            world.ScreenBlood.Advance(first.Duration * 2, false);
            if (world.ScreenBlood.ActiveDrops != retained) throw new InvalidOperationException("Paused blood lifetime advanced.");
            world.ScreenBlood.Advance(first.Duration * .8, true);
            var fading = await Pixels();
            if (active.AsSpan().SequenceEqual(fading)) throw new InvalidOperationException("Owned blood fade did not change actual pixels.");
            world.ScreenBlood.Advance(first.Duration, true);
            var expired = await Pixels();
            if (world.ScreenBlood.ActiveDrops != 0 || presenter.ActiveGroups != 0 || !before.AsSpan().SequenceEqual(expired))
                throw new InvalidOperationException("Blood expiration retained geometry or failed to restore the underlying canvas.");
            executor.ExecuteProgram(quest, script, program, 0);
            var media = world.ScreenBlood.LastMedia; var unbound = world.ScreenBlood.Unbound;
            presenter.Free();
            if (world.ScreenBlood.ActiveDrops != 0 || !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(questHash) ||
                !SHA256.HashData(script.ReadData()).AsSpan().SequenceEqual(scriptHash))
                throw new InvalidDataException("Blood retirement leaked geometry or changed owned source bytes.");
            var executable = Path.Combine(baseRoot, "FalloutNV.exe");
            var defaults = FalloutExecutableStringTable.ReadBooleanDefaults(executable);
            GD.Print("OPENNV_NATIVE_SCREEN_BLOOD_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                script = script.FormKey,
                command,
                first,
                maximum,
                changed,
                media,
                unbound,
                booleanDefaults = defaults.Count,
                ownedEnableDefault = defaults["bBloodSplatterEnabled:ScreenSplatter"],
                menuClock = true,
                fadePixels = true,
                expiredPixels = true,
                retirement = true,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                boundary = "isolated-owned-command-and-native-pixels;lighting-xr-and-campaign-parity-unverified"
            }));
        }
        finally { view?.Free(); GetTree().Paused = paused; RuntimeLiveContentSource.Clear(); }
        static bool IsBlood(string command) => command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() is "triggerscreenblood" or "tsb";
    }
}
