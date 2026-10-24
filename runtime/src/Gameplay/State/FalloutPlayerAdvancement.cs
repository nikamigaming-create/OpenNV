using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutLevelUpSkillRate { NewVegas, RestoredSkillPointBase }

internal sealed record FalloutLevelUpRules(FalloutLevelUpSkillRate SkillRate, int MaximumLevel,
    int LevelsPerPerk, int TaggedSkillMultiplier, int SkillPointBase)
{
    internal static FalloutLevelUpRules Read(FalloutPluginStack records, FalloutLevelUpSkillRate rate)
    {
        var rules = new FalloutLevelUpRules(rate,
            unchecked((int)FalloutGameSettingIntegers.Read(records, "iMaxCharacterLevel")),
            unchecked((int)FalloutGameSettingIntegers.Read(records, "iLevelsPerPerk")),
            unchecked((int)FalloutGameSettingIntegers.Read(records, "iSkillPointsTagSkillMult")),
            rate == FalloutLevelUpSkillRate.RestoredSkillPointBase ?
                unchecked((int)FalloutGameSettingIntegers.Read(records, "iLevelUpSkillPointsBase")) : 0);
        rules.Validate();
        return rules;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(SkillRate) || MaximumLevel is < 1 or > ushort.MaxValue || LevelsPerPerk <= 0 ||
            TaggedSkillMultiplier <= 0 || SkillPointBase < 0)
            throw new NotSupportedException("Level-up rules exceed their admitted source storage.");
    }
}

// The skill-rate receipt must identify an admitted engine/dependency contract;
// neither a plugin name nor the presence of a GMST selects the policy.
internal sealed record FalloutPlayerAdvancementSource(FalloutFormKey Player, string PlayerSha256,
    FalloutFormKey StatsOwner, string StatsSha256, FalloutLevelUpSkillRate SkillRate, string SkillRateSourceSha256)
{
    internal static FalloutPlayerAdvancementSource Bind(FalloutPlayerActorValueSource player,
        FalloutLevelUpSkillRate rate, string admittedContractSha256) => new(player.Player, player.PlayerSha256,
        player.StatsOwner, player.StatsSha256, rate, admittedContractSha256);

    internal void Validate()
    {
        if (Player.ObjectId == 0 || StatsOwner.ObjectId == 0 || !Enum.IsDefined(SkillRate) ||
            !Hash(PlayerSha256) || !Hash(StatsSha256) || !Hash(SkillRateSourceSha256))
            throw new InvalidDataException("Player advancement source receipt is invalid.");
    }
    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed record FalloutLevelUpAdmission(bool Ready, string? UnsupportedOwner = null);

internal sealed record FalloutPlayerAdvancementBinding(Func<int> Level, Func<int> Experience,
    Func<int, int> Threshold, Action<int> AdvanceLevel, Func<int> PermanentIntelligenceInteger,
    Func<FalloutLevelUpRules> Rules, Func<FalloutLevelUpAdmission> Admission,
    Func<int, FalloutLevelUpMenuBinding> Menu, Action<FalloutLevelUpMenuSession> Present,
    Action? RetirePresentationForCommit = null, Func<int, int, int>? SkillPointBudget = null);

internal sealed record FalloutPlayerAdvancementSnapshot(string Schema, FalloutPlayerAdvancementSource Source,
    ulong Generation, bool Pending, int ObservedLevel, int ObservedExperience, int? OpeningLevel,
    FalloutLevelUpMenuSnapshot? Menu, string? Error);

internal sealed class FalloutPlayerAdvancement
{
    internal const string SnapshotSchema = "opennv-player-advancement/v1";
    private readonly FalloutPlayerAdvancementSource _source;
    private readonly FalloutPlayerAdvancementBinding _binding;
    private ulong _generation;
    private bool _pending;
    private int? _openingLevel;
    private string? _error;
    internal FalloutLevelUpMenuSession? Menu { get; private set; }
    internal bool Pending => _pending;
    internal bool Active => Menu is { Completed: false };
    internal string? Error => _error ?? Menu?.Error;
    internal string? SaveBlocker => Error is not null ? "player-advancement-failure" :
        Active ? "player-level-up-menu" : _openingLevel is not null ? "player-advancement-opening" : null;

    internal FalloutPlayerAdvancement(FalloutPlayerAdvancementSource source,
        FalloutPlayerAdvancementBinding binding, FalloutPlayerAdvancementSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        source.Validate();
        if (binding.SkillPointBudget is null)
            throw new NotSupportedException("Player advancement requires its explicit qualified skill-point budget policy.");
        _source = source;
        _binding = binding;
        if (restore is not null) Restore(restore);
        else _pending = Due();
    }

    internal void RequestEligibility()
    {
        RequireHealthy();
        try { if (Due()) _pending = true; }
        catch (Exception error) { throw Retain(error); }
    }

    internal void EarnedExperience(int previous, int current)
    {
        RequireHealthy();
        if (previous < 0 || current < 0 || current != _binding.Experience())
            throw new InvalidDataException("Level-up XP notification differs from the authoritative player value.");
        if (current <= previous) return;
        try { if (Due()) _pending = true; }
        catch (Exception error) { throw Retain(error); }
    }

    internal bool TryOpen(bool inCharacterGeneration)
    {
        RequireHealthy();
        if (!_pending || Menu is not null || _openingLevel is not null || inCharacterGeneration) return false;
        try
        {
            var admission = _binding.Admission();
            if (admission.UnsupportedOwner is { } unsupported) throw new NotSupportedException(unsupported);
            if (!admission.Ready) return false;
            var rules = Rules();
            var level = _binding.Level();
            if (level >= rules.MaximumLevel || !Due()) return false; // Do not clear a latched request.
            _openingLevel = checked(level + 1);
            _generation = checked(_generation + 1);
            _binding.AdvanceLevel(_openingLevel.Value);
            if (_binding.Level() != _openingLevel.Value)
                throw new InvalidDataException("The level-up owner did not publish the requested player level.");
            var requested = (_binding.SkillPointBudget ?? throw new NotSupportedException(
                "Level-up skill-point rate/rounding requires its qualified allocation policy."))(
                    _binding.PermanentIntelligenceInteger(), _openingLevel.Value);
            if (requested < 0) throw new InvalidDataException("Level-up skill budget cannot be negative.");
            Menu = new(_source, _generation, _openingLevel.Value, requested, rules,
                _binding.Menu(_openingLevel.Value), PrepareCompletion);
            _binding.Present(Menu);
            return true;
        }
        catch (Exception error) { throw Retain(error); }
    }

    internal bool ReopenMenu()
    {
        RequireHealthy();
        if (Menu is not { Completed: false }) return false;
        try { _binding.Present(Menu); return true; }
        catch (Exception error) { throw Retain(error); }
    }

    internal bool FinishMenu()
    {
        RequireHealthy();
        if (Menu is not { Completed: true }) return false;
        try { _pending = Due(); }
        catch (Exception error) { throw Retain(error); }
        _openingLevel = null;
        Menu = null;
        return true;
    }

    private void PrepareCompletion()
    {
        // Release the presentation before an acquired perk can open another
        // modal owner. FinishMenu checks the next earned level after commit.
        _binding.RetirePresentationForCommit?.Invoke();
    }

    private bool Due()
    {
        var level = _binding.Level();
        if (level < 1 || _binding.Experience() < 0) throw new InvalidDataException("Player progression state is invalid.");
        if (level >= Rules().MaximumLevel) return false;
        var threshold = _binding.Threshold(checked(level + 1));
        if (threshold <= 0) throw new InvalidDataException("Level-up XP threshold is invalid.");
        return _binding.Experience() >= threshold;
    }

    private FalloutLevelUpRules Rules()
    {
        var rules = _binding.Rules();
        rules.Validate();
        if (rules.SkillRate != _source.SkillRate)
            throw new InvalidDataException("Level-up rules differ from their admitted source contract.");
        return rules;
    }

    private void RequireHealthy()
    {
        if (Error is not null) throw new InvalidOperationException("Player advancement retains a consumed failure: " + Error);
    }
    private Exception Retain(Exception error) { _error ??= error.Message; return error; }

    internal FalloutPlayerAdvancementSnapshot Capture() => new(SnapshotSchema, _source, _generation, _pending,
        _binding.Level(), _binding.Experience(), _openingLevel, Menu?.Capture(), _error);

    internal FalloutPlayerAdvancementSnapshot CaptureSettled()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException("Complete save cannot own " + blocker + ".");
        return Capture();
    }

    private void Restore(FalloutPlayerAdvancementSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Source.Validate();
        if (state.Schema != SnapshotSchema || state.Source != _source ||
            state.ObservedLevel < 1 || state.ObservedLevel != _binding.Level() ||
            state.ObservedExperience < 0 || state.ObservedExperience != _binding.Experience() ||
            state.OpeningLevel is { } level && (level <= 1 || state.Generation == 0 ||
                state.Error is null && level != state.ObservedLevel) ||
            state.Menu is { } menu && (state.OpeningLevel != menu.Level || state.Generation != menu.Generation) ||
            state.OpeningLevel is not null && state.Menu is null && state.Error is null ||
            state.OpeningLevel is null && state.Menu is not null ||
            state.Error is { Length: 0 })
            throw new InvalidDataException("Saved advancement differs from its source or consumed player state.");
        _generation = state.Generation;
        _pending = state.Pending;
        _openingLevel = state.OpeningLevel;
        _error = state.Error;
        if (state.Menu is { } saved)
            Menu = FalloutLevelUpMenuSession.Restore(_source, saved, Rules(), _binding.Menu(saved.Level), PrepareCompletion);
    }
}
