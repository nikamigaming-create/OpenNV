using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void ExerciseCreatureAssembly(string game, string mod, string root, string[] references, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = installation.OpenSource();
        ExerciseCreatureAssembly(content, references);
    }

    private void ExerciseCreatureAssembly(RuntimeLiveContentSource content, string[] references)
    {
        if (references.Length == 0) throw new ArgumentException("Creature audit requires placed source references.");
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var globals = FalloutGlobalState.Read(records);
        var quests = new FalloutQuestState(records);
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        foreach (var identity in references)
        {
            using var world = new FalloutReferenceWorld(records);
            var parts = identity.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Creature audit requires owner:FormID reference identities.");
            var key = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
            var source = records.GetEffective(key);
            if (source.Signature != "ACRE") throw new ArgumentException($"{key} is not an owned creature reference.");
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(source)!.Value);
            world.LoadCell(cell);
            var reference = cell.References.Single(value => value.FormKey == key);
            world.InitializeActorTemplates(key, 1, globals);
            var state = world.Get(key);
            var appearance = FalloutCreatureAppearanceResolver.Resolve(records, reference.Base, key, state.Templates);
            var hashes = appearance.Models.Prepend(appearance.SkeletonPath).ToDictionary(path => path, Hash);
            var sourceHash = SHA256.HashData(source.ReadData());
            var clockBefore = System.Text.Json.JsonSerializer.Serialize(state.Animation.Capture());
            var preparedSource = RuntimeNativeCreature.PreparedSource.Read(appearance, content, units, CancellationToken.None);
            var preparedPartitionShapes = preparedSource.HardwarePartitions.Sum(parts => parts.Count);
            try { using var wrongScale = new RuntimeNativeCreature.GeometryAssembly(preparedSource, content, units * 2); throw new InvalidOperationException("Mismatched creature source scale was admitted."); }
            catch (InvalidDataException) { }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { _ = RuntimeNativeCreature.PreparedSource.Read(appearance, content, units, cancelled.Token); throw new InvalidDataException("Cancelled creature source read completed."); }
                catch (OperationCanceledException) { }
            }
            using var assembly = new RuntimeNativeCreature.GeometryAssembly(preparedSource, content, units);
            var steps = 0;
            var maximumStepMilliseconds = 0d;
            var maximumStepStage = "";
            var textureLoads = 0;
            var textureResolves = 0;
            var textureResolveMilliseconds = 0d;
            var textureLoadMilliseconds = 0d;
            var texturePreparationMilliseconds = 0d;
            var textureUploadMilliseconds = 0d;
            var shaderBuilds = 0;
            var shaderCacheHits = 0;
            var shaderMilliseconds = 0d;
            var particleStages = new Dictionary<string, double>(StringComparer.Ordinal);
            RuntimeNifParticleSystem.ConfigureObserver = (stage, elapsed) =>
                particleStages[stage] = particleStages.GetValueOrDefault(stage) + elapsed;
            NativeNifLightingMaterial.BuildObserver = (created, elapsed) =>
            { if (created) shaderBuilds++; else shaderCacheHits++; shaderMilliseconds += elapsed; };
            OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.LoadObserver = (_, _, _, _, elapsed) =>
            { textureLoads++; textureLoadMilliseconds += elapsed; };
            OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.ResolveObserver = (_, elapsed) =>
            { textureResolves++; textureResolveMilliseconds += elapsed; };
            OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.UploadObserver = (_, _, _, prepare, upload) =>
            { texturePreparationMilliseconds += prepare; textureUploadMilliseconds += upload; };
            bool complete;
            try
            {
                do
                {
                    var stage = assembly.NextStage;
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    complete = assembly.Advance();
                    var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (milliseconds > maximumStepMilliseconds) { maximumStepMilliseconds = milliseconds; maximumStepStage = stage; }
                    steps++;
                } while (!complete);
            }
            finally
            {
                OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.LoadObserver = null;
                OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.ResolveObserver = null;
                OpenNV.Runtime.Presentation.Rendering.NativeDdsTexture.UploadObserver = null;
                NativeNifLightingMaterial.BuildObserver = null;
                RuntimeNifParticleSystem.ConfigureObserver = null;
            }
            if (particleStages.Count != 0)
                GD.Print($"OPENNV_CREATURE_PARTICLE_SETUP reference={key} " +
                    string.Join(" ", particleStages.Select(pair => $"{pair.Key}Ms={pair.Value:F3}")));
            using var preparedGeometry = assembly.Take();
            if (state.QuerySpatialPlacement is not null || state.QueryCurrentPackage is not null ||
                clockBefore != System.Text.Json.JsonSerializer.Serialize(state.Animation.Capture()))
                throw new InvalidDataException("Detached creature geometry changed authoritative actor state.");
            GD.Print($"OPENNV_CREATURE_SLICED_GEOMETRY reference={key} steps={steps} preparedPartitionShapes={preparedPartitionShapes} maximumStepMs={maximumStepMilliseconds:F3} maximumStepStage={maximumStepStage} textureResolves={textureResolves} textureResolveMs={textureResolveMilliseconds:F3} textureLoads={textureLoads} textureLoadMs={textureLoadMilliseconds:F3} texturePrepareMs={texturePreparationMilliseconds:F3} textureUploadMs={textureUploadMilliseconds:F3} shaderBuilds={shaderBuilds} shaderCacheHits={shaderCacheHits} shaderBuildMs={shaderMilliseconds:F3} actorStateUnchanged=true renderer=unverified");
            VerifySkeletonAssembly(preparedSource, preparedGeometry.Skeleton, units);
            VerifyCreaturePartAssembly(content, appearance, preparedGeometry.Parts, units);
            VerifyPartitionCancellation(content, preparedSource, units);
            var actor = RuntimeNativeCreature.Create(records, content, reference, state, units, preparedGeometry: preparedGeometry);
            try
            {
                VerifyCreatureAttachmentIndex(actor, preparedGeometry.AttachmentTargets);
                actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(
                    new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
                var sourcePlacement = world.SpatialPlacement(key, null, units);
                actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                    world, state, records, content, 1, 1);
                if (state.QuerySpatialPlacement is not null ||
                    !world.SpatialPlacement(key, null, units).Position.SequenceEqual(sourcePlacement.Position))
                    throw new InvalidDataException("Detached creature published a live spatial owner during staging.");
                if (!ReferenceEquals(actor.Skeleton.Source, NativeOwnedNifCache.Read(content, appearance.SkeletonPath, out _)))
                    throw new InvalidDataException("Creature skeleton did not reuse its decoded source.");
                foreach (var path in appearance.Models)
                    if (!ReferenceEquals(NativeOwnedNifCache.Read(content, path, out _), NativeOwnedNifCache.Read(content, path, out _)))
                        throw new InvalidDataException("Creature source model was not reused.");
                AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                if (state.QuerySpatialPlacement is null ||
                    world.SpatialPlacement(key, null, units).Position.Zip(sourcePlacement.Position).Any(pair => MathF.Abs(pair.First - pair.Second) > .01f))
                    throw new InvalidDataException("Live creature did not publish its actual source placement after attachment.");
                var livePlacementOwner = state.QuerySpatialPlacement;
                var replacement = RuntimeNativeCreature.Create(records, content, reference, state, units);
                try
                {
                    replacement.Combat = RuntimeNativeActorCombat.Attach(replacement, replacement.Skeleton, replacement.Appearance.SkeletonPath,
                        world, state, records, content, 1, 1);
                    if (state.QuerySpatialPlacement != livePlacementOwner)
                        throw new InvalidDataException("Detached replacement displaced the live creature spatial owner.");
                }
                finally { RuntimeCoordinator.DiscardNativeActor(replacement, actor); }
                if (state.QuerySpatialPlacement != livePlacementOwner)
                    throw new InvalidDataException("Discarding detached replacement changed the live creature spatial owner.");
                actor.ConfigureAi(records, quests, world, globals: globals);
                actor.EvaluatePackages(true);
                if (actor.Error is not null || actor.Parts.Count != appearance.Models.Count ||
                    actor.Parts.Sum(part => part.Surfaces) == 0 || state.QueryCurrentPackage is null)
                    throw new InvalidDataException("Owned creature assembly lost source geometry or its package query owner.");
                GD.Print($"OPENNV_CREATURE_ASSEMBLY_PASS reference={key} parts={actor.Parts.Count} surfaces={actor.Parts.Sum(part => part.Surfaces)} " +
                    "ownedMaterials=true decodedSourceReuse=true packageOwnerRegistered=true packageExecution=unverified finalPixels=unverified");
            }
            finally { actor.Free(); }
            if (state.QueryCurrentPackage is not null || state.QuerySpatialPlacement is not null || !sourceHash.SequenceEqual(SHA256.HashData(source.ReadData())) ||
                hashes.Any(pair => pair.Value != Hash(pair.Key)))
                throw new InvalidDataException("Creature audit retained a retired package owner or changed an owned source input.");
            GD.Print($"OPENNV_CREATURE_ASSEMBLY_RETIREMENT_PASS reference={key} sourceReadonly=true ownerRetired=true");
        }

        string Hash(string path) => content.TryRead(path, null, out var bytes, out _)
            ? Convert.ToHexString(SHA256.HashData(bytes)) : throw new FileNotFoundException(path);
    }

    private static void VerifyCreatureAttachmentIndex(RuntimeNativeCreature actor,
        IReadOnlyDictionary<string, Node3D?> prepared)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var indexMethod = typeof(RuntimeNativeCreature).GetMethod("IndexAttachmentTargets", flags)
            ?? throw new InvalidDataException("Creature attachment index owner is missing.");
        Dictionary<string, Node3D?> ReadIndex() => indexMethod.Invoke(actor, null) as Dictionary<string, Node3D?>
            ?? throw new InvalidDataException("Creature attachment index has invalid type.");
        var expected = actor.FindChildren("*", "", true, false).OfType<Node3D>()
            .GroupBy(node => node.GetMeta("opennv_nif_source_name", "").AsString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count() == 1 ? group.Single() : null, StringComparer.Ordinal);
        var index = ReadIndex();
        if (prepared.Count != expected.Count || expected.Any(pair => !prepared.TryGetValue(pair.Key, out var node) || !ReferenceEquals(node, pair.Value)))
            throw new InvalidDataException("Detached creature attachment index differs from the adopted source-tree scan.");
        if (index.Count != expected.Count || expected.Any(pair => !index.TryGetValue(pair.Key, out var node) || !ReferenceEquals(node, pair.Value)))
            throw new InvalidDataException("Creature attachment index differs from the original source-tree scan.");
        const string syntheticName = "opennv-audit-duplicate-attachment-name";
        if (index.ContainsKey(syntheticName)) throw new InvalidDataException("Synthetic index identity collides with source tree.");
        var first = new Node3D(); var second = new Node3D();
        try
        {
            first.SetMeta("opennv_nif_source_name", syntheticName); second.SetMeta("opennv_nif_source_name", syntheticName);
            actor.AddChild(first); actor.AddChild(second);
            if (!ReadIndex().TryGetValue(syntheticName, out var duplicate) || duplicate is not null)
                throw new InvalidDataException("Ambiguous creature attachment was accepted as unique.");
            second.Free();
            if (!ReferenceEquals(ReadIndex()[syntheticName], first))
                throw new InvalidDataException("Creature attachment index retained stale ambiguity.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(first)) first.Free();
            if (GodotObject.IsInstanceValid(second)) second.Free();
        }
        GD.Print($"OPENNV_CREATURE_ATTACHMENT_INDEX_PASS names={index.Count} uniqueAndDuplicate=true freshAfterChange=true animationTiming=unverified");
    }

    private static void VerifySkeletonAssembly(RuntimeNativeCreature.PreparedSource prepared,
        RuntimeNativeNifSkeleton actual, float units)
    {
        var direct = NativeNifMeshBuilder.BuildActorSkeleton(prepared.Skeleton, units);
        try
        {
            var expected = direct.Node;
            var observed = actual.Node;
            if (expected.GetBoneCount() != observed.GetBoneCount())
                throw new InvalidDataException("Staged skeleton changed source bone count.");
            for (var bone = 0; bone < expected.GetBoneCount(); bone++)
            {
                if (expected.GetBoneName(bone) != observed.GetBoneName(bone) ||
                    expected.GetBoneParent(bone) != observed.GetBoneParent(bone) ||
                    !expected.GetBoneRest(bone).IsEqualApprox(observed.GetBoneRest(bone)) ||
                    expected.GetBoneMeta(bone, "opennv_nif_block").AsInt32() != observed.GetBoneMeta(bone, "opennv_nif_block").AsInt32() ||
                    expected.GetBoneMeta(bone, "opennv_nif_flags").AsInt32() != observed.GetBoneMeta(bone, "opennv_nif_flags").AsInt32())
                    throw new InvalidDataException("Staged skeleton changed bone order, source metadata or rest transform.");
            }
            if (direct.GeometryAttachments.Count != actual.GeometryAttachments.Count)
                throw new InvalidDataException("Staged skeleton changed source geometry attachments.");
            for (var index = 0; index < direct.GeometryAttachments.Count; index++)
                if (direct.GeometryAttachments[index].ParentBone != actual.GeometryAttachments[index].ParentBone ||
                    direct.GeometryAttachments[index].Geometry.Block.Index != actual.GeometryAttachments[index].Geometry.Block.Index)
                    throw new InvalidDataException("Staged skeleton changed source geometry attachment order.");
            using var cancelled = new RuntimeNativeNifSkeleton.Assembly(prepared.Skeleton, units);
            if (cancelled.Advance()) throw new InvalidDataException("Skeleton completed before its cancellation boundary.");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var partial = typeof(RuntimeNativeNifSkeleton.Assembly).GetField("_skeleton", flags)?.GetValue(cancelled) as RuntimeNativeNifSkeleton
                ?? throw new InvalidDataException("Partial skeleton owner is missing.");
            var node = partial.Node;
            cancelled.Dispose();
            if (GodotObject.IsInstanceValid(node)) throw new InvalidDataException("Cancelled skeleton retained its detached node.");
            GD.Print($"OPENNV_CREATURE_SKELETON_ASSEMBLY_PASS bones={expected.GetBoneCount()} sourceOrder=true rest=true cancellation=true animation=unverified");
        }
        finally { direct.Node.Free(); }
    }

    private static void VerifyPartitionCancellation(RuntimeLiveContentSource content,
        RuntimeNativeCreature.PreparedSource prepared, float units)
    {
        var checkedBoundaries = 0;
        for (var index = 0; index < prepared.Models.Count; index++)
        {
            if (prepared.HardwarePartitions[index].Count == 0) continue;
            foreach (var boundary in new[] { "partition-upload:", "partition-publish:" })
            {
                var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(prepared.Skeleton, units);
                try
                {
                    using var part = RuntimeNativeNifMeshBuilder.ActorPartAssembly.TryCreate(prepared.Models[index], skeleton,
                        content, null, null, null, null, null, 0, prepared.ControlledNodes, prepared.HardwarePartitions[index]);
                    if (part is null) continue;
                    var before = skeleton.Node.GetChildCount();
                    var steps = 0;
                    while (!part.NextStage.StartsWith(boundary, StringComparison.Ordinal))
                    {
                        if (part.Advance() || ++steps > 10000)
                            throw new InvalidDataException($"Prepared partition never reached cancellation boundary {boundary}.");
                    }
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var root = typeof(RuntimeNativeNifMeshBuilder.ActorPartAssembly).GetField("_root", flags)?.GetValue(part) as Node3D
                        ?? throw new InvalidDataException("Partition assembly has no detached root.");
                    var retained = typeof(RuntimeNativeNifMeshBuilder.ActorPartAssembly).GetField("_partitionResources", flags)?.GetValue(part)
                        as System.Collections.IEnumerable ?? throw new InvalidDataException("Partition resource owner is missing.");
                    var owners = retained.Cast<object>().ToArray();
                    var held = owners.SelectMany(owner => owner.GetType().GetFields(flags | System.Reflection.BindingFlags.Public)
                        .Where(field => field.Name is "Skin" or "Mesh").Select(field => field.GetValue(owner)))
                        .OfType<GodotObject>().ToArray();
                    if (held.Length == 0 || boundary == "partition-publish:" && held.Length < 2)
                        throw new InvalidDataException("Cancellation did not reach its expected native resource boundary.");
                    part.Dispose();
                    if (GodotObject.IsInstanceValid(root) || held.Any(GodotObject.IsInstanceValid) || skeleton.Node.GetChildCount() != before)
                        throw new InvalidDataException("Cancelled partition retained resources or published its detached tree.");
                    checkedBoundaries++;
                }
                finally { skeleton.Node.Free(); }
            }
        }
        if (checkedBoundaries == 0) throw new InvalidDataException("No prepared skin partition cancellation boundaries were checked.");
        GD.Print($"OPENNV_CREATURE_PARTITION_CANCELLATION_PASS boundaries={checkedBoundaries} unpublished=true nativeResourcesRetired=true");
    }

    private static void VerifyCreaturePartAssembly(RuntimeLiveContentSource content, FalloutCreatureAppearance appearance,
        IReadOnlyList<RuntimeNativeNifScene> actualParts, float units)
    {
        var directory = appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
        var idle = FalloutCreatureAppearanceResolver.SelectIdle(appearance,
            new[] { directory + "/mtidle.kf", directory + "/locomotion/mtidle.kf" }
                .Where(path => content.TryResolve(path, null, out _)));
        if (!content.TryRead(idle, null, out var bytes, out _)) throw new FileNotFoundException(idle);
        var kf = FalloutNifFile.Read(bytes);
        var sequence = kf.Roots.Select(kf.ReadObject).OfType<FalloutNifControllerSequence>().Single();
        var targets = sequence.ControlledBlocks.Where(link => link.ControllerType is "NiTransformController" or "NiVisController")
            .Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal);
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(NativeOwnedNifCache.Read(content, appearance.SkeletonPath, out _), units);
        try
        {
            var checkedParts = 0;
            var cancelledAttachments = 0;
            for (var index = 0; index < appearance.Models.Count; index++)
            {
                var source = NativeOwnedNifCache.Read(content, appearance.Models[index], out _);
                using var eligible = RuntimeNativeNifMeshBuilder.ActorPartAssembly.TryCreate(source, skeleton, content,
                    null, null, null, null, null, 0, targets);
                using var attachment = eligible is null
                    ? RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly.TryCreate(source, skeleton, content, targets) : null;
                if (eligible is null && attachment is null) continue;
                if (attachment is not null)
                {
                    var childCount = skeleton.Node.GetChildCount();
                    _ = attachment.Advance();
                    var detachedRoot = typeof(RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly)
                        .GetField("_root", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(attachment) as Node3D
                        ?? throw new InvalidDataException("Staged attachment has no detached root.");
                    attachment.Dispose();
                    if (GodotObject.IsInstanceValid(detachedRoot) || skeleton.Node.GetChildCount() != childCount)
                        throw new InvalidDataException("Cancelled attachment leaked or published its detached tree.");
                    cancelledAttachments++;
                    if (source.Blocks.Any(block => block.TypeName == "NiParticleSystem"))
                    {
                        using var partial = RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly.TryCreate(source, skeleton, content, targets)
                            ?? throw new InvalidDataException("Particle attachment lost staged admission.");
                        var steps = 0;
                        while (partial.NextStage != "finalize:4")
                            if (partial.Advance() || ++steps > 10000)
                                throw new InvalidDataException("Particle preparation cancellation boundary was not reached.");
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var setup = typeof(RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly).GetField("_particleSetup", flags)?.GetValue(partial)
                            ?? throw new InvalidDataException("Prepared particle has no retained source setup.");
                        var particle = setup.GetType().GetProperty("Runtime")?.GetValue(setup) as RuntimeNifParticleSystem
                            ?? throw new InvalidDataException("Prepared particle has no detached native owner.");
                        partial.Dispose();
                        if (GodotObject.IsInstanceValid(particle) || skeleton.Node.GetChildCount() != childCount)
                            throw new InvalidDataException("Cancelled prepared particle retained or published its native owner.");
                        cancelledAttachments++;
                    }
                }
                var direct = NativeNifMeshBuilder.AddActorPart(source, skeleton, externalTransformTargets: targets, contentSource: content);
                try
                {
                    var actual = actualParts[index];
                    if (direct.Nodes != actual.Nodes || direct.Surfaces != actual.Surfaces ||
                        direct.Vertices != actual.Vertices || direct.Triangles != actual.Triangles)
                        throw new InvalidDataException("Incremental creature part changed geometry counts.");
                    Compare(direct.Root, actual.Root);
                    checkedParts++;
                }
                finally { direct.Root.Free(); }
            }
            GD.Print($"OPENNV_CREATURE_PART_ASSEMBLY_COMPARE reference={appearance.Reference} parts={checkedParts} cancelledAttachments={cancelledAttachments} targets={targets.Count} beforeAnimation=true gpuAndGameplayTiming=unverified");
        }
        finally { skeleton.Node.Free(); }
    }
}
