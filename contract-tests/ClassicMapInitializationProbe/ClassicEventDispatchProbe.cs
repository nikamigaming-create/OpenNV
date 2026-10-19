using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicEventDispatchProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var state = new ClassicIntProcedureState(Enumerable.Range(0, 5).ToDictionary(index => index, _ => 0),
            new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        var program = Program();
        var before = JsonSerializer.Serialize(state);
        ClassicIntProcedureResult Run(string campaign, int action, bool first, int budget = 1000) =>
            ClassicIntEvents.Execute(program, campaign, action, state, context, ClassicIntWorldObjectState.Empty, null, budget, first);
        var first = Run("fallout-1", 15, true);
        if (first.State.ProgramVariables[0] != 1 || first.State.ProgramVariables[1] != 15 || first.State.ProgramVariables[2] != 0)
            throw new InvalidOperationException("FO1 first load must run startup with the triggering action exactly once.");
        var second = Run("fallout-2", 15, true);
        if (second.State.ProgramVariables[0] != 1 || second.State.ProgramVariables[1] != 0 ||
            second.State.ProgramVariables[2] != 1 || second.State.ProgramVariables[3] != 15)
            throw new InvalidOperationException("FO2 first load lost action-zero startup or the explicit requested event.");
        var loaded = Run("fallout-1", 15, false);
        if (loaded.State.ProgramVariables[0] != 0 || loaded.State.ProgramVariables[2] != 1 || loaded.State.ProgramVariables[3] != 15)
            throw new InvalidOperationException("Loaded named event reran startup or lost its action.");
        if (ClassicIntEvents.Procedure(program, "fallout-1", 3) != "desc_p_proc" ||
            ClassicIntEvents.Procedure(program, "fallout-2", 3) != "description_p_proc" ||
            ClassicIntEvents.Procedure(program, "fallout-1", 15) != "MAP_ENTER_P_PROC" ||
            !ClassicIntEvents.HasHandler(program, "fallout-1", 15) || ClassicIntEvents.HasHandler(program, "fallout-1", 22))
            throw new InvalidOperationException("Source event name, first matching index or bulk-handler eligibility changed.");
        foreach (var action in new[] { 0, 22 })
        {
            var fallback = Run("fallout-1", action, false);
            if (fallback.State.ProgramVariables[4] != action || fallback.State.ProgramVariables[0] != 0)
                throw new InvalidOperationException("Missing/index-zero handler did not use source index one.");
        }
        try { _ = Run("fallout-2", 15, true, second.ExecutedInstructions - 1); throw new Exception("First-load calls received independent budgets."); }
        catch (InvalidOperationException) { }
        foreach (var (campaign, action) in new[] { ("fallout-1", -1), ("fallout-1", 24), ("fallout-2", 28) })
        {
            try { _ = Run(campaign, action, false); throw new Exception("Unknown script action was accepted."); }
            catch (InvalidDataException) { }
        }
        if (JsonSerializer.Serialize(state) != before) throw new InvalidOperationException("Event dispatch mutated caller state.");
        foreach (var (tick, hour) in new[] { (0, 0), (599, 0), (600, 1), (35999, 59), (36000, 100),
            (264600, 721), (863999, 2359), (864000, 0), (1128600, 721) })
            if (ClassicIntTimerOwner.GameTimeHour(new(10), tick) != hour)
                throw new InvalidOperationException("Script clock hour lost minute truncation or day rollover.");
        foreach (var (rate, tick) in new[] { (0, 0), (10, -1) })
        {
            try { _ = ClassicIntTimerOwner.GameTimeHour(new(rate), tick); throw new Exception("Invalid clock input was accepted."); }
            catch (InvalidOperationException) { }
        }
        Console.WriteLine("OPENNV_CLASSIC_EVENT_DISPATCH_PASS firstLoadByCampaign=true sourceIndexFallback=true asciiNames=true descriptionByCampaign=true sharedBudget=true rollback=true");
    }

    private static ClassicIntProgram Program()
    {
        var offset = 0;
        var declarations = new List<ClassicIntProcedure>();
        void Add(string name, params (ushort Opcode, int? Operand)[] source)
        {
            var instructions = source.Select(row =>
            {
                var instruction = new ClassicIntInstruction(offset, row.Opcode, row.Operand);
                offset += row.Operand is null ? 2 : 6; return instruction;
            }).ToArray();
            declarations.Add(new(name, instructions[0].Offset, null, instructions));
        }
        Add("no_p_proc", (0xffff, null));
        Add("fallback", (0x80c7, null), (0xc001, 4), (0x8013, null), (0x801c, null));
        Add("StaRt", (0xc001, 0), (0x8012, null), (0xc001, 1), (0x8039, null), (0xc001, 0), (0x8013, null),
            (0x80c7, null), (0xc001, 1), (0x8013, null), (0x801c, null));
        Add("MAP_ENTER_P_PROC", (0xc001, 2), (0x8012, null), (0xc001, 1), (0x8039, null), (0xc001, 2), (0x8013, null),
            (0x80c7, null), (0xc001, 3), (0x8013, null), (0x801c, null));
        Add("map_enter_p_proc", (0xffff, null));
        Add("desc_p_proc", (0x801c, null));
        Add("description_p_proc", (0x801c, null));
        return new("synthetic-event-dispatch", declarations, declarations.ToDictionary(row => row.Name),
            declarations.SelectMany(row => row.Instructions).ToDictionary(row => row.Offset),
            new Dictionary<int, string>(), new Dictionary<int, string>()) { StartupProcedure = "StaRt" };
    }
}
