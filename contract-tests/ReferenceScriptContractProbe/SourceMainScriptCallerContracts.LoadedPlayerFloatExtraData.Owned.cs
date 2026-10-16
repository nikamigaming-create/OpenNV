using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    // Explicit file audit: callers select the owned archive/skeleton/KF. This
    // does not construct a winning gameplay Actor or certify the whole loader.
    internal static void AuditOwnedLoadedPlayerFloatExtraData(string archivePath, string skeletonPath, string sequencePath)
    {
        using var archive = new FalloutBsaArchive(archivePath);
        var skeleton = FalloutNifFile.Read(archive.Read(skeletonPath));
        var file = FalloutNifFile.Read(archive.Read(sequencePath));
        var model = FloatExtraModel(skeleton, skeletonPath); var scene = model.ConstructLogicalScene();
        try
        {
            var targets = scene.Objects.ToDictionary(node => node.Name, StringComparer.Ordinal); var fields = 0;
            foreach (var root in file.Roots)
            {
                var sequence = file.ReadControllerSequence(root);
                var links = sequence.ControlledBlocks.Where(link => link.ControllerType == "NiFloatExtraDataController" &&
                    targets.ContainsKey(link.NodeName)).ToArray();
                foreach (var link in links)
                {
                    var target = targets[link.NodeName]; var expected = new FalloutNifFloatAnimation(file, link.Interpolator);
                    var apply = scene.BindFloatExtraDataAnimation(target.Key, file, link);
                    foreach (var time in new[] { sequence.StartTime, (sequence.StartTime + sequence.StopTime) * .5f, sequence.StopTime })
                    {
                        apply(time);
                        Require(BitConverter.SingleToUInt32Bits(scene.ReadFloatExtraData(target.Key, link.Variable1)) ==
                            BitConverter.SingleToUInt32Bits(expected.Sample(time)), "Owned float animation changed its actual sample time or source target.");
                    }
                    ++fields;
                }
            }
            Require(fields > 0 && scene.Capture().FloatExtraData.Any(field => field.Stores > 0),
                "Owned file audit did not exercise a present declared float field/controller.");
            FalloutActorLogicalScene.RequireComplete(scene.Capture());
            Console.WriteLine($"OPENNV_OWNED_PLAYER_FLOAT_EXTRA_PASS fields={fields} samples={fields * 3} " +
                $"skeleton={skeleton.Sha256} sequence={file.Sha256} productLoader=UNEXECUTED gameplay=UNEXECUTED");
        }
        finally { scene.Retire(); }
    }
}
