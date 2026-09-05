using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Formats.FaceGen;
using OpenNV.Runtime.Content;

var combined = FalloutFaceGenCoefficients.AddSourceGeometry(Floats(0.25f, -0.5f), Floats(0.5f, 0.75f), 2);
Require(combined.SequenceEqual(new[] { 0.75f, 0.25f }), "NPC/RACE source coefficients were not added componentwise.");
Throws(() => FalloutFaceGenCoefficients.AddSourceGeometry(Floats(1), Floats(1, 2), 1));
Throws(() => FalloutFaceGenCoefficients.AddSourceGeometry(Floats(float.NaN), Floats(0), 1));
Throws(() => FalloutFaceGenCoefficients.AddSourceGeometry(Floats(float.MaxValue), Floats(float.MaxValue), 1));

var bytes = new byte[64 + 2 * (4 + 2 * 6)];
"FREGM002"u8.CopyTo(bytes);
BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 2);
BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 2);
for (var index = 0; index < 2; index++)
{
    var offset = 64 + index * 16;
    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 4), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 6), -2);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 10), 3);
}
var egm = FalloutEgmFile.Read(bytes);
var original = new[] { new Vector3(16777216, 10, 0) };
var result = egm.EvaluateSourcePrefixPositions(original, [1, 1], []);
Require(result.Length == 1 && egm.VertexCount == 2, "The explicit NIF prefix changed the complete EGM vertex count.");
Require(BitConverter.SingleToInt32Bits(result[0].X) == BitConverter.SingleToInt32Bits(original[0].X),
    "Source base-first float accumulation order changed.");
Require(result[0].Y == 6 && original[0].Y == 10, "Source positions were mutated or signed deltas were lost.");
Require(egm.EvaluateDeltas([1, 1], [])[0].X + original[0].X != result[0].X,
    "Fixture does not distinguish base-first from delta-first accumulation.");
Throws(() => egm.EvaluateSourcePrefixPositions([], [1, 1], []));
Throws(() => egm.EvaluateSourcePrefixPositions([Vector3.Zero, Vector3.Zero, Vector3.Zero], [1, 1], []));
Throws(() => egm.EvaluateSourcePrefixPositions([new(float.PositiveInfinity, 0, 0)], [1, 1], []));
Console.WriteLine("OPENNV_FACEGEN_GEOMETRY_OK sourceCoefficientComposition=true baseFirstFloatOrder=true explicitVertexPrefix=true originalSourceRetained=true");

using var triStream = new MemoryStream();
using (var writer = new BinaryWriter(triStream, System.Text.Encoding.Latin1, true))
{
    writer.Write("FRTRI003"u8);
    foreach (var field in new[] { 3, 1, 0, 0, 0, 0, 0, 1, 1, 1 }) writer.Write(field);
    writer.Write(new byte[16]);
    foreach (var vertex in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(2, 3, 4) })
    { writer.Write(vertex.X); writer.Write(vertex.Y); writer.Write(vertex.Z); }
    writer.Write(0); writer.Write(1); writer.Write(2);
    void Label(string value) { var text = System.Text.Encoding.Latin1.GetBytes(value + '\0'); writer.Write(text.Length); writer.Write(text); }
    Label("SyntheticDelta"); writer.Write(0.5f);
    foreach (var value in new short[] { -2, 4, 6, 0, 0, 0, 1, -1, 0 }) writer.Write(value);
    Label("SyntheticTarget"); writer.Write(1); writer.Write(1);
}
var triBytes = triStream.ToArray();
var tri = FalloutTriFile.Read(triBytes);
var shaped = tri.Vertices.ToArray();
shaped[1] += new Vector3(3, 0, 0);
shaped[3] += new Vector3(8, 0, 0);
var morphs = tri.BuildDeltas(shaped);
Require(morphs["SyntheticDelta"][0] == new Vector3(-1, 2, 3), "TRI signed packed deltas or scale changed.");
Require(morphs["SyntheticTarget"][1] == new Vector3(6, 3, 4), "Statistical target was not subtracted after shaping both vertices.");
Require(morphs["SyntheticTarget"][0] == Vector3.Zero && tri.Vertices[1] == Vector3.UnitX, "TRI target affected an unrelated vertex or mutated source data.");
Throws(() => FalloutTriFile.Read(triBytes[..^1]));
Throws(() => FalloutTriFile.Read(triBytes.Concat(new byte[] { 0 }).ToArray()));
Throws(() => tri.BuildDeltas(shaped[..^1]));
var invalidTri = triBytes.ToArray();
BinaryPrimitives.WriteInt32LittleEndian(invalidTri.AsSpan(8), -1);
Throws(() => FalloutTriFile.Read(invalidTri));
invalidTri = triBytes.ToArray();
BinaryPrimitives.WriteInt32LittleEndian(invalidTri.AsSpan(invalidTri.Length - 4), 3);
Throws(() => FalloutTriFile.Read(invalidTri));
Console.WriteLine("OPENNV_TRI_CONTRACT_OK signedDeltas=true shapedStatTargets=true exactExtent=true invalidIndicesFail=true");

if (args is ["--owned", var dataRoot])
{
    using var archive = new FalloutBsaArchive(Path.Combine(dataRoot, "Fallout - Meshes.bsa"));
    var paths = archive.MemberPaths.Where(path => path.EndsWith(".tri", StringComparison.OrdinalIgnoreCase)).ToArray();
    var admitted = 0;
    foreach (var path in paths)
    {
        try
        {
            var owned = FalloutTriFile.Read(archive.Read(path));
            var deltas = owned.BuildDeltas(owned.Vertices);
            if (deltas.Count != owned.DeltaMorphs.Count + owned.StatMorphs.Count) throw new InvalidDataException("Owned target count changed.");
            admitted++;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        { Console.WriteLine($"OPENNV_OWNED_TRI_UNBOUND path={path} reason={error.Message}"); }
    }
    Console.WriteLine($"OPENNV_OWNED_TRI_AUDIT files={paths.Length} admitted={admitted} unbound={paths.Length - admitted} parity=unmeasured");
    if (admitted != paths.Length) Environment.ExitCode = 1;
}

static byte[] Floats(params float[] values)
{
    var bytes = new byte[values.Length * sizeof(float)];
    for (var index = 0; index < values.Length; index++)
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), values[index]);
    return bytes;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Invalid FaceGen geometry input was accepted.");
}
