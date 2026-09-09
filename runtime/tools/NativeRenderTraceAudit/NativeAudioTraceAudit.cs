using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Diagnostics.Parity;

public partial class NativeAudioTraceAudit : Node
{
    public override async void _Ready()
    {
        var bus = -1;
        try
        {
            using var directory = new TemporaryCaptureDirectory(Path.Combine(Path.GetTempPath(), "opennv-audio-audit-" + Guid.NewGuid()));
            var blobs = new RenderTraceBlobStore(Path.Combine(directory.Path, "blobs"));
            bus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(bus, "OpenNVAudioTraceAudit");
            AudioServer.SetBusVolumeDb(bus, -60);
            var originalMasterEffects = AudioServer.GetBusEffectCount(0);
            using (var canceled = new RuntimeAudioTrace()) { }
            if (AudioServer.GetBusEffectCount(0) != originalMasterEffects || AudioServer.GetBusEffectCount(bus) != 0)
                throw new InvalidDataException("Canceled audio observation left capture effects attached.");
            using var trace = new RuntimeAudioTrace(.1f);
            var generator = new AudioStreamGenerator { MixRate = AudioServer.GetMixRate(), BufferLength = 1 };
            var voice = new AudioStreamPlayer { Stream = generator, Bus = "OpenNVAudioTraceAudit" };
            AddChild(voice); voice.Play();
            var playback = (AudioStreamGeneratorPlayback)voice.GetStreamPlayback();
            var input = Enumerable.Repeat(new Vector2(.03125f, -.0625f), (int)(AudioServer.GetMixRate() * .7)).ToArray();
            if (!playback.PushBuffer(input)) throw new InvalidDataException("Synthetic voice buffer was rejected.");
            // Deliberately occupy the main thread beyond the selected capture
            // buffer while the independent audio reader must keep every sample.
            Thread.Sleep(650);
            await ToSignal(GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
            var missing = new List<string>();
            using var report = JsonDocument.Parse(JsonSerializer.Serialize(trace.Finish(blobs, missing)));
            if (missing.Count != 0) throw new InvalidDataException(string.Join(';', missing));
            var observed = report.RootElement.GetProperty("buses").EnumerateArray()
                .Single(row => row.GetProperty("bus").GetString() == "OpenNVAudioTraceAudit");
            var samples = File.ReadAllBytes(observed.GetProperty("samples").GetProperty("File").GetString()!);
            if (samples.Length == 0 || samples.Length % 8 != 0) throw new InvalidDataException("Audio trace lost stereo frame structure.");
            var matched = 0;
            for (var i = 0; i < samples.Length; i += 8)
                if (BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(i)) == .03125f &&
                    BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(i + 4)) == -.0625f) matched++;
            if (matched != input.Length) throw new InvalidDataException($"Only {matched}/{input.Length} exact channel frames survived the synthetic voice.");
            if (AudioServer.GetBusEffectCount(0) != originalMasterEffects || AudioServer.GetBusEffectCount(bus) != 0 ||
                AudioServer.GetBusVolumeDb(bus) != -60)
                throw new InvalidDataException("Audio observation changed routing, gain or effects after completion.");
            voice.Stop(); voice.Free();
            GD.Print($"OPENNV_NATIVE_AUDIO_TRACE_AUDIT_PASS exactStereoFrames={matched} loss=0 cancellationCleanup=true completionCleanup=true endpointParity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { if (bus >= 0) AudioServer.RemoveBus(bus); }
    }
}
