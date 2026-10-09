using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutLevelUpPage { Skills, Perks, Complete }
internal sealed record FalloutLevelUpPerkChoice(FalloutFormKey Form, string SourceSha256, int MaximumRank);
internal sealed record FalloutLevelUpMenuBinding(IReadOnlyList<int> SkillOrder,
    Func<int, float> ReadUnmodifiedSkill, Action<int, float> WriteUnmodifiedSkill,
    Func<int, int> DisplayedSkill, Func<int, bool> Tagged,
    Func<int, int> AdmittedSkillBudget, IReadOnlyList<FalloutLevelUpPerkChoice> Perks,
    Func<FalloutFormKey, bool> PerkEnabled, Func<FalloutFormKey, int> PerkRank,
    Action<FalloutFormKey, int> AcquirePerkRank);

internal enum FalloutLevelUpOperationKind { SkillClick, SkillResetRead, SkillResetWrite, CompletionScheduling, PerkRankRead, PerkAcquisition }
internal sealed record FalloutLevelUpOperation(FalloutLevelUpOperationKind Kind, int? Skill, FalloutFormKey? Perk,
    float? Before, float? Requested);
internal sealed record FalloutLevelUpMenuSnapshot(FalloutPlayerAdvancementSource Source, ulong Generation, int Level,
    FalloutLevelUpRules Rules, int Budget, int AssignedSkillPoints, FalloutLevelUpPage Page, IReadOnlyDictionary<int, int> AllocatedPoints,
    IReadOnlyDictionary<int, int> AllocatedDeltas,
    IReadOnlyDictionary<int, float> UnmodifiedSkills, FalloutLevelUpPerkChoice? SelectedPerk,
    int? PerkRankBefore, int? PerkObservedRank, bool CompletionPrepared, bool PerkAttempted, bool PerkCommitted,
    FalloutLevelUpOperation? Operation, string? Error);

// UI adapts the winning LevelUpMenu source tiles to these operations. Skills
// are changed through the existing player owner when clicked; they are not a
// draft applied later by the UI. The saved values below are validation joins.
internal sealed partial class FalloutLevelUpMenuSession
{
    private readonly FalloutPlayerAdvancementSource _source;
    private readonly FalloutLevelUpRules _rules;
    private readonly FalloutLevelUpMenuBinding _binding;
    private readonly Dictionary<int, int> _allocated = [];
    private readonly Dictionary<int, int> _deltas = [];
    private readonly Action _prepareCompletion;
    private readonly ulong _generation;
    private int _assigned;
    private FalloutLevelUpPerkChoice? _selected;
    private int? _rankBefore;
    private bool _completionPrepared, _perkAttempted, _perkCommitted;
    private FalloutLevelUpOperation? _operation;
    internal string? Error { get; private set; }
    internal int Level { get; }
    internal int Budget { get; }
    internal int Assigned => _assigned;
    internal FalloutLevelUpPage Page { get; private set; }
    internal bool Completed => Page == FalloutLevelUpPage.Complete;
    internal bool HasPerkPage => (_rules.LevelsPerPerk is not { } interval || Level % interval == 0) && _binding.Perks.Count != 0;
    internal IReadOnlyDictionary<int, int> Allocated => _allocated;
    internal IReadOnlyDictionary<int, int> AllocatedDeltas => _deltas;
    internal IReadOnlyList<int> Skills => _binding.SkillOrder;
    internal IReadOnlyList<FalloutLevelUpPerkChoice> Perks => _binding.Perks;
    internal FalloutLevelUpPerkChoice? SelectedPerk => _selected;
    internal bool CanContinue => Error is null && (Page == FalloutLevelUpPage.Skills ? Assigned == Budget :
        Page == FalloutLevelUpPage.Perks && _selected is not null && PerkState(_selected.Form).Enabled);

    internal FalloutLevelUpMenuSession(FalloutPlayerAdvancementSource source, ulong generation, int level,
        int requestedBudget, FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding, Action prepareCompletion)
    {
        _source = source; _generation = generation; Level = level; _rules = rules; _binding = binding;
        _prepareCompletion = prepareCompletion;
        ValidateBinding();
        Budget = _binding.AdmittedSkillBudget(requestedBudget);
        // Entry-point effects can increase the source budget. The bound owner
        // also reduces it to the available skill capacity before publication.
        if (Budget < 0)
            throw new InvalidDataException("Level-up skill budget exceeds its admitted signed storage.");
    }

    private FalloutLevelUpMenuSession(FalloutPlayerAdvancementSource source, FalloutLevelUpMenuSnapshot state,
        FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding, Action prepareCompletion)
    {
        ValidateSnapshot(state);
        _source = source; _generation = state.Generation; Level = state.Level; Budget = state.Budget;
        _rules = rules; _binding = binding; ValidateBinding();
        _prepareCompletion = prepareCompletion;
        if (state.Source != source || state.Generation == 0 || state.Level <= 1 || state.Budget < 0 ||
            state.Rules != rules || !Enum.IsDefined(state.Page) || state.AllocatedPoints is null || state.AllocatedDeltas is null || state.UnmodifiedSkills is null ||
            state.Error is null && state.AssignedSkillPoints > state.Budget ||
            state.AllocatedPoints.Any(pair => !_binding.SkillOrder.Contains(pair.Key)) ||
            state.AllocatedDeltas.Any(pair => !_binding.SkillOrder.Contains(pair.Key)) ||
            !state.AllocatedPoints.Keys.Order().SequenceEqual(state.AllocatedDeltas.Keys.Order()) ||
            state.Error is null && state.AssignedSkillPoints != state.AllocatedPoints.Values.Sum(value => (long)value) ||
            state.UnmodifiedSkills.Count != _binding.SkillOrder.Count || _binding.SkillOrder.Any(value =>
                !state.UnmodifiedSkills.TryGetValue(value, out var saved) || !float.IsFinite(saved) || saved != _binding.ReadUnmodifiedSkill(value)) ||
            state.Operation is not null && (state.Error is null || !Enum.IsDefined(state.Operation.Kind) ||
                state.Operation.Before is { } before && !float.IsFinite(before) ||
                state.Operation.Requested is { } requested && !float.IsFinite(requested)) ||
            state.PerkAttempted && state.SelectedPerk is null ||
            state.PerkCommitted && (!state.PerkAttempted || state.PerkRankBefore is null) ||
            state.Page == FalloutLevelUpPage.Complete && (state.Error is not null || !state.CompletionPrepared) || state.Error is { Length: 0 })
            throw new InvalidDataException("Saved level-up menu differs from its source or consumed skill state.");
        foreach (var pair in state.AllocatedPoints) _allocated.Add(pair.Key, pair.Value);
        foreach (var pair in state.AllocatedDeltas) _deltas.Add(pair.Key, pair.Value);
        _assigned = state.AssignedSkillPoints;
        Page = state.Page; _selected = state.SelectedPerk; _rankBefore = state.PerkRankBefore;
        _completionPrepared = state.CompletionPrepared;
        _perkAttempted = state.PerkAttempted; _perkCommitted = state.PerkCommitted; _operation = state.Operation; Error = state.Error;
        if (_selected is not null && !_binding.Perks.Contains(_selected))
            throw new InvalidDataException("Saved level-up perk differs from its winning source choice.");
        if (Page == FalloutLevelUpPage.Perks && !HasPerkPage)
            throw new InvalidDataException("Saved level-up perk page differs from its source cadence or choices.");
        if (Page == FalloutLevelUpPage.Complete && (Assigned != Budget || HasPerkPage && (!_perkCommitted || _selected is null)))
            throw new InvalidDataException("Saved level-up completion has an unconsumed allocation or perk.");
        if (_perkCommitted && _binding.PerkRank(_selected!.Form) != _rankBefore + 1)
            throw new InvalidDataException("Saved level-up commit differs from the shared acquired perk rank.");
        if (state.PerkObservedRank is { } rank && (_selected is null || _binding.PerkRank(_selected.Form) != rank))
            throw new InvalidDataException("Saved level-up receipt differs from its observed acquired perk rank.");
    }

    internal static void ValidateSnapshot(FalloutLevelUpMenuSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Source);
        ArgumentNullException.ThrowIfNull(state.Rules);
        state.Source.Validate(); state.Rules.Validate();
        if (state.Generation == 0 || state.Level <= 1 || state.Budget < 0 || state.Source.SkillRate != state.Rules.SkillRate ||
            !Enum.IsDefined(state.Page) || state.AllocatedPoints is null || state.AllocatedDeltas is null || state.UnmodifiedSkills is null ||
            state.UnmodifiedSkills.Count is < 1 or > 13 || state.UnmodifiedSkills.Any(pair => pair.Key is < 32 or > 45 || !float.IsFinite(pair.Value)) ||
            state.AllocatedPoints.Keys.Any(key => !state.UnmodifiedSkills.ContainsKey(key)) ||
            !state.AllocatedPoints.Keys.Order().SequenceEqual(state.AllocatedDeltas.Keys.Order()) ||
            state.Error is null && (state.AssignedSkillPoints < 0 || state.AssignedSkillPoints > state.Budget ||
                state.AllocatedPoints.Values.Any(value => value < 0) || state.AllocatedDeltas.Values.Any(value => value < 0) ||
                state.AssignedSkillPoints != state.AllocatedPoints.Values.Sum(value => (long)value)) ||
            state.Operation is not null && (state.Error is null || !Enum.IsDefined(state.Operation.Kind) ||
                state.Operation.Before is { } before && !float.IsFinite(before) || state.Operation.Requested is { } requested && !float.IsFinite(requested)) ||
            state.SelectedPerk is { } perk && (perk.Form.ObjectId == 0 || perk.MaximumRank is < 1 or > byte.MaxValue || !Hash(perk.SourceSha256)) ||
            state.PerkAttempted && state.SelectedPerk is null || state.PerkCommitted && (!state.PerkAttempted || state.PerkRankBefore is null) ||
            state.Page == FalloutLevelUpPage.Complete && (state.Error is not null || !state.CompletionPrepared) || state.Error is { Length: 0 })
            throw new InvalidDataException("Saved level-up menu has invalid primary joins or consumed-prefix shape.");
        static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private void ValidateBinding()
    {
        _source.Validate(); _rules.Validate();
        if (_source.SkillRate != _rules.SkillRate || _generation == 0 || Level <= 1 || _binding.SkillOrder is null || _binding.SkillOrder.Count == 0 ||
            _binding.SkillOrder.Distinct().Count() != _binding.SkillOrder.Count || _binding.Perks is null ||
            _binding.Perks.Select(choice => choice.Form).Distinct().Count() != _binding.Perks.Count ||
            _binding.Perks.Any(choice => choice.Form.ObjectId == 0 || choice.MaximumRank is < 1 or > byte.MaxValue ||
                choice.SourceSha256 is not { Length: 64 } || choice.SourceSha256.Any(character =>
                    character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("Level-up source skill/perk binding is invalid.");
    }

    internal bool ChangeSkill(int skill, int direction)
    {
        RequireHealthy();
        if (!_binding.SkillOrder.Contains(skill) || direction is not (-1 or 1))
            throw new InvalidDataException("Level-up skill operation is outside its source rows.");
        if (Page != FalloutLevelUpPage.Skills || direction > 0 && Assigned >= Budget ||
            direction < 0 && (_deltas.GetValueOrDefault(skill) <= 0 || _binding.DisplayedSkill(skill) <= 0)) return false;
        var step = _binding.Tagged(skill) ? _rules.TaggedSkillMultiplier : 1;
        // The source arrow tests the current displayed value. A tagged step
        // can overshoot the cap; its unmodified value and overflow are retained.
        if (direction > 0 && _binding.DisplayedSkill(skill) >= 100) return false;
        var delta = checked(_deltas.GetValueOrDefault(skill) + direction * step);
        var before = _binding.ReadUnmodifiedSkill(skill);
        var changed = before + direction * step;
        if (!float.IsFinite(before) || !float.IsFinite(changed)) throw new InvalidDataException("Level-up skill storage is non-finite.");
        _allocated[skill] = checked(_allocated.GetValueOrDefault(skill) + direction);
        _deltas[skill] = delta;
        _assigned = checked(_assigned + direction);
        WriteSkill(skill, before, changed, FalloutLevelUpOperationKind.SkillClick);
        return true;
    }

    internal void Reset()
    {
        RequireHealthy();
        if (Page == FalloutLevelUpPage.Skills)
        {
            // The source clears the page counter before subtracting each
            // row's consumed delta. A later row failure keeps that prefix.
            _assigned = 0;
            try
            {
                foreach (var skill in _binding.SkillOrder)
                {
                    _operation = new(FalloutLevelUpOperationKind.SkillResetRead, skill, null, null, null);
                    var before = _binding.ReadUnmodifiedSkill(skill);
                    WriteSkill(skill, before, before - _deltas.GetValueOrDefault(skill), FalloutLevelUpOperationKind.SkillResetWrite);
                    _allocated.Remove(skill);
                    _deltas.Remove(skill);
                }
            }
            catch (Exception error) when (Retainable(error)) { Error ??= FailureMessage(error); throw; }
        }
        else if (Page == FalloutLevelUpPage.Perks) _selected = null;
    }

    private void WriteSkill(int skill, float before, float requested, FalloutLevelUpOperationKind kind)
    {
        if (!float.IsFinite(before) || !float.IsFinite(requested)) throw new InvalidDataException("Level-up skill storage is non-finite.");
        _operation = new(kind, skill, null, before, requested);
        try
        {
            _binding.WriteUnmodifiedSkill(skill, requested);
            if (_binding.ReadUnmodifiedSkill(skill) != requested)
                throw new InvalidDataException("The shared skill owner did not publish the level-up operation.");
            _operation = null;
        }
        catch (Exception error) when (Retainable(error)) { Error ??= FailureMessage(error); throw; }
    }

    internal bool SelectPerk(FalloutFormKey form)
    {
        RequireHealthy();
        var choice = _binding.Perks.SingleOrDefault(choice => choice.Form == form) ??
            throw new InvalidDataException("Level-up perk is outside the source menu choices.");
        if (Page != FalloutLevelUpPage.Perks || !_binding.PerkEnabled(form) ||
            _binding.PerkRank(form) >= choice.MaximumRank) return false;
        _selected = _selected == choice ? null : choice;
        return true;
    }

    internal bool Back()
    {
        RequireHealthy();
        if (Page != FalloutLevelUpPage.Perks) return false;
        Page = FalloutLevelUpPage.Skills; return true;
    }

    internal bool Continue()
    {
        RequireHealthy();
        if (!CanContinue) return false;
        if (Page == FalloutLevelUpPage.Skills && HasPerkPage) { Page = FalloutLevelUpPage.Perks; return true; }
        if (_completionPrepared) throw new InvalidOperationException("Level-up completion scheduling has already been attempted.");
        _operation = new(FalloutLevelUpOperationKind.CompletionScheduling, null, null, 0, 1);
        try { _prepareCompletion(); _completionPrepared = true; _operation = null; }
        catch (Exception error) when (Retainable(error)) { Error ??= FailureMessage(error); throw; }
        if (Page == FalloutLevelUpPage.Perks)
        {
            if (_perkAttempted) throw new InvalidOperationException("Level-up perk acquisition has already been attempted.");
            try
            {
                _operation = new(FalloutLevelUpOperationKind.PerkRankRead, null, _selected!.Form, null, null);
                _rankBefore = _binding.PerkRank(_selected.Form);
                _perkAttempted = true;
                _operation = new(FalloutLevelUpOperationKind.PerkAcquisition, null, _selected.Form, _rankBefore.Value, _rankBefore.Value + 1);
                _binding.AcquirePerkRank(_selected.Form, checked(_rankBefore.Value + 1));
                if (_binding.PerkRank(_selected.Form) != _rankBefore.Value + 1)
                    throw new InvalidDataException("The shared perk owner did not publish the selected acquired rank.");
                _perkCommitted = true; _operation = null;
            }
            catch (Exception error) when (Retainable(error)) { Error ??= FailureMessage(error); throw; }
        }
        Page = FalloutLevelUpPage.Complete; return true;
    }

    private void RequireHealthy()
    {
        if (Error is not null) throw new InvalidOperationException("Level-up menu retains a failed consumed operation: " + Error);
        if (Completed) throw new InvalidOperationException("Level-up menu is already complete.");
    }
    // Any ordinary callback fault can follow a committed prefix. Keep that
    // prefix and refuse retry regardless of the callback's exception type.
    private static bool Retainable(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    private static string FailureMessage(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;

    internal FalloutLevelUpMenuSnapshot Capture() => new(_source, _generation, Level, _rules, Budget, _assigned, Page,
        new Dictionary<int, int>(_allocated), new Dictionary<int, int>(_deltas), _binding.SkillOrder.ToDictionary(skill => skill, _binding.ReadUnmodifiedSkill),
        _selected, _rankBefore, _selected is null ? null : _binding.PerkRank(_selected.Form),
        _completionPrepared, _perkAttempted, _perkCommitted, _operation, Error);

    internal static FalloutLevelUpMenuSession Restore(FalloutPlayerAdvancementSource source,
        FalloutLevelUpMenuSnapshot state, FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding,
        Action prepareCompletion) => new(source, state, rules, binding, prepareCompletion);
}
