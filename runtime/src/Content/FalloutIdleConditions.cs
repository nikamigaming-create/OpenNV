using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdleConditionDecision(FalloutFormKey Candidate, bool Eligible,
    FalloutFormKey? StoppedAt, int ConditionsEvaluated);

/// <summary>Package admission evaluates the candidate before its source ancestors.</summary>
internal sealed class FalloutIdleConditions(FalloutPluginStack stack)
{
    private readonly Dictionary<FalloutFormKey, Entry> _entries = [];
    internal FalloutIdleConditionDecision? LastDecision { get; private set; }

    internal bool AllPass(FalloutFormKey idle, Func<FalloutCondition, float> evaluate)
    {
        LastDecision = null;
        var visited = new HashSet<FalloutFormKey>();
        var evaluated = 0;
        FalloutFormKey? current = idle;
        while (current is { } form)
        {
            if (!visited.Add(form)) throw new InvalidDataException("Package IDLE parent cycle.");
            var entry = Read(form);
            if (!FalloutCondition.AllPass(entry.Conditions, condition => { evaluated++; return evaluate(condition); }))
            {
                LastDecision = new(idle, false, form, evaluated);
                return false;
            }
            current = entry.Parent;
        }
        LastDecision = new(idle, true, null, evaluated);
        return true;
    }

    private Entry Read(FalloutFormKey form)
    {
        if (_entries.TryGetValue(form, out var entry)) return entry;
        var record = stack.GetEffective(form);
        if (record.Signature != "IDLE") throw new InvalidDataException("Package IDLE parent is not IDLE.");
        var related = record.ReadSubrecords().Where(field => field.Signature == "ANAM").ToArray();
        if (related.Length != 1 || related[0].Data.Length != 8)
            throw new InvalidDataException("Package IDLE has an invalid parent field.");
        entry = new(FalloutCondition.Read(record), record.Plugin.AdjustOptionalFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(related[0].Data.Span)));
        _entries.Add(form, entry);
        return entry;
    }

    private sealed record Entry(IReadOnlyList<FalloutCondition> Conditions, FalloutFormKey? Parent);
}
