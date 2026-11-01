using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;

internal static partial class QuestStateQueryContracts
{
    private static void ExplicitNullQuestArguments()
    {
        using var fixture = new Fixture("set result to 0", shared: true);
        var records = fixture.Records;
        var caller = new FalloutFormKey("QuestQueries.esm", 0x101);
        var declaration = records.GetEffective(caller);
        var runtime = new FalloutConditionRuntime(records, fixture.Quests, fixture.World);
        fixture.Quests.EnterStage(caller, 12);
        fixture.Quests.SetVariable(caller, 1, 7);
        fixture.Quests.Complete(caller);
        var before = JsonSerializer.Serialize(fixture.Quests.Capture());
        foreach (var function in new[] { Function.GetQuestRunning, Function.GetStage, Function.GetStageDone,
                     Function.GetQuestVariable, Function.GetQuestCompleted })
        {
            var condition = new FalloutCondition(declaration, 0, 0, (ushort)function, 0, uint.MaxValue, 0, 0);
            Require(fixture.Quests.Evaluate(condition) == 0 && runtime.Evaluate(condition, new(caller)) == 0,
                "Explicit null quest getter did not retain its initialized zero result.");
            var boundNull = new FalloutConditionQuery(condition with { Argument1 = caller.ObjectId }, records.RuntimeFormKey(0));
            Require(runtime.Evaluate(boundNull, new(caller)) == 0,
                "Bound null quest was confused with the raw declaration operand.");
            var bound = new FalloutConditionQuery(condition with
            { Argument2 = function == Function.GetQuestVariable ? 1u : 12u }, caller);
            Require(runtime.Evaluate(bound, new(caller)) == (function == Function.GetStage ? 12 : function == Function.GetQuestVariable ? 7 : 1),
                "Raw zero placeholder overrode a bound non-null quest or changed live query behavior.");
            RefuseNullQuest(() => runtime.Evaluate(condition with { Argument1 = 0x7fffff }, new(caller)));
            RefuseNullQuest(() => runtime.Evaluate(condition with { Argument1 = 0x300 }, new(caller)));
        }
        foreach (var function in new[] { Function.GetObjectiveCompleted, Function.GetObjectiveDisplayed })
            RefuseNullQuest(() => runtime.Evaluate(new FalloutCondition(declaration, 0, 0, (ushort)function, 0, 0, 0, 0), new(caller)));
        var branch = new FalloutCondition(declaration, 0x40, 0, (ushort)Function.GetStageDone, 0, 0, 0, 0);
        Require(!FalloutCondition.AllPass([branch], value => runtime.Evaluate(value, new(caller))),
            "Null stage-done greater-than-zero condition incorrectly selected an idle branch.");
        Require(JsonSerializer.Serialize(fixture.Quests.Capture()) == before,
            "Null or refused quest queries created quest state or changed retained flags/variables.");
        Console.WriteLine("OPENNV_NULL_QUEST_QUERY_CONTRACT_PASS getters=5 explicitNull=true boundNull=true boundNonNull=true missingNonzeroRefused=true wrongTypeRefused=true objectivesUnqualified=true readonly=true");
    }

    private static void RefuseNullQuest(Action query)
    {
        var refused = false;
        try { query(); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidDataException or NotSupportedException) { refused = true; }
        Require(refused, "Missing/nonquest or unqualified null query was silently replaced with zero.");
    }

    private static void OwnedExplicitNullQuestArguments(FalloutPluginStack records)
    {
        var conditions = records.EffectiveRecords("IDLE").SelectMany(FalloutCondition.Read)
            .Where(condition => condition.Argument1 == 0 && FalloutQuestState.ExplicitNullQuestValue(condition.Kind) is not null).ToArray();
        Require(conditions.Any(condition => condition.Kind == Function.GetStageDone),
            "Owned IDLE graph no longer exercises the selected explicit-null stage query.");
        var originals = conditions.Select(condition => condition.Owner).DistinctBy(record => record.FormKey)
            .Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var quests = new FalloutQuestState(records);
        var runtime = new FalloutConditionRuntime(records, quests);
        foreach (var condition in conditions)
            Require(quests.Evaluate(condition) == 0 && runtime.Evaluate(condition, new()) == 0,
                "Actual owned null quest IDLE condition did not evaluate through both shared and direct owners.");
        Require(quests.Capture().Count == 0 && originals.All(value => SHA256.HashData(value.Record.ReadData()).SequenceEqual(value.Hash)),
            "Owned explicit-null evaluation changed quest state or source records.");
        Console.WriteLine($"OPENNV_OWNED_NULL_QUEST_QUERY_PASS idleConditions={conditions.Length} sourceUnchanged=true noQuestCreated=true parity=unverified");
    }
}
