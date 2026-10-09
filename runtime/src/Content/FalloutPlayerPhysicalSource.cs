using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

// These are selected original constructor/query contracts. No process layout,
// source address, decoded image or original machine code is a runtime input.
internal sealed record FalloutPlayerPhysicalSource(FalloutFormKey Player, string PlayerSha256,
    FalloutFormKey StatsOwner, string StatsSha256, string RuntimeSha256, string ContractSha256)
{
    private const string Contract = "independent-player-sleep-flag-initial-false;" +
        "initial-physical-process-normal;bed-loading1-entry2-occupied3-exit4;" +
        "knockdown-and-recovery-query1;normal-query0;source-furniture-and-physics-publication-required";
    private static readonly HashSet<string> ReviewedImages = new(StringComparer.Ordinal)
    {
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
        "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e",
    };
    internal string Identity => Hash(JsonSerializer.Serialize(this));
    internal static string CurrentContractSha256 => Hash(Contract);
    internal static FalloutPlayerPhysicalSource Read(FalloutPluginStack records,
        FalloutAdvancementRuntimeReceipt runtime)
    {
        runtime.Validate();
        if (!ReviewedImages.Contains(runtime.EngineSha256))
            throw new NotSupportedException("Selected executable has no physical player initialization/query contract.");
        var actor = FalloutPlayerActorValueSource.Read(records);
        return new(actor.Player, actor.PlayerSha256, actor.StatsOwner, actor.StatsSha256,
            runtime.SourceSha256, Hash(Contract));
    }
    internal void Validate()
    {
        if (!FalloutActorFurnitureKey(Player) || !FalloutActorFurnitureKey(StatsOwner) ||
            !Digest(PlayerSha256) || !Digest(StatsSha256) || !Digest(RuntimeSha256) || ContractSha256 != Hash(Contract))
            throw new InvalidDataException("Player physical source identity or contract is invalid.");
    }
    internal void RequireCurrent(FalloutPlayerPhysicalSource current)
    {
        Validate(); current.Validate();
        if (this != current) throw new InvalidDataException("Saved player physical state has a different selected source.");
    }
    private static bool FalloutActorFurnitureKey(FalloutFormKey key) =>
        !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
