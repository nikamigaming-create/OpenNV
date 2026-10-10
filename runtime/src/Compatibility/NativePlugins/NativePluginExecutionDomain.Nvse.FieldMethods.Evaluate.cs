using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativeSourceFieldValue EvaluateSourceFieldMethod(NativeNvsePlugin plugin, NativeSourceFieldMethod method,
        NativeNvseSourceObject? receiver)
    {
        RequireSourceFieldMethod(method, plugin);
        return EvaluateSourceFieldExpression(plugin, method.Source, method.Abi, method.ReceiverClass, receiver);
    }
    private NativeSourceFieldValue EvaluateSourceFieldExpression(NativeNvsePlugin plugin, FalloutEngineFieldMethod source,
        NativePluginAbi abi, NativeNvseSourceClass? receiverClass, NativeNvseSourceObject? receiver)
    {
        if (abi == NativePluginAbi.Thiscall)
        {
            if (receiver is null || receiver.Class != receiverClass)
                throw new InvalidDataException("Source thiscall method lost its actual constructed receiver class.");
            VerifyNvseSourceObject(plugin, receiver);
        }
        else if (receiver is not null || source.Result.UsesReceiver || source.Reads.Any(value => value.UsesReceiver))
            throw new InvalidDataException("Source cdecl field method cannot borrow an implicit receiver.");
        var values = new Dictionary<FalloutEngineFieldExpression, NativeSourceFieldValue>(ReferenceEqualityComparer.Instance);
        // Preserve even discarded source memory reads and their original
        // order. An overwritten return register cannot hide a source fault.
        foreach (var read in source.Reads) _ = Evaluate(read, 0);
        return Evaluate(source.Result, 0);

        NativeSourceFieldValue Evaluate(FalloutEngineFieldExpression expression, int depth)
        {
            if (values.TryGetValue(expression, out var actual)) return actual;
            actual = EvaluateCore(expression, depth); values.Add(expression, actual); return actual;
        }
        NativeSourceFieldValue EvaluateCore(FalloutEngineFieldExpression expression, int depth)
        {
            if (depth > 256) throw new InvalidDataException("Source field expression exceeds its complete admitted tree.");
            NativeSourceFieldValue Input(int index)
            {
                if (index >= expression.Inputs.Count) throw new InvalidDataException("Source field expression omits a semantic input.");
                return Evaluate(expression.Inputs[index], depth + 1);
            }
            switch (expression.Operation)
            {
                case FalloutEngineFieldOperation.Receiver:
                    return receiver is { } actual ? new(actual.Address, NativeSourceFieldValueRole.Object, actual) :
                        throw new NotSupportedException("Source field expression requires its genuine native receiver.");
                case FalloutEngineFieldOperation.Constant:
                    return new(expression.Operand, NativeSourceFieldValueRole.Scalar);
                case FalloutEngineFieldOperation.GlobalAddress:
                    {
                        var globals = _engineGlobals.Values.Where(value => value.Address == expression.Operand &&
                            value.Generation == Generation && value.Module == plugin.Module).ToArray();
                        if (globals.Length != 1) throw new NotSupportedException("Source field read requires its exact living global cell producer.");
                        var global = globals[0]; RequireEngineGlobalCurrent(global, plugin);
                        return new(global.Address, NativeSourceFieldValueRole.Global, Global: global);
                    }
                case FalloutEngineFieldOperation.AddAddress:
                    {
                        var basis = Input(0); var value = unchecked(basis.Bits + expression.Operand);
                        if (basis.Role != NativeSourceFieldValueRole.Scalar)
                        {
                            var displacement = unchecked((int)expression.Operand);
                            var exact = (long)basis.Bits + displacement;
                            if (exact != value) throw new InvalidDataException("Source pointer arithmetic wrapped its x86 allocation.");
                            if (basis.Object is { } target && (value < target.Address ||
                                (ulong)value >= (ulong)target.Address + target.Image.Length) ||
                                basis.Buffer is { } allocation && (value < allocation.Address ||
                                (ulong)value >= (ulong)allocation.Address + allocation.Length) ||
                                basis.Global is { } global && value != global.Address)
                                throw new InvalidDataException("Source pointer arithmetic left its genuine published owner.");
                        }
                        return basis with { Bits = value };
                    }
                case FalloutEngineFieldOperation.Read:
                    return ReadSourceMethodMemory(plugin, Input(0), expression.Width);
                case FalloutEngineFieldOperation.And:
                    {
                        var basis = Input(0); return BitsResult(basis, basis.Bits & expression.Operand);
                    }
                case FalloutEngineFieldOperation.Xor:
                    {
                        var basis = Input(0); return BitsResult(basis, basis.Bits ^ expression.Operand);
                    }
                case FalloutEngineFieldOperation.Or:
                    {
                        if (expression.Width == 1 && expression.Inputs.Count == 1)
                            throw new NotSupportedException("A partial AL result retains its unowned upper EAX bits.");
                        var first = Input(0);
                        if (expression.Inputs.Count != 2) return BitsResult(first, first.Bits | expression.Operand);
                        var second = Input(1);
                        if (first.Role != NativeSourceFieldValueRole.Scalar && second.Role != NativeSourceFieldValueRole.Scalar)
                            throw new NotSupportedException("Combining two pointer values requires their exact independent result-alias owner.");
                        return BitsResult(first.Role == NativeSourceFieldValueRole.Scalar ? second : first, first.Bits | second.Bits);
                    }
                case FalloutEngineFieldOperation.ShiftRight:
                    {
                        var basis = Input(0); return BitsResult(basis, basis.Bits >> checked((int)expression.Operand));
                    }
                case FalloutEngineFieldOperation.ShiftLeft:
                    {
                        var basis = Input(0); return BitsResult(basis, basis.Bits << checked((int)expression.Operand));
                    }
                case FalloutEngineFieldOperation.EqualZero:
                    return Scalar(Input(0).Bits == 0 ? 1U : 0U);
                case FalloutEngineFieldOperation.NotEqualZero:
                    return Scalar(Input(0).Bits != 0 ? 1U : 0U);
                case FalloutEngineFieldOperation.SignExtend:
                    {
                        var value = Input(0).Bits;
                        return expression.Width switch
                        {
                            1 => Scalar(unchecked((uint)(int)(sbyte)value)),
                            2 => Scalar(unchecked((uint)(int)(short)value)),
                            _ => throw new InvalidDataException("Source sign extension changed its admitted width.")
                        };
                    }
                default: throw new NotSupportedException("Source field expression requires another effect/ABI producer.");
            }
        }
        static NativeSourceFieldValue Scalar(uint value) => new(value, NativeSourceFieldValueRole.Scalar);
        static NativeSourceFieldValue BitsResult(NativeSourceFieldValue basis, uint value)
        {
            if (basis.Role == NativeSourceFieldValueRole.Scalar) return Scalar(value);
            // A pointer-derived result cannot lose its retained alias merely
            // because the original body uses an integer bit operation. Only
            // actual null or an address within the same real allocation is
            // owned; no numerical coincidence binds another allocation.
            if (value == 0 || value == basis.Bits) return basis with { Bits = value };
            var allocation = basis.Object?.Image ?? basis.Buffer;
            if (allocation is not null && value >= allocation.Address &&
                (ulong)value < (ulong)allocation.Address + allocation.Length) return basis with { Bits = value };
            throw new NotSupportedException("Source pointer manipulation leaves its actual alias/result-lifetime owner.");
        }
    }

    private void RetainSourceFieldResult(NativeNvsePlugin plugin, NativeSourceFieldMethod method, NativeSourceFieldValue result)
    {
        if (result.Bits == 0 || result.Role == NativeSourceFieldValueRole.Scalar) return;
        if (result.Global is not null)
            throw new NotSupportedException("Returning the address of a source global needs its separate externally borrowed cell lifetime.");
        var allocation = result.Object?.Image ?? result.Buffer ??
            throw new InvalidDataException("Nonnull source field result omitted its real native allocation.");
        var owner = result.Object ?? _nvseSourceObjects.Values.SingleOrDefault(value =>
            ReferenceEquals(value.Metadata, allocation) || ReferenceEquals(value.Code, allocation)) ??
            throw new InvalidDataException("Returned source buffer lost its actual constructing object.");
        VerifyNvseSourceObject(plugin, owner); VerifyGuest(allocation);
        if (result.Bits < allocation.Address || (ulong)result.Bits >= (ulong)allocation.Address + allocation.Length)
            throw new InvalidDataException("Returned source pointer leaves its retained allocation.");
        if (method.Borrows.Any(value => ReferenceEquals(value.Owner, owner) && value.Allocation.Handle == allocation.Handle &&
            value.Pointer == result.Bits && value.Role == result.Role)) return;
        if (owner.Dependents == uint.MaxValue) throw new InvalidDataException("Source result dependency counter exhausted.");
        ++owner.Dependents;
        method.Borrows.Add(new(method, owner, allocation, result.Bits, result.Role, owner.Authority.SourceOwner));
    }

    private static bool SameSourceFieldResult(NativeSourceFieldValue first, NativeSourceFieldValue second) =>
        first.Bits == second.Bits && first.Role == second.Role && ReferenceEquals(first.Object, second.Object) &&
        first.Buffer?.Handle == second.Buffer?.Handle && ReferenceEquals(first.Global, second.Global);
}
