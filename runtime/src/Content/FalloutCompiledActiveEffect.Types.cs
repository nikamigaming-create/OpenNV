namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledActiveEffectEventState(int Ordinal, ushort Event, int Begin, int End,
    string EventScopeSha256, bool Attempted, FalloutCompiledCursorSnapshot Cursor,
    FalloutCompiledSliceReceipt? Receipt, int? LastReachedOffset, string? Failure);

internal sealed record FalloutCompiledActiveEffectSnapshot(string Schema, FalloutFormKey Target,
    FalloutFormKey Spell, int EffectOrdinal, FalloutFormKey Effect, FalloutFormKey Script,
    long Generation, string Winner, string RecordSha256, string ScopeSha256, string ProgramSha256,
    IReadOnlyList<FalloutCompiledActiveEffectEventState> Events);

// This invocation retains the actual event-list cells and source program. It
// cannot select the target actor's attached script as its implicit local owner.
internal sealed class FalloutCompiledActiveEffectInvocation : IFalloutCompiledEventLocalAuthority
{
    internal FalloutCompiledActiveEffectExecution Owner { get; }
    internal int Ordinal { get; }
    internal FalloutFormKey Target => Owner.Target;
    internal FalloutCompiledScriptProgram Program => Owner.Program;
    internal FalloutCompiledEvent Block => Program.Events[Ordinal];
    internal FalloutCompiledExecutionCursor Cursor { get; }
    internal int PrefixBefore { get; }
    internal string EventScopeSha256 => FalloutCompiledSliceReceipt.EventScope(Program, Ordinal);

    internal FalloutCompiledActiveEffectInvocation(FalloutCompiledActiveEffectExecution owner,
        int ordinal, FalloutCompiledExecutionCursor cursor)
    {
        Owner = owner; Ordinal = ordinal; Cursor = cursor;
        PrefixBefore = cursor.State.CommittedInstructions;
    }

    internal void Require(FalloutPluginStack records, FalloutFormKey target,
        FalloutCompiledScriptProgram program, FalloutCompiledEvent? block)
    {
        Owner.RequireInvocation(this);
        if (Target != target || !ReferenceEquals(Program, program) || !ReferenceEquals(Block, block))
            throw new InvalidDataException("Compiled active-effect invocation changed its actual target/program/event owner.");
        Program.Scope.RequireSource(records.GetEffective(Program.Source.FormKey));
        if (!program.Standalone || FalloutScriptSourceKinds.Classify(program.ScriptType) != FalloutScriptSourceKind.MagicEffect ||
            program.CompiledFlag != 1)
            throw new InvalidDataException("Compiled active-effect invocation is not a winning magic-effect program.");
        if (records.RuntimeFormId(target) != 0x14 && records.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Compiled active-effect target has no genuine actor identity.");
        FalloutCompiledActiveEffectExecution.RequireStart(program, Block);
    }

    FalloutScriptEffectLocals IFalloutCompiledEventLocalAuthority.Locals => Owner.Locals;
    void IFalloutCompiledEventLocalAuthority.Require(FalloutPluginStack records, FalloutFormKey target,
        FalloutCompiledScriptProgram program, FalloutCompiledEvent block, FalloutCompiledExecutionCursor cursor,
        double seconds, FalloutFormKey? action)
    {
        Require(records, target, program, block);
        if (!ReferenceEquals(cursor, Cursor) || seconds != 0 || action is not null)
            throw new NotSupportedException("Magic-effect cursor/time/action differs from its actual Start producer.");
    }
    FalloutScriptValue IFalloutCompiledEventLocalAuthority.Read(FalloutCompiledVariable variable)
    {
        Owner.RequireInvocation(this);
        return Owner.Locals.ReadCompiled(variable);
    }
    void IFalloutCompiledEventLocalAuthority.Write(FalloutCompiledVariable variable, FalloutScriptValue value)
    {
        Owner.RequireInvocation(this);
        Owner.Locals.WriteCompiled(variable, value);
    }
}

internal sealed class FalloutCompiledActiveEffectReceipt
{
    internal FalloutCompiledActiveEffectInvocation Invocation { get; }
    internal FalloutCompiledSliceReceipt Retired { get; }
    internal int? LastReachedOffset { get; }

    private FalloutCompiledActiveEffectReceipt(FalloutCompiledActiveEffectInvocation invocation,
        FalloutCompiledSliceReceipt retired, int? lastReachedOffset)
    { Invocation = invocation; Retired = retired; LastReachedOffset = lastReachedOffset; }

    internal static FalloutCompiledActiveEffectReceipt Retire(FalloutScriptManualSaveRequests authority,
        FalloutCompiledActiveEffectInvocation invocation, FalloutCompiledSliceReceipt retired, int? lastReachedOffset)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var result = new FalloutCompiledActiveEffectReceipt(invocation, retired, lastReachedOffset);
        result.Require();
        if (retired.Invocation != 0) authority.RequireCurrentCompiledReceipt(retired);
        else if (retired.Disposition != "admission-refusal")
            throw new InvalidDataException("Unentered active effect cannot mint a successful compiled retirement.");
        return result;
    }

    internal void Require()
    {
        Invocation.Owner.RequireInvocation(Invocation);
        Retired.Require(Invocation.Program, Invocation.Target);
        if (Retired.EventOrdinal != Invocation.Ordinal || Retired.PrefixBefore != Invocation.PrefixBefore ||
            Retired.Disposition is not ("completed" or "closed-failure" or "admission-refusal") ||
            !FalloutCompiledActiveEffectExecution.SameCursor(Retired.Cursor, Invocation.Cursor.State) ||
            LastReachedOffset is { } offset && !Invocation.Program.EventInstructions(Invocation.Block)
                .Any(instruction => instruction.Offset == offset))
            throw new InvalidDataException("Compiled active-effect receipt changed its actual event prefix/retirement.");
    }
}

internal sealed class FalloutCompiledActiveEffectFailure(FalloutCompiledActiveEffectReceipt receipt, Exception cause)
    : NotSupportedException(receipt.Retired.Error, cause)
{
    internal FalloutCompiledActiveEffectReceipt Receipt { get; } = receipt;
}
