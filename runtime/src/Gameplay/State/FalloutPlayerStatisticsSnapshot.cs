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
    FalloutStatisticRefreshReceipt? Refresh, string? FailureType, string? Error);
internal sealed record FalloutPlayerStatisticsSnapshot(FalloutMiscellaneousStatisticSource Source,
    IReadOnlyList<int> Values, long Operations, FalloutStatisticOperation? LastOperation)
{
    internal void Validate()
    {
        Source.Validate();
        if (Values is null || Values.Count != Source.Rows.Count || Operations < 0 ||
            (Operations == 0) != (LastOperation is null)) throw new InvalidDataException("Statistic values/operation ordinal are incomplete.");
        if (LastOperation is not { } last)
        {
            if (Values.Where((value, index) => value != Source.Rows[index].InitialValue).Any())
                throw new InvalidDataException("Unchanged statistic constructor has mutated values.");
            return;
        }
        var mutation = last.Mutation ?? throw new InvalidDataException("Statistic operation has no committed value prefix.");
        if (mutation.Ordinal != Operations || mutation.SourceSha256 != Source.Identity || mutation.Index >= Values.Count ||
            mutation.Delta == 0 || mutation.After != unchecked(mutation.Before + mutation.Delta) || Values[mutation.Index] != mutation.After ||
            string.IsNullOrWhiteSpace(mutation.Origin) || !Enum.IsDefined(last.Prefix) ||
            (last.Error is null) != (last.FailureType is null) || last.Error is not null && string.IsNullOrWhiteSpace(last.FailureType) ||
            (last.Error is null) != (last.Prefix == FalloutStatisticPrefix.Complete))
            throw new InvalidDataException("Statistic attempted operation/value/failure prefix is inconsistent.");
        var challengeReturned = last.Prefix >= FalloutStatisticPrefix.ChallengeReturned;
        if (Source.ChallengeEvent is { } kind)
        {
            if (challengeReturned && (last.Challenge is not { } challenge || challenge.Mutation != mutation ||
                challenge.Event != kind || challenge.CompletedEventOrdinal <= 0 || !FalloutAdvancementRuntimeReceipt.Digest(challenge.ProducerSha256)))
                throw new InvalidDataException("Statistic challenge suffix has no real completed event receipt.");
            if (!challengeReturned && last.Challenge is not null) throw new InvalidDataException("Statistic event receipt precedes its completion.");
        }
        else if (last.Challenge is not null || last.Prefix is FalloutStatisticPrefix.ChallengeEntered or FalloutStatisticPrefix.ChallengeReturned)
            throw new InvalidDataException("Statistic state invented a challenge consumer absent from its source.");
        if (last.Prefix >= FalloutStatisticPrefix.MenuAbsent)
        {
            var menu = last.Menu ?? throw new InvalidDataException("Statistic menu suffix has no completed probe.");
            menu.Validate(Source.StatsMenuId);
            if (last.Prefix == FalloutStatisticPrefix.MenuAbsent && menu.Present ||
                last.Prefix is FalloutStatisticPrefix.MenuPresent or FalloutStatisticPrefix.RefreshEntered or FalloutStatisticPrefix.RefreshReturned && !menu.Present)
                throw new InvalidDataException("Statistic menu branch differs from its actual probe.");
            if (menu.Present && last.Prefix >= FalloutStatisticPrefix.RefreshReturned &&
                (last.Refresh is not { } refresh || refresh.Mutation != mutation || refresh.Menu != menu || refresh.CompletedCallOrdinal <= 0))
                throw new InvalidDataException("Statistic refresh has no actual completed call receipt.");
            if (!menu.Present && last.Refresh is not null) throw new InvalidDataException("Absent statistic menu has an invented refresh.");
        }
        else if (last.Menu is not null || last.Refresh is not null)
            throw new InvalidDataException("Statistic menu receipt precedes the actual source probe.");
        if (last.Prefix < FalloutStatisticPrefix.RefreshReturned && last.Refresh is not null)
            throw new InvalidDataException("Statistic refresh receipt precedes its completion.");
    }
}
