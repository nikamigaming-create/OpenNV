namespace OpenNV.Runtime.Content;

internal sealed record FalloutPrimitiveContactSource(string EngineSha256, string ContractSha256)
{
    private const string ReviewedEngine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    private const string Rules = "source-placed-activator-primitive/v1;model-load-before-primitive;" +
        "source-XPRM-box-or-first-bound-radius-sphere;root-native-collision-replaced;" +
        "query-volume-independent-of-visual-art;source-placement-and-current-enable;" +
        "single-contact-owner;no-key-replay;other-reference-types-and-filters-independent";

    internal static FalloutPrimitiveContactSource Read(string engineSha256)
    {
        if (engineSha256 != ReviewedEngine)
            throw new NotSupportedException("Selected source activator primitive/model collision replacement is unowned.");
        return new(engineSha256, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }

    internal void RequireReplacement(string signature, FalloutReferencePrimitive primitive,
        int sourceRootPhantoms, int descendantPhantoms)
    {
        if (this != Read(EngineSha256) || signature != "ACTI" || primitive.Type is not (1 or 2) ||
            primitive.CollisionLayer is not (null or 12) || sourceRootPhantoms != 1 || descendantPhantoms != 1)
            throw new NotSupportedException("Primitive collision replacement needs its actual admitted activator/root query-volume consumer.");
    }
}
