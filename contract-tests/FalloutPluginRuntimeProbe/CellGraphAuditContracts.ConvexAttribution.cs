using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void ConvexProjectionAttribution()
    {
        foreach (var item in new[]
        {
            (Bytes: FalloutConvexListFixture.Create(), Blocks: new[] { 3, 4, 5 }, Leaves: new[] { 4, 5 }),
            (Bytes: FalloutConvexListFixture.Nested(), Blocks: new[] { 3, 6, 4, 5 }, Leaves: new[] { 4, 5 }),
            (Bytes: FalloutConvexListFixture.ChildTransformed(), Blocks: new[] { 3, 6, 4, 5 }, Leaves: new[] { 4, 5 }),
            (Bytes: FalloutConvexListFixture.Create(children: [5, 4, 5]), Blocks: new[] { 3, 5, 4, 5 }, Leaves: new[] { 5, 4, 5 }),
            (Bytes: FalloutConvexListFixture.Create(children: Enumerable.Repeat(4, 256).ToArray()),
                Blocks: new[] { 3 }.Concat(Enumerable.Repeat(4, 256)).ToArray(), Leaves: Enumerable.Repeat(4, 256).ToArray()),
            (Bytes: FalloutConvexListFixture.Create(concaveSecond: true), Blocks: new[] { 3, 4, 5 }, Leaves: new[] { 4, 5 })
        })
        {
            var before = Convert.ToHexString(SHA256.HashData(item.Bytes));
            var source = FalloutNifFile.Read(item.Bytes);
            var list = (FalloutNifConvexListShape)source.ReadObject(3);
            var math = CellGraphAudit.ReadPlacedCollision(source, .1f);
            var failure = JsonSerializer.SerializeToElement(math.Failures.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(math.Shapes == 0 && math.PackedTriangles == 0 && math.Triangles.Count == 0 &&
                failure.GetProperty("lane").GetString() == "collision-floor-projection" &&
                failure.GetProperty("block").GetInt32() == 3 && failure.GetProperty("type").GetString() == "bhkConvexListShape" &&
                failure.GetProperty("nativeImplementationOwner").GetString() == "NativeNifCollisionBuilder.BuildConvexList" &&
                failure.GetProperty("nativeAdmission").GetString() == "unverified" &&
                failure.GetProperty("mathProjection").GetString() == "uninspected" &&
                failure.GetProperty("sourceOperation").GetString() == "ordered-independent-convex-leaf-union" &&
                !failure.GetProperty("error").GetString()!.Contains("no native owner", StringComparison.Ordinal),
                "Known convex implementation ownership was misattributed, its floor boundary cleared or unsupported geometry admitted.");
            var rows = failure.GetProperty("declarations").EnumerateArray().ToArray();
            Require(rows.Select(row => row.GetProperty("block").GetInt32()).SequenceEqual(item.Blocks) &&
                rows.Where(row => row.GetProperty("role").GetString() == "uninspected-leaf").Select(row => row.GetProperty("block").GetInt32()).SequenceEqual(item.Leaves) &&
                rows[0].GetProperty("childReferences").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(list.Children) &&
                rows.All(row => row.GetProperty("nativeAdmission").GetString() == "unverified" && row.GetProperty("mathProjection").GetString() == "uninspected"),
                "Ordered nested/duplicate/wide source union or leaf declarations disappeared or acquired native/projection success.");
            foreach (var row in rows)
            {
                var path = row.GetProperty("shapePath").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                var ordinals = row.GetProperty("sourceChildOrdinals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                Require(path[0] == 3 && path[^1] == row.GetProperty("block").GetInt32() && ordinals.Length == path.Length - 1 &&
                    row.GetProperty("type").GetString() == source.Blocks[path[^1]].TypeName,
                    "A declaration path lost the original compound/leaf type, source index or child ordinal.");
                uint? material = source.ReadObject(path[^1]) switch
                {
                    FalloutNifConvexListShape declared => declared.Material,
                    FalloutNifConvexTransformShape declared => declared.Material,
                    FalloutNifConvexVerticesShape declared => declared.Material,
                    _ => null
                };
                Require(material is null ? row.GetProperty("sourceMaterial").ValueKind == JsonValueKind.Null :
                    row.GetProperty("sourceMaterial").GetUInt32() == material,
                    "Compound/leaf attribution lost its original material or invented precedence for an unrelated packed leaf.");
                for (var step = 0; step < ordinals.Length; step++)
                {
                    int[] children = source.ReadObject(path[step]) switch
                    {
                        FalloutNifConvexListShape value => value.Children,
                        FalloutNifConvexTransformShape value => [value.Child],
                        _ => throw new InvalidDataException("The authored path contains an unrelated shape.")
                    };
                    Require(children[ordinals[step]] == path[step + 1], "A duplicate/nested leaf was joined through a guessed source path.");
                }
                if (source.ReadObject(path[^1]) is FalloutNifConvexTransformShape transformed)
                    Require(row.GetProperty("sourceMatrix").EnumerateArray().Select(value => value.GetSingle()).SequenceEqual(transformed.MatrixRowMajor),
                        "The declared child transform was omitted or replaced by inferred geometry.");
            }
            Require(Convert.ToHexString(SHA256.HashData(item.Bytes)) == before,
                "Diagnostic compound attribution changed original source bytes.");
        }
        var cycle = FalloutConvexListFixture.Create(children: [3]);
        Require(((FalloutNifConvexListShape)FalloutNifFile.Read(cycle).ReadObject(3)).Children.SequenceEqual([3]),
            "The cycle fixture failed framing before its source declaration could be inspected.");
        var rejected = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(cycle), .1f);
        var cycleFailure = JsonSerializer.SerializeToElement(rejected.Failures.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Require(rejected.Shapes == 0 && rejected.Triangles.Count == 0 &&
            cycleFailure.GetProperty("lane").GetString() == "source-collision-admission" &&
            cycleFailure.GetProperty("error").GetString()!.Contains("graph cycle", StringComparison.Ordinal),
            "A cyclic declaration was hidden inside an attribution-only success or lost its source refusal.");
        var malformed = FalloutConvexListFixture.Rewrite(FalloutConvexListFixture.Create(), 3,
            bytes => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), float.NaN));
        var refused = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(malformed), .1f);
        Require(refused.Failures.Count == 1 && refused.Shapes == 0 && refused.Triangles.Count == 0 &&
            JsonSerializer.SerializeToElement(refused.Failures.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("lane").GetString() == "source-collision-admission",
            "Malformed original convex-list fields were admitted as complete declaration context.");
        var box = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture()), .1f);
        Require(box.Failures.Count == 0 && box.Shapes == 1 && box.Triangles.Count == 12,
            "Convex attribution changed the unrelated existing box projection owner.");
        Console.WriteLine("OPENNV_CELL_GRAPH_CONVEX_ATTRIBUTION_PASS actualNifReader=true knownNativeImplementation=true projectionUninspected=true orderedUnion=true nestedLeafPaths=true transformDeclaration=true duplicateWideChildren=true concaveMemberUnadmitted=true malformedAndCycleRefused=true unrelatedBoxUnchanged=true sourceReadOnly=true nativeContactsUnverified=true");
    }
}
