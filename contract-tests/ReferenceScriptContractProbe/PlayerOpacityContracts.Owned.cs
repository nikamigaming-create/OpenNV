using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

internal static partial class PlayerOpacityContracts
{
    internal static int AuditOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var source = FalloutExecutableStringTable.ReadPlayerOpacity(content.FalloutExecutablePath, controls);
        var appearance = FalloutPlayerSourceAppearance.Select(FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7)), content, true, records);
        var prepared = FalloutNpcPreparedGeometry.Read(appearance, content);
        var model = prepared.SourceModel.Instantiate(); var scene = model.ConstructLogicalScene();
        try
        {
            // This is the selected owned assembly, not a prepared dummy graph or
            // a Godot pose query. Consume the original first-person idle KF too.
            var directory = appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
            var clipPath = content.ActorAnimations.Require(directory, "mtidle");
            if (!content.TryRead(clipPath, null, out var bytes, out _)) throw new FileNotFoundException("Owned idle resource is missing.", clipPath);
            var clip = FalloutNifFile.Read(bytes);
            var sequences = clip.Roots.Select(clip.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
            var requested = Path.GetFileNameWithoutExtension(clipPath);
            var matching = sequences.Where(sequence => requested.EndsWith(sequence.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var sequence = sequences.Length == 1 ? sequences[0] : matching.Length == 1 ? matching[0] :
                throw new InvalidDataException("Owned idle has no unique original source sequence.");
            scene.ResetBonePoses(sequence.TargetName);
            var names = scene.Objects.Where(node => node.Key.Asset == 0 && node.IsNode).Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
            var sampled = 0;
            foreach (var channel in sequence.ControlledBlocks)
            {
                if (channel.ControllerType != "NiTransformController" || channel.NodeName == sequence.TargetName || !names.Contains(channel.NodeName)) continue;
                var sampler = new FalloutNifAnimationSampler(clip, channel.Interpolator);
                scene.PublishBoneSample(channel.NodeName, sampler.Sample(sequence.StartTime)); sampled++;
            }
            scene.UpdateWorldAndBounds();
            Require(sampled > 0 && scene.Objects.Any(node => node.IsGeometry) && scene.Root.Bound is { } bound && bound.Sphere.Radius > 0,
                "Owned scene did not construct real geometry/current source-KF bounds.");
            var shaders = scene.Properties.Where(property => property.ShaderKind is not null and not -1).ToArray();
            Require(shaders.Length > 0, "Owned scene has no actual mutable runtime shader alpha fields.");
            var material = scene.Properties.ToDictionary(property => property.Key, property => property.MaterialAlphaBits);
            var result = scene.SetAlpha(Guid.NewGuid(), Bits(.375f));
            Require(result is { Step: FalloutNifSceneAlphaStep.Returned } && result.Stores.Count > 0 &&
                result.Stores.All(key => scene.RequireProperty(key).ShaderAlphaBits == Bits(.375f)) &&
                scene.Properties.All(property => property.MaterialAlphaBits == material[property.Key]),
                "Owned source traversal did not mutate real shader state independently from material alpha.");
            var saved = RoundTrip(scene.Capture()); FalloutActorLogicalScene.RequireComplete(saved);
            var cold = prepared.SourceModel.Instantiate().ConstructLogicalScene(saved);
            try
            {
                var restored = cold.Capture();
                Require(cold.Factory != scene.Factory && cold.Lifetime != scene.Lifetime && restored.RestoredLifetime == saved.Lifetime &&
                    restored.Objects.Select(PortableFields).SequenceEqual(saved.Objects.Select(PortableFields)) &&
                    restored.Properties.SequenceEqual(saved.Properties),
                    "Owned cold scene reused identities, omitted fields or replayed a source constructor/pose/alpha store.");
                var oldStores = restored.Properties.Sum(property => property.ShaderStores);
                cold.SetAlpha(Guid.NewGuid(), Bits(.625f));
                Require(cold.Capture().Properties.Sum(property => property.ShaderStores) > oldStores,
                    "Fresh owned cold scene had only metadata and could not execute a new alpha traversal.");
            }
            finally { cold.Retire(); }
            Require(FalloutGameSettingFloats.Read(records, FalloutPlayerOpacitySource.PlayerFadeSeconds) > 0 &&
                FalloutGameSettingFloats.Read(records, FalloutPlayerOpacitySource.ActorFadeSeconds) > 0,
                "Owned typed winning fade settings are unavailable.");
            Console.WriteLine($"OPENNV_OWNED_PLAYER_OPACITY_PASS source={source.Identity} actualObjects={scene.Objects.Count} " +
                $"actualShaders={shaders.Length} stores={result.Stores.Count} sampledSourceChannels={sampled} " +
                "coldFresh=true coldNoReplay=true renderer=UNEXECUTED canonicalParity=UNVERIFIED gameplay=UNEXECUTED");
            return 0;
        }
        finally { scene.Retire(); }
        static string PortableFields(FalloutNifSceneObjectSnapshot node) => System.Text.Json.JsonSerializer.Serialize(new
        {
            node.Key, node.Flags, node.Local, node.World, node.PoseStores, node.BoundStores,
            node.Parent, node.Children, node.Shader,
            Bound = node.Bound is { } bound ? new uint[] { bound.X, bound.Y, bound.Z, bound.Radius } : null
        });
    }
}
