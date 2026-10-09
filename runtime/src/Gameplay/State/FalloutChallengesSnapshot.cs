using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutChallengeSnapshot(FalloutFormKey Form, string SourceSha256,
    int Progress, uint RuntimeFlags, string? Error = null, string? FailureType = null)
{
    internal bool Unlocked => (RuntimeFlags & 1) != 0;
    internal bool Completed => (RuntimeFlags & 2) != 0;
    internal bool EverCompleted => (RuntimeFlags & 6) != 0;
}

internal enum FalloutChallengePrefix
{
    ProgressCommitted, RewardEntered, RewardReturned, CompletionStatisticEntered,
    CompletionStatisticReturned, NoticeTestEntered, NoticeSkipped, NoticeQueued,
    InterfaceCueEntered, InterfaceCueReturned, FlagsCommitted, Complete,
}

internal sealed record FalloutChallengeAttempt(FalloutFormKey Form, string SourceSha256,
    int Before, int After, uint FlagsBefore, FalloutChallengePrefix Prefix,
    FalloutChallengeRewardSnapshot? Reward = null, long? CompletionStatisticOrdinal = null,
    long? NoticeOrdinal = null, string? FailureType = null, string? Error = null,
    long? InterfaceCueOrdinal = null);
internal sealed record FalloutChallengeDispatch(long Ordinal, long ThroughOrdinal, uint Event, int Amount,
    ushort Value1, ushort Value2, ushort Value3, FalloutFormKey? Primary, FalloutFormKey? Secondary,
    FalloutStatisticMutation? Statistic, FalloutFormKey? ScriptedTarget, long BucketGeneration,
    IReadOnlyList<FalloutChallengeAttempt> Attempts, IReadOnlyList<FalloutChallengeDispatch> Children,
    bool Complete, bool RebuildEntered, string? FailureType, string? Error)
{
    public IReadOnlyList<FalloutChallengeCursorObservation> Traversal { get; init; } = [];
    public long BucketGenerationAfter { get; init; }
}
internal sealed record FalloutChallengeCursorObservation(uint Event, long Node, bool StableHead,
    long ConstructedGeneration, long ObservedGeneration, string Operation, FalloutFormKey? Form, string? Error);
internal sealed record FalloutChallengeBucket(uint Event, IReadOnlyList<FalloutFormKey> Members,
    IReadOnlyList<FalloutFormKey>? Excluded = null);
internal sealed record FalloutChallengesSnapshot(IReadOnlyList<FalloutChallengeSnapshot> Entries,
    int ChallengesCompleted, FalloutChallengeEventSource? Source, string RegistrySha256,
    long BucketGeneration, IReadOnlyList<FalloutChallengeBucket> Buckets,
    long Events, FalloutChallengeDispatch? LastDispatch)
{
    internal void Validate()
    {
        Source?.Validate();
        if (Entries is null || !FalloutAdvancementRuntimeReceipt.Digest(RegistrySha256) ||
            BucketGeneration <= 0 || Buckets is null || Buckets.Count != 14 || Events < 0 ||
            (Events == 0) != (LastDispatch is null) || Source is null && (Events != 0 || ChallengesCompleted != 0) ||
            Entries.Select(value => value.Form).Distinct().Count() != Entries.Count)
            throw new InvalidDataException("Saved challenge registry/bucket/dispatch ownership is incomplete.");
        foreach (var entry in Entries)
            if (entry is null || entry.Form.ObjectId == 0 || string.IsNullOrWhiteSpace(entry.Form.OwnerPlugin) ||
                !FalloutAdvancementRuntimeReceipt.Digest(entry.SourceSha256) ||
                (entry.Error is null) != (entry.FailureType is null) || entry.FailureType is { Length: 0 })
                throw new InvalidDataException("Saved challenge identity or exact failure is invalid.");
        if (Source is null && Entries.Any(entry => entry.Progress != 0 || entry.RuntimeFlags != 0 || entry.Error is not null))
            throw new InvalidDataException("An unadmitted source challenge registry invented runtime progress/flags.");
        var forms = Entries.Select(entry => entry.Form).ToHashSet();
        var bucketed = new HashSet<FalloutFormKey>();
        for (var index = 0; index < Buckets.Count; ++index)
        {
            var bucket = Buckets[index];
            if (bucket is null || bucket.Event != index || bucket.Members is null || bucket.Excluded is null ||
                bucket.Members.Concat(bucket.Excluded).Any(form => !forms.Contains(form) || !bucketed.Add(form)))
                throw new InvalidDataException("Saved source challenge buckets are missing, duplicated or reordered.");
        }
        if (!bucketed.SetEquals(forms)) throw new InvalidDataException("Source challenge active/excluded buckets lost registered members.");
        if (LastDispatch is not { } root) return;
        var nextOrdinal = root.Ordinal;
        ValidateDispatch(root);
        if (root.ThroughOrdinal != Events || nextOrdinal != checked(Events + 1))
            throw new InvalidDataException("Challenge nested dispatch ordinals lost their actual suffix.");

        void ValidateDispatch(FalloutChallengeDispatch dispatch)
        {
            if (dispatch.Ordinal != nextOrdinal++ || dispatch.ThroughOrdinal < dispatch.Ordinal ||
                dispatch.Event is not (11 or 13) || dispatch.Amount == 0 || dispatch.BucketGeneration <= 0 ||
                dispatch.BucketGeneration > BucketGeneration || dispatch.BucketGenerationAfter < dispatch.BucketGeneration ||
                dispatch.BucketGenerationAfter > BucketGeneration || dispatch.Traversal is null || dispatch.Attempts is null || dispatch.Children is null ||
                (dispatch.Error is null) != (dispatch.FailureType is null) ||
                (dispatch.Error is null) != dispatch.Complete || dispatch.Complete && dispatch.RebuildEntered &&
                dispatch.BucketGeneration == BucketGeneration ||
                dispatch.Event == 11 && (dispatch.Statistic is null || dispatch.ScriptedTarget is not null ||
                    dispatch.Primary is not null || dispatch.Secondary is not null || dispatch.Value2 != 0 || dispatch.Value3 != 0 ||
                    dispatch.Statistic.Index != dispatch.Value1 || dispatch.Statistic.Delta != dispatch.Amount ||
                    dispatch.Statistic.SourceSha256 != Source?.StatisticSourceSha256) ||
                dispatch.Event == 13 && (dispatch.Statistic is not null || dispatch.ScriptedTarget is null || dispatch.Amount != 1))
                throw new InvalidDataException("Challenge dispatch lacks its actual source request/prefix.");
            foreach (var read in dispatch.Traversal)
                if (read is null || read.Event != dispatch.Event || read.Node <= 0 ||
                    read.StableHead != (read.ConstructedGeneration == 0) || read.ConstructedGeneration < 0 ||
                    read.ConstructedGeneration > read.ObservedGeneration || read.ObservedGeneration < dispatch.BucketGeneration ||
                    read.ObservedGeneration > dispatch.BucketGenerationAfter ||
                    read.Operation is not ("read" or "next" or "read-refusal" or "next-refusal") ||
                    read.Operation.EndsWith("-refusal", StringComparison.Ordinal) != (read.Error is not null) ||
                    read.Form is { } value && !forms.Contains(value) || read.Error is not null && dispatch.Error is null)
                    throw new InvalidDataException("Challenge traversal lost its genuine node/generation retirement prefix.");
            foreach (var attempt in dispatch.Attempts)
            {
                if (attempt is null || !forms.Contains(attempt.Form) ||
                    Entries.Single(entry => entry.Form == attempt.Form).SourceSha256 != attempt.SourceSha256 ||
                    attempt.After != unchecked(attempt.Before + dispatch.Amount) || !Enum.IsDefined(attempt.Prefix) ||
                    (attempt.Error is null) != (attempt.FailureType is null) ||
                    (attempt.Error is null) != (attempt.Prefix == FalloutChallengePrefix.Complete) ||
                    attempt.CompletionStatisticOrdinal is <= 0 || attempt.NoticeOrdinal is <= 0 || attempt.InterfaceCueOrdinal is <= 0)
                    throw new InvalidDataException("Challenge attempt has no genuine progress/callback prefix.");
                attempt.Reward?.Validate();
            }
            foreach (var child in dispatch.Children) ValidateDispatch(child);
            if (dispatch.ThroughOrdinal != nextOrdinal - 1)
                throw new InvalidDataException("Challenge child dispatch lies outside its owning invocation.");
        }
    }
}
