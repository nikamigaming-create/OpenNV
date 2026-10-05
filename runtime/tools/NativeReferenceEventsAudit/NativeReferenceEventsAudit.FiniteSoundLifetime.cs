using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private static byte[] FiniteSoundFixture()
    {
        static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
        var data = new byte[36]; data[0] = 1; data[1] = 10;
        return Record("SOUN", 0xb60, Field("EDID", Text("FixtureFiniteSound")),
            Field("FNAM", Text("fixture/finite.wav")), Field("SNDD", data));
    }

    private async Task FiniteSoundLifetime(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var root = new Node3D(); AddChild(root);
        root.AddChild(new Camera3D { Position = new(2, 4, -4), Current = true });
        using var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = 8000,
            Data = new byte[16000],
            LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
        };
        stream.SetMeta("opennv_owned_media_sha256", Convert.ToHexString(SHA256.HashData(stream.Data)));
        var source = FalloutSoundRecordReader.Read(records, Key(0xb60));
        var random = new FalloutSoundRandomState(123);
        var nativeFinished = 0; var sourceStops = 0; var cancellations = 0;
        var leases = new List<IDisposable>();
        Node3D Emitter(FalloutFormKey reference)
        {
            var node = new Node3D { Position = new(2.5f, 3, -4) };
            node.SetMeta("opennv_reference_form_key", reference.ToString()); root.AddChild(node); return node;
        }
        (AudioStreamPlayer3D Node, FalloutAnimationSoundEvents Ledger, long Generation,
            NativeOwnedFiniteSoundHost.Attachment Attachment) Start(Node3D emitter, FalloutFormKey reference, bool followEmitter = true)
        {
            var ledger = world.Get(reference).AnimationSoundEvents;
            var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], random, true, true);
            var generation = ledger.Begin(records, selected, "Sound: FixtureFiniteSound", true, [source.LogicalPath]);
            ledger.BindMedia(generation, stream.GetMeta("opennv_owned_media_sha256").AsString());
            var node = new AudioStreamPlayer3D { Stream = stream, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled };
            var nativeEmitter = emitter.GetInstanceId();
            var receipt = new OpenNV.Runtime.Content.FalloutFiniteSoundVoice(nativeEmitter, reference,
                generation, source.FormKey, ledger.Events.Last().SoundSha256, selected.Path!, ledger.Events.Last().MediaSha256!);
            NativeOwnedFiniteSoundHost.Attachment? attachment = null; IDisposable? registration = null;
            var settled = false;
            void Release()
            {
                registration?.Dispose(); attachment?.Dispose();
                if (GodotObject.IsInstanceValid(node)) { node.Stream = null; node.QueueFree(); }
            }
            void Cancel()
            {
                if (settled) return; settled = true; cancellations++;
                ledger.Cancel(generation, "Synthetic actual native voice retired.");
                if (GodotObject.IsInstanceValid(node)) node.Stop(); Release();
            }
            attachment = NativeOwnedFiniteSoundHost.Attach(records, ledger, generation, node, emitter,
                FalloutSoundLoop.Read(source), Cancel, () => { }, followEmitter); leases.Add(attachment);
            registration = records.SoundVoices.Register(source.FormKey, followEmitter ? reference : null, "synthetic-native-lifetime", () =>
                GodotObject.IsInstanceValid(node) && node.Playing, () =>
                {
                    if (settled) return; settled = true; sourceStops++;
                    node.Stop(); ledger.Complete(generation, FalloutAnimationSoundEnd.SourceStopped); Release();
                }, Cancel, reference, () => !settled && GodotObject.IsInstanceValid(node) && node.Playing ? receipt : null); leases.Add(registration);
            node.TreeExiting += Cancel;
            node.Finished += () =>
            {
                if (settled) return; settled = true; nativeFinished++;
                ledger.Complete(generation, FalloutAnimationSoundEnd.NativeFinished); Release();
            };
            node.Play(); return (node, ledger, generation, attachment);
        }
        async Task Finish(FalloutAnimationSoundEvents ledger, long generation)
        {
            var timer = Stopwatch.StartNew();
            while (ledger.Events.Single(entry => entry.Generation == generation).End == FalloutAnimationSoundEnd.Active && timer.Elapsed.TotalSeconds < 5)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(ledger.Events.Single(entry => entry.Generation == generation).End == FalloutAnimationSoundEnd.NativeFinished,
                "Synthetic finite source voice did not deliver genuine native Finished.");
        }
        try
        {
            var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901));
            var last = new Vector3(3.25f, 4.5f, -2); emitter.GlobalPosition = last;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            root.RemoveChild(emitter); emitter.Free();
            Require(active.Attachment.EmitterRetired && active.Attachment.Alive && active.Node.IsInsideTree() &&
                active.Node.GlobalPosition == last && active.Attachment.LastRealPosition == last &&
                active.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Active && !active.Ledger.CanCapture &&
                world.PendingAnimationSoundFiniteVoiceWait() is { Count: 1 },
                "Emitter retirement cancelled, completed or moved the actual finite source voice.");
            Reject(() => world.Capture());
            await Finish(active.Ledger, active.Generation);
            var snapshot = JsonSerializer.Serialize(world.Capture());
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshot)!);
            Require(JsonSerializer.Serialize(cold.Capture()) == snapshot && records.SoundVoices.ActiveVoices == 0 &&
                nativeFinished == 1 && sourceStops == 0 && cancellations == 0,
                "Cold settled native audio replayed playback, changed history or invented source stop.");

            var positionEmitter = Emitter(Key(0x901)); var initial = positionEmitter.GlobalPosition;
            var positional = Start(positionEmitter, Key(0x901), followEmitter: false);
            positionEmitter.GlobalPosition += new Vector3(3, 2, 1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            records.SoundVoices.Stop(source.FormKey, Key(0x901));
            Require(!positional.Attachment.FollowEmitter && positional.Node.GlobalPosition == initial &&
                positional.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Active && sourceStops == 0,
                "Positional-only finite voice followed a root or falsely matched a node-reference stop.");
            positionEmitter.Free();
            await Finish(positional.Ledger, positional.Generation);

            var stoppedEmitter = Emitter(Key(0x901)); var stopped = Start(stoppedEmitter, Key(0x901));
            records.SoundVoices.Stop(source.FormKey, Key(0x901));
            Require(stopped.Ledger.Events.Last().End == FalloutAnimationSoundEnd.SourceStopped && sourceStops == 1,
                "Authored StopSound lost its independent native receipt.");

            var invalidEmitter = Emitter(Key(0x900)); var invalid = Start(invalidEmitter, Key(0x900));
            invalid.Node.Free();
            Require(invalid.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Cancelled && !invalid.Ledger.CanCapture,
                "Actual audio-node destruction was mistaken for native Finished.");
            Reject(() => invalid.Ledger.Complete(invalid.Generation, FalloutAnimationSoundEnd.NativeFinished));

            var graphEmitter = Emitter(Key(0x901)); var graph = Start(graphEmitter, Key(0x901));
            var entry = graph.Ledger.Events[0];
            var negative = new FalloutAnimationSoundEvents(Key(0x901));
            var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], new(71), true, true);
            var negativeGeneration = negative.Begin(records, selected, "Sound: FixtureFiniteSound", true, [source.LogicalPath]);
            negative.BindMedia(negativeGeneration, stream.GetMeta("opennv_owned_media_sha256").AsString());
            using var rejected = new AudioStreamPlayer3D { Stream = stream };
            Reject(() => NativeOwnedFiniteSoundHost.Attach(records, negative, negativeGeneration, rejected, graphEmitter,
                new(FalloutSoundLoopMode.Loop, 0, 0), () => { }, () => { }));
            Reject(() => NativeOwnedFiniteSoundHost.Attach(records, negative, long.MaxValue, rejected, graphEmitter,
                new(FalloutSoundLoopMode.None, 0, 0), () => { }, () => { }));
            Reject(() => NativeOwnedFiniteSoundHost.Attach(records, negative, negativeGeneration, rejected, invalidEmitter,
                new(FalloutSoundLoopMode.None, 0, 0), () => { }, () => { }));
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            Reject(() => NativeOwnedFiniteSoundHost.Attach(records, negative, negativeGeneration, rejected, graphEmitter,
                new(FalloutSoundLoopMode.None, 0, 0), () => { }, () => { }));
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
            var mediaHash = stream.GetMeta("opennv_owned_media_sha256").AsString();
            stream.SetMeta("opennv_owned_media_sha256", "missing");
            Reject(() => NativeOwnedFiniteSoundHost.Attach(records, negative, negativeGeneration, rejected, graphEmitter,
                new(FalloutSoundLoopMode.None, 0, 0), () => { }, () => { }));
            stream.SetMeta("opennv_owned_media_sha256", mediaHash);
            Require(graph.Ledger.Events[0] == entry, "Rejected lifetime admission changed prior source history.");
            records.SoundVoices.Retire();
            Require(graph.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Cancelled && sourceStops == 1 &&
                cancellations == 2 && records.SoundVoices.ActiveVoices == 0,
                "Source graph teardown was mistaken for authored StopSound.");
            Require(JsonSerializer.Serialize(cold.Capture()) == snapshot, "Later cancellation rewrote immutable cold history.");
            GD.Print("OPENNV_NATIVE_FINITE_SOUND_LIFETIME_PASS " + JsonSerializer.Serialize(new
            {
                runtimeBuild = typeof(NativeOwnedFiniteSoundHost).Assembly.ManifestModule.ModuleVersionId,
                emitterRetirement = true,
                lastActualPosition = new[] { last.X, last.Y, last.Z },
                nativeFinished,
                sourceStops,
                cancellations,
                activeRefused = true,
                cancelledImmutable = true,
                coldNoReplay = true,
                graphStopDistinct = true,
                recording = false,
                allLedgerFiniteWait = true,
                positionalOnly = true,
                wrongEmitterRefused = true,
                missingMediaRefused = true,
                nativeLoopRefused = true,
                boundary = "synthetic-source-record-and-native-audio-lifetime;owned-source-and-retail-policy-independent"
            }));
        }
        finally
        {
            records.SoundVoices.Retire();
            foreach (var lease in leases) lease.Dispose();
            root.Free();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unfinished or unsupported native source sound was admitted.");
    }
}
