namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeScriptClockOwner(float Remaining, float Elapsed,
    FalloutFormKey? Quest, string SourceOwner, Action RequireCurrent);

internal sealed partial class FalloutQuestScripts
{
    internal FalloutNativeScriptClockOwner NativeDefinitionClock(FalloutFormKey script)
    {
        var rows = NativeDefinitionClockRows(script);
        if (rows.Length == 0)
        {
            if (!_initialization.Definitions.TryGetValue(script, out var definition))
                throw new NotSupportedException("Native Script has no actual source recurrence declaration.");
            // A definition without an assigned quest has no reached recurring
            // campaign instance. Its actual original initialization phase is
            // retained, and no native frame advancement is fabricated.
            var header = _records.GetEffective(script).ReadSubrecords().Single(field => field.Signature == "SCHR").Data;
            var disabled = header.Length == 20 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Span[8..]) == 0;
            if (definition.Quest is not null && !disabled)
                throw new NotSupportedException("Native Script's assigned quest lacks its actual scheduling owner.");
            return new(definition.InitialPhase, 0, definition.Quest, "source-definition-initialization:" + script,
                () =>
                {
                    if (!_initialization.Definitions.TryGetValue(script, out var current) || !ReferenceEquals(current, definition) ||
                        NativeDefinitionClockRows(script).Length != 0)
                        throw new InvalidOperationException("Native orphan definition initialization changed.");
                });
        }
        var clock = rows[0].Clock!; var quest = rows[0].Quest;
        if (rows.Any(row => !ReferenceEquals(row.Clock, clock) || row.Quest != quest))
            throw new InvalidDataException("Native shared Script has competing actual clock/quest owners.");
        return new(clock.Remaining, clock.Elapsed, quest, "actual-shared-quest-clock:" + script,
            () =>
            {
                var current = NativeDefinitionClockRows(script);
                if (current.Length != rows.Length || current.Any(row => !ReferenceEquals(row.Clock, clock) || row.Quest != quest))
                    throw new InvalidOperationException("Native Script clock changed its actual campaign binding.");
            });
    }

    private (FalloutQuestScriptClock? Clock, FalloutFormKey? Quest)[] NativeDefinitionClockRows(FalloutFormKey script)
        => _schedule.Select(row => row switch
        {
            CompiledInstance compiled when FalloutFormKeyComparer.Instance.Equals(compiled.Program.Source.FormKey, script)
                => (Clock: compiled.Clock, Quest: compiled.Definition.Quest),
            Instance source when FalloutFormKeyComparer.Instance.Equals(source.Script.FormKey, script)
                => (Clock: source.Clock, Quest: _initialization.Definitions[script].Quest),
            _ => (Clock: (FalloutQuestScriptClock?)null, Quest: (FalloutFormKey?)null),
        }).Where(row => row.Clock is not null).ToArray();
}
