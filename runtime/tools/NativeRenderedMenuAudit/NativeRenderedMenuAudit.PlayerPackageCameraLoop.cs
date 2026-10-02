using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task PlayerPackageCameraLoop(string baseRoot, string mod, string root,
        string initialId, string nextId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        RuntimeNativePlayer? player = null, coldPlayer = null;
        AudioEffectCapture? capture = null;
        var previousObserver = NativeOwnedAnimationSoundPlayer.SoundObserver;
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var initialRecord = FalloutDialogueTopic.Find(records, "PACK", initialId);
            var nextRecord = FalloutDialogueTopic.Find(records, "PACK", nextId);
            var initial = FalloutScriptPackage.Read(initialRecord);
            var next = FalloutScriptPackage.Read(nextRecord);
            var idle = initial.Events.GetValueOrDefault("POCA") ?? throw new InvalidDataException("Camera fixture requires an outgoing IDLE.");
            if (next.Events.GetValueOrDefault("POBA") != idle || initial.Form == next.Form)
                throw new InvalidDataException("Camera fixture requires distinct packages with a shared change/begin IDLE.");
            var idleRecord = records.GetEffective(idle);
            var idleHash = SHA256.HashData(idleRecord.ReadData());
            var timing = FalloutIdleAnimationData.Read(idleRecord);
            if (timing.SelectAdditionalLoops(_ => throw new InvalidDataException("Camera fixture must not choose random repetitions.")) != byte.MaxValue)
                throw new InvalidDataException("Camera fixture requires the source forever-loop sentinel.");
            var selected = FalloutActorIdleSource.Resolve(records, idleRecord);
            if (!content.TryRead(selected.AnimationPath, null, out var bytes, out var identity) ||
                !content.TryRead("meshes/characters/_1stperson/skeleton.nif", null, out var skeletonBytes, out _))
                throw new FileNotFoundException("Camera fixture source KF or skeleton is missing.");
            var animation = new FalloutNifAnimatedNodePath(FalloutNifFile.Read(skeletonBytes), FalloutNifFile.Read(bytes), "Camera1st");
            var sequence = animation.Sequence;
            var sourceClock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency, sequence.CycleType,
                animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), byte.MaxValue);
            var sound = animation.TextKeys.Single(key => key.Value.Trim().StartsWith("Sound:", StringComparison.OrdinalIgnoreCase));
            var stop = animation.TextKeys.Single(key => key.Value.Trim().Equals("Enum: StopSounds", StringComparison.OrdinalIgnoreCase));
            if (sound.Time <= sequence.StartTime || stop.Time <= sound.Time || stop.Time >= sourceClock.LoopStart)
                throw new InvalidDataException("Camera fixture requires ordered intro sound/stop keys outside the repeat interval.");
            var marker = world.Placement(initial.LocationReference ?? throw new InvalidDataException("Camera fixture requires an explicit marker."));
            var configuration = RuntimeConfiguration.Load();
            var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(marker.RotationRadians[0], marker.RotationRadians[1], marker.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(marker.Position[0], marker.Position[1], marker.Position[2])) * configuration.World.GameUnitsToMeters);
            RuntimeNativePlayer Create()
            {
                var actor = new RuntimeNativePlayer(); actor.Configure(configuration, transform);
                AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.SetProcessUnhandledInput(false);
                return actor;
            }
            player = Create();
            var session = new FalloutScriptSession();
            var owner = new RuntimeNativePlayerPackage(records, player, session, world, () => marker.Cell);
            owner.Apply(initial.Form); owner.Apply(next.Form);
            if (session.PlayerPackage?.Package != next.Form || session.PlayerPackage.PendingPackage is not null ||
                session.PlayerPackage.Idle != idle || session.PlayerPackage.Elapsed != 0)
                throw new InvalidDataException("Infinite camera event did not settle onto its incoming assignment.");
            var initialCamera = player.Camera.Transform;
            var events = new List<string>();
            NativeOwnedAnimationSoundPlayer.SoundObserver = observation =>
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(observation));
                events.Add(json.RootElement.GetProperty("observation").GetProperty("textKey").GetString()!);
                previousObserver?.Invoke(observation);
            };
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            long samples = 0; var peak = 0f;
            void Drain()
            {
                var count = capture.GetFramesAvailable();
                if (count == 0) return;
                var frames = capture.GetBuffer(count);
                if (frames.Length != count) throw new InvalidDataException("Camera sound fixture lost a native mixer packet.");
                samples += frames.Length;
                foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            owner.Advance((sound.Time - sequence.StartTime) / sequence.Frequency + .02);
            var voices = player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Single();
            if (events.Count != 1 || FalloutAnimationSound.EditorId(events[0]) != FalloutAnimationSound.EditorId(sound.Value) || voices.LastEvent is null)
                throw new InvalidDataException("Authored camera sound did not dispatch exactly once: " + JsonSerializer.Serialize(new { events, voices.LastEvent }));
            var waitUntil = Time.GetTicksMsec() + 300;
            while (Time.GetTicksMsec() < waitUntil) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
            Drain();
            if (peak <= .000001f || capture.GetDiscardedFrames() != 0)
                throw new InvalidDataException("Authored camera sound produced no observed mixer output or lost samples.");
            var currentSource = session.PlayerPackage!.Playback!.SourceSeconds;
            owner.Advance((stop.Time - currentSource) / sequence.Frequency + .02);
            using (var stopped = JsonDocument.Parse(JsonSerializer.Serialize(voices.LastEvent)))
                if (stopped.RootElement.GetProperty("disposition").GetString() != "source-stop-sounds" ||
                    stopped.RootElement.GetProperty("voices").GetInt32() == 0)
                    throw new InvalidDataException("Authored StopSounds did not retire its active camera voice.");
            owner.Advance(85.75);
            var saved = session.Capture();
            if (saved.PlayerPackage is not { PendingPackage: null, Playback.CompletedRepeats: > 0 } ||
                saved.PlayerPackage.Playback.SourceSeconds < sourceClock.LoopStart || saved.PlayerPackage.Playback.SourceSeconds >= sourceClock.LoopEnd ||
                saved.PlayerPackage.Elapsed <= (sequence.StopTime - sequence.StartTime) / sequence.Frequency ||
                player.Camera.Transform.Origin.DistanceTo(initialCamera.Origin) <= .01f || events.Count != 2 || events[1] != stop.Value.Trim())
                throw new InvalidDataException("Camera replayed its intro/travel/sounds instead of the source repeat interval.");
            coldPlayer = Create();
            var cold = new FalloutScriptSession();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(saved))!);
            var restored = new RuntimeNativePlayerPackage(records, coldPlayer, cold, world, () => marker.Cell);
            if (cold.PlayerPackage != saved.PlayerPackage || coldPlayer.Camera.Transform != player.Camera.Transform ||
                coldPlayer.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Any())
                throw new InvalidDataException("Cold loop restoration restarted its source phase or replayed a past sound.");
            var liveState = JsonSerializer.Serialize(owner.State);
            var liveCamera = player.Camera.Transform;
            var rejectedSnapshot = saved.PlayerPackage! with
            {
                Package = initial.Form,
                PackageSha256 = Convert.ToHexString(SHA256.HashData(initialRecord.ReadData())),
                Cursor = 0,
                EventKind = "POCA",
                Elapsed = 0,
                Playback = new(sequence.StartTime, byte.MaxValue, byte.MaxValue, 0, true, false),
                PendingPackage = next.Form,
                PendingPackageSha256 = new string('0', 64)
            };
            var rejected = false;
            try { owner.Restore(rejectedSnapshot); } catch (InvalidDataException) { rejected = true; }
            if (!rejected || session.PlayerPackage != saved.PlayerPackage || player.Camera.Transform != liveCamera ||
                JsonSerializer.Serialize(owner.State) != liveState ||
                player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Single() != voices)
                throw new InvalidDataException("Rejected pending restore changed the live camera clock or its existing sound child.");
            owner.Advance(12.375); restored.Advance(12.375);
            if (session.PlayerPackage != cold.PlayerPackage || coldPlayer.Camera.Transform != player.Camera.Transform || events.Count != 2)
                throw new InvalidDataException("Cold repeated camera advancement replayed past sound keys or changed its phase.");
            const ulong restoredSoundSeed = 0xA05B3E918D72F641;
            owner.Restore(session.PlayerPackage! with { SoundRandomState = restoredSoundSeed });
            restored.Restore(cold.PlayerPackage! with { SoundRandomState = restoredSoundSeed });
            owner.Advance(0); restored.Advance(0);
            if (session.PlayerPackage != cold.PlayerPackage || session.PlayerPackage!.SoundRandomState != restoredSoundSeed ||
                player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Single() != voices || events.Count != 2)
                throw new InvalidDataException("Live restore replaced its sound child or replayed a past key.");
            var beforeRemoval = session.Capture();
            owner.Apply(null); restored.Apply(null); owner.Advance(60); restored.Advance(60);
            if (session.PlayerPackage is not null || cold.PlayerPackage is not null ||
                !SHA256.HashData(idleRecord.ReadData()).AsSpan().SequenceEqual(idleHash))
                throw new InvalidDataException("Camera removal retained its assignment or changed owned source bytes.");
            var unboundAudio = voices.Unbound.ToArray();
            owner.Apply(initial.Form); owner.Apply(next.Form);
            owner.Advance((sound.Time - sequence.StartTime) / sequence.Frequency + .02);
            using (var restartedSound = JsonDocument.Parse(JsonSerializer.Serialize(voices.LastEvent)))
                if (restartedSound.RootElement.GetProperty("selected").GetProperty("RandomBefore").GetUInt64() != restoredSoundSeed)
                    throw new InvalidDataException("Existing sound child retained a stale RNG object after live restore.");
            if (events.Count != 3 || player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Single() != voices ||
                records.SoundVoices.ActiveVoices == 0)
                throw new InvalidDataException("A new camera clock duplicated its sound child or failed to register its source voice.");
            owner.Apply(null);
            if (player.GetChildren().OfType<NativeOwnedAnimationSoundPlayer>().Single() != voices || records.SoundVoices.ActiveVoices == 0)
                throw new InvalidDataException("Camera removal discarded the source transient voice or duplicated its sound owner.");
            player.Free(); player = null;
            if (GodotObject.IsInstanceValid(voices) || records.SoundVoices.ActiveVoices != 0)
                throw new InvalidDataException("Player tree exit retained its animation sound owner or voice registration.");
            GD.Print("OPENNV_NATIVE_PLAYER_PACKAGE_CAMERA_LOOP_PASS " + JsonSerializer.Serialize(new
            {
                initial = initial.Form,
                next = next.Form,
                idle,
                source = identity,
                sourceHash = Convert.ToHexString(SHA256.HashData(bytes)),
                idleHash = Convert.ToHexString(idleHash),
                sound = sound.Value,
                stop = stop.Value,
                beforeRemoval.PlayerPackage!.Playback,
                samples,
                peak,
                discarded = capture.GetDiscardedFrames(),
                authoredRepeat = true,
                assignmentSettled = true,
                unboundAudio,
                noTravelReplay = true,
                soundAndStopOnce = true,
                liveRestoreAtomic = true,
                restoredSoundStream = true,
                soundChildReused = true,
                playerExitRetiredVoices = true,
                coldClock = true,
                coldCamera = true,
                coldSoundReplay = false,
                removal = true,
                sourceReadonly = true,
                recording = false,
                finiteEventTiming = "unverified",
                unboundBodyTargets = animation.UnboundOtherTargets,
                parity = "unverified",
                boundary = "isolated-owned-camera-and-native-mixer"
            }));
        }
        finally
        {
            NativeOwnedAnimationSoundPlayer.SoundObserver = previousObserver;
            player?.Free(); coldPlayer?.Free();
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; index--)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
        }
    }
}
