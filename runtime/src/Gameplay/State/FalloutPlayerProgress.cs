using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerXpOrigin(ulong Generation, int Level, int PreviousExperience, int PublishedExperience);

internal sealed record FalloutPlayerProgressSnapshot(string Schema, uint Reference,
    FalloutFormKey Player, string PlayerSha256, FalloutFormKey StatsOwner, string StatsSha256,
    int Level, int Experience, int NextThreshold, ulong XpGeneration,
    int? LastPreviousExperience, int? LastPublishedExperience, bool PendingXpOrigin, FalloutPlayerXpOrigin? XpOrigin,
    FalloutPlayerSkillValuesSnapshot Skills, FalloutPlayerAdvancementSnapshot? Advancement, string? Error);

// XP, skills and level views share the actual owners. Pending requests come
// from committed XP changes, never from guessing a prior menu from a save.
internal sealed partial class FalloutPlayerProgress
{
    internal const string SnapshotSchema = "opennv-player-progress/v1";
    private readonly FalloutPlayerVitals _vitals;
    private readonly FalloutPlayerSkills _skills;
    private readonly FalloutPlayerActorValueSource _source;
    private readonly FalloutPlayerActorValues _values;
    private readonly FalloutPlayerExperience _experience;
    private readonly FalloutPluginStack _records;
    private ulong _xpGeneration;
    private int? _lastPrevious, _lastPublished;
    private bool _pendingXpOrigin;
    private FalloutPlayerXpOrigin? _xpOrigin;
    private string? _error;
    private FalloutPlayerAdvancementSnapshot? _savedAdvancement;
    private FalloutPlayerAdvancement? _advancement;

    internal int Level => _vitals.State.Level;
    internal bool Pending => _advancement?.Pending ?? _pendingXpOrigin;
    internal FalloutLevelUpMenuSession? Menu => _advancement?.Menu;
    internal string? Error => _error ?? _advancement?.Error ?? _savedAdvancement?.Error ?? _savedAdvancement?.Menu?.Error;
    internal string? SaveBlocker => Error is not null ? "player-advancement-failure" :
        (_advancement?.Menu is not null ? "player-level-up-menu" : _advancement?.SaveBlocker) ?? (_savedAdvancement?.Menu is not null ? "player-level-up-menu" : null) ?? (Pending && _advancement is null ? "player-advancement-owner" : null);
    internal object State => new
    {
        level = Level,
        experience = _vitals.State.ExperiencePoints,
        nextThreshold = _vitals.State.NextLevelExperiencePoints,
        xpGeneration = _xpGeneration,
        pending = Pending,
        nativeOwnerBound = _advancement is not null,
        rateSource = (_advancement?.Source ?? _savedAdvancement?.Source)?.Runtime,
        menuLevel = Menu?.Level,
        menuPage = Menu?.Page.ToString(),
        menuBudget = Menu?.Budget,
        menuAssigned = Menu?.Assigned,
        failure = Error,
        saveBlocker = SaveBlocker,
    };

    internal FalloutPlayerProgress(FalloutPluginStack records, FalloutPlayerVitals vitals,
        FalloutPlayerActorValues values, FalloutPlayerSkills skills, FalloutPlayerExperience experience,
        FalloutPlayerProgressSnapshot? restore = null)
    {
        _records = records; _vitals = vitals; _source = values.Source; _values = values; _skills = skills; _experience = experience;
        _vitals.RequireProgressJoin(Level, _vitals.State.ExperiencePoints, _vitals.State.NextLevelExperiencePoints);
        if (restore is not null) Restore(restore);
        _experience.ExperienceChanged += ObserveExperience;
    }

    internal int Reward(double operand)
    {
        RequireHealthy();
        return _experience.Reward(operand);
    }

    private void ObserveExperience(int previous, int published)
    {
        try
        {
            if (previous < 0 || published < 0 || published != _vitals.State.ExperiencePoints)
                throw new InvalidDataException("XP publication differs from the persistent player owner.");
            _lastPrevious = previous; _lastPublished = published;
            _xpGeneration = checked(_xpGeneration + 1);
            if (published > previous && Level < MaximumLevel() && published >= _vitals.ExperienceThreshold(checked(Level + 1)))
            {
                if (_xpOrigin is null || _advancement is { Pending: false })
                    _xpOrigin = new(_xpGeneration, Level, previous, published);
                _pendingXpOrigin = true;
            }
            _advancement?.EarnedExperience(previous, published);
        }
        catch (Exception error) when (Retainable(error)) { _error ??= FailureMessage(error); throw; }
    }

    // The active engine/dependency owner must supply its admitted skill-rate
    // source and actual activity/UI admission. A plugin name cannot select it.
    internal void BindAdvancement(FalloutPlayerAdvancementSource source, FalloutPlayerAdvancementBinding binding)
    {
        RequireHealthy(); source.Validate();
        if (_advancement is not null || source.Player != _source.Player || source.PlayerSha256 != _source.PlayerSha256 ||
            source.StatsOwner != _source.StatsOwner || source.StatsSha256 != _source.StatsSha256 ||
            binding.Level() != Level || binding.Experience() != _vitals.State.ExperiencePoints ||
            binding.Threshold(checked(Level + 1)) != _vitals.State.NextLevelExperiencePoints)
            throw new InvalidDataException("Advancement binding differs from the current persistent player owner.");
        if (_pendingXpOrigin && _savedAdvancement is null && _vitals.State.ExperiencePoints < _vitals.State.NextLevelExperiencePoints)
            throw new NotSupportedException("Reduced XP cannot reconstruct a retained queue without the original consumed request owner.");
        // Presentation can provide source admission and menu semantics, but
        // cannot substitute another level/XP/skill owner for this player.
        if (binding.Rules() != FalloutLevelUpRules.Read(_records, source.SkillRate))
            throw new InvalidDataException("Advancement rules differ from the winning source settings and admitted rate.");
        var actual = binding with
        {
            Rules = () => FalloutLevelUpRules.Read(_records, source.SkillRate),
            Level = () => _vitals.State.Level,
            Experience = () => _vitals.State.ExperiencePoints,
            Threshold = _vitals.ExperienceThreshold,
            AdvanceLevel = _vitals.AdvancePlayerLevel,
            PermanentIntelligenceInteger = () => source.Runtime.IntelligenceGetter.Read(_values.ReadRawPermanent(9)),
            Menu = level => BindActualSkills(binding.Menu(level)),
        };
        var advancement = new FalloutPlayerAdvancement(source, actual, _savedAdvancement);
        if (_savedAdvancement is null && _pendingXpOrigin)
            advancement.EarnedExperience((_xpOrigin ?? throw new InvalidDataException("Pending advancement has no XP publication.")).PreviousExperience,
                _vitals.State.ExperiencePoints);
        _advancement = advancement; _savedAdvancement = null;
    }

    private FalloutLevelUpMenuBinding BindActualSkills(FalloutLevelUpMenuBinding binding)
    {
        if (binding.SkillOrder is null || !binding.SkillOrder.Order().SequenceEqual(_skills.SkillOrder.Order()))
            throw new InvalidDataException("Level-up source rows differ from the selected player skill catalog.");
        if (binding.Perks is null || binding.Perks.Any(choice => choice is null))
            throw new InvalidDataException("Level-up source candidates are absent or null.");
        foreach (var choice in binding.Perks)
        {
            var original = FalloutLevelUpPerkSource.Read(_records, choice.Form);
            if (original.SourceSha256 != choice.SourceSha256 || original.MaximumRank != choice.MaximumRank)
                throw new InvalidDataException("Level-up candidate differs from its original winning PERK.");
        }
        return binding with
        {
            ReadUnmodifiedSkill = _skills.ReadUnmodifiedSkill,
            WriteUnmodifiedSkill = _skills.WriteUnmodifiedSkill,
            Tagged = _skills.IsTaggedSkill,
            DisplayedSkill = value =>
            {
                var current = _skills.Value(value);
                if (current != MathF.Truncate(current))
                    throw new NotSupportedException("Fractional displayed skill requires its original menu integer getter contract.");
                return checked((int)current);
            },
        };
    }

    internal bool TryOpen(bool inCharacterGeneration)
    {
        RequireHealthy();
        if (!Pending || inCharacterGeneration || Menu is not null) return false;
        if (_pendingXpOrigin && _vitals.State.ExperiencePoints < _vitals.ExperienceThreshold(checked(Level + 1)))
            throw new NotSupportedException("A retained XP-origin request with subsequently reduced XP requires the original queue consumer.");
        if (_advancement is null)
            throw new NotSupportedException("Earned XP awaits the actual engine/dependency advancement, activity and source LevelUpMenu owners.");
        try { return _advancement.TryOpen(inCharacterGeneration); }
        catch (Exception error) when (Retainable(error)) { _error ??= FailureMessage(error); throw; }
    }

    internal bool FinishMenu()
    {
        RequireHealthy();
        if (_advancement is null) return false;
        var finished = _advancement.FinishMenu();
        if (finished)
        {
            _pendingXpOrigin = _advancement.Pending;
            if (!_pendingXpOrigin) _xpOrigin = null;
        }
        return finished;
    }

    internal FalloutPlayerProgressSnapshot Capture()
    {
        var state = _vitals.State;
        var snapshot = new FalloutPlayerProgressSnapshot(SnapshotSchema, FalloutPlayerActorValues.PlayerReference,
            _source.Player, _source.PlayerSha256, _source.StatsOwner, _source.StatsSha256,
            state.Level, state.ExperiencePoints, state.NextLevelExperiencePoints, _xpGeneration,
            _lastPrevious, _lastPublished, Pending, _xpOrigin, _skills.CaptureValues(), _advancement?.Capture() ?? _savedAdvancement, _error);
        Validate(snapshot);
        return snapshot;
    }

    private void Restore(FalloutPlayerProgressSnapshot state)
    {
        Validate(state);
        if (state.Player != _source.Player || state.PlayerSha256 != _source.PlayerSha256 ||
            state.StatsOwner != _source.StatsOwner || state.StatsSha256 != _source.StatsSha256)
            throw new InvalidDataException("Saved player progression differs from its winning player/stats sources.");
        _vitals.RequireProgressJoin(state.Level, state.Experience, state.NextThreshold);
        if (state.Advancement is null && state.Level != _source.Level ||
            state.Advancement is { Error: null } advanced && (ulong)(state.Level - _source.Level) != advanced.Generation)
            throw new InvalidDataException("Saved current level has no matching consumed advancement owner.");
        _skills.RestoreValues(state.Skills);
        _xpGeneration = state.XpGeneration; _lastPrevious = state.LastPreviousExperience;
        _lastPublished = state.LastPublishedExperience; _pendingXpOrigin = state.PendingXpOrigin;
        _savedAdvancement = state.Advancement; _error = state.Error; _xpOrigin = state.XpOrigin;
        if (_xpOrigin is { } origin && origin.PublishedExperience < _vitals.ExperienceThreshold(checked(origin.Level + 1)))
            throw new InvalidDataException("Saved XP-origin request never reached its source level threshold.");
    }

    internal static void Validate(FalloutPlayerProgressSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Schema != SnapshotSchema || state.Reference != FalloutPlayerActorValues.PlayerReference ||
            state.Player.ObjectId == 0 || state.StatsOwner.ObjectId == 0 || !Hash(state.PlayerSha256) || !Hash(state.StatsSha256) ||
            state.Level < 1 || state.Experience < 0 || state.NextThreshold <= 0 || state.Skills is null ||
            state.LastPreviousExperience is < 0 || state.LastPublishedExperience is < 0 ||
            state.XpGeneration == 0 && (state.LastPreviousExperience is not null || state.LastPublishedExperience is not null || state.PendingXpOrigin) ||
            state.XpGeneration != 0 && (state.LastPreviousExperience is null || state.LastPublishedExperience is null || state.LastPublishedExperience != state.Experience) ||
            state.PendingXpOrigin && state.XpOrigin is null ||
            state.XpOrigin is { } origin && (origin.Generation == 0 || origin.Generation > state.XpGeneration || origin.Level < 1 || origin.Level > state.Level ||
                origin.PreviousExperience < 0 || origin.PublishedExperience <= origin.PreviousExperience) ||
            state.Skills.Player != state.Player || state.Skills.PlayerSha256 != state.PlayerSha256 ||
            state.Skills.StatsOwner != state.StatsOwner || state.Skills.StatsSha256 != state.StatsSha256 ||
            state.Advancement is { Source: null } ||
            state.Advancement is { } advancement && (advancement.Source.Player != state.Player || advancement.Source.PlayerSha256 != state.PlayerSha256 ||
                advancement.Source.StatsOwner != state.StatsOwner || advancement.Source.StatsSha256 != state.StatsSha256 ||
                advancement.ObservedLevel != state.Level || advancement.Pending != state.PendingXpOrigin) || state.Error is { Length: 0 })
            throw new InvalidDataException("Saved player progression has inconsistent source, XP-origin or primary-state joins.");
        FalloutPlayerSkills.ValidateValues(state.Skills);
        if (state.Advancement is { } saved) FalloutPlayerAdvancement.ValidateSnapshot(saved);
        static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private int MaximumLevel()
    {
        var level = unchecked((int)FalloutGameSettingIntegers.Read(_records, "iMaxCharacterLevel"));
        return level > 0 ? level : throw new InvalidDataException("Source player level cap is not positive.");
    }
    private void RequireHealthy()
    {
        if (Error is not null) throw new InvalidOperationException("Player progression retains a failed operation: " + Error);
    }
    // Any ordinary callback fault can follow a committed prefix. Keep that
    // prefix and refuse retry regardless of the callback's exception type.
    private static bool Retainable(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    private static string FailureMessage(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
}
