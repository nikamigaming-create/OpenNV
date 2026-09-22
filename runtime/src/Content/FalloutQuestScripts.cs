using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceMessage(FalloutFormKey Form, string Title, string Text,
    bool Modal, IReadOnlyList<string> Buttons, FalloutFormKey? Icon = null, uint? DisplaySeconds = null, bool AutomaticTime = false,
    FalloutMessageRequest? Request = null, bool ConditionalButtons = false, IReadOnlyList<int>? ButtonIndices = null)
{
    internal static FalloutSourceMessage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "MESG") throw new InvalidDataException("ShowMessage target is not MESG.");
        var fields = record.ReadSubrecords().ToArray();
        string Text(string name, bool required = false)
        {
            var matches = fields.Where(field => field.Signature == name).ToArray();
            if (matches.Length > 1 || required && matches.Length != 1) throw new InvalidDataException($"MESG {name} is ambiguous or missing.");
            return matches.Length == 0 ? "" : FalloutDialogueTopic.Text(matches[0].Data.Span);
        }
        var flags = fields.Single(field => field.Signature == "DNAM").Data;
        if (flags.Length != 4) throw new InvalidDataException("MESG flag extent is invalid.");
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(flags.Span);
        if ((bits & ~3u) != 0)
            throw new NotSupportedException($"MESG {record.FormKey} flags need an owner.");
        var icon = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        if (icon.Length != 4) throw new InvalidDataException("MESG icon extent is invalid.");
        var iconId = BinaryPrimitives.ReadUInt32LittleEndian(icon.Span);
        var time = fields.SingleOrDefault(field => field.Signature == "TNAM").Data;
        if (time.Length != 0 && time.Length != 4) throw new InvalidDataException("MESG time extent is invalid.");
        var seconds = time.Length == 0 ? (uint?)null : BinaryPrimitives.ReadUInt32LittleEndian(time.Span);
        if ((bits & 3) == 0 && seconds is null or 0) throw new NotSupportedException("Timed MESG has no positive display time.");
        return new(record.FormKey, Text("FULL"), Text("DESC", true), (bits & 1) != 0,
            fields.Where(field => field.Signature == "ITXT").Select(field => FalloutDialogueTopic.Text(field.Data.Span)).ToArray(),
            iconId == 0 ? null : record.Plugin.AdjustFormId(iconId), seconds, (bits & 2) != 0,
            ConditionalButtons: fields.Any(field => field.Signature == "CTDA"));
    }

    internal FalloutSourceMessage ResolveButtons(FalloutPluginStack records, Func<FalloutCondition, float> evaluate)
    {
        if (!ConditionalButtons) return this;
        var record = records.GetEffective(Form);
        var groups = new List<List<FalloutCondition>>();
        foreach (var field in record.ReadSubrecords())
        {
            if (field.Signature == "ITXT") groups.Add([]);
            if (field.Signature != "CTDA") continue;
            if (groups.Count == 0) throw new InvalidDataException("Message condition precedes its source button.");
            groups[^1].Add(FalloutCondition.Read(record, field.Data.Span));
        }
        if (groups.Count != Buttons.Count) throw new InvalidDataException("Message button/source count differs.");
        var visible = Enumerable.Range(0, groups.Count).Where(index => FalloutCondition.AllPass(groups[index], evaluate)).ToArray();
        if (visible.Length == 0) throw new InvalidOperationException("Message has no eligible source buttons.");
        return this with { Buttons = visible.Select(index => Buttons[index]).ToArray(), ButtonIndices = visible };
    }
}

internal sealed record FalloutQuestScriptSnapshot(FalloutFormKey Quest, FalloutFormKey Script,
    double Remaining, long Executions, string? Error, FalloutQuestScriptClockSnapshot? Clock = null);
internal sealed record FalloutQuestScriptsSnapshot(IReadOnlyList<FalloutQuestScriptSnapshot> Instances,
    IReadOnlyList<FalloutMessageRequest> Messages, FalloutHudNotificationsSnapshot? Notifications = null,
    FalloutMessageResultsSnapshot? MessageResults = null, FalloutScriptSessionSnapshot? Session = null,
    IReadOnlyList<FalloutFormKey>? SaidInfos = null, int ParserVersion = 0,
    FalloutScriptValueStoreSnapshot? Values = null, FalloutAuxiliaryStoreSnapshot? Auxiliary = null)
{
    internal void Validate()
    {
        if (ParserVersion is < 0 or > FalloutGameModeProgram.ParserVersion)
            throw new NotSupportedException("Saved quest script parser version is unsupported.");
        if (Instances is null || Messages is null)
            throw new InvalidDataException("Saved quest script owners are missing.");
        if (Values is { Strings: null })
            throw new InvalidDataException("Saved script value state is missing its string table.");
        Auxiliary?.Validate();
        if (SaidInfos is { } said && (said.Distinct().Count() != said.Count || said.Any(key => key.ObjectId == 0 || string.IsNullOrWhiteSpace(key.OwnerPlugin))))
            throw new InvalidDataException("Saved dialogue history is invalid or duplicated.");
        if (Messages.Count != 0 && MessageResults is null ||
            Messages.Any(message => message is null || message.Sequence == 0 || message.Sequence > MessageResults?.Sequence) ||
            Messages.Select(message => message.Sequence).Distinct().Count() != Messages.Count)
            throw new InvalidDataException("Saved messages have no valid result ownership.");
        var quests = new HashSet<FalloutFormKey>();
        var definitions = new Dictionary<FalloutFormKey, FalloutQuestScriptClockSnapshot>();
        foreach (var instance in Instances)
        {
            if (instance is null || !quests.Add(instance.Quest))
                throw new InvalidDataException("Saved quest script owner is absent or duplicated.");
            if (instance.Clock is null)
                throw new NotSupportedException("Legacy quest scripts have no elapsed/cadence clock owner.");
            instance.Clock.Validate();
            if (definitions.TryGetValue(instance.Script, out var clock) && !clock.HasSameBits(instance.Clock))
                throw new InvalidDataException("Saved shared script clocks disagree.");
            definitions[instance.Script] = instance.Clock;
            if (instance.Remaining != instance.Clock.Remaining || instance.Executions < 0 ||
                instance.Executions > instance.Clock.Invocations)
                throw new InvalidDataException("Saved quest script scheduling is invalid.");
        }
    }
}

internal sealed record FalloutScriptSessionSnapshot(bool Hardcore, bool AutoDisplayObjectives, IReadOnlyList<int> Achievements);
internal sealed class FalloutScriptSession
{
    internal bool Hardcore { get; set; }
    internal bool AutoDisplayObjectives { get; set; }
    private readonly HashSet<int> _achievements = [];
    internal void AddAchievement(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        _achievements.Add(id);
    }
    internal FalloutScriptSessionSnapshot Capture() => new(Hardcore, AutoDisplayObjectives, _achievements.Order().ToArray());
    internal void Restore(FalloutScriptSessionSnapshot state)
    {
        if (state.Achievements is null || state.Achievements.Any(id => id < 0) || state.Achievements.Distinct().Count() != state.Achievements.Count)
            throw new InvalidDataException("Saved script session state is invalid.");
        Hardcore = state.Hardcore; AutoDisplayObjectives = state.AutoDisplayObjectives;
        _achievements.Clear(); _achievements.UnionWith(state.Achievements);
    }
}

internal sealed record FalloutQuestScriptHost(Func<FalloutFormKey, short, Action> PrepareSetStage,
    Func<string, double> PlayerActorValue,
    Action<FalloutPluginRecord, FalloutPluginRecord, FalloutGameModeProgram, double>? ExecuteProgram = null,
    FalloutUserFunctionInvoker? InvokeFunction = null);

internal sealed class FalloutQuestScripts
{
    // Engine-created player reference; it is not a placed record in an ESM.
    private sealed class Instance(FalloutPluginRecord quest, FalloutPluginRecord script, FalloutGameModeProgram program,
        FalloutQuestScriptClock clock, Func<FalloutScriptBindings> createBindings, bool claimed)
    {
        internal readonly FalloutPluginRecord Quest = quest, Script = script;
        internal readonly FalloutGameModeProgram Program = program;
        internal readonly FalloutQuestScriptClock Clock = clock;
        private readonly Lazy<FalloutScriptBindings> _bindings = new(createBindings);
        internal FalloutScriptBindings Bindings => _bindings.Value;
        internal readonly bool Claimed = claimed;
        internal string? Error;
        internal long Executions;
    }

    private readonly FalloutPluginStack _records;
    private readonly FalloutQuestState _quests;
    private readonly List<Instance> _instances = [];
    private readonly Dictionary<FalloutFormKey, string> _unbound = [];
    private readonly List<FalloutFormKey> _newlyParsed = [];
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutGlobalState? _globals;
    private readonly Queue<FalloutSourceMessage> _messages = [];
    private readonly FalloutQuestScriptInitialization _initialization;
    internal FalloutReferenceWorld? References { get; }
    internal FalloutScriptValueStore ScriptValues { get; }
    internal FalloutAuxiliaryStore Auxiliary { get; }
    internal FalloutScriptIniStore? Ini { get; }
    internal FalloutQuestScriptHost? Host { get; set; }
    internal FalloutMessageResults MessageResults { get; } = new();
    internal FalloutScriptSession Session { get; } = new();
    internal FalloutScriptEvents Events { get; }
    internal HashSet<FalloutFormKey> SaidInfos { get; } = [];
    internal double Variable(FalloutFormKey owner, uint index) => References?.ReadVariable(_quests, owner, index) ?? _quests.Variable(owner, index);
    internal void SetVariable(FalloutFormKey owner, uint index, double value)
    {
        if (References is null) _quests.SetVariable(owner, index, value);
        else References.WriteVariable(_quests, owner, index, value);
    }

    internal object State => new
    {
        quests = _instances.Select(instance => new { quest = instance.Quest.FormKey.ToString(), script = instance.Script.FormKey.ToString(), instance.Claimed, instance.Executions, instance.Clock.Remaining, clock = instance.Clock.Capture(), instance.Clock.Interval, instance.Error }).ToArray(),
        unbound = _unbound.Select(pair => new { quest = pair.Key.ToString(), error = pair.Value }).ToArray(),
        newlyParsedOnRestore = _newlyParsed.Select(key => key.ToString()).ToArray(),
        inventory = _inventory.Items,
        messages = _messages.ToArray(),
        messageResults = MessageResults.Capture(),
        notifications = _inventory.Notifications.Capture(),
        session = Session.Capture(),
        events = Events.State,
        strings = ScriptValues.Capture(),
        auxiliary = Auxiliary.State,
        objectives = _quests.ObjectiveState,
        variables = _quests.VariableState,
        initialization = new { _initialization.EmbeddedQuestScripts, _initialization.Initializations, _initialization.DefaultDelay },
        scheduling = "shared SCPT clocks; running quest admission and retained stop/restart clocks; exact retail MenuMode scheduling unverified",
    };
    internal IReadOnlyList<FalloutCampaignItem> Inventory => _inventory.Items;
    internal bool TryTakeMessage(out FalloutSourceMessage? message)
    {
        // Script execution can publish another message before Godot presents
        // the first. Only the request owning the consumptive result slot can
        // accept input. Retain source execution order, not obsolete prompts.
        while (_messages.TryDequeue(out message))
            if (message.Request is { } request && MessageResults.IsPending(request)) return true;
        return false;
    }

    internal void ShowMessage(FalloutFormKey form, FalloutFormKey? script = null, FalloutFormKey? caller = null)
    {
        var message = FalloutSourceMessage.Read(_records.GetEffective(form));
        var owner = caller is { } reference && _records.RuntimeFormId(reference) != 0x14 ? reference :
            script ?? throw new NotSupportedException("ShowMessage has no executing reference or SCPT owner.");
        message = message with { Request = MessageResults.Begin(form, owner) };
        if (message.Modal) _messages.Enqueue(message);
        else _inventory.Notifications.Publish([new(FalloutHudEventKind.Message, message.Form, 0, Script: script)]);
    }

    internal FalloutQuestScriptsSnapshot Capture(FalloutSourceMessage? displayed = null) => new(
        _instances.Select(instance => new FalloutQuestScriptSnapshot(instance.Quest.FormKey, instance.Script.FormKey,
            instance.Clock.Remaining, instance.Executions, instance.Error, instance.Clock.Capture())).ToArray(),
        (displayed is null ? Enumerable.Empty<FalloutMessageRequest>() : [displayed.Request ?? throw new InvalidDataException("Displayed message has no result owner.")])
            .Concat(_messages.Select(message => message.Request!)).Where(MessageResults.IsPending).ToArray(),
        _inventory.Notifications.Capture(), MessageResults.Capture(), Session.Capture(), SaidInfos.OrderBy(key => _records.RuntimeFormId(key)).ToArray(),
        FalloutGameModeProgram.ParserVersion, ScriptValues.Capture(), Auxiliary.CapturePermanent());

    internal void Restore(FalloutQuestScriptsSnapshot snapshot)
    {
        if (_instances.Any(instance => instance.Executions != 0 || instance.Clock.Invocations != 0) || _messages.Count != 0)
            throw new InvalidOperationException("Script restoration requires a fresh owner.");
        snapshot.Validate();
        ScriptValues.Restore(snapshot.Values);
        Auxiliary.RestorePermanent(snapshot.Auxiliary);
        ValidateStringHandles();
        References?.ValidateStringHandles();
        var states = snapshot.Instances.ToDictionary(instance => instance.Quest);
        var owners = _instances.Select(instance => instance.Quest.FormKey).ToHashSet();
        var newlyParsed = _instances.Where(instance => !states.ContainsKey(instance.Quest.FormKey)).ToArray();
        if (states.Keys.Any(key => !owners.Contains(key)) || newlyParsed.Any(instance =>
            !FalloutGameModeProgram.WasRejectedByParser(FalloutDialogueTopic.ScriptText(
                instance.Script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span), snapshot.ParserVersion)))
            throw new InvalidDataException("Saved quest script owners differ from the winning source graph.");
        foreach (var instance in _instances)
        {
            if (!states.TryGetValue(instance.Quest.FormKey, out var state)) continue;
            instance.Clock.Validate(state.Clock!);
            if (state.Script != instance.Script.FormKey)
                throw new InvalidDataException("Saved quest script scheduling is invalid.");
        }
        var messages = snapshot.Messages.Select(request => FalloutSourceMessage.Read(_records.GetEffective(request.Form)) with { Request = request }).ToArray();
        if (messages.Any(message => !message.Modal)) throw new NotSupportedException("Saved message needs a timed HUD owner.");
        if (snapshot.Notifications is { } notifications) _inventory.Notifications.Restore(notifications);
        if (snapshot.MessageResults is { } results) MessageResults.Restore(results);
        if (snapshot.Session is { } session) Session.Restore(session);
        foreach (var info in snapshot.SaidInfos ?? [])
        {
            if (_records.GetEffective(info).Signature != "INFO") throw new InvalidDataException("Saved dialogue history contains a non-INFO source.");
            SaidInfos.Add(info);
        }
        foreach (var instance in _instances)
        {
            if (!states.TryGetValue(instance.Quest.FormKey, out var state)) continue;
            instance.Clock.Restore(state.Clock!);
            instance.Executions = state.Executions;
            instance.Error = state.Error;
            if (state.Error is not null) _unbound[instance.Quest.FormKey] = state.Error;
        }
        // Newly supported programs had no prior invocation. Their original
        // initialization clock is retained; quest locals/progression live in
        // the separately restored quest state and are never reset here.
        _newlyParsed.AddRange(newlyParsed.Select(instance => instance.Quest.FormKey));
        foreach (var message in messages) _messages.Enqueue(message);
    }

    private void ValidateStringHandles()
    {
        foreach (var instance in _instances)
        {
            foreach (var declaration in FalloutScriptLocals.ReadDeclarations(instance.Script).Values)
            {
                if (declaration.Kind != FalloutScriptLocalKind.String) continue;
                ScriptValues.ValidateHandle(Variable(instance.Quest.FormKey, declaration.Index));
            }
        }
    }

    internal FalloutQuestScripts(FalloutPluginStack records, FalloutQuestState quests, IReadOnlySet<FalloutFormKey> claimedQuests,
        FalloutPlayerInventory inventory, FalloutGlobalState? globals = null, float? defaultProcessingDelay = null,
        FalloutReferenceWorld? references = null, FalloutScriptEvents? events = null,
        FalloutScriptStorage? storage = null, FalloutAuxiliaryStore? auxiliary = null)
    {
        _records = records;
        _quests = quests;
        _inventory = inventory;
        _globals = globals;
        References = references;
        ScriptValues = references?.ScriptValues ?? new();
        Auxiliary = references?.Auxiliary ?? storage?.Auxiliary ?? auxiliary ?? new();
        Ini = references?.Ini ?? storage?.Ini;
        Events = events ?? new();
        var defaultDelay = defaultProcessingDelay ?? FalloutInstallationSettings.Read(
            RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Quest script timing needs owned installation settings."))
            .Number("MAIN", "fQuestScriptDelayTime");
        if (!float.IsFinite(defaultDelay)) throw new InvalidDataException("Default quest script delay is invalid.");
        _initialization = new(records, defaultDelay);
        var clocks = new Dictionary<FalloutFormKey, FalloutQuestScriptClock>();
        foreach (var quest in _initialization.QuestOrder)
        {
            var claimed = claimedQuests.Contains(quest.FormKey);
            try
            {
                var fields = quest.ReadSubrecords().ToArray();
                var data = fields.Single(field => field.Signature == "DATA").Data;
                if (data.Length is not (2 or 8)) throw new NotSupportedException("Quest DATA version is unbound.");
                if (!fields.Any(field => field.Signature == "SCRI")) continue;
                var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
                if (script.Signature != "SCPT") throw new InvalidDataException("Quest script is not SCPT.");
                var source = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
                if (source.Length != 1) throw new NotSupportedException("Quest script source is absent or ambiguous.");
                var program = FalloutGameModeProgram.Read(source[0].Data.Span);
                if (!_initialization.Definitions.TryGetValue(script.FormKey, out var definition))
                    throw new NotSupportedException("Attached quest script has no quest-clock declaration.");
                if (!clocks.TryGetValue(script.FormKey, out var clock))
                    clocks.Add(script.FormKey, clock = new(defaultDelay, definition.ProcessingDelay, definition.InitialPhase));
                _instances.Add(new(quest, script, program, clock, () => new(records, quest, script, script.ReadSubrecords()), claimed));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
            { _unbound[quest.FormKey] = error.Message; }
        }
    }

    internal void Advance(double seconds, bool gameMode = true)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var instance in _instances)
        {
            if (instance.Claimed || instance.Error is not null) continue;
            try
            {
                if (!_quests.IsRunning(instance.Quest.FormKey) || !instance.Clock.Advance((float)seconds)) continue;
                if (gameMode) { Execute(instance, Host); ++instance.Executions; }
                instance.Clock.CompleteInvocation();
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            { instance.Error = error.Message; _unbound[instance.Quest.FormKey] = error.Message; }
        }
    }

    internal void AdvanceClaimed(FalloutFormKey quest, double seconds, FalloutQuestScriptHost host)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(seconds));
        var instance = _instances.SingleOrDefault(value => value.Quest.FormKey == quest && value.Claimed) ??
            throw new NotSupportedException($"Claimed quest {quest} has no source program: {_unbound.GetValueOrDefault(quest)}");
        if (instance.Error is not null) throw new NotSupportedException(instance.Error);
        if (!_quests.IsRunning(quest)) return;
        if (!instance.Clock.Advance((float)seconds)) return;
        try
        {
            Execute(instance, host);
            ++instance.Executions;
            instance.Clock.CompleteInvocation();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            instance.Error = error.Message;
            _unbound[quest] = error.Message;
            throw;
        }
    }

    internal void ExecuteClaimedMenu(FalloutFormKey quest, uint menu, FalloutQuestScriptHost host)
    {
        var instance = _instances.SingleOrDefault(value => value.Quest.FormKey == quest && value.Claimed) ??
            throw new NotSupportedException($"Menu event has no claimed quest script owner: {quest}");
        if (instance.Error is not null) throw new NotSupportedException(instance.Error);
        var source = instance.Script.ReadSubrecords().Single(field => field.Signature == "SCTX");
        Execute(instance, host, FalloutGameModeProgram.Read(source.Data.Span, "MenuMode", menu));
    }

    private void Execute(Instance instance, FalloutQuestScriptHost? host, FalloutGameModeProgram? program = null)
    {
        if (host?.ExecuteProgram is { } execute)
        {
            execute(instance.Quest, instance.Script, program ?? instance.Program, instance.Clock.Elapsed);
            return;
        }
        FalloutPluginRecord? TryForm(string name) => instance.Bindings.TryForm(name);
        FalloutPluginRecord Form(string name) => instance.Bindings.Form(name);
        (FalloutFormKey Owner, uint Index) Variable(string name) => instance.Bindings.Variable(name);
        FalloutFormKey? Global(string name)
        {
            if (TryForm(name) is not { Signature: "GLOB" } record) return null;
            if (_globals is null) throw new NotSupportedException($"Script global {name} has no shared state owner.");
            var source = FalloutGlobal.Read(record);
            if (instance.Script.ReadSubrecords().Any(field => field.Signature == "SCVR" &&
                string.Equals(FalloutDialogueTopic.Text(field.Data.Span), name, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException($"Script operand {name} has ambiguous local/global binding.");
            return source.Form;
        }
        string FormName(uint runtimeFormId)
        {
            if (runtimeFormId == 0) return "0";
            var key = _records.RuntimeFormKey(runtimeFormId);
            var record = _records.GetEffective(key);
            var ids = record.ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
            return ids.Length == 1 ? FalloutDialogueTopic.Text(ids[0].Data.Span) : key.ToString();
        }
        FalloutScriptValue ReadValue(string name)
        {
            if (Global(name) is { } global) return _globals!.Get(global);
            if (TryForm(name) is { } form) return FalloutScriptValue.Form(_records.RuntimeFormId(form.FormKey));
            var key = Variable(name);
            return ScriptValues.Read(instance.Bindings.VariableKind(name), this.Variable(key.Owner, key.Index));
        }
        double Read(string name) => ReadValue(name).Number;
        void WriteValue(string name, FalloutScriptValue value)
        {
            if (Global(name) is { } global)
            {
                var stored = (float)value.Number;
                if (!float.IsFinite(stored)) throw new InvalidDataException("Script global exceeds Float32 storage.");
                (_globals ?? throw new InvalidDataException("Script global has no state owner.")).Set(global, stored);
                return;
            }
            var key = Variable(name);
            var previous = this.Variable(key.Owner, key.Index);
            var raw = ScriptValues.Write(instance.Bindings.VariableKind(name), previous, value,
                instance.Bindings.Source.OwnerPlugin);
            SetVariable(key.Owner, key.Index, raw);
        }
        void DestroyString(string name)
        {
            var key = instance.Bindings.Variable(name);
            var raw = this.Variable(key.Owner, key.Index);
            var cleared = ScriptValues.DestroyString(instance.Bindings.VariableKind(name), raw);
            SetVariable(key.Owner, key.Index, cleared);
        }
        void Write(string name, double value) => WriteValue(name, value);
        var values = new FalloutScriptValueContext(ReadValue, WriteValue, FormName);
        FalloutPluginRecord Quest(string name)
        {
            var quest = Form(name);
            return quest.Signature == "QUST" ? quest : throw new InvalidDataException("Script quest argument is not QUST.");
        }
        string StringArgument(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1]
            : values.Read(token).Text;
        double NumberArgument(string token) => FalloutNvseNumericExpression.EvaluateValue([token], values, Function).Number;
        int AuxiliaryIndex(IReadOnlyList<string> arguments, int position, int fallback)
        {
            if (arguments.Count <= position) return fallback;
            var value = NumberArgument(arguments[position]);
            return AuxiliaryIndexValue(value);
        }
        int AuxiliaryIndexValue(double value) => value != Math.Truncate(value) || value < -1 || value > int.MaxValue
            ? throw new InvalidDataException("Auxiliary variable index is invalid.") : (int)value;
        FalloutFormKey AuxiliaryValueOwner(FalloutScriptValue value)
        {
            if (value.Kind != FalloutScriptValueKind.Form || value.Number <= 0 ||
                value.Number > uint.MaxValue || value.Number != Math.Truncate(value.Number))
                throw new InvalidDataException("Auxiliary variable owner has no valid form identity.");
            return _records.RuntimeFormKey((uint)value.Number);
        }
        FalloutFormKey AuxiliaryTarget(string[] parts, IReadOnlyList<FalloutScriptArgument>? arguments = null) =>
            arguments is { Count: >= 3 } ? AuxiliaryValueOwner(arguments[2].Value) : parts.Length == 1
                ? instance.Bindings.HasPlayerReference
                    ? instance.Bindings.Reference("PlayerRef")
                    : _records.RuntimeFormKey(0x14)
                : instance.Bindings.Reference(parts[0]);
        FalloutFormKey ReferenceArgument(string token)
        {
            if (FalloutScriptBindings.IsPlayer(token) || TryForm(token) is not null)
                return instance.Bindings.Reference(token);
            return AuxiliaryValueOwner(NumberArgument(token));
        }
        FalloutScriptFunction? Function(string name)
        {
            var parts = name.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            if (parts.Length <= 2 && operation is "auxiliaryvariablegetfloat" or "auxvargetflt" or
                "auxiliaryvariablegettype" or "auxvartype" or "auxiliaryvariablegetref" or "auxvargetref" or
                "auxiliaryvariablegetstring" or "auxvargetstr")
            {
                return operation switch
                {
                    "auxiliaryvariablegetfloat" or "auxvargetflt" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => Auxiliary.GetFloat(AuxiliaryTarget(parts, arguments), instance.Script.FormKey.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0)),
                    "auxiliaryvariablegettype" or "auxvartype" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => Auxiliary.GetType(AuxiliaryTarget(parts, arguments), instance.Script.FormKey.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0)),
                    "auxiliaryvariablegetref" or "auxvargetref" =>
                        FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments =>
                            {
                                var form = Auxiliary.GetForm(AuxiliaryTarget(parts, arguments), instance.Script.FormKey.OwnerPlugin,
                                    arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0);
                                return FalloutScriptValue.Form(form is { } value ? _records.RuntimeFormId(value) : 0);
                            }),
                    _ => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                        FalloutScriptArgumentKind.OptionalValue],
                        arguments => FalloutScriptValue.String(Auxiliary.GetString(AuxiliaryTarget(parts, arguments),
                            instance.Script.FormKey.OwnerPlugin, arguments[0].Text,
                            arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0))),
                };
            }
            if (parts.Length == 1 && operation is "getinifloat" or "getinistring")
            {
                var ini = Ini ?? throw new NotSupportedException("INI functions have no user/profile storage owner.");
                return operation == "getinifloat"
                    ? new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalString],
                        arguments => ini.GetFloat(arguments[0].Text, arguments.Count == 2 ? arguments[1].Text : null,
                            instance.Script.FormKey.OwnerPlugin))
                    : FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalString],
                        arguments => FalloutScriptValue.String(ini.GetString(arguments[0].Text,
                            arguments.Count == 2 ? arguments[1].Text : null, instance.Script.FormKey.OwnerPlugin)));
            }
            return name.ToLowerInvariant() switch
            {
                "getgameloaded" => new([], _ => Events.GetGameLoaded(instance.Script.FormKey) ? 1 : 0),
                "getgamerestarted" => new([], _ => Events.GetGameRestarted(instance.Script.FormKey) ? 1 : 0),
                "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    var quest = Quest(arguments[0].Identifier!).FormKey;
                    return _quests.Stage(quest);
                }),
                "getsecondspassed" => new([], _ => instance.Clock.Elapsed),
                "getbuttonpressed" => new([], _ => MessageResults.Take(instance.Script.FormKey)),
                "abs" => new([FalloutScriptArgumentKind.Number], arguments => Math.Abs(arguments[0].Number)),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                {
                    var index = arguments[1].Number;
                    if (index != Math.Truncate(index)) throw new InvalidDataException("Objective index is fractional.");
                    return _quests.Objective(Quest(arguments[0].Identifier!).FormKey, checked((uint)index)).Displayed ? 1 : 0;
                }),
                "player.getactorvalue" or "player.getav" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    if (!instance.Bindings.HasPlayerReference) throw new InvalidDataException("Player function has no compiled engine reference.");
                    return host?.PlayerActorValue(arguments[0].Identifier!) ?? throw new NotSupportedException("Player actor values have no gameplay owner.");
                }),
                _ => null,
            };
        }
        void Call(string command, IReadOnlyList<string> rawArguments)
        {
            var parts = command.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            var arguments = FalloutGameModeProgram.ResolveCommandArguments(rawArguments, values, Function);
            var caller = instance.Script.FormKey.OwnerPlugin;
            if (parts.Length <= 2 && operation is "setinifloat" or "setinistring")
            {
                var ini = Ini ?? throw new NotSupportedException("INI functions have no user/profile storage owner.");
                if (arguments.Count is < 2 or > 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                var key = StringArgument(arguments[0]);
                var file = arguments.Count == 3 ? StringArgument(arguments[2]) : null;
                if (operation == "setinifloat") ini.SetFloat(key, NumberArgument(arguments[1]), file, caller);
                else ini.SetString(key, StringArgument(arguments[1]), file, caller);
                return;
            }
            if (parts.Length <= 2 && operation is "auxiliaryvariablesetfloat" or "auxvarsetflt" or
                "auxiliaryvariablesetref" or "auxvarsetref" or "auxiliaryvariablesetstring" or "auxvarsetstr" or
                "auxiliaryvariableerase" or "auxvarerase")
            {
                var target = AuxiliaryTarget(parts);
                if (operation is "auxiliaryvariableerase" or "auxvarerase")
                {
                    if (arguments.Count is < 1 or > 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                    var eraseOwner = arguments.Count == 3 ? ReferenceArgument(arguments[2]) : target;
                    _ = Auxiliary.Erase(eraseOwner, caller, StringArgument(arguments[0]),
                        AuxiliaryIndex(arguments, 1, -1));
                    return;
                }
                if (arguments.Count is < 2 or > 4) throw new InvalidDataException($"{command} has an invalid argument count.");
                var name = StringArgument(arguments[0]);
                var setOwner = arguments.Count == 4 ? ReferenceArgument(arguments[3]) : target;
                var index = AuxiliaryIndex(arguments, 2, 0);
                if (operation is "auxiliaryvariablesetfloat" or "auxvarsetflt")
                    _ = Auxiliary.SetFloat(setOwner, caller, name, NumberArgument(arguments[1]), index);
                else if (operation is "auxiliaryvariablesetref" or "auxvarsetref")
                    _ = Auxiliary.SetForm(setOwner, caller, name, ReferenceArgument(arguments[1]), index);
                else
                    _ = Auxiliary.SetString(setOwner, caller, name, StringArgument(arguments[1]), index);
                return;
            }
            switch (command.ToLowerInvariant())
            {
                case "sv_destruct" when arguments.Count > 0:
                    foreach (var argument in arguments) DestroyString(argument);
                    break;
                case "short" or "int" or "long" or "float" when arguments.Count == 1: _ = Variable(arguments[0]); break;
                case "showmessage" when arguments.Count == 1:
                    ShowMessage(Form(arguments[0]).FormKey, instance.Script.FormKey);
                    break;
                case "startquest" or "stopquest" when arguments.Count == 1:
                    _quests.SetRunning(Quest(arguments[0]).FormKey, command.Equals("startquest", StringComparison.OrdinalIgnoreCase));
                    break;
                case "player.additem" when arguments.Count is 2 or 3:
                    if (!instance.Bindings.HasPlayerReference)
                        throw new InvalidDataException("Player command has no bound engine reference in SCRO.");
                    var item = Form(arguments[0]);
                    var numericCount = FalloutGameModeProgram.Evaluate([arguments[1]], Read);
                    if (numericCount != Math.Truncate(numericCount)) throw new NotSupportedException("Fractional item additions are unbound.");
                    var count = checked((int)numericCount);
                    if (count <= 0) throw new NotSupportedException("Non-positive item additions are unbound.");
                    var previous = _inventory.Item(item.FormKey);
                    var request = new FalloutCampaignInventoryRequest(_records.RuntimeFormId(item.FormKey), arguments[0], item.Signature,
                        checked((previous?.Count ?? 0) + count));
                    var addition = FalloutCampaignInventoryResolver.Resolve(_records, [request], null).Items.Single();
                    var silent = arguments.Count == 3 ? FalloutGameModeProgram.Evaluate([arguments[2]], Read) : 0;
                    if (silent is not (0 or 1)) throw new NotSupportedException("AddItem silent argument is not a boolean.");
                    _inventory.Publish([addition]);
                    if (silent == 0) _inventory.Notifications.Publish([new(FalloutHudEventKind.ItemAdded, item.FormKey, count, instance.Quest.FormKey, instance.Script.FormKey)]);
                    break;
                case "setstage" when arguments.Count == 2:
                    if (host is null) throw new NotSupportedException("SetStage has no result-script execution owner.");
                    var target = Quest(arguments[0]).FormKey;
                    var numericStage = FalloutGameModeProgram.Evaluate([arguments[1]], Read, Function);
                    if (numericStage != Math.Truncate(numericStage)) throw new InvalidDataException("Quest stage is fractional.");
                    var stage = checked((short)numericStage);
                    host.PrepareSetStage(target, stage)();
                    break;
                default: throw new NotSupportedException($"Reached script command {command} with {arguments.Count} arguments has no owner.");
            }
        }
        (program ?? instance.Program).Execute(Read, Write, Call, Function, values: values);
        // Each reached operation publishes in source order. A later failure
        // retains the executed prefix, including consumptive message results
        // and nested SetStage scripts. Retrying a failed instance is forbidden.
    }
}
