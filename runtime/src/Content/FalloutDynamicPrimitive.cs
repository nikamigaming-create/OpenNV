using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed partial record FalloutDynamicPrimitive(FalloutDynamicPrimitiveSource Source,
    FalloutFormKey Reference, FalloutFormKey Base, FalloutFormKey Cell, uint ReferenceFlags,
    FalloutDynamicPrimitiveRole Role, FalloutReferencePrimitive Primitive,
    IReadOnlyList<float>? BoundHalfExtents, IReadOnlyList<FalloutFormKey?> PortalRooms,
    IReadOnlyList<FalloutFormKey> LinkedRooms, ushort? RoomFlags, string SourceSha256)
{
    internal static FalloutDynamicPrimitive Read(FalloutDynamicPrimitiveSource source,
        FalloutPluginStack records, FalloutPlacedReference reference)
    {
        var role = source.Role(records, reference.Base);
        if (role == FalloutDynamicPrimitiveRole.None)
            throw new InvalidDataException("Reference has no selected dynamic-primitive factory role.");
        var record = records.GetEffective(reference.FormKey);
        var fields = record.ReadSubrecords().ToArray();
        var names = fields.Where(field => field.Signature == "NAME").ToArray();
        if (record.Signature != "REFR" || names is not [{ Data.Length: 4 }] ||
            record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span)) != reference.Base ||
            FalloutCellSceneReader.ParentCell(record) != reference.Cell || record.Flags != reference.Flags)
            throw new InvalidDataException("Dynamic primitive differs from its winning placed reference/base/CELL.");
        if (records.TryGetEffective(reference.Base, out var basis) && basis.Signature != "STAT")
            throw new NotSupportedException("Source dynamic-primitive factory has no winning STAT base consumer.");
        var primitive = FalloutReferencePrimitive.Read(record) ??
            throw new NotSupportedException("Source dynamic primitive has no actual XPRM constructor input.");
        if (role is FalloutDynamicPrimitiveRole.OcclusionPlane or FalloutDynamicPrimitiveRole.Portal && primitive.Type != 3 ||
            role == FalloutDynamicPrimitiveRole.Room && primitive.Type is not (1 or 2))
            throw new NotSupportedException("Source primitive kind does not match its selected engine factory role.");
        ReadOnlyMemory<byte>? Single(string signature, int extent)
        {
            var rows = fields.Where(field => field.Signature == signature).ToArray();
            if (rows.Length > 1 || rows.Length == 1 && rows[0].Data.Length != extent)
                throw new InvalidDataException($"Dynamic primitive {signature} has invalid or duplicated source extent.");
            return rows.Length == 0 ? (ReadOnlyMemory<byte>?)null : rows[0].Data;
        }
        var boundsBytes = Single("XMBO", 12);
        float[]? bounds = boundsBytes is null ? null : Enumerable.Range(0, 3).Select(index =>
            BinaryPrimitives.ReadSingleLittleEndian(boundsBytes.Value.Span[(index * sizeof(float))..])).ToArray();
        if (bounds is not null && bounds.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidDataException("Source multibound dimensions are invalid.");
        var portals = Single("XPOD", 8);
        var rooms = portals is null ? Array.Empty<FalloutFormKey?>() : Enumerable.Range(0, 2).Select(index =>
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(portals.Value.Span[(index * sizeof(uint))..]))).ToArray();
        var header = Single("XRMR", 4);
        var linked = fields.Where(field => field.Signature == "XLRM").ToArray();
        if (linked.Any(field => field.Data.Length != sizeof(uint)) ||
            linked.Length != (header is null ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(header.Value.Span)))
            throw new InvalidDataException("Source linked-room count does not match its physical XLRM fields.");
        var linkedRooms = linked.Select(field => record.Plugin.AdjustOptionalFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) ??
            throw new InvalidDataException("Source linked room is null.")).ToArray();
        foreach (var target in rooms.Where(room => room is not null).Select(room => room!.Value).Concat(linkedRooms))
            if (records.GetEffective(target).Signature != "REFR")
                throw new InvalidDataException("Source portal/linked-room target is not a winning placed reference.");
        if (role == FalloutDynamicPrimitiveRole.Portal && ((record.Flags & 0x4000) != 0 || portals is null))
            throw new NotSupportedException("Source portal flags/connections require an independent native reference consumer.");
        var serialized = ReadSerialized(source, records, record, role);
        var hash = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
        return new(source, reference.FormKey, reference.Base, reference.Cell, record.Flags, role, primitive,
            bounds, rooms, linkedRooms, header is null ? null : BinaryPrimitives.ReadUInt16LittleEndian(header.Value.Span[2..]), hash)
        {
            SerializedPlane = serialized.Pose,
            DiscardedPortalDeclaration = serialized.Portal,
            OrderedOcclusionPlanes = serialized.Links,
            DeclaredReferencePose = serialized.ReferencePose
        };
    }
}
