using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutStatisticPrefix
{
    ValueCommitted, ChallengeEntered, ChallengeReturned, MenuProbeEntered, MenuAbsent,
    MenuPresent, RefreshEntered, RefreshReturned, Complete,
}
internal sealed record FalloutStatisticMutation(long Ordinal, string SourceSha256, ushort Index, int Delta,
    int Before, int After, string Origin);
internal sealed record FalloutStatisticChallengeReceipt(FalloutStatisticMutation Mutation, uint Event,
    string ProducerSha256, long CompletedEventOrdinal);
internal sealed record FalloutStatisticMenuObservation(uint MenuId, bool Present, string ProbeSha256,
    ulong NativeInstanceId, string? PublicationSha256)
{
    internal void Validate(uint menu)
    {
        if (MenuId != menu || !FalloutAdvancementRuntimeReceipt.Digest(ProbeSha256) ||
            Present && (NativeInstanceId == 0 || !FalloutAdvancementRuntimeReceipt.Digest(PublicationSha256 ?? "")) ||
            !Present && (NativeInstanceId != 0 || PublicationSha256 is not null))
            throw new InvalidDataException("Statistic menu presence has no actual source/native probe receipt.");
    }
}
internal sealed record FalloutStatisticRefreshReceipt(FalloutStatisticMutation Mutation,
    FalloutStatisticMenuObservation Menu, long CompletedCallOrdinal);
internal sealed record FalloutStatisticOperation(FalloutStatisticMutation Mutation, FalloutStatisticPrefix Prefix,
    FalloutStatisticChallengeReceipt? Challenge, FalloutStatisticMenuObservation? Menu,
    FalloutStatisticRefreshReceipt? Refresh, string? FailureType, string? Error,
    IReadOnlyList<FalloutStatisticOperation>? Children = null, long ThroughOrdinal = 0);
internal sealed record FalloutPlayerStatisticsSnapshot(FalloutMiscellaneousStatisticSource Source,
    IReadOnlyList<int> Values, long Operations, FalloutStatisticOperation? LastOperation)
{
    internal void Validate()
    {
        Source.Validate();
        if (Values is null || Values.Count != Source.Rows.Count || Operations < 0 ||
            (Operations == 0) != (LastOperation is null)) throw new InvalidDataException("Statistic values/operation ordinal are incomplete.");
        if (LastOperation is not { } root)
        {
            if (Values.Where((value, index) => value != Source.Rows[index].InitialValue).Any())
                throw new InvalidDataException("Unchanged statistic constructor has mutated values.");
            return;
        }
        var nextOrdinal = root.Mutation.Ordinal;
        var latest = new Dictionary<ushort, int>();
        ValidateOperation(root);
        if (root.ThroughOrdinal != Operations || nextOrdinal != checked(Operations + 1) ||
            latest.Any(pair => Values[pair.Key] != pair.Value))
            throw new InvalidDataException("Statistic nested prefix/last committed values are inconsistent.");
        if (root.Mutation.Ordinal == 1 && Values.Where((value, index) => !latest.ContainsKey((ushort)index) && value != Source.Rows[index].InitialValue).Any())
            throw new InvalidDataException("First actual statistic transaction invented unrelated counter values.");

        void ValidateOperation(FalloutStatisticOperation operation)
        {
            var mutation = operation.Mutation ?? throw new InvalidDataException("Statistic operation has no committed value prefix.");
            if (mutation.Ordinal != nextOrdinal++ || mutation.SourceSha256 != Source.Identity || mutation.Index >= Values.Count ||
                mutation.Delta == 0 || mutation.After != unchecked(mutation.Before + mutation.Delta) ||
                string.IsNullOrWhiteSpace(mutation.Origin) || !Enum.IsDefined(operation.Prefix) ||
                operation.Children is null || operation.ThroughOrdinal < mutation.Ordinal ||
                (operation.Error is null) != (operation.FailureType is null) || operation.Error is not null && string.IsNullOrWhiteSpace(operation.FailureType) ||
                (operation.Error is null) != (operation.Prefix == FalloutStatisticPrefix.Complete) ||
                latest.TryGetValue(mutation.Index, out var previous) && previous != mutation.Before)
                throw new InvalidDataException("Statistic attempted operation/value/failure prefix is inconsistent.");
            latest[mutation.Index] = mutation.After;
            ValidateSuffix(operation, mutation);
            if (operation.Children.Count != 0 && (Source.ChallengeEvent is null || operation.Prefix < FalloutStatisticPrefix.ChallengeEntered))
                throw new InvalidDataException("Nested statistic operation has no entered source challenge owner.");
            foreach (var child in operation.Children)
            {
                if (child is null || child.Error is not null && operation.Error is null)
                    throw new InvalidDataException("Nested statistic failure was omitted from its owning callback.");
                ValidateOperation(child);
            }
            if (operation.ThroughOrdinal != nextOrdinal - 1)
                throw new InvalidDataException("Statistic nested operation lies outside its actual parent invocation.");
        }
    }

    private void ValidateSuffix(FalloutStatisticOperation operation, FalloutStatisticMutation mutation)
    {
        var challengeReturned = operation.Prefix >= FalloutStatisticPrefix.ChallengeReturned;
        if (Source.ChallengeEvent is { } kind)
        {
            if (challengeReturned && (operation.Challenge is not { } challenge || challenge.Mutation != mutation ||
                challenge.Event != kind || challenge.CompletedEventOrdinal <= 0 || !FalloutAdvancementRuntimeReceipt.Digest(challenge.ProducerSha256)))
                throw new InvalidDataException("Statistic challenge suffix has no real completed event receipt.");
            if (!challengeReturned && operation.Challenge is not null) throw new InvalidDataException("Statistic event receipt precedes its completion.");
        }
        else if (operation.Challenge is not null || operation.Prefix is FalloutStatisticPrefix.ChallengeEntered or FalloutStatisticPrefix.ChallengeReturned)
            throw new InvalidDataException("Statistic state invented a challenge consumer absent from its source.");
        if (operation.Prefix >= FalloutStatisticPrefix.MenuAbsent)
        {
            var menu = operation.Menu ?? throw new InvalidDataException("Statistic menu suffix has no completed probe.");
            menu.Validate(Source.StatsMenuId);
            if (operation.Prefix == FalloutStatisticPrefix.MenuAbsent && menu.Present ||
                operation.Prefix is FalloutStatisticPrefix.MenuPresent or FalloutStatisticPrefix.RefreshEntered or FalloutStatisticPrefix.RefreshReturned && !menu.Present)
                throw new InvalidDataException("Statistic menu branch differs from its actual probe.");
            if (menu.Present && operation.Prefix >= FalloutStatisticPrefix.RefreshReturned &&
                (operation.Refresh is not { } refresh || refresh.Mutation != mutation || refresh.Menu != menu || refresh.CompletedCallOrdinal <= 0))
                throw new InvalidDataException("Statistic refresh has no actual completed call receipt.");
            if (!menu.Present && operation.Refresh is not null) throw new InvalidDataException("Absent statistic menu has an invented refresh.");
        }
        else if (operation.Menu is not null || operation.Refresh is not null)
            throw new InvalidDataException("Statistic menu receipt precedes the actual source probe.");
        if (operation.Prefix < FalloutStatisticPrefix.RefreshReturned && operation.Refresh is not null)
            throw new InvalidDataException("Statistic refresh receipt precedes its completion.");
    }
}
