using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeTraitIdentity(
    uint RuntimeFormId,
    string EditorId,
    string DisplayName);

internal sealed record FalloutNativeTraitFarewellContract(
    IReadOnlyList<FalloutNativeTraitIdentity> Traits,
    int MaximumTraits,
    short TraitMenuStage,
    FalloutPlacedReference ExitTriggerReference,
    IReadOnlyList<float> ExitTriggerDimensionsGameUnits,
    short ExitTriggerFromStage,
    short FarewellStage,
    short CompletedStage,
    float CompletionDelaySeconds,
    string GrantSource,
    IReadOnlyDictionary<string, FalloutCampaignItem> GrantItems,
    IReadOnlyDictionary<string, int> GrantItemMultipliers);

internal static partial class FalloutNativeTraitFarewellResolver
{
    private const int PlayablePerkDataBytes = 5;
    private const int LeveledListEntryBytes = 12;
    private const int LeveledListCountOffset = 8;
    private static readonly string[] ItemSignatures =
        ["ALCH", "AMMO", "ARMO", "IMOD", "KEYM", "LVLI", "MISC", "WEAP"];
    private const string QuestEditorId = "VCG01";
    private const string ExitTriggerEditorId = "GSDocMitchellExitTrigger";
    private const string GenericTimerEditorId = "VGenericTimer";
    private const short TraitMenuStage = 102;
    private const short ExitTriggerFromStage = 110;
    private const short FarewellStage = 115;
    private const short CompletedStage = 200;
    private const int MaximumTraits = 2;
    private const int PrimitiveBytes = 32;

    internal static FalloutNativeTraitFarewellContract Resolve(
        FalloutPluginStack stack,
        FalloutOpeningControlGraph controls,
        FalloutCellScene cell)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(cell);
        if (!controls.Stage(QuestEditorId, TraitMenuStage).Source.Contains(
                "ShowTraitMenu", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Native {QuestEditorId}:{TraitMenuStage} lacks ShowTraitMenu.");
        _ = controls.Stage(QuestEditorId, ExitTriggerFromStage);
        _ = controls.Stage(QuestEditorId, FarewellStage);
        _ = controls.Stage(QuestEditorId, CompletedStage);

        var traits = stack.EffectiveRecords("PERK").Where(IsPlayableTrait)
            .Select(record => new FalloutNativeTraitIdentity(
                stack.RuntimeFormId(record.FormKey),
                ReadText(record, "EDID"),
                ReadText(record, "FULL")))
            .OrderBy(value => value.RuntimeFormId)
            .ToArray();
        if (traits.Length == 0 ||
            traits.Select(value => value.RuntimeFormId).Distinct().Count() != traits.Length)
            throw new InvalidDataException("Native playable trait graph is empty or duplicated.");

        var trigger = ExactlyOneByEditorId(
            stack.EffectiveRecords("ACTI"), ExitTriggerEditorId);
        var triggerScript = RequireScript(stack, trigger);
        var triggerSource = ReadSource(triggerScript);
        if (!triggerSource.Contains("onTriggerEnter player", StringComparison.OrdinalIgnoreCase) ||
            !StageCondition(ExitTriggerFromStage).IsMatch(triggerSource) ||
            !StageTarget(FarewellStage).IsMatch(triggerSource))
            throw new InvalidDataException(
                $"Native exit trigger script {triggerScript.FormKey} stage contract is unsupported.");
        var triggerReferences = cell.References.Where(value => value.Base == trigger.FormKey).ToArray();
        if (triggerReferences.Length != 1)
            throw new InvalidDataException(
                $"Native Doc exit trigger must have one reference; found {triggerReferences.Length}.");
        var triggerRecord = stack.GetEffective(triggerReferences[0].FormKey);
        var primitive = Single(triggerRecord, "XPRM");
        if (primitive.Length != PrimitiveBytes)
            throw new InvalidDataException("Native Doc exit trigger XPRM layout is unsupported.");
        var dimensions = new float[3];
        for (var index = 0; index < dimensions.Length; ++index)
        {
            dimensions[index] = BinaryPrimitives.ReadSingleLittleEndian(
                primitive.Span[(index * sizeof(float))..]);
            if (!float.IsFinite(dimensions[index]) || dimensions[index] <= 0.0f)
                throw new InvalidDataException("Native Doc exit trigger bounds are invalid.");
        }

        var quest = controls.Quests[QuestEditorId].Values.First().Quest;
        var questResults = stack.EffectiveRecords("INFO").Where(record =>
            LinksQuest(record, quest)).SelectMany(record =>
                record.ReadSubrecords().Where(value => value.Signature == "SCTX")
                    .Select(value => (Record: record, Source: ReadAscii(record, value.Data.Span))))
            .ToArray();
        var grants = questResults.Where(value => value.Source.Contains(
            "Basic starting equipment for all players", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (grants.Length != 1)
            throw new InvalidDataException(
                $"Native farewell starting-equipment result is ambiguous: {grants.Length}.");
        var completions = questResults.Where(value =>
            GenericTimerEvent().IsMatch(value.Source)).ToArray();
        if (completions.Length != 1)
            throw new InvalidDataException(
                $"Native farewell completion result is ambiguous: {completions.Length}.");
        var delayMatch = GenericTimerDelay().Match(completions[0].Source);
        if (!delayMatch.Success || !float.TryParse(
                delayMatch.Groups["delay"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var delay) || delay <= 0.0f)
            throw new InvalidDataException("Native farewell timer delay is unsupported.");
        var timerQuest = ExactlyOneByEditorId(stack.EffectiveRecords("QUST"), GenericTimerEditorId);
        var timerScript = RequireScript(stack, timerQuest);
        var timerSource = ReadSource(timerScript);
        if (!timerSource.Contains("nEvent == 3", StringComparison.OrdinalIgnoreCase) ||
            !StageTarget(CompletedStage).IsMatch(timerSource))
            throw new InvalidDataException("Native generic timer event 3 does not complete VCG01.");

        var itemEditorIds = AddItem().Matches(grants[0].Source).Cast<Match>()
            .Select(value => value.Groups["item"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var itemRecords = ItemSignatures.SelectMany(stack.EffectiveRecords)
            .Select(record => (Record: record, EditorId: TryReadEditorId(record)))
            .Where(value => value.EditorId is not null &&
                itemEditorIds.Contains(value.EditorId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(value => value.EditorId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.Record).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var aliases = new List<(string Alias, FalloutPluginRecord Record, int Multiplier)>();
        foreach (var editorId in itemEditorIds)
        {
            if (!itemRecords.TryGetValue(editorId, out var matches) || matches.Length != 1)
                throw new InvalidDataException(
                    $"Native farewell item {editorId} must resolve once; found {(matches?.Length ?? 0)}.");
            var concrete = ResolveConcreteItem(stack, matches[0]);
            aliases.Add((editorId, concrete.Record, concrete.Multiplier));
        }
        var requests = aliases.Select(value => value.Record)
            .DistinctBy(value => value.FormKey)
            .Select(value => new FalloutCampaignInventoryRequest(
                stack.RuntimeFormId(value.FormKey),
                ReadText(value, "EDID"),
                value.Signature,
                1))
            .ToArray();
        var concreteItems = FalloutCampaignInventoryResolver.Resolve(stack, requests, null).Items;
        var resolvedItems = aliases.ToDictionary(
            value => value.Alias,
            value => concreteItems.Single(item =>
                item.RuntimeFormId == stack.RuntimeFormId(value.Record.FormKey)),
            StringComparer.OrdinalIgnoreCase);
        var multipliers = aliases.ToDictionary(
            value => value.Alias,
            value => value.Multiplier,
            StringComparer.OrdinalIgnoreCase);
        return new FalloutNativeTraitFarewellContract(
            traits,
            MaximumTraits,
            TraitMenuStage,
            triggerReferences[0],
            dimensions,
            ExitTriggerFromStage,
            FarewellStage,
            CompletedStage,
            delay,
            grants[0].Source,
            resolvedItems,
            multipliers);
    }

    internal static void ValidateTraits(
        FalloutNativeTraitFarewellContract contract,
        IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Count > contract.MaximumTraits ||
            selection.Select(value => value.RuntimeFormId).Distinct().Count() != selection.Count ||
            selection.Any(value => !contract.Traits.Contains(value)))
            throw new InvalidDataException(
                "Native campaign traits differ from the live ShowTraitMenu/PERK contract.");
    }

    internal static FalloutOpeningInventoryGrant ResolveGrant(
        FalloutNativeTraitFarewellContract contract,
        FalloutOpeningInventoryGrant opening,
        IReadOnlyList<FalloutNativeSkillIdentity> tagSkills)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(tagSkills);
        var tags = tagSkills.Select(TagScriptName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counts = InterpretGrant(contract.GrantSource, tags);
        var items = opening.Inventory.Items.ToDictionary(
            value => value.RuntimeFormId,
            value => value);
        foreach (var pair in counts)
        {
            if (!contract.GrantItems.TryGetValue(pair.Key, out var prototype))
                throw new InvalidDataException($"Native farewell item is unresolved: {pair.Key}.");
            var resolvedCount = checked(pair.Value * contract.GrantItemMultipliers[pair.Key]);
            if (items.TryGetValue(prototype.RuntimeFormId, out var existing))
                items[prototype.RuntimeFormId] = existing with
                {
                    Count = checked(existing.Count + resolvedCount),
                };
            else
                items.Add(prototype.RuntimeFormId, prototype with { Count = resolvedCount });
        }
        return new FalloutOpeningInventoryGrant(
            new FalloutCampaignInventory(
                items.Values.OrderBy(value => value.RuntimeFormId).ToArray(),
                opening.Inventory.EquippedWeapon),
            opening.EquippedRuntimeFormIds);
    }

    private static IReadOnlyDictionary<string, int> InterpretGrant(
        string source,
        IReadOnlySet<string> tags)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var active = true;
        var branchMatched = false;
        foreach (var raw in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line.Length == 0)
                continue;
            var condition = TagCondition().Match(line);
            if (condition.Success)
            {
                var matched = tags.Contains(condition.Groups["skill"].Value);
                var command = condition.Groups["command"].Value;
                if (command.Equals("if", StringComparison.OrdinalIgnoreCase))
                {
                    active = matched;
                    branchMatched = matched;
                }
                else
                {
                    active = !branchMatched && matched;
                    branchMatched |= matched;
                }
                continue;
            }
            if (line.Equals("else", StringComparison.OrdinalIgnoreCase))
            {
                active = !branchMatched;
                branchMatched = true;
                continue;
            }
            if (line.Equals("endif", StringComparison.OrdinalIgnoreCase))
            {
                active = true;
                branchMatched = false;
                continue;
            }
            var addition = AddItem().Match(line);
            if (!addition.Success)
                continue;
            if (!active || !int.TryParse(addition.Groups["count"].Value, out var count) || count <= 0)
                continue;
            var editorId = addition.Groups["item"].Value;
            counts[editorId] = counts.GetValueOrDefault(editorId) + count;
        }
        if (counts.Count == 0)
            throw new InvalidDataException("Native farewell grant produced no items.");
        return counts;
    }

    private static string TagScriptName(FalloutNativeSkillIdentity skill) => skill.EditorId switch
    {
        "AVSmallGuns" => "Guns",
        "AVThrowing" => "Survival",
        _ when skill.EditorId.StartsWith("AV", StringComparison.Ordinal) => skill.EditorId[2..],
        _ => throw new InvalidDataException($"Native tag skill EDID is unsupported: {skill.EditorId}."),
    };

    private static bool IsPlayableTrait(FalloutPluginRecord record)
    {
        var data = record.ReadSubrecords().Where(value =>
            value.Signature == "DATA" &&
            value.Data.Length == PlayablePerkDataBytes).ToArray();
        return data.Length == 1 && data[0].Data.Span.SequenceEqual(
            new byte[] { 1, 1, 1, 1, 0 });
    }

    private static (FalloutPluginRecord Record, int Multiplier) ResolveConcreteItem(
        FalloutPluginStack stack,
        FalloutPluginRecord record)
    {
        if (record.Signature != "LVLI")
            return (record, 1);
        var entries = record.ReadSubrecords().Where(value => value.Signature == "LVLO").ToArray();
        if (entries.Length == 0 || entries.Any(value =>
                value.Data.Length != LeveledListEntryBytes))
            throw new InvalidDataException(
                $"Native farewell leveled item {record.FormKey} has an unsupported LVLO layout.");
        var targets = entries.Select(value =>
                record.Plugin.AdjustFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span[4..])))
            .Distinct()
            .ToArray();
        var counts = entries.Select(value =>
                BinaryPrimitives.ReadUInt16LittleEndian(
                    value.Data.Span[LeveledListCountOffset..]))
            .Distinct()
            .ToArray();
        if (targets.Length != 1 || counts.Length != 1 || counts[0] == 0)
            throw new InvalidDataException(
                $"Native farewell leveled item {ReadText(record, "EDID")}/{record.FormKey} " +
                $"does not reduce to one item: targets=" +
                $"{string.Join(',', targets)} counts=" +
                $"{string.Join(',', counts)}.");
        var target = stack.GetEffective(targets[0]);
        if (target.Signature == "LVLI" || !ItemSignatures.Contains(target.Signature))
            throw new InvalidDataException(
                $"Native farewell leveled item {record.FormKey} target is unsupported: {target.Signature}.");
        return (target, counts[0]);
    }

    private static bool LinksQuest(FalloutPluginRecord record, FalloutFormKey quest)
    {
        var links = record.ReadSubrecords().Where(value => value.Signature == "QSTI").ToArray();
        return links.Length == 1 && links[0].Data.Length == sizeof(uint) &&
            record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) == quest;
    }

    private static FalloutPluginRecord ExactlyOneByEditorId(
        IReadOnlyList<FalloutPluginRecord> records,
        string editorId)
    {
        var matches = records.Where(value =>
            TryReadEditorId(value)?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native {editorId} must resolve once; found {matches.Length}.");
        return matches[0];
    }

    private static FalloutPluginRecord RequireScript(
        FalloutPluginStack stack,
        FalloutPluginRecord owner)
    {
        var link = Single(owner, "SCRI");
        if (link.Length != sizeof(uint))
            throw new InvalidDataException($"Native {owner.FormKey} SCRI layout is unsupported.");
        var script = stack.GetEffective(owner.Plugin.AdjustFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(link.Span)));
        if (script.Signature != "SCPT")
            throw new InvalidDataException($"Native {owner.FormKey} SCRI target is not SCPT.");
        return script;
    }

    private static ReadOnlyMemory<byte> Single(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} requires one {signature}; found {rows.Length}.");
        return rows[0].Data;
    }

    private static string ReadSource(FalloutPluginRecord script) =>
        ReadAscii(script, Single(script, "SCTX").Span);

    private static string ReadAscii(FalloutPluginRecord record, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException($"Native {record.Signature} {record.FormKey} source is not ASCII.");
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static string ReadText(FalloutPluginRecord record, string signature)
    {
        var bytes = Single(record, signature).Span;
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1 || end == 0 ||
            bytes[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(bytes[..end]);
    }

    private static string? TryReadEditorId(FalloutPluginRecord record)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
        return rows.Length == 0 ? null : rows.Length == 1
            ? ReadText(record, "EDID")
            : throw new InvalidDataException($"Native {record.FormKey} repeats EDID.");
    }

    private static Regex StageCondition(short stage) => new(
        $@"\bGetStage\s+VCG01\s*==\s*{stage}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex StageTarget(short stage) => new(
        $@"\bSetStage\s+VCG01\s+{stage}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\bset\s+VGenericTimer\.nEvent\s+to\s+3\b", RegexOptions.IgnoreCase)]
    private static partial Regex GenericTimerEvent();

    [GeneratedRegex(@"\bset\s+VGenericTimer\.fTimer\s+to\s+(?<delay>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GenericTimerDelay();

    [GeneratedRegex(@"\bplayer\.additem\s+(?<item>[A-Za-z_][A-Za-z0-9_]*)\s+(?<count>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddItem();

    [GeneratedRegex(@"^(?<command>if|elseif)\s*\(?\s*IsPlayerTagSkill\s+(?<skill>[A-Za-z_][A-Za-z0-9_]*)\s*\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TagCondition();
}
