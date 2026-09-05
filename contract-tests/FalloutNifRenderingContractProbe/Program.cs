using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

// A source XY triangle points toward +Z. The handedness-preserving (x,z,-y)
// mapping puts that normal at +Y. Godot's clockwise front must face +Y too.
Vector3[] sourceVertices = [new(0, 0, 0), new(2, 0, 0), new(0, 3, 0), new(1, 1, 1)];
FalloutNifTriangle[] sourceTriangles = [new(0, 1, 2), new(2, 2, 3), new(3, 1, 0)];
var originalTriangles = sourceTriangles.ToArray();
var indices = FalloutNifTriangleWinding.ToGodotIndices(sourceTriangles);
Require(indices.SequenceEqual([0, 2, 1, 3, 0, 1]), "Source order or degenerate removal differs.");
Require(sourceTriangles.SequenceEqual(originalTriangles), "Source triangle bytes were mutated.");
var godotVertices = sourceVertices.Select(value => new Vector3(value.X, value.Z, -value.Y)).ToArray();
var clockwiseNormal = Vector3.Cross(
    godotVertices[indices[2]] - godotVertices[indices[0]],
    godotVertices[indices[1]] - godotVertices[indices[0]]);
Require(clockwiseNormal == new Vector3(0, 6, 0), "Godot front face opposes the source normal.");
Require(!FalloutNifTextureAddressing.RepeatForGodot(0), "CLAMP_S_CLAMP_T repeats.");
Require(FalloutNifTextureAddressing.RepeatForGodot(3), "WRAP_S_WRAP_T clamps.");
foreach (var mode in new uint[] { 1, 2 })
    ExpectException<NotSupportedException>(() => FalloutNifTextureAddressing.RepeatForGodot(mode));
ExpectException<InvalidDataException>(() => FalloutNifTextureAddressing.RepeatForGodot(4));
Console.WriteLine("OPENNV_NIF_RENDERING_CONTRACT_OK sourceFrontFace=true sourceSamplerAddressing=true");
Require(FalloutNifAlphaState.Read(0x100d, 0).Blend == FalloutNifBlendMode.Add, "Source-alpha/one blend was reduced to ordinary transparency.");
Require(FalloutNifAlphaState.Read(0x0043, 0).Blend == FalloutNifBlendMode.Multiply, "Zero/source-colour blend was reduced to ordinary transparency.");
var mixed = FalloutNifAlphaState.Read(0x12ed, 73);
Require(mixed.Blend == FalloutNifBlendMode.SourceAlpha && mixed.TestEnabled && mixed.TestFunction == 4 && mixed.Threshold == 73,
    "Independent alpha blend/test fields were lost.");
ExpectException<NotSupportedException>(() => FalloutNifAlphaState.Read(0x0001, 0));
var angle = new FalloutNifAngleFalloff(0.8f, 0.2f, 0.75f, 0.15f);
Require(Math.Abs(angle.Sample(0.5f) - 0.45f) < 0.00001f && angle.Sample(1) == 0.75f && angle.Sample(0) == 0.15f,
    "Source cosine falloff does not preserve authored endpoints and interpolation.");
Console.WriteLine("OPENNV_NIF_ALPHA_CONTRACT_OK independentBlendAndTest=true cosineFalloff=true");

var hairDiffuse = new Vector3(0.2f, 0.4f, 0.6f);
var hairLayer = new Vector4(0.8f, 0.2f, 0.4f, 0.25f);
var hairTint = new Vector3(0.4f, 0.3f, 0.2f);
Require(Vector3.Distance(FalloutNifHairShading.BaseColor(hairDiffuse, hairLayer, hairTint, 1),
    new Vector3(0.28f, 0.21f, 0.22f)) < 0.000001f, "Hair layer alpha or tint scale was lost.");
Require(FalloutNifHairShading.BaseColor(hairDiffuse, new Vector4(9, 8, 7, 0), hairTint, 0) == hairDiffuse,
    "Zero layer alpha and zero tint mask must preserve diffuse bytes.");
Require(Vector3.Distance(FalloutNifHairShading.BaseColor(hairDiffuse, hairLayer, new Vector3(0.5f), 0.73f),
    new Vector3(0.35f, 0.35f, 0.55f)) < 0.000001f, "Neutral hair tint altered the source layer.");
Console.WriteLine("OPENNV_HAIR_BASE_CONTRACT_OK layerAlpha=true scalarTintMask=true neutralTint=true noRgbVertexMultiply=true");

// Optional owned audit takes a plain list of logical NIF paths. Nothing is
// extracted or persisted, and no Bethesda data is a public test fixture.
if (args.Length == 2)
{
    var archives = Directory.EnumerateFiles(Path.GetFullPath(args[0]), "*.bsa")
        .Select(path => new FalloutBsaArchive(path)).ToArray();
    try
    {
        var modelCount = 0;
        var surfaceCount = 0;
        var counterclockwise = 0;
        var authoredOpposingNormals = 0;
        foreach (var path in File.ReadLines(args[1]).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var archive = archives.Single(value => value.Contains(path));
            var nif = FalloutNifFile.Read(archive.Read(path));
            foreach (var block in nif.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips"))
            {
                var geometry = nif.ReadGeometry(block.Index);
                var mesh = nif.ReadMeshData(geometry.Data);
                var converted = FalloutNifTriangleWinding.ToGodotIndices(mesh.Triangles);
                Require(converted.Length == mesh.Triangles.Count(value =>
                    value.A != value.B && value.A != value.C && value.B != value.C) * 3,
                    $"Converted topology differs: {path}:{block.Index}");
                if (mesh.Normals.Length == mesh.Vertices.Length)
                    foreach (var triangle in mesh.Triangles)
                    {
                        var normal = Vector3.Cross(
                            Vector(mesh.Vertices[triangle.B]) - Vector(mesh.Vertices[triangle.A]),
                            Vector(mesh.Vertices[triangle.C]) - Vector(mesh.Vertices[triangle.A]));
                        var alignment = Vector3.Dot(normal, Vector(mesh.Normals[triangle.A]) +
                            Vector(mesh.Normals[triangle.B]) + Vector(mesh.Normals[triangle.C]));
                        if (alignment > 0.00001f)
                            counterclockwise++;
                        else if (alignment < -0.00001f)
                            authoredOpposingNormals++;
                    }
                foreach (var property in geometry.Properties.Where(reference => reference >= 0)
                    .Select(nif.ReadObject))
                    if (property is FalloutNifShaderProperty lighting)
                        _ = FalloutNifTextureAddressing.RepeatForGodot(lighting.TextureClampMode);
                    else if (property is FalloutNifNoLightingProperty unlit)
                    {
                        _ = FalloutNifTextureAddressing.RepeatForGodot(unlit.TextureClampMode);
                        if ((unlit.ShaderFlags & 0x40) != 0)
                        {
                            var falloff = FalloutNifAngleFalloff.Read(unlit);
                            Require(falloff.Sample(falloff.StartCosine) == falloff.StartOpacity &&
                                falloff.Sample(falloff.StopCosine) == falloff.StopOpacity, "Owned effect lost its opacity endpoints.");
                            Console.WriteLine($"OPENNV_OWNED_EFFECT_FALLOFF model={path} block={unlit.Block.Index} start={falloff.StartCosine}/{falloff.StartOpacity} stop={falloff.StopCosine}/{falloff.StopOpacity}");
                        }
                    }
                    else if (property is FalloutNifAlphaProperty alpha)
                        Console.WriteLine($"OPENNV_OWNED_ALPHA model={path} block={alpha.Block.Index} flags=0x{alpha.Flags:x4} state={FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold)}");
                surfaceCount++;
            }
            modelCount++;
        }
        Require(modelCount > 0 && counterclockwise > authoredOpposingNormals,
            "Owned audit did not establish the source front-face convention.");
        Console.WriteLine($"OPENNV_NIF_RENDERING_OWNED_AUDIT models={modelCount} surfaces={surfaceCount} " +
            $"counterclockwise={counterclockwise} authoredOpposingNormals={authoredOpposingNormals} parity=unverified");
    }
    finally
    {
        foreach (var archive in archives)
            archive.Dispose();
    }
}

static Vector3 Vector(FalloutNifVector3 value) => new(value.X, value.Y, value.Z);

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ExpectException<T>(Action operation) where T : Exception
{
    try
    {
        operation();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
