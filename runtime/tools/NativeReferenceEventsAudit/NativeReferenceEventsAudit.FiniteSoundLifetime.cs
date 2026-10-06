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
        var spatial = Record("SOUN", 0xb60, Field("EDID", Text("FixtureFiniteSound")),
            Field("FNAM", Text("fixture/finite.wav")), Field("SNDD", data));
        var flatData = (byte[])data.Clone(); flatData[4] = (byte)FalloutSoundFlags.TwoDimensional;
        return spatial.Concat(Record("SOUN", 0xb61, Field("EDID", Text("FixtureFlatFiniteSound")),
            Field("FNAM", Text("fixture/finite.wav")), Field("SNDD", flatData))).ToArray();
    }

    private async Task FiniteSoundLifetime(FalloutPluginStack records, bool completionEdge = false, bool pausedSave = false)
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
        (Node Node, FalloutAnimationSoundEvents Ledger, long Generation,
            NativeOwnedFiniteSoundHost.Attachment Attachment) Start(Node3D emitter, FalloutFormKey reference,
            bool followEmitter = true, bool flat = false, bool suppressSourceFinished = false, FalloutReferenceWorld? sourceWorld = null)
        {
            var ledger = (sourceWorld ?? world).Get(reference).AnimationSoundEvents;
            var descriptor = flat ? FalloutSoundRecordReader.Read(records, Key(0xb61)) : source;
            var selected = FalloutAnimationSound.Select(descriptor, [descriptor.LogicalPath], random, true, true);
            var generation = ledger.Begin(records, selected, "Sound: " + descriptor.EditorId, true, [descriptor.LogicalPath]);
            ledger.BindMedia(generation, stream.GetMeta("opennv_owned_media_sha256").AsString());
            Node node = flat ? new AudioStreamPlayer { Stream = stream } :
                new AudioStreamPlayer3D { Stream = stream, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled };
            var nativeEmitter = emitter.GetInstanceId();
            var receipt = new OpenNV.Runtime.Content.FalloutFiniteSoundVoice(nativeEmitter, reference,
                generation, descriptor.FormKey, ledger.Events.Last().SoundSha256, selected.Path!, ledger.Events.Last().MediaSha256!);
            NativeOwnedFiniteSoundHost.Attachment? attachment = null; IDisposable? registration = null;
            FalloutFiniteSoundCompletionWait? completion = null;
            NativeOwnedFiniteSoundSaveDrain? saveDrain = null;
            var settled = false;
            void Release()
            {
                registration?.Dispose(); attachment?.Dispose();
                if (!GodotObject.IsInstanceValid(node)) return;
                if (node is AudioStreamPlayer3D spatial) spatial.Stream = null;
                else ((AudioStreamPlayer)node).Stream = null;
                node.QueueFree();
            }
            void Stop() { if (node is AudioStreamPlayer3D spatial) spatial.Stop(); else ((AudioStreamPlayer)node).Stop(); }
            void Cancel()
            {
                if (settled) return; settled = true; cancellations++;
                ledger.Cancel(generation, "Synthetic actual native voice retired.");
                if (GodotObject.IsInstanceValid(node)) Stop(); Release();
            }
            attachment = NativeOwnedFiniteSoundHost.Attach(records, ledger, generation, node, emitter,
                FalloutSoundLoop.Read(descriptor), Cancel, () => { }, followEmitter); leases.Add(attachment);
            FalloutFiniteSoundVoice? Pending()
            {
                if (saveDrain is not null) return saveDrain.PendingVoice;
                if (settled || !GodotObject.IsInstanceValid(node) || attachment?.Alive != true || !node.CanProcess()) return null;
                var playback = NativeFinitePlayback(node);
                var paused = node is AudioStreamPlayer3D spatial ? spatial.StreamPaused : ((AudioStreamPlayer)node).StreamPaused;
                return paused || playback is null ? null :
                    completion?.Observe(receipt, node.GetInstanceId(), playback.GetInstanceId(), stream.GetInstanceId(),
                        NativeFinitePlaying(node), node is AudioStreamPlayer3D ? Engine.GetPhysicsFrames() : Engine.GetProcessFrames(), Time.GetTicksMsec());
            }
            registration = records.SoundVoices.Register(descriptor.FormKey, !flat && followEmitter ? reference : null, "synthetic-native-lifetime", () =>
                GodotObject.IsInstanceValid(node) && NativeFinitePlaying(node), () =>
                {
                    if (settled) return; settled = true; sourceStops++;
                    Stop(); ledger.Complete(generation, FalloutAnimationSoundEnd.SourceStopped); Release();
                }, Cancel, reference, Pending, () => completion?.State, () =>
                {
                    var wait = completion ?? throw new NotSupportedException("Fixture has no original observed native playback.");
                    saveDrain = new NativeOwnedFiniteSoundSaveDrain(records, ledger, receipt, node, wait,
                        () => !settled && attachment is { Alive: true }, () => saveDrain = null);
                    return saveDrain;
                }); leases.Add(registration);
            node.TreeExiting += Cancel;
            void Finished()
            {
                if (suppressSourceFinished) return;
                saveDrain?.NativeFinished();
                if (settled) return; settled = true; nativeFinished++;
                ledger.Complete(generation, FalloutAnimationSoundEnd.NativeFinished); Release();
            }
            if (node is AudioStreamPlayer3D positioned) { positioned.Finished += Finished; positioned.Play(); }
            else { var unpositioned = (AudioStreamPlayer)node; unpositioned.Finished += Finished; unpositioned.Play(); }
            if (NativeFinitePlayback(node) is { } initial)
            {
                completion = new(receipt, node.GetInstanceId(), initial.GetInstanceId(), stream.GetInstanceId());
                _ = Pending();
            }
            return (node, ledger, generation, attachment);
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
            if (pausedSave)
            {
                var results = new List<object>();
                foreach (var flat in new[] { false, true })
                    foreach (var alreadyPaused in new[] { false, true })
                    {
                        var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901), flat: flat);
                        results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", alreadyPaused));
                    }
                foreach (var alreadyPaused in new[] { false, true })
                {
                    var emitter = Emitter(Key(0x901));
                    var spatial = Start(emitter, Key(0x901)); var flat = Start(emitter, Key(0x901), flat: true);
                    results.Add(await ProvePausedFiniteSave(records, world, [spatial.Node, flat.Node],
                        "synthetic-native-fixture", alreadyPaused));
                }
                foreach (var alreadyPaused in new[] { false, true })
                {
                    using var orderedWorld = new FalloutReferenceWorld(records);
                    var emitter = Emitter(Key(0x901));
                    var spatial = Start(emitter, Key(0x901), sourceWorld: orderedWorld);
                    var flat = Start(emitter, Key(0x901), flat: true, sourceWorld: orderedWorld);
                    results.Add(await ProvePausedFiniteSave(records, orderedWorld, [spatial.Node, flat.Node],
                        "synthetic-native-fixture", alreadyPaused, orderedSource: true));
                }
                foreach (var failSource in new[] { false, true })
                {
                    using var orderedWorld = new FalloutReferenceWorld(records);
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901), sourceWorld: orderedWorld);
                    results.Add(await ProvePausedFiniteSave(records, orderedWorld, [active.Node], "synthetic-native-fixture", true,
                        expectedDisposition: failSource ? "failed" : "cancelled",
                        afterBegin: failSource ? null : transaction => transaction.Cancel("Actual ordered user cancel."),
                        orderedSource: true, failSourceWriter: failSource));
                    if (!failSource) await Finish(active.Ledger, active.Generation);
                }
                foreach (var flat in new[] { false, true })
                    foreach (var alreadyPaused in new[] { false, true })
                    {
                        var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901), flat: flat);
                        results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", alreadyPaused,
                            expectedDisposition: "cancelled", afterBegin: transaction => transaction.Cancel("Actual player cancel.")));
                        await Finish(active.Ledger, active.Generation);
                    }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901));
                    var unknown = new AudioStreamPlayer { Stream = stream };
                    NativeOwnedSoundVoice.Bind(records, unknown, source.FormKey, () => Key(0x901),
                        "actual-native-opaque-save-fixture", () => unknown.Playing, unknown.Stop);
                    root.AddChild(unknown); unknown.Play();
                    try
                    {
                        results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", true,
                            expectedDisposition: "failed"));
                    }
                    finally { unknown.Free(); }
                    await Finish(active.Ledger, active.Generation);
                }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901));
                    (Node Node, FalloutAnimationSoundEvents Ledger, long Generation,
                        NativeOwnedFiniteSoundHost.Attachment Attachment)? added = null;
                    results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", false,
                        expectedDisposition: "cancelled", afterBegin: _ => added = Start(emitter, Key(0x901), flat: true)));
                    await Finish(active.Ledger, active.Generation);
                    var extra = added ?? throw new InvalidDataException("Native generation-drift fixture never registered its actual new voice.");
                    await Finish(extra.Ledger, extra.Generation);
                }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901));
                    results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", true,
                        expectedDisposition: "failed", independentBlocker: "unowned-actor/movement/conversation/source-continuation"));
                    await Finish(active.Ledger, active.Generation);
                }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901), flat: true);
                    results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", false,
                        expectedDisposition: "failed", maximumWaitMilliseconds: 1));
                    await Finish(active.Ledger, active.Generation);
                }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901));
                    string? changed = null;
                    results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", true,
                        expectedDisposition: "cancelled", afterBegin: _ => changed = "Actual native session/source/death generation changed.",
                        invalidation: () => changed));
                    await Finish(active.Ledger, active.Generation);
                }
                {
                    var emitter = Emitter(Key(0x901)); var active = Start(emitter, Key(0x901), flat: true);
                    var replacement = new AudioStreamWav
                    {
                        Format = stream.Format,
                        MixRate = stream.MixRate,
                        Data = stream.Data,
                        LoopMode = stream.LoopMode
                    };
                    replacement.SetMeta("opennv_owned_media_sha256", stream.GetMeta("opennv_owned_media_sha256"));
                    try
                    {
                        results.Add(await ProvePausedFiniteSave(records, world, [active.Node], "synthetic-native-fixture", false,
                            expectedDisposition: "cancelled", afterBegin: _ => ((AudioStreamPlayer)active.Node).Stream = replacement));
                    }
                    finally
                    {
                        active.Node.Free(); replacement.Dispose();
                    }
                    Require(active.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Cancelled,
                        "Native replacement/retirement was erased or fabricated as Finished.");
                }
                // A new world must not erase the previous world's real cancellation.
                {
                    using var missingWorld = new FalloutReferenceWorld(records);
                    var emitter = Emitter(Key(0x900));
                    var active = Start(emitter, Key(0x900), flat: true, suppressSourceFinished: true, sourceWorld: missingWorld);
                    results.Add(await ProvePausedFiniteSave(records, missingWorld, [active.Node], "synthetic-native-fixture", false,
                        expectedDisposition: "failed"));
                    Require(active.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Active,
                        "Missing source Finished was fabricated instead of retained as refusal.");
                }
                GD.Print("OPENNV_NATIVE_PAUSED_SAVE_TRANSACTION_PASS " + JsonSerializer.Serialize(results));
                return;
            }
            if (completionEdge)
            {
                var proofs = new List<object>();
                foreach (var flat in new[] { false, true })
                {
                    var edgeEmitter = Emitter(Key(0x901)); var edge = Start(edgeEmitter, Key(0x901), flat: flat);
                    proofs.Add(new { flat, proof = await ProveFiniteCompletionEdge(records, world, [edge.Node], "synthetic-native-fixture") });
                }
                Require(nativeFinished == 2 && sourceStops == 0 && cancellations == 0,
                    "Native completion-edge fixture fabricated, repeated or cancelled a completion.");
                GD.Print("OPENNV_NATIVE_FINITE_COMPLETION_EDGE_PASS " + JsonSerializer.Serialize(proofs));
                return;
            }
            var retiredEmitter = Emitter(Key(0x901)); var retiredVoice = Start(retiredEmitter, Key(0x901));
            var last = new Vector3(3.25f, 4.5f, -2); retiredEmitter.GlobalPosition = last;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            root.RemoveChild(retiredEmitter); retiredEmitter.Free();
            Require(retiredVoice.Attachment.EmitterRetired && retiredVoice.Attachment.Alive && retiredVoice.Node.IsInsideTree() &&
                ((AudioStreamPlayer3D)retiredVoice.Node).GlobalPosition == last && retiredVoice.Attachment.LastRealPosition == last &&
                retiredVoice.Ledger.Events.Last().End == FalloutAnimationSoundEnd.Active && !retiredVoice.Ledger.CanCapture &&
                world.PendingAnimationSoundFiniteVoiceWait() is { Count: 1 },
                "Emitter retirement cancelled, completed or moved the actual finite source voice.");
            Reject(() => world.Capture());
            await Finish(retiredVoice.Ledger, retiredVoice.Generation);
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
            Require(!positional.Attachment.FollowEmitter && ((AudioStreamPlayer3D)positional.Node).GlobalPosition == initial &&
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
