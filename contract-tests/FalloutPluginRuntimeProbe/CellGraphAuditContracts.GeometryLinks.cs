using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void NifGeometryDataLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-geometry-links-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game");
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(Path.Combine(data, "meshes"));
        Directory.CreateDirectory(Path.Combine(data, "textures"));
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), PluginFixture());
            ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "unrelated authored member");
            var archiveIni = Path.Combine(game, "Archives.ini");
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            File.WriteAllBytes(Path.Combine(data, "textures", "geometry.dds"), DdsFixture());
            var cases = new (string Name, byte[] Bytes)[]
            {
                ("valid", GeometryDataLinkNif()),
                ("cross-type-mesh", GeometryDataLinkNif(firstTarget: 4)),
                ("wrong-type", GeometryDataLinkNif(firstTarget: 6)),
                ("null", GeometryDataLinkNif(firstTarget: -1)),
                ("out-of-range", GeometryDataLinkNif(firstTarget: 8)),
                ("invalid-negative", GeometryDataLinkNif(firstTarget: -2)),
                ("malformed-target", GeometryDataLinkNif(malformedFirst: true)),
                ("unsupported-target", GeometryDataLinkNif(unsupportedFirst: true)),
                ("empty-typed-target", GeometryDataLinkNif(emptyFirst: true)),
            };
            foreach (var item in cases)
                File.WriteAllBytes(Path.Combine(data, "meshes", item.Name + ".nif"), item.Bytes);
            var before = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
            using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, [], [Plugin], archiveIni))
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                source.ArchiveWarmup.GetAwaiter().GetResult();
                Require(ReferenceEquals(records.OwnedSource, source), "Geometry links used an unrelated source owner.");
                var resources = new CellGraphAudit.CellAuditResources(source, .1f);

                // Regression first: all blocks decode and collision projection
                // has no work, but the actual mesh-data reader rejects NiNode.
                var wrongBytes = cases.Single(item => item.Name == "wrong-type").Bytes;
                var wrongNif = FalloutNifFile.Read(wrongBytes);
                var wrongGeometry = wrongNif.ReadGeometry(1);
                var wrongError = GeometryDataLinkReaderError(wrongNif, wrongGeometry.Data);
                Require(wrongGeometry.Data == 6 && wrongNif.ReadObject(6) is FalloutNifNode && wrongError.Length != 0,
                    "The wrong-type fixture failed before its independent mesh-data refusal.");
                var wrong = resources.Read("meshes/wrong-type.nif", "animation");
                Require(wrong.Failures.Any(value => GeometryDataLinkFailure(value, "nif-geometry-data-link", 1, wrongError)),
                    "A fully decoded geometry Data link to NiNode was awarded zero reader failures.");

                var valid = resources.Read("meshes/valid.nif", "animation");
                var validNif = FalloutNifFile.Read(cases.Single(item => item.Name == "valid").Bytes);
                Require(valid.Failures.Count == 0 && valid.DecodedBlocks == 8 && valid.Blocks == 8 && valid.Geometry == 3 && valid.Roots == 1,
                    "Genuine triangle, strip and segmented geometry failed full source decoding.");
                RequireGeometryDataLinkRows(valid, validNif, [1, 3, 5]);
                Require(valid.GeometryDataLinks.Select(link => link.TargetBlock).SequenceEqual([2, 4, 2]) &&
                    valid.GeometryDataLinks.All(link => link.ReaderDisposition == "typed-mesh-data-decoded" && link.Vertices == 3 && link.Triangles == 1),
                    "Valid or shared Data targets lost their exact source link or decoded mesh dimensions.");
                var shape = validNif.ReadMeshData(2);
                var strip = validNif.ReadMeshData(4);
                Require(shape.Vertices.Select(vertex => new[] { vertex.X, vertex.Y, vertex.Z })
                    .SelectMany(vertex => vertex).SequenceEqual(new float[] { 0, 0, 0, 2, 0, 0, 0, 3, 0 }) &&
                    shape.Triangles.Single() == new FalloutNifTriangle(0, 1, 2) &&
                    strip.Triangles.SequenceEqual(shape.Triangles) &&
                    validNif.ReadGeometry(5).Segments.Count == 1,
                    "The valid links point at invented or empty mesh records instead of complete authored geometry.");
                var promoted = resources.ReadModel("MESHES\\VALID.NIF");
                Require(ReferenceEquals(promoted.Report, valid) && valid.Kind == "model" &&
                    valid.InspectionRoles.SetEquals(["animation", "model"]) && valid.GeometryDataLinks.Count == 3 && valid.Failures.Count == 0,
                    "A resource role upgrade duplicated links, lost a source failure or replaced the original model.");
                RequireGeometryDataLinkRows(wrong, wrongNif, [1, 3, 5]);
                Require(wrong.DecodedBlocks == 8 && wrong.Geometry == 3 && wrong.Failures.Count == 1 &&
                    wrong.GeometryDataLinks[0].ReaderDisposition == "reader-refused" &&
                    wrong.GeometryDataLinks[0].Error == wrongError &&
                    wrong.GeometryDataLinks[0].TargetType == "NiNode" &&
                    wrong.GeometryDataLinks.Skip(1).All(link => link.ReaderDisposition == "typed-mesh-data-decoded"),
                    "A wrong target concealed later valid siblings or changed a decoded block into a native success.");

                var crossType = resources.Read("meshes/cross-type-mesh.nif", "animation");
                var crossTypeNif = FalloutNifFile.Read(cases.Single(item => item.Name == "cross-type-mesh").Bytes);
                RequireGeometryDataLinkRows(crossType, crossTypeNif, [1, 3, 5]);
                Require(crossType.Failures.Count == 0 && crossType.GeometryDataLinks[0].Type == "NiTriShape" &&
                    crossType.GeometryDataLinks[0].TargetType == "NiTriStripsData" && crossType.GeometryDataLinks[0].TargetBlock == 4 &&
                    crossType.GeometryDataLinks.All(link => link.ReaderDisposition == "typed-mesh-data-decoded" && link.Vertices == 3 && link.Triangles == 1),
                    "The audit imposed a guessed geometry-to-mesh-type restriction beyond the actual shared reader.");

                var nullNif = FalloutNifFile.Read(cases.Single(item => item.Name == "null").Bytes);
                var nullError = GeometryDataLinkReaderError(nullNif, -1);
                var emptyReference = resources.Read("meshes/null.nif", "animation");
                RequireGeometryDataLinkRows(emptyReference, nullNif, [1, 3, 5]);
                Require(emptyReference.DecodedBlocks == 8 && emptyReference.Geometry == 3 && emptyReference.Failures.Count == 1 &&
                    emptyReference.GeometryDataLinks[0].TargetBlock == -1 && emptyReference.GeometryDataLinks[0].TargetType is null &&
                    emptyReference.GeometryDataLinks[0].TargetOffset is null && emptyReference.GeometryDataLinks[0].TargetBytes is null &&
                    emptyReference.GeometryDataLinks[0].Error == nullError && nullError.Length != 0 &&
                    emptyReference.GeometryDataLinks.Skip(1).All(link => link.ReaderDisposition == "typed-mesh-data-decoded"),
                    "A nullable declaration invented a target or suppressed the actual no-mesh-data refusal.");

                foreach (var item in cases.Where(item => item.Name is "out-of-range" or "invalid-negative"))
                {
                    var nif = FalloutNifFile.Read(item.Bytes);
                    string? originalError = null;
                    try { _ = nif.ReadGeometry(1); }
                    catch (InvalidDataException error) { originalError = error.Message; }
                    Require(originalError is not null, "An out-of-range geometry declaration no longer reaches the existing decoder refusal.");
                    var row = resources.Read("meshes/" + item.Name + ".nif", "animation");
                    Require(row.DecodedBlocks == 7 && row.Geometry == 2 && row.Failures.Count == 1 &&
                        GeometryDataLinkFailure(row.Failures.Single(), "nif-block", 1, originalError!) &&
                        row.GeometryDataLinks.All(link => link.Block != 1),
                        "An undecoded geometry acquired a fabricated typed link or lost its original block refusal.");
                    RequireGeometryDataLinkRows(row, nif, [3, 5]);
                    Require(row.GeometryDataLinks.All(link => link.ReaderDisposition == "typed-mesh-data-decoded"),
                        "An invalid earlier Data reference concealed valid later geometry.");
                }

                foreach (var item in cases.Where(item => item.Name is "malformed-target" or "unsupported-target"))
                {
                    var nif = FalloutNifFile.Read(item.Bytes);
                    var originalError = GeometryDataLinkReaderError(nif, 2);
                    var row = resources.Read("meshes/" + item.Name + ".nif", "animation");
                    RequireGeometryDataLinkRows(row, nif, [1, 3, 5]);
                    Require(originalError.Length != 0 && row.DecodedBlocks == 7 && row.Geometry == 3 && row.Failures.Count == 3 &&
                        row.Failures.Any(value => GeometryDataLinkFailure(value, "nif-block", 2, originalError)) &&
                        new[] { 1, 5 }.All(block => row.Failures.Any(value => GeometryDataLinkFailure(value, "nif-geometry-data-link", block, originalError))) &&
                        row.GeometryDataLinks.Where(link => link.TargetBlock == 2).All(link => link.ReaderDisposition == "reader-refused" &&
                            link.Error == originalError && link.Vertices is null && link.Triangles is null) &&
                        row.GeometryDataLinks.Single(link => link.Block == 3).ReaderDisposition == "typed-mesh-data-decoded",
                        "A failed shared target lost a block/link refusal, invented dimensions or concealed a later independent mesh.");
                }

                var emptyBytes = cases.Single(item => item.Name == "empty-typed-target").Bytes;
                var typedEmpty = resources.Read("meshes/empty-typed-target.nif", "animation");
                RequireGeometryDataLinkRows(typedEmpty, FalloutNifFile.Read(emptyBytes), [1, 3, 5]);
                Require(typedEmpty.Failures.Count == 0 && typedEmpty.GeometryDataLinks.Where(link => link.TargetBlock == 2)
                    .All(link => link.ReaderDisposition == "typed-mesh-data-decoded" && link.Vertices == 0 && link.Triangles == 0 && link.NativeAdmission == "unverified"),
                    "Typed decoding introduced a new format guard or pretended empty mesh data had native geometry admission.");

                foreach (var item in cases)
                {
                    var row = resources.Resources["meshes/" + item.Name + ".nif"];
                    Require(row.Source == Path.Combine(data, "meshes", item.Name + ".nif") &&
                        row.Sha256 == Convert.ToHexString(SHA256.HashData(item.Bytes)) && row.Bytes == item.Bytes.Length &&
                        row.Dependencies.SequenceEqual(["textures/geometry.dds"]) && TextureDeclarations(row.DependencyDeclarations).Length == 1 &&
                        JsonSerializer.SerializeToElement(row.Native).GetProperty("admission").GetString() == "unverified",
                        "A typed link lost original source identity, later texture declarations or independent native refusal boundaries.");
                    var serialized = JsonSerializer.SerializeToElement(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    Require(serialized.GetProperty("geometryDataLinks").GetArrayLength() == row.GeometryDataLinks.Count &&
                        serialized.GetProperty("geometryDataLinks").EnumerateArray().All(link => link.GetProperty("nativeAdmission").GetString() == "unverified"),
                        "Serialized resource evidence omitted link rows or promoted typed decoding to native readiness.");
                }
                var texture = resources.Resources["textures/geometry.dds"];
                Require(texture.Failures.Count == 0 && texture.Source == Path.Combine(data, "textures", "geometry.dds") &&
                    texture.Sha256 == Convert.ToHexString(SHA256.HashData(DdsFixture())),
                    "A failed geometry link hid an unrelated complete texture dependency.");
            }
            Require(before.All(item => item.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key)))) &&
                before.Count == Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count(),
                "The geometry-link audit modified an authored original or created a transformed source asset.");
            Console.WriteLine("OPENNV_CELL_NIF_GEOMETRY_DATA_LINK_PASS actualNifReader=true triangleStripSegmented=true sharedTargets=true nullWrongTypeRefused=true outOfRangeDecoderPreserved=true malformedUnsupportedTargetsRetained=true laterMeshTextureSiblings=true sourceExtents=true typedEmptyNotNativeAdmission=true sourceBytes=unchanged nativeGeometryAdmission=unverified addnDependencies=uninspected");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool GeometryDataLinkFailure(object value, string lane, int block, string error)
    {
        var row = JsonSerializer.SerializeToElement(value);
        return row.TryGetProperty("lane", out var actualLane) && actualLane.GetString() == lane &&
            row.TryGetProperty("block", out var actualBlock) && actualBlock.GetInt32() == block &&
            row.TryGetProperty("error", out var actualError) && actualError.GetString() == error;
    }

    private static string GeometryDataLinkReaderError(FalloutNifFile nif, int target)
    {
        try { _ = nif.ReadMeshData(target); }
        catch (Exception error) { return error.Message; }
        return "";
    }

    private static void RequireGeometryDataLinkRows(CellGraphAudit.ResourceRow row, FalloutNifFile nif, int[] blocks)
    {
        Require(row.GeometryDataLinks.Select(link => link.Block).SequenceEqual(blocks),
            "Geometry link rows do not retain all and only successfully decoded geometry declarations.");
        foreach (var link in row.GeometryDataLinks)
        {
            var geometry = nif.ReadGeometry(link.Block);
            Require(link.Type == geometry.Block.TypeName && link.Offset == geometry.Block.Offset && link.Bytes == geometry.Block.Size &&
                link.Field == "Data" && link.TargetBlock == geometry.Data && link.Reader == "FalloutNifFile.ReadMeshData" &&
                link.NativeAdmission == "unverified",
                "A link changed its exact source block, Data value, reader owner or native admission boundary.");
            if (geometry.Data == -1)
                Require(link.TargetType is null && link.TargetOffset is null && link.TargetBytes is null,
                    "A null source declaration acquired an invented block target.");
            else
            {
                var target = nif.Blocks[geometry.Data];
                Require(link.TargetType == target.TypeName && link.TargetOffset == target.Offset && link.TargetBytes == target.Size,
                    "A link lost its exact target block type or original byte extent.");
            }
        }
    }

    private static byte[] GeometryDataLinkNif(int firstTarget = 2, bool malformedFirst = false, bool unsupportedFirst = false, bool emptyFirst = false)
    {
        var blocks = new (string Type, byte[] Payload)[]
        {
            ("NiNode", Node([0, 0, 0], 1, -1, [1, 3, 5])),
            ("NiTriShape", GeometryDataLinkGeometry("NiTriShape", firstTarget)),
            (unsupportedFirst ? "AuthoredUnsupportedMeshOwner" : "NiTriShapeData",
                unsupportedFirst ? new byte[] { 13, 17, 23 } : GeometryDataLinkMesh(strips: false, malformed: malformedFirst, empty: emptyFirst)),
            ("NiTriStrips", GeometryDataLinkGeometry("NiTriStrips", 4)),
            ("NiTriStripsData", GeometryDataLinkMesh(strips: true, malformed: false, empty: false)),
            ("BSSegmentedTriShape", GeometryDataLinkGeometry("BSSegmentedTriShape", 2)),
            ("NiNode", Node([0, 0, 0], 1, -1, [])),
            ("BSShaderTextureSet", Bytes(writer =>
            {
                const string path = "Data/textures/geometry.dds";
                writer.Write(1); writer.Write(path.Length); writer.Write(Encoding.UTF8.GetBytes(path));
            })),
        };
        return Bytes(writer =>
        {
            void Sized(string value) { var encoded = Encoding.UTF8.GetBytes(value); writer.Write(encoded.Length); writer.Write(encoded); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length); foreach (var block in blocks) Sized(block.Type);
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length);
            writer.Write(1U); writer.Write(7U); Sized("fixture"); writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Payload);
            writer.Write(1U); writer.Write(0);
        });
    }

    private static byte[] GeometryDataLinkGeometry(string type, int data) => Bytes(writer =>
    {
        writer.Write(0); writer.Write(0U); writer.Write(-1); // ObjectNET.
        writer.Write(14U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0U); writer.Write(-1); // No properties/collision.
        writer.Write(data); writer.Write(-1); // Exact Data and absent skin.
        writer.Write(0U); writer.Write(-1); writer.Write(false); // Empty material table.
        if (type == "BSSegmentedTriShape")
        { writer.Write(1U); writer.Write((byte)0); writer.Write(0U); writer.Write(1U); }
    });

    private static byte[] GeometryDataLinkMesh(bool strips, bool malformed, bool empty) => Bytes(writer =>
    {
        writer.Write(0); writer.Write(empty ? (ushort)0 : (ushort)3);
        writer.Write((byte)0); writer.Write((byte)0); writer.Write(!empty);
        if (!empty) foreach (var value in new float[] { 0, 0, 0, 2, 0, 0, 0, 3, 0 }) writer.Write(value);
        writer.Write(empty ? (byte)0 : (byte)1); writer.Write((byte)0); writer.Write(!empty);
        if (!empty) for (var index = 0; index < 3; index++) { writer.Write(0f); writer.Write(0f); writer.Write(1f); }
        writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(empty ? 0f : 4f); writer.Write(false);
        if (!empty) foreach (var value in new float[] { 0, 0, 1, 0, 0, 1 }) writer.Write(value);
        writer.Write((ushort)0); writer.Write(-1); writer.Write(empty ? (ushort)0 : (ushort)1);
        if (strips)
        {
            writer.Write(empty ? (ushort)0 : (ushort)1);
            if (!empty) writer.Write((ushort)3);
            writer.Write(!empty);
            if (!empty) { writer.Write((ushort)0); writer.Write((ushort)1); writer.Write(malformed ? (ushort)3 : (ushort)2); }
        }
        else
        {
            writer.Write(empty ? 0U : 3U); writer.Write(!empty);
            if (!empty) { writer.Write((ushort)0); writer.Write((ushort)1); writer.Write(malformed ? (ushort)3 : (ushort)2); }
            writer.Write((ushort)0); // No triangle match groups.
        }
    });
}
