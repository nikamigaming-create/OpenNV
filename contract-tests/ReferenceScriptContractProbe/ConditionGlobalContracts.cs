using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ConditionGlobalContracts
{
    internal static void Run()
    {
        using var fixture = new Fixture();
        var records = fixture.Records;
        var globals = FalloutGlobalState.Read(records);
        var owner = records.GetEffective(Quest);
        var condition = GlobalCondition(owner);
        var evaluated = 0;
        float Evaluate(FalloutCondition value)
        {
            evaluated++;
            if (value.Function != GlobalValueFunction)
                throw new NotSupportedException("Authored condition function is unowned.");
            return globals.Get(value.FormArgument1);
        }
        bool Pass(FalloutCondition value) => FalloutCondition.AllPass([value], Evaluate, globals: globals);

        Require(condition.Comparison == 0 && condition.GlobalComparison?.RawFormId == ComparisonRaw &&
            condition.FormArgument1 == FunctionGlobal && records.GetEffective(ComparisonGlobal).Plugin.Name == PatchName &&
            globals.Get(ComparisonGlobal) == 5 && globals.Get(FunctionGlobal) == 7,
            "CTDA lost its typed raw operand, owning master order or winning override.");
        Require(Pass(condition), "Independent function result did not compare against the winning GLOB.");
        globals.Set(ComparisonGlobal, 8);
        Require(!Pass(condition), "CTDA cached the comparison GLOB's initial/previous value.");
        globals.Set(FunctionGlobal, 9);
        Require(Pass(condition) && evaluated == 3,
            "CTDA used its comparison global as the function result or failed to evaluate the function anew.");

        globals.Set(ComparisonGlobal, 3);
        bool[] operations = [true, false, false, true, false, true];
        for (byte operation = 0; operation < operations.Length; operation++)
            Require(FalloutCondition.AllPass([GlobalCondition(owner, operation)], _ => 3, globals: globals) == operations[operation],
                "Comparison-global changed an authored comparison operator.");
        globals.Set(ComparisonGlobal, 5.5f);
        Require(FalloutCondition.AllPass([GlobalCondition(owner, 0)], _ => 5.5f, globals: globals),
            "Integer-declared GLOB storage was rounded instead of retaining live Float32.");
        globals.Set(ComparisonGlobal, BitConverter.UInt32BitsToSingle(0x8000_0000));
        Require(BitConverter.SingleToUInt32Bits(globals.ReadConditionComparison(condition)) == 0x8000_0000,
            "Comparison-global changed the current signed-zero storage.");

        var literal = LiteralCondition(owner, 7);
        Require(FalloutCondition.AllPass([literal], _ => 7), "Literal CTDA began requiring a global owner.");
        Reject(() => FalloutCondition.AllPass([condition], Evaluate), "Missing live RHS owner was admitted.");
        Reject(() => FalloutCondition.AllPass([condition], Evaluate, globals: new(globals.Sources)),
            "Unbound globals borrowed another stack's comparison authority.");
        Reject(() => Pass(condition with { GlobalComparison = null }), "Flagged CTDA borrowed its literal field as a FormID.");
        Reject(() => Pass(condition with { Comparison = 1 }), "Flagged CTDA admitted conflicting literal and raw operands.");
        Reject(() => FalloutCondition.AllPass([literal with { GlobalComparison = new(ComparisonRaw) }], _ => 7, globals: globals),
            "Literal CTDA borrowed a raw global operand.", "mismatched");
        foreach (var invalid in new[] { NullRaw, MissingRaw, NonGlobalRaw, DeletedRaw, UndeclaredRaw })
            Reject(() => Pass(GlobalCondition(owner, raw: invalid)), "CTDA admitted a null, missing, non-GLOB, deleted or unowned namespace.");
        var nanWord = GlobalCondition(owner, raw: 0xffc0_0000);
        Require(nanWord.GlobalComparison?.RawFormId == 0xffc0_0000,
            "Comparison-global FormID was decoded as a non-finite Float32.");
        Reject(() => Pass(nanWord), "Raw invalid namespace was accepted after retaining its exact bytes.");

        var before = evaluated;
        foreach (byte flag in new byte[] { 0x02, 0x08, 0x10 })
            Reject(() => Pass(GlobalCondition(owner, extraFlags: flag)), "Another CTDA flag became supported accidentally.");
        Reject(() => FalloutCondition.AllPass([GlobalCondition(owner, runOn: 1)], Evaluate, globals: globals),
            "Comparison-global bypassed run-on admission.");
        Require(evaluated == before, "Unowned flag/run-on admission entered the function result first.");
        Reject(() => FalloutCondition.AllPass([GlobalCondition(owner, function: UnknownFunction)], Evaluate),
            "Missing comparison owner disguised an unknown function as a valid global query.", "Authored condition function is unowned.");
        Reject(() => FalloutCondition.AllPass([condition], _ => float.NaN, globals: globals),
            "Comparison-global admitted a non-finite function result.");
        Reject(() => FalloutCondition.Read(owner, LiteralBytes(float.PositiveInfinity)), "Literal non-finite comparison was admitted.");
        Reject(() => GlobalCondition(owner, operation: 6), "Unknown comparison operation was admitted.");

        // A satisfied OR alternative owns neither the skipped function nor RHS.
        var skipped = 0;
        Require(FalloutCondition.AllPass([literal with { Flags = 1 }, GlobalCondition(owner, function: UnknownFunction, raw: NullRaw)], value =>
        {
            skipped++;
            return value.Function == GlobalValueFunction ? 7 : throw new InvalidOperationException("Skipped function ran.");
        }) && skipped == 1, "OR short-circuit read an unvisited function/global operand.");
        foreach (var extent in new[] { 20, 24, 28 })
            Require(GlobalCondition(owner, extent: extent).GlobalComparison?.RawFormId == ComparisonRaw,
                "Supported CTDA extent changed its raw global operand.");

        using (var other = FalloutPluginStack.Load(fixture.Directory, InputOrder))
        {
            var otherGlobals = FalloutGlobalState.Read(other);
            Reject(() => FalloutCondition.AllPass([condition], _ => 1, globals: otherGlobals),
                "Identical names/content in another reader lifetime replaced the condition's actual owner.", "plugin lifetime");
            var otherCondition = GlobalCondition(other.GetEffective(Quest));
            Reject(() => FalloutCondition.AllPass([otherCondition], _ => 1, globals: globals),
                "A foreign condition borrowed this load's global state.", "plugin lifetime");
        }

        for (var cycle = 0; cycle < 3; cycle++)
        {
            globals.Set(ComparisonGlobal, cycle + 2.25f);
            var saved = JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(globals.Capture())) ??
                throw new InvalidDataException("Authored global snapshot disappeared during JSON.");
            using var coldRecords = FalloutPluginStack.Load(fixture.Directory, InputOrder);
            var cold = FalloutGlobalState.Read(coldRecords);
            cold.Restore(saved);
            var coldCondition = GlobalCondition(coldRecords.GetEffective(Quest), 0);
            Require(FalloutCondition.AllPass([coldCondition], _ => cycle + 2.25f, globals: cold),
                "Cold global comparison used the constructor value instead of restored current storage.");
            Reject(() => FalloutCondition.AllPass([condition], _ => cycle + 2.25f, globals: cold),
                "Cold restoration accepted the prior reader's condition object.");
        }
        RuntimeCallers(records, globals);
        using var retired = FalloutPluginStack.Load(fixture.Directory, InputOrder);
        var retiredGlobals = FalloutGlobalState.Read(retired);
        var retiredCondition = GlobalCondition(retired.GetEffective(Quest));
        retired.Dispose();
        Reject(() => FalloutCondition.AllPass([retiredCondition], _ => 1, globals: retiredGlobals),
            "Comparison-global survived retirement of its actual plugin stack.");
        Console.WriteLine("OPENNV_CONDITION_GLOBAL_PASS rawForm=true ownerMasters=true winningOverride=true currentWrites=true " +
            "functionIndependent=true operators=6 flagsRetained=true shortCircuit=true wrongLifetime=true cold=3 runtimeCallers=true");
    }

    private static void RuntimeCallers(FalloutPluginStack records, FalloutGlobalState globals)
    {
        globals.Set(ComparisonGlobal, 5); globals.Set(FunctionGlobal, 7);
        var quests = new FalloutQuestState(records);
        quests.SetRunning(Quest, true);
        float Evaluate(FalloutCondition condition) => condition.Function == GlobalValueFunction ? globals.Get(condition.FormArgument1) :
            throw new NotSupportedException("Authored runtime function is unowned.");
        var selection = new FalloutDialogueQuestSelection(records, quests, globals);
        Require(selection.Eligible(Quest, Evaluate), "Quest header did not borrow live comparison globals.");
        globals.Set(ComparisonGlobal, 8);
        Require(!selection.Eligible(Quest, Evaluate), "Cached quest header cached a mutable comparison value.");

        var infoRecord = records.GetEffective(Info);
        var info = new FalloutDialogueInfo(infoRecord, Quest, 1, 0, 0, 0, [GlobalBytes()], [], "", "");
        Require(!FalloutDialogueTopic.ConditionsPass(info, Speaker, quest => quests.Stage(quest), Evaluate, globals),
            "INFO eligibility failed to apply current global comparison.");
        globals.Set(ComparisonGlobal, 5);
        Require(FalloutDialogueTopic.ConditionsPass(info, Speaker, quest => quests.Stage(quest), Evaluate, globals),
            "INFO eligibility substituted another condition operand.");

        var message = FalloutSourceMessage.Read(records.GetEffective(Message));
        Require(message.ResolveButtons(records, Evaluate, globals).ButtonIndices!.SequenceEqual(new[] { 0, 1 }),
            "Message global comparison omitted an eligible authored button.");
        globals.Set(ComparisonGlobal, 8);
        Require(message.ResolveButtons(records, Evaluate, globals).ButtonIndices!.SequenceEqual(new[] { 1 }),
            "Message cached button eligibility across a live global store.");

        globals.Set(ComparisonGlobal, 5);
        var idles = new FalloutIdleConditions(records, globals);
        Require(idles.AllPass(Idle, Evaluate) && FalloutAiPackages.Select(records, Speaker, Evaluate, globals: globals)?.FormKey == Package,
            "Actual package/idle selection did not borrow current comparison globals.");
        globals.Set(ComparisonGlobal, 8);
        Require(!idles.AllPass(Idle, Evaluate) && FalloutAiPackages.Select(records, Speaker, Evaluate, globals: globals)?.FormKey == FallbackPackage,
            "Package priority or cached IDLE fields froze mutable comparison eligibility.");
        Require(FalloutAiPackages.Select(records, Speaker, _ => throw new InvalidOperationException("Excluded package evaluated."),
            eligible: candidate => candidate.FormKey != Package)?.FormKey == FallbackPackage,
            "Excluded package entered its function/global comparison instead of the authored fallback.");

        globals.Set(ComparisonGlobal, 5);
        var executed = new List<string>();
        IEnumerable<bool> Execute(FalloutPluginRecord _, IReadOnlyList<FalloutPluginSubrecord> __, string source)
        {
            executed.Add(source);
            if (source == "set-comparison") globals.Set(ComparisonGlobal, 8);
            return Array.Empty<bool>();
        }
        var stages = new FalloutQuestStages(records, quests, Execute, Evaluate, globals: globals);
        stages.Enter(Quest, 1);
        Require(executed.SequenceEqual(new[] { "set-comparison", "after" }) && !stages.HasUnfinishedResults,
            "Quest stage entries reused a stale comparison-global value after actual earlier result writes.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message, string? expectedRefusal = null)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
        {
            if (expectedRefusal is not null && !error.Message.Contains(expectedRefusal, StringComparison.Ordinal))
                throw new InvalidDataException(message + " Refused for another reason.", error);
            return;
        }
        throw new InvalidDataException(message);
    }

    private static FalloutCondition GlobalCondition(FalloutPluginRecord owner, byte operation = 2, uint raw = ComparisonRaw,
        ushort function = GlobalValueFunction, uint runOn = 0, byte extraFlags = 0, int extent = 28) =>
        FalloutCondition.Read(owner, GlobalBytes(operation, raw, function, runOn, extraFlags, extent));
    private static byte[] GlobalBytes(byte operation = 2, uint raw = ComparisonRaw, ushort function = GlobalValueFunction,
        uint runOn = 0, byte extraFlags = 0, int extent = 28)
    {
        var bytes = new byte[extent];
        bytes[0] = checked((byte)((operation << 5) | FalloutCondition.ComparisonGlobalFlag | extraFlags));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), raw);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), FunctionRaw);
        if (extent >= 24) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), runOn);
        return bytes;
    }
    private static FalloutCondition LiteralCondition(FalloutPluginRecord owner, float comparison) =>
        FalloutCondition.Read(owner, LiteralBytes(comparison));
    private static byte[] LiteralBytes(float comparison)
    {
        var bytes = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), GlobalValueFunction);
        return bytes;
    }
}
