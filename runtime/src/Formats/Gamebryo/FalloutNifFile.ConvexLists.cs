namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class FalloutNifFile
{
    private FalloutNifConvexListShape ReadConvexListShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        var children = ReadReferences(ref cursor, "convex list children");
        var material = cursor.ReadUInt32("convex list material");
        var radius = cursor.ReadFiniteSingle("convex list radius");
        var unknownInteger = cursor.ReadUInt32("convex list unknown integer");
        // This field is not geometry. Preserve its complete IEEE payload until
        // its runtime purpose is established, including non-finite bit patterns.
        var unknownFloatBits = cursor.ReadUInt32("convex list unknown float bits");
        var childProperty = new FalloutNifHavokProperty(cursor.ReadUInt32("convex list child property data"),
            cursor.ReadUInt32("convex list child property size"), cursor.ReadUInt32("convex list child property capacity and flags"));
        var cachedAabb = cursor.ReadBoolean("convex list cached AABB");
        var closestDistance = cursor.ReadFiniteSingle("convex list closest point minimum distance");
        return new(block, children, material, radius, unknownInteger, unknownFloatBits, childProperty,
            cachedAabb, closestDistance)
        { SourceBytes = _payload.Slice(block.Offset, block.Size) };
    }
}

internal sealed record FalloutNifConvexListShape(FalloutNifBlock Block, int[] Children, uint Material, float Radius,
    uint UnknownInteger, uint UnknownFloatBits, FalloutNifHavokProperty ChildProperty, bool UseCachedAabb,
    float ClosestPointMinDistance) : FalloutNifObject(Block)
{
    internal required ReadOnlyMemory<byte> SourceBytes { get; init; }
}
