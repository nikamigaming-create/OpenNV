namespace OpenNV.Runtime.Content;

internal sealed record FalloutActivationCall(string Command, IReadOnlyList<string> Arguments);

// Evaluate an object's authored activation conditions before admitting any
// effect. Queries after effects need synchronous script execution and are not
// silently evaluated against the pre-activation state.
internal sealed class FalloutActivationProgram
{
    private readonly FalloutGameModeProgram _program;
    private readonly FalloutScriptBindings _bindings;

    internal FalloutActivationProgram(FalloutPluginStack records, FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Activation owner is not SCPT.");
        var fields = script.ReadSubrecords().ToArray();
        var source = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length != 1) throw new NotSupportedException("Activation needs one available source program.");
        _program = FalloutGameModeProgram.Read(source[0].Data.Span, "OnActivate");
        _bindings = new(records, script, script, fields);
    }

    internal FalloutPluginRecord Form(string name) => _bindings.Form(name);

    internal IReadOnlyList<FalloutActivationCall> Prepare(FalloutQuestState quests)
    {
        var calls = new List<FalloutActivationCall>();
        void BeforeEffect()
        {
            if (calls.Count != 0)
                throw new NotSupportedException("Activation queries after effects need synchronous result execution.");
        }
        FalloutFormKey Quest(string name)
        {
            BeforeEffect();
            var form = _bindings.Form(name);
            return form.Signature == "QUST" ? form.FormKey : throw new InvalidDataException("Activation quest argument is not QUST.");
        }
        double Variable(string name)
        {
            BeforeEffect();
            var key = _bindings.Variable(name);
            return quests.Variable(key.Quest, key.Index);
        }
        double Objective(IReadOnlyList<FalloutScriptArgument> arguments, bool completed)
        {
            var index = arguments[1].Number;
            if (index < 0 || index > uint.MaxValue || index != Math.Truncate(index))
                throw new InvalidDataException("Activation objective index is invalid.");
            var state = quests.Objective(Quest(arguments[0].Identifier!), (uint)index);
            return (completed ? state.Completed : state.Displayed) ? 1 : 0;
        }
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.Stage(Quest(arguments[0].Identifier!))),
            "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, false)),
            "getobjectivecompleted" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, true)),
            _ => null,
        };
        _program.Execute(Variable, (_, _) => throw new NotSupportedException("Activation assignment requires a reference script instance."),
            (command, arguments) => calls.Add(new(command, arguments.ToArray())), Function);
        return calls;
    }
}
