using System.Text;
using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

var decoded = (FalloutNifSkinPartition)FalloutNifFile.Read(Wrap(PartitionBytes(), "NiSkinPartition")).ReadObject(0);
Require(FalloutNifHardwareSkin.VisibleOnIntactBody(null), "Ordinary skin was hidden.");
for (ushort body = 0; body <= 13; body++)
    Require(FalloutNifHardwareSkin.VisibleOnIntactBody(new(0, body)), "Intact body part was hidden.");
for (ushort body = 1; body <= 13; body++)
{
    Require(!FalloutNifHardwareSkin.VisibleOnIntactBody(new(0x101, (ushort)(100 + body))), "Section cap shown on intact actor.");
    Require(!FalloutNifHardwareSkin.VisibleOnIntactBody(new(0x101, (ushort)(200 + body))), "Torso cap shown on intact actor.");
    Require(FalloutNifHardwareSkin.VisibleOnIntactBody(new(0, (ushort)(1000 * body))), "Torso section hidden on intact actor.");
}
try
{
    FalloutNifHardwareSkin.VisibleOnIntactBody(new(0, 99));
    throw new InvalidOperationException("Unknown body-part layout was accepted.");
}
catch (NotSupportedException) { }
var partition = decoded.Partitions.Single();
Require(partition.Strips is { Length: 2 } && partition.Strips[0].SequenceEqual(new ushort[] { 0, 1, 2, 3 }), "Original strips lost.");
Require(partition.Triangles.SequenceEqual(new FalloutNifTriangle[] { new(0, 1, 2), new(2, 1, 3), new(1, 3, 0) }),
    "Strip parity or restart winding differs.");
var identity = new FalloutNifTransform(new(0, 0, 0), [1, 0, 0, 0, 1, 0, 0, 0, 1], 1);
var instance = new FalloutNifSkinInstance(new(2, "NiSkinInstance", 0, 0), 1, 0, 3, [4, 5], []);
var data = new FalloutNifSkinData(new(1, "NiSkinData", 0, 0), identity, false,
    [new(identity, new(0, 0, 0), 1, []), new(identity, new(0, 0, 0), 1, [])]);
var binding = FalloutNifHardwareSkin.Read(instance, data, decoded, 4).Single();
Require(binding.VertexMap.SequenceEqual(new ushort[] { 2, 0, 3, 1 }) &&
    binding.BonePalette.SequenceEqual(new ushort[] { 1, 0 }), "Source palette or vertex order changed.");
for (var row = 0; row < 4; row++)
    for (var influence = 0; influence < 4; influence++)
        Require(BitConverter.SingleToInt32Bits(binding.Weights[row * 4 + influence]) ==
            BitConverter.SingleToInt32Bits(partition.VertexWeights[row][influence]), "Weight bits changed.");
var invalid = partition with { BoneIndices = partition.BoneIndices.Select(row => row.ToArray()).ToArray() };
invalid.BoneIndices[0][0] = 2;
ExpectInvalid(() => FalloutNifHardwareSkin.Read(instance, data, decoded with { Partitions = [invalid] }, 4));
invalid = partition with { VertexWeights = partition.VertexWeights.Select(row => row.ToArray()).ToArray() };
invalid.VertexWeights[0][0] = float.NaN;
ExpectInvalid(() => FalloutNifHardwareSkin.Read(instance, data, decoded with { Partitions = [invalid] }, 4));
Console.WriteLine("OPENNV_NIF_SKINNING_CONTRACT_OK stripRestarts=true palettes=true originalWeightBits=true");

if (args.Length is 3 or 4)
{
    using var archive = new FalloutBsaArchive(Path.Combine(args[0], "Fallout - Meshes.bsa"));
    var skeleton = FalloutNifFile.Read(archive.Read(args[1]));
    var headBinds = args.Length == 4 ? FalloutNpcFaceAttachment.ReadHeadBinds(FalloutNifFile.Read(archive.Read(args[3]))) : null;
    var names = skeleton.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
        .Select(block => skeleton.ReadNode(block.Index).Name).ToHashSet(StringComparer.Ordinal);
    var models = 0;
    var partitions = 0;
    var weightedVertices = 0;
    var rigidAttachments = 0;
    foreach (var model in File.ReadLines(args[2]).Where(line => !string.IsNullOrWhiteSpace(line)))
    {
        var source = FalloutNifFile.Read(archive.Read(model));
        var worldTransforms = SourceWorldTransforms(source);
        foreach (var geometry in source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips").Select(block => source.ReadGeometry(block.Index)))
        {
            var mesh = source.ReadMeshData(geometry.Data);
            Console.WriteLine($"OPENNV_OWNED_ACTOR_GEOMETRY model={model} name={geometry.Name} block={geometry.Block.Index} skin={geometry.SkinInstance} uvSets={mesh.TextureCoordinates.Length} properties={string.Join(',', geometry.Properties.Where(index => index >= 0).Select(index => source.Blocks[index].TypeName))} translation={geometry.Transform.Translation} rotation={string.Join(',', geometry.Transform.RotationRowMajor)} scale={geometry.Transform.Scale:R}");
        }
        foreach (var root in source.Roots.Select(source.ReadNode))
            foreach (var extra in root.ExtraData.Where(index => index >= 0).Select(source.ReadObject)
                .OfType<FalloutNifStringExtraData>().Where(extra => extra.Name == "Prn"))
            {
                Require(names.Contains(extra.Value), $"Source attachment bone missing: {model}/{extra.Value}");
                Console.WriteLine($"OPENNV_OWNED_PRN model={model} root={root.Block.Index} parent={extra.Value} translation={root.Transform.Translation} rotation={string.Join(',', root.Transform.RotationRowMajor)}");
                rigidAttachments++;
                if (headBinds is not null)
                {
                    Require(headBinds.ContainsKey(extra.Value), $"Rigid FaceGen part lacks a source head bind: {model}/{extra.Value}");
                    Console.WriteLine($"OPENNV_OWNED_FACE_ATTACHMENT_BOUND model={model} bone={extra.Value} owner=source-head-skin");
                }
            }
        foreach (var geometry in source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Select(block => source.ReadGeometry(block.Index)).Where(geometry => geometry.SkinInstance >= 0))
        {
            var mesh = source.ReadMeshData(geometry.Data);
            var skin = (FalloutNifSkinInstance)source.ReadObject(geometry.SkinInstance);
            var skinData = (FalloutNifSkinData)source.ReadObject(skin.Data);
            Require(Matrix4x4.Invert(ToMatrix(skinData.SkinTransform), out var skinModel), "Non-invertible source skin frame.");
            Require(Matrix4x4.Invert(worldTransforms[skin.SkeletonRoot], out var rootInverse), "Non-invertible source skeleton frame.");
            var bindResidual = skin.Bones.Select((bone, index) => MatrixDifference(
                ToMatrix(skinData.Bones[index].SkinTransform) * worldTransforms[bone] * rootInverse, skinModel)).Max();
            Require(bindResidual < 0.001f, $"Source Float32 skin rest identity failed: {model}/{geometry.Name}/{bindResidual:R}");
            Console.WriteLine($"OPENNV_OWNED_SKIN_REST_RESIDUAL model={model} geometry={geometry.Name} maximum={bindResidual:R}");
            Console.WriteLine($"OPENNV_OWNED_SKIN_FRAME model={model} geometry={geometry.Name} translation={skinData.SkinTransform.Translation} rotation={string.Join(',', skinData.SkinTransform.RotationRowMajor)}");
            for (var boneIndex = 0; boneIndex < skin.Bones.Length; boneIndex++)
            {
                var sourceBone = source.ReadNode(skin.Bones[boneIndex]);
                var inverseBind = skinData.Bones[boneIndex].SkinTransform;
                Console.WriteLine($"OPENNV_OWNED_SKIN_BIND model={model} geometry={geometry.Name} bone={sourceBone.Name} translation={inverseBind.Translation} rotation={string.Join(',', inverseBind.RotationRowMajor)}");
            }
            foreach (var bone in skin.Bones)
                Require(names.Contains(source.ReadNode(bone).Name), $"Source skin bone missing: {model}/{bone}");
            var result = FalloutNifHardwareSkin.Read(skin, skinData,
                (FalloutNifSkinPartition)source.ReadObject(skin.SkinPartition), mesh.Vertices.Length);
            partitions += result.Count;
            foreach (var part in result)
                Console.WriteLine($"OPENNV_OWNED_PARTITION_VISIBILITY model={model} geometry={geometry.Block.Index} partition={part.PartitionIndex} bodyPart={part.BodyPart?.BodyPart} intactVisible={FalloutNifHardwareSkin.VisibleOnIntactBody(part.BodyPart)}");
            weightedVertices += result.Sum(part => part.VertexMap.Length);
        }
        models++;
    }
    Console.WriteLine($"OPENNV_NIF_SKINNING_OWNED_AUDIT models={models} skeletonNodes={names.Count} " +
        $"partitions={partitions} weightedVertices={weightedVertices} rigidAttachments={rigidAttachments} parity=unverified");
}

static Matrix4x4 ToMatrix(FalloutNifTransform transform)
{
    var r = transform.RotationRowMajor;
    var s = transform.Scale;
    var t = transform.Translation;
    // System.Numerics uses row-vector matrices; the decoded contract is row-major
    // storage of a column-vector transform.
    return new(r[0] * s, r[3] * s, r[6] * s, 0,
        r[1] * s, r[4] * s, r[7] * s, 0, r[2] * s, r[5] * s, r[8] * s, 0,
        t.X, t.Y, t.Z, 1);
}

static Dictionary<int, Matrix4x4> SourceWorldTransforms(FalloutNifFile source)
{
    var result = new Dictionary<int, Matrix4x4>();
    void Visit(int index, Matrix4x4 parent)
    {
        if (source.Blocks[index].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode")) return;
        var node = source.ReadNode(index);
        var world = ToMatrix(node.Transform) * parent;
        if (!result.TryAdd(index, world)) throw new InvalidDataException("Multiple source parents.");
        foreach (var child in node.Children.Where(child => child >= 0)) Visit(child, world);
    }
    foreach (var root in source.Roots) Visit(root, Matrix4x4.Identity);
    return result;
}

static float MatrixDifference(Matrix4x4 a, Matrix4x4 b) => new[] {
    a.M11-b.M11, a.M12-b.M12, a.M13-b.M13, a.M21-b.M21, a.M22-b.M22, a.M23-b.M23,
    a.M31-b.M31, a.M32-b.M32, a.M33-b.M33, a.M41-b.M41, a.M42-b.M42, a.M43-b.M43,
}.Max(MathF.Abs);

static byte[] PartitionBytes()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(1U);
    foreach (var value in new ushort[] { 4, 3, 2, 2, 4, 1, 0 }) writer.Write(value);
    writer.Write(true);
    foreach (var value in new ushort[] { 2, 0, 3, 1 }) writer.Write(value);
    writer.Write(true);
    for (var row = 0; row < 4; row++)
        foreach (var value in new float[] { 0.75f, 0.25f, -0.0f, 0.0f }) writer.Write(value);
    writer.Write((ushort)4); writer.Write((ushort)3); writer.Write(true);
    foreach (var value in new ushort[] { 0, 1, 2, 3, 1, 3, 0 }) writer.Write(value);
    writer.Write(true);
    for (var row = 0; row < 4; row++) writer.Write(new byte[] { 0, 1, 0, 0 });
    return stream.ToArray();
}

static byte[] Wrap(byte[] body, string type)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
    writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
    writer.Write(1U); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
    writer.Write((ushort)1); writer.Write(type.Length); writer.Write(Encoding.ASCII.GetBytes(type));
    writer.Write((ushort)0); writer.Write(body.Length); writer.Write(0U); writer.Write(0U); writer.Write(0U);
    writer.Write(body); writer.Write(1U); writer.Write(0);
    return stream.ToArray();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action operation)
{
    try { operation(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Invalid skin was accepted.");
}
