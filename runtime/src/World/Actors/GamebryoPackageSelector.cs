using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed record GamebryoPackageCondition(
    string FunctionName,
    GamebryoPackageComparison Comparison,
    double ComparisonValue,
    string QuestFormId,
    uint VariableIndex,
    uint RunOn,
    string ReferenceFormId);

internal sealed record GamebryoPackageTarget(
    string Kind,
    string? ReferenceFormId,
    SourcePackagePlacement? Placement)
{
    internal static readonly GamebryoPackageTarget None = new("none", null, null);
}

internal sealed record GamebryoPackageCandidate<T>(
    string FormId,
    IReadOnlyList<GamebryoPackageCondition> Conditions,
    GamebryoPackageTarget Target,
    SourceActorAnimation? Animation,
    T Value);

internal sealed record GamebryoPackageState(
    IReadOnlyDictionary<string, int> QuestStages,
    IReadOnlySet<string> CompletedQuests,
    IReadOnlyDictionary<string, double> QuestVariables);

internal static class GamebryoPackageSelector
{
    private const uint SubjectRunOn = 0;

    internal static GamebryoPackageCandidate<T>? SelectFirst<T>(
        IReadOnlyList<GamebryoPackageCandidate<T>> orderedCandidates,
        GamebryoPackageState state,
        bool requireMatch)
    {
        if (orderedCandidates.Count == 0 ||
            orderedCandidates.Any(value => string.IsNullOrWhiteSpace(value.FormId)) ||
            orderedCandidates.Select(value => value.FormId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != orderedCandidates.Count)
            throw new InvalidOperationException(
                "Source package selection has no unique ordered candidates.");
        foreach (var candidate in orderedCandidates)
        {
            ValidateTarget(candidate.Target);
            foreach (var condition in candidate.Conditions)
                ValidateCondition(condition);
            if (candidate.Animation is { } animation)
                ValidateAnimation(animation);
        }
        foreach (var candidate in orderedCandidates)
        {
            if (candidate.Conditions.All(condition => Evaluate(condition, state)))
                return candidate;
        }
        if (requireMatch)
            throw new InvalidOperationException("Source package selection has no eligible package.");
        return null;
    }

    private static bool Evaluate(
        GamebryoPackageCondition condition,
        GamebryoPackageState state)
    {
        var actual = condition.FunctionName.ToLowerInvariant() switch
        {
            "getstage" => state.QuestStages.GetValueOrDefault(condition.QuestFormId),
            "getquestcompleted" => state.CompletedQuests.Contains(condition.QuestFormId)
                ? 1.0
                : 0.0,
            "getquestvariable" => state.QuestVariables.GetValueOrDefault(
                VariableKey(condition.QuestFormId, condition.VariableIndex)),
            _ => throw new InvalidOperationException(
                $"Source package condition function is unsupported: " +
                $"{condition.FunctionName}"),
        };
        return Compare(condition.Comparison, actual, condition.ComparisonValue);
    }

    private static void ValidateCondition(GamebryoPackageCondition condition)
    {
        if (condition.RunOn != SubjectRunOn ||
            !string.IsNullOrEmpty(condition.ReferenceFormId) &&
            !condition.ReferenceFormId.Equals("00000000", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Source package condition has an unsupported run-on target.");
        if (string.IsNullOrWhiteSpace(condition.QuestFormId) ||
            condition.FunctionName.ToLowerInvariant() is not
                ("getstage" or "getquestcompleted" or "getquestvariable"))
            throw new InvalidOperationException(
                $"Source package condition function is unsupported: " +
                $"{condition.FunctionName}");
        _ = Compare(condition.Comparison, 0.0, condition.ComparisonValue);
    }

    private static void ValidateAnimation(SourceActorAnimation animation)
    {
        if (string.IsNullOrWhiteSpace(animation.LogicalPath) ||
            string.IsNullOrWhiteSpace(animation.Sha256) ||
            string.IsNullOrWhiteSpace(animation.SequenceName) ||
            string.IsNullOrWhiteSpace(animation.AccumulationRootTranslationDisposition) ||
            !float.IsFinite(animation.StartSeconds) ||
            !float.IsFinite(animation.StopSeconds) ||
            animation.StartSeconds != 0.0f ||
            animation.StopSeconds <= animation.StartSeconds)
            throw new InvalidOperationException(
                "Source package animation contract is incomplete.");
        _ = ActorAnimationPlayback.LoopModeForCycleType(animation.CycleType);
    }

    private static bool Compare(
        GamebryoPackageComparison comparison,
        double actual,
        double expected)
    {
        if (!double.IsFinite(actual) || !double.IsFinite(expected))
            throw new InvalidOperationException(
                "Source package condition comparison is non-finite.");
        return comparison switch
        {
            GamebryoPackageComparison.Equal => Mathf.IsEqualApprox(actual, expected),
            GamebryoPackageComparison.NotEqual => !Mathf.IsEqualApprox(actual, expected),
            GamebryoPackageComparison.Greater => actual > expected,
            GamebryoPackageComparison.GreaterOrEqual => actual >= expected,
            GamebryoPackageComparison.Less => actual < expected,
            GamebryoPackageComparison.LessOrEqual => actual <= expected,
            _ => throw new InvalidOperationException(
                $"Source package comparison is unsupported: {comparison}"),
        };
    }

    private static void ValidateTarget(GamebryoPackageTarget target)
    {
        if (target.Kind.Equals("none", StringComparison.Ordinal))
        {
            if (target.ReferenceFormId is not null || target.Placement is not null)
                throw new InvalidOperationException(
                    "Source package without a target contains a reference.");
            return;
        }
        if (target.Kind is "nearReference" or "referenceMarker" &&
            !string.IsNullOrWhiteSpace(target.ReferenceFormId) &&
            target.Placement is { } placement &&
            placement.Kind.Equals(target.Kind, StringComparison.Ordinal) &&
            placement.TargetFormId.Equals(
                target.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase))
            return;
        throw new InvalidOperationException(
            $"Source package target is unsupported: {target.Kind}");
    }

    internal static string VariableKey(string questFormId, uint variableIndex) =>
        $"{questFormId}.{variableIndex}";
}

internal enum GamebryoPackageComparison
{
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}
