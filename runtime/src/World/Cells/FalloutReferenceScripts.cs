using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceEffectKind
{
    Conversation, PlayerControls, Message, DefaultActivate, SetStage, SpecialMenu, ReferenceEnable, Texture,
    SayTo, HeadTracking, EvaluatePackages, ScriptPackage, ImageSpace, AddItem, EquipItem, AddNote, RemoveItem,
    ScriptActivate, PipBoyReset, Hardcore, AutoDisplayObjectives, Achievement
}
internal sealed record FalloutReferenceScriptEffect(FalloutReferenceEffectKind Kind, FalloutFormKey Source,
    FalloutFormKey? Target = null, FalloutFormKey? Argument = null, IReadOnlyList<bool>? Controls = null,
    bool Enable = false, short Stage = 0, int Value = 0, FalloutFormKey? Topic = null,
    bool Fade = false, string? NodeName = null, string? TexturePath = null);
internal sealed record FalloutReferenceScriptHost(Func<FalloutFormKey, FalloutFormKey, bool> IsCurrentFurniture,
    Action<FalloutReferenceScriptEffect> Apply, Func<FalloutFormKey, int>? GetButtonPressed = null,
    Func<FalloutFormKey, bool>? IsTalking = null, Func<FalloutFormKey, string, double>? ActorValue = null,
    Func<string, bool>? IsPlayerTagSkill = null, FalloutGlobalState? Globals = null,
    Action<FalloutFormKey, FalloutScriptBindings, string, IReadOnlyList<string>>? Command = null);
internal sealed record FalloutReferenceScriptEventResult(FalloutFormKey Reference, string Event, int Blocks, string? Error);
internal sealed record FalloutReferenceScriptEvent(string Name, FalloutFormKey? ActionReference = null,
    IReadOnlySet<FalloutFormKey>? TriggerReferences = null);

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
    private readonly Dictionary<(FalloutFormKey Owner, FalloutFormKey Script), FalloutScriptBindings> _questBindings = [];

    internal FalloutReferenceScriptEventResult Dispatch(FalloutFormKey reference, string eventName,
        FalloutFormKey? actor = null, double elapsedSeconds = 0) =>
        DispatchFrame(reference, [new(eventName, actor)], elapsedSeconds).Single();

    internal FalloutReferenceScriptEventResult Activate(FalloutFormKey reference, FalloutFormKey actor) =>
        Dispatch(reference, "OnActivate", actor);

    // Event admission and script block order are different things. A frame can
    // admit a contact event and GameMode together; execute their blocks in the
    // authored order, never in the order callbacks arrived from presentation.
    internal IReadOnlyList<FalloutReferenceScriptEventResult> DispatchFrame(FalloutFormKey reference,
        IReadOnlyList<FalloutReferenceScriptEvent> events, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!world.IsResident(reference)) throw new InvalidOperationException($"Reference {reference} cannot receive events while its cell is unloaded.");
        var admitted = new Dictionary<string, FalloutReferenceScriptEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in events)
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || !admitted.TryAdd(item.Name, item))
                throw new InvalidDataException("A reference frame has absent or duplicate event admission.");
        var instance = world.Get(reference);
        // A failed attempt cannot run again on its GameMode clock. A new
        // activation or contact entry is an explicit new event and may retry
        // the source program, including its guards and already-applied prefix.
        // Never skip the failed instruction or silently continue beyond it.
        if (instance.ScriptError is { } previousError && events.Any(item =>
            item.Name is "OnActivate" or "OnTriggerEnter" && previousError.StartsWith(item.Name + ":", StringComparison.OrdinalIgnoreCase)))
            instance.ScriptError = null;
        var counts = admitted.Keys.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var failure = instance.ScriptError;
        IReadOnlyList<FalloutReferenceScriptEventResult> Results() => events.Select(item =>
            new FalloutReferenceScriptEventResult(reference, item.Name, counts[item.Name], failure)).ToArray();
        if (instance.ScriptError is not null || instance.DeletePending || instance.Deleted || events.Count == 0) return Results();
        var runningEvent = "Parse";
        try
        {
            var program = instance.Script is null ? null : Program(instance);
            foreach (var block in program?.Events ?? [])
            {
                if (!admitted.TryGetValue(block.Event, out var item)) continue;
                runningEvent = block.Event;
                // The engine ignores an OnActivate header argument. OnTrigger
                // filters contact membership but does not establish an action ref.
                var activation = block.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase);
                var trigger = block.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase);
                if (!activation && block.Filter is not null)
                {
                    var filter = program!.Bindings.Reference(block.Filter);
                    if (trigger && item.TriggerReferences is { } contacts ? !contacts.Contains(filter) : filter != item.ActionReference) continue;
                }
                Execute(instance.Reference, program!.Bindings, block.Program, trigger ? null : item.ActionReference, elapsedSeconds);
                ++counts[block.Event];
            }
            if (admitted.TryGetValue("OnActivate", out var activationEvent) && counts["OnActivate"] == 0)
            {
                runningEvent = "OnActivate";
                host.Apply(new(FalloutReferenceEffectKind.DefaultActivate, reference, reference, activationEvent.ActionReference));
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            failure = $"{runningEvent}: {error.Message}";
            // Failed native actions do not poison an absent source program.
            // Authored programs still retain their failure and executed prefix.
            if (instance.Script is not null) instance.ScriptError = failure;
        }
        return Results();
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

    internal void ExecuteResult(FalloutDialogueInfo info, FalloutFormKey speaker, bool begin)
    {
        var fields = info.Record.ReadSubrecords().ToArray();
        var split = Array.FindIndex(fields, field => field.Signature == "NEXT");
        var selected = begin ? (split < 0 ? fields : fields[..split]) : (split < 0 ? [] : fields[(split + 1)..]);
        var bindings = new FalloutScriptBindings(records, records.GetEffective(info.Quest), info.Record, selected);
        var program = FalloutGameModeProgram.Read("begin Result\n" + (begin ? info.BeginScript : info.EndScript) + "\nend", "Result");
        Execute(speaker, bindings, program, null, 0);
    }

    internal void ExecuteProgram(FalloutPluginRecord owner, FalloutPluginRecord script, FalloutGameModeProgram program, double seconds)
    {
        var key = (owner.FormKey, script.FormKey);
        if (!_questBindings.TryGetValue(key, out var bindings))
            _questBindings.Add(key, bindings = new(records, owner, script, script.ReadSubrecords()));
        Execute(owner.FormKey, bindings, program, null, seconds);
    }

    internal void ExecuteStage(FalloutPluginRecord quest, IReadOnlyList<FalloutPluginSubrecord> fields, string source) =>
        Execute(quest.FormKey, new(records, quest, quest, fields),
            FalloutGameModeProgram.Read("begin Result\n" + source + "\nend", "Result"), null, 0);

    internal IEnumerable<bool> StageSteps(FalloutPluginRecord quest, IReadOnlyList<FalloutPluginSubrecord> fields, string source) =>
        Steps(quest.FormKey, new(records, quest, quest, fields),
            FalloutGameModeProgram.Read("begin Result\n" + source + "\nend", "Result"), null, 0);

    private void Execute(FalloutFormKey source, FalloutScriptBindings bindings, FalloutGameModeProgram program,
        FalloutFormKey? actor, double seconds)
    {
        foreach (var _ in Steps(source, bindings, program, actor, seconds)) { }
    }

    private IEnumerable<bool> Steps(FalloutFormKey source, FalloutScriptBindings bindings, FalloutGameModeProgram program,
        FalloutFormKey? actor, double seconds)
    {
        double Read(string name)
        {
            if (bindings.TryForm(name) is { Signature: "GLOB" } global)
                return (host.Globals ?? throw new NotSupportedException("Script global has no state owner.")).Get(global.FormKey);
            var key = bindings.Variable(name);
            return records.GetEffective(key.Owner).Signature == "QUST" ? quests.Variable(key.Owner, key.Index) :
                world.Get(key.Owner).Read(key.Index);
        }
        void Write(string name, double value)
        {
            if (bindings.TryForm(name) is { Signature: "GLOB" } global)
            {
                (host.Globals ?? throw new NotSupportedException("Script global has no state owner.")).Set(global.FormKey, (float)value);
                return;
            }
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
            if (parts.Length <= 2 && parts[^1].Equals("GetDisabled", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsEnabled(parts.Length == 1 ? source : bindings.Reference(parts[0])) ? 0 : 1);
            if (parts.Length <= 2 && parts[^1].Equals("GetUnconscious", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsUnconscious(parts.Length == 1 ? source : bindings.Reference(parts[0])) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetDead", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsDead(parts.Length == 1 ? source : bindings.Reference(parts[0])) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetMapMarkerVisible", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.MapMarkerVisibility(parts.Length == 1 ? source : bindings.Reference(parts[0])));
            if (parts.Length <= 2 && parts[^1].Equals("IsTalking", StringComparison.OrdinalIgnoreCase))
                return new([], _ => (host.IsTalking ?? throw new NotSupportedException("IsTalking has no speech owner."))
                    (parts.Length == 1 ? source : bindings.Reference(parts[0])) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].ToLowerInvariant() is "getav" or "getactorvalue")
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host.ActorValue ?? ((target, value) => world.ActorValue(target, value)))
                    (parts.Length == 1 ? source : bindings.Reference(parts[0]), arguments[0].Identifier!));
            return name.ToLowerInvariant() switch
            {
                "isxbox" or "isps3" => new([], _ => 0),
                "iswin32" => new([], _ => 1),
                "getbuttonpressed" => new([], _ => (host.GetButtonPressed ??
                    throw new NotSupportedException("GetButtonPressed has no message result owner."))(
                        records.GetEffective(source).Signature == "QUST" ?
                            FalloutScriptLocals.AttachedScript(records, records.GetEffective(source))!.FormKey : source)),
                "getsecondspassed" => new([], _ => seconds),
                "isplayertagskill" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host.IsPlayerTagSkill ?? throw new NotSupportedException("Player tag skills have no owner."))(arguments[0].Identifier!) ? 1 : 0),
                "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.Stage(Quest(arguments[0].Identifier!))),
                "getquestrunning" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.IsRunning(Quest(arguments[0].Identifier!)) ? 1 : 0),
                "getstagedone" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                    quests.StageDone(Quest(arguments[0].Identifier!), checked((short)Index(arguments[1].Number))) ? 1 : 0),
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
            var target = parts.Length == 1 ? source : parts.Length == 2 ? bindings.Reference(parts[0]) :
                throw new NotSupportedException("Script command target path is unbound.");
            switch (operation)
            {
                case "setunconscious" when arguments.Count == 1:
                    world.SetUnconscious(target, Boolean(arguments[0]));
                    break;
                case "short" or "int" or "long" or "float" or "ref" when parts.Length == 1 && arguments.Count == 1:
                    _ = bindings.Variable(arguments[0]);
                    break;
                case "startquest" or "stopquest" when parts.Length == 1 && arguments.Count == 1:
                    quests.SetRunning(Quest(arguments[0]), operation == "startquest");
                    break;
                case "forceactivequest" when parts.Length == 1 && arguments.Count == 1:
                    quests.ForceActive(Quest(arguments[0]));
                    break;
                case "setdestroyed" when arguments.Count == 1:
                    world.Get(target).Destroyed = Boolean(arguments[0]);
                    break;
                case "markfordelete" when arguments.Count == 0:
                    // Retain the tombstone through save/reload. Physical removal
                    // is deferred to the reference's residency teardown.
                    if (!world.Get(target).Deleted) world.Get(target).DeletePending = true;
                    break;
                case "resetpipboymanager" when parts.Length == 1 && arguments.Count == 0:
                    host.Apply(new(FalloutReferenceEffectKind.PipBoyReset, source));
                    break;
                case "sethardcore" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.Hardcore, source, Enable: Boolean(arguments[0])));
                    break;
                case "autodisplayobjectives" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.AutoDisplayObjectives, source, Enable: Boolean(arguments[0])));
                    break;
                case "addachievement" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.Achievement, source, Value: checked((int)Index(Number(arguments[0])))));
                    break;
                case "removeitem" when arguments.Count is 2 or 3:
                    var removed = Number(arguments[1]);
                    if (removed <= 0 || removed > int.MaxValue || removed != Math.Truncate(removed)) throw new InvalidDataException("RemoveItem count must be a positive integer.");
                    host.Apply(new(FalloutReferenceEffectKind.RemoveItem, source, target, bindings.Form(arguments[0]).FormKey,
                        Value: (int)removed, Enable: arguments.Count == 3 && Boolean(arguments[2])));
                    break;
                case "additem" when arguments.Count is 2 or 3:
                    var quantity = Number(arguments[1]);
                    if (quantity <= 0 || quantity > int.MaxValue || quantity != Math.Truncate(quantity)) throw new InvalidDataException("AddItem count must be a positive integer.");
                    host.Apply(new(FalloutReferenceEffectKind.AddItem, source, target, bindings.Form(arguments[0]).FormKey,
                        Value: (int)quantity, Enable: arguments.Count == 3 && Boolean(arguments[2])));
                    break;
                case "equipitem" when arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.EquipItem, source, target, bindings.Form(arguments[0]).FormKey));
                    break;
                case "addnote" when parts.Length == 1 && arguments.Count == 1:
                    var note = bindings.Form(arguments[0]);
                    if (note.Signature != "NOTE") throw new InvalidDataException("AddNote target is not NOTE.");
                    host.Apply(new(FalloutReferenceEffectKind.AddNote, source, note.FormKey));
                    break;
                case "sayto" when arguments.Count == 2:
                    var sayTopic = bindings.Form(arguments[1]);
                    if (sayTopic.Signature != "DIAL") throw new InvalidDataException("SayTo topic is not DIAL.");
                    host.Apply(new(FalloutReferenceEffectKind.SayTo, source, target, bindings.Reference(arguments[0]), Topic: sayTopic.FormKey));
                    break;
                case "look" when arguments.Count is 1 or 2:
                    if (arguments.Count == 2 && Number(arguments[1]) != 0) throw new NotSupportedException("Whole-body Look is unbound.");
                    host.Apply(new(FalloutReferenceEffectKind.HeadTracking, source, target, bindings.Reference(arguments[0])));
                    break;
                case "stoplook" when arguments.Count <= 1:
                    host.Apply(new(FalloutReferenceEffectKind.HeadTracking, source, target));
                    break;
                case "evp" or "evaluatepackage" when arguments.Count <= 1:
                    host.Apply(new(FalloutReferenceEffectKind.EvaluatePackages, source, target, Enable: arguments.Count == 1 && Boolean(arguments[0])));
                    break;
                case "addscriptpackage" when arguments.Count == 1:
                    var package = bindings.Form(arguments[0]);
                    if (package.Signature != "PACK") throw new InvalidDataException("Script package is not PACK.");
                    host.Apply(new(FalloutReferenceEffectKind.ScriptPackage, source, target, package.FormKey));
                    break;
                case "applyimagespacemodifier" or "imod" or "removeimagespacemodifier" or "rimod" when
                    parts.Length == 1 && (arguments.Count == 1 || arguments.Count == 2 && arguments[1] == "*"):
                    var modifier = bindings.Form(arguments[0]);
                    if (modifier.Signature != "IMAD") throw new InvalidDataException("Image-space modifier is not IMAD.");
                    host.Apply(new(FalloutReferenceEffectKind.ImageSpace, source, modifier.FormKey,
                        Enable: operation is "applyimagespacemodifier" or "imod"));
                    break;
                case "setav" or "setactorvalue" or "modav" or "modactorvalue" or "forceav" or "forceactorvalue" when arguments.Count == 2:
                    world.ChangeActorValue(target, arguments[0], operation, (float)Number(arguments[1]));
                    break;
                case "enable" or "disable" when arguments.Count <= 1:
                    var enable = operation == "enable";
                    var fade = arguments.Count == 1 ? Boolean(arguments[0]) : enable;
                    if (world.SetEnabled(target, enable, fade))
                        host.Apply(new(FalloutReferenceEffectKind.ReferenceEnable, source, target, Enable: enable, Fade: fade));
                    break;
                case "swaptexture" or "swaptextureonref" when parts.Length == 1 && arguments.Count == 3:
                    host.Apply(new(FalloutReferenceEffectKind.Texture, source, bindings.Reference(arguments[0]),
                        NodeName: StringArgument(arguments[1]), TexturePath: StringArgument(arguments[2])));
                    break;
                case "showmap" when parts.Length == 1 && arguments.Count is 1 or 2:
                    world.ShowMap(bindings.Reference(arguments[0]), arguments.Count == 2 && Boolean(arguments[1]));
                    break;
                case "setobjectivedisplayed" or "setobjectivecompleted" when parts.Length == 1 && arguments.Count == 3:
                    quests.ApplyObjective(Quest(arguments[0]), Index(Number(arguments[1])),
                        operation == "setobjectivedisplayed", Boolean(arguments[2]));
                    break;
                case "startconversation" when arguments.Count is 1 or 2:
                    var topic = arguments.Count == 2 ? bindings.Form(arguments[1]) : null;
                    if (topic is not null && topic.Signature != "DIAL") throw new InvalidDataException("StartConversation topic is not DIAL.");
                    host.Apply(new(FalloutReferenceEffectKind.Conversation, source, target,
                        bindings.Reference(arguments[0]), Topic: topic?.FormKey));
                    break;
                case "enableplayercontrols" or "disableplayercontrols" when parts.Length == 1 && arguments.Count <= 7:
                    host.Apply(new(FalloutReferenceEffectKind.PlayerControls, source,
                        Controls: arguments.Select(Boolean).ToArray(), Enable: operation == "enableplayercontrols"));
                    break;
                case "showmessage" when parts.Length == 1 && arguments.Count is 1 or 2:
                    var message = bindings.Form(arguments[0]);
                    if (message.Signature != "MESG") throw new InvalidDataException("ShowMessage target is not MESG.");
                    if (arguments.Count == 2) _ = Number(arguments[1]); // Formatting argument is unused when the source has no substitution.
                    if (FalloutSourceMessage.Read(message).Text.Contains('%')) throw new NotSupportedException("ShowMessage formatting is unbound.");
                    host.Apply(new(FalloutReferenceEffectKind.Message, source, message.FormKey));
                    break;
                case "showlovetestermenuparams" when parts.Length == 1 && arguments.Count == 1:
                    var total = checked((int)Index(Number(arguments[0])));
                    host.Apply(new(FalloutReferenceEffectKind.SpecialMenu, source, Value: total));
                    break;
                case "activate" when arguments.Count == 0:
                    host.Apply(new(FalloutReferenceEffectKind.DefaultActivate, source, target, actor));
                    break;
                case "activate" when arguments.Count is 1 or 2:
                    host.Apply(new(FalloutReferenceEffectKind.ScriptActivate, source, target, bindings.Reference(arguments[0]),
                        Enable: arguments.Count == 2 && Boolean(arguments[1])));
                    break;
                case "setstage" when parts.Length == 1 && arguments.Count == 2:
                    var stage = Number(arguments[1]);
                    if (stage < 0 || stage > short.MaxValue || stage != Math.Truncate(stage))
                        throw new InvalidDataException("Script quest stage is invalid.");
                    host.Apply(new(FalloutReferenceEffectKind.SetStage, source, Quest(arguments[0]), Stage: (short)stage));
                    break;
                default:
                    if (host.Command is null) throw new NotSupportedException($"Reached object-script command {command} ({arguments.Count} arguments) has no owner.");
                    host.Command(source, bindings, command, arguments);
                    break;
            }
        }
        return program.Steps(Read, Write, Call, Function);
    }

    private static string StringArgument(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"' &&
        !token[1..^1].Contains('"') ? token[1..^1] : throw new NotSupportedException("Script string expression is unbound.");
}
