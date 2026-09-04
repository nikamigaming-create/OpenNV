using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeSpecialState(
    int Strength,
    int Perception,
    int Endurance,
    int Charisma,
    int Intelligence,
    int Agility,
    int Luck)
{
    private const int AgilityIndex = 5;
    private const int LuckIndex = 6;

    internal IReadOnlyList<int> Values =>
        [Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck];

    internal FalloutNativeSpecialState WithValue(int index, int value) => index switch
    {
        0 => this with { Strength = value },
        1 => this with { Perception = value },
        2 => this with { Endurance = value },
        3 => this with { Charisma = value },
        4 => this with { Intelligence = value },
        AgilityIndex => this with { Agility = value },
        LuckIndex => this with { Luck = value },
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

internal sealed record FalloutNativeVigorContract(
    FalloutFormKey Player,
    FalloutPlacedReference TriggerReference,
    IReadOnlyList<float> TriggerDimensionsGameUnits,
    FalloutPlacedReference TesterReference,
    short TriggerFromStage,
    short TesterStage,
    short CompletedStage,
    int RequiredTotal,
    int MinimumAttribute,
    int MaximumAttribute,
    FalloutNativeSpecialState Initial);

internal static partial class FalloutNativeVigorResolver
{
    private const int AgilityIndex = 5;
    private const int LuckIndex = 6;
    internal static readonly IReadOnlyList<string> AttributeNames =
        ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];

    private const string QuestEditorId = "VCG01";
    private const string PlayerEditorId = "Player";
    private const string TriggerEditorId = "VCG01VigorTesterTrigger";
    private const string TesterEditorId = "VCG01VigorTester";
    private const int PlayerDataBytes = 11;
    private const int PlayerAttributeOffset = sizeof(uint);
    private const int AttributeCount = 7;
    private const int PrimitiveBytes = 32;
    private const int PrimitiveDimensionCount = 3;
    private const short TriggerFromStage = 55;
    private const int MinimumAttribute = 1;
    private const int MaximumAttribute = 10;

    internal static FalloutNativeVigorContract Resolve(
        FalloutPluginStack stack,
        FalloutCellScene cell)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(cell);
        var player = ExactlyOneByEditorId(stack.EffectiveRecords("NPC_"), PlayerEditorId);
        var playerData = Single(player, "DATA");
        if (playerData.Length != PlayerDataBytes)
            throw new InvalidDataException(
                $"Native Player DATA must contain {PlayerDataBytes} bytes for SPECIAL.");
        var attributes = playerData.Span.Slice(PlayerAttributeOffset, AttributeCount).ToArray();
        var initial = new FalloutNativeSpecialState(
            attributes[0], attributes[1], attributes[2], attributes[3],
            attributes[4], attributes[AgilityIndex], attributes[LuckIndex]);

        var trigger = ExactlyOneByEditorId(stack.EffectiveRecords("ACTI"), TriggerEditorId);
        var triggerScript = RequireScript(stack, trigger);
        var triggerSource = Source(triggerScript);
        var testerStage = ParseSingleStage(triggerScript, triggerSource, "SetStage", QuestEditorId);
        if (!triggerSource.Contains("onTriggerEnter", StringComparison.OrdinalIgnoreCase) ||
            !triggerSource.Contains("IsActionRef player", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Native Vigor trigger script {triggerScript.FormKey} lacks its player-enter condition.");

        var tester = ExactlyOneByEditorId(stack.EffectiveRecords("ACTI"), TesterEditorId);
        var testerScript = RequireScript(stack, tester);
        var testerSource = Source(testerScript);
        if (!testerSource.Contains("OnActivate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Native Vigor tester script {testerScript.FormKey} lacks OnActivate.");
        var totalMatches = LoveTesterTotal().Matches(testerSource).Cast<Match>().ToArray();
        if (totalMatches.Length != 1 ||
            !int.TryParse(totalMatches[0].Groups["total"].Value, out var requiredTotal))
            throw new InvalidDataException(
                $"Native Vigor tester script {testerScript.FormKey} has no unique allocation total.");
        var completedStage = ParseSingleStage(testerScript, testerSource, "SetStage", QuestEditorId);
        if (!TesterStageCondition().IsMatch(testerSource) ||
            testerStage <= TriggerFromStage || completedStage <= testerStage ||
            requiredTotal is < AttributeCount * MinimumAttribute or > AttributeCount * MaximumAttribute ||
            initial.Values.Any(value => value is < MinimumAttribute or > MaximumAttribute) ||
            initial.Values.Sum() > requiredTotal)
            throw new InvalidDataException("Native Vigor stage or SPECIAL bounds are unsupported.");

        var triggerReference = ExactlyOneReference(cell, trigger.FormKey, TriggerEditorId);
        var testerReference = ExactlyOneReference(cell, tester.FormKey, TesterEditorId);
        var triggerRecord = stack.GetEffective(triggerReference.FormKey);
        var primitive = Single(triggerRecord, "XPRM");
        if (primitive.Length != PrimitiveBytes)
            throw new InvalidDataException(
                $"Native Vigor trigger {triggerReference.FormKey} XPRM layout is unsupported.");
        var dimensions = new float[PrimitiveDimensionCount];
        for (var index = 0; index < dimensions.Length; ++index)
        {
            dimensions[index] = BinaryPrimitives.ReadSingleLittleEndian(
                primitive.Span[(index * sizeof(float))..]);
            if (!float.IsFinite(dimensions[index]) || dimensions[index] <= 0.0f)
                throw new InvalidDataException(
                    $"Native Vigor trigger {triggerReference.FormKey} has invalid bounds.");
        }

        return new FalloutNativeVigorContract(
            player.FormKey,
            triggerReference,
            dimensions,
            testerReference,
            TriggerFromStage,
            checked((short)testerStage),
            checked((short)completedStage),
            requiredTotal,
            MinimumAttribute,
            MaximumAttribute,
            initial);
    }

    internal static void Validate(
        FalloutNativeVigorContract contract,
        FalloutNativeSpecialState state)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Values.Count != AttributeCount ||
            state.Values.Any(value =>
                value < contract.MinimumAttribute || value > contract.MaximumAttribute) ||
            state.Values.Sum() != contract.RequiredTotal)
            throw new InvalidDataException(
                "Native campaign SPECIAL state differs from the live Vigor allocation contract.");
    }

    private static FalloutPlacedReference ExactlyOneReference(
        FalloutCellScene cell,
        FalloutFormKey baseKey,
        string label)
    {
        var matches = cell.References.Where(value => value.Base == baseKey).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native {label} must have one reference in {cell.Cell.FormKey}; found {matches.Length}.");
        return matches[0];
    }

    private static FalloutPluginRecord ExactlyOneByEditorId(
        IReadOnlyList<FalloutPluginRecord> records,
        string editorId)
    {
        var matches = records.Where(value =>
            ReadEditorId(value).Equals(editorId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native {editorId} must resolve to one winning record; found {matches.Length}.");
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

    private static int ParseSingleStage(
        FalloutPluginRecord script,
        string source,
        string command,
        string questEditorId)
    {
        var matches = SetStage().Matches(source).Cast<Match>()
            .Where(value => value.Groups["command"].Value.Equals(
                    command, StringComparison.OrdinalIgnoreCase) &&
                value.Groups["quest"].Value.Equals(
                    questEditorId, StringComparison.OrdinalIgnoreCase))
            .Select(value => int.Parse(value.Groups["stage"].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native Vigor script {script.FormKey} has no unique {questEditorId} stage target.");
        return matches[0];
    }

    private static string Source(FalloutPluginRecord script)
    {
        var bytes = Single(script, "SCTX").Span;
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException($"Native Vigor script {script.FormKey} is not ASCII.");
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static ReadOnlyMemory<byte> Single(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} requires one {signature}; found {rows.Length}.");
        return rows[0].Data;
    }

    private static string ReadEditorId(FalloutPluginRecord record)
    {
        var bytes = Single(record, "EDID").Span;
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1 || end == 0 ||
            bytes[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} EDID is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(bytes[..end]);
    }

    [GeneratedRegex(@"\bShowLoveTesterMenuParams\s+(?<total>\d+)\s*;?", RegexOptions.IgnoreCase)]
    private static partial Regex LoveTesterTotal();

    [GeneratedRegex(@"\b(?<command>SetStage)\s+(?<quest>[A-Za-z_][A-Za-z0-9_]*)\s+(?<stage>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SetStage();

    [GeneratedRegex(@"\bGetStage\s+VCG01\s*==\s*60\b", RegexOptions.IgnoreCase)]
    private static partial Regex TesterStageCondition();
}
