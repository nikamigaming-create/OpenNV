using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLevelUpRules(FalloutSkillPointRate SkillRate, int MaximumLevel,
    int? LevelsPerPerk, int TaggedSkillMultiplier, int SkillPointBase, int IntelligenceMultiplier = 1)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(SkillRate); SkillRate.Validate();
        if (MaximumLevel is < 1 or > ushort.MaxValue || LevelsPerPerk <= 0 || TaggedSkillMultiplier <= 0 ||
            SkillRate.Base.Setting is null && SkillPointBase != SkillRate.Base.Constant ||
            SkillRate.IntelligenceMultiplier.Setting is null && IntelligenceMultiplier != SkillRate.IntelligenceMultiplier.Constant)
            throw new NotSupportedException("Level-up rules differ from their admitted source operands or storage.");
    }

    // A reviewed source declaration chooses the consumed operands. Their live
    // GMST values neither select the arithmetic owner nor replace that receipt.
    internal static FalloutLevelUpRules Read(FalloutPluginStack records, FalloutAdvancementRuntimeReceipt source)
    {
        source.Validate();
        var rate = source.SkillRate;
        int Integer(string name) => unchecked((int)FalloutGameSettingIntegers.Read(records, name));
        var result = new FalloutLevelUpRules(rate, Integer("iMaxCharacterLevel"), source.PerkCadence.ReadInterval(records),
            Integer("iSkillPointsTagSkillMult"), rate.Base.Resolve(Integer), rate.IntelligenceMultiplier.Resolve(Integer));
        result.Validate(); return result;
    }

    internal int SkillPoints(int permanentIntelligence, int gainedLevel)
    {
        Validate();
        return SkillRate.Points(permanentIntelligence, gainedLevel, SkillPointBase, IntelligenceMultiplier);
    }
}

internal sealed record FalloutPlayerAdvancementSource(FalloutFormKey Player, string PlayerSha256,
    FalloutFormKey StatsOwner, string StatsSha256, FalloutAdvancementRuntimeReceipt Runtime)
{
    internal FalloutSkillPointRate SkillRate => Runtime.SkillRate;
    internal string SkillRateSourceSha256 => Runtime.SourceSha256;
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Runtime); Runtime.Validate();
        if (Player.ObjectId == 0 || StatsOwner.ObjectId == 0 ||
            !FalloutAdvancementRuntimeReceipt.Digest(PlayerSha256) || !FalloutAdvancementRuntimeReceipt.Digest(StatsSha256))
            throw new InvalidDataException("Player advancement source receipt is invalid.");
    }
}

internal sealed record FalloutLevelUpAdmission(bool Ready, string? UnsupportedOwner = null);

internal sealed record FalloutPlayerAdvancementBinding(Func<int> Level, Func<int> Experience,
    Func<int, int> Threshold, Action<int> AdvanceLevel, Func<int> PermanentIntelligenceInteger,
    Func<FalloutLevelUpRules> Rules, Func<FalloutLevelUpAdmission> Admission,
    Func<int, FalloutLevelUpMenuBinding> Menu, Action<FalloutLevelUpMenuSession> Present);

internal sealed record FalloutPlayerAdvancementSnapshot(string Schema, FalloutPlayerAdvancementSource Source,
    ulong Generation, bool Pending, int ObservedLevel, int? OpeningLevel, FalloutLevelUpMenuSnapshot? Menu, string? Error);

// XP award and character-generation flags do not open this menu inline. An
// admitted engine update consumes one level, then opens that level's menu.
internal sealed partial class FalloutPlayerAdvancement
{
    internal const string SnapshotSchema = "opennv-player-advancement/v1";
    private readonly FalloutPlayerAdvancementSource _source;
    private readonly FalloutPlayerAdvancementBinding _binding;
    private ulong _generation;
    private bool _pending;
    private int? _openingLevel;
    private string? _error;
    private FalloutLevelUpMenuSession? _retiredNativeMenu;
    internal FalloutLevelUpMenuSession? Menu { get; private set; }
    internal FalloutPlayerAdvancementSource Source => _source;
    internal bool Pending => _pending;
    internal bool Active => Menu is { Completed: false };
    internal string? Error => _error ?? Menu?.Error;
    internal string? SaveBlocker => Error is not null ? "player-advancement-failure" :
        Active ? "player-level-up-menu" : _openingLevel is not null && Menu is null ? "player-advancement-opening" : null;

    internal FalloutPlayerAdvancement(FalloutPlayerAdvancementSource source,
        FalloutPlayerAdvancementBinding binding, FalloutPlayerAdvancementSnapshot? restore = null)
    {
        source.Validate(); _source = source; _binding = binding;
        if (restore is not null) Restore(restore);
    }

    internal void EarnedExperience(int previous, int current)
    {
        if (previous < 0 || current < 0 || current != _binding.Experience())
            throw new InvalidDataException("Level-up XP notification differs from the authoritative player value.");
        if (current <= previous) return;
        var rules = Rules();
        if (_binding.Level() < rules.MaximumLevel && Due()) _pending = true;
    }

    internal bool TryOpen(bool inCharacterGeneration)
    {
        RequireHealthy();
        if (!_pending || Menu is not null || inCharacterGeneration) return false;
        try
        {
            var admission = _binding.Admission();
            if (admission.UnsupportedOwner is { } unsupported)
                throw new NotSupportedException(unsupported);
            if (!admission.Ready) return false;
            var rules = Rules();
            var level = _binding.Level();
            if (level >= rules.MaximumLevel)
                throw new NotSupportedException("A retained level-up request no longer fits the source level cap.");
            _openingLevel = checked(level + 1);
            _generation = checked(_generation + 1);
            // Retain the attempted operation before calling an owner which may
            // publish a prefix and then fail. It is never retried implicitly.
            _binding.AdvanceLevel(_openingLevel.Value);
            if (_binding.Level() != _openingLevel.Value)
                throw new InvalidDataException("The level-up owner did not publish the requested player level.");
            var requested = rules.SkillPoints(_binding.PermanentIntelligenceInteger(), _openingLevel.Value);
            Menu = new(_source, _generation, _openingLevel.Value, requested, rules, _binding.Menu(_openingLevel.Value), PrepareCompletion);
            PublishMenu();
            return true;
        }
        catch (Exception error) when (Retainable(error)) { throw Retain(error); }
    }

    internal bool FinishMenu()
    {
        RequireHealthy();
        if (Menu is not { Completed: true }) return false;
        // Retain this actual process-local session through its native input/
        // pause retirement. A later release callback can fail after completion
        // has committed and Menu has already been cleared.
        _retiredNativeMenu = Menu;
        _openingLevel = null;
        Menu = null;
        _menuPublished = false;
        // Another due level waits for the next admitted update. This method
        // never opens a second menu or reapplies the completed perk receipt.
        return true;
    }

    private void PrepareCompletion()
    {
        // Source completion recomputes the pending request BEFORE applying the
        // selected perk. A perk's later source effects can change XP and queue
        // another request through the shared award notification.
        _pending = _binding.Level() < Rules().MaximumLevel && Due();
    }

    private bool Due()
    {
        var threshold = _binding.Threshold(checked(_binding.Level() + 1));
        if (threshold <= 0) throw new InvalidDataException("Level-up XP threshold is invalid.");
        return _binding.Experience() >= threshold;
    }
    private FalloutLevelUpRules Rules()
    {
        var rules = _binding.Rules(); rules.Validate();
        if (rules.SkillRate != _source.SkillRate)
            throw new InvalidDataException("Level-up rules differ from their admitted source contract.");
        return rules;
    }
    private void RequireHealthy()
    {
        if (Error is not null) throw new InvalidOperationException("Player advancement retains a failed consumed operation: " + Error);
    }
    private Exception Retain(Exception error) { _error ??= FailureMessage(error); return error; }
    private static string FailureMessage(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    // Any ordinary callback fault can follow a committed prefix. Keep that
    // prefix and refuse retry regardless of the callback's exception type.
    private static bool Retainable(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal FalloutPlayerAdvancementSnapshot Capture() => new(SnapshotSchema, _source, _generation, _pending, _binding.Level(),
        _openingLevel, Menu?.Capture(), _error);

    internal static void ValidateSnapshot(FalloutPlayerAdvancementSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Source);
        state.Source.Validate();
        if (state.Schema != SnapshotSchema || state.ObservedLevel < 1 ||
            state.OpeningLevel is { } level && (level <= 1 || state.Error is null && level != state.ObservedLevel) ||
            state.Menu is { } menu && (state.OpeningLevel != menu.Level || state.Generation != menu.Generation || menu.Source != state.Source) ||
            state.OpeningLevel is not null && state.Generation == 0 ||
            state.OpeningLevel is not null && state.Menu is null && state.Error is null || state.Error is { Length: 0 })
            throw new InvalidDataException("Saved advancement has inconsistent consumed-operation shape.");
        if (state.Menu is { } saved) FalloutLevelUpMenuSession.ValidateSnapshot(saved);
    }

    private void Restore(FalloutPlayerAdvancementSnapshot state)
    {
        ValidateSnapshot(state);
        if (state.Schema != SnapshotSchema || state.Source != _source ||
            state.ObservedLevel < 1 || state.ObservedLevel != _binding.Level() ||
            state.OpeningLevel is { } level && (level <= 1 || state.Error is null && level != state.ObservedLevel) ||
            state.Menu is { } menu && (state.OpeningLevel != menu.Level || state.Generation != menu.Generation) ||
            state.OpeningLevel is not null && state.Menu is null && state.Error is null ||
            state.Error is { Length: 0 })
            throw new InvalidDataException("Saved player advancement differs from its source or consumed player state.");
        _generation = state.Generation; _pending = state.Pending; _openingLevel = state.OpeningLevel; _error = state.Error;
        if (state.Menu is { } saved)
            Menu = FalloutLevelUpMenuSession.Restore(_source, saved, Rules(), _binding.Menu(saved.Level), PrepareCompletion);
    }
}
