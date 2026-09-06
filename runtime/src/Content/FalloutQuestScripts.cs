using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceMessage(FalloutFormKey Form, string Title, string Text,
    bool Modal, IReadOnlyList<string> Buttons, FalloutFormKey? Icon = null, uint? DisplaySeconds = null, bool AutomaticTime = false)
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
        if ((bits & ~3u) != 0 || fields.Any(field => field.Signature == "CTDA"))
            throw new NotSupportedException($"MESG {record.FormKey} flags or conditional buttons need an owner.");
        var icon = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        if (icon.Length != 4) throw new InvalidDataException("MESG icon extent is invalid.");
        var iconId = BinaryPrimitives.ReadUInt32LittleEndian(icon.Span);
        var time = fields.SingleOrDefault(field => field.Signature == "TNAM").Data;
        if (time.Length != 0 && time.Length != 4) throw new InvalidDataException("MESG time extent is invalid.");
        var seconds = time.Length == 0 ? (uint?)null : BinaryPrimitives.ReadUInt32LittleEndian(time.Span);
        if ((bits & 3) == 0 && seconds is null or 0) throw new NotSupportedException("Timed MESG has no positive display time.");
        return new(record.FormKey, Text("FULL"), Text("DESC", true), (bits & 1) != 0,
            fields.Where(field => field.Signature == "ITXT").Select(field => FalloutDialogueTopic.Text(field.Data.Span)).ToArray(),
            iconId == 0 ? null : record.Plugin.AdjustFormId(iconId), seconds, (bits & 2) != 0);
    }
}

internal sealed record FalloutQuestScriptSnapshot(FalloutFormKey Quest, FalloutFormKey Script,
    double Remaining, long Executions, string? Error, FalloutQuestScriptClockSnapshot? Clock = null);
internal sealed record FalloutQuestScriptsSnapshot(IReadOnlyList<FalloutQuestScriptSnapshot> Instances,
    IReadOnlyList<FalloutFormKey> Messages, FalloutHudNotificationsSnapshot? Notifications = null)
{
    internal void Validate()
    {
        if (Instances is null || Messages is null)
            throw new InvalidDataException("Saved quest script owners are missing.");
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

internal sealed record FalloutQuestScriptHost(Func<FalloutFormKey, short, Action> PrepareSetStage,
    Func<string, double> PlayerActorValue);

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
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutGlobalState? _globals;
    private readonly Queue<FalloutSourceMessage> _messages = [];
    private readonly FalloutQuestScriptInitialization _initialization;

    internal object State => new
    {
        quests = _instances.Select(instance => new { quest = instance.Quest.FormKey.ToString(), script = instance.Script.FormKey.ToString(), instance.Claimed, instance.Executions, instance.Clock.Remaining, clock = instance.Clock.Capture(), instance.Clock.Interval, instance.Error }).ToArray(),
        unbound = _unbound.Select(pair => new { quest = pair.Key.ToString(), error = pair.Value }).ToArray(),
        inventory = _inventory.Items,
        messages = _messages.ToArray(),
        notifications = _inventory.Notifications.Capture(),
        objectives = _quests.ObjectiveState,
        variables = _quests.VariableState,
        initialization = new { _initialization.EmbeddedQuestScripts, _initialization.Initializations, _initialization.DefaultDelay },
        scheduling = "shared SCPT clocks; claimed GameMode uses the active result-script host; exact MenuMode admission and dynamic quest scheduling unbound",
    };
    internal IReadOnlyList<FalloutCampaignItem> Inventory => _inventory.Items;
    internal bool TryTakeMessage(out FalloutSourceMessage? message) => _messages.TryDequeue(out message);

    internal FalloutQuestScriptsSnapshot Capture(FalloutSourceMessage? displayed = null) => new(
        _instances.Select(instance => new FalloutQuestScriptSnapshot(instance.Quest.FormKey, instance.Script.FormKey,
            instance.Clock.Remaining, instance.Executions, instance.Error, instance.Clock.Capture())).ToArray(),
        (displayed is null ? Enumerable.Empty<FalloutFormKey>() : [displayed.Form]).Concat(_messages.Select(message => message.Form)).ToArray(),
        _inventory.Notifications.Capture());

    internal void Restore(FalloutQuestScriptsSnapshot snapshot)
    {
        if (_instances.Any(instance => instance.Executions != 0 || instance.Clock.Invocations != 0) || _messages.Count != 0)
            throw new InvalidOperationException("Script restoration requires a fresh owner.");
        snapshot.Validate();
        var states = snapshot.Instances.ToDictionary(instance => instance.Quest);
        if (states.Count != _instances.Count || _instances.Any(instance => !states.ContainsKey(instance.Quest.FormKey)))
            throw new InvalidDataException("Saved quest script owners differ from the winning source graph.");
        foreach (var instance in _instances)
        {
            var state = states[instance.Quest.FormKey];
            instance.Clock.Validate(state.Clock!);
            if (state.Script != instance.Script.FormKey)
                throw new InvalidDataException("Saved quest script scheduling is invalid.");
        }
        var messages = snapshot.Messages.Select(form => FalloutSourceMessage.Read(_records.GetEffective(form))).ToArray();
        if (messages.Any(message => !message.Modal)) throw new NotSupportedException("Saved message needs a timed HUD owner.");
        if (snapshot.Notifications is { } notifications) _inventory.Notifications.Restore(notifications);
        foreach (var instance in _instances)
        {
            var state = states[instance.Quest.FormKey];
            instance.Clock.Restore(state.Clock!);
            instance.Executions = state.Executions;
            instance.Error = state.Error;
            if (state.Error is not null) _unbound[instance.Quest.FormKey] = state.Error;
        }
        foreach (var message in messages) _messages.Enqueue(message);
    }

    internal FalloutQuestScripts(FalloutPluginStack records, FalloutQuestState quests, IReadOnlySet<FalloutFormKey> claimedQuests,
        FalloutPlayerInventory inventory, FalloutGlobalState? globals = null, float? defaultProcessingDelay = null)
    {
        _records = records;
        _quests = quests;
        _inventory = inventory;
        _globals = globals;
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
                if ((!claimed && (data.Span[0] & 1) == 0) || !fields.Any(field => field.Signature == "SCRI")) continue;
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
            if (!instance.Clock.Advance((float)seconds)) continue;
            try
            {
                if (gameMode) { Execute(instance, null); ++instance.Executions; }
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
        var writes = new Dictionary<(FalloutFormKey Quest, uint Index), double>();
        var globalWrites = new Dictionary<FalloutFormKey, float>();
        var additions = new Dictionary<FalloutFormKey, FalloutCampaignItem>();
        var messages = new List<FalloutSourceMessage>();
        var notifications = new List<FalloutHudEvent>();
        (FalloutFormKey Quest, short Stage, Action Publish)? stageWrite = null;
        FalloutPluginRecord? TryForm(string name) => instance.Bindings.TryForm(name);
        FalloutPluginRecord Form(string name) => instance.Bindings.Form(name);
        (FalloutFormKey Quest, uint Index) Variable(string name) => instance.Bindings.Variable(name);
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
        double Read(string name)
        {
            if (Global(name) is { } global)
                return globalWrites.TryGetValue(global, out var pending) ? pending : _globals!.Get(global);
            var key = Variable(name);
            return writes.TryGetValue(key, out var value) ? value : _quests.Variable(key.Quest, key.Index);
        }
        void Write(string name, double value)
        {
            if (stageWrite is not null)
                throw new NotSupportedException("Script effects after SetStage require synchronous result-script execution.");
            if (Global(name) is { } global)
            {
                _ = _globals!.Get(global);
                var stored = (float)value;
                if (!float.IsFinite(stored)) throw new InvalidDataException("Script global exceeds Float32 storage.");
                globalWrites[global] = stored;
                return;
            }
            var key = Variable(name);
            _ = _quests.Variable(key.Quest, key.Index);
            if (!double.IsFinite(value)) throw new InvalidDataException("Script variable exceeds its runtime representation.");
            writes[key] = value;
        }
        FalloutPluginRecord Quest(string name)
        {
            var quest = Form(name);
            return quest.Signature == "QUST" ? quest : throw new InvalidDataException("Script quest argument is not QUST.");
        }
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "getstage" => new([FalloutScriptArgumentKind.Identifier], arguments =>
            {
                var quest = Quest(arguments[0].Identifier!).FormKey;
                return stageWrite is { } pending && pending.Quest == quest ? Math.Max(_quests.Stage(quest), pending.Stage) : _quests.Stage(quest);
            }),
            "getsecondspassed" => new([], _ => instance.Clock.Elapsed),
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
        (program ?? instance.Program).Execute(Read, Write, (command, arguments) =>
        {
            if (stageWrite is not null)
                throw new NotSupportedException("Script effects after SetStage require synchronous result-script execution.");
            switch (command.ToLowerInvariant())
            {
                case "short" or "int" or "long" or "float" when arguments.Count == 1: _ = Variable(arguments[0]); break;
                case "showmessage" when arguments.Count == 1:
                    var message = FalloutSourceMessage.Read(Form(arguments[0]));
                    if (message.Modal) messages.Add(message);
                    else notifications.Add(new(FalloutHudEventKind.Message, message.Form, 0, instance.Quest.FormKey, instance.Script.FormKey));
                    break;
                case "player.additem" when arguments.Count is 2 or 3:
                    if (!instance.Bindings.HasPlayerReference)
                        throw new InvalidDataException("Player command has no bound engine reference in SCRO.");
                    var item = Form(arguments[0]);
                    var numericCount = FalloutGameModeProgram.Evaluate([arguments[1]], Read);
                    if (numericCount != Math.Truncate(numericCount)) throw new NotSupportedException("Fractional item additions are unbound.");
                    var count = checked((int)numericCount);
                    if (count <= 0) throw new NotSupportedException("Non-positive item additions are unbound.");
                    var previous = additions.GetValueOrDefault(item.FormKey) ?? _inventory.Item(item.FormKey);
                    var request = new FalloutCampaignInventoryRequest(_records.RuntimeFormId(item.FormKey), arguments[0], item.Signature,
                        checked((previous?.Count ?? 0) + count));
                    additions[item.FormKey] = FalloutCampaignInventoryResolver.Resolve(_records, [request], null).Items.Single();
                    var silent = arguments.Count == 3 ? FalloutGameModeProgram.Evaluate([arguments[2]], Read) : 0;
                    if (silent is not (0 or 1)) throw new NotSupportedException("AddItem silent argument is not a boolean.");
                    if (silent == 0) notifications.Add(new(FalloutHudEventKind.ItemAdded, item.FormKey, count, instance.Quest.FormKey, instance.Script.FormKey));
                    break;
                case "setstage" when arguments.Count == 2:
                    if (host is null) throw new NotSupportedException("SetStage has no result-script execution owner.");
                    var target = Quest(arguments[0]).FormKey;
                    var numericStage = FalloutGameModeProgram.Evaluate([arguments[1]], Read, Function);
                    if (numericStage != Math.Truncate(numericStage)) throw new InvalidDataException("Quest stage is fractional.");
                    var stage = checked((short)numericStage);
                    stageWrite = (target, stage, host.PrepareSetStage(target, stage));
                    break;
                default: throw new NotSupportedException($"Reached script command {command} with {arguments.Count} arguments has no owner.");
            }
        }, Function);
        // Validate the entire reached block before publishing any effects.
        FalloutHudNotifications.Validate(notifications);
        foreach (var (key, value) in writes) _quests.SetVariable(key.Quest, key.Index, value);
        foreach (var (form, value) in globalWrites) _globals!.Set(form, value);
        _inventory.Publish(additions.Values);
        foreach (var message in messages) _messages.Enqueue(message);
        _inventory.Notifications.Publish(notifications);
        stageWrite?.Publish();
    }
}
