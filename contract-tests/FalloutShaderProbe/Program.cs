using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Formats.Gamebryo;

if (args is ["--inspect-program", var packagePath, var shaderName])
{
    var selected = FalloutShaderPackage.Read(File.ReadAllBytes(packagePath)).Single(shader => shader.Name == shaderName);
    Console.WriteLine(FalloutD3D9PixelProgram.Read(selected.Bytecode).GodotSource);
    return;
}

static byte[] Words(params uint[] words)
{
    var bytes = new byte[words.Length * 4];
    for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
    return bytes;
}
static void Reject(Action action)
{
    try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
    throw new Exception("An unsupported or truncated shader was accepted.");
}
var program = FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x02000001, 0x801f0800, 0xa1e40003, 0xffff));
if (!program.Constants.SequenceEqual(new[] { 3 }) || !program.GodotSource.Contains("(-c3.xyzw)", StringComparison.Ordinal) ||
    !program.GodotSource.Contains("clamp(", StringComparison.Ordinal)) throw new Exception("Source negation or saturation was lost.");
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x02000001, 0x800f0800)));
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x00001234, 0xffff)));
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0300, 0xffff)));
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x02000001, 0x800f0800, 0xa0e42003, 0xffff)));
Reject(() => FalloutShaderPackage.Read(Words(100, 1, 0)));
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x02000001, 0x800f0800, 0x80e40000, 0xffff)));
Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200, 0x03000042, 0x800f0800, 0xb0e40000, 0xa0e40000, 0xffff)));
var evaluated = program.Evaluate(Vector2.Zero, new Dictionary<int, Vector4> { [3] = new(-2, 0.5f, -0.25f, -1) },
    (_, _) => throw new Exception("An arithmetic program sampled a texture."));
if (evaluated != new Vector4(1, 0, 0.25f, 1)) throw new Exception("CPU instruction modifiers differ from the source program.");
var interpolated = FalloutD3D9PixelProgram.Read(Words(0xffff0200,
    0x0200001f, 0x80000000, 0xb00f0001,
    0x04000012, 0x800f0800, 0xa0e40000, 0xb0e40001, 0xa0e40001, 0xffff));
var interpolation = interpolated.Evaluate(new(0.25f, 0.75f), new Dictionary<int, Vector4>
    { [0] = new(0.5f), [1] = Vector4.One }, (_, _) => throw new Exception("An arithmetic program sampled a texture."));
if (interpolation != new Vector4(0.625f, 0.875f, 0.5f, 1)) throw new Exception("Source LRP or second texture-coordinate register changed.");
var computeFunction = interpolated.ComputeFunction("source_filter", new Dictionary<int, string>
    { [0] = "params.weight", [1] = "params.color" }, new Dictionary<int, string>());
if (!computeFunction.Contains("vec4 source_filter(vec2 coordinate)", StringComparison.Ordinal) ||
    !computeFunction.Contains("params.weight.xyzw", StringComparison.Ordinal) || computeFunction.Contains("uniform ", StringComparison.Ordinal))
    throw new Exception("Compute program did not preserve its body and external register bindings.");
Reject(() => interpolated.ComputeFunction("source_filter", new Dictionary<int, string> { [0] = "params.weight" }, new Dictionary<int, string>()));
Console.WriteLine("OPENNV_SHADER_CONTRACT_PASS modifiers=true truncationFails=true unknownOpcodeFails=true relativeAddressingFails=true");
if (args is [var root])
{
    var packages = Directory.GetFiles(Path.Combine(root, "Shaders"), "*.sdp");
    if (packages.Length == 0) throw new Exception("Owned shader packages are absent.");
    var count = 0;
    var constants = new Dictionary<int, Vector4> { [0] = new(1f / 1280, 1f / 720, 0, 0), [1] = new(1, 0, 0, 0),
        [2] = new(0, -1, 0, 0), [3] = new(0, -1, 0, 0), [4] = Vector4.One };
    foreach (var path in packages)
    {
        var source = FalloutShaderPackage.Read(File.ReadAllBytes(path));
        var selected = source.Single(shader => shader.Name.Equals("ISTV.pso", StringComparison.OrdinalIgnoreCase));
        var decoded = FalloutD3D9PixelProgram.Read(selected.Bytecode);
        if (!decoded.Constants.SequenceEqual(new[] { 0, 1, 2, 3, 4 }) || !decoded.Samplers.SequenceEqual(new[] { 0, 1, 2 }))
            throw new Exception("Owned TV program interface differs.");
        var coordinates = new List<Vector2>();
        _ = decoded.Evaluate(new(0.25f, 0.5f), constants, (index, uv) => { if (index == 0) coordinates.Add(uv); return Vector4.One; });
        if (coordinates.Count == 0 || coordinates.Any(value => !float.IsFinite(value.X) || !float.IsFinite(value.Y)))
            throw new Exception("Owned screen input coordinates are missing or non-finite.");
        var visible = decoded.VisibleSampleCoordinate(0, new(0.8f, 0.5f), constants, (_, _) => Vector4.One);
        if (Vector2.Distance(visible, new(0.874f, 0.665f)) > 0.000001f) throw new Exception("Owned packed menu coordinate differs.");
        Reject(() => decoded.VisibleSampleCoordinate(0, new(0.25f, 0.5f), constants, (_, _) => Vector4.One));
        count++;
    }
    Console.WriteLine($"OPENNV_OWNED_SCREEN_PROGRAM_PASS packages={count} program=ISTV.pso parity=unverified");
}
