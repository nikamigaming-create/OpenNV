namespace OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicIntEventDispatcher
{
    internal static ClassicIntProcedureResult Execute(
        ClassicMapIntProgram sourceProgram,
        string sourceProcedure,
        ClassicIntProcedureState sourceState,
        ClassicIntExpressionContext game,
        IClassicIntWorldObjectState worldObjects,
        ClassicRetailRandomContract randomContract,
        int instructionBudget)
    {
        if (sourceProgram.ExecutableProgram.Identity != sourceProgram.Program ||
            !sourceProgram.ExecutableProgram.Procedures.ContainsKey(sourceProcedure))
            throw new InvalidOperationException(
                $"Classic INT event is not source-owned: " +
                $"{sourceProgram.Program}:{sourceProcedure}.");
        return ClassicIntProcedureVm.Execute(
            sourceProgram.ExecutableProgram,
            sourceProcedure,
            sourceState,
            game,
            worldObjects,
            randomContract,
            instructionBudget);
    }
}
