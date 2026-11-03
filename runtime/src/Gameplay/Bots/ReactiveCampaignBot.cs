namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotCampaignGoal(string Identity, string Quest, uint Objective, string Target,
    string Reference, string Mode, int Priority, string? Error = null, string? ObjectiveText = null);

internal sealed record BotCampaignChoice(string Identity, string Path, string Text, string Kind,
    string Menu, bool Goodbye = false, IReadOnlyList<string>? SourceNames = null);
internal sealed record BotCampaignControl(string Path, string Text, string Name, string? Kind = null,
    string? Identity = null, string? Menu = null, bool Goodbye = false, IReadOnlyList<string>? SourceNames = null);

internal sealed record BotCampaignSave(ulong Generation, string Disposition, string? Slot, string? Error);

internal sealed record BotCampaignObservation(BotSkillBinding? Binding, long Sample, string Scene,
    long Progress, bool Loading, bool Paused, bool ModalInput, bool Defeated, bool CanSave,
    IReadOnlyList<BotCampaignGoal> Goals, IReadOnlyList<BotCampaignChoice> Choices,
    BotCampaignSave? Save = null, string? Error = null,
    bool PortalGraphInspected = false, IReadOnlyList<string>? SourceIssues = null,
    bool SpeechActive = false);

internal sealed record BotCampaignSkill(bool Active, string Phase, string? Error, string? FailureKind);
internal enum BotCampaignCommandKind { None, Reference, Choice, Save, Pause }
internal sealed record BotCampaignCommand(BotCampaignCommandKind Kind, string? Reference = null,
    string? Mode = null, string? Path = null, float Distance = 1.5f);

// Chooses source goals and offered input, never writes quests or world state.
internal sealed class ReactiveCampaignBot
{
    internal const float ProgressLimitSeconds = 90;
    private readonly Dictionary<string, long> _attemptedGoals = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Quest, uint Objective, string Target), long> _failedActivations = [];
    private readonly HashSet<string> _choices = new(StringComparer.Ordinal);
    private readonly HashSet<(long Progress, string Topic)> _dialogueChoices = [];
    private readonly HashSet<string> _recipeAttempts = new(StringComparer.Ordinal);
    private string? _recipeMenu;
    private float _quantityElapsed;
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
        _attemptedGoals.Clear(); _failedActivations.Clear(); _choices.Clear(); _dialogueChoices.Clear(); _recipeAttempts.Clear();
        _recipeMenu = null; _quantityElapsed = 0;
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
            if (_pendingChoice is not null && MenuIdentity(observation) != _choiceBefore)
                _pendingChoice = _choiceBefore = null;
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
        if (observation.Choices.Any(choice => IsRecipeQuantity(choice.Kind)))
        {
            if (_recipeMenu is null || observation.Choices.Any(choice => choice.Menu != _recipeMenu || !IsRecipeQuantity(choice.Kind)))
                return Fail("input-owner", "Recipe quantity input has no matching campaign-owned recipe selection.", false);
            _quantityElapsed += seconds;
            if (_quantityElapsed > ProgressLimitSeconds)
                return Fail("bot-policy", "Recipe quantity input exceeded its execution bound.", false);
        }
        if (_scene != observation.Scene) { _scene = observation.Scene; _idle = 0; }
        if (_progress < 0) { _savedProgress = observation.Progress; _savedScene = observation.Scene; }
        if (_progress != observation.Progress)
        {
            if (_progress >= 0) ++_verifiedSegments;
            _progress = observation.Progress; _idle = 0;
        }
        if (_goal is not null && skill.Error is { } skillError)
        {
            // A quest may name several places for the same objective. A
            // delivered activation with no response is not completion, but
            // another untried source target can still contain the encounter.
            var failed = _goal;
            var noResponse = skill.FailureKind == "bot-policy" && skillError == "Activation had no observed gameplay response.";
            if (noResponse) _failedActivations[(failed.Quest, failed.Objective, failed.Target)] = observation.Progress;
            var alternative = noResponse &&
                observation.Goals.Any(candidate => candidate.Quest == failed.Quest && candidate.Objective == failed.Objective &&
                    candidate.Target != failed.Target && candidate.Error is null && !ActivationFailed(candidate, observation.Progress) &&
                    (!_attemptedGoals.TryGetValue(candidate.Identity, out var tried) || tried != observation.Progress));
            if (!alternative) return Fail(skill.FailureKind ?? "bot-policy", skillError, !observation.Paused);
            _goal = null;
        }
        if (_goal is null && skill.Active)
            return Fail("input-owner", "An active reference skill belongs to a different campaign attempt.", !observation.Paused);
        if (skill.Active && _pendingChoice is null && observation.Choices.Count == 0 && !observation.ModalInput)
        {
            // A target can disappear from the live goals as soon as the shot
            // kills it. Let its already-fired impact settle before replacing
            // the skill, so another source goal cannot erase the receipt.
            if (skill.Phase == "combat-awaiting-shot")
            { Phase = "executing-skill"; return new(BotCampaignCommandKind.None); }
            var highestGoal = observation.Goals.Where(value => !ActivationFailed(value, observation.Progress))
                .OrderByDescending(value => value.Priority).FirstOrDefault();
            var currentAction = _goal is null ? null : observation.Goals.FirstOrDefault(value =>
                value.Quest == _goal.Quest && value.Objective == _goal.Objective && value.Target == _goal.Target &&
                value.Reference == _goal.Reference && value.Mode == _goal.Mode && value.Error is null);
            var highestIsCurrent = highestGoal is not null && currentAction is not null &&
                highestGoal.Quest == currentAction.Quest && highestGoal.Objective == currentAction.Objective &&
                highestGoal.Target == currentAction.Target && highestGoal.Reference == currentAction.Reference && highestGoal.Mode == currentAction.Mode;
            if (_goal is not null && highestGoal is not null &&
                !highestIsCurrent && (currentAction is null || highestGoal.Priority > currentAction.Priority))
            {
                if (string.IsNullOrWhiteSpace(highestGoal.Reference) || highestGoal.Mode is not ("interact" or "travel" or "approach" or "shoot" or "follow" or "combat"))
                    return Fail("observation-loss", "Source campaign goal has no supported ordinary reference action.");
                _goal = highestGoal; _attemptedGoals[highestGoal.Identity] = observation.Progress; ++_goalAttempts;
                Phase = "starting-source-goal";
                var preemptDistance = GoalDistance(highestGoal.Mode);
                return new(BotCampaignCommandKind.Reference, highestGoal.Reference, highestGoal.Mode, Distance: preemptDistance);
            }
            Phase = "executing-skill"; return new(BotCampaignCommandKind.None);
        }
        if (observation.Paused && observation.Choices.Count == 0 && !observation.ModalInput)
        { Phase = "paused"; return new(BotCampaignCommandKind.None); }
        if (observation.SpeechActive || skill.Phase == "following-at-distance") _idle = 0;
        _idle += seconds;
        if (_idle > ProgressLimitSeconds)
            return Fail("bot-policy", "No observed quest/objective, menu or scene progress within the campaign bound.");
        if (_saving)
        {
            if (observation.Save is not { } saved || saved.Generation <= _saveBefore)
            {
                // An offered source menu can open between admission and the
                // input pulse, consuming it without starting a save. Let that
                // menu proceed; no receipt means no checkpoint is credited.
                if (observation.Choices.Count == 0)
                { Phase = "awaiting-save-input"; return new(BotCampaignCommandKind.None); }
                _saving = false;
            }
            else
            {
                if (saved.Disposition == "pending")
                { Phase = "awaiting-complete-checkpoint"; return new(BotCampaignCommandKind.None); }
                if (saved.Disposition == "failed" && saved.Error is "speech" or "actor-procedure-initialization")
                {
                    // No checkpoint was committed: wait for fresh admission.
                    // A package can begin between the previous observation
                    // and the save input, just as speech can begin there.
                    _saving = false; Phase = "awaiting-save-admission";
                    return new(BotCampaignCommandKind.None);
                }
                if (saved.Disposition != "completed" || string.IsNullOrWhiteSpace(saved.Slot) || saved.Error is not null)
                    return Fail("engine-owner", "Complete checkpoint was not written: " + (saved.Error ?? saved.Disposition));
                _savedProgress = _saveProgress; _savedScene = _saveScene;
                ++_writtenCheckpoints; _saving = false; _idle = 0;
            }
        }
        var menu = MenuIdentity(observation);
        if (!observation.ModalInput && !observation.Choices.Any(choice => choice.Kind == "dialogue"))
            _dialogueChoices.Clear();
        if (_pendingChoice is not null)
        {
            if (menu == _choiceBefore) { Phase = "awaiting-menu-result"; return new(BotCampaignCommandKind.None); }
            _pendingChoice = _choiceBefore = null; _idle = 0;
        }
        if (observation.Choices.Count != 0)
        {
            var available = observation.Choices.Where(value => value.Kind == "dialogue"
                    ? !_dialogueChoices.Contains((observation.Progress, value.Identity))
                    : !_choices.Contains(menu + "\n" + value.Identity));
            BotCampaignChoice? choice;
            if (observation.Choices.Any(value => IsRecipeQuantity(value.Kind)))
                choice = available.FirstOrDefault(value => value.Kind is "recipe-quantity-decrease" or "recipe-quantity-confirm");
            else if (observation.Choices.Any(value => IsRecipeMenu(value.Kind)))
            {
                var owner = observation.Choices[0].Menu;
                if (observation.Choices.Any(value => value.Menu != owner || !IsRecipeMenu(value.Kind)))
                    return Fail("observation-loss", "Recipe input mixes different source menus.", false);
                // One ordinary crafting attempt per opened menu. Source quest
                // state, never a click or inventory revision, owns progress.
                if (_recipeAttempts.Contains(owner)) choice = available.FirstOrDefault(value => value.Kind == "recipe-back");
                else if (_goal is { } sourceGoal)
                {
                    var matches = available.Where(value => (value.Kind is "recipe-accept" or "recipe-row") &&
                            value.SourceNames?.Any(name => ObjectiveNamesRecipe(sourceGoal.ObjectiveText, name)) == true)
                        .GroupBy(value => value.Identity, StringComparer.Ordinal).ToArray();
                    if (matches.Length != 1)
                        return Fail("bot-policy", "Source objective does not name exactly one offered craftable recipe; manual recipe selection is required.", false);
                    choice = matches[0].FirstOrDefault(value => value.Kind == "recipe-accept") ?? matches[0].First();
                }
                else choice = available.FirstOrDefault(value => value.Kind == "recipe-accept") ??
                    available.FirstOrDefault(value => value.Kind == "recipe-row") ?? available.FirstOrDefault(value => value.Kind == "recipe-back");
                if (choice?.Kind is "recipe-accept" or "recipe-row")
                {
                    _recipeAttempts.Add(owner); _recipeMenu = owner; _quantityElapsed = 0;
                }
            }
            else choice = available.OrderBy(value => value.Goodbye || EndsTutorial(value.Text)).FirstOrDefault();
            if (choice is null)
                return Fail("bot-policy", "All currently offered source menu choices were attempted without new observable state.");
            if (choice.Kind == "dialogue") _dialogueChoices.Add((observation.Progress, choice.Identity));
            else _choices.Add(menu + "\n" + choice.Identity);
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
            !ActivationFailed(value, observation.Progress) &&
            (!_attemptedGoals.TryGetValue(value.Identity, out var previous) || previous != observation.Progress));
        if (goal is null)
        {
            Phase = observation.Goals.Count == 0 ? "awaiting-source-objective" : "awaiting-source-progress";
            return new(BotCampaignCommandKind.None);
        }
        if (goal.Error is { } goalError) return Fail("engine-owner", goalError);
        if (string.IsNullOrWhiteSpace(goal.Reference) || goal.Mode is not ("interact" or "travel" or "approach" or "shoot" or "follow" or "combat"))
            return Fail("observation-loss", "Source campaign goal has no supported ordinary reference action.");
        _goal = goal; _attemptedGoals[goal.Identity] = observation.Progress; ++_goalAttempts;
        Phase = "starting-source-goal";
        var distance = GoalDistance(goal.Mode);
        return new(BotCampaignCommandKind.Reference, goal.Reference, goal.Mode, Distance: distance);
    }

    private static float GoalDistance(string mode) => mode == "travel" ? .5f : mode is "shoot" or "combat" ? 6f : 1.5f;

    private static bool IsRecipeMenu(string? kind) => kind is "recipe-row" or "recipe-accept" or "recipe-back";
    private static bool IsRecipeQuantity(string? kind) => kind is "recipe-quantity-decrease" or "recipe-quantity-confirm" or "recipe-quantity-cancel";

    private static bool ObjectiveNamesRecipe(string? objective, string name)
    {
        if (string.IsNullOrWhiteSpace(objective) || string.IsNullOrWhiteSpace(name)) return false;
        for (var start = 0; start < objective.Length;)
        {
            var index = objective.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            var end = index + name.Length;
            if ((index == 0 || !WordCharacter(objective[index - 1])) &&
                (end == objective.Length || !WordCharacter(objective[end]))) return true;
            start = index + 1;
        }
        return false;
    }

    private static bool WordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    internal static IEnumerable<BotCampaignControl> ActiveMenuControls(IReadOnlyList<BotCampaignControl> controls) =>
        controls.Any(control => IsRecipeMenu(control.Kind) || IsRecipeQuantity(control.Kind)) && controls.Any(control => control.Kind == "message")
            ? controls.Where(control => control.Kind == "message") :
        controls.Any(control => IsRecipeQuantity(control.Kind)) ? controls.Where(control => IsRecipeQuantity(control.Kind)) :
        controls.Any(control => IsRecipeMenu(control.Kind)) ? controls.Where(control => IsRecipeMenu(control.Kind)) :
        controls.Any(control => control.Kind == "barter-back") ? controls.Where(control => control.Kind == "barter-back") : controls;

    internal static IEnumerable<BotCampaignChoice> SourceMenuChoices(IReadOnlyList<BotCampaignControl> controls)
    {
        foreach (var control in ActiveMenuControls(controls))
            if (control.Kind is "dialogue" or "terminal" or "terminal-back" or "barter-back" or "message" or "chargen" or "special" ||
                IsRecipeMenu(control.Kind) || IsRecipeQuantity(control.Kind))
                yield return new(control.Identity ?? throw new InvalidDataException("Source choice has no identity."),
                    control.Path, control.Text, control.Kind!,
                    control.Menu ?? throw new InvalidDataException("Source choice has no owning menu."),
                    control.Goodbye || control.Kind == "terminal-back", control.SourceNames);
    }

    // This is a diagnostic input policy using the player's displayed objective,
    // not a quest transition. Unknown wording keeps the existing behavior.
    internal static bool RequestsConversation(string objective) =>
        objective.TrimStart().StartsWith("Talk to ", StringComparison.OrdinalIgnoreCase);

    internal static bool RequestsDefeat(string objective) =>
        objective.TrimStart().StartsWith("Kill ", StringComparison.OrdinalIgnoreCase);

    internal static bool RequestsFollow(string objective) =>
        objective.TrimStart().StartsWith("Follow ", StringComparison.OrdinalIgnoreCase);

    // A quest marker naming an actor is not permission to attack it. Combat
    // goals require both displayed defeat intent and observed hostility.
    internal static string SourceTargetMode(bool staticMarker, bool actor, bool defeat, bool hostile) =>
        staticMarker ? "travel" : actor && defeat && hostile ? "shoot" : "interact";

    internal static bool EndsTutorial(string text) =>
        text.TrimStart().StartsWith("[END TUTORIAL]", StringComparison.OrdinalIgnoreCase);

    internal static int NearbyUnselectedQuestBonus(float distanceMeters) =>
        float.IsFinite(distanceMeters) && distanceMeters >= 0 ? Math.Max(0, 1000 - (int)Math.Min(1000, distanceMeters)) : 0;

    private bool ActivationFailed(BotCampaignGoal goal, long progress) =>
        _failedActivations.TryGetValue((goal.Quest, goal.Objective, goal.Target), out var failed) && failed == progress;

    private static string MenuIdentity(BotCampaignObservation state) =>
        state.Scene + "\n" + state.Progress + "\n" +
        string.Join('\n', state.Choices.Select(choice => choice.Menu + ":" + choice.Identity));
}
