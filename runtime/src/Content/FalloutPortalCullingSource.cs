namespace OpenNV.Runtime.Content;

// Source selection owns the engine algorithm; record names and model paths do
// not select culling behavior. Camera/world observations remain live inputs.
internal sealed record FalloutPortalCullingSource(string EngineSha256, string ContractSha256,
    float CoplanarGameUnits)
{
    private const string Engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    private const string Contract = "source-room-portal-culling/v1;ordered-room-and-portal-registration;" +
        "two-physical-room-slots;null-is-external;camera-near-point;cached-containing-room;" +
        "ordered-containing-starts;first-start-cache;ordered-breadth-first-neighbors;" +
        "portal-visited-once;window-intersection-and-union;camera-rectangle-cone;" +
        "coplanar-five-game-units;linked-bound-union;source-enabled-native-lifetimes;" +
        "fresh-native-camera-after-cold;render-gate-independent-of-enable-and-collision";

    internal static bool Supports(string engineSha256) => engineSha256 == Engine;

    internal static FalloutPortalCullingSource Read(string engineSha256)
    {
        if (!Supports(engineSha256))
            throw new NotSupportedException("Selected source room/portal camera algorithm is unowned.");
        return new(engineSha256, FalloutAdvancementRuntimeReceipt.Hash(Contract), 5);
    }

    internal void RequireCurrent()
    {
        if (this != Read(EngineSha256))
            throw new InvalidDataException("Room/portal source declaration changed.");
    }
}
