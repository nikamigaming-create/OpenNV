using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ConditionGlobalContracts
{
    // Decoder/owner corpus proof only. It never substitutes a constant for an
    // original function or claims that any campaign predicate was executed.
    internal static void RunOwned(string dataDirectory, IReadOnlyList<string> plugins)
    {
        using var records = FalloutPluginStack.Load(dataDirectory, plugins);
        RunOwned(records);
    }

    internal static void RunOwned(FalloutPluginStack records)
    {
        var globals = FalloutGlobalState.Read(records);
        var conditionCount = 0;
        var globalCount = 0;
        var unownedFlags = 0;
        var runOn = 0;
        var functions = new SortedDictionary<ushort, int>();
        FalloutCondition? mutationRead = null;
        foreach (var record in records.EffectiveRecords())
            foreach (var field in record.ReadSubrecords().Where(field => field.Signature == "CTDA"))
            {
                var condition = FalloutCondition.Read(record, field.Data.Span);
                conditionCount++;
                functions[condition.Function] = functions.GetValueOrDefault(condition.Function) + 1;
                if ((condition.Flags & FalloutCondition.UnownedFlagMask) != 0) unownedFlags++;
                if (condition.RunOn != 0) runOn++;
                if (!condition.UsesComparisonGlobal) continue;
                var operand = condition.GlobalComparison ?? throw new InvalidDataException("Owned CTDA lost its raw GLOB operand.");
                var form = record.Plugin.AdjustOptionalFormId(operand.RawFormId) ??
                    throw new InvalidDataException("Owned CTDA comparison has a null global FormID.");
                var winner = records.GetEffective(form);
                Require(winner.Signature == "GLOB" && globals.ReadConditionComparison(condition) == globals.Get(form),
                    "Owned CTDA normalized to another type or cached its RHS.");
                globalCount++;
                mutationRead ??= condition;
            }
        if (mutationRead is { GlobalComparison: { } raw } retained)
        {
            var form = retained.Owner.Plugin.AdjustFormId(raw.RawFormId);
            var before = globals.Get(form);
            var changed = before == 0 ? 1 : 0;
            globals.Set(form, changed);
            Require(globals.ReadConditionComparison(retained) == changed, "Owned RHS read missed a current C# global store.");
            globals.Set(form, before);
            Require(globals.ReadConditionComparison(retained) == before, "Owned RHS failed to observe the next actual store.");
        }
        Console.WriteLine($"OPENNV_CONDITION_GLOBAL_OWNED_PASS plugins={records.Plugins.Count} conditions={conditionCount} " +
            $"comparisonGlobals={globalCount} distinctFunctions={functions.Count} otherUnownedFlags={unownedFlags} runOn={runOn} " +
            $"currentRhsMutation={(mutationRead is not null)} functionExecution=unrun gameplay=unaccepted");
    }
}
