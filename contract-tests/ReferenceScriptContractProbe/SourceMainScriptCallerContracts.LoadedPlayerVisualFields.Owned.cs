using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedLoadedPlayerVisualFields(string archivePath, string skeletonPath, string sequencePath)
    {
        using var archive = new FalloutBsaArchive(archivePath);
        var source = FalloutNifFile.Read(archive.Read(skeletonPath)); var file = FalloutNifFile.Read(archive.Read(sequencePath));
        var scene = FloatExtraModel(source, skeletonPath).ConstructLogicalScene();
        try
        {
            var targets = scene.Objects.ToDictionary(node => node.Name, StringComparer.Ordinal);
            var visibilityCount = 0; var morphCount = 0;
            foreach (var root in file.Roots)
            {
                var sequence = file.ReadControllerSequence(root);
                var binding = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file, sequence), scene);
                Require(binding.UnboundChannels.Count == 0, "Owned visual file audit left a present channel unbound.");
                foreach (var time in new[] { sequence.StartTime, (sequence.StartTime + sequence.StopTime) * .5f, sequence.StopTime })
                {
                    binding.ApplySourceTime(time); scene.UpdateWorldAndBounds();
                    foreach (var link in sequence.ControlledBlocks)
                    {
                        if (!targets.TryGetValue(link.NodeName, out var target)) continue;
                        if (link.ControllerType == "NiVisController")
                            Require(scene.ReadVisibility(target.Key) == new FalloutNifBoolAnimation(file, link.Interpolator).Sample(time),
                                "Owned visibility did not retain the actual boolean sample and source target.");
                        if (link.ControllerType == "NiGeomMorpherController")
                        {
                            var field = scene.RequireMorphField(target.Key); var index = field.RequireIndex(link.Variable2);
                            var expected = field.Declaration.EffectiveWeight(index, new FalloutNifFloatAnimation(file, link.Interpolator).Sample(time));
                            Require(BitConverter.SingleToUInt32Bits(scene.ReadMorphWeight(target.Key, link.Variable2)) == BitConverter.SingleToUInt32Bits(expected),
                                "Owned morph did not retain the ordered source target and effective scalar.");
                        }
                    }
                }
                visibilityCount += binding.VisibilityChannelCount; morphCount += binding.MorphChannelCount;
            }
            Require(visibilityCount > 0 && morphCount > 0 && scene.Capture().Morphs.Any(field => field.Weights.Any(weight => weight.Stores > 0)),
                "Owned audit did not exercise both present source visual channels.");
            var saved = scene.Capture(); FalloutActorLogicalScene.RequireComplete(saved);
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!;
                var cold = FloatExtraModel(source, skeletonPath).ConstructLogicalScene(saved);
                var after = cold.Capture(); FalloutActorLogicalScene.RequireColdRestoration(saved, after);
                saved = after; cold.Retire();
            }
            Console.WriteLine($"OPENNV_OWNED_PLAYER_VISUAL_FIELDS_PASS visibility={visibilityCount} morphs={morphCount} " +
                $"skeleton={source.Sha256} sequence={file.Sha256} cold3=true productLoader=UNEXECUTED gameplay=UNEXECUTED");
        }
        finally { scene.Retire(); }
    }
}
