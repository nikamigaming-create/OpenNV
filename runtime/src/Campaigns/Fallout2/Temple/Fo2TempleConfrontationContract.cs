using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

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
    int ActionPointCostSecondary,
    int AnimationCode);

internal sealed record Fo2TempleGuardianDialogueSegment(
    int? MessageId,
    string Text,
    bool PlayerName);

internal sealed record Fo2TempleGuardianDialogueOption(
    int MessageId,
    string Text,
    string Target,
    int? MinimumIntelligence,
    int? MaximumIntelligence,
    int Reaction);

internal sealed record Fo2TempleGuardianDialogueNode(
    string Id,
    IReadOnlyList<Fo2TempleGuardianDialogueSegment> Reply,
    IReadOnlyList<Fo2TempleGuardianDialogueOption> Options);

internal sealed record Fo2TempleGuardianScript(
    string Schema,
    string Authority,
    int ScriptsListIndex,
    string ScriptsListLogicalPath,
    string ScriptsListSha256,
    string ProgramLogicalPath,
    string ProgramSha256,
    string MessageLogicalPath,
    string MessageSha256,
    int MessageListId,
    IReadOnlySet<string> PreTrialPlayerArtFids,
    string InitialNode,
    string TerminalNode,
    IReadOnlyDictionary<string, Fo2TempleGuardianDialogueNode> Nodes,
    IReadOnlyDictionary<int, string> DisplayMessages,
    ClassicScriptProgram EffectProgram,
    string ContractSha256);

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
    Fo2TempleGuardianScript GuardianScript,
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
        var guardian = ReadGuardianScript(source.GetProperty("guardianScript"));
        var script = source.GetProperty("scriptBoundary");
        if (!script.GetProperty("boundedDialogueExecuted").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 ACKlint bounded dialogue contract is unavailable.");
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
                    weapon.GetProperty("actionPointCostSecondary").GetInt32(),
                    weapon.GetProperty("animationCode").GetInt32())),
            guardian,
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
        var topLevelCritters = placements
            .Where(row => row.TopLevel && row.ObjectType == 1)
            .ToArray();
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
            !placed.TopLevel || topLevelCritters.Length != 1 ||
            topLevelCritters[0].Serial != Critter.Serial ||
            Critter.CurrentHitPoints <= 0 || Critter.CurrentActionPoints < 0 ||
            Critter.Stats.HitPoints <= 0 || Critter.Stats.ActionPoints <= 0 ||
            Critter.Stats.Team != Critter.RuntimeTeam ||
            Critter.Stats.AiPacket != Critter.RuntimeAiPacket ||
            DefeatLoot.Quantity <= 0 || DefeatLoot.Weapon.MinimumDamage <= 0 ||
            DefeatLoot.Weapon.MaximumDamage < DefeatLoot.Weapon.MinimumDamage ||
            DefeatLoot.Weapon.ActionPointCostPrimary <= 0 ||
            DefeatLoot.Weapon.AnimationCode !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode ||
            !Owns(Critter.PrototypeLogicalPath, Critter.PrototypeSha256) ||
            !Owns(Critter.MessageLogicalPath, Critter.MessageSha256) ||
            !Owns(DefeatLoot.PrototypeLogicalPath, DefeatLoot.PrototypeSha256) ||
            !Owns(DefeatLoot.MessageLogicalPath, DefeatLoot.MessageSha256) ||
            !Owns(GuardianScript.ScriptsListLogicalPath, GuardianScript.ScriptsListSha256) ||
            !Owns(GuardianScript.ProgramLogicalPath, GuardianScript.ProgramSha256) ||
            !Owns(GuardianScript.MessageLogicalPath, GuardianScript.MessageSha256) ||
            GuardianScript.ScriptsListIndex != Critter.ScriptIndex ||
            !GuardianScript.PreTrialPlayerArtFids.SetEquals(
                new[] { "0100003d", "0100003e" }) ||
            ScriptExecuted ||
            string.IsNullOrWhiteSpace(ScriptBoundaryReason))
            throw new InvalidOperationException(
                "Fallout 2 bounded Temple confrontation contract is invalid.");
    }

    private static Fo2TempleGuardianScript ReadGuardianScript(JsonElement source)
    {
        var program = source.GetProperty("program");
        var message = source.GetProperty("messageCatalog");
        var boundary = source.GetProperty("implementedBoundary");
        var nodes = source.GetProperty("nodes").EnumerateArray()
            .Select(ReadGuardianNode)
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        var displayMessages = source.GetProperty("displayMessages").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("messageId").GetInt32(),
                row => Fo2TemplePresentationCatalog.RequiredString(row, "text"));
        var effectProgram = ClassicScriptProgram.Parse(source.GetProperty("effectProgram"));
        var result = new Fo2TempleGuardianScript(
            Fo2TemplePresentationCatalog.RequiredString(source, "schema"),
            Fo2TemplePresentationCatalog.RequiredString(source, "authority"),
            program.GetProperty("scriptsListIndex").GetInt32(),
            Fo2TemplePresentationCatalog.RequiredString(program, "scriptsListLogicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(program, "scriptsListSha256"),
            Fo2TemplePresentationCatalog.RequiredString(program, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(program, "sha256"),
            Fo2TemplePresentationCatalog.RequiredString(message, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(message, "sha256"),
            message.GetProperty("messageListId").GetInt32(),
            source.GetProperty("preTrialPlayerArtFids").EnumerateArray()
                .Select(row => row.GetString() ?? "")
                .ToHashSet(StringComparer.Ordinal),
            Fo2TemplePresentationCatalog.RequiredString(source, "initialNode"),
            Fo2TemplePresentationCatalog.RequiredString(source, "terminalNode"),
            nodes,
            displayMessages,
            effectProgram,
            Fo2TemplePresentationCatalog.RequiredHash(source, "contractSha256"));
        var lookState = new ClassicScriptState();
        var firstLook = effectProgram.ExecuteWithActions(
            "look_at_p_proc", lookState, new ClassicScriptContext(false, false, default));
        var repeatLook = effectProgram.ExecuteWithActions(
            "look_at_p_proc", lookState, new ClassicScriptContext(false, false, default));
        var lookMessageIds = firstLook.DisplayMessages.Concat(repeatLook.DisplayMessages)
            .Select(row => row.MessageId).ToHashSet();
        var talkEntries = result.PreTrialPlayerArtFids.Select(fid =>
            effectProgram.ExecuteWithActions(
                "talk_p_proc",
                new ClassicScriptState(),
                new ClassicScriptContext(false, false, default, fid))).ToArray();
        bool DialogueMatches(Fo2TempleGuardianDialogueNode node)
        {
            var execution = effectProgram.ExecuteWithActions(
                node.Id,
                new ClassicScriptState(),
                new ClassicScriptContext(false, false, default));
            var reply = execution.DialogueReply.Select(segment =>
                segment.PlayerName ? (int?)null : segment.Message!.Value.MessageId).ToArray();
            var expectedReply = node.Reply.Select(segment => segment.MessageId).ToArray();
            var options = execution.DialogueOptions.Select(option =>
                (option.Message.MessageId, option.Target, option.MinimumIntelligence,
                    option.MaximumIntelligence, option.Reaction)).ToArray();
            var expectedOptions = node.Options.Select(option =>
                (option.MessageId, option.Target, option.MinimumIntelligence,
                    option.MaximumIntelligence, option.Reaction)).ToArray();
            return execution.Executed && reply.SequenceEqual(expectedReply) &&
                options.SequenceEqual(expectedOptions) &&
                execution.DialogueReply.Where(segment => !segment.PlayerName).All(segment =>
                    segment.Message!.Value.MessageListId == result.MessageListId) &&
                execution.DialogueOptions.All(option =>
                    option.Message.MessageListId == result.MessageListId);
        }
        if (result.Schema != "opennv-fo2-acklint-guardian-script/v1" ||
            string.IsNullOrWhiteSpace(result.Authority) ||
            result.ScriptsListIndex != 750 ||
            !result.ScriptsListLogicalPath.Equals(
                "scripts\\scripts.lst", StringComparison.OrdinalIgnoreCase) ||
            !result.ProgramLogicalPath.Equals(
                "scripts\\acklint.int", StringComparison.OrdinalIgnoreCase) ||
            !result.MessageLogicalPath.Equals(
                "text\\english\\dialog\\acklint.msg", StringComparison.OrdinalIgnoreCase) ||
            result.MessageListId != 751 || result.Nodes.Count == 0 ||
            !result.Nodes.ContainsKey(result.InitialNode) ||
            talkEntries.Length == 0 || talkEntries.Any(entry =>
                !entry.Executed || entry.OpenDialogueNode != result.InitialNode) ||
            result.Nodes.Values.Any(node => !DialogueMatches(node)) ||
            !firstLook.Executed || !firstLook.ScriptOverrides ||
            firstLook.DisplayMessages.Count != 1 ||
            firstLook.DisplayMessages[0].MessageListId != result.MessageListId ||
            !repeatLook.Executed || !repeatLook.ScriptOverrides ||
            repeatLook.DisplayMessages.Count != 1 ||
            repeatLook.DisplayMessages[0].MessageListId != result.MessageListId ||
            lookMessageIds.Count != 2 ||
            !result.DisplayMessages.Keys.ToHashSet().SetEquals(lookMessageIds) ||
            !boundary.GetProperty("dialogueNodes").GetBoolean() ||
            !boundary.GetProperty("pickupToAttackTransition").GetBoolean() ||
            boundary.GetProperty("generalIntExecution").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 ACKlint guardian script contract is invalid.");
        if (result.Nodes.Values.SelectMany(node => node.Options).Any(option =>
                !result.Nodes.ContainsKey(option.Target) && option.Target != result.TerminalNode ||
                (option.MinimumIntelligence is not null) ==
                    (option.MaximumIntelligence is not null) ||
                option.MinimumIntelligence is < 0 || option.MaximumIntelligence is < 0 ||
                string.IsNullOrWhiteSpace(option.Text)))
            throw new InvalidOperationException(
                "Fallout 2 ACKlint guardian dialogue graph is invalid.");
        return result;
    }

    private static Fo2TempleGuardianDialogueNode ReadGuardianNode(JsonElement source) => new(
        Fo2TemplePresentationCatalog.RequiredString(source, "id"),
        source.GetProperty("reply").EnumerateArray().Select(segment =>
        {
            var playerName = segment.TryGetProperty("playerName", out var name) &&
                name.GetBoolean();
            var hasMessage = segment.TryGetProperty("messageId", out var messageId);
            var text = hasMessage
                ? Fo2TemplePresentationCatalog.RequiredString(segment, "text")
                : "";
            if (playerName == hasMessage)
                throw new InvalidOperationException(
                    "Fallout 2 ACKlint reply segment is invalid.");
            return new Fo2TempleGuardianDialogueSegment(
                hasMessage ? messageId.GetInt32() : null,
                text,
                playerName);
        }).ToArray(),
        source.GetProperty("options").EnumerateArray().Select(option =>
            new Fo2TempleGuardianDialogueOption(
                option.GetProperty("messageId").GetInt32(),
                Fo2TemplePresentationCatalog.RequiredString(option, "text"),
                Fo2TemplePresentationCatalog.RequiredString(option, "target"),
                option.GetProperty("minimumIntelligence").ValueKind == JsonValueKind.Null
                    ? null
                    : option.GetProperty("minimumIntelligence").GetInt32(),
                option.GetProperty("maximumIntelligence").ValueKind == JsonValueKind.Null
                    ? null
                    : option.GetProperty("maximumIntelligence").GetInt32(),
                option.GetProperty("reaction").GetInt32()))
            .ToArray());
}
