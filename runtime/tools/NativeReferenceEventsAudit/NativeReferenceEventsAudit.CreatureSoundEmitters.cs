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
    private async Task ExerciseOwnedCreatureSoundEmitters(string gameRoot, string mod, string modRoot,
        string firstSelector, string secondSelector, string sourceKf, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var results = new List<object>();
            foreach (var selector in new[] { firstSelector, secondSelector })
            {
                var parts = selector.Split(':');
                Require(parts.Length == 2, "Creature sound selector requires a plugin and object ID.");
                var record = records.GetEffective(new(parts[0], Convert.ToUInt32(parts[1], 16)));
                Require(record.Signature == "ACRE", "Owned creature sound selector is not an actual ACRE.");
                using var world = new FalloutReferenceWorld(records);
                var instance = world.Get(record.FormKey);
                var cell = FalloutCellSceneReader.Read(records, instance.Cell);
                var reference = cell.References.Single(value => value.FormKey == record.FormKey);
                world.LoadCell(cell with { References = [reference] });
                RuntimeNativeCreature? actor = null, resumed = null;
                FalloutReferenceWorld? cold = null;
                try
                {
                    var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
                    actor = RuntimeNativeCreature.Create(records, content, reference, instance, units);
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
                    var randomBefore = instance.SoundRandom.State;
                    var before = Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount()).Select(actor.Skeleton.Node.GetBonePose).ToArray();
                    var rootBlock = actor.Skeleton.Source.Roots.Single();
                    var rootName = actor.Skeleton.Source.ReadNode(rootBlock).Name;
                    var namedRoot = sounds.ResolveEmitter(rootName);
                    Require(namedRoot.FollowEmitter && namedRoot.Emitter is BoneAttachment3D { OverridePose: false } root &&
                        root.BoneIdx == actor.Skeleton.BoneIndex(rootName), "Actual creature scene root has no exact native bone adapter.");
                    Require(instance.SoundRandom.State == randomBefore && ledger.Events.Count == 0 &&
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
                        Require(entry.Sound == sound.FormKey && entry.Played && entry.End == FalloutAnimationSoundEnd.Active &&
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
                    Require(ledger.CanCapture && ledger.Events.All(entry => entry.End == FalloutAnimationSoundEnd.NativeFinished),
                        "Creature source sound did not naturally finish after actual skeleton retirement: " + JsonSerializer.Serialize(ledger.CaptureDiagnostic));
                    var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(instance.Capture()))!;
                    var historyJson = JsonSerializer.Serialize(ledger.Capture());
                    cold = new FalloutReferenceWorld(records); cold.Restore([saved]);
                    resumed = RuntimeNativeCreature.Create(records, content, reference, cold.Get(reference.FormKey), units);
                    resumed.Transform = placement; AddChild(resumed);
                    foreach (var node in NodeTraversal.SelfAndDescendants<Node>(resumed))
                    { node.SetProcess(false); node.SetPhysicsProcess(false); }
                    var coldSounds = new NativeOwnedAnimationSoundPlayer(records, content, resumed, units,
                        cold.Get(reference.FormKey).SoundRandom, cold.Get(reference.FormKey).AnimationSoundEvents);
                    resumed.AddChild(coldSounds); coldSounds.SetProcess(false);
                    var coldEmitter = coldSounds.ResolveEmitter("");
                    Require(!coldEmitter.FollowEmitter && coldEmitter.Emitter is BoneAttachment3D &&
                        historyJson == JsonSerializer.Serialize(cold.Get(reference.FormKey).AnimationSoundEvents.Capture()) &&
                        instance.SoundRandom.State == cold.Get(reference.FormKey).SoundRandom.State && records.SoundVoices.ActiveVoices == 0,
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
                        preparationNoDraw = true,
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
            GD.Print("OPENNV_NATIVE_OWNED_CREATURE_SOUND_EMITTERS_PASS " + JsonSerializer.Serialize(results));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
