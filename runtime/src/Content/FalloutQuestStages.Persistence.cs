using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// A closed execution receipt, never a resumable script cursor. The owning
// quest state contains the consumed effects; a failed Enter preserves its
// original refusal before source conditions, random draws or results rerun.
internal sealed record FalloutQuestStageResultSnapshot(FalloutFormKey Quest, short Stage,
    string WinningPlugin, string QuestSha256, int Steps, bool Completed, string? Error);
internal sealed record FalloutQuestStageDriverFailure(FalloutFormKey Quest, short Stage, string Error);

internal sealed partial class FalloutQuestStages
{
    private Exception? _closedFailureException;
    private FalloutQuestStageDriverFailure? _closedFailure;

    private void RetainClosedFailure(FalloutFormKey quest, short stage, Exception error)
    {
        _closedFailureException = error;
        _closedFailure = new(quest, stage, error.Message);
    }

    internal FalloutQuestStageDriverFailure? ClosedFailureFor(Exception error) =>
        ReferenceEquals(_closedFailureException, error) ? _closedFailure : null;

    internal static void ValidateDriverFailure(IReadOnlyList<FalloutQuestStageResultSnapshot>? results,
        FalloutQuestStageDriverFailure? failure)
    {
        if (failure is null) return;
        if (results is null || failure.Stage < 0 || string.IsNullOrWhiteSpace(failure.Error) ||
            results.Count(item => item is not null && item.Quest == failure.Quest && item.Stage == failure.Stage &&
                !item.Completed && item.Error == failure.Error) != 1)
            throw new InvalidDataException("Saved native stage failure has no matching closed source result.");
    }

    internal IReadOnlyList<FalloutQuestStageResultSnapshot> CaptureResults()
    {
        if (HasPendingResults)
            throw new NotSupportedException("Saving active quest-stage results requires their exact execution continuation.");
        if (_errors.Keys.Any(key => !_progress.ContainsKey(key)))
            throw new NotSupportedException("A failed quest-stage result has no consumed execution receipt.");
        var result = _progress.OrderBy(pair => records.RuntimeFormId(pair.Key.Quest)).ThenBy(pair => pair.Key.Stage)
            .Select(pair =>
            {
                var record = records.GetEffective(pair.Key.Quest);
                return new FalloutQuestStageResultSnapshot(pair.Key.Quest, pair.Key.Stage, record.Plugin.Name,
                    QuestResultHash(record), pair.Value.Steps, pair.Value.Completed,
                    _errors.GetValueOrDefault(pair.Key));
            }).ToArray();
        ValidateResults(result);
        return result;
    }

    internal void RestoreResults(IReadOnlyList<FalloutQuestStageResultSnapshot> saved)
    {
        if (_depth != 0 || _pending.Count != 0 || _progress.Count != 0 || _errors.Count != 0)
            throw new InvalidOperationException("Quest-stage result restoration requires an unused owner.");
        ValidateResults(saved);
        // Validation resolves every winning source and entered stage first.
        // It executes no result program and invokes no condition delegate.
        foreach (var item in saved)
        {
            _progress.Add((item.Quest, item.Stage), (item.Steps, item.Completed));
            if (item.Error is { } error) _errors.Add((item.Quest, item.Stage), error);
        }
    }

    private void ValidateResults(IReadOnlyList<FalloutQuestStageResultSnapshot> saved)
    {
        ValidateSnapshotShape(saved);
        var entered = quests.Capture().ToDictionary(quest => quest.Quest, quest => quest.EnteredStages.ToHashSet());
        foreach (var item in saved)
        {
            if (!entered.TryGetValue(item.Quest, out var stages) || !stages.Contains(item.Stage))
                throw new InvalidDataException("Saved quest-stage result is not an execution of an entered stage.");
            var record = records.GetEffective(item.Quest);
            if (record.Signature != "QUST" || record.IsDeleted || !string.Equals(record.Plugin.Name, item.WinningPlugin, StringComparison.OrdinalIgnoreCase) ||
                item.QuestSha256 != QuestResultHash(record))
                throw new InvalidDataException("Saved quest-stage result differs from its winning quest source.");
            var declarations = record.ReadSubrecords().Where(field => field.Signature == "INDX").ToArray();
            if (declarations.Any(field => field.Data.Length != 2) ||
                declarations.Count(field => BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == item.Stage) != 1)
                throw new InvalidDataException("Saved quest-stage result has no unique authored source stage.");
        }
    }

    internal static void ValidateSnapshotShape(IReadOnlyList<FalloutQuestStageResultSnapshot> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var seen = new HashSet<(FalloutFormKey Quest, short Stage)>();
        foreach (var item in saved)
            if (item is null || string.IsNullOrWhiteSpace(item.Quest.OwnerPlugin) || item.Quest.ObjectId == 0 ||
                string.IsNullOrWhiteSpace(item.WinningPlugin) || item.QuestSha256 is not { Length: 64 } ||
                item.QuestSha256.Any(character => !char.IsAsciiHexDigit(character)) ||
                item.Stage < 0 || item.Steps < 0 || !seen.Add((item.Quest, item.Stage)) ||
                item.Completed != (item.Error is null) || item.Error is { } error && string.IsNullOrWhiteSpace(error))
                throw new InvalidDataException("Saved quest-stage result is not a unique closed execution receipt.");
    }

    private static string QuestResultHash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
