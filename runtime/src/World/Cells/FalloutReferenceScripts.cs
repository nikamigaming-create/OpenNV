using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceEffectKind
{
    Conversation, PlayerControls, Message, DefaultActivate, SetStage, SpecialMenu, ReferenceEnable, Texture,
    SayTo, HeadTracking, EvaluatePackages, ScriptPackage, ImageSpace, AddItem, EquipItem, AddNote, RemoveItem,
    ScriptActivate, PipBoyReset, Hardcore, AutoDisplayObjectives, Achievement, LoadingScreenPolicy, CharacterGeneration, Say, PlayerYouth,
    PlayerToddler, PlayerScale, DoorOpenState
}
internal sealed record FalloutReferenceScriptEffect(FalloutReferenceEffectKind Kind, FalloutFormKey Source,
    FalloutFormKey? Target = null, FalloutFormKey? Argument = null, IReadOnlyList<bool>? Controls = null,
    bool Enable = false, short Stage = 0, int Value = 0, FalloutFormKey? Topic = null,
    bool Fade = false, string? NodeName = null, string? TexturePath = null, bool ForceSubtitles = false, float Scale = 1);
internal sealed record FalloutReferenceScriptHost(Func<FalloutFormKey, FalloutFormKey, bool> IsCurrentFurniture,
    Action<FalloutReferenceScriptEffect> Apply, Func<FalloutFormKey, int>? GetButtonPressed = null,
    Func<FalloutFormKey, bool>? IsTalking = null, Func<FalloutFormKey, string, double>? ActorValue = null,
    Func<string, bool>? IsPlayerTagSkill = null, FalloutGlobalState? Globals = null,
    Action<FalloutFormKey, FalloutScriptBindings, string, IReadOnlyList<string>>? Command = null,
    Func<FalloutFormKey, bool>? IsInCombat = null,
    Func<FalloutFormKey, FalloutFormKey, bool>? IsInSameCell = null, FalloutScriptEvents? Events = null,
    Func<FalloutFormKey, FalloutFormKey, float>? Distance = null,
    Func<FalloutFormKey, bool>? IsInInterior = null,
    Action<FalloutFormKey, string, int>? PlayGroup = null,
    Func<FalloutFormKey, string?, bool>? IsAnimPlaying = null,
    Func<int>? PlayerLevel = null, Func<bool>? LocationSpecificLoadScreensOnly = null, Func<bool>? InCharGen = null,
    Func<FalloutFormKey, int>? GetOpenState = null);
internal sealed record FalloutReferenceScriptEventResult(FalloutFormKey Reference, string Event, int Blocks, string? Error,
    string? RecoveredError = null);
internal sealed record FalloutReferenceScriptEvent(string Name, FalloutFormKey? ActionReference = null,
    IReadOnlySet<FalloutFormKey>? TriggerReferences = null, FalloutFormKey? Topic = null,
    IReadOnlySet<FalloutFormKey>? Topics = null, FalloutFormKey? Package = null,
    IReadOnlySet<FalloutFormKey>? Packages = null);

// Dispatches authored object-script blocks against world-owned locals. Functions
// and effects use the same authoritative owners in a lab or a presentation host.
// An unsupported reached operation stops this instance, preserving the executed
// prefix and its error; it never silently advances past the missing behavior.
internal sealed partial class FalloutReferenceScripts(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutQuestState quests, FalloutReferenceScriptHost host)
{
    private sealed record InstanceProgram(FalloutScriptBindings Bindings, IReadOnlyList<FalloutScriptEventProgram> Events);
    private readonly Dictionary<FalloutFormKey, InstanceProgram> _programs = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutScriptEventProgram>> _definitions = [];
    private readonly Dictionary<(FalloutFormKey Owner, FalloutFormKey Script), FalloutScriptBindings> _questBindings = [];

    internal FalloutReferenceScriptEventResult Dispatch(FalloutFormKey reference, string eventName,
        FalloutFormKey? actor = null, double elapsedSeconds = 0, FalloutFormKey? topic = null,
        FalloutFormKey? package = null) =>
        DispatchFrame(reference, [new(eventName, actor, Topic: topic, Package: package)], elapsedSeconds).Single();

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
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("A reference frame has absent or duplicate event admission.");
            var name = FalloutReferencePackageEvents.CanonicalName(item.Name);
            if (!admitted.TryAdd(name, item))
                throw new InvalidDataException("A reference frame has absent or duplicate event admission.");
            if (FalloutReferencePackageEvents.TryKind(name, out _))
            {
                FalloutReferencePackageEvents.RequireActor(records, reference);
                if (item.ActionReference is not null || item.TriggerReferences is not null || item.Topic is not null || item.Topics is not null)
                    throw new InvalidDataException("Package events have a typed PACK identity and no action reference, contact or dialogue topic.");
                if (item.Packages is { } packages)
                {
                    if (item.Package is not null || packages.Count == 0)
                        throw new InvalidDataException("Package events have absent or conflicting source packages.");
                    foreach (var package in packages) FalloutReferencePackageEvents.RequirePackage(records, package);
                    admitted[name] = item with { Packages = new HashSet<FalloutFormKey>(packages) };
                }
                else if (item.Package is { } package) FalloutReferencePackageEvents.RequirePackage(records, package);
                else throw new InvalidDataException("Package event has no typed source package.");
            }
            else if (item.Package is not null || item.Packages is not null)
                throw new InvalidDataException("Package registration belongs to an actor package event.");
            if (name.Equals("SayToDone", StringComparison.OrdinalIgnoreCase))
            {
                if (item.Topics is { } topics)
                {
                    if (item.Topic is not null || topics.Count == 0 || topics.Any(topic => records.GetEffective(topic).Signature != "DIAL"))
                        throw new InvalidDataException("SayToDone has absent, conflicting or untyped source dialogue topics.");
                    admitted[item.Name] = item with { Topics = new HashSet<FalloutFormKey>(topics) };
                }
                else if (item.Topic is not { } topic || records.GetEffective(topic).Signature != "DIAL")
                    throw new InvalidDataException("SayToDone has no typed source dialogue topic.");
            }
            else if (item.Topics is not null) throw new InvalidDataException("Topic registration belongs to SayToDone.");
        }
        var instance = world.Get(reference);
        var recovered = RecoverMissingRead(instance, events) ?? RecoverMissingCommand(instance, events);
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
            new FalloutReferenceScriptEventResult(reference, item.Name, counts[FalloutReferencePackageEvents.CanonicalName(item.Name)], failure,
                recovered?.StartsWith(item.Name + ":", StringComparison.OrdinalIgnoreCase) == true ? recovered : null)).ToArray();
        if (instance.ScriptError is not null || instance.DeletePending || instance.Deleted || events.Count == 0) return Results();
        var runningEvent = "Parse";
        try
        {
            var program = instance.Script is null ? null : Program(instance);
            foreach (var block in program?.Events ?? [])
            {
                var name = FalloutReferencePackageEvents.CanonicalName(block.Event);
                if (!admitted.TryGetValue(name, out var item)) continue;
                runningEvent = block.Event;
                // The engine ignores an OnActivate header argument. OnTrigger
                // filters contact membership but does not establish an action ref.
                var activation = block.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase);
                var trigger = block.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase);
                var packageEvent = FalloutReferencePackageEvents.TryKind(name, out _);
                if (packageEvent)
                {
                    if (block.Filter is null) throw new InvalidDataException("Actor package event requires one source PACK filter.");
                    var package = program!.Bindings.Form(block.Filter);
                    FalloutReferencePackageEvents.RequirePackage(records, package.FormKey);
                    if (!(item.Packages?.Contains(package.FormKey) ?? package.FormKey == item.Package)) continue;
                }
                else if (!activation && block.Filter is not null)
                {
                    if (block.Event.Equals("SayToDone", StringComparison.OrdinalIgnoreCase))
                    {
                        var topic = program!.Bindings.Form(block.Filter);
                        if (topic.Signature != "DIAL") throw new InvalidDataException("SayToDone source filter is not a DIAL form.");
                        if (!(item.Topics?.Contains(topic.FormKey) ?? topic.FormKey == item.Topic)) continue;
                    }
                    else
                    {
                        var filter = program!.Bindings.Reference(block.Filter);
                        if (trigger && item.TriggerReferences is { } contacts ? !contacts.Contains(filter) : filter != item.ActionReference) continue;
                    }
                }
                var actionReference = packageEvent || trigger || block.Event.Equals("OnDeath", StringComparison.OrdinalIgnoreCase)
                    ? null : item.ActionReference;
                Execute(instance.Reference, program!.Bindings, block.Program, actionReference, elapsedSeconds);
                ++counts[name];
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
        program = new(Bindings(records.GetEffective(instance.Reference), script, script.ReadSubrecords()), events);
        _programs.Add(instance.Reference, program);
        return program;
    }

    internal void ExecuteResult(FalloutDialogueInfo info, FalloutFormKey speaker, bool begin)
    {
        var fields = info.Record.ReadSubrecords().ToArray();
        var split = Array.FindIndex(fields, field => field.Signature == "NEXT");
        var selected = begin ? (split < 0 ? fields : fields[..split]) : (split < 0 ? [] : fields[(split + 1)..]);
        var bindings = Bindings(records.GetEffective(info.Quest), info.Record, selected);
        var program = FalloutGameModeProgram.Read("begin Result\n" + (begin ? info.BeginScript : info.EndScript) + "\nend", "Result");
        Execute(speaker, bindings, program, null, 0);
    }

    internal void ExecuteProgram(FalloutPluginRecord owner, FalloutPluginRecord script, FalloutGameModeProgram program, double seconds)
    {
        var key = (owner.FormKey, script.FormKey);
        if (!_questBindings.TryGetValue(key, out var bindings))
            _questBindings.Add(key, bindings = Bindings(owner, script, script.ReadSubrecords()));
        Execute(owner.FormKey, bindings, program, null, seconds);
    }

    internal void ExecutePackageEvent(FalloutPackageEvent program, FalloutFormKey actor)
    {
        program.ValidateScript();
        var reference = records.GetEffective(actor);
        if (reference.Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Package results require a placed actor as their calling reference.");
        Execute(actor, Bindings(reference, program.Package, program.Fields),
            FalloutGameModeProgram.Read("begin Result\n" + program.Source + "\nend", "Result"), null, 0);
        // A reached unsupported topic retains the script prefix. The package
        // lifecycle latches that failure before admitting another event.
        program.RequireEmptyTopic();
    }

    internal void ExecuteStage(FalloutPluginRecord quest, IReadOnlyList<FalloutPluginSubrecord> fields, string source) =>
        Execute(quest.FormKey, Bindings(quest, quest, fields),
            FalloutGameModeProgram.Read("begin Result\n" + source + "\nend", "Result"), null, 0);

    internal IEnumerable<bool> StageSteps(FalloutPluginRecord quest, IReadOnlyList<FalloutPluginSubrecord> fields, string source) =>
        Steps(quest.FormKey, Bindings(quest, quest, fields),
            FalloutGameModeProgram.Read("begin Result\n" + source + "\nend", "Result"), null, 0);

    private FalloutScriptBindings Bindings(FalloutPluginRecord owner, FalloutPluginRecord source,
        IEnumerable<FalloutPluginSubrecord> fields) => new(records, owner, source, fields,
        target => target.Signature is "REFR" or "ACHR" or "ACRE" ? world.Get(target.FormKey).Script?.Record :
            FalloutScriptLocals.AttachedScript(records, target));

    private void Execute(FalloutFormKey source, FalloutScriptBindings bindings, FalloutGameModeProgram program,
        FalloutFormKey? actor, double seconds)
    {
        foreach (var _ in Steps(source, bindings, program, actor, seconds)) { }
    }

    private IEnumerable<bool> Steps(FalloutFormKey source, FalloutScriptBindings bindings, FalloutGameModeProgram program,
        FalloutFormKey? actor, double seconds, FalloutUserFunctionFrame? frame = null, FalloutScriptExecutionBudget? budget = null,
        Action<Func<string, FalloutScriptFunction?>>? inspectFunctions = null)
    {
        budget ??= new();
        var valueStore = world.ScriptValues;
        FalloutScriptEvents Events() => host.Events ?? throw new NotSupportedException("Script event has no process owner.");
        FalloutFormKey? CallingReference() => records.RuntimeFormId(source) == 0x14 ||
            records.GetEffective(source).Signature is "REFR" or "ACHR" or "ACRE" ? source : null;
        string FormName(uint runtimeFormId)
        {
            if (runtimeFormId == 0) return "0";
            var key = records.RuntimeFormKey(runtimeFormId);
            var record = records.GetEffective(key);
            var ids = record.ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
            return ids.Length == 1 ? FalloutDialogueTopic.Text(ids[0].Data.Span) : key.ToString();
        }
        FalloutScriptValue ReadValue(string name)
        {
            if (frame?.Contains(name) == true) return frame.ReadValue(name);
            if (name.Equals("this", StringComparison.OrdinalIgnoreCase))
                return FalloutScriptValue.Form(CallingReference() is { } caller ? records.RuntimeFormId(caller) : 0);
            if (FalloutScriptBindings.IsPlayer(name))
                return FalloutScriptValue.Form(records.RuntimeFormId(bindings.Reference(name)));
            if (bindings.TryForm(name) is { Signature: "GLOB" } global)
                return (host.Globals ?? throw new NotSupportedException("Script global has no state owner.")).Get(global.FormKey);
            if (bindings.TryForm(name) is { } form)
                return FalloutScriptValue.Form(records.RuntimeFormId(form.FormKey));
            var key = bindings.Variable(name);
            var raw = records.GetEffective(key.Owner).Signature == "QUST" ? quests.Variable(key.Owner, key.Index) :
                world.Get(key.Owner).Read(key.Index);
            return valueStore.Read(bindings.VariableKind(name), raw);
        }
        double Read(string name) => ReadValue(name).Number;
        void WriteValue(string name, FalloutScriptValue value)
        {
            if (frame?.Contains(name) == true) { frame.WriteValue(name, value); return; }
            if (bindings.TryForm(name) is { Signature: "GLOB" } global)
            {
                var number = value.Number;
                (host.Globals ?? throw new NotSupportedException("Script global has no state owner.")).Set(global.FormKey, (float)number);
                return;
            }
            var key = bindings.Variable(name);
            var previous = records.GetEffective(key.Owner).Signature == "QUST" ? quests.Variable(key.Owner, key.Index) :
                world.Get(key.Owner).Read(key.Index);
            var raw = valueStore.Write(bindings.VariableKind(name), previous, value, bindings.Source.OwnerPlugin,
                $"{key.Owner}:{key.Index}");
            if (records.GetEffective(key.Owner).Signature == "QUST") quests.SetVariable(key.Owner, key.Index, raw);
            else world.Get(key.Owner).Write(key.Index, raw);
        }
        void DestroyString(string name)
        {
            if (frame?.Contains(name) == true)
            {
                if (frame.Definition.Kind(name) != FalloutScriptLocalKind.String)
                    throw new InvalidDataException("Script destruction target is not a string local.");
                frame.WriteValue(name, FalloutScriptValue.String(string.Empty));
                return;
            }
            var key = bindings.Variable(name);
            var previous = records.GetEffective(key.Owner).Signature == "QUST" ? quests.Variable(key.Owner, key.Index) :
                world.Get(key.Owner).Read(key.Index);
            var cleared = valueStore.DestroyString(bindings.VariableKind(name), previous);
            if (records.GetEffective(key.Owner).Signature == "QUST") quests.SetVariable(key.Owner, key.Index, cleared);
            else world.Get(key.Owner).Write(key.Index, cleared);
        }
        void Write(string name, double value) => WriteValue(name, value);
        bool IsForm(string name) => frame?.Contains(name) == true
            ? frame.Definition.Kind(name) == FalloutScriptLocalKind.Form
            : FalloutScriptBindings.IsPlayer(name) || bindings.TryForm(name) is { Signature: not "GLOB" } ||
                bindings.HasVariable(name) && bindings.VariableKind(name) == FalloutScriptLocalKind.Form;
        bool HasValueOwner(string name) => frame?.Contains(name) == true ||
            name.Equals("this", StringComparison.OrdinalIgnoreCase) || FalloutScriptBindings.IsPlayer(name) ||
            bindings.TryForm(name) is not null || bindings.HasVariable(name);
        var values = new FalloutScriptValueContext(ReadValue, WriteValue, FormName, valueStore.Arrays,
            ReferenceFunction, IsForm, HasValueOwner);
        FalloutFormKey Reference(string name)
        {
            if (FalloutScriptBindings.IsPlayer(name) || bindings.TryForm(name) is not null) return bindings.Reference(name);
            var value = Read(name);
            if (value <= 0 || value > uint.MaxValue || value != Math.Truncate(value))
                throw new InvalidDataException("Script reference variable has no valid form identity.");
            var key = records.RuntimeFormKey((uint)value);
            if ((uint)value != 0x14 && records.GetEffective(key).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Script reference variable is not a placed reference.");
            return key;
        }
        FalloutScriptFunction UserFunction(string command, string name)
        {
            var parts = command.Split('.');
            if (parts.Length > 2) throw new NotSupportedException("Function caller path is unbound.");
            var definition = this.UserFunction(bindings.Form(name).FormKey);
            var kinds = definition.Parameters.Select(parameter => definition.Kind(parameter) switch
            {
                FalloutScriptLocalKind.String => FalloutScriptArgumentKind.String,
                FalloutScriptLocalKind.Form or FalloutScriptLocalKind.Array => FalloutScriptArgumentKind.Value,
                _ => FalloutScriptArgumentKind.Number,
            }).ToArray();
            return FalloutScriptFunction.Typed(kinds, arguments => InvokeFunctionValue(
                definition.Script.FormKey, parts.Length == 2 ? Reference(parts[0]) : CallingReference(),
                arguments.Select(argument => argument.Value).ToArray(), seconds, budget));
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
        FalloutScriptFunction? ReferenceFunction(string name)
        {
            var signature = FunctionFor("this." + name, null);
            return signature is null ? null : FalloutScriptFunction.Reference(signature,
                (caller, arguments) => FunctionFor("this." + name, caller)!.InvokeValue(arguments));
        }
        FalloutScriptFunction? Function(string name) => FunctionFor(name, null);
        FalloutScriptFunction? FunctionFor(string name, FalloutScriptValue? caller)
        {
            var parts = name.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            FalloutFormKey? suppliedTarget = null;
            if (caller is { } value)
            {
                FalloutScriptFunction.RequireReference(value);
                suppliedTarget = value.FormKey(records);
                if (value.Number != 0x14 && records.GetEffective(suppliedTarget.Value).Signature is not ("REFR" or "ACHR" or "ACRE"))
                    throw new InvalidDataException("Reference function caller is not a placed reference.");
            }
            if (parts.Length == 1 && FalloutSoundCommands.Function(records, operation) is { } soundFunction)
                return soundFunction;
            if (parts.Length == 1 && FalloutNumericGameSettingCommands.Function(records, operation) is { } settingFunction)
                return settingFunction;
            if (parts.Length == 1 && operation == "menumode")
                return new([FalloutScriptArgumentKind.OptionalNumber], arguments => world.Menus.Query(arguments.Count == 0 ? null : arguments[0].Number)) { ReadOnly = true };
            if (parts.Length == 1 && operation == "getlocationspecificloadscreensonly")
                return new([], _ => (host.LocationSpecificLoadScreensOnly ??
                    throw new NotSupportedException("Loading-screen policy query has no session owner."))() ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length == 1 && operation == "getinchargen")
                return new([], _ => (host.InCharGen ??
                    throw new NotSupportedException("Character-generation query has no session owner."))() ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length == 1 && valueStore.Arrays.Function(name) is { } arrayFunction) return arrayFunction;
            FalloutFormKey Target() => suppliedTarget ?? (parts.Length == 1 ? source : Reference(parts[0]));
            FalloutFormKey AuxiliaryTarget(IReadOnlyList<FalloutScriptArgument> arguments)
            {
                if (arguments.Count >= 3)
                {
                    if (arguments[2].Value.Kind != FalloutScriptValueKind.Form)
                        throw new InvalidDataException("Auxiliary variable owner is not a form.");
                    var form = arguments[2].Value.Number;
                    if (form <= 0 || form > uint.MaxValue || form != Math.Truncate(form))
                        throw new InvalidDataException("Auxiliary variable owner has no valid form identity.");
                    return records.RuntimeFormKey((uint)form);
                }
                return Target();
            }
            if (parts.Length <= 2 && operation is "auxiliaryvariablegetfloat" or "auxvargetflt" or
                "auxiliaryvariablegettype" or "auxvartype" or "auxiliaryvariablegetref" or "auxvargetref" or
                "auxiliaryvariablegetstring" or "auxvargetstr")
            {
                return operation switch
                {
                    "auxiliaryvariablegetfloat" or "auxvargetflt" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => world.Auxiliary.GetFloat(AuxiliaryTarget(arguments), bindings.Source.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndex(arguments[1].Number) : 0)),
                    "auxiliaryvariablegettype" or "auxvartype" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => world.Auxiliary.GetType(AuxiliaryTarget(arguments), bindings.Source.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndex(arguments[1].Number) : 0)),
                    "auxiliaryvariablegetref" or "auxvargetref" =>
                        FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments =>
                            {
                                var form = world.Auxiliary.GetForm(AuxiliaryTarget(arguments), bindings.Source.OwnerPlugin,
                                    arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndex(arguments[1].Number) : 0);
                                return FalloutScriptValue.Form(form is { } value ? records.RuntimeFormId(value) : 0);
                            }),
                    _ => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                        FalloutScriptArgumentKind.OptionalValue],
                        arguments => FalloutScriptValue.String(world.Auxiliary.GetString(AuxiliaryTarget(arguments),
                             bindings.Source.OwnerPlugin, arguments[0].Text,
                             arguments.Count >= 2 ? AuxiliaryIndex(arguments[1].Number) : 0))),
                };
            }
            if (parts.Length == 1 && operation is "getinifloat" or "getinistring")
            {
                var ini = world.Ini ?? throw new NotSupportedException("INI functions have no user/profile storage owner.");
                return operation == "getinifloat"
                    ? new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalString],
                        arguments => ini.GetFloat(arguments[0].Text, arguments.Count == 2 ? arguments[1].Text : null,
                            bindings.Source.OwnerPlugin))
                    : FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalString],
                        arguments => FalloutScriptValue.String(ini.GetString(arguments[0].Text,
                            arguments.Count == 2 ? arguments[1].Text : null, bindings.Source.OwnerPlugin)));
            }
            if (parts.Length == 1 && operation is "getuifloat" or "getuifloatalt" or "getuistring")
            {
                var ui = world.Ui ?? throw new NotSupportedException("UI functions have no menu-session owner.");
                return operation switch
                {
                    "getuifloat" => new([FalloutScriptArgumentKind.String], arguments => ui.GetFloat(arguments[0].Text)),
                    "getuifloatalt" => new([FalloutScriptArgumentKind.String], arguments => ui.GetFloat(arguments[0].Text, alt: true)),
                    _ => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String],
                        arguments => FalloutScriptValue.String(ui.GetString(arguments[0].Text))),
                };
            }
            if (parts.Length == 1 && FalloutInputControlCommands.IsQuery(operation))
                return FalloutInputControlCommands.Query(operation, world.Controls ?? throw new NotSupportedException("Control queries have no profile input owner."));
            if (parts.Length == 1 && operation is "getnthperkentryvalue1" or "getnthperkentryvalue2" or
                "getnthperkentrytype" or "getnthperkentryfunction")
                return new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
                {
                    var form = arguments[0].Value.FormKey(records);
                    var index = FalloutPerkParameters.Index(arguments[1].Number);
                    return operation switch
                    {
                        "getnthperkentrytype" => records.PerkParameters.Type(form, index),
                        "getnthperkentryfunction" => records.PerkParameters.EntryPoint(form, index),
                        _ => records.PerkParameters.Get(form, index, operation == "getnthperkentryvalue2" ? 1 : 0),
                    };
                });
            if (parts.Length == 1 && operation == "getperkentrycount")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    records.PerkParameters.Count(arguments[0].Value.FormKey(records)));
            if (parts.Length <= 2 && parts[^1].Equals("GetInSameCell", StringComparison.OrdinalIgnoreCase))
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    return (host.IsInSameCell ?? throw new NotSupportedException("GetInSameCell has no spatial owner."))
                        (Target(), Reference(arguments[0].Identifier!)) ? 1 : 0;
                });
            if (parts.Length <= 2 && operation == "getdistance")
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host.Distance ?? throw new NotSupportedException("GetDistance has no spatial owner."))
                    (Target(), Reference(arguments[0].Identifier!)));
            if (parts.Length <= 2 && operation == "isininterior")
                return new([], _ => (host.IsInInterior?.Invoke(Target()) ?? world.IsInInterior(Target())) ? 1 : 0) { ReadOnly = true };
            if (parts.Length <= 2 && operation == "isanimplaying")
                return new([FalloutScriptArgumentKind.OptionalIdentifier], arguments =>
                    (host.IsAnimPlaying ?? throw new NotSupportedException("IsAnimPlaying has no animation owner."))
                    (Target(), arguments.Count == 0 ? null : arguments[0].Identifier) ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length <= 2 && parts[^1].Equals("GetIgnoreCrime", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IgnoresCrime(Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetIgnoreFriendlyHits", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IgnoresFriendlyHits(Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("HasPerk", StringComparison.OrdinalIgnoreCase))
                return new([FalloutScriptArgumentKind.Identifier], args => world.AcquiredPerks(Target()).Contains(bindings.Form(args[0].Identifier!).FormKey) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].ToLowerInvariant() is "getinfaction" or "getfactionrank")
                return new([FalloutScriptArgumentKind.Identifier], args =>
                {
                    var rank = world.ActorFactions(Target()).GetValueOrDefault(bindings.Form(args[0].Identifier!).FormKey, (sbyte)-1);
                    return parts[^1].Equals("GetInFaction", StringComparison.OrdinalIgnoreCase) ? rank >= 0 ? 1 : 0 : rank;
                });
            if (parts.Length == 2 && parts[1].Equals("IsCurrentFurnitureRef", StringComparison.OrdinalIgnoreCase))
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                    host.IsCurrentFurniture(Target(), Reference(arguments[0].Identifier!)) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetDisabled", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsEnabled(Target()) ? 0 : 1) { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getopenstate")
                return new([], _ => host.GetOpenState is not null && world.IsResident(Target()) ? host.GetOpenState(Target()) :
                    world.Get(Target()).DoorMotion?.OpenState ?? throw new NotSupportedException("GetOpenState has no source animation owner."))
                { ReadOnly = true };
            if (parts.Length <= 2 && parts[^1].Equals("GetUnconscious", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsUnconscious(Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetPlayerTeammate", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.Get(Target()).PlayerTeammate ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetDead", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.IsDead(Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("GetMapMarkerVisible", StringComparison.OrdinalIgnoreCase))
                return new([], _ => world.MapMarkerVisibility(Target()));
            if (parts.Length <= 2 && parts[^1].Equals("IsInCombat", StringComparison.OrdinalIgnoreCase))
                return new([], _ => (host.IsInCombat ?? throw new NotSupportedException("IsInCombat has no gameplay owner."))
                    (Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].Equals("IsTalking", StringComparison.OrdinalIgnoreCase))
                return new([], _ => (host.IsTalking ?? throw new NotSupportedException("IsTalking has no speech owner."))
                    (Target()) ? 1 : 0);
            if (parts.Length <= 2 && parts[^1].ToLowerInvariant() is "getav" or "getactorvalue")
                return new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host.ActorValue ?? ((target, value) => world.ActorValue(target, value)))
                    (Target(), arguments[0].Identifier!));
            if (parts.Length <= 2 && operation == "getkiller")
                return FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(
                    world.Get(Target()).Injury?.Killer is { } killer ? records.RuntimeFormId(killer) : 0));
            return name.ToLowerInvariant() switch
            {
                "getgameloaded" => new([], _ => Events().GetGameLoaded(bindings.Source) ? 1 : 0),
                "getgamerestarted" => new([], _ => Events().GetGameRestarted(bindings.Source) ? 1 : 0),
                "getself" or "getselfalt" => FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(
                    CallingReference() is { } reference ? records.RuntimeFormId(reference) : 0)),
                "iskeypressed" => new([FalloutScriptArgumentKind.Number], arguments => Events().IsKeyPressed(checked((int)Index(arguments[0].Number))) ? 1 : 0),
                "isxbox" or "isps3" => new([], _ => 0),
                "iswin32" => new([], _ => 1),
                "getbuttonpressed" => new([], _ => (host.GetButtonPressed ??
                    throw new NotSupportedException("GetButtonPressed has no message result owner."))(
                        CallingReference() is { } caller && records.RuntimeFormId(caller) != 0x14 ? caller : bindings.Source)),
                "getsecondspassed" => new([], _ => seconds),
                "getrandompercent" => new([], _ => valueStore.RandomPercent()),
                "getcurrenttime" => new([], _ => (host.Globals ??
                    throw new NotSupportedException("GetCurrentTime has no simulation clock."))
                    .Get(FalloutGameTimeBindings.Read(records).Hour))
                { ReadOnly = true },
                "isplayertagskill" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host.IsPlayerTagSkill ?? throw new NotSupportedException("Player tag skills have no owner."))(arguments[0].Identifier!) ? 1 : 0),
                "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.Stage(Quest(arguments[0].Identifier!))),
                "getquestrunning" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.IsRunning(Quest(arguments[0].Identifier!)) ? 1 : 0),
                "getquestcompleted" => new([FalloutScriptArgumentKind.Identifier], arguments => quests.IsCompleted(Quest(arguments[0].Identifier!)) ? 1 : 0),
                "getstagedone" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                    quests.StageDone(Quest(arguments[0].Identifier!), checked((short)Index(arguments[1].Number))) ? 1 : 0),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, false)),
                "getobjectivecompleted" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments => Objective(arguments, true)),
                "isactionref" => new([FalloutScriptArgumentKind.Identifier], arguments => actor == bindings.Reference(arguments[0].Identifier!) ? 1 : 0),
                "getactionref" => FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(
                    actor is { } activator ? records.RuntimeFormId(activator) : 0)),
                "abs" => new([FalloutScriptArgumentKind.Number], arguments => Math.Abs(arguments[0].Number)),
                _ => null,
            };
        }
        double Number(string argument) => FalloutGameModeProgram.Evaluate([argument], Read, Function);
        string StringValue(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1]
            : values.Read(token).Text;
        int AuxiliaryIndex(double value) => value >= -1 && value <= int.MaxValue && value == Math.Truncate(value) ?
            (int)value : throw new InvalidDataException("Auxiliary variable index is invalid.");
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
            var target = parts.Length == 1 ? source : parts.Length == 2 ? Reference(parts[0]) :
                throw new NotSupportedException("Script command target path is unbound.");
            if (operation == "call")
            {
                _ = FalloutNvseNumericExpression.EvaluateValue([command, .. arguments], values,
                    Function, UserFunction);
                return;
            }
            if (operation == "setfunctionvalue" && parts.Length == 1)
            {
                if (frame is null) throw new NotSupportedException("SetFunctionValue needs an active function call.");
                frame.ResultValue = FalloutNvseNumericExpression.EvaluateValue(arguments, values,
                    Function, UserFunction);
                return;
            }
            if (parts.Length == 1 && operation == "setnumericgamesetting")
            {
                _ = FalloutNvseNumericExpression.EvaluateValue([command, .. arguments], values,
                    Function, UserFunction);
                return;
            }
            arguments = FalloutGameModeProgram.ResolveCommandArguments(arguments, values, Function, UserFunction);
            var callerPlugin = bindings.Source.OwnerPlugin;
            if (parts.Length == 1 && operation is "triggerscreenblood" or "tsb")
            {
                if (arguments.Count != 1) throw new InvalidDataException("TriggerScreenBlood requires one count.");
                world.ScreenBlood.Trigger(source, FalloutScreenBlood.Count(Number(arguments[0])));
                return;
            }
            if (parts.Length == 1 && operation == "playsound")
            {
                if (arguments.Count is < 1 or > 2) throw new InvalidDataException("PlaySound requires a sound and optional system flag.");
                var sound = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function, UserFunction);
                if (sound.Kind == FalloutScriptValueKind.Number) sound = FalloutScriptValue.Form(sound.Number);
                world.Sounds.Play(source, sound.FormKey(records), arguments.Count == 2 &&
                    FalloutScriptSounds.SystemFlag(Number(arguments[1])));
                return;
            }
            if (parts.Length == 1 && operation is "stopsound" or "setsoundsourcefile" or "getsoundsourcefile")
            {
                FalloutNvseNumericExpression.EvaluateValue([parts[^1], .. arguments], values, Function, UserFunction);
                return;
            }
            if (parts.Length == 1 && operation == "setnoactivationsound")
            {
                if (arguments.Count != 1) throw new InvalidDataException("SetNoActivationSound requires one SOUN form.");
                var sound = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function, UserFunction);
                if (sound.Kind == FalloutScriptValueKind.Number) sound = FalloutScriptValue.Form(sound.Number);
                world.NoActivationSound.Set(sound.FormKey(records));
                return;
            }
            if (parts.Length == 1 && operation == "clearnoactivationsound")
            {
                if (arguments.Count != 0) throw new InvalidDataException("ClearNoActivationSound takes no arguments.");
                world.NoActivationSound.Clear();
                return;
            }
            if (parts.Length == 1 && FalloutInputControlCommands.IsCommand(operation))
            {
                FalloutInputControlCommands.Execute(operation, world.Controls ?? throw new NotSupportedException("Control commands have no profile input owner."),
                    arguments.Select(Number).ToArray());
                return;
            }
            if (parts.Length == 1 && operation is "setnthperkentryvalue1" or "setnthperkentryvalue2")
            {
                if (arguments.Count != 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                var form = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function, UserFunction);
                if (form.Kind == FalloutScriptValueKind.Number) form = FalloutScriptValue.Form(form.Number);
                _ = records.PerkParameters.Set(form.FormKey(records),
                    FalloutPerkParameters.Index(Number(arguments[1])), (float)Number(arguments[2]),
                    operation == "setnthperkentryvalue2" ? 1 : 0);
                return;
            }
            if (parts.Length <= 2 && operation is "setinifloat" or "setinistring")
            {
                var ini = world.Ini ?? throw new NotSupportedException("INI functions have no user/profile storage owner.");
                if (arguments.Count is < 2 or > 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                var key = StringValue(arguments[0]);
                var file = arguments.Count == 3 ? StringValue(arguments[2]) : null;
                if (operation == "setinifloat") ini.SetFloat(key, Number(arguments[1]), file, callerPlugin);
                else ini.SetString(key, StringValue(arguments[1]), file, callerPlugin);
                return;
            }
            if (parts.Length == 1 && operation == "setuifloatgradual")
            {
                var ui = world.Ui ?? throw new NotSupportedException("UI commands have no menu-session owner.");
                if (arguments.Count is < 1 or > 5) throw new InvalidDataException($"{command} has an invalid argument count.");
                _ = ui.SetFloatGradual(StringValue(arguments[0]),
                    arguments.Count >= 2 ? (float)Number(arguments[1]) : null,
                    arguments.Count >= 3 ? (float)Number(arguments[2]) : null,
                    arguments.Count >= 4 ? (float)Number(arguments[3]) : null,
                    arguments.Count == 5 ? checked((int)Index(Number(arguments[4]))) : 0);
                return;
            }
            if (parts.Length == 1 && operation is "setuifloat" or "setuifloatalt" or "setuistring" or
                "setuistringalt" or "setuistringex" or "unloaduicomponent")
            {
                var ui = world.Ui ?? throw new NotSupportedException("UI commands have no menu-session owner.");
                if (operation == "unloaduicomponent")
                {
                    if (arguments.Count != 1) throw new InvalidDataException($"{command} has an invalid argument count.");
                    _ = ui.Unload(StringValue(arguments[0]));
                    return;
                }
                if (arguments.Count < 2 || operation == "setuistringex" && arguments.Count > 3 ||
                    operation != "setuistringex" && arguments.Count != 2)
                    throw new InvalidDataException($"{command} has an invalid argument count.");
                var path = StringValue(arguments[0]);
                var alt = operation is "setuifloatalt" or "setuistringalt";
                if (operation is "setuifloat" or "setuifloatalt") _ = ui.SetFloat(path, (float)Number(arguments[1]), alt);
                else _ = ui.SetString(path, StringValue(arguments[1]), alt,
                    arguments.Count == 3 ? StringValue(arguments[2]) : null);
                return;
            }
            if (parts.Length <= 2 && operation is "auxiliaryvariablesetfloat" or "auxvarsetflt" or
                "auxiliaryvariablesetref" or "auxvarsetref" or "auxiliaryvariablesetstring" or "auxvarsetstr" or
                "auxiliaryvariableerase" or "auxvarerase")
            {
                var auxiliary = world.Auxiliary;
                if (operation is "auxiliaryvariableerase" or "auxvarerase")
                {
                    if (arguments.Count is < 1 or > 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                    var owner = arguments.Count == 3 ? Reference(arguments[2]) : target;
                    auxiliary.Erase(owner, callerPlugin, StringValue(arguments[0]),
                        arguments.Count >= 2 ? AuxiliaryIndex(Number(arguments[1])) : -1);
                }
                else
                {
                    if (arguments.Count is < 2 or > 4) throw new InvalidDataException($"{command} has an invalid argument count.");
                    var name = StringValue(arguments[0]);
                    var owner = arguments.Count == 4 ? Reference(arguments[3]) : target;
                    var index = arguments.Count >= 3 ? AuxiliaryIndex(Number(arguments[2])) : 0;
                    if (operation is "auxiliaryvariablesetfloat" or "auxvarsetflt") auxiliary.SetFloat(owner, callerPlugin, name, Number(arguments[1]), index);
                    else if (operation is "auxiliaryvariablesetref" or "auxvarsetref") auxiliary.SetForm(owner, callerPlugin, name, Reference(arguments[1]), index);
                    else auxiliary.SetString(owner, callerPlugin, name, StringValue(arguments[1]), index);
                }
                return;
            }
            switch (operation)
            {
                case "setopenstate" when arguments.Count == 1:
                    _ = world.Get(target);
                    host.Apply(new(FalloutReferenceEffectKind.DoorOpenState, source, target, Enable: Boolean(arguments[0])));
                    break;
                case "kill" or "killactor":
                    if (arguments.Count > 3) throw new InvalidDataException("KillActor has an invalid argument count.");
                    if (arguments.Count > 1) throw new NotSupportedException("Script death limb/cause parameters have no source owner.");
                    if (records.RuntimeFormId(target) == 0x14) throw new NotSupportedException("Script player death requires the player vitals owner.");
                    var killer = arguments.Count == 0 || Number(arguments[0]) == 0 ? (FalloutFormKey?)null : Reference(arguments[0]);
                    _ = world.KillActor(target, killer,
                        (host.PlayerLevel ?? throw new NotSupportedException("Script death has no player-level owner."))(), host.Globals);
                    break;
                case "playgroup" when arguments.Count == 2:
                    var initialization = Number(arguments[1]);
                    if (initialization != Math.Truncate(initialization) || initialization is < 0 or > 2)
                        throw new InvalidDataException("PlayGroup initialization must be 0, 1 or 2.");
                    (host.PlayGroup ?? throw new NotSupportedException("PlayGroup has no animation owner."))
                        (target, arguments[0].Trim('"'), (int)initialization);
                    break;
                case "sv_destruct" when arguments.Count > 0:
                    foreach (var argument in arguments) DestroyString(argument);
                    break;
                case "setgamemainloopcallback" or "sgmlc" when arguments.Count is >= 2 and <= 4:
                    var register = Boolean(arguments[1]);
                    var loopScript = bindings.Form(arguments[0]);
                    if (loopScript.Signature != "SCPT") throw new InvalidDataException("Main-loop handler is not SCPT.");
                    if (register && this.UserFunction(loopScript.FormKey).Parameters.Count != 0)
                        throw new InvalidDataException("Main-loop callback must have no parameters.");
                    Events().SetMainLoop(loopScript.FormKey, parts.Length == 2 ? target : CallingReference() ?? records.RuntimeFormKey(0x14),
                        register, arguments.Count >= 3 ? checked((int)Index(Number(arguments[2]))) : 1,
                        arguments.Count == 4 ? checked((int)Index(Number(arguments[3]))) : 3);
                    break;
                case "setonkeydowneventhandler" or "setonkeyupeventhandler" when parts.Length == 1 && arguments.Count is 2 or 3:
                    var keyScript = bindings.Form(arguments[0]);
                    if (keyScript.Signature != "SCPT") throw new InvalidDataException("Key handler is not SCPT.");
                    var addKey = Boolean(arguments[1]);
                    if (addKey)
                    {
                        var definition = this.UserFunction(keyScript.FormKey);
                        if (definition.Parameters.Count != 1 || definition.Types[definition.Parameters[0]] is not ("int" or "short" or "long" or "float"))
                            throw new InvalidDataException("Key handler must take one numeric parameter.");
                    }
                    Events().SetKey(keyScript.FormKey, records.RuntimeFormKey(0x14), addKey, operation == "setonkeydowneventhandler",
                        arguments.Count == 3 ? checked((int)Index(Number(arguments[2]))) : null);
                    break;
                case "setjohnnyonrenderupdateeventhandler" or "setonrenderupdateeventhandler" when parts.Length == 1 && arguments.Count is >= 2 and <= 4:
                    var addRender = Boolean(arguments[0]);
                    var renderScript = bindings.Form(arguments[1]);
                    if (renderScript.Signature != "SCPT") throw new InvalidDataException("Render handler is not SCPT.");
                    // Removal only needs the compiled identity. An unsupported
                    // function body must not prevent unregistering its old hook.
                    if (addRender && this.UserFunction(renderScript.FormKey).Parameters.Count != 0)
                        throw new InvalidDataException("Render callback must have no parameters.");
                    Events().SetRender(renderScript.FormKey, addRender,
                        arguments.Count >= 3 ? checked((int)Index(Number(arguments[2]))) : 0,
                        arguments.Count == 4 ? checked((int)Index(Number(arguments[3]))) : 0);
                    break;
                case "resethealth" when arguments.Count == 0:
                    world.ResetHealth(target);
                    break;
                case "matchrace" when arguments.Count == 1:
                    world.MatchRace(target, Reference(arguments[0]));
                    break;
                case "matchfacegeometry" when arguments.Count == 2:
                    var facePercentage = Number(arguments[1]);
                    if (!double.IsFinite(facePercentage) || Math.Truncate(facePercentage) is < int.MinValue or > int.MaxValue)
                        throw new InvalidDataException("Face matching percentage is outside its signed-integer domain.");
                    world.MatchFaceGeometry(target, Reference(arguments[0]), (int)Math.Truncate(facePercentage));
                    break;
                case "restoreav" or "restoreactorvalue" when arguments.Count == 2:
                    world.RestoreActorValue(target, arguments[0], (float)Number(arguments[1]));
                    break;
                case "ignorecrime" when arguments.Count == 1:
                    world.SetActorFlag(target, Boolean(arguments[0]), false);
                    break;
                case "sifh" or "setignorefriendlyhits" when arguments.Count == 1:
                    world.SetActorFlag(target, Boolean(arguments[0]), true);
                    break;
                case "addperk" or "removeperk" when arguments.Count == 1:
                    world.ChangePerk(target, bindings.Form(arguments[0]).FormKey, operation == "addperk");
                    break;
                case "setcs" or "setcombatstyle" when arguments.Count == 1:
                    world.SetCombatStyle(target, bindings.Form(arguments[0]).FormKey);
                    break;
                case "addtofaction" or "addfac" or "setfactionrank" when arguments.Count == 2:
                    var rank = Number(arguments[1]);
                    if (rank != Math.Truncate(rank) || rank is < -1 or > 127) throw new InvalidDataException("Faction rank is invalid.");
                    world.ChangeFaction(target, bindings.Form(arguments[0]).FormKey, (int)rank, operation == "setfactionrank");
                    break;
                case "removefromfaction" when arguments.Count == 1:
                    world.ChangeFaction(target, bindings.Form(arguments[0]).FormKey, -1, false);
                    break;
                case "setunconscious" when arguments.Count == 1:
                    world.SetUnconscious(target, Boolean(arguments[0]));
                    break;
                case "setrestrained" when arguments.Count == 1:
                    world.SetRestrained(target, Boolean(arguments[0]));
                    break;
                case "setplayerteammate" when arguments.Count == 1:
                    world.SetPlayerTeammate(target, Boolean(arguments[0]));
                    break;
                case "moveto" when arguments.Count is >= 1 and <= 4:
                    var moveDestination = Reference(arguments[0]);
                    var moveX = arguments.Count > 1 ? (float)Number(arguments[1]) : 0;
                    var moveY = arguments.Count > 2 ? (float)Number(arguments[2]) : 0;
                    var moveZ = arguments.Count > 3 ? (float)Number(arguments[3]) : 0;
                    if (records.RuntimeFormId(target) == 0x14)
                        world.QueuePlayerMoveTo(source, moveDestination, moveX, moveY, moveZ);
                    else world.MoveTo(target, moveDestination, moveX, moveY, moveZ);
                    break;
                case "short" or "int" or "long" or "float" or "ref" when parts.Length == 1 && arguments.Count == 1:
                    if (frame?.Contains(arguments[0]) != true) _ = bindings.Variable(arguments[0]);
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
                case "setlocationspecificloadscreensonly" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.LoadingScreenPolicy, source, Enable: Boolean(arguments[0])));
                    break;
                case "setinchargen" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.CharacterGeneration, source, Enable: Boolean(arguments[0])));
                    break;
                case "setscale" when parts.Length == 2 && arguments.Count == 1 && records.RuntimeFormId(target) == 0x14:
                    host.Apply(new(FalloutReferenceEffectKind.PlayerScale, source, Target: target,
                        Scale: FalloutScriptSession.PlayerScaleValue(Number(arguments[0]))));
                    break;
                case "setpctoddler" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.PlayerToddler, source,
                        Enable: FalloutScriptSession.PlayerToddlerFlag(Number(arguments[0]))));
                    break;
                case "setpcyoung" when parts.Length == 1 && arguments.Count == 1:
                    host.Apply(new(FalloutReferenceEffectKind.PlayerYouth, source,
                        Enable: FalloutScriptSession.PlayerYouthFlag(Number(arguments[0]))));
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
                case "sayto" when arguments.Count is 2 or 3:
                    var sayTopic = bindings.Form(arguments[1]);
                    if (sayTopic.Signature != "DIAL") throw new InvalidDataException("SayTo topic is not DIAL.");
                    host.Apply(new(FalloutReferenceEffectKind.SayTo, source, target, bindings.Reference(arguments[0]), Topic: sayTopic.FormKey,
                        ForceSubtitles: arguments.Count == 3 && FalloutSayToCommand.SubtitleFlag(Number(arguments[2]))));
                    break;
                case "say" when arguments.Count is 1 or 2:
                    var speechTopic = bindings.Form(arguments[0]);
                    if (speechTopic.Signature != "DIAL") throw new InvalidDataException("Say topic is not DIAL.");
                    host.Apply(new(FalloutReferenceEffectKind.Say, source, target, Topic: speechTopic.FormKey,
                        ForceSubtitles: arguments.Count == 2 && FalloutSayToCommand.SubtitleFlag(Number(arguments[1]))));
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
                case "resetai" when arguments.Count == 0:
                    host.Apply(new(FalloutReferenceEffectKind.EvaluatePackages, source, target, Enable: true));
                    break;
                case "addscriptpackage" when arguments.Count == 1:
                    var package = bindings.Form(arguments[0]);
                    if (package.Signature != "PACK") throw new InvalidDataException("Script package is not PACK.");
                    host.Apply(new(FalloutReferenceEffectKind.ScriptPackage, source, target, package.FormKey));
                    break;
                case "removescriptpackage" when arguments.Count <= 1:
                    // The retail compiler accepts a redundant package argument;
                    // removal concerns the caller's script override, not its base list.
                    host.Apply(new(FalloutReferenceEffectKind.ScriptPackage, source, target));
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
                case "showbartermenu" or "sbm" when arguments.Count <= 1:
                    var discount = arguments.Count == 0 ? 0 : Number(arguments[0]);
                    if (!double.IsFinite(discount) || discount is < -100 or > 100 || discount != Math.Truncate(discount))
                        throw new InvalidDataException("ShowBarterMenu discount must be an integer from -100 through 100.");
                    (host.Command ?? throw new NotSupportedException("ShowBarterMenu has no native menu owner."))(
                        source, bindings, command,
                        [((int)discount).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
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
        if (inspectFunctions is not null) { inspectFunctions(Function); return []; }
        return program.Steps(Read, Write, Call, Function, UserFunction, budget, values);
    }

    private static string StringArgument(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"' &&
        !token[1..^1].Contains('"') ? token[1..^1] : throw new NotSupportedException("Script string expression is unbound.");
}
