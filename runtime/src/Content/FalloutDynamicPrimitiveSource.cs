namespace OpenNV.Runtime.Content;

internal enum FalloutDynamicPrimitiveRole { None, OcclusionPlane, Room, Portal }

// These are original engine factory roles. They are not source-content IDs,
// internal-STAT exemptions or a rule that every model-less reference is hidden.
internal sealed record FalloutDynamicPrimitiveSource(string EngineSha256, string ContractSha256)
{
    private const string Engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    private const string Contract = "source-dynamic-primitive/v1;engine-STAT-factory-roles;" +
        "plane-rectangle-XZ;stored-middle-bound-independent;room-primitive-bound;" +
        "portal-derived-plane-owned-bound-node;two-source-XPOD-slots-in-order;" +
        "source-current-position-and-rotation;portal-reference-bit-14-independent;" +
        "existing-source-STAT-reused;no-editor-model-substitution;unknown-culling-refuses";

    internal static bool Supports(string engineSha256) => engineSha256 == Engine;

    internal static FalloutDynamicPrimitiveSource Read(string engineSha256)
    {
        if (!Supports(engineSha256))
            throw new NotSupportedException("Selected engine dynamic-primitive factory/culling declaration is unowned.");
        return new(engineSha256, FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }

    internal FalloutDynamicPrimitiveRole Role(FalloutPluginStack records, FalloutFormKey basis)
    {
        if (this != Read(EngineSha256)) throw new InvalidDataException("Dynamic-primitive source declaration changed.");
        return records.RuntimeFormId(basis) switch
        {
            0x17 => FalloutDynamicPrimitiveRole.OcclusionPlane,
            0x1f => FalloutDynamicPrimitiveRole.Room,
            0x20 => FalloutDynamicPrimitiveRole.Portal,
            _ => FalloutDynamicPrimitiveRole.None,
        };
    }
}
