using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerActorValueSource(FalloutFormKey Player, string PlayerWinner, string PlayerSha256,
    FalloutFormKey StatsOwner, string StatsWinner, string StatsSha256, IReadOnlyList<int> Special, int Level)
{
    internal static FalloutPlayerActorValueSource Read(FalloutPluginStack records)
    {
        // The engine creates reference 0x14 from the winning base NPC 0x7.
        // There is no placed ESM reference to fabricate or register here.
        var player = records.GetEffective(records.RuntimeFormKey(7));
        if (player.Signature != "NPC_") throw new InvalidDataException("Engine player base is not NPC_.");
        var stats = FalloutActorTemplateOwner.Resolve(records, player, 2);
        var fields = stats.ReadSubrecords().ToArray();
        var data = fields.Where(field => field.Signature == "DATA").ToArray();
        var acbs = fields.Where(field => field.Signature == "ACBS").ToArray();
        if (data.Length != 1 || data[0].Data.Length != 11 || acbs.Length != 1 || acbs[0].Data.Length != 24)
            throw new InvalidDataException("Engine player stats have invalid DATA/ACBS extents.");
        var level = FalloutActorLevel.Resolve(acbs[0].Data.Span, null);
        return new(player.FormKey, player.Plugin.Name, Hash(player), stats.FormKey, stats.Plugin.Name, Hash(stats),
            data[0].Data.Span[4..11].ToArray().Select(value => (int)value).ToArray(), level);
    }

    private static string Hash(FalloutPluginRecord source) => Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant();
}
