using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

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
    double Remaining, long Executions, string? Error, FalloutQuestScriptClockSnapshot? Clock = null,
    FalloutQuestScriptPendingCommand? PendingCommand = null,
    IReadOnlyList<FalloutQuestScriptContinuationReceipt>? Continuations = null);
internal sealed record FalloutQuestScriptsSnapshot(IReadOnlyList<FalloutQuestScriptSnapshot> Instances,
    IReadOnlyList<FalloutMessageRequest> Messages, FalloutHudNotificationsSnapshot? Notifications = null,
    FalloutMessageResultsSnapshot? MessageResults = null, FalloutScriptSessionSnapshot? Session = null,
    IReadOnlyList<FalloutFormKey>? SaidInfos = null, int ParserVersion = 0,
    FalloutScriptValueStoreSnapshot? Values = null, FalloutAuxiliaryStoreSnapshot? Auxiliary = null,
    FalloutChallengesSnapshot? Challenges = null, FalloutRadioStationsSnapshot? Radio = null)
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
        Challenges?.Validate();
        Radio?.Validate();
        Session?.NoActivationSound?.Validate();
        FalloutQuestObjectFlags.ValidateSnapshot(Session?.QuestObjects);
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
            instance.PendingCommand?.Validate(instance.Error);
            foreach (var receipt in instance.Continuations ?? []) receipt.Validate(instance.Clock.Invocations);
        }
    }
}

internal sealed record FalloutScriptSessionSnapshot(bool Hardcore, bool AutoDisplayObjectives, IReadOnlyList<int> Achievements,
    bool LocationSpecificLoadScreensOnly = false, bool InCharGen = false,
    FalloutPlayerScriptPackageSnapshot? PlayerPackage = null, FalloutNoActivationSoundSnapshot? NoActivationSound = null,
    bool PlayerYoung = false, bool PlayerToddler = false, float PlayerScale = 1,
    IReadOnlyList<FalloutQuestObjectFlagSnapshot>? QuestObjects = null);
internal sealed class FalloutScriptSession(FalloutNoActivationSound? noActivationSound = null,
    FalloutQuestObjectFlags? questObjects = null)
{
    internal bool Hardcore { get; set; }
    internal bool AutoDisplayObjectives { get; set; }
    internal bool LocationSpecificLoadScreensOnly { get; set; }
    internal bool InCharGen { get; private set; }
    internal bool PlayerYoung { get; private set; }
    internal bool PlayerToddler { get; private set; }
    internal float PlayerScale { get; private set; } = 1;
    internal void SetPlayerToddler(bool enabled) => PlayerToddler = enabled;
    internal void SetPlayerScale(double value) => PlayerScale = PlayerScaleValue(value);
    internal static float PlayerScaleValue(double value)
    {
        var single = (float)value;
        if (!float.IsFinite(single)) throw new InvalidDataException("Player scale requires a finite source float.");
        // The owned command stores two decimal places, clamped to its reference range.
        var rounded = float.Parse(single.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        return Math.Clamp(rounded, .01f, 10f);
    }
    internal long PlayerAppearanceRevision { get; private set; }
    internal void SetPlayerYoung(bool enabled)
    {
        if (PlayerYoung == enabled) return;
        PlayerYoung = enabled;
        PlayerAppearanceRevision++;
    }
    internal static bool PlayerYouthFlag(double value)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("SetPCYoung requires a signed integer flag.");
        return value != 0;
    }
    internal static bool PlayerToddlerFlag(double value)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("SetPCToddler requires a signed integer flag.");
        return value != 0;
    }
    internal FalloutPlayerScriptPackageSnapshot? PlayerPackage { get; private set; }
    internal void PublishPlayerPackage(FalloutPlayerScriptPackageSnapshot? state)
    {
        state?.Validate();
        PlayerPackage = state;
    }
    internal void SetInCharGen(bool enabled, Action? requireLevelUpOwner)
    {
        // Leaving chargen consumes earned XP immediately. Never clear the flag
        // while skipping an unsupported level-up or lacking the player owner.
        if (!enabled)
            (requireLevelUpOwner ?? throw new NotSupportedException("Character-generation exit has no player advancement owner."))();
        InCharGen = enabled;
    }
    private readonly HashSet<int> _achievements = [];
    internal void AddAchievement(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        _achievements.Add(id);
    }
    internal FalloutScriptSessionSnapshot Capture() => new(Hardcore, AutoDisplayObjectives, _achievements.Order().ToArray(), LocationSpecificLoadScreensOnly, InCharGen, PlayerPackage, noActivationSound?.Capture(), PlayerYoung, PlayerToddler, PlayerScale, questObjects?.Capture());
    internal void Restore(FalloutScriptSessionSnapshot state)
    {
        if (state.Achievements is null || state.Achievements.Any(id => id < 0) || state.Achievements.Distinct().Count() != state.Achievements.Count)
            throw new InvalidDataException("Saved script session state is invalid.");
        state.PlayerPackage?.Validate();
        if (PlayerScaleValue(state.PlayerScale) != state.PlayerScale) throw new InvalidDataException("Saved player scale is outside the source reference range or precision.");
        if (state.NoActivationSound is not null && noActivationSound is null)
            throw new NotSupportedException("Saved no-activation sound has no shared source owner.");
        if (state.QuestObjects is { Count: > 0 } && questObjects is null)
            throw new NotSupportedException("Saved quest-object changes have no shared form owner.");
        questObjects?.Restore(state.QuestObjects);
        noActivationSound?.Restore(state.NoActivationSound);
        Hardcore = state.Hardcore; AutoDisplayObjectives = state.AutoDisplayObjectives;
        LocationSpecificLoadScreensOnly = state.LocationSpecificLoadScreensOnly;
        InCharGen = state.InCharGen;
        SetPlayerYoung(state.PlayerYoung);
        SetPlayerToddler(state.PlayerToddler);
        SetPlayerScale(state.PlayerScale);
        PlayerPackage = state.PlayerPackage;
        _achievements.Clear(); _achievements.UnionWith(state.Achievements);
    }
}

internal sealed record FalloutQuestScriptHost(Func<FalloutFormKey, short, Action> PrepareSetStage,
    Func<string, double> PlayerActorValue,
    Action<FalloutPluginRecord, FalloutPluginRecord, FalloutGameModeProgram, double>? ExecuteProgram = null,
    FalloutUserFunctionInvoker? InvokeFunction = null, Action? RequireLevelUpOwner = null,
    Func<string, FalloutActorValueRead, double>? ReadPlayerActorValue = null,
    Action<string, string, double>? ChangePlayerActorValue = null, FalloutInventoryCommands? Inventory = null,
    Func<FalloutFormKey, FalloutFormKey, float>? HeadingAngle = null,
    Action? ResetPlayerHealth = null, Func<FalloutFormKey, FalloutFormKey?>? CurrentPackage = null,
    Func<FalloutFormKey, int>? Sitting = null, FalloutPlayerTagSkills? TagSkills = null,
    Func<FalloutFormKey, FalloutFormKey, bool>? IsInCell = null);

internal sealed partial class FalloutQuestScripts
{
    // Engine-created player reference; it is not a placed record in an ESM.
    private sealed class Instance(FalloutPluginRecord quest, FalloutPluginRecord script, FalloutGameModeProgram program, FalloutGameModeProgram menuProgram,
        FalloutQuestScriptClock clock, Func<FalloutScriptBindings> createBindings, bool claimed)
    {
        internal readonly FalloutPluginRecord Quest = quest, Script = script;
        internal readonly FalloutGameModeProgram Program = program;
        internal readonly FalloutGameModeProgram MenuProgram = menuProgram;
        internal readonly FalloutQuestScriptClock Clock = clock;
        private readonly Lazy<FalloutScriptBindings> _bindings = new(createBindings);
        internal FalloutScriptBindings Bindings => _bindings.Value;
        internal readonly bool Claimed = claimed;
        internal string? Error;
        internal long Executions;
        internal FalloutQuestScriptPendingCommand? PendingCommand;
        internal readonly List<FalloutQuestScriptContinuationReceipt> Continuations = [];
        internal string? ContinuationError;
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
    private readonly FalloutActorQueries _actorQueries = new();
    internal FalloutActorQueries ActorQueries => References?.ActorQueries ?? _actorQueries;
    internal FalloutAuxiliaryStore Auxiliary { get; }
    internal FalloutScriptIniStore? Ini { get; }
    internal FalloutInputControls? Controls { get; }
    internal FalloutScriptMenus Menus { get; }
    internal FalloutScriptSounds Sounds { get; }
    internal FalloutNoActivationSound NoActivationSound { get; }
    internal FalloutScreenBlood ScreenBlood { get; }
    internal FalloutUiComponentStore? Ui => References?.Ui;
    internal FalloutQuestScriptHost? Host { get; set; }
    internal FalloutMessageResults MessageResults { get; } = new();
    internal FalloutScriptSession Session { get; }
    internal FalloutChallenges Challenges { get; }
    internal FalloutRadioStations? Radio { get; }
    internal FalloutScriptEvents Events { get; }
    internal HashSet<FalloutFormKey> SaidInfos { get; } = [];
    internal double Variable(FalloutFormKey owner, uint index) => References?.ReadVariable(_quests, owner, index) ?? _quests.Variable(owner, index);
    internal void SetVariable(FalloutFormKey owner, uint index, double value)
    {
        if (References is null) _quests.SetVariable(owner, index, value);
        else References.WriteVariable(_quests, owner, index, value);
    }

    internal object State => Observe(detailed: true);
    internal object Observe(bool detailed) => new
    {
        detail = detailed ? "complete-script-observation" : "live-summary;request-state-for-quest-variables-and-objective-details",
        quests = _instances.Select(instance => new
        {
            quest = instance.Quest.FormKey.ToString(),
            script = instance.Script.FormKey.ToString(),
            instance.Claimed,
            instance.Executions,
            instance.Clock.Remaining,
            clock = instance.Clock.Capture(),
            instance.Clock.Interval,
            instance.Error,
            pendingCommand = instance.PendingCommand,
            continuations = instance.Continuations.ToArray(),
            continuationError = instance.ContinuationError
        }).ToArray(),
        unbound = _unbound.Select(pair => new { quest = pair.Key.ToString(), error = pair.Value }).ToArray(),
        newlyParsedOnRestore = _newlyParsed.Select(key => key.ToString()).ToArray(),
        inventory = _inventory.Items,
        messages = _messages.ToArray(),
        messageResults = MessageResults.Capture(),
        notifications = _inventory.Notifications.Capture(),
        session = Session.Capture(),
        challenges = Challenges.Capture(),
        radio = Radio?.State,
        events = Events.State,
        strings = ScriptValues.Capture(),
        auxiliary = Auxiliary.State,
        objectives = detailed ? _quests.ObjectiveState : _quests.ObjectiveSummaryState,
        variables = detailed ? _quests.VariableState : _quests.VariableSummaryState,
        initialization = new { _initialization.EmbeddedQuestScripts, _initialization.Initializations, _initialization.DefaultDelay },
        menus = Menus.State,
        sounds = Sounds.State,
        noActivationSound = NoActivationSound.State,
        screenBlood = ScreenBlood.State,
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
            instance.Clock.Remaining, instance.Executions, instance.Error, instance.Clock.Capture(), instance.PendingCommand,
            instance.Continuations.ToArray())).ToArray(),
        (displayed is null ? Enumerable.Empty<FalloutMessageRequest>() : [displayed.Request ?? throw new InvalidDataException("Displayed message has no result owner.")])
            .Concat(_messages.Select(message => message.Request!)).Where(MessageResults.IsPending).ToArray(),
        _inventory.Notifications.Capture(), MessageResults.Capture(), Session.Capture(), SaidInfos.OrderBy(key => _records.RuntimeFormId(key)).ToArray(),
        FalloutGameModeProgram.ParserVersion, ScriptValues.Capture(), Auxiliary.CapturePermanent(), Challenges.Capture(), Radio?.Capture());

    internal void Restore(FalloutQuestScriptsSnapshot snapshot)
    {
        if (_instances.Any(instance => instance.Executions != 0 || instance.Clock.Invocations != 0) || _messages.Count != 0)
            throw new InvalidOperationException("Script restoration requires a fresh owner.");
        snapshot.Validate();
        Challenges.Restore(snapshot.Challenges);
        Radio?.Restore(snapshot.Radio);
        ScriptValues.Restore(snapshot.Values);
        Auxiliary.RestorePermanent(snapshot.Auxiliary);
        ValidateValueHandles();
        References?.ValidateValueHandles();
        ScriptValues.Arrays.ValidateRestoredRoots();
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
            if (state.PendingCommand is { } pending && pending.SourceSha256 != ScriptHash(instance))
                throw new InvalidDataException("Saved command continuation differs from its winning script source.");
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
            instance.PendingCommand = state.PendingCommand;
            instance.Continuations.AddRange(state.Continuations ?? []);
            if (state.Error is not null) _unbound[instance.Quest.FormKey] = state.Error;
        }
        // Newly supported programs had no prior invocation. Their original
        // initialization clock is retained; quest locals/progression live in
        // the separately restored quest state and are never reset here.
        _newlyParsed.AddRange(newlyParsed.Select(instance => instance.Quest.FormKey));
        foreach (var message in messages) _messages.Enqueue(message);
    }

    private void ValidateValueHandles()
    {
        foreach (var snapshot in _quests.Capture())
        {
            var script = FalloutScriptLocals.AttachedScript(_records, _records.GetEffective(snapshot.Quest));
            if (script is null) continue;
            foreach (var declaration in FalloutScriptLocals.ReadDeclarations(script).Values)
            {
                ScriptValues.ValidateLocal(declaration.Kind, Variable(snapshot.Quest, declaration.Index),
                    $"{snapshot.Quest}:{declaration.Index}");
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
        Controls = references?.Controls ?? storage?.Controls;
        Menus = references?.Menus ?? new();
        Sounds = references?.Sounds ?? new(records, Menus);
        NoActivationSound = references?.NoActivationSound ?? new(records, Sounds);
        Session = new(NoActivationSound, records.QuestObjects);
        Challenges = new(records, inventory.Notifications);
        Radio = references is null ? null : new(records, references, inventory.Notifications);
        ScreenBlood = references?.ScreenBlood ?? new(records);
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
                var menuProgram = FalloutGameModeProgram.Read(source[0].Data.Span, "MenuMode");
                if (!_initialization.Definitions.TryGetValue(script.FormKey, out var definition))
                    throw new NotSupportedException("Attached quest script has no quest-clock declaration.");
                if (!clocks.TryGetValue(script.FormKey, out var clock))
                    clocks.Add(script.FormKey, clock = new(defaultDelay, definition.ProcessingDelay, definition.InitialPhase));
                _instances.Add(new(quest, script, program, menuProgram, clock, () => new(records, quest, script, script.ReadSubrecords()), claimed));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
            { _unbound[quest.FormKey] = error.Message; }
        }
    }

    internal void Advance(double seconds, bool gameMode = true, IEnumerable<uint>? menus = null, bool execute = true)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(seconds));
        Menus.Publish(gameMode, menus);
        foreach (var instance in _instances)
        {
            if (instance.Claimed) continue;
            if (instance.Error is not null)
            {
                if (gameMode && execute && _quests.IsRunning(instance.Quest.FormKey)) ContinueMissingCommand(instance, Host);
                continue;
            }
            var program = gameMode ? instance.Program : instance.MenuProgram;
            try
            {
                if (!_quests.IsRunning(instance.Quest.FormKey) || !instance.Clock.Advance((float)seconds)) continue;
                if (execute && (gameMode || program.HasStatements)) { Execute(instance, Host, program); ++instance.Executions; }
                instance.Clock.CompleteInvocation();
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            { RetainFailure(instance, error, program, gameMode); }
        }
    }

    internal void RequireQuestExecution(FalloutFormKey quest)
    {
        if (_unbound.TryGetValue(quest, out var error)) throw new NotSupportedException($"Startup quest {quest} is unbound: {error}");
    }

    internal void AdvanceClaimed(FalloutFormKey quest, double seconds, FalloutQuestScriptHost host)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(seconds));
        var instance = _instances.SingleOrDefault(value => value.Quest.FormKey == quest && value.Claimed) ??
            throw new NotSupportedException($"Claimed quest {quest} has no source program: {_unbound.GetValueOrDefault(quest)}");
        if (!_quests.IsRunning(quest)) return;
        if (instance.Error is not null)
        {
            ContinueMissingCommand(instance, host);
            if (instance.Error is not null) throw new NotSupportedException(instance.Error);
            return;
        }
        if (!instance.Clock.Advance((float)seconds)) return;
        try
        {
            Execute(instance, host);
            ++instance.Executions;
            instance.Clock.CompleteInvocation();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            RetainFailure(instance, error, instance.Program, true);
            throw;
        }
    }

    internal void ExecuteClaimedMenu(FalloutFormKey quest, uint menu, FalloutQuestScriptHost host)
    {
        var instance = _instances.SingleOrDefault(value => value.Quest.FormKey == quest && value.Claimed) ??
            throw new NotSupportedException($"Menu event has no claimed quest script owner: {quest}");
        if (instance.Error is not null) throw new NotSupportedException(instance.Error);
        if (!_quests.IsRunning(quest)) return;
        using var context = Menus.Enter(menu);
        try { Execute(instance, host, instance.MenuProgram); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            RetainFailure(instance, error, instance.MenuProgram, false);
            throw;
        }
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
            if (FalloutScriptBindings.IsPlayer(name))
                return FalloutScriptValue.Form(_records.RuntimeFormId(instance.Bindings.Reference(name)));
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
                instance.Bindings.Source.OwnerPlugin, $"{key.Owner}:{key.Index}");
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
        bool IsForm(string name) => FalloutScriptBindings.IsPlayer(name) || TryForm(name) is { Signature: not "GLOB" } ||
            instance.Bindings.HasVariable(name) && instance.Bindings.VariableKind(name) == FalloutScriptLocalKind.Form;
        bool HasValueOwner(string name) => FalloutScriptBindings.IsPlayer(name) || TryForm(name) is not null ||
            instance.Bindings.HasVariable(name);
        var values = new FalloutScriptValueContext(ReadValue, WriteValue, FormName, ScriptValues.Arrays,
            ReferenceFunction, IsForm, HasValueOwner);
        FalloutPluginRecord Quest(string name)
        {
            var quest = Form(name);
            return quest.Signature == "QUST" ? quest : throw new InvalidDataException("Script quest argument is not QUST.");
        }
        string StringArgument(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1]
            : values.Read(token).Text;
        string UiStringArgument(string token) => token.Length >= 2 && token[0] == '"' && token[^1] == '"'
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
        FalloutFormKey AuxiliaryTarget(string[] parts, IReadOnlyList<FalloutScriptArgument>? arguments = null,
            FalloutScriptValue? caller = null) =>
            arguments is { Count: >= 3 } ? AuxiliaryValueOwner(arguments[2].Value) : caller is { } value
                ? AuxiliaryValueOwner(value) : parts.Length == 1
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
        FalloutScriptFunction? ReferenceFunction(string name)
        {
            var signature = FunctionFor("player." + name, null);
            return signature is null ? null : FalloutScriptFunction.Reference(signature,
                (caller, arguments) => FunctionFor("player." + name, caller)!.InvokeValue(arguments));
        }
        FalloutScriptFunction? Function(string name) => FunctionFor(name, null);
        FalloutScriptFunction? FunctionFor(string name, FalloutScriptValue? caller)
        {
            var parts = name.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            if (caller is { } receiver)
            {
                FalloutScriptFunction.RequireReference(receiver);
                if (receiver.Number != 0x14) _ = _records.GetEffective(receiver.FormKey(_records));
            }
            if (parts.Length == 1 && FalloutSoundCommands.Function(_records, operation) is { } soundFunction)
                return soundFunction;
            if (parts.Length == 1 && operation == "menumode")
                return new([FalloutScriptArgumentKind.OptionalNumber], arguments => Menus.Query(arguments.Count == 0 ? null : arguments[0].Number)) { ReadOnly = true };
            if (parts.Length == 1 && operation == "getdeadcount")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    (References ?? throw new NotSupportedException("GetDeadCount has no shared death history."))
                    .GetDeadCount(arguments[0].Value.FormKey(_records)))
                { ReadOnly = true };
            if (parts.Length == 1 && operation == "getlocationspecificloadscreensonly")
                return new([], _ => Session.LocationSpecificLoadScreensOnly ? 1 : 0) { ReadOnly = true };
            if (parts.Length == 1 && operation == "getinchargen")
                return new([], _ => Session.InCharGen ? 1 : 0) { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getheadingangle")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    (host?.HeadingAngle ?? throw new NotSupportedException("GetHeadingAngle has no spatial owner."))
                    (caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])),
                        arguments[0].Value.FormKey(_records)))
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getincell")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    (host?.IsInCell ?? (References ?? throw new NotSupportedException("GetInCell has no reference owner.")).IsInCell)
                    (caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])),
                        arguments[0].Value.FormKey(_records)) ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getiscurrentpackage")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    FalloutAiPackages.IsCurrentPackage(_records,
                        caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])),
                        arguments[0].Value.FormKey(_records), host?.CurrentPackage ?? (References ??
                            throw new NotSupportedException("Current-package query has no reference owner.")).CurrentPackage) ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getsitting")
                return new([], _ => (References ?? throw new NotSupportedException("GetSitting has no reference owner."))
                    .GetSitting(caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])),
                        host?.Sitting))
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "gettalkedtopc")
                return new([], _ => (References ?? throw new NotSupportedException("Talked-to-player query has no reference owner."))
                    .GetTalkedToPlayer(caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])))
                    ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getlinkedref")
                return FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(
                    (References ?? throw new NotSupportedException("Linked-reference query has no reference owner."))
                    .GetLinkedRef(caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])))
                        is { } linked ? _records.RuntimeFormId(linked) : 0), readOnly: true);
            if (parts.Length <= 2 && operation == "getbroadcaststate")
                return new([], _ => (References ?? throw new NotSupportedException("Broadcast query has no reference owner."))
                    .GetBroadcastState(caller?.FormKey(_records) ?? (parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0])))
                    ? 1 : 0)
                { ReadOnly = true };
            if (parts.Length <= 2 && operation == "getvampire")
                return new([], arguments =>
                {
                    if (caller is null && parts.Length == 2) _ = instance.Bindings.Reference(parts[0]);
                    return ActorQueries.GetVampire();
                })
                { ReadOnly = true };
            if (parts.Length == 1 && ScriptValues.Arrays.Function(name) is { } arrayFunction) return arrayFunction;
            if (parts.Length <= 2 && operation == "playsound3d")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                {
                    Sounds.PlayAtReference(caller?.FormKey(_records) ?? (parts.Length == 2
                        ? instance.Bindings.Reference(parts[0]) : instance.Quest.FormKey), arguments[0].Value.FormKey(_records));
                    return 0;
                });
            if (parts.Length <= 2 && operation == "getitemcount")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                {
                    var target = caller?.FormKey(_records) ?? (parts.Length == 2
                        ? instance.Bindings.Reference(parts[0]) : instance.Quest.FormKey);
                    var owner = host?.Inventory ?? new FalloutInventoryCommands(_records, References ??
                        throw new NotSupportedException("Item count has no reference world."), _inventory,
                        () => throw new NotSupportedException("NPC inventory has no player-level owner."), _globals);
                    return owner.ItemCount(target, arguments[0].Value.FormKey(_records));
                })
                { ReadOnly = true };
            if (parts.Length <= 2 && operation is "getequippedobject" or "geteqobj")
                return FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Number], arguments =>
                {
                    var target = caller?.FormKey(_records) ?? (parts.Length == 2
                        ? instance.Bindings.Reference(parts[0]) : instance.Quest.FormKey);
                    var owner = host?.Inventory ?? new FalloutInventoryCommands(_records, References ??
                        throw new NotSupportedException("Equipment query has no reference world."), _inventory,
                        () => throw new NotSupportedException("NPC inventory has no player-level owner."), _globals);
                    var equipped = owner.EquippedObject(target, FalloutInventoryCommands.Slot(arguments[0].Number));
                    return FalloutScriptValue.Form(equipped is { } item ? _records.RuntimeFormId(item) : 0);
                });
            if (parts.Length == 1 && FalloutInputControlCommands.IsQuery(operation))
                return FalloutInputControlCommands.Query(operation, Controls ?? throw new NotSupportedException("Control queries have no profile input owner."));
            if (parts.Length == 1 && operation is "getnthperkentryvalue1" or "getnthperkentryvalue2" or
                "getnthperkentrytype" or "getnthperkentryfunction")
                return new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
                {
                    var form = arguments[0].Value.FormKey(_records);
                    var index = FalloutPerkParameters.Index(arguments[1].Number);
                    return operation switch
                    {
                        "getnthperkentrytype" => _records.PerkParameters.Type(form, index),
                        "getnthperkentryfunction" => _records.PerkParameters.EntryPoint(form, index),
                        _ => _records.PerkParameters.Get(form, index, operation == "getnthperkentryvalue2" ? 1 : 0),
                    };
                });
            if (parts.Length == 1 && operation == "getperkentrycount")
                return new([FalloutScriptArgumentKind.Value], arguments =>
                    _records.PerkParameters.Count(arguments[0].Value.FormKey(_records)));
            if (parts.Length <= 2 && operation is "auxiliaryvariablegetfloat" or "auxvargetflt" or
                "auxiliaryvariablegettype" or "auxvartype" or "auxiliaryvariablegetref" or "auxvargetref" or
                "auxiliaryvariablegetstring" or "auxvargetstr")
            {
                return operation switch
                {
                    "auxiliaryvariablegetfloat" or "auxvargetflt" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => Auxiliary.GetFloat(AuxiliaryTarget(parts, arguments, caller), instance.Script.FormKey.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0)),
                    "auxiliaryvariablegettype" or "auxvartype" =>
                        new([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments => Auxiliary.GetType(AuxiliaryTarget(parts, arguments, caller), instance.Script.FormKey.OwnerPlugin,
                                arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0)),
                    "auxiliaryvariablegetref" or "auxvargetref" =>
                        FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                            FalloutScriptArgumentKind.OptionalValue],
                            arguments =>
                            {
                                var form = Auxiliary.GetForm(AuxiliaryTarget(parts, arguments, caller), instance.Script.FormKey.OwnerPlugin,
                                    arguments[0].Text, arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0);
                                return FalloutScriptValue.Form(form is { } value ? _records.RuntimeFormId(value) : 0);
                            }),
                    _ => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String, FalloutScriptArgumentKind.OptionalNumber,
                        FalloutScriptArgumentKind.OptionalValue],
                        arguments => FalloutScriptValue.String(Auxiliary.GetString(AuxiliaryTarget(parts, arguments, caller),
                            instance.Script.FormKey.OwnerPlugin, arguments[0].Text,
                            arguments.Count >= 2 ? AuxiliaryIndexValue(arguments[1].Number) : 0))),
                };
            }
            if (parts.Length == 1 && FalloutQuestObjectCommands.Function(_records, operation) is { } questObjectFunction)
                return questObjectFunction;
            if (parts.Length == 1 && FalloutNumericGameSettingCommands.Function(_records, operation) is { } settingFunction)
                return settingFunction;
            if (parts.Length == 1 && FalloutNumericIniSettingCommands.Function(_records, operation) is { } iniSettingFunction)
                return iniSettingFunction;
            if (parts.Length == 1 && FalloutModQueryCommands.Function(_records, operation) is { } modQueryFunction)
                return modQueryFunction;
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
            if (parts.Length == 1 && operation is "getuifloat" or "getuifloatalt" or "getuistring")
            {
                var ui = Ui ?? throw new NotSupportedException("UI functions have no menu-session owner.");
                return operation switch
                {
                    "getuifloat" => new([FalloutScriptArgumentKind.String], arguments => ui.GetFloat(arguments[0].Text)),
                    "getuifloatalt" => new([FalloutScriptArgumentKind.String], arguments => ui.GetFloat(arguments[0].Text, alt: true)),
                    _ => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String],
                        arguments => FalloutScriptValue.String(ui.GetString(arguments[0].Text))),
                };
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
                "getstagedone" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                {
                    var stage = arguments[1].Number;
                    if (stage < 0 || stage > short.MaxValue || stage != Math.Truncate(stage))
                        throw new InvalidDataException("Quest stage index is invalid.");
                    return _quests.StageDone(Quest(arguments[0].Identifier!).FormKey, (short)stage) ? 1 : 0;
                }),
                "getsecondspassed" => new([], _ => instance.Clock.Elapsed),
                "getrandompercent" => new([], _ => ScriptValues.RandomPercent()),
                "getcurrenttime" => new([], _ => (_globals ??
                    throw new NotSupportedException("GetCurrentTime has no simulation clock."))
                    .Get(FalloutGameTimeBindings.Read(_records).Hour))
                { ReadOnly = true },
                "getbuttonpressed" => new([], _ => MessageResults.Take(instance.Script.FormKey)),
                "isplayertagskill" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                    (host?.TagSkills ?? throw new NotSupportedException("Player tag skills have no shared owner."))
                        .IsTagged(arguments[0].Identifier!) ? 1 : 0)
                { ReadOnly = true },
                "abs" => new([FalloutScriptArgumentKind.Number], arguments => Math.Abs(arguments[0].Number)),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                {
                    var index = arguments[1].Number;
                    if (index != Math.Truncate(index)) throw new InvalidDataException("Objective index is fractional.");
                    return _quests.Objective(Quest(arguments[0].Identifier!).FormKey, checked((uint)index)).Displayed ? 1 : 0;
                }),
                "player.getactorvalue" or "player.getav" or "player.getbaseactorvalue" or "player.getbaseav" or
                    "player.getpermanentactorvalue" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    if (!instance.Bindings.HasPlayerReference) throw new InvalidDataException("Player function has no compiled engine reference.");
                    if (caller is { } value && value.Number != 0x14)
                        throw new NotSupportedException("Quest fallback actor values have only a player gameplay owner.");
                    var kind = FalloutActorValue.Query(operation)!.Value;
                    return host?.ReadPlayerActorValue is { } read ? read(arguments[0].Identifier!, kind) :
                        kind == FalloutActorValueRead.Current ? host?.PlayerActorValue(arguments[0].Identifier!) ??
                            throw new NotSupportedException("Player actor values have no gameplay owner.") :
                            throw new NotSupportedException("Player base/permanent values have no source pool owner.");
                }),
                _ => null,
            };
        }
        void Call(string command, IReadOnlyList<string> rawArguments)
        {
            var parts = command.Split('.');
            var operation = parts[^1].ToLowerInvariant();
            if (parts.Length == 1 && operation is "setnumericgamesetting" or "setquestobject")
            {
                _ = FalloutNvseNumericExpression.EvaluateValue([command, .. rawArguments], values, Function);
                return;
            }
            var arguments = FalloutGameModeProgram.ResolveCommandArguments(rawArguments, values, Function);
            var caller = instance.Script.FormKey.OwnerPlugin;
            if (parts.Length <= 2 && operation is "equipitem" or "equipobject" or "unequipitem" or "removeallitems" or "resetinventory")
            {
                var target = parts.Length == 2 ? instance.Bindings.Reference(parts[0]) : instance.Quest.FormKey;
                bool Flag(string token) => NumberArgument(token) switch
                { 0 => false, 1 => true, _ => throw new InvalidDataException("Inventory command flag is not boolean.") };
                var owner = host?.Inventory ?? new FalloutInventoryCommands(_records, References ??
                    throw new NotSupportedException("Inventory command has no reference world."), _inventory,
                    () => throw new NotSupportedException("NPC inventory has no player-level owner."), _globals);
                var request = operation switch
                {
                    "equipitem" or "equipobject" when arguments.Count is >= 1 and <= 3 => new FalloutInventoryCommand(
                        FalloutInventoryCommandKind.Equip, target, Item: Form(arguments[0]).FormKey,
                        NoUnequip: arguments.Count >= 2 && Flag(arguments[1]), Silent: arguments.Count < 3 || Flag(arguments[2])),
                    "unequipitem" when arguments.Count is >= 1 and <= 3 => new FalloutInventoryCommand(
                        FalloutInventoryCommandKind.Unequip, target, Item: Form(arguments[0]).FormKey,
                        NoEquip: arguments.Count >= 2 && Flag(arguments[1]), Silent: arguments.Count >= 3 && Flag(arguments[2])),
                    "removeallitems" when arguments.Count <= 3 => new FalloutInventoryCommand(
                        FalloutInventoryCommandKind.RemoveAll, target,
                        Destination: arguments.Count == 0 || arguments[0] == "0" ? null : instance.Bindings.Reference(arguments[0]),
                        RetainOwnership: arguments.Count >= 2 && Flag(arguments[1]), Silent: arguments.Count >= 3 && Flag(arguments[2])),
                    "resetinventory" when arguments.Count == 0 => new FalloutInventoryCommand(FalloutInventoryCommandKind.Reset, target),
                    _ => throw new InvalidDataException("Inventory command argument count is invalid.")
                };
                owner.Execute(request);
                return;
            }
            if (parts.Length == 2 && operation == "agerace")
            {
                if (arguments.Count != 1) throw new InvalidDataException("AgeRace requires one signed step count.");
                (References ?? throw new NotSupportedException("AgeRace has no shared reference world."))
                    .AgeRace(ReferenceArgument(parts[0]), FalloutReferenceWorld.RaceAgeSteps(NumberArgument(arguments[0])));
                return;
            }
            if (parts.Length == 2 && operation == "resethealth" && FalloutScriptBindings.IsPlayer(parts[0]))
            {
                if (ReferenceArgument(parts[0]) != _records.RuntimeFormKey(0x14) || arguments.Count != 0)
                    throw new InvalidDataException("Player ResetHealth requires its compiled reference and no arguments.");
                (host?.ResetPlayerHealth ?? throw new NotSupportedException("Player ResetHealth has no shared vitals owner."))();
                return;
            }
            if (parts.Length <= 2 && operation is "stopcombatalarmonactor" or "scaonactor")
            {
                if (arguments.Count != 0) throw new InvalidDataException("StopCombatAlarmOnActor takes no arguments.");
                (References ?? throw new NotSupportedException("Combat alarm has no shared reference owner."))
                    .StopCombatAlarmOnActor(parts.Length == 1 ? instance.Quest.FormKey : instance.Bindings.Reference(parts[0]));
                return;
            }
            if (parts.Length == 2 && parts[0].Equals("player", StringComparison.OrdinalIgnoreCase) &&
                operation is "setav" or "setactorvalue" or "modav" or "modactorvalue" or "forceav" or "forceactorvalue")
            {
                if (!instance.Bindings.HasPlayerReference || arguments.Count != 2)
                    throw new InvalidDataException("Player actor value command requires its compiled reference and two arguments.");
                (host?.ChangePlayerActorValue ?? throw new NotSupportedException("Player actor value writes have no source pool owner."))
                    (arguments[0], operation, NumberArgument(arguments[1]));
                return;
            }
            if (parts.Length == 1 && operation is "triggerscreenblood" or "tsb")
            {
                if (arguments.Count != 1) throw new InvalidDataException("TriggerScreenBlood requires one count.");
                ScreenBlood.Trigger(instance.Quest.FormKey, FalloutScreenBlood.Count(NumberArgument(arguments[0])));
                return;
            }
            if (parts.Length <= 2 && operation == "playsound3d")
            {
                if (arguments.Count != 1) throw new InvalidDataException("PlaySound3D requires one SOUN form.");
                var sound = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function);
                if (sound.Kind == FalloutScriptValueKind.Number) sound = FalloutScriptValue.Form(sound.Number);
                Sounds.PlayAtReference(parts.Length == 2 ? ReferenceArgument(parts[0]) : instance.Quest.FormKey,
                    sound.FormKey(_records));
                return;
            }
            if (parts.Length == 1 && operation == "playsound")
            {
                if (arguments.Count is < 1 or > 2) throw new InvalidDataException("PlaySound requires a sound and optional system flag.");
                var sound = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function);
                if (sound.Kind == FalloutScriptValueKind.Number) sound = FalloutScriptValue.Form(sound.Number);
                Sounds.Play(instance.Quest.FormKey, sound.FormKey(_records), arguments.Count == 2 &&
                    FalloutScriptSounds.SystemFlag(NumberArgument(arguments[1])));
                return;
            }
            if (parts.Length == 1 && operation is "stopsound" or "setsoundsourcefile" or "getsoundsourcefile")
            {
                FalloutNvseNumericExpression.EvaluateValue([parts[^1], .. arguments], values, Function);
                return;
            }
            if (parts.Length == 1 && operation == "setnoactivationsound")
            {
                if (arguments.Count != 1) throw new InvalidDataException("SetNoActivationSound requires one SOUN form.");
                var sound = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function);
                if (sound.Kind == FalloutScriptValueKind.Number) sound = FalloutScriptValue.Form(sound.Number);
                NoActivationSound.Set(sound.FormKey(_records));
                return;
            }
            if (parts.Length == 1 && operation == "clearnoactivationsound")
            {
                if (arguments.Count != 0) throw new InvalidDataException("ClearNoActivationSound takes no arguments.");
                NoActivationSound.Clear();
                return;
            }
            if (parts.Length == 2 && parts[0].Equals("player", StringComparison.OrdinalIgnoreCase) && operation == "setscale")
            {
                if (!instance.Bindings.HasPlayerReference || arguments.Count != 1) throw new InvalidDataException("Player SetScale requires its compiled reference and one source float.");
                Session.SetPlayerScale(NumberArgument(arguments[0]));
                return;
            }
            if (parts.Length == 1 && operation == "setpctoddler")
            {
                if (arguments.Count != 1) throw new InvalidDataException("SetPCToddler requires one integer flag.");
                Session.SetPlayerToddler(FalloutScriptSession.PlayerToddlerFlag(NumberArgument(arguments[0])));
                return;
            }
            if (parts.Length == 1 && operation == "setpcyoung")
            {
                if (arguments.Count != 1) throw new InvalidDataException("SetPCYoung requires one integer flag.");
                Session.SetPlayerYoung(FalloutScriptSession.PlayerYouthFlag(NumberArgument(arguments[0])));
                return;
            }
            if (parts.Length == 1 && operation == "setinchargen")
            {
                if (arguments.Count != 1) throw new InvalidDataException("Character-generation policy requires one flag.");
                var enabled = NumberArgument(arguments[0]) switch
                {
                    0 => false,
                    1 => true,
                    _ => throw new InvalidDataException("Character-generation flag must be zero or one."),
                };
                Session.SetInCharGen(enabled, host?.RequireLevelUpOwner);
                return;
            }
            if (parts.Length == 1 && operation == "setlocationspecificloadscreensonly")
            {
                if (arguments.Count != 1) throw new InvalidDataException("Loading-screen policy requires one flag.");
                var flag = NumberArgument(arguments[0]);
                Session.LocationSpecificLoadScreensOnly = flag switch
                {
                    0 => false,
                    1 => true,
                    _ => throw new InvalidDataException("Loading-screen policy flag must be zero or one."),
                };
                return;
            }
            if (parts.Length == 1 && FalloutInputControlCommands.IsCommand(operation))
            {
                FalloutInputControlCommands.Execute(operation, Controls ?? throw new NotSupportedException("Control commands have no profile input owner."),
                    arguments.Select(NumberArgument).ToArray());
                return;
            }
            if (parts.Length == 1 && operation == "setplayertagskill")
            {
                if (arguments.Count != 2) throw new InvalidDataException("SetPlayerTagSkill requires a skill and slot.");
                (host?.TagSkills ?? throw new NotSupportedException("SetPlayerTagSkill has no shared player tag owner."))
                    .Set(arguments[0], NumberArgument(arguments[1]));
                return;
            }
            if (parts.Length <= 2 && operation == "setbroadcaststate")
            {
                if (arguments.Count != 1) throw new InvalidDataException("SetBroadcastState requires one state.");
                (References ?? throw new NotSupportedException("Broadcast command has no reference owner."))
                    .SetBroadcastState(parts.Length == 1 ? instance.Quest.FormKey : ReferenceArgument(parts[0]), NumberArgument(arguments[0]));
                return;
            }
            if (parts.Length == 1 && operation is "setally" or "setenemy")
            {
                if (arguments.Count is < 2 or > 4) throw new InvalidDataException("Faction relationship requires two factions and optional directional flags.");
                (References ?? throw new NotSupportedException("Faction relationship has no shared world owner."))
                    .SetFactionRelationship(TryForm(arguments[0])?.FormKey ?? AuxiliaryValueOwner(NumberArgument(arguments[0])),
                        TryForm(arguments[1])?.FormKey ?? AuxiliaryValueOwner(NumberArgument(arguments[1])), operation == "setally",
                        arguments.Count > 2 ? NumberArgument(arguments[2]) : 0,
                        arguments.Count > 3 ? NumberArgument(arguments[3]) : 0);
                return;
            }
            if (parts.Length == 1 && operation is "setnthperkentryvalue1" or "setnthperkentryvalue2")
            {
                if (arguments.Count != 3) throw new InvalidDataException($"{command} has an invalid argument count.");
                var form = FalloutNvseNumericExpression.EvaluateValue([arguments[0]], values, Function);
                if (form.Kind == FalloutScriptValueKind.Number) form = FalloutScriptValue.Form(form.Number);
                _ = _records.PerkParameters.Set(form.FormKey(_records),
                    FalloutPerkParameters.Index(NumberArgument(arguments[1])), (float)NumberArgument(arguments[2]),
                    operation == "setnthperkentryvalue2" ? 1 : 0);
                return;
            }
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
            if (parts.Length == 1 && operation == "setuifloatgradual")
            {
                var ui = Ui ?? throw new NotSupportedException("UI commands have no menu-session owner.");
                if (arguments.Count is < 1 or > 5) throw new InvalidDataException($"{command} has an invalid argument count.");
                var mode = arguments.Count == 5 ? NumberArgument(arguments[4]) : 0;
                if (mode != Math.Truncate(mode)) throw new InvalidDataException("UI animation mode is fractional.");
                _ = ui.SetFloatGradual(StringArgument(arguments[0]),
                    arguments.Count >= 2 ? (float)NumberArgument(arguments[1]) : null,
                    arguments.Count >= 3 ? (float)NumberArgument(arguments[2]) : null,
                    arguments.Count >= 4 ? (float)NumberArgument(arguments[3]) : null,
                    checked((int)mode));
                return;
            }
            if (parts.Length == 1 && operation is "setuifloat" or "setuifloatalt" or "setuistring" or
                "setuistringalt" or "setuistringex" or "unloaduicomponent")
            {
                var ui = Ui ?? throw new NotSupportedException("UI commands have no menu-session owner.");
                if (operation == "unloaduicomponent")
                {
                    if (arguments.Count != 1) throw new InvalidDataException($"{command} has an invalid argument count.");
                    _ = ui.Unload(StringArgument(arguments[0]));
                    return;
                }
                if (arguments.Count < 2 || operation == "setuistringex" && arguments.Count > 3 ||
                    operation != "setuistringex" && arguments.Count != 2)
                    throw new InvalidDataException($"{command} has an invalid argument count.");
                var path = StringArgument(arguments[0]);
                var alt = operation is "setuifloatalt" or "setuistringalt";
                if (operation is "setuifloat" or "setuifloatalt") _ = ui.SetFloat(path, (float)NumberArgument(arguments[1]), alt);
                else _ = ui.SetString(path, UiStringArgument(arguments[1]), alt,
                    arguments.Count == 3 ? UiStringArgument(arguments[2]) : null);
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
                case "pipboyradiooff" when arguments.Count == 0:
                    (References ?? throw new NotSupportedException("Pip-Boy radio has no shared reference world.")).PipBoyRadio.Off();
                    break;
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
                case "completeallobjectives" when arguments.Count == 1:
                    _quests.CompleteAllObjectives(Quest(arguments[0]).FormKey);
                    break;
                case "unlockchallenge" when arguments.Count == 1:
                    Challenges.Unlock(Form(arguments[0]).FormKey);
                    break;
                case "incrementscriptedchallenge" when arguments.Count == 1:
                    Challenges.IncrementScripted(Form(arguments[0]).FormKey);
                    break;
                case "killquestupdates" or "kqu" when arguments.Count == 0:
                    _quests.KillQuestUpdates();
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
        // and nested SetStage scripts. A bound missing command may continue
        // from its stopped instruction; the invocation prefix never retries.
    }
}
