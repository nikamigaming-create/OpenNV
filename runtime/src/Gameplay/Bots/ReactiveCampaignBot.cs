namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotCampaignGoal(string Identity, string Quest, uint Objective, string Target,
    string Reference, string Mode, int Priority, string? Error = null);

internal sealed record BotCampaignChoice(string Identity, string Path, string Text, string Kind,
    string Menu, bool Goodbye = false);
internal sealed record BotCampaignControl(string Path, string Text, string Name, string? Kind = null,
    string? Identity = null, string? Menu = null, bool Goodbye = false);

internal sealed record BotCampaignSave(ulong Generation, string Disposition, string? Slot, string? Error);

internal sealed record BotCampaignObservation(BotSkillBinding? Binding, long Sample, string Scene,
    long Progress, bool Loading, bool Paused, bool ModalInput, bool Defeated, bool CanSave,
    IReadOnlyList<BotCampaignGoal> Goals, IReadOnlyList<BotCampaignChoice> Choices,
    BotCampaignSave? Save = null, string? Error = null,
    bool PortalGraphInspected = false, IReadOnlyList<string>? SourceIssues = null);

internal sealed record BotCampaignSkill(bool Active, string Phase, string? Error, string? FailureKind);
internal enum BotCampaignCommandKind { None, Reference, Choice, Save, Pause }
internal sealed record BotCampaignCommand(BotCampaignCommandKind Kind, string? Reference = null,
    string? Mode = null, string? Path = null);

// Chooses source goals and offered input, never writes quests or world state.
internal sealed class ReactiveCampaignBot
{
    internal const float ProgressLimitSeconds = 90;
    private readonly Dictionary<string, long> _attemptedGoals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _choices = new(StringComparer.Ordinal);
    private BotSkillBinding? _binding;
    private BotCampaignGoal? _goal;
    private string? _pendingChoice, _choiceBefore;
    private string? _scene;
    private string? _savedScene, _saveScene;
    private long _sample = -1, _progress = -1, _savedProgress = -1, _saveProgress = -1;
    private ulong _saveBefore;
    private float _idle, _stale;
    private bool _saving;
    private int _goalAttempts, _choiceAttempts, _verifiedSegments, _writtenCheckpoints;
    private bool _portalGraphInspected;
    private IReadOnlyList<string> _sourceIssues = [];
    internal bool Active { get; private set; }
    internal string Phase { get; private set; } = "idle";
    internal string? Error { get; private set; }
    internal string? FailureKind { get; private set; }
    internal object State => new
    {
        active = Active,
        phase = Phase,
        binding = _binding,
        goal = _goal,
        observedProgress = _progress,
        savedProgress = _savedProgress,
        savedScene = _savedScene,
        idleSeconds = _idle,
        progressLimitSeconds = ProgressLimitSeconds,
        goalAttempts = _goalAttempts,
        choiceAttempts = _choiceAttempts,
        observedProgressChanges = _verifiedSegments,
        writtenCheckpoints = _writtenCheckpoints,
        portalGraphInspected = _portalGraphInspected,
        sourceIssues = _sourceIssues,
        error = Error,
        failureKind = FailureKind,
        coverage = "attempts current source objectives, directed portal travel, offered dialogue/terminal choices and verified skills; whole-game completion and retail parity unverified"
    };

    internal void Start()
    {
        if (Active) throw new InvalidOperationException("Campaign input already has an owner.");
        _attemptedGoals.Clear(); _choices.Clear();
        _binding = null; _goal = null; _pendingChoice = _choiceBefore = _scene = _savedScene = _saveScene = null;
        _sample = _progress = _savedProgress = _saveProgress = -1;
        _saving = false; _saveBefore = 0; _idle = _stale = 0;
        _goalAttempts = _choiceAttempts = _verifiedSegments = _writtenCheckpoints = 0;
        _portalGraphInspected = false; _sourceIssues = [];
        Error = FailureKind = null; Phase = "observing"; Active = true;
    }

    internal void Stop()
    {
        Active = false; Phase = "stopped";
        _goal = null; _pendingChoice = _choiceBefore = null; _saving = false;
    }

    internal BotCampaignCommand Fail(string kind, string error, bool protect = true)
    {
        Active = false; Phase = "blocked"; FailureKind = kind; Error = error;
        return new(protect ? BotCampaignCommandKind.Pause : BotCampaignCommandKind.None);
    }

    internal BotCampaignCommand Tick(BotCampaignObservation observation, BotCampaignSkill skill, float seconds)
    {
        if (!Active) return new(BotCampaignCommandKind.None);
        _portalGraphInspected = observation.PortalGraphInspected;
        _sourceIssues = observation.SourceIssues ?? [];
        if (!float.IsFinite(seconds) || seconds <= 0)
            return Fail("bot-policy", "Campaign frame duration is not finite and positive.");
        if (observation.Error is { } fault) return Fail("engine-owner", fault, !observation.Paused && !observation.Defeated);
        if (observation.Defeated)
            return Fail("recovery-required", "Player died; only an ordinary load of a complete checkpoint may resume the campaign.", false);
        if (observation.Loading) { Phase = "loading"; return new(BotCampaignCommandKind.None); }
        if (observation.Binding is not { } binding)
        {
            if (_pendingChoice is null && observation.Choices.FirstOrDefault(choice => choice.Kind == "startup") is { } startup)
            {
                _pendingChoice = startup.Identity; _choiceBefore = MenuIdentity(observation);
                ++_choiceAttempts; Phase = "starting-owned-game";
                return new(BotCampaignCommandKind.Choice, Path: startup.Path);
            }
            _idle += seconds; Phase = "awaiting-owned-game";
            return _idle > ProgressLimitSeconds
                ? Fail("observation-loss", "The campaign has no admitted source/build/player input binding.")
                : new(BotCampaignCommandKind.None);
        }
        if (binding.InputMode is not ("flat" or "simulator"))
            return Fail("input-adapter", "Physical-headset campaign automation has no ordinary input adapter.", false);
        _binding ??= binding;
        if (binding != _binding)
            return Fail("observation-loss", "Campaign source/build/native generation changed; cold restoration requires a new attempt.");
        if (observation.Sample < _sample) return Fail("observation-loss", "Campaign observation revision regressed.");
        if (observation.Sample == _sample)
        {
            _stale += seconds;
            return _stale > 2 ? Fail("observation-loss", "Campaign observations stopped advancing.") : new(BotCampaignCommandKind.None);
        }
        _sample = observation.Sample; _stale = 0;
        if (_scene != observation.Scene) { _scene = observation.Scene; _idle = 0; }
        if (_progress < 0) { _savedProgress = observation.Progress; _savedScene = observation.Scene; }
        if (_progress != observation.Progress)
        {
            if (_progress >= 0) ++_verifiedSegments;
            _progress = observation.Progress; _idle = 0;
        }
        if (_goal is not null && skill.Error is { } skillError)
            return Fail(skill.FailureKind ?? "bot-policy", skillError, !observation.Paused);
        if (_goal is null && skill.Active)
            return Fail("input-owner", "An active reference skill belongs to a different campaign attempt.", !observation.Paused);
        if (skill.Active && _pendingChoice is null && observation.Choices.Count == 0 && !observation.ModalInput)
        { Phase = "executing-skill"; return new(BotCampaignCommandKind.None); }
        if (observation.Paused && observation.Choices.Count == 0 && !observation.ModalInput)
        { Phase = "paused"; return new(BotCampaignCommandKind.None); }
        _idle += seconds;
        if (_idle > ProgressLimitSeconds)
            return Fail("bot-policy", "No observed quest/objective, menu or scene progress within the campaign bound.");
        if (_saving)
        {
            if (observation.Save is not { } saved || saved.Generation <= _saveBefore)
            { Phase = "awaiting-save-input"; return new(BotCampaignCommandKind.None); }
            if (saved.Disposition == "pending")
            { Phase = "awaiting-complete-checkpoint"; return new(BotCampaignCommandKind.None); }
            if (saved.Disposition != "completed" || string.IsNullOrWhiteSpace(saved.Slot) || saved.Error is not null)
                return Fail("engine-owner", "Complete checkpoint was not written: " + (saved.Error ?? saved.Disposition));
            _savedProgress = _saveProgress; _savedScene = _saveScene;
            ++_writtenCheckpoints; _saving = false; _idle = 0;
        }
        var menu = MenuIdentity(observation);
        if (_pendingChoice is not null)
        {
            if (menu == _choiceBefore) { Phase = "awaiting-menu-result"; return new(BotCampaignCommandKind.None); }
            _pendingChoice = _choiceBefore = null; _idle = 0;
        }
        if (observation.Choices.Count != 0)
        {
            var choice = observation.Choices.Where(value =>
                    !_choices.Contains(menu + "\n" + value.Identity))
                .OrderBy(value => value.Goodbye).FirstOrDefault();
            if (choice is null)
                return Fail("bot-policy", "All currently offered source menu choices were attempted without new observable state.");
            _choices.Add(menu + "\n" + choice.Identity);
            _pendingChoice = choice.Identity; _choiceBefore = menu; ++_choiceAttempts;
            Phase = "choosing-" + choice.Kind;
            return new(BotCampaignCommandKind.Choice, Path: choice.Path);
        }
        if (observation.ModalInput) { Phase = "awaiting-source-modal"; return new(BotCampaignCommandKind.None); }
        if (skill.Active) { Phase = "executing-skill"; return new(BotCampaignCommandKind.None); }
        if (observation.CanSave && _savedProgress >= 0 &&
            (observation.Progress != _savedProgress || observation.Scene != _savedScene))
        {
            _saving = true; _saveBefore = observation.Save?.Generation ?? 0;
            _saveProgress = observation.Progress; _saveScene = observation.Scene;
            Phase = "requesting-complete-checkpoint";
            return new(BotCampaignCommandKind.Save);
        }
        var goal = observation.Goals.OrderByDescending(value => value.Priority).FirstOrDefault(value =>
            !_attemptedGoals.TryGetValue(value.Identity, out var previous) || previous != observation.Progress);
        if (goal is null)
        {
            Phase = observation.Goals.Count == 0 ? "awaiting-source-objective" : "awaiting-source-progress";
            return new(BotCampaignCommandKind.None);
        }
        if (goal.Error is { } goalError) return Fail("engine-owner", goalError);
        if (string.IsNullOrWhiteSpace(goal.Reference) || goal.Mode is not ("interact" or "travel" or "approach"))
            return Fail("observation-loss", "Source campaign goal has no supported ordinary reference action.");
        _goal = goal; _attemptedGoals[goal.Identity] = observation.Progress; ++_goalAttempts;
        Phase = "starting-source-goal";
        return new(BotCampaignCommandKind.Reference, goal.Reference, goal.Mode);
    }

    private static string MenuIdentity(BotCampaignObservation state) =>
        state.Scene + "\n" + state.Progress + "\n" +
        string.Join('\n', state.Choices.Select(choice => choice.Menu + ":" + choice.Identity));
}
