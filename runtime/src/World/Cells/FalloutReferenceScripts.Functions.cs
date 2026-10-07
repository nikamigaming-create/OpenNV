using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly Dictionary<FalloutFormKey, (FalloutUserFunction Function, FalloutScriptBindings Bindings)> _functions = [];
    private int _functionDepth;

    private FalloutUserFunction UserFunction(FalloutFormKey script)
    {
        if (_functions.TryGetValue(script, out var existing)) return existing.Function;
        var definition = FalloutUserFunction.Read(records.GetEffective(script));
        _functions.Add(script, (definition, Bindings(definition.Script, definition.Script, definition.Script.ReadSubrecords())));
        return definition;
    }

    internal double InvokeFunction(FalloutFormKey script, FalloutFormKey? caller,
        IReadOnlyList<double> arguments, double seconds) =>
        InvokeFunctionValue(script, caller, arguments.Select(value => (FalloutScriptValue)value).ToArray(), seconds).Number;

    internal IReadOnlyList<FalloutScriptHitHandlerResult> DispatchHitCallbacks(FalloutFormKey actor, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Hit callback frame time must be finite and nonnegative.");
        return (host.Events ?? throw new NotSupportedException("Hit callbacks have no process event owner.")).Hits
            .Dispatch(records, actor, (script, caller) =>
                _ = InvokeFunctionValue(script, caller, [], seconds, defaultParameters: true));
    }

    private FalloutScriptValue InvokeFunctionValue(FalloutFormKey script, FalloutFormKey? caller,
        IReadOnlyList<FalloutScriptValue> arguments, double seconds,
        FalloutScriptExecutionBudget? suppliedBudget = null, bool defaultParameters = false)
    {
        var definition = UserFunction(script);
        if (arguments.Count > definition.Parameters.Count ||
            arguments.Count != definition.Parameters.Count && !defaultParameters)
            throw new InvalidDataException("Function argument count differs from its declaration.");
        if (_functionDepth >= 30) throw new NotSupportedException("User function recursion exceeds 30 calls.");
        if (caller is { } reference && records.RuntimeFormId(reference) != 0x14 &&
            records.GetEffective(reference).Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Function caller is not a reference.");
        using var execution = world.ScriptValues.Arrays.BeginExecution();
        using var frame = new FalloutUserFunctionFrame(definition, world.ScriptValues.Arrays);
        for (var index = 0; index < arguments.Count; ++index)
            frame.WriteValue(definition.Parameters[index], arguments[index]);
        ++_functionDepth;
        try
        {
            foreach (var _ in Steps(caller ?? script, _functions[script].Bindings, definition.Program, null,
                seconds, frame, suppliedBudget ?? new())) { }
            return frame.ResultValue;
        }
        finally { --_functionDepth; }
    }
}
