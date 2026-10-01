using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private async Task Subtitles(string baseRoot, string mod, string root, string topicId, string[] dependencies)
    {
        SubViewport? view = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            var executable = Path.Combine(baseRoot, "FalloutNV.exe"); var hash = SHA256.HashData(File.ReadAllBytes(executable));
            var declaration = FalloutExecutableStringTable.ReadHudSubtitleDeclarations(executable);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var info = topic.Infos.First(item => item.Responses.Count > 0 && item.Responses[0].Text.Length > 0);
            var recordHash = SHA256.HashData(info.Record.ReadData());
            FalloutSpeechSubtitle? subtitle = null;
            view = new() { Size = new(640, 360), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view); view.AddChild(new ColorRect { Size = view.Size, Color = new(.04f, .04f, .04f, 1) });
            var presenter = new NativeOwnedSubtitles(records, () => subtitle, () => true); view.AddChild(presenter);
            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var pixels = view.GetTexture().GetImage();
                if (pixels.IsEmpty()) throw new InvalidOperationException("Subtitle fixture has no native viewport pixels.");
                return pixels.GetData();
            }
            var before = await Pixels();
            subtitle = new(records.RuntimeFormKey(0x14), info.Record.FormKey, topic.Topic.FormKey, info.Responses[0].Text, true);
            presenter.Prepare(subtitle);
            var active = await Pixels(); var changed = before.Where((value, index) => value != active[index]).Count();
            if (changed == 0 || !presenter.Visible || presenter.Error is not null)
                throw new InvalidDataException("Forced owned subtitle did not draw native font pixels.");
            subtitle = subtitle with { Forced = false };
            var unforced = await Pixels();
            var enabled = FalloutInstallationSettings.Read(source).Boolean("GamePlay", "bGeneralSubtitles");
            if (presenter.Visible != enabled || (!enabled && !before.AsSpan().SequenceEqual(unforced)))
                throw new InvalidDataException("Background subtitle ignored its owned general-subtitle setting.");
            subtitle = null; var expired = await Pixels();
            if (presenter.Visible || presenter.Error is not null || !before.AsSpan().SequenceEqual(expired))
                throw new InvalidDataException("Completed subtitle retained pixels or hid a drawing fault.");
            if (!hash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(executable))) ||
                !recordHash.AsSpan().SequenceEqual(SHA256.HashData(info.Record.ReadData())))
                throw new InvalidDataException("Subtitle fixture changed owned source bytes.");
            GD.Print("OPENNV_NATIVE_SUBTITLES_PASS " + JsonSerializer.Serialize(new
            {
                topic = topic.Topic.FormKey,
                info = info.Record.FormKey,
                declaration,
                changed,
                generalEnabled = enabled,
                forced = true,
                retiredPixels = true,
                sourceReadonly = true,
                recording = false,
                retainedFrames = 0,
                boundary = "isolated-owned-HUD-font-pixels;actor-speech-campaign-and-retail-parity-unverified"
            }));
        }
        finally { view?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
