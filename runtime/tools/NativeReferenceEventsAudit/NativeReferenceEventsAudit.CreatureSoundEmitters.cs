using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private Task ExerciseOwnedCreatureSoundEmitters(string gameRoot, string mod, string modRoot,
        string firstSelector, string secondSelector, string sourceKf, string[] dependencies)
        => ExerciseOwnedCreatureSoundEmitters(gameRoot, mod, modRoot, [firstSelector, secondSelector],
            sourceKf, dependencies, null);

    private async Task ExerciseOwnedCreatureSoundEmitters(string gameRoot, string mod, string modRoot,
        IReadOnlyList<string> selectors, string sourceKf, string[] dependencies, string? checkpointPath)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var checkpoint = checkpointPath is null ? null : ReadCreatureEmitterCheckpoint(checkpointPath, content, records);
            var results = new List<object>();
            foreach (var selector in selectors)
            {
                var parts = selector.Split(':');
                Require(parts.Length == 2, "Creature sound selector requires a plugin and object ID.");
                var record = records.GetEffective(new(parts[0], Convert.ToUInt32(parts[1], 16)));
                Require(record.Signature == "ACRE", "Owned creature sound selector is not an actual ACRE.");
                using var world = CreateCreatureEmitterWorld(records, checkpoint);
                var instance = world.Get(record.FormKey);
                if (checkpoint is not null) RequireCheckpointCreature(checkpoint, records, world, instance);
                instance.AnimationSoundEvents.RequireEnabledSourceEmitter();
                var admittedHistory = JsonSerializer.Serialize(instance.AnimationSoundEvents.Capture());
                var admittedRandom = instance.SoundRandom.State;
                var admittedAnimation = instance.Animation.Capture();
                var scriptValuesBefore = JsonSerializer.Serialize(world.ScriptValues.Capture());
                var cell = world.ComposeResidency(FalloutCellSceneReader.Read(records, instance.Placement?.Cell ?? instance.Cell));
                var reference = cell.References.Single(value => value.FormKey == record.FormKey);
                world.LoadCell(cell with { References = [reference] });
                RuntimeNativeCreature? actor = null, resumed = null;
                FalloutReferenceWorld? cold = null;
                try
                {
                    var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
                    actor = RuntimeNativeCreature.Create(records, content, reference, instance, units);
                    Require(JsonSerializer.Serialize(instance.AnimationSoundEvents.Capture()) == admittedHistory &&
                        instance.SoundRandom.State == admittedRandom,
                        "Actual creature reconstruction replayed or redrew its retained source sound history.");
                    if (checkpoint is not null && admittedAnimation is not null)
                    {
                        var actual = instance.Animation.Capture()!;
                        Require(actual.Resource.Equals(admittedAnimation.Resource, StringComparison.OrdinalIgnoreCase) &&
                            actual.Sha256.Equals(admittedAnimation.Sha256, StringComparison.OrdinalIgnoreCase) &&
                            actual.ElapsedSeconds == admittedAnimation.ElapsedSeconds && actual.StartPending == admittedAnimation.StartPending,
                            "Actual creature reconstruction changed its saved animation clock.");
                    }
                    actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(
                        new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                        GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
                    var placement = actor.Transform; AddChild(actor);
                    foreach (var node in NodeTraversal.SelfAndDescendants<Node>(actor))
                    { node.SetProcess(false); node.SetPhysicsProcess(false); }
                    var directory = actor.Appearance.SkeletonPath[..actor.Appearance.SkeletonPath.LastIndexOf('/')];
                    Require(sourceKf.Replace('\\', '/').StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase),
                        "Selected diagnostic KF is outside the actual creature's source directory.");
                    var resources = new[] { actor.Appearance.SkeletonPath }.Concat(actor.Appearance.Models).Append(sourceKf)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, path =>
                        {
                            if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
                            return (Bytes: bytes, Identity: identity, Hash: Convert.ToHexString(SHA256.HashData(bytes)));
                        }, StringComparer.OrdinalIgnoreCase);
                    var original = new[] { record, records.GetEffective(reference.Base) };
                    var hashes = original.Select(value => Convert.ToHexString(SHA256.HashData(value.ReadData()))).ToArray();
                    var animation = FalloutNifFile.Read(resources[sourceKf].Bytes);
                    var sequence = animation.Roots.Select(animation.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                    var keys = ((FalloutNifTextKeyExtraData)animation.ReadObject(sequence.TextKeys)).Keys
                        .SelectMany((key, ordinal) => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(text => new FalloutNifTextKeyEvent(0, ordinal, key.Time, text.Trim())))
                        .Where(key => key.Text.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase)).ToArray();
                    Require(keys.Length != 0, "Selected source KF has no actual sound request.");
                    var sounds = new NativeOwnedAnimationSoundPlayer(records, content, actor, units,
                        instance.SoundRandom, instance.AnimationSoundEvents);
                    actor.AddChild(sounds); sounds.SetProcess(false);
                    var ledger = instance.AnimationSoundEvents;
                    var originalHistory = ledger.Capture();
                    var historyBefore = JsonSerializer.Serialize(originalHistory);
                    var originalEventCount = ledger.Events.Count;
                    var prefixBefore = JsonSerializer.Serialize(ledger.Events.ToArray());
                    var randomBefore = instance.SoundRandom.State;
                    var before = Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount()).Select(actor.Skeleton.Node.GetBonePose).ToArray();
                    var rootBlock = actor.Skeleton.Source.Roots.Single();
                    var rootName = actor.Skeleton.Source.ReadNode(rootBlock).Name;
                    var namedRoot = sounds.ResolveEmitter(rootName);
                    ledger.RequireEnabledSourceEmitter();
                    var rootIdentity = RuntimeNativeNifSoundEmitters.CaptureBone(actor, namedRoot.Emitter, reference.FormKey) ??
                        throw new InvalidDataException("Owned creature root bone has no typed primary source identity.");
                    Require(namedRoot.FollowEmitter && namedRoot.Emitter is BoneAttachment3D { OverridePose: false } root &&
                        root.BoneIdx == actor.Skeleton.BoneIndex(rootName), "Actual creature scene root has no exact native bone adapter.");
                    Require(instance.SoundRandom.State == randomBefore && JsonSerializer.Serialize(ledger.Capture()) == historyBefore &&
                        before.SequenceEqual(Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount()).Select(actor.Skeleton.Node.GetBonePose)),
                        "Emitter preparation consumed source RNG/history or changed creature poses.");
                    var historical = new FalloutAnimationSoundEvents(reference.FormKey);
                    historical.Fail(null, "retained-component-source-fault");
                    var historicalBefore = JsonSerializer.Serialize(historical.CaptureDiagnostic);
                    var stopped = new NativeOwnedAnimationSoundPlayer(records, content, actor, units, instance.SoundRandom, historical);
                    actor.AddChild(stopped); stopped.SetProcess(false);
                    Require(stopped.ResolveEmitter(rootName).Emitter == namedRoot.Emitter &&
                        JsonSerializer.Serialize(historical.CaptureDiagnostic) == historicalBefore && !historical.CanCapture,
                        "Source binding replaced a retained stopped fault or duplicated its bone adapter.");
                    stopped.Free();
                    var sampler = new RuntimeNativeNifAnimation(animation, sequence, actor.Skeleton);
                    var dispatched = 0;
                    foreach (var key in keys)
                    {
                        sampler.ApplySourceTime(key.SourceSeconds);
                        var payload = key.Text["Sound:".Length..].Trim(); var separator = payload.IndexOfAny([' ', '\t']);
                        var sound = FalloutSoundRecordReader.Read(records, FalloutSoundRecordReader.Find(records,
                            separator < 0 ? payload : payload[..separator]).FormKey);
                        Require(!sound.IsLooping, "Selected owned key needs a separate authored loop-stop proof.");
                        var requested = separator < 0 ? "" : payload[(separator + 1)..].Trim();
                        var emitted = sounds.ResolveEmitter(requested, sound.IsLooping);
                        var expectedRoot = actor.Skeleton.Node.GlobalTransform * actor.Skeleton.Node.GetBoneGlobalPose(actor.Skeleton.BoneIndex(rootName));
                        Require(!emitted.FollowEmitter && emitted.Emitter == namedRoot.Emitter &&
                            emitted.Emitter.GlobalTransform.IsEqualApprox(expectedRoot), "Source-null finite lookup did not use actual Get3D key-time position.");
                        var disposition = sounds.Dispatch(key);
                        Require(!disposition.Contains("unbound-source-sound", StringComparison.Ordinal),
                            "Actual creature key failed: " + JsonSerializer.Serialize(sounds.LastEvent));
                        var entry = ledger.Events.Last();
                        Require(entry.Generation == originalHistory.Generation + dispatched + 1 &&
                            entry.Sound == sound.FormKey && entry.Played && entry.End == FalloutAnimationSoundEnd.Active &&
                            entry.Error is null && FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256),
                            "Creature finite sound has no actual selected source/media generation.");
                        dispatched++;
                    }
                    Require(actor.Transform == placement && sounds.PendingFiniteVoices?.Count == dispatched && !ledger.CanCapture,
                        "Creature sound changed source placement or lost its native finite wait receipts.");
                    actor.Free(); actor = null;
                    var timer = Stopwatch.StartNew();
                    while (!ledger.CanCapture && timer.Elapsed.TotalSeconds < 90)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(ledger.CanCapture && ledger.Events.Count == originalEventCount + dispatched &&
                        ledger.Events.Skip(originalEventCount).All(entry => entry.End == FalloutAnimationSoundEnd.NativeFinished) &&
                        JsonSerializer.Serialize(ledger.Events.Take(originalEventCount).ToArray()) == prefixBefore,
                        "Creature source sound did not naturally finish after actual skeleton retirement: " + JsonSerializer.Serialize(ledger.CaptureDiagnostic));
                    var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(instance.Capture()))!;
                    var historyJson = JsonSerializer.Serialize(ledger.Capture());
                    Require(JsonSerializer.Serialize(world.ScriptValues.Capture()) == scriptValuesBefore,
                        "Creature component changed the saved authoritative script value store.");
                    var coldReferences = (checkpoint?.State.References ?? world.Capture())
                        .Select(value => value.Reference == saved.Reference ? saved : value).ToArray();
                    cold = CreateCreatureEmitterWorld(records, checkpoint, coldReferences);
                    resumed = RuntimeNativeCreature.Create(records, content, reference, cold.Get(reference.FormKey), units);
                    resumed.Transform = placement; AddChild(resumed);
                    foreach (var node in NodeTraversal.SelfAndDescendants<Node>(resumed))
                    { node.SetProcess(false); node.SetPhysicsProcess(false); }
                    var coldSounds = new NativeOwnedAnimationSoundPlayer(records, content, resumed, units,
                        cold.Get(reference.FormKey).SoundRandom, cold.Get(reference.FormKey).AnimationSoundEvents);
                    resumed.AddChild(coldSounds); coldSounds.SetProcess(false);
                    Require(!resumed.Skeleton.Node.GetChildren().OfType<BoneAttachment3D>()
                        .Any(node => node.HasMeta("opennv_nif_sound_bone_emitter")),
                        "Fresh owned creature replayed a key or already contained a sound bone adapter.");
                    cold.Get(reference.FormKey).AnimationSoundEvents.RequireEnabledSourceEmitter();
                    var reconstructed = RuntimeNativeNifSoundEmitters.RestoreBone(resumed, reference.FormKey, rootIdentity,
                        new Dictionary<string, Node3D>(StringComparer.Ordinal));
                    Require(reconstructed is BoneAttachment3D { OverridePose: false } restoredRoot &&
                        restoredRoot.BoneIdx == resumed.Skeleton.BoneIndex(rootName) &&
                        resumed.Skeleton.Node.GetChildren().OfType<BoneAttachment3D>()
                            .Count(node => node.HasMeta("opennv_nif_sound_bone_emitter")) == 1 &&
                        reconstructed.GlobalTransform.IsEqualApprox(resumed.Skeleton.Node.GlobalTransform *
                            resumed.Skeleton.Node.GetBoneGlobalPose(resumed.Skeleton.BoneIndex(rootName))) &&
                        RuntimeNativeNifSoundEmitters.CaptureBone(resumed, reconstructed, reference.FormKey) == rootIdentity,
                        "Fresh owned source skeleton did not reconstruct its exact typed bone and current real pose.");
                    var coldEmitter = coldSounds.ResolveEmitter("");
                    Require(!coldEmitter.FollowEmitter && coldEmitter.Emitter is BoneAttachment3D &&
                        historyJson == JsonSerializer.Serialize(cold.Get(reference.FormKey).AnimationSoundEvents.Capture()) &&
                        instance.SoundRandom.State == cold.Get(reference.FormKey).SoundRandom.State && records.SoundVoices.ActiveVoices == 0 &&
                        JsonSerializer.Serialize(cold.ScriptValues.Capture()) == scriptValuesBefore,
                        "Cold creature emitter binding redrew, replayed or changed finished source history.");
                    foreach (var (path, value) in resources)
                        Require(content.TryRead(path, null, out var bytes, out var identity) && identity == value.Identity &&
                            Convert.ToHexString(SHA256.HashData(bytes)) == value.Hash, "Owned creature resource changed during the proof.");
                    Require(original.Select(value => Convert.ToHexString(SHA256.HashData(value.ReadData()))).SequenceEqual(hashes),
                        "Owned creature records changed during the proof.");
                    results.Add(new
                    {
                        reference = reference.FormKey.ToString(),
                        baseForm = reference.Base.ToString(),
                        sourceKf,
                        sources = resources.Select(value => new { path = value.Key, value.Value.Identity, value.Value.Hash }),
                        keys,
                        dispatched,
                        actualRootBone = rootName,
                        typedBoneIdentity = rootIdentity,
                        freshBoneReconstructedBeforeKey = true,
                        sourceBoneCurrentPose = true,
                        preparationNoDraw = true,
                        originalGeneration = originalHistory.Generation,
                        originalEvents = originalEventCount,
                        originalFaults = originalHistory.Faults,
                        retainedHistoryPrefix = true,
                        checkpointSha256 = checkpoint?.Sha256,
                        checkpointSourceSchema = checkpoint?.SourceSchema,
                        checkpointUnchanged = checkpoint is not null,
                        retainedReferenceCount = checkpoint?.State.References?.Count,
                        authenticEnableGraph = checkpoint is not null,
                        authoritativeScriptValues = true,
                        sourceFaultRetained = true,
                        genuineNativeFinished = true,
                        actualSkeletonRetirement = true,
                        coldHistoryNoReplay = true,
                        sourceUnchanged = true,
                        partialLanes = ledger.PartialLanes.ToArray(),
                        componentOnly = true,
                        wholeCampaignSave = "unverified",
                        audibleOutput = "unverified"
                    });
                }
                finally
                {
                    if (resumed is not null && GodotObject.IsInstanceValid(resumed)) resumed.Free();
                    if (actor is not null && GodotObject.IsInstanceValid(actor)) actor.Free();
                    cold?.Dispose();
                }
            }
            if (checkpoint is not null) RequireCreatureCheckpointUnchanged(checkpoint);
            GD.Print("OPENNV_NATIVE_OWNED_CREATURE_SOUND_EMITTERS_PASS " + JsonSerializer.Serialize(results));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
