using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed partial record FalloutDynamicPrimitive
{
    internal FalloutSerializedPlane? SerializedPlane { get; init; }
    internal FalloutSerializedPlane? DiscardedPortalDeclaration { get; init; }
    // Physical slots are not deduplicated, sorted or inferred from positions.
    internal IReadOnlyList<FalloutFormKey?> OrderedOcclusionPlanes { get; init; } = [];
    internal IReadOnlyList<uint> DeclaredReferencePose { get; init; } = [];

    private static (FalloutSerializedPlane? Pose, FalloutSerializedPlane? Portal,
        IReadOnlyList<FalloutFormKey?> Links, IReadOnlyList<uint> ReferencePose) ReadSerialized(FalloutDynamicPrimitiveSource source,
        FalloutPluginStack records, FalloutPluginRecord record, FalloutDynamicPrimitiveRole role)
    {
        _ = FalloutOcclusionCullingSource.Read(source.EngineSha256);
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte>? One(string signature, int extent)
        {
            var rows = fields.Where(row => row.Signature == signature).ToArray();
            if (rows.Length > 1 || rows.Length == 1 && rows[0].Data.Length != extent)
                throw new InvalidDataException($"Serialized plane {signature} has duplicate or incomplete fields.");
            return rows.Length == 0 ? (ReadOnlyMemory<byte>?)null : rows[0].Data;
        }
        if (One("XPLT", 36) is not null)
            throw new NotSupportedException("XPLT is not the selected source's XPTL field; its runtime consumer is unowned.");
        var poseBytes = One("XOCP", 36); var portalBytes = One("XPTL", 36); var linkBytes = One("XORD", 16);
        var data = One("DATA", 24) ?? throw new InvalidDataException("Source plane lost its reference placement declaration.");
        var referencePose = new uint[6];
        for (var ordinal = 0; ordinal < referencePose.Length; ordinal++)
            referencePose[ordinal] = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(ordinal * sizeof(uint))..]);
        if ((poseBytes is not null || linkBytes is not null) && role != FalloutDynamicPrimitiveRole.OcclusionPlane)
            throw new NotSupportedException("Serialized occlusion state has no matching source plane factory.");
        var pose = poseBytes is null ? null : FalloutSerializedPlane.Decode("XOCP", poseBytes.Value.Span);
        var discarded = portalBytes is null ? null : FalloutSerializedPlane.Decode("XPTL", portalBytes.Value.Span);
        if (linkBytes is null) return (pose, discarded, [], Array.AsReadOnly(referencePose));
        var links = new FalloutFormKey?[4];
        for (var ordinal = 0; ordinal < links.Length; ordinal++)
        {
            var target = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(
                linkBytes.Value.Span[(ordinal * sizeof(uint))..]));
            links[ordinal] = target;
            if (target is not { } key) continue;
            var winning = records.GetEffective(key);
            if (winning.IsDeleted || winning.Signature != "REFR" ||
                FalloutCellSceneReader.ParentCell(winning) != FalloutCellSceneReader.ParentCell(record))
                throw new NotSupportedException("Ordered source plane link has no current winning resident reference in this CELL.");
            var names = winning.ReadSubrecords().Where(row => row.Signature == "NAME").ToArray();
            if (names is not [{ Data.Length: 4 }] || source.Role(records, winning.Plugin.AdjustFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span))) != FalloutDynamicPrimitiveRole.OcclusionPlane)
                throw new NotSupportedException("Ordered source plane target has no actual occlusion factory role.");
        }
        return (pose, discarded, Array.AsReadOnly(links), Array.AsReadOnly(referencePose));
    }
}
