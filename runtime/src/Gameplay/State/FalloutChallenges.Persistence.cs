using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutChallenges
{
    internal void RequireContinuation(FalloutChallengesSnapshot? snapshot)
    {
        if (snapshot is null || !Restored || System.Text.Json.JsonSerializer.Serialize(snapshot) !=
            System.Text.Json.JsonSerializer.Serialize(Capture()))
            throw new InvalidDataException("Quest and campaign challenge owners do not share the exact current cold continuation.");
    }
    internal FalloutChallengesSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_executing.Count != 0) throw new InvalidOperationException("A source challenge completion is still executing its attempted suffix.");
        var saved = new FalloutChallengesSnapshot(_registrationOrder.Select(form => _state[form]).ToArray(), ChallengesCompleted, Source,
            _registrySha256, _bucketGeneration, _buckets.Capture(), _events,
            _last?.Capture() ?? _restoredLast);
        saved.Validate(); return saved;
    }

    internal void Restore(FalloutChallengesSnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Restored || _events != 0 || _executing.Count != 0 || _state.Values.Any(value =>
            value.Progress != 0 || value.RuntimeFlags != 0 || value.Error is not null))
            throw new InvalidOperationException("Challenge restore requires its actual fresh campaign constructor.");
        if (snapshot is null) throw new InvalidDataException("Current campaign has no challenge registry/dispatch continuation.");
        snapshot.Validate();
        if (snapshot.Source?.Identity != Source?.Identity || snapshot.RegistrySha256 != _registrySha256 ||
            !snapshot.Entries.Select(entry => entry.Form).SequenceEqual(_registrationOrder))
            throw new InvalidDataException("Challenge continuation differs from its selected source/first registration order.");
        var entries = snapshot.Entries.ToDictionary(entry => entry.Form);
        foreach (var entry in snapshot.Entries)
        {
            var definition = Definition(entry.Form);
            if (entry.SourceSha256 != definition.Sha256)
                throw new InvalidDataException("Challenge progress/flag continuation differs from its winning definition.");
        }
        foreach (var bucket in snapshot.Buckets)
        {
            if (bucket.Members.Concat(bucket.Excluded!).Any(form => _definitions[form].Type != bucket.Event))
                throw new InvalidDataException("Saved source bucket contains another event family's registered challenge.");
            foreach (var form in bucket.Excluded!)
                if (!entries[form].Completed && (!_definitions[form].StartDisabled || entries[form].Unlocked))
                    throw new InvalidDataException("Saved excluded bucket lost an enabled living source member.");
            foreach (var form in bucket.Members)
                if (_definitions[form].StartDisabled && !entries[form].Unlocked ||
                    entries[form].Completed && !entries[form].Unlocked && snapshot.LastDispatch?.Error is null)
                    throw new InvalidDataException("Saved active bucket invented an unenabled or fully retired source member.");
            // Unlock prepends a formerly excluded member; a nested rebuild can
            // replace the head during traversal. The exact current mutable
            // order is saved state, not a sorted reconstruction of source rows.
        }
        RequireRewardSources(snapshot.LastDispatch);
        RequireStatisticSources(snapshot.LastDispatch);
        RequireInterfaceSoundSources(snapshot.LastDispatch);
        if (snapshot.ChallengesCompleted != ChallengesCompleted)
            throw new InvalidDataException("Challenge completed count differs from the actual restored statistic counter.");
        foreach (var entry in snapshot.Entries) _state[entry.Form] = entry;
        _buckets.Restore(snapshot.Buckets, snapshot.BucketGeneration);
        _bucketGeneration = snapshot.BucketGeneration; _events = snapshot.Events;
        _restoredLast = snapshot.LastDispatch; Restored = true;
        // A closed callback prefix is evidence only. No script, counter, notice,
        // cue or bucket callback is replayed when the campaign is reconstructed.
    }

    private void RequireRewardSources(FalloutChallengeDispatch? dispatch)
    {
        if (dispatch is null) return;
        foreach (var attempt in dispatch.Attempts)
        {
            var definition = Definition(attempt.Form);
            var completed = attempt.After >= definition.Threshold;
            var scriptReturned = attempt.Prefix >= FalloutChallengePrefix.RewardReturned;
            if (!completed && (attempt.Reward is not null || attempt.CompletionStatisticOrdinal is not null) ||
                completed && definition.Script is not null && scriptReturned && attempt.Reward is null ||
                attempt.CompletionStatisticOrdinal is not null && attempt.Prefix < FalloutChallengePrefix.CompletionStatisticEntered ||
                completed && Source is { } source &&
                    (definition.Type != source.StatisticEvent || definition.Value1 != source.CompletionStatistic) &&
                    attempt.Prefix >= FalloutChallengePrefix.CompletionStatisticReturned && attempt.CompletionStatisticOrdinal is null ||
                attempt.NoticeOrdinal is not null && (attempt.Prefix < FalloutChallengePrefix.NoticeQueued ||
                    Source?.ShowNotices != true || definition.Name.Length == 0 || definition.Description.Length == 0) ||
                completed && Source?.ShowNotices == true && definition.Name.Length != 0 && definition.Description.Length != 0 &&
                    attempt.Prefix >= FalloutChallengePrefix.InterfaceCueReturned && attempt.InterfaceCueOrdinal is null)
                throw new InvalidDataException("Saved challenge attempt invented an unreached or unowned completion suffix.");
            if (attempt.Reward is { } reward)
            {
                if (definition.Script != reward.Program || reward.Challenge != attempt.Form || reward.DispatchOrdinal != dispatch.Ordinal)
                    throw new InvalidDataException("Saved challenge reward is not its actual completion script.");
                reward.RequireSource(_records);
                foreach (var row in reward.Events)
                    if (row.GameMode?.Predicate is { } predicate && (predicate.EngineSha256 != Source?.EngineSha256 ||
                        Source is null || predicate.ProducerSha256 != FalloutImmediateScriptSource.Read(Source).Identity))
                        throw new InvalidDataException("Cold immediate filter read another selected Main scalar producer.");
                if (scriptReturned && reward.Disposition is not ("completed" or "authored-empty"))
                    throw new InvalidDataException("Challenge completion passed an unretired source reward.");
            }
        }
        foreach (var child in dispatch.Children) RequireRewardSources(child);
    }

    private void RequireStatisticSources(FalloutChallengeDispatch? dispatch)
    {
        if (dispatch is null || Source is null) return;
        var statistics = _statistics ?? throw new InvalidDataException("Saved challenge dispatch lacks its actual restored counter owner.");
        var operations = new Dictionary<long, FalloutStatisticOperation>();
        Add(statistics.LastOperation);
        Validate(dispatch);
        void Add(FalloutStatisticOperation? row)
        {
            if (row is null) return;
            operations.Add(row.Mutation.Ordinal, row);
            foreach (var child in row.Children ?? []) Add(child);
        }
        void Validate(FalloutChallengeDispatch row)
        {
            if (row.Statistic is { } mutation && operations.TryGetValue(mutation.Ordinal, out var actual) &&
                (actual.Mutation != mutation || actual.Challenge is { } returned &&
                    (returned.CompletedEventOrdinal != row.Ordinal || !row.Complete || returned.ProducerSha256 != Source.Identity)))
                throw new InvalidDataException("Cold CHAL11 dispatch disagrees with the actual last statistic transaction.");
            foreach (var attempt in row.Attempts)
                if (attempt.CompletionStatisticOrdinal is { } ordinal && operations.TryGetValue(ordinal, out var entered) &&
                    (entered.Mutation.Index != Source.CompletionStatistic || entered.Mutation.Delta != 1 ||
                        entered.Mutation.Origin != "source-challenge-completion" ||
                        attempt.Prefix >= FalloutChallengePrefix.CompletionStatisticReturned && entered.Error is not null))
                    throw new InvalidDataException("Cold challenge completion invented or discarded its actual counter call prefix.");
            foreach (var child in row.Children) Validate(child);
        }
    }
}
