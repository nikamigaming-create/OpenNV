using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicFumbleResolutionContract(
    string Schema,
    string ExactBuild,
    IReadOnlyList<string> EffectOrder,
    int HurtSelfMinimumDamage,
    int HurtSelfMaximumDamage,
    int RandomCrippleMinimumRoll,
    int RandomCrippleMaximumRoll,
    IReadOnlyList<string> RandomCrippleFlags,
    int LoseTurnActionPoints)
{
    internal const string ExpectedSchema = "opennv-classic-fumble-resolution/v1";
    internal static readonly string[] ExpectedEffectOrder =
    [
        "drop",
        "hit-self",
        "hurt-self",
        "lose-turn",
        "random-cripple",
        "random-hit",
    ];

    internal static ClassicFumbleResolutionContract Parse(JsonElement source)
    {
        var hurtSelf = ReadPair(source, "hurtSelfDamageRange");
        var randomCripple = ReadPair(source, "randomCrippleRollRange");
        var result = new ClassicFumbleResolutionContract(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            ReadStrings(source, "effectOrder"),
            hurtSelf[0],
            hurtSelf[1],
            randomCripple[0],
            randomCripple[1],
            ReadStrings(source, "randomCrippleFlags"),
            source.GetProperty("loseTurnActionPoints").GetInt32());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            !EffectOrder.SequenceEqual(ExpectedEffectOrder, StringComparer.Ordinal) ||
            HurtSelfMinimumDamage < 0 || HurtSelfMaximumDamage < HurtSelfMinimumDamage ||
            RandomCrippleMinimumRoll < 0 ||
            RandomCrippleMaximumRoll < RandomCrippleMinimumRoll ||
            RandomCrippleMaximumRoll - RandomCrippleMinimumRoll + 1 !=
                RandomCrippleFlags.Count ||
            RandomCrippleFlags.Count == 0 ||
            RandomCrippleFlags.Any(string.IsNullOrWhiteSpace) ||
            RandomCrippleFlags.Distinct(StringComparer.Ordinal).Count() !=
                RandomCrippleFlags.Count || LoseTurnActionPoints < 0)
            throw new InvalidOperationException(
                "Classic fumble-resolution contract is invalid.");
    }

    private static int[] ReadPair(JsonElement source, string property)
    {
        var values = source.GetProperty(property).EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException(
                $"Classic fumble range is invalid: {property}");
        return values;
    }

    private static string[] ReadStrings(JsonElement source, string property) =>
        source.GetProperty(property).EnumerateArray().Select(value =>
            value.GetString() ?? "").ToArray();

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Classic fumble-resolution string is empty: {property}");
    }
}

internal enum ClassicFumbleFollowUp
{
    None,
    HitSelf,
    RandomHit,
}

internal sealed record ClassicFumbleTransaction(
    ClassicRetailRandomState RandomState,
    int AttackerActionPoints,
    string? DroppedAttackSourceId,
    int SelfDamageBonus,
    string? RandomCrippleFlag,
    ClassicFumbleFollowUp FollowUp,
    string? FollowUpTargetId,
    IReadOnlySet<string> DamageFlags);

internal static class ClassicFumbleOwner
{
    internal static ClassicFumbleTransaction Resolve(
        ClassicFumbleResolutionContract fumbleContract,
        ClassicCriticalSelectionContract criticalContract,
        ClassicRetailRandomContract randomContract,
        ClassicRetailRandomState randomState,
        ClassicFumbleEffect effect,
        string attackerId,
        string originalTargetId,
        int attackerActionPoints,
        string? equippedAttackSourceId,
        bool attackSourceCanDrop,
        string? exactRandomHitTargetId)
    {
        fumbleContract.Validate();
        criticalContract.Validate();
        randomContract.Validate();
        if (fumbleContract.ExactBuild != criticalContract.ExactBuild ||
            fumbleContract.ExactBuild != randomContract.ExactBuild ||
            string.IsNullOrWhiteSpace(attackerId) ||
            string.IsNullOrWhiteSpace(originalTargetId) || attackerId == originalTargetId ||
            attackerActionPoints < 0 || attackSourceCanDrop &&
                string.IsNullOrWhiteSpace(equippedAttackSourceId))
            throw new InvalidOperationException("Classic fumble state is invalid.");

        var knownFlags = criticalContract.DecodeFlags(
            effect.DamageFlags.Select(flag => criticalContract.DamageFlags[flag])
                .Aggregate(0, (value, flag) => value | flag));
        var state = randomState;
        var selfDamageBonus = 0;
        string? crippleFlag = null;
        var actionPoints = attackerActionPoints;
        var dropped = knownFlags.Contains("drop") && attackSourceCanDrop
            ? equippedAttackSourceId
            : null;

        if (knownFlags.Contains("hurt-self"))
        {
            var roll = ClassicRetailRandom.Next(
                state,
                fumbleContract.HurtSelfMinimumDamage,
                fumbleContract.HurtSelfMaximumDamage,
                randomContract);
            state = roll.State;
            selfDamageBonus = roll.Value;
        }
        if (knownFlags.Contains("lose-turn"))
            actionPoints = fumbleContract.LoseTurnActionPoints;
        if (knownFlags.Contains("random-cripple"))
        {
            var roll = ClassicRetailRandom.Next(
                state,
                fumbleContract.RandomCrippleMinimumRoll,
                fumbleContract.RandomCrippleMaximumRoll,
                randomContract);
            state = roll.State;
            crippleFlag = fumbleContract.RandomCrippleFlags[
                roll.Value - fumbleContract.RandomCrippleMinimumRoll];
        }

        var followUp = ClassicFumbleFollowUp.None;
        string? followUpTarget = null;
        if (knownFlags.Contains("hit-self"))
        {
            followUp = ClassicFumbleFollowUp.HitSelf;
            followUpTarget = attackerId;
        }
        if (knownFlags.Contains("random-hit"))
        {
            if (string.IsNullOrWhiteSpace(exactRandomHitTargetId) ||
                exactRandomHitTargetId == attackerId ||
                exactRandomHitTargetId == originalTargetId)
                throw new InvalidOperationException(
                    "Classic random-hit requires an exact source-selected alternate target.");
            followUp = ClassicFumbleFollowUp.RandomHit;
            followUpTarget = exactRandomHitTargetId;
        }

        return new ClassicFumbleTransaction(
            state,
            actionPoints,
            dropped,
            selfDamageBonus,
            crippleFlag,
            followUp,
            followUpTarget,
            knownFlags);
    }
}
