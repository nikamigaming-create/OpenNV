namespace OpenNV.Runtime.Compatibility.NativePlugins;

// The leaf's encoded variable type is independent of SLSD scalar storage.
// The event-list lookup selects the first matching ID, preserving every tail
// entry and its independent payload. This object never owns a second value.
internal sealed class NativeNvseExpressionLocal
{
    internal NativeNvseLocalContext Context { get; }
    internal int Entry { get; }
    internal uint Index { get; }
    internal byte VariableType { get; }
    internal NativeNvseTokenType Type { get; }
    internal uint CachedStringIdentity { get; }
    internal uint Address => checked(Context.Variables + checked((uint)Entry * 24U));

    private NativeNvseExpressionLocal(NativeNvseLocalContext context, int entry, byte variableType)
    {
        Context = context; Entry = entry; Index = context.Declarations[entry].Index; VariableType = variableType;
        Type = variableType switch
        {
            0 or 1 => NativeNvseTokenType.NumericVariable,
            2 => NativeNvseTokenType.StringVariable,
            3 => NativeNvseTokenType.ArrayVariable,
            4 => NativeNvseTokenType.ReferenceVariable,
            _ => throw new NotSupportedException("Native expression variable type has no public scalar/local owner."),
        };
        if (Type == NativeNvseTokenType.StringVariable)
            CachedStringIdentity = Handle(context.Authority.ReadEntry(entry));
    }

    internal static NativeNvseExpressionLocal Bind(NativeNvseLocalContext context, byte variableType,
        ushort referenceIndex, ushort index)
    {
        ArgumentNullException.ThrowIfNull(context); context.Authority.RequireCurrent();
        if (referenceIndex != 0)
            throw new NotSupportedException("Native expression foreign reference/event-list resolution is not bound to this caller.");
        if (context.Retired || context.EventList == 0 || context.Variables == 0)
            throw new InvalidOperationException("Native expression local has no living constructed event-list.");
        var entry = -1;
        for (var ordinal = 0; ordinal < context.Declarations.Length; ++ordinal)
            if (context.Declarations[ordinal].Index == index) { entry = ordinal; break; }
        if (entry < 0) throw new InvalidDataException("Native expression local ID is absent from the original ordered event-list.");
        // String/array registration owns root retention and destruction. The
        // encoded V type alone cannot create that owner in a scalar campaign.
        if (variableType == 2 && context.Declarations[entry].Kind != NativeNvseLocalKind.String ||
            variableType == 3 && context.Declarations[entry].Kind != NativeNvseLocalKind.Array)
            throw new NotSupportedException("Native expression string/array local needs its actual registered value-lifetime owner.");
        return new(context, entry, variableType);
    }

    internal void RequireCurrent()
    {
        Context.Authority.RequireCurrent();
        if (Context.Retired || (uint)Entry >= Context.Declarations.Length || Context.Declarations[Entry].Index != Index ||
            Context.Declarations.Take(Entry).Any(declaration => declaration.Index == Index))
            throw new InvalidOperationException("Native expression local no longer owns the first original matching entry.");
    }

    internal ulong Read() { RequireCurrent(); return Context.Authority.ReadEntry(Entry); }
    internal static uint Handle(ulong bits)
    {
        var number = BitConverter.UInt64BitsToDouble(bits);
        if (!double.IsFinite(number) || number < 0 || number > uint.MaxValue || number != Math.Truncate(number))
            throw new NotSupportedException("Native expression handle conversion is outside its exact UInt32 domain.");
        return (uint)number;
    }
}

internal sealed record NativeNvseExpressionLocalReceipt(ulong Generation, ulong Callback, ulong Parent,
    ulong Caller, ulong Evaluator, ulong Token, ulong Context, int Entry, uint Index,
    NativeNvseTokenType Type, uint Address, ulong Bits);
