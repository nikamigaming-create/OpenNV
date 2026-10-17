using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutChallengeDefinition(FalloutPluginRecord Record, uint Type, int Threshold,
    uint Flags, uint Interval, ushort Value1, FalloutFormKey? Script, string Name, string Description, string? Icon)
{
    internal bool StartDisabled => (Flags & 1) != 0;
    internal bool Recurring => (Flags & 2) != 0;
    internal string Sha256 => Convert.ToHexString(SHA256.HashData(Record.ReadData())).ToLowerInvariant();

    internal static FalloutChallengeDefinition Read(FalloutPluginRecord record)
    {
        if (record.Signature != "CHAL") throw new InvalidDataException("Challenge target is not CHAL.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 24) throw new InvalidDataException("Challenge DATA must contain 24 bytes.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var threshold = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        if (type > 13 || threshold <= 0 || (flags & ~7u) != 0)
            throw new NotSupportedException("Challenge type, threshold or flags need another source contract.");
        string Text(string name)
        {
            var field = fields.SingleOrDefault(field => field.Signature == name).Data;
            return field.IsEmpty ? "" : FalloutDialogueTopic.Text(field.Span);
        }
        var script = fields.SingleOrDefault(field => field.Signature == "SCRI").Data;
        if (script.Length is not (0 or 4)) throw new InvalidDataException("Challenge script link has an invalid extent.");
        return new(record, type, threshold, flags, BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[16..]), script.IsEmpty ? null :
                record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(script.Span)),
            Text("FULL"), Text("DESC"), fields.Any(field => field.Signature == "ICON") ? Text("ICON") : null);
    }
}

internal sealed record FalloutChallengeSnapshot(FalloutFormKey Form, string SourceSha256, bool Unlocked,
    int Progress, bool Completed, bool EverCompleted, string? Error = null);
internal sealed record FalloutChallengesSnapshot(IReadOnlyList<FalloutChallengeSnapshot> Entries, int ChallengesCompleted)
{
    internal void Validate()
    {
        if (Entries is null || ChallengesCompleted < 0 || Entries.Any(value => value is null || value.Form.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(value.Form.OwnerPlugin) || value.SourceSha256 is null || value.SourceSha256.Length != 64 ||
            !value.SourceSha256.All(Uri.IsHexDigit) || value.Progress < 0 || value.Completed && !value.EverCompleted ||
            value.Error is { Length: 0 }) || Entries.Select(value => value.Form).Distinct().Count() != Entries.Count)
            throw new InvalidDataException("Saved challenge state is invalid or duplicated.");
    }
}

// CHAL progress is player state, shared by every script context. Completion
// faults retain the incremented prefix and cannot silently retry reward scripts.
internal sealed class FalloutChallenges(FalloutPluginStack records, FalloutHudNotifications notifications)
{
    private readonly Dictionary<FalloutFormKey, FalloutChallengeDefinition> _definitions = [];
    private readonly Dictionary<FalloutFormKey, FalloutChallengeSnapshot> _state = [];
    private FalloutChallengeDefinition[]? _completionStatistics;
    internal int ChallengesCompleted { get; private set; }
    private FalloutChallengeDefinition Definition(FalloutFormKey form)
    {
        if (!_definitions.TryGetValue(form, out var result))
            _definitions.Add(form, result = FalloutChallengeDefinition.Read(records.GetEffective(form)));
        return result;
    }
    internal FalloutChallengeSnapshot State(FalloutFormKey form)
    {
        var definition = Definition(form);
        return _state.GetValueOrDefault(form) ?? new(form, definition.Sha256, false, 0, false, false);
    }
    internal bool Locked(FalloutFormKey form) => Definition(form).StartDisabled && !State(form).Unlocked;
    internal void Unlock(FalloutFormKey form) => _state[form] = State(form) with { Unlocked = true };
    internal void IncrementScripted(FalloutFormKey form)
    {
        var definition = Definition(form);
        // The native command searches only the scripted challenge bucket.
        if (definition.Type == 13) Increment(definition);
    }
    private void Increment(FalloutChallengeDefinition definition)
    {
        var form = definition.Record.FormKey;
        var previous = State(form);
        if (previous.Error is not null) throw new NotSupportedException($"Challenge {form} retains its completion failure: {previous.Error}");
        if (Locked(form) || previous.Completed && !definition.Recurring) return;
        var next = previous with { Progress = checked(previous.Progress + 1) };
        _state[form] = next;
        try
        {
            if (next.Progress >= definition.Threshold)
            {
                if (definition.Script is not null)
                    throw new NotSupportedException($"Challenge {form} completion script requires an immediate player script owner.");
                // Completing a statistic-27 challenge must not recursively
                // count itself as another completed challenge.
                if (definition.Type != 11 || definition.Value1 != 27)
                {
                    ChallengesCompleted = checked(ChallengesCompleted + 1);
                    _completionStatistics ??= records.EffectiveRecordsInRegistrationOrder("CHAL")
                        .Select(record => Definition(record.FormKey)).Where(value => value.Type == 11 && value.Value1 == 27).ToArray();
                    foreach (var statistic in _completionStatistics) Increment(statistic);
                }
                Publish(definition, definition.Threshold, completed: true);
                next = next with
                {
                    Progress = definition.Recurring ? next.Progress - definition.Threshold : next.Progress,
                    Completed = !definition.Recurring,
                    EverCompleted = true
                };
                _state[form] = next;
            }
            else if (next.Progress % (definition.Interval == 0 ? 100u : definition.Interval) == 0)
                Publish(definition, next.Progress, completed: false);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            _state[form] = _state[form] with { Error = error.Message };
            throw;
        }
    }
    private void Publish(FalloutChallengeDefinition definition, int progress, bool completed)
    {
        if (definition.Name.Length == 0 || definition.Description.Length == 0) return;
        notifications.Publish([new(completed ? FalloutHudEventKind.ChallengeCompleted : FalloutHudEventKind.ChallengeProgress,
            definition.Record.FormKey, progress)]);
    }
    internal FalloutChallengesSnapshot Capture() => new(_state.Values.OrderBy(value => records.RuntimeFormId(value.Form)).ToArray(), ChallengesCompleted);
    internal void Restore(FalloutChallengesSnapshot? snapshot)
    {
        if (_state.Count != 0 || ChallengesCompleted != 0) throw new InvalidOperationException("Challenges restore requires a fresh owner.");
        if (snapshot is null) return;
        snapshot.Validate();
        foreach (var entry in snapshot.Entries)
        {
            var definition = Definition(entry.Form);
            if (!definition.Sha256.Equals(entry.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                entry.Completed && definition.Recurring || entry.Completed && entry.Progress < definition.Threshold ||
                entry.Error is null && !entry.Completed && entry.Progress >= definition.Threshold ||
                definition.StartDisabled && !entry.Unlocked && (entry.Progress != 0 || entry.EverCompleted))
                throw new InvalidDataException("Saved challenge progress differs from its winning source definition.");
        }
        foreach (var entry in snapshot.Entries) _state.Add(entry.Form, entry);
        ChallengesCompleted = snapshot.ChallengesCompleted;
    }
}
