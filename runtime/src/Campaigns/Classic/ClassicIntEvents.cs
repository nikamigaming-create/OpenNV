namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Source event lookup and first-load execution for the two classic engines.</summary>
internal static class ClassicIntEvents
{
    private static readonly string[] Names =
    [
        "no_p_proc", "start", "spatial_p_proc", "desc_p_proc", "pickup_p_proc", "drop_p_proc",
        "use_p_proc", "use_obj_on_p_proc", "use_skill_on_p_proc", "none_x_bad", "none_x_bad",
        "talk_p_proc", "critter_p_proc", "combat_p_proc", "damage_p_proc", "map_enter_p_proc",
        "map_exit_p_proc", "create_p_proc", "destroy_p_proc", "none_x_bad", "none_x_bad",
        "look_at_p_proc", "timed_event_p_proc", "map_update_p_proc", "push_p_proc",
        "is_dropping_p_proc", "combat_is_starting_p_proc", "combat_is_over_p_proc"
    ];

    internal static string Name(string campaign, int action)
    {
        var count = campaign switch
        {
            "fallout-1" => 24,
            "fallout-2" => 28,
            _ => throw new NotSupportedException("Classic event contract is unavailable for " + campaign)
        };
        if (action < 0 || action >= count) throw new InvalidDataException("Classic script action is outside its source event table.");
        return campaign == "fallout-2" && action == 3 ? "description_p_proc" : Names[action];
    }

    private static int Index(ClassicIntProgram program, string campaign, int action)
    {
        var name = Name(campaign, action);
        for (var index = 0; index < program.ProcedureOrder.Count; index++)
            if (ClassicIntExportNames.Equal(program.ProcedureOrder[index].Name, name)) return index;
        return -1;
    }

    internal static bool HasHandler(ClassicIntProgram program, string campaign, int action) => Index(program, campaign, action) > 0;

    internal static string Procedure(ClassicIntProgram program, string campaign, int action)
    {
        var index = Index(program, campaign, action);
        // The native dispatcher uses source table index one, not a synthesized
        // empty handler, when the named procedure is absent or has index zero.
        if (index <= 0) index = 1;
        if (index >= program.ProcedureOrder.Count) throw new InvalidDataException("Classic event has no source fallback procedure.");
        return program.ProcedureOrder[index].Name;
    }

    internal static ClassicIntProcedureResult Execute(ClassicIntProgram program, string campaign, int action,
        ClassicIntProcedureState state, ClassicIntExpressionContext context, ClassicIntWorldObjectState world,
        ClassicRetailRandomContract? randomContract, int instructionBudget = 100000, bool initializeModule = false)
    {
        _ = Name(campaign, action);
        ClassicIntProcedureResult? initialized = null;
        if (initializeModule)
        {
            var startup = program.StartupProcedure ?? throw new InvalidDataException("Classic module has no source startup entry.");
            initialized = ClassicIntProcedureVm.Execute(program, startup, state,
                context with { ScriptAction = campaign == "fallout-1" ? action : 0 }, world,
                randomContract, instructionBudget, initializeModule: true);
            // FO1 schedules the fresh source program with the triggering action.
            // FO2 runs startup at action zero, then explicitly calls the event.
            if (campaign == "fallout-1") return initialized;
            state = initialized.State; world = initialized.WorldObjects;
            context = context with { ExternalVariables = initialized.ExternalVariables, ExternalOwners = initialized.ExternalOwners };
            instructionBudget -= initialized.ExecutedInstructions;
        }
        var result = ClassicIntProcedureVm.Execute(program, Procedure(program, campaign, action), state,
            context with { ScriptAction = action }, world, randomContract, instructionBudget);
        return initialized is null ? result : result with
        {
            ExecutedInstructions = checked(initialized.ExecutedInstructions + result.ExecutedInstructions),
            ModuleInitializationInstructions = initialized.ModuleInitializationInstructions,
            MessageEffects = initialized.MessageEffects.Concat(result.MessageEffects).ToArray(),
            SoundEffects = initialized.SoundEffects.Concat(result.SoundEffects).ToArray()
        };
    }
}
