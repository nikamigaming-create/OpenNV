using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task ExerciseOwnedSoundEmitters(string gameRoot, string mod, string modRoot,
        string firstSelector, string secondSelector, string[] dependencies, bool completionEdge = false, string? completionSound = null)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            FalloutFormKey? requestedSound = null;
            if (completionEdge)
            {
                var soundParts = (completionSound ?? throw new InvalidDataException("Completion edge requires its exact source sound.")).Split(':');
                requestedSound = soundParts.Length == 2
                    ? FalloutSoundRecordReader.Read(records, new(soundParts[0], Convert.ToUInt32(soundParts[1], 16))).FormKey
                    : FalloutSoundRecordReader.Find(records, completionSound!).FormKey;
            }
            var results = new List<object>();
            foreach (var selector in new[] { firstSelector, secondSelector })
            {
                using var world = new FalloutReferenceWorld(records);
                var parts = selector.Split(':');
                var record = parts.Length == 2 ? records.GetEffective(new(parts[0], Convert.ToUInt32(parts[1], 16))) :
                    FalloutDialogueTopic.Find(records, "REFR", selector);
                Require(record.Signature == "REFR", "Owned sound selector is not a placed source reference.");
                var instance = world.Get(record.FormKey);
                var sourceCell = FalloutCellSceneReader.Read(records, instance.Cell);
                var reference = sourceCell.References.Single(value => value.FormKey == record.FormKey);
                world.LoadCell(sourceCell with { References = [reference] });
                var model = sourceCell.BaseObjects[reference.Base].ModelPath ??
                    throw new InvalidDataException("Selected sound reference has no actual source model.");
                Require(content.TryRead(model, null, out var bytes, out var identity), "Selected sound model is absent.");
                var source = FalloutNifFile.Read(bytes);
                var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
                var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                    new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
                var prototype = new RuntimeNativeNifPrototype(source, units);
                Node3D? node = null;
                try
                {
                    node = prototype.InstantiatePlaced(placement);
                    node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); AddChild(node);
                    foreach (var owned in NodeTraversal.SelfAndDescendants<Node>(node))
                    { owned.SetProcess(false); owned.SetPhysicsProcess(false); }
                    var sounds = new NativeOwnedAnimationSoundPlayer(records, content, node, units,
                        instance.SoundRandom, instance.AnimationSoundEvents);
                    node.AddChild(sounds); sounds.SetProcess(false);
                    var ledger = instance.AnimationSoundEvents;
                    var originalRecords = new[] { record, records.GetEffective(reference.Base) };
                    var hashes = originalRecords.Select(value => Convert.ToHexString(SHA256.HashData(value.ReadData()))).ToArray();
                    var randomBefore = instance.SoundRandom.State;
                    if (!completionEdge)
                    {
                        var nativeNames = NodeTraversal.SelfAndDescendants<Node3D>(node)
                            .Where(value => value.HasMeta("opennv_nif_source_name"))
                            .GroupBy(value => value.GetMeta("opennv_nif_source_name").AsString(), StringComparer.Ordinal).ToArray();
                        var named = nativeNames.First(group => group.Key.Length != 0 && group.Count() == 1);
                        var positive = sounds.ResolveEmitter(named.Key);
                        Require(positive.Emitter == named.Single() && positive.FollowEmitter,
                            "Actual authored named emitter lost its exact native owner.");
                        var unowned = source.Strings.First(value => value.Length != 0 && nativeNames.All(group => group.Key != value));
                        RefuseEmitter(unowned);
                    }
                    var keys = source.Blocks.Where(block => block.TypeName == "NiTextKeyExtraData")
                        .SelectMany(block => ((FalloutNifTextKeyExtraData)source.ReadObject(block.Index)).Keys)
                        .SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(text => (key.Time, Text: text.Trim())))
                        .Where(key => key.Text.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase))
                        .DistinctBy(key => key.Text, StringComparer.Ordinal).ToArray();
                    var selected = new List<(float Time, string Text, string Name, FalloutSoundRecord Source)>();
                    foreach (var key in keys)
                    {
                        var payload = key.Text["Sound:".Length..].Trim();
                        var separator = payload.IndexOfAny([' ', '\t']);
                        var editorId = separator < 0 ? payload : payload[..separator];
                        var name = separator < 0 ? "" : payload[(separator + 1)..].Trim();
                        var sound = FalloutSoundRecordReader.Read(records, FalloutSoundRecordReader.Find(records, editorId).FormKey);
                        if (completionEdge)
                        {
                            if (sound.FormKey != requestedSound || FalloutSoundLoop.Read(sound).Mode != FalloutSoundLoopMode.None) continue;
                            _ = sounds.ResolveEmitter(name);
                            selected.Add((key.Time, key.Text, name, sound));
                            continue;
                        }
                        if (sound.IsLooping || source.Strings.Contains(name.Length == 0 ? "AttachSound" : name, StringComparer.Ordinal)) continue;
                        var finite = sounds.ResolveEmitter(name, sound.IsLooping);
                        var loop = sounds.ResolveEmitter(name, sourceLoop: true);
                        Require(finite.Emitter == node && !finite.FollowEmitter && loop.Emitter == node && loop.FollowEmitter,
                            "Original null named/default lookup did not distinguish finite position from Loop root attachment.");
                        selected.Add((key.Time, key.Text, name, sound));
                    }
                    Require(selected.Count != 0 && randomBefore == instance.SoundRandom.State && ledger.Events.Count == 0,
                        "Source emitter selection consumed RNG/history or selected no actual finite source keys.");
                    if (completionEdge) selected = selected.Take(1).ToList();
                    else
                    {
                        var domain = node.GetMeta("opennv_nif_fixed_strings");
                        node.RemoveMeta("opennv_nif_fixed_strings");
                        try { RefuseEmitter(selected[0].Name); }
                        finally { node.SetMeta("opennv_nif_fixed_strings", domain); }
                    }
                    var dispatched = 0;
                    foreach (var key in selected)
                    {
                        var disposition = sounds.Dispatch(new FalloutNifTextKeyEvent(0, dispatched++, key.Time, key.Text));
                        Require(!disposition.Contains("unbound-source-sound", StringComparison.Ordinal),
                            "Actual source key failed dispatch: " + JsonSerializer.Serialize(sounds.LastEvent));
                        var entry = ledger.Events.Last();
                        Require(entry.Sound == key.Source.FormKey && entry.Played && entry.End == FalloutAnimationSoundEnd.Active &&
                            entry.Error is null && FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256),
                            "Actual finite source sound has no native media generation.");
                    }
                    Require(sounds.PendingFiniteVoices?.Count == dispatched && !ledger.CanCapture && node.Transform == placement,
                        "Actual voices lost their source wait receipts or changed source placement.");
                    var nativeCompletion = completionEdge
                        ? await ProveFiniteCompletionEdge(records, world, sounds.ActiveNativeVoices, content.SaveCompatibilityId) : null;
                    node.Free(); node = null; // Genuine model retirement, before disposing its source graph.
                    var timer = Stopwatch.StartNew();
                    while (!ledger.CanCapture && timer.Elapsed.TotalSeconds < 90)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(ledger.CanCapture && ledger.Events.All(entry => entry.End == FalloutAnimationSoundEnd.NativeFinished),
                        "Actual finite source playback did not finish after model retirement: " + JsonSerializer.Serialize(ledger.CaptureDiagnostic));
                    var snapshot = ledger.Capture(); var json = JsonSerializer.Serialize(snapshot);
                    var cold = new FalloutAnimationSoundEvents(reference.FormKey);
                    cold.Restore(JsonSerializer.Deserialize<FalloutAnimationSoundEventsSnapshot>(json)!, records); cold.ValidateMedia(content);
                    Require(JsonSerializer.Serialize(cold.Capture()) == json && records.SoundVoices.ActiveVoices == 0,
                        "Cold ended-source history changed selection/media or replayed a voice.");
                    Require(content.TryRead(model, null, out var again, out var againIdentity) && identity == againIdentity &&
                        SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(again)) &&
                        originalRecords.Select(value => Convert.ToHexString(SHA256.HashData(value.ReadData()))).SequenceEqual(hashes),
                        "Owned emitter proof changed its source inputs.");
                    results.Add(new
                    {
                        reference = reference.FormKey.ToString(),
                        model,
                        identity,
                        source.Sha256,
                        keys = selected.Select(key => new
                        { key.Text, key.Time, key.Name, sound = key.Source.FormKey.ToString(), flags = (ushort)key.Source.Flags }),
                        dispatched,
                        nativeFinished = ledger.Events.Count,
                        defaultLookup = "AttachSound",
                        preparationNoDraw = true,
                        sourceNullPositionOnly = !completionEdge,
                        loopRootFollowing = !completionEdge,
                        sourceDomainRefusal = !completionEdge,
                        nativeRetirement = !completionEdge,
                        retiredAfterCompletion = completionEdge,
                        nativeCompletion,
                        coldNoReplay = true,
                        sourceUnchanged = true,
                        partialLanes = ledger.PartialLanes.ToArray()
                    });

                    void RefuseEmitter(string name)
                    {
                        try { _ = sounds.ResolveEmitter(name); }
                        catch (NotSupportedException) { return; }
                        throw new InvalidDataException("Unknown or source-present missing emitter was silently admitted.");
                    }
                }
                finally
                {
                    if (node is not null && GodotObject.IsInstanceValid(node)) node.Free();
                    if (GodotObject.IsInstanceValid(prototype.Scene.Root)) prototype.Scene.Root.Free();
                }
            }
            GD.Print((completionEdge ? "OPENNV_NATIVE_OWNED_FINITE_COMPLETION_EDGE_PASS " : "OPENNV_NATIVE_OWNED_SOUND_EMITTERS_PASS ") +
                JsonSerializer.Serialize(results));
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }
}
