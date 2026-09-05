using System.Numerics;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

var spline = Spline(false, 4, [0, 2, 0, 1, 2, 0, 2, 2, 0, 3, 2, 0]);
var sampler = new FalloutNifAnimationSampler(spline, 0);
foreach (var time in new float[] { 0, 0.25f, 0.5f, 0.75f, 1 })
{
    var sample = sampler.Sample(time);
    Near(sample.Translation!.Value.X, 3 * time, "Cubic affine reproduction");
    Near(sample.Translation.Value.Y, 2, "Cubic constant reproduction");
    Require(sample.Rotation is null && sample.Scale is null, "Absent channels invented a pose.");
}
var compact = Spline(true, 4, [], [-32767, -1, 0, -10922, -1, 0, 10922, -1, 0, 32767, -1, 0]);
var interior = new FalloutNifAnimationSampler(Spline(false, 5,
    [0, 0, 0, 0.5f, 0, 0, 1.5f, 0, 0, 2.5f, 0, 0, 3, 0, 0]), 0);
foreach (var time in new[] { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f })
    Near(interior.Sample(time).Translation!.Value.X, 3 * time, "Interior-knot affine reproduction");
var compactSampler = new FalloutNifAnimationSampler(compact, 0);
Near(compactSampler.Sample(0).Translation!.Value.X, -3, "Compact negative endpoint");
Near(compactSampler.Sample(1).Translation!.Value.X, 7, "Compact positive endpoint");
Require(((FalloutNifSplineData)compact.ReadObject(1)).CompactControlPoints[1] == -1,
    "Signed compact source bits were changed.");
ExpectInvalid(() => new FalloutNifAnimationSampler(Spline(false, 5, [0, 0, 0]), 0));
ExpectInvalid(() => new FalloutNifAnimationSampler(Spline(false, 3, new float[9]), 0));
var keyed = new FalloutNifAnimationSampler(Keyed(), 0);
var middle = keyed.Sample(0.5f);
Near(middle.Translation!.Value.X, 5, "Linear keyed translation");
Require(middle.Scale is null, "Unset keyed scale was replaced.");
var quaternion = middle.Rotation!.Value;
var point = Vector3.Transform(Vector3.UnitX, new Quaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W));
Near(point.X, 0, "XYZ composed X"); Near(point.Y, 0, "XYZ composed Y"); Near(point.Z, -1, "XYZ composed Z");
Console.WriteLine("OPENNV_NIF_ANIMATION_CONTRACT_OK cubicEndpoints=true sourceSentinels=true compactSigned=true xyzOrder=true");

var floats = FloatKeys();
var floatState = new FalloutNifFloatExtraDataState();
floatState.Add("Synthetic Joint", "Authored Parameter", -7);
var floatLink = new FalloutNifControllerLink("Synthetic Joint", "", "NiFloatExtraDataController",
    "Authored Parameter", "", 0, -1, 0);
var applyFloat = floatState.Bind(floats, floatLink);
applyFloat(0.5f);
Near(floatState.Values.Single().Value, 6, "Declared float target interpolation");
applyFloat(10);
Near(floatState.Values.Single().Value, 10, "Scalar endpoint clamping");
ExpectInvalid(() => floatState.Bind(floats, floatLink with { Variable1 = "Missing" }));
ExpectInvalid(() => floatState.Add("Synthetic Joint", "Authored Parameter", 1));
ExpectInvalid(() => new FalloutNifFloatAnimation(Wrap(("NiFloatInterpolator",
    Bytes(writer => { writer.Write(float.MinValue); writer.Write(-1); }))), 0));
var constantFloat = new FalloutNifFloatAnimation(Wrap(("NiFloatInterpolator",
    Bytes(writer => { writer.Write(4.25f); writer.Write(-1); }))), 0);
Near(constantFloat.Sample(9), 4.25f, "Authored constant scalar");
Console.WriteLine("OPENNV_NIF_FLOAT_CHANNEL_OK declaredTargets=true arbitraryNames=true linearKeys=true constant=true missingTargetFails=true unsetFails=true");

var booleanSource = Wrap(("NiBoolInterpolator", Bytes(writer => { writer.Write((byte)2); writer.Write(1); })),
    ("NiBoolData", Bytes(writer =>
    {
        writer.Write(2); writer.Write(5U);
        writer.Write(1.0f); writer.Write((byte)0);
        writer.Write(2.0f); writer.Write((byte)1);
    })));
var boolean = new FalloutNifBoolAnimation(booleanSource, 0);
Require(!boolean.Sample(-1) && !boolean.Sample(1.99f) && boolean.Sample(2) && boolean.Sample(10),
    "Boolean animation lost step timing or endpoint clamping.");
ExpectInvalid(() => new FalloutNifBoolAnimation(Wrap(("NiBoolInterpolator", Bytes(writer => { writer.Write((byte)2); writer.Write(-1); }))), 0));
var morphSource = WrapNamed(["SyntheticBase", "SyntheticExpression"], ("NiMorphData", Bytes(writer =>
{
    writer.Write(2); writer.Write(1); writer.Write((byte)1);
    writer.Write(0); writer.Write(2.0f); writer.Write(3.0f); writer.Write(4.0f);
    writer.Write(1); writer.Write(-1.0f); writer.Write(0.0f); writer.Write(1.0f);
})));
var morphData = (FalloutNifMorphData)morphSource.ReadObject(0);
Require(morphData.RelativeTargets == 1 && morphData.Morphs[1].Name == "SyntheticExpression" &&
    morphData.Morphs[1].Vectors.Single().X == -1, "Morph declaration changed its named source vectors.");
Console.WriteLine("OPENNV_NIF_MORPH_DECLARATION_OK namedTargets=true sourceVectors=true visibilitySteps=true unsetFails=true");

var morphGeometrySource = MorphGeometryFixture();
var morphGeometry = new FalloutNifMorphGeometry(morphGeometrySource, morphGeometrySource.ReadGeometry(0));
Require(morphGeometry.Index("duplicate") == 1 && morphGeometry.Data.Morphs.Length == 3,
    "Duplicate morph names lost their source indices.");
Require(morphGeometry.BaseGeometry(morphGeometrySource.ReadMeshData(3)).Vertices.Single() == new FalloutNifVector3(2, 3, 4),
    "Relative morph target zero did not replace the source geometry base.");
Require(morphGeometry.RelativeDeltas().Values.Select(values => values.Single()).SequenceEqual(new[] { new Vector3(-1, 0, 1), new Vector3(7, -2, 0) }),
    "Relative morph vectors were normalized, subtracted or reordered.");
Require(morphGeometry.EffectiveWeight(0, 0) == 1 && morphGeometry.EffectiveWeight(0, -2) == 1 &&
    morphGeometry.EffectiveWeight(1, -2) == -2, "Relative target zero or signed delta weights changed.");
ExpectInvalid(() => morphGeometry.Index("Missing"));
ExpectInvalid(() => morphGeometry.EffectiveWeight(3, 1));
ExpectInvalid(() => morphGeometry.EffectiveWeight(1, float.NaN));
var mismatchedMorphs = MorphGeometryFixture(weightCount: 2);
ExpectInvalid(() => new FalloutNifMorphGeometry(mismatchedMorphs, mismatchedMorphs.ReadGeometry(0)));
Console.WriteLine("OPENNV_NIF_MORPH_GEOMETRY_OK sourceBase=true relativeDeltas=true baseWeightOne=true signedWeights=true duplicateNames=true mismatchFails=true");

var cameraHierarchy = WrapNamed(["ProbeParent", "ProbeCamera"],
    ("NiNode", Node(0, 37, [1])), ("NiNode", Node(1, 5, [])));
var cameraClip = WrapNamed(["ProbeClip", "ProbeParent", "ProbeCamera", "NiTransformController"],
    ("NiControllerSequence", Bytes(writer =>
    {
        writer.Write(0); writer.Write(2); writer.Write(0);
        foreach (var target in new[] { 1, 2 })
        {
            writer.Write(target); writer.Write(-1); writer.Write((byte)7);
            writer.Write(target); writer.Write(-1); writer.Write(3); writer.Write(-1); writer.Write(-1);
        }
        writer.Write(1.0f); writer.Write(-1); writer.Write(2U); writer.Write(1.0f);
        writer.Write(0.0f); writer.Write(1.0f); writer.Write(-1); writer.Write(1); writer.Write((ushort)0);
    })),
    ("NiTransformInterpolator", ConstantTransform(11)),
    ("NiTransformInterpolator", ConstantTransform(23)));
var cameraPath = new FalloutNifAnimatedNodePath(cameraHierarchy, cameraClip, "ProbeCamera");
var cameraPose = cameraPath.Sample(0.5f);
Require(cameraPath.AnimatedPathNodes == 2 && cameraPose.Count == 2 && cameraPath.UnboundOtherTargets == 0,
    "The camera path dropped an animated ancestor.");
Near(cameraPose[0].Sample!.Translation!.Value.Z, 11, "Animated camera parent");
Near(cameraPose[1].Sample!.Translation!.Value.Z, 23, "Animated camera node");
ExpectInvalid(() => new FalloutNifAnimatedNodePath(cameraHierarchy, cameraClip, "MissingCamera"));
Console.WriteLine("OPENNV_CAMERA_PATH_CONTRACT_OK ancestorAnimation=true arbitraryNames=true missingTargetFails=true");

if (args is ["--list", var listRoot, var contains])
{
    foreach (var archivePath in Directory.EnumerateFiles(listRoot, "*.bsa"))
    {
        using var archive = new FalloutBsaArchive(archivePath);
        foreach (var path in archive.MemberPaths.Where(path => path.Contains(contains, StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"{Path.GetFileName(archivePath)}::{path}");
    }
    return;
}

if (args is ["--inspect-geometry", var geometryRoot, var geometryModel, var geometryName])
{
    using var archive = new FalloutBsaArchive(Path.Combine(geometryRoot, "Fallout - Meshes.bsa"));
    var model = FalloutNifFile.Read(archive.Read(geometryModel));
    var geometry = model.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
        .Select(block => model.ReadGeometry(block.Index)).Single(value => value.Name == geometryName);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(model.ReadMeshData(geometry.Data)));
    return;
}

if (args is ["--inspect-model", var modelRoot, var modelPath])
{
    using var archive = new FalloutBsaArchive(Path.Combine(modelRoot, "Fallout - Meshes.bsa"));
    var model = FalloutNifFile.Read(archive.Read(modelPath));
    foreach (var group in model.Blocks.GroupBy(block => block.TypeName))
        Console.WriteLine($"BLOCK_TYPE {group.Key} count={group.Count()}");
    foreach (var block in model.Blocks)
    {
        try
        {
            switch (model.ReadObject(block.Index))
            {
                case FalloutNifGeometry geometry:
                    Console.WriteLine($"GEOMETRY block={block.Index} name={geometry.Name} flags={geometry.Flags} controller={geometry.Controller} data={geometry.Data} properties={string.Join(',', geometry.Properties)}");
                    Console.WriteLine($"GEOMETRY_TRANSFORM block={block.Index} {System.Text.Json.JsonSerializer.Serialize(geometry.Transform)}");
                    var mesh = model.ReadMeshData(geometry.Data);
                    var vertices = mesh.Vertices.Select(value => new System.Numerics.Vector3(value.X, value.Y, value.Z)).ToArray();
                    Console.WriteLine($"GEOMETRY_BOUNDS block={block.Index} min={vertices.Aggregate(System.Numerics.Vector3.Min)} max={vertices.Aggregate(System.Numerics.Vector3.Max)} uv={System.Text.Json.JsonSerializer.Serialize(mesh.TextureCoordinates.Select(values => new { minU = values.Min(uv => uv.U), minV = values.Min(uv => uv.V), maxU = values.Max(uv => uv.U), maxV = values.Max(uv => uv.V) }))}");
                    break;
                case FalloutNifNode node:
                    Console.WriteLine($"NODE block={block.Index} name={node.Name} flags={node.Flags} controller={node.Controller} transform={System.Text.Json.JsonSerializer.Serialize(node.Transform)} children={string.Join(',', node.Children)} effects={string.Join(',', node.Effects)}");
                    break;
                case FalloutNifPointLight light:
                    Console.WriteLine($"POINT_LIGHT {System.Text.Json.JsonSerializer.Serialize(light)}");
                    break;
                case FalloutNifBound bound:
                    Console.WriteLine($"SOURCE_BOUND {System.Text.Json.JsonSerializer.Serialize(bound)}");
                    break;
                case FalloutNifSourceTexture texture:
                    Console.WriteLine($"TEXTURE block={block.Index} path={texture.FileName}");
                    Console.WriteLine($"SOURCE_TEXTURE {System.Text.Json.JsonSerializer.Serialize(texture)}");
                    break;
                case FalloutNifNoLightingProperty shader:
                    Console.WriteLine($"NO_LIGHTING {System.Text.Json.JsonSerializer.Serialize(shader)}");
                    break;
                case FalloutNifMaterialProperty material:
                    Console.WriteLine($"MATERIAL {System.Text.Json.JsonSerializer.Serialize(material)}");
                    break;
                case FalloutNifShaderTextureSet textureSet:
                    Console.WriteLine($"TEXTURE_SET block={block.Index} paths={string.Join(',', textureSet.Textures)}");
                    break;
                case FalloutNifStringExtraData extra:
                    Console.WriteLine($"STRING_EXTRA block={block.Index} name={extra.Name} value={extra.Value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " | ", StringComparison.Ordinal)}");
                    break;
                case FalloutNifMorphData morphs:
                    Console.WriteLine($"MORPH_DATA block={block.Index} relative={morphs.RelativeTargets} names={string.Join(',', morphs.Morphs.Select(morph => morph.Name))}");
                    foreach (var morph in morphs.Morphs)
                        Console.WriteLine($"MORPH_SHAPE name={morph.Name} vertices={morph.Vectors.Length} nonzero={string.Join(';', morph.Vectors.Select((value, index) => (value, index)).Where(row => row.value.X != 0 || row.value.Y != 0 || row.value.Z != 0).Take(8))}");
                    break;
                case FalloutNifMorphController controller:
                    Console.WriteLine($"MORPH_CONTROLLER block={block.Index} target={controller.Time.Target} flags={controller.Flags} count={controller.Weights.Length}");
                    break;
                case FalloutNifVisibilityController visibility:
                    Console.WriteLine($"VISIBILITY_CONTROLLER {System.Text.Json.JsonSerializer.Serialize(visibility)}");
                    break;
                case FalloutNifTransformController transform:
                    Console.WriteLine($"TRANSFORM_CONTROLLER {System.Text.Json.JsonSerializer.Serialize(transform)}");
                    break;
                case FalloutNifTextKeyExtraData keys:
                    foreach (var key in keys.Keys)
                        Console.WriteLine($"TEXT_KEY time={key.Time:R} value={key.Value.Replace("\n", " | ", StringComparison.Ordinal)}");
                    break;
                case FalloutNifControllerSequence clip:
                    Console.WriteLine($"SEQUENCE name={clip.Name} start={clip.StartTime:R} stop={clip.StopTime:R} cycle={clip.CycleType} frequency={clip.Frequency:R} weight={clip.Weight:R}");
                    foreach (var link in clip.ControlledBlocks)
                    {
                        Console.WriteLine($"LINK node={link.NodeName} controller={link.ControllerType} property={link.PropertyType} variable={link.Variable1}/{link.Variable2} priority={link.Priority}");
                        if (link.ControllerType == "NiVisController")
                            Console.WriteLine($"VISIBILITY_VALUES start={new FalloutNifBoolAnimation(model, link.Interpolator).Sample(clip.StartTime)} end={new FalloutNifBoolAnimation(model, link.Interpolator).Sample(clip.StopTime)}");
                        if (link.ControllerType == "NiGeomMorpherController")
                            Console.WriteLine($"MORPH_VALUES start={new FalloutNifFloatAnimation(model, link.Interpolator).Sample(clip.StartTime)} end={new FalloutNifFloatAnimation(model, link.Interpolator).Sample(clip.StopTime)}");
                    }
                    break;
            }
        }
        catch (Exception error) { Console.WriteLine($"UNBOUND block={block.Index} type={block.TypeName}: {error.Message}"); }
    }
    return;
}

if (args.Length is 2 or 3)
{
    using var archive = new FalloutBsaArchive(Path.Combine(args[0], "Fallout - Meshes.bsa"));
    var source = FalloutNifFile.Read(archive.Read(args[1]));
    FalloutNifFloatExtraDataState? ownedFloats = null;
    if (args.Length == 3)
    {
        ownedFloats = new();
        var skeleton = FalloutNifFile.Read(archive.Read(args[2]));
        if (skeleton.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
            .Any(block => skeleton.ReadNode(block.Index).Name == "Camera1st") &&
            source.Roots.Any(index => source.ReadControllerSequence(index).ControlledBlocks.Any(link => link.NodeName == "Camera1st")))
        {
            var path = new FalloutNifAnimatedNodePath(skeleton, source, "Camera1st");
            foreach (var time in new[] { path.Sequence.StartTime, (path.Sequence.StartTime + path.Sequence.StopTime) / 2, path.Sequence.StopTime })
            {
                var pose = path.Sample(time);
                Console.WriteLine($"OPENNV_OWNED_CAMERA_PATH time={time:R} parents={pose.Count - 1} animatedPathNodes={path.AnimatedPathNodes} " +
                    $"otherTargetsUnbound={path.UnboundOtherTargets} cameraTranslation={pose[^1].Sample?.Translation}");
            }
        }
        foreach (var block in skeleton.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode"))
        {
            var node = skeleton.ReadNode(block.Index);
            foreach (var extra in node.ExtraData.Where(index => index >= 0).Select(skeleton.ReadObject).OfType<FalloutNifFloatExtraData>())
                ownedFloats.Add(node.Name, extra.Name, extra.Value);
        }
    }
    var sequences = source.Roots.Select(source.ReadControllerSequence).ToArray();
    var transforms = 0; var other = 0; var animated = 0; var samples = 0;
    foreach (var sequence in sequences)
        foreach (var link in sequence.ControlledBlocks)
        {
            if (link.ControllerType != "NiTransformController")
            {
                if (link.ControllerType == "NiFloatExtraDataController" && ownedFloats is not null)
                {
                    var apply = ownedFloats.Bind(source, link);
                    for (var tick = 0; tick <= 120; tick++)
                        apply(sequence.StartTime + (sequence.StopTime - sequence.StartTime) * tick / 120.0f);
                    Console.WriteLine($"OPENNV_OWNED_FLOAT_CHANNEL_BOUND node={link.NodeName} extra={link.Variable1} samples=121");
                    continue;
                }
                other++;
                Console.WriteLine($"OPENNV_ANIMATION_CHANNEL node={link.NodeName} controller={link.ControllerType} property={link.PropertyType} variable1={link.Variable1} variable2={link.Variable2} interpolator={link.Interpolator} block={source.Blocks[link.Interpolator].TypeName}");
                if (source.ReadObject(link.Interpolator) is FalloutNifFloatInterpolator extraFloat)
                {
                    Console.WriteLine($"OPENNV_ANIMATION_FLOAT value={extraFloat.Value} data={extraFloat.Data}");
                    if (extraFloat.Data >= 0 && source.ReadObject(extraFloat.Data) is FalloutNifFloatData floatData)
                        foreach (var key in floatData.Keys) Console.WriteLine($"OPENNV_ANIMATION_FLOAT_KEY {key}");
                }
                continue;
            }
            var channel = new FalloutNifAnimationSampler(source, link.Interpolator);
            FalloutNifAnimationSample? first = null;
            var changed = false;
            for (var tick = 0; tick <= 120; tick++)
            {
                var sample = channel.Sample(sequence.StartTime + (sequence.StopTime - sequence.StartTime) * tick / 120.0f);
                if (sample.Translation is { } position)
                    Require(float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z), "Nonfinite source animation translation.");
                if (sample.Rotation is { } rotation)
                    Require(float.IsFinite(rotation.W) && float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) &&
                        rotation.W * rotation.W + rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z > 0, "Invalid source animation rotation.");
                first ??= sample;
                changed |= sample != first;
                samples++;
            }
            transforms++; if (changed) animated++;
        }
    Console.WriteLine($"OPENNV_NIF_ANIMATION_OWNED_AUDIT sequences={sequences.Length} transformChannels={transforms} " +
        $"changingChannels={animated} unboundNonTransformChannels={other} samples={samples} parity=unverified selection=unassigned");
}

static FalloutNifFile FloatKeys() => Wrap(
    ("NiFloatInterpolator", Bytes(writer => { writer.Write(float.MinValue); writer.Write(1); })),
    ("NiFloatData", Bytes(writer =>
    {
        writer.Write(2); writer.Write(1U);
        writer.Write(0.0f); writer.Write(2.0f);
        writer.Write(1.0f); writer.Write(10.0f);
    })));

static FalloutNifFile MorphGeometryFixture(int weightCount = 3) => WrapNamed(["Arbitrary Geometry", "Arbitrary Base", "duplicate"],
    ("NiTriShape", Bytes(writer =>
    {
        writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(14U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0); writer.Write(-1); writer.Write(3); writer.Write(-1);
        writer.Write(0); writer.Write(-1); writer.Write((byte)0);
    })),
    ("NiGeomMorpherController", Bytes(writer =>
    {
        writer.Write(-1); writer.Write((ushort)76); writer.Write(1.0f); writer.Write(0.0f);
        writer.Write(0.0f); writer.Write(1.0f); writer.Write(0); writer.Write((ushort)0); writer.Write(2); writer.Write((byte)0);
        writer.Write(weightCount);
        for (var index = 0; index < weightCount; index++) { writer.Write(-1); writer.Write(0.0f); }
    })),
    ("NiMorphData", Bytes(writer =>
    {
        writer.Write(3); writer.Write(1); writer.Write((byte)1);
        foreach (var (name, value) in new[] { (1, new Vector3(2, 3, 4)), (2, new Vector3(-1, 0, 1)), (2, new Vector3(7, -2, 0)) })
        { writer.Write(name); writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    })),
    ("NiTriShapeData", Bytes(writer =>
    {
        writer.Write(0); writer.Write((ushort)1); writer.Write((ushort)0); writer.Write((byte)1);
        writer.Write(99.0f); writer.Write(98.0f); writer.Write(97.0f); writer.Write((ushort)0); writer.Write((byte)0);
        for (var index = 0; index < 4; index++) writer.Write(0.0f);
        writer.Write((byte)0); writer.Write((ushort)0); writer.Write(-1);
        writer.Write((ushort)0); writer.Write(0U); writer.Write((byte)0); writer.Write((ushort)0);
    })));

static FalloutNifFile Spline(bool compact, uint count, float[] points, short[]? compressed = null)
{
    return Wrap(
        (compact ? "NiBSplineCompTransformInterpolator" : "NiBSplineTransformInterpolator", Bytes(writer =>
        {
            writer.Write(0.0f); writer.Write(1.0f); writer.Write(1); writer.Write(2);
            for (var index = 0; index < 8; index++) writer.Write(float.MinValue);
            writer.Write(0U); writer.Write(65535U); writer.Write(65535U);
            if (compact)
                foreach (var value in new float[] { 2, 5, float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue }) writer.Write(value);
        })),
        ("NiBSplineData", Bytes(writer =>
        {
            writer.Write(points.Length); foreach (var point in points) writer.Write(point);
            writer.Write(compressed?.Length ?? 0); foreach (var point in compressed ?? []) writer.Write(point);
        })),
        ("NiBSplineBasisData", Bytes(writer => writer.Write(count))));
}

static FalloutNifFile Keyed() => Wrap(
    ("NiTransformInterpolator", Bytes(writer =>
    {
        for (var index = 0; index < 8; index++) writer.Write(float.MinValue);
        writer.Write(1);
    })),
    ("NiTransformData", Bytes(writer =>
    {
        writer.Write(1); writer.Write(4U);
        foreach (var angle in new[] { 0.0f, MathF.PI * 0.5f, MathF.PI * 0.5f })
        {
            writer.Write(2); writer.Write(2U);
            foreach (var time in new[] { 0.0f, 1.0f })
            {
                writer.Write(time); writer.Write(angle); writer.Write(0.0f); writer.Write(0.0f);
            }
        }
        writer.Write(2); writer.Write(1U);
        foreach (var time in new[] { 0.0f, 1.0f })
        {
            writer.Write(time); writer.Write(10 * time); writer.Write(0.0f); writer.Write(0.0f);
        }
        writer.Write(0);
    })));

static byte[] Bytes(Action<BinaryWriter> emit)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    emit(writer); return stream.ToArray();
}

static byte[] Node(int name, float z, int[] children) => Bytes(writer =>
{
    writer.Write(name); writer.Write(0); writer.Write(-1); writer.Write((ushort)14); writer.Write((ushort)0);
    writer.Write(0.0f); writer.Write(0.0f); writer.Write(z);
    foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) writer.Write(value);
    writer.Write(1.0f); writer.Write(0); writer.Write(-1); writer.Write(children.Length);
    foreach (var child in children) writer.Write(child);
    writer.Write(0);
});

static byte[] ConstantTransform(float z) => Bytes(writer =>
{
    writer.Write(0.0f); writer.Write(0.0f); writer.Write(z);
    for (var index = 0; index < 5; index++) writer.Write(float.MinValue);
    writer.Write(-1);
});

static FalloutNifFile Wrap(params (string Type, byte[] Bytes)[] blocks) => WrapNamed([], blocks);

static FalloutNifFile WrapNamed(string[] strings, params (string Type, byte[] Bytes)[] blocks)
{
    var bytes = Bytes(writer =>
    {
        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        writer.Write((ushort)blocks.Length);
        foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
        for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
        foreach (var block in blocks) writer.Write(block.Bytes.Length);
        writer.Write(strings.Length); writer.Write(strings.Length == 0 ? 0 : strings.Max(value => value.Length));
        foreach (var value in strings) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
        writer.Write(0U);
        foreach (var block in blocks) writer.Write(block.Bytes);
        writer.Write(1U); writer.Write(0);
    });
    return FalloutNifFile.Read(bytes);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Near(float value, float expected, string message) => Require(MathF.Abs(value - expected) < 0.00001f, $"{message}: {value} != {expected}");

static void ExpectInvalid(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Invalid spline was accepted.");
}
