using System.Buffers.Binary;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class ConvexListContracts
{
    internal static void Run()
    {
        foreach (var version in new uint[] { 21, 30, 31, 32, 33, 34 })
        foreach (var cached in new[] { false, true })
        {
            var bytes = FalloutConvexListFixture.Create(cachedAabb: cached, version2: version);
            var observed = new List<FalloutNifReadRange>();
            var file = FalloutNifFile.Read(bytes, observed.Add);
            var shape = (FalloutNifConvexListShape)file.ReadObject(3);
            Require(shape.Children.SequenceEqual([4, 5]) && shape.Material == 7 && shape.Radius == .05f &&
                shape.UnknownInteger == 0x12345678U && shape.UnknownFloatBits == 0x7fc12345U &&
                shape.ChildProperty == new FalloutNifHavokProperty(0x11111111U, 0x22222222U, 0xa0000022U) &&
                shape.UseCachedAabb == cached && shape.ClosestPointMinDistance == .75f,
                "Convex list changed a declared field, opaque bits or ordered child identity.");
            Require(Encode(shape).AsSpan().SequenceEqual(shape.SourceBytes.Span), "Convex list lost a source byte.");
            var rows = observed.Where(row => row.Owner == "NIF block 3 (bhkConvexListShape)").ToArray();
            Require(rows.Sum(row => row.Length) == 45 && rows.First().Offset == shape.Block.Offset &&
                rows.Last().Offset + rows.Last().Length == shape.Block.Offset + shape.Block.Size,
                "Convex list has no complete independent field-read coverage.");
            var body = (FalloutNifRigidBody)file.ReadObject(2);
            Require(body.Filter == new FalloutNifCollisionFilter(7, 0x24, 0x4567) &&
                body.InfoFilter == new FalloutNifCollisionFilter(8, 0x31, 0x1234) && body.Shape == 3,
                "Convex list substituted its parent body/filter ownership.");
            Require(ReferenceEquals(shape, file.ReadObject(3)), "Immutable decoded list was not reused.");
        }
        var original = FalloutConvexListFixture.Create();
        void Refuse(byte[] bytes)
        {
            try { FalloutNifFile.Read(bytes).ReadObject(3); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Malformed convex list was admitted.");
        }
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, bytes => bytes[40] = 2));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, bytes => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), float.NaN)));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, bytes => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(41), float.PositiveInfinity)));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, bytes => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 600)));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue)));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, _ => { }, truncate: true));
        Refuse(FalloutConvexListFixture.Rewrite(original, 3, _ => { }, append: true));
        // A uint table is not an eight-bit table. Parser admission must follow
        // backed source extents, rather than an old authoring-tool suggestion.
        var wide = (FalloutNifConvexListShape)FalloutNifFile.Read(FalloutConvexListFixture.Create(children: Enumerable.Repeat(4, 256).ToArray())).ReadObject(3);
        Require(wide.Children.Length == 256 && wide.Children.All(child => child == 4), "A source-backed child table was artificially narrowed.");
        var nested = FalloutNifFile.Read(FalloutConvexListFixture.Nested());
        Require(((FalloutNifConvexListShape)nested.ReadObject(3)).Children.SequenceEqual([6]) &&
            ((FalloutNifConvexListShape)nested.ReadObject(6)).Children.SequenceEqual([4, 5]),
            "Nested convex collection replaced its original graph.");
        var transformed = FalloutNifFile.Read(FalloutConvexListFixture.ChildTransformed());
        var transform = (FalloutNifConvexTransformShape)transformed.ReadObject(6);
        Require(((FalloutNifConvexListShape)transformed.ReadObject(3)).Children.SequenceEqual([6, 5]) &&
            transform.Child == 4 && transform.MatrixRowMajor[12] == 0 && transform.MatrixRowMajor[13] == 3 && transform.MatrixRowMajor[14] == 0,
            "Convex child transform lost its original child/translation declaration.");
        Console.WriteLine("OPENNV_CONVEX_LIST_READER_CONTRACT_PASS fields=true byteLoss=0 readCoverage=complete orderedChildren=true " +
            "opaqueBits=true cachedAlternatives=true parentBodyFilters=true versions=21,30,31,32,33,34 wideTable=true nestedAndChildTransformGraphs=true " +
            "malformedFieldsRefused=true nativeGeometry=unexecuted retailParity=unverified");
    }

    private static byte[] Encode(FalloutNifConvexListShape shape)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(shape.Children.Length); foreach (var child in shape.Children) writer.Write(child);
        writer.Write(shape.Material); writer.Write(shape.Radius); writer.Write(shape.UnknownInteger); writer.Write(shape.UnknownFloatBits);
        writer.Write(shape.ChildProperty.Data); writer.Write(shape.ChildProperty.Size); writer.Write(shape.ChildProperty.CapacityAndFlags);
        writer.Write(shape.UseCachedAabb); writer.Write(shape.ClosestPointMinDistance); return output.ToArray();
    }
    private static void Require(bool pass, string message) { if (!pass) throw new InvalidDataException(message); }
}
