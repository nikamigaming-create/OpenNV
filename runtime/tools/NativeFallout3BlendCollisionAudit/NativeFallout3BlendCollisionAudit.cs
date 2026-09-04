using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeFallout3BlendCollisionAudit : Node
{
    private const string SkeletonPath = @"meshes\characters\_male\skeleton.nif";
    private const uint ExpectedBoneLod = 0;
    private const uint ExpectedBoneLodCount = 8;
    private static readonly int[] ExpectedBoneLodGroupSizes = [38, 4, 0, 0, 10, 3, 2, 5];

    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = ParseArguments(OS.GetCmdlineUserArgs());
            var manifestPath = Path.GetFullPath(arguments["source-stack"]);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            var dataRoot = Path.GetFullPath(manifest.GetProperty("roots")[0]
                .GetProperty("root").GetString()!);
            RuntimeOwnedContentSource.Configure(
                dataRoot,
                manifestPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                manifest.GetProperty("stackId").GetString());
            var source = RuntimeOwnedContentSource.Current!;
            if (source.Game != RuntimeOwnedContentSource.Fallout3Game)
                throw new InvalidDataException("Blend-collision audit requires standalone Fallout 3.");
            if (!source.TryRead(SkeletonPath, null, out var bytes, out var resolvedSource))
                throw new FileNotFoundException($"Fallout 3 skeleton is missing: {SkeletonPath}");

            var nif = FalloutNifFile.Read(bytes);
            var boneLodBlocks = nif.Blocks
                .Where(block => block.TypeName == "NiBSBoneLODController").ToArray();
            if (boneLodBlocks.Length != 1)
                throw new InvalidDataException(
                    $"Fallout 3 skeleton requires one bone LOD controller, found {boneLodBlocks.Length}.");
            foreach (var block in boneLodBlocks)
            {
                var controller = (FalloutNifBoneLodController)nif.ReadObject(block.Index);
                if (controller.Lod != ExpectedBoneLod ||
                    controller.LodCount != ExpectedBoneLodCount ||
                    controller.DeclaredNodeGroupCount != ExpectedBoneLodCount ||
                    !controller.NodeGroups.Select(group => group.Length)
                        .SequenceEqual(ExpectedBoneLodGroupSizes))
                    throw new InvalidDataException(
                        $"Bone LOD controller {block.Index} differs from the owned skeleton contract.");
                var targetName = controller.Time.Target == -1
                    ? "none"
                    : nif.ReadNode(controller.Time.Target).Name;
                var nextSummary = controller.Time.NextController == -1
                    ? "none"
                    : nif.ReadObject(controller.Time.NextController) switch
                    {
                        FalloutNifTransformController transform => DescribeTransformController(nif, transform),
                        var next => $"{next.Block.TypeName}:{next.Block.Index}",
                    };
                var groups = controller.NodeGroups.Select((group, groupIndex) =>
                    $"{groupIndex}:[{string.Join(',', group.Select(reference =>
                    {
                        if (reference < 0 || reference >= nif.Blocks.Count ||
                            nif.Blocks[reference].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode"))
                            throw new InvalidDataException(
                                $"Bone LOD controller {block.Index} group {groupIndex} has an invalid node reference.");
                        return $"{reference}:{nif.ReadNode(reference).Name}";
                    }))}]").ToArray();
                GD.Print(
                    $"OPENNV_NATIVE_FO3_BONE_LOD block={block.Index} bytes={block.Size} " +
                    $"next={controller.Time.NextController} " +
                    $"nextType={(controller.Time.NextController == -1 ? "none" : nif.Blocks[controller.Time.NextController].TypeName)} " +
                    $"flags=0x{controller.Time.Flags:x4} " +
                    $"frequency={controller.Time.Frequency:R} phase={controller.Time.Phase:R} " +
                    $"start={controller.Time.StartTime:R} stop={controller.Time.StopTime:R} " +
                    $"target={controller.Time.Target}:{targetName} nextSummary={nextSummary} " +
                    $"lod={controller.Lod} lods={controller.LodCount} " +
                    $"declaredNodeGroups={controller.DeclaredNodeGroupCount} " +
                    $"groupSizes=[{string.Join(',', controller.NodeGroups.Select(group => group.Length))}] " +
                    $"groups=[{string.Join(';', groups)}] rendered=false");
            }
            foreach (var block in nif.Blocks.Where(block => block.TypeName == "NiIntegerExtraData"))
            {
                var value = (FalloutNifIntegerExtraData)nif.ReadObject(block.Index);
                var owners = nif.Blocks
                    .Where(candidate => candidate.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
                    .Select(candidate => nif.ReadNode(candidate.Index))
                    .Where(candidate => candidate.ExtraData.Contains(block.Index))
                    .Select(candidate => $"{candidate.Block.Index}:{candidate.Name}")
                    .ToArray();
                if (owners.Length == 0)
                    throw new InvalidDataException(
                        $"Integer extra-data block {block.Index} has no decoded visual owner.");
                GD.Print(
                    $"OPENNV_NATIVE_FO3_INTEGER_EXTRA_DATA block={block.Index} " +
                    $"name={value.Name} value={value.Value} bytes={block.Size} " +
                    $"owners=[{string.Join(',', owners)}] rendered=false");
            }
            var blends = nif.Blocks
                .Where(block => block.TypeName == "bhkBlendCollisionObject")
                .Select(block => nif.ReadObject(block.Index) as FalloutNifCollisionObject ??
                    throw new InvalidDataException($"Blend collision block {block.Index} did not decode."))
                .ToArray();
            if (blends.Length == 0)
                throw new InvalidDataException("Fallout 3 skeleton has no blend collision objects.");

            var constrainedBodies = 0;
            var constraintBlocks = new HashSet<int>();
            foreach (var blend in blends)
            {
                if (!blend.IsBlend || !blend.HeirGain.HasValue || !blend.VelocityGain.HasValue)
                    throw new InvalidDataException($"Blend collision block {blend.Block.Index} lost its gain contract.");
                if (blend.Target < 0 || blend.Target >= nif.Blocks.Count ||
                    nif.Blocks[blend.Target].TypeName is not ("NiNode" or "NiBone"))
                    throw new InvalidDataException($"Blend collision block {blend.Block.Index} has an invalid target.");
                if (blend.Body < 0 || blend.Body >= nif.Blocks.Count ||
                    nif.ReadObject(blend.Body) is not FalloutNifRigidBody body)
                    throw new InvalidDataException($"Blend collision block {blend.Block.Index} has an invalid body.");
                var shapeType = body.Shape >= 0 && body.Shape < nif.Blocks.Count
                    ? nif.Blocks[body.Shape].TypeName
                    : "invalid";
                if (body.Constraints.Length != 0)
                    constrainedBodies++;
                foreach (var constraint in body.Constraints)
                {
                    if (constraint < 0 || constraint >= nif.Blocks.Count)
                        throw new InvalidDataException(
                            $"Rigid body {body.Block.Index} has an invalid constraint reference.");
                    constraintBlocks.Add(constraint);
                    var header = nif.ReadConstraintHeader(constraint);
                    GD.Print(
                        $"OPENNV_NATIVE_FO3_RAGDOLL_CONSTRAINT body={body.Block.Index} " +
                        $"constraint={constraint} type={nif.Blocks[constraint].TypeName} " +
                        $"bytes={nif.Blocks[constraint].Size} wrappedType={header.WrappedType} " +
                        $"entityA={header.EntityA} entityB={header.EntityB} priority={header.Priority} " +
                        $"undecodedPayloadBytes={header.UndecodedPayloadBytes} " +
                        "transport=identity-only rendered=false");
                }
                GD.Print(
                    $"OPENNV_NATIVE_FO3_BLEND_COLLISION block={blend.Block.Index} target={blend.Target} " +
                    $"targetType={nif.Blocks[blend.Target].TypeName} flags=0x{blend.Flags:x4} " +
                    $"body={blend.Body} bodyType={body.Block.TypeName} shape={body.Shape} " +
                    $"shapeType={shapeType} mass={body.Mass:R} motion={body.MotionSystem} " +
                    $"constraints={body.Constraints.Length} heirGain={blend.HeirGain.Value:R} " +
                    $"velocityGain={blend.VelocityGain.Value:R}");
            }

            GD.Print(
                $"OPENNV_NATIVE_FO3_BLEND_COLLISION_AUDIT_OK blocks={blends.Length} " +
                $"constrainedBodies={constrainedBodies} unconstrainedBodies={blends.Length - constrainedBodies} " +
                $"uniqueConstraints={constraintBlocks.Count} " +
                $"sha256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()} " +
                $"source={resolvedSource} cache=none writes=zero rendered=false");
            exitCode = 0;
        }
        catch (Exception error)
        {
            GD.PrintErr(
                $"OPENNV_NATIVE_FO3_BLEND_COLLISION_AUDIT_ERROR {error.GetType().Name}: {error.Message} " +
                $"inner={error.InnerException?.Message ?? "none"}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; ++index)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
                throw new ArgumentException("Audit arguments must be --name value pairs.");
            result.Add(args[index][2..], args[++index]);
        }
        if (!result.ContainsKey("source-stack"))
            throw new ArgumentException("Blend-collision audit requires --source-stack.");
        return result;
    }

    private static string DescribeTransformController(
        FalloutNifFile nif,
        FalloutNifTransformController controller)
    {
        if (nif.ReadObject(controller.Interpolator) is not FalloutNifTransformInterpolator interpolator)
            throw new InvalidDataException(
                $"Transform controller {controller.Block.Index} has an unsupported interpolator.");
        if (interpolator.Data == -1)
        {
            var target = nif.ReadNode(controller.Time.Target).Transform;
            return $"transform:{controller.Block.Index}:next={controller.Time.NextController}:" +
                $"flags=0x{controller.Time.Flags:x4}:frequency={controller.Time.Frequency:R}:" +
                $"phase={controller.Time.Phase:R}:start={controller.Time.StartTime:R}:" +
                $"stop={controller.Time.StopTime:R}:target={controller.Time.Target}:" +
                $"interpolator={interpolator.Block.Index}:translation={interpolator.Translation}:" +
                $"rotation={interpolator.Rotation}:scale={interpolator.Scale:R}:data=-1:" +
                $"targetTranslation={target.Translation}:targetScale={target.Scale:R}:" +
                $"targetRotation=[{string.Join(',', target.RotationRowMajor.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}]";
        }
        if (nif.ReadObject(interpolator.Data) is not FalloutNifTransformData data)
            throw new InvalidDataException(
                $"Transform interpolator {interpolator.Block.Index} has unsupported data.");
        return $"transform:{controller.Block.Index}:next={controller.Time.NextController}:" +
            $"flags=0x{controller.Time.Flags:x4}:frequency={controller.Time.Frequency:R}:" +
            $"phase={controller.Time.Phase:R}:start={controller.Time.StartTime:R}:" +
            $"stop={controller.Time.StopTime:R}:target={controller.Time.Target}:" +
            $"interpolator={interpolator.Block.Index}:translation={interpolator.Translation}:" +
            $"rotation={interpolator.Rotation}:scale={interpolator.Scale:R}:data={data.Block.Index}:" +
            $"rotationType={data.RotationType}:quaternionKeys={data.QuaternionRotations.Length}:" +
            $"xyzKeys={data.XyzRotations.Sum(axis => axis.Length)}:" +
            $"translationKeys={data.Translations.Length}:scaleKeys={data.Scales.Length}";
    }
}
