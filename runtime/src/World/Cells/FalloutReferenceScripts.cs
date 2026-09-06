using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceEffectKind { Conversation, PlayerControls, Message, DefaultActivate, SetStage }
internal sealed record FalloutReferenceScriptEffect(FalloutReferenceEffectKind Kind, FalloutFormKey Source,
    FalloutFormKey? Target = null, FalloutFormKey? Argument = null, IReadOnlyList<bool>? Controls = null,
    bool Enable = false, short Stage = 0);
internal sealed record FalloutReferenceScriptHost(Func<FalloutFormKey, FalloutFormKey, bool> IsCurrentFurniture,
    Action<FalloutReferenceScriptEffect> Apply);
internal sealed record FalloutReferenceScriptEventResult(FalloutFormKey Reference, string Event, int Blocks, string? Error);

// Dispatches authored object-script blocks against world-owned locals. Functions
// and effects use the same authoritative owners in a lab or a presentation host.
// An unsupported reached operation stops this instance, preserving the executed
// prefix and its error; it never silently advances past the missing behavior.
internal sealed class FalloutReferenceScripts(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutQuestState quests, FalloutReferenceScriptHost host)
{
    private sealed record InstanceProgram(FalloutScriptBindings Bindings, IReadOnlyList<FalloutScriptEventProgram> Events);
    private readonly Dictionary<FalloutFormKey, InstanceProgram> _programs = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutScriptEventProgram>> _definitions = [];

    internal FalloutReferenceScriptEventResult Dispatch(FalloutFormKey reference, string eventName,
        FalloutFormKey? actor = null, double elapsedSeconds = 0)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!world.IsResident(reference)) throw new InvalidOperationException($"Reference {reference} cannot receive {eventName} while its cell is unloaded.");
        var instance = world.Get(reference);
        if (instance.Script is null) return new(reference, eventName, 0, null);
        if (instance.ScriptError is not null) return new(reference, eventName, 0, instance.ScriptError);
        var blocks = 0;
        try
        {
            var program = Program(instance);
            foreach (var block in program.Events)
            {
                if (!block.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)) continue;
                if (block.Filter is not null && program.Bindings.Reference(block.Filter) != actor) continue;
                Execute(instance, program.Bindings, block.Program, actor, elapsedSeconds);
                ++blocks;
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            instance.ScriptError = $"{eventName}: {error.Message}";
        }
        return new(reference, eventName, blocks, instance.ScriptError);
    }

    internal IReadOnlyList<FalloutReferenceScriptEventResult> Advance(double seconds) =>
        world.ResidentInstances.Where(instance => instance.Script is not null)
            .Select(instance => Dispatch(instance.Reference, "GameMode", elapsedSeconds: seconds)).ToArray();

    internal void UnloadCell(FalloutFormKey cell)
    {
        foreach (var key in _programs.Keys.Where(key => world.Get(key).Cell == cell).ToArray()) _programs.Remove(key);
        // Programs are immutable decode products; they may be rebuilt from the
        // winning records. Mutable locals remain exclusively in the world.
        _definitions.Clear();
    }

    private InstanceProgram Program(FalloutReferenceInstance instance)
    {
        if (_programs.TryGetValue(instance.Reference, out var program)) return program;
        var script = instance.Script!.Record;
        if (!_definitions.TryGetValue(script.FormKey, out var events))
        {
            var sources = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
            if (sources.Length != 1) throw new NotSupportedException("Object script needs one available source program.");
            events = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(sources[0].Data.Span));
            _definitions.Add(script.FormKey, events);
        }
        program = new(new(records, records.GetEffective(instance.Reference), script, script.ReadSubrecords()), events);
        _programs.Add(instance.Reference, program);
        return program;
    }

    private void Execute(FalloutReferenceInstance instance, FalloutScriptBindings bindings, FalloutGameModeProgram program,
        FalloutFormKey? actor, double seconds)
    {
        double Read(string name)
        {
            var key = bindings.Variable(name);
            return records.GetEffective(key.Owner).Signature == "QUST" ? quests.Variable(key.Owner, key.Index) :
                world.Get(key.Owner).Read(key.Index);
        }
        void Write(string name, double value)
        {
            var key = bindings.Variable(name);
            if (records.GetEffective(key.Owner).Signature == "QUST") quests.SetVariable(key.Owner, key.Index, value);
            else world.Get(key.Owner).Write(key.Index, value);
        }
        FalloutFormKey Quest(string name)
        {
            var record = bindings.Form(name);
            return record.Signature == "QUST" ? record.FormKey : throw new InvalidDataException("Script quest argument is not QUST.");
        }
        uint Index(double value) => value >= 0 && value <= uint.MaxValue && value == Math.Truncate(value) ?
            (uint)value : throw new InvalidDataException("Script objective index is invalid.");
        double Objective(IReadOnlyList<FalloutScriptArgument> arguments, bool completed)
        {
            var value = quests.Objective(Quest(arguments[0].Identifier!), Index(arguments[1].Number));
            return (completed ? value.Completed : value.Displayed) ? 1 : 0;
        }
        FalloutScriptFunction? Function(string name)
        {
            var parts = name.Split('.');
            if (parts.Length == 2 && parts[1].Equals("IsCurrentFurnitureRef", StringComparison.OrdinalIgnoreCase))
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                    host.IsCurrentFurniture(bindings.Reference(parts[0]), bindings.Reference(arguments[0].Identifier!)) ? 1 : 0);
            return name.ToLowerInvariant() switch
            {
                "getsecondspassed" => new([], _ => seconds),
                "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.Stage(Quest(arguments[0].Identifier!))),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, false)),
                "getobjectivecompleted" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, true)),
                "isactionref" => new([FalloutScriptArgumentKind.Identifier], arguments => actor == bindings.Reference(arguments[0].Identifier!) ? 1 : 0),
                "abs" => new([FalloutScriptArgumentKind.Number], arguments => Math.Abs(arguments[0].Number)),
                _ => null,
            };
        }
        double Number(string argument) => FalloutGameModeProgram.Evaluate([argument], Read, Function);
        bool Boolean(string argument) => Number(argument) switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Script boolean argument is invalid."),
        };
        void Call(string command, IReadOnlyList<string> arguments)
        {
            var parts = command.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            var target = parts.Length == 1 ? instance.Reference : parts.Length == 2 ? bindings.Reference(parts[0]) :
                throw new NotSupportedException("Script command target path is unbound.");
            switch (operation)
            {
                case "setobjectivedisplayed" or "setobjectivecompleted" when parts.Length == 1 && arguments.Count == 3:
                    quests.ApplyObjective(Quest(arguments[0]), Index(Number(arguments[1])),
                        operation == "setobjectivedisplayed", Boolean(arguments[2]));
                    break;
                case "startconversation" when arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.Conversation, instance.Reference, target,
                        bindings.Reference(arguments[0])));
                    break;
                case "enableplayercontrols" or "disableplayercontrols" when parts.Length == 1 && arguments.Count is >= 1 and <= 7:
                    host.Apply(new(FalloutReferenceEffectKind.PlayerControls, instance.Reference,
                        Controls: arguments.Select(Boolean).ToArray(), Enable: operation == "enableplayercontrols"));
                    break;
                case "showmessage" when parts.Length == 1 && arguments.Count == 1:
                    var message = bindings.Form(arguments[0]);
                    if (message.Signature != "MESG") throw new InvalidDataException("ShowMessage target is not MESG.");
                    host.Apply(new(FalloutReferenceEffectKind.Message, instance.Reference, message.FormKey));
                    break;
                case "activate" when arguments.Count == 0:
                    host.Apply(new(FalloutReferenceEffectKind.DefaultActivate, instance.Reference, target, actor));
                    break;
                case "setstage" when parts.Length == 1 && arguments.Count == 2:
                    var stage = Number(arguments[1]);
                    if (stage < 0 || stage > short.MaxValue || stage != Math.Truncate(stage))
                        throw new InvalidDataException("Script quest stage is invalid.");
                    host.Apply(new(FalloutReferenceEffectKind.SetStage, instance.Reference, Quest(arguments[0]), Stage: (short)stage));
                    break;
                default: throw new NotSupportedException($"Reached object-script command {command} ({arguments.Count} arguments) has no owner.");
            }
        }
        program.Execute(Read, Write, Call, Function);
    }
}
