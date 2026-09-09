using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Stage log entries retain their own condition and compiled-reference scopes.
// Entered state is published before a result script; nested stages and a failed
// result retain the reached prefix rather than replaying rewards on retry.
internal sealed class FalloutQuestStages(FalloutPluginStack records, FalloutQuestState quests,
    Func<FalloutPluginRecord, IReadOnlyList<FalloutPluginSubrecord>, string, IEnumerable<bool>> execute,
    Func<FalloutCondition, float> evaluate, Func<bool>? canContinue = null)
{
    private readonly Dictionary<(FalloutFormKey, short), string> _errors = [];
    private readonly List<(FalloutFormKey Quest, short Stage, IEnumerator<bool> Steps)> _pending = [];
    private int _depth;
    internal object Errors => _errors.Select(value => new { quest = value.Key.Item1.ToString(), stage = value.Key.Item2, error = value.Value }).ToArray();

    internal void Enter(FalloutFormKey key, short stage)
    {
        if (stage < 0) throw new ArgumentOutOfRangeException(nameof(stage));
        if (_errors.TryGetValue((key, stage), out var failure)) throw new NotSupportedException(failure);
        var quest = records.GetEffective(key);
        if (quest.Signature != "QUST") throw new InvalidDataException("SetStage target is not a quest.");
        var fields = quest.ReadSubrecords().ToArray();
        var header = fields.Single(field => field.Signature == "DATA").Data;
        if (header.Length is not (2 or 8)) throw new InvalidDataException("Quest header extent is invalid.");
        if (quests.StageDone(key, stage) && (header.Span[0] & 8) == 0) return;
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" &&
            field.Data.Length == 2 && BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == stage);
        if (begin < 0) throw new NotSupportedException($"Quest {key} has no authored stage {stage}.");
        quests.SetRunning(key, true);
        quests.EnterStage(key, stage);
        Resume((key, stage, EntrySteps().GetEnumerator()));

        IEnumerable<bool> EntrySteps()
        {
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
            for (var index = begin + 1; index < end;)
            {
                if (fields[index].Signature != "QSDT") throw new InvalidDataException("Quest stage entry has no flag header.");
                var next = index + 1;
                while (next < end && fields[next].Signature != "QSDT") ++next;
                var entry = fields[index..next];
                var flags = entry[0].Data;
                if (flags.Length != 1 || (flags.Span[0] & ~3) != 0) throw new NotSupportedException("Quest stage flags are unbound.");
                var conditions = entry.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(quest, field.Data.Span)).ToArray();
                if (FalloutCondition.AllPass(conditions, evaluate))
                {
                    if ((flags.Span[0] & 2) != 0) throw new NotSupportedException("Failed quest presentation and state are unbound.");
                    if ((flags.Span[0] & 1) != 0) quests.Complete(key);
                    var sources = entry.Where(field => field.Signature == "SCTX").ToArray();
                    if (sources.Length > 1) throw new InvalidDataException("Quest stage entry has ambiguous source.");
                    if (sources.Length == 1)
                        foreach (var step in execute(quest, entry, FalloutDialogueTopic.ScriptText(sources[0].Data.Span))) yield return step;
                    else if (entry.Any(field => field.Signature == "SCDA" && field.Data.Length > 0))
                        throw new NotSupportedException("Quest stage compiled program has no source execution owner.");
                    foreach (var field in entry.Where(field => field.Signature == "NAM0"))
                    {
                        if (field.Data.Length != 4) throw new InvalidDataException("Quest stage next-quest extent is invalid.");
                        if (quest.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) is { } nextQuest)
                            quests.SetNextQuest(key, nextQuest);
                    }
                }
                index = next;
            }
        }
    }

    internal void Continue()
    {
        var pending = _pending.ToArray(); _pending.Clear();
        foreach (var execution in pending) Resume(execution);
    }

    private void Resume((FalloutFormKey Quest, short Stage, IEnumerator<bool> Steps) execution)
    {
        if (++_depth > 64) { --_depth; throw new InvalidDataException("Recursive quest stage limit exceeded."); }
        try
        {
            while (canContinue?.Invoke() ?? true)
            {
                if (execution.Steps.MoveNext()) continue;
                execution.Steps.Dispose();
                return;
            }
            _pending.Add(execution);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { _errors[(execution.Quest, execution.Stage)] = error.Message; execution.Steps.Dispose(); throw; }
        finally { --_depth; }
    }
}
