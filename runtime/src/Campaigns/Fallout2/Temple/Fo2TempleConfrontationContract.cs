using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleCritterStats(
    int Strength,
    int Perception,
    int Endurance,
    int Agility,
    int HitPoints,
    int ActionPoints,
    int ArmorClass,
    int UnarmedDamage,
    int MeleeDamage,
    int Sequence,
    int CriticalChance,
    int AiPacket,
    int Team);

internal sealed record Fo2TempleWeaponStats(
    int MinimumDamage,
    int MaximumDamage,
    int DamageType,
    int MaximumRangePrimary,
    int MaximumRangeSecondary,
    int MinimumStrength,
    int ActionPointCostPrimary,
    int ActionPointCostSecondary);

internal sealed record Fo2TempleConfrontationCritter(
    int Serial,
    int Tile,
    int Elevation,
    int Rotation,
    string Fid,
    string Pid,
    string Sid,
    int ScriptIndex,
    string DisplayName,
    int CurrentHitPoints,
    int CurrentActionPoints,
    int RuntimeAiPacket,
    int RuntimeTeam,
    string PrototypeLogicalPath,
    string PrototypeSha256,
    string MessageLogicalPath,
    string MessageSha256,
    Fo2TempleCritterStats Stats);

internal sealed record Fo2TempleConfrontationLoot(
    int Serial,
    int Quantity,
    string Fid,
    string Pid,
    string DisplayName,
    string PrototypeLogicalPath,
    string PrototypeSha256,
    string MessageLogicalPath,
    string MessageSha256,
    Fo2TempleWeaponStats Weapon);

internal sealed record Fo2TempleConfrontationContract(
    string Schema,
    string Authority,
    Fo2TempleConfrontationCritter Critter,
    Fo2TempleConfrontationLoot DefeatLoot,
    bool ScriptExecuted,
    string ScriptBoundaryReason)
{
    internal const string ExpectedSchema = "opennv-fo2-temple-confrontation/v1";

    internal static Fo2TempleConfrontationContract Parse(
        JsonElement source,
        JsonElement resources,
        IReadOnlyList<Fo2MapObjectPlacement> placements)
    {
        var critter = source.GetProperty("critter");
        var critterPrototype = critter.GetProperty("prototype");
        var critterStats = critterPrototype.GetProperty("stats");
        var critterMessage = critter.GetProperty("messageCatalog");
        var loot = source.GetProperty("defeatLoot");
        var lootPrototype = loot.GetProperty("prototype");
        var weapon = lootPrototype.GetProperty("weapon");
        var lootMessage = loot.GetProperty("messageCatalog");
        var script = source.GetProperty("scriptBoundary");
        var result = new Fo2TempleConfrontationContract(
            Fo2TemplePresentationCatalog.RequiredString(source, "schema"),
            Fo2TemplePresentationCatalog.RequiredString(source, "authority"),
            new Fo2TempleConfrontationCritter(
                critter.GetProperty("serial").GetInt32(),
                critter.GetProperty("tile").GetInt32(),
                critter.GetProperty("elevation").GetInt32(),
                critter.GetProperty("rotation").GetInt32(),
                Fo2TemplePresentationCatalog.RequiredString(critter, "fid"),
                Fo2TemplePresentationCatalog.RequiredString(critter, "pid"),
                Fo2TemplePresentationCatalog.RequiredString(critter, "sid"),
                critter.GetProperty("scriptIndex").GetInt32(),
                Fo2TemplePresentationCatalog.RequiredString(critter, "displayName"),
                critter.GetProperty("currentHitPoints").GetInt32(),
                critter.GetProperty("currentActionPoints").GetInt32(),
                critter.GetProperty("runtimeAiPacket").GetInt32(),
                critter.GetProperty("runtimeTeam").GetInt32(),
                Fo2TemplePresentationCatalog.RequiredString(critterPrototype, "logicalPath"),
                Fo2TemplePresentationCatalog.RequiredHash(critterPrototype, "sha256"),
                Fo2TemplePresentationCatalog.RequiredString(critterMessage, "logicalPath"),
                Fo2TemplePresentationCatalog.RequiredHash(critterMessage, "sha256"),
                new Fo2TempleCritterStats(
                    critterStats.GetProperty("strength").GetInt32(),
                    critterStats.GetProperty("perception").GetInt32(),
                    critterStats.GetProperty("endurance").GetInt32(),
                    critterStats.GetProperty("agility").GetInt32(),
                    critterStats.GetProperty("hitPoints").GetInt32(),
                    critterStats.GetProperty("actionPoints").GetInt32(),
                    critterStats.GetProperty("armorClass").GetInt32(),
                    critterStats.GetProperty("unarmedDamage").GetInt32(),
                    critterStats.GetProperty("meleeDamage").GetInt32(),
                    critterStats.GetProperty("sequence").GetInt32(),
                    critterStats.GetProperty("criticalChance").GetInt32(),
                    critterStats.GetProperty("aiPacket").GetInt32(),
                    critterStats.GetProperty("team").GetInt32())),
            new Fo2TempleConfrontationLoot(
                loot.GetProperty("serial").GetInt32(),
                loot.GetProperty("quantity").GetInt32(),
                Fo2TemplePresentationCatalog.RequiredString(loot, "fid"),
                Fo2TemplePresentationCatalog.RequiredString(loot, "pid"),
                Fo2TemplePresentationCatalog.RequiredString(loot, "displayName"),
                Fo2TemplePresentationCatalog.RequiredString(lootPrototype, "logicalPath"),
                Fo2TemplePresentationCatalog.RequiredHash(lootPrototype, "sha256"),
                Fo2TemplePresentationCatalog.RequiredString(lootMessage, "logicalPath"),
                Fo2TemplePresentationCatalog.RequiredHash(lootMessage, "sha256"),
                new Fo2TempleWeaponStats(
                    weapon.GetProperty("minimumDamage").GetInt32(),
                    weapon.GetProperty("maximumDamage").GetInt32(),
                    weapon.GetProperty("damageType").GetInt32(),
                    weapon.GetProperty("maximumRangePrimary").GetInt32(),
                    weapon.GetProperty("maximumRangeSecondary").GetInt32(),
                    weapon.GetProperty("minimumStrength").GetInt32(),
                    weapon.GetProperty("actionPointCostPrimary").GetInt32(),
                    weapon.GetProperty("actionPointCostSecondary").GetInt32())),
            script.GetProperty("executed").GetBoolean(),
            Fo2TemplePresentationCatalog.RequiredString(script, "reason"));
        result.Validate(resources, placements);
        return result;
    }

    private void Validate(
        JsonElement resources,
        IReadOnlyList<Fo2MapObjectPlacement> placements)
    {
        var placed = placements.SingleOrDefault(row => row.Serial == Critter.Serial);
        var resourceIdentities = resources.EnumerateArray().Select(row =>
            $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Owns(string logicalPath, string sha256) =>
            resourceIdentities.Contains($"{logicalPath}|{sha256}");
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(Authority) ||
            placed is null || placed.Tile != Critter.Tile || placed.Elevation != Critter.Elevation ||
            placed.Rotation != Critter.Rotation || placed.Fid != Critter.Fid ||
            placed.Pid != Critter.Pid || placed.Sid != Critter.Sid ||
            placed.ScriptIndex != Critter.ScriptIndex || placed.ObjectType != 1 ||
            Critter.CurrentHitPoints <= 0 || Critter.CurrentActionPoints < 0 ||
            Critter.Stats.HitPoints <= 0 || Critter.Stats.ActionPoints <= 0 ||
            Critter.Stats.Team != Critter.RuntimeTeam ||
            Critter.Stats.AiPacket != Critter.RuntimeAiPacket ||
            DefeatLoot.Quantity <= 0 || DefeatLoot.Weapon.MinimumDamage <= 0 ||
            DefeatLoot.Weapon.MaximumDamage < DefeatLoot.Weapon.MinimumDamage ||
            DefeatLoot.Weapon.ActionPointCostPrimary <= 0 ||
            !Owns(Critter.PrototypeLogicalPath, Critter.PrototypeSha256) ||
            !Owns(Critter.MessageLogicalPath, Critter.MessageSha256) ||
            !Owns(DefeatLoot.PrototypeLogicalPath, DefeatLoot.PrototypeSha256) ||
            !Owns(DefeatLoot.MessageLogicalPath, DefeatLoot.MessageSha256) ||
            ScriptExecuted || string.IsNullOrWhiteSpace(ScriptBoundaryReason))
            throw new InvalidOperationException(
                "Fallout 2 bounded Temple confrontation contract is invalid.");
    }
}
