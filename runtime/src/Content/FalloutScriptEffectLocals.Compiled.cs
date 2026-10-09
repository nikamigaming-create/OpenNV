namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptEffectLocals
{
    internal FalloutScriptValue ReadCompiled(FalloutCompiledVariable variable) => _storage.ReadCompiled(variable);
    internal void WriteCompiled(FalloutCompiledVariable variable, FalloutScriptValue value) =>
        _storage.WriteCompiled(variable, value);
}

internal sealed partial class FalloutScriptEffectLocalStorage
{
    private FalloutScriptLocalMetadata CompiledSlot(FalloutCompiledVariable variable, out bool reference)
    {
        if (variable.OwnerReference is not null || variable.Slot == 0 ||
            variable.Storage is not ((byte)'s' or (byte)'l' or (byte)'f'))
            throw new InvalidDataException("Implicit active-effect operand has no admitted scalar slot/view.");
        var script = _script;
        _ = FalloutScriptLocals.ReadDeclarations(script, FalloutScriptDeclarationAuthority.CompiledVanilla);
        var flag = variable.Storage is (byte)'s' or (byte)'l' ? (byte)1 : (byte)0;
        var declared = FalloutScriptLocals.ReadMetadata(script).FirstOrDefault(entry =>
            entry.Index == variable.Slot && entry.StorageFlags == flag) ??
            throw new InvalidDataException("Compiled active-effect local differs from its ordered SLSD declaration.");
        if (!_first.ContainsKey(variable.Slot))
            throw new InvalidDataException("Compiled active-effect slot has no actual first-ID cell.");
        reference = FalloutScriptLocals.HasReferenceView(script, variable.Slot) &&
            (!FalloutScriptLocals.HasMixedStorage(script, variable.Slot) || variable.Storage == (byte)'f');
        return declared;
    }

    internal FalloutScriptValue ReadCompiled(FalloutCompiledVariable variable)
    {
        _ = CompiledSlot(variable, out var reference);
        var payload = _entries[_first[variable.Slot]].Payload;
        if (reference) return FalloutScriptValue.Form(unchecked((uint)payload));
        var number = BitConverter.UInt64BitsToDouble(payload);
        RequireCompiledNumber(variable, number);
        return number;
    }

    internal void WriteCompiled(FalloutCompiledVariable variable, FalloutScriptValue value)
    {
        _ = CompiledSlot(variable, out var reference);
        ulong payload;
        if (reference)
        {
            if (value.Kind == FalloutScriptValueKind.Number && value.Number == 0)
                value = FalloutScriptValue.Form(0);
            if (value.Kind != FalloutScriptValueKind.Form)
                throw new NotSupportedException("Compiled active-effect reference assignment requires a form or literal null.");
            payload = checked((ulong)FalloutScriptValue.Form(value.Number).Number);
        }
        else
        {
            if (value.Kind != FalloutScriptValueKind.Number)
                throw new NotSupportedException("Compiled active-effect numeric assignment has no string/array/form lifetime owner.");
            RequireCompiledNumber(variable, value.Number);
            payload = BitConverter.DoubleToUInt64Bits(value.Number);
        }
        var ordinal = _first[variable.Slot];
        var entry = _entries[ordinal];
        _entries[ordinal] = (entry.Index, entry.Flags, payload);
    }

    private static void RequireCompiledNumber(FalloutCompiledVariable variable, double number)
    {
        if (!double.IsFinite(number))
            throw new InvalidDataException("Reached compiled active-effect numeric view is non-finite.");
        if (variable.Storage is (byte)'s' or (byte)'l' &&
            (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue))
            throw new NotSupportedException("Compiled active-effect integer view exceeds its exact Int32 domain.");
    }
}
