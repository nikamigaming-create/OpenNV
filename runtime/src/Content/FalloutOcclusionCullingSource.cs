namespace OpenNV.Runtime.Content;

internal sealed record FalloutOcclusionCullingSource(string EngineSha256, string ContractSha256,
    float CoplanarGameUnits)
{
    private const string Engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    private const string Contract = "source-occlusion-culling/v1;XOCP-nine-physical-floats;" +
        "source-clockwise-axis-angle-without-normalization;independent-stored-center-and-half-extents;" +
        "XPTL-consumed-discarded;XPLT-unknown;four-physical-nullable-XORD-slots;" +
        "factory-automatic-mode;actual-current-camera-six-halfspaces;ordered-native-plane-links;" +
        "negative-bounding-sphere-cone;frustum-side-mask;linked-negative-AND-OR;" +
        "original-enable-independent-of-visibility;fresh-native-frame-after-cold;independent-retirement";

    internal static FalloutOcclusionCullingSource Read(string engineSha256)
    {
        if (engineSha256 != Engine)
            throw new NotSupportedException("Selected engine serialized-plane/occlusion frame algorithm is unowned.");
        return new(engineSha256, FalloutAdvancementRuntimeReceipt.Hash(Contract), 5);
    }
    internal void RequireCurrent()
    {
        if (this != Read(EngineSha256)) throw new InvalidDataException("Occlusion source declaration changed.");
    }
}
