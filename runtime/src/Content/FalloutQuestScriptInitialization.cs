using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestScriptDefinition(FalloutFormKey Script, FalloutFormKey? Quest,
    long InitializationOrdinal, float InitialPhase, float ProcessingDelay);

// All source declarations participate, including empty embedded scripts and
// unsupported/inactive standalone scripts. Runtime support never selects the
// initialization denominator, and reference-process clocks never populate it.
internal sealed class FalloutQuestScriptInitialization
{
    internal IReadOnlyList<FalloutPluginRecord> QuestOrder { get; }
    internal IReadOnlyDictionary<FalloutFormKey, FalloutQuestScriptDefinition> Definitions { get; }
    internal long EmbeddedQuestScripts { get; }
    internal long Initializations { get; }
    internal float DefaultDelay { get; }

    internal FalloutQuestScriptInitialization(FalloutPluginStack records, float defaultDelay)
    {
        if (!float.IsFinite(defaultDelay)) throw new InvalidDataException("Default quest script delay is invalid.");
        DefaultDelay = defaultDelay;
        QuestOrder = records.EffectiveRecordsInRegistrationOrder("QUST").Reverse().ToArray();
        var parents = new Dictionary<FalloutFormKey, FalloutPluginRecord>();
        foreach (var quest in QuestOrder)
        {
            var references = quest.ReadSubrecords().Where(field => field.Signature == "SCRI").ToArray();
            if (references.Length == 0) continue;
            if (references.Length != 1 || references[0].Data.Length != sizeof(uint))
                throw new InvalidDataException("Quest script binding is ambiguous or malformed.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(references[0].Data.Span);
            if (id == 0) continue;
            var script = records.GetEffective(quest.Plugin.AdjustFormId(id));
            if (script.Signature != "SCPT") throw new InvalidDataException("Quest script is not SCPT.");
            // Each quest links its event list; the last link publishes the
            // shared definition's associated quest, including its delay.
            parents[script.FormKey] = quest;
        }

        foreach (var record in records.EffectiveRecords())
            if (record.Signature != "SCPT")
                foreach (var header in record.ReadSubrecords().Where(field => field.Signature == "SCHR"))
                    if (IsQuestScript(header.Data.Span)) EmbeddedQuestScripts = checked(EmbeddedQuestScripts + 1);

        var ordinal = defaultDelay > 0 ? EmbeddedQuestScripts : 0;
        var definitions = new Dictionary<FalloutFormKey, FalloutQuestScriptDefinition>();
        foreach (var script in records.EffectiveRecordsInRegistrationOrder("SCPT").Reverse())
        {
            var headers = script.ReadSubrecords().Where(field => field.Signature == "SCHR").ToArray();
            if (headers.Length != 1) throw new NotSupportedException("Standalone script header ownership is absent or ambiguous.");
            if (!IsQuestScript(headers[0].Data.Span)) continue;
            var parent = parents.GetValueOrDefault(script.FormKey);
            var delay = parent is null ? 0 : ProcessingDelay(parent);
            var phase = defaultDelay <= 0 || delay > 0 ? 0 : Phase(defaultDelay, ordinal);
            definitions.Add(script.FormKey, new(script.FormKey, parent?.FormKey, ordinal, phase, delay));
            if (defaultDelay > 0) ordinal = checked(ordinal + 1);
        }
        Definitions = definitions;
        Initializations = ordinal;
    }

    internal static bool IsQuestScript(ReadOnlySpan<byte> header)
    {
        if (header.Length != 20) throw new NotSupportedException("Script SCHR layout is unbound.");
        return header[16] != 0;
    }

    internal static float ProcessingDelay(FalloutPluginRecord quest)
    {
        var data = quest.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        var delay = data.Length switch
        {
            2 => 0,
            8 => BinaryPrimitives.ReadSingleLittleEndian(data.Span[4..]),
            _ => throw new NotSupportedException("Quest DATA layout is unbound."),
        };
        if (!float.IsFinite(delay)) throw new InvalidDataException("Quest delay is invalid.");
        return delay;
    }

    internal static float Phase(float defaultDelay, long ordinal)
    {
        if (!float.IsFinite(defaultDelay) || defaultDelay < 0 || ordinal < 0)
            throw new InvalidDataException("Script initialization phase input is invalid.");
        var index = ordinal & 0xff;
        if (index == 0) return defaultDelay;
        var phase = 0f;
        var fraction = defaultDelay;
        for (var bit = 0; bit < 8; bit++)
        {
            fraction = (float)((double)fraction / 2);
            if ((index & (1 << bit)) != 0) phase = (float)((double)phase + fraction);
        }
        return phase;
    }
}
