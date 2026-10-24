using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutLevelUpPage { Skills, Perks, Complete }
internal sealed record FalloutLevelUpPerkChoice(FalloutFormKey Form, string SourceSha256, int MaximumRank);
internal sealed record FalloutLevelUpMenuBinding(IReadOnlyList<int> SkillOrder,
    Func<int, float> ReadUnmodifiedSkill, Action<int, float> WriteUnmodifiedSkill,
    Func<int, int> DisplayedSkill, Func<int, bool> Tagged,
    Func<int, int> AdmittedSkillBudget, IReadOnlyList<FalloutLevelUpPerkChoice> Perks,
    Func<FalloutFormKey, bool> PerkEnabled, Func<FalloutFormKey, int> PerkRank,
    Action<FalloutFormKey, int> AcquirePerkRank, Func<FalloutFormKey, string?>? UnsupportedPerk = null);

internal enum FalloutLevelUpOperationKind
{
    SkillClick, SkillResetRead, SkillResetWrite, CompletionScheduling, PerkRankRead, PerkAcquisition
}
internal sealed record FalloutLevelUpOperation(FalloutLevelUpOperationKind Kind, int? Skill, FalloutFormKey? Perk,
    float? Before, float? Requested);
internal sealed record FalloutLevelUpMenuSnapshot(FalloutPlayerAdvancementSource Source, ulong Generation, int Level,
    FalloutLevelUpRules Rules, int Budget, int AssignedSkillPoints, FalloutLevelUpPage Page,
    IReadOnlyDictionary<int, int> AllocatedPoints, IReadOnlyDictionary<int, int> AllocatedDeltas,
    IReadOnlyDictionary<int, float> UnmodifiedSkills, FalloutLevelUpPerkChoice? SelectedPerk,
    int? PerkRankBefore, int? PerkObservedRank, bool CompletionPrepared, bool PerkAttempted, bool PerkCommitted,
    FalloutLevelUpOperation? Operation, string? Error);

internal sealed class FalloutLevelUpMenuSession
{
    private readonly FalloutPlayerAdvancementSource _source;
    private readonly FalloutLevelUpRules _rules;
    private readonly FalloutLevelUpMenuBinding _binding;
    private readonly Dictionary<int, int> _allocated = [];
    private readonly Dictionary<int, int> _deltas = [];
    private readonly Action _prepareCompletion;
    private readonly ulong _generation;
    private bool _busy;
    private int _assigned;
    private FalloutLevelUpPerkChoice? _selected;
    private int? _rankBefore;
    private bool _completionPrepared, _perkAttempted, _perkCommitted;
    private FalloutLevelUpOperation? _operation;
    internal string? Error { get; private set; }
    internal int Level { get; }
    internal int Budget { get; }
    internal int Assigned => _assigned;
    internal ulong Generation => _generation;
    internal FalloutLevelUpPage Page { get; private set; }
    internal bool Completed => Page == FalloutLevelUpPage.Complete;
    internal bool HasPerkPage => Level % _rules.LevelsPerPerk == 0;
    internal IReadOnlyDictionary<int, int> Allocated => new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(_allocated);
    internal IReadOnlyDictionary<int, int> AllocatedDeltas => new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(_deltas);
    internal IReadOnlyList<int> Skills => _binding.SkillOrder;
    internal IReadOnlyList<FalloutLevelUpPerkChoice> Perks => _binding.Perks;
    internal FalloutLevelUpPerkChoice? SelectedPerk => _selected;
    internal bool CanContinue => !_busy && Error is null && (Page == FalloutLevelUpPage.Skills ? Assigned == Budget :
        Page == FalloutLevelUpPage.Perks && _selected is not null && PerkEnabled(_selected.Form));
    internal int DisplayedSkill(int skill) => _binding.DisplayedSkill(skill);
    internal float UnmodifiedSkill(int skill) => _binding.ReadUnmodifiedSkill(skill);
    internal string? UnsupportedPerk(FalloutFormKey perk) => _binding.UnsupportedPerk?.Invoke(perk);
    internal bool PerkEnabled(FalloutFormKey perk) => _binding.PerkEnabled(perk) && UnsupportedPerk(perk) is null;

    internal FalloutLevelUpMenuSession(FalloutPlayerAdvancementSource source, ulong generation, int level,
        int requestedBudget, FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding, Action prepareCompletion)
    {
        _source = source; _generation = generation; Level = level; _rules = rules; _binding = binding with { SkillOrder = binding.SkillOrder.ToArray(), Perks = binding.Perks.ToArray() };
        _prepareCompletion = prepareCompletion;
        ValidateBinding();
        Budget = _binding.AdmittedSkillBudget(requestedBudget);
        if (requestedBudget < 0 || Budget < 0 || Budget > requestedBudget)
            throw new InvalidDataException("Level-up admitted skill budget exceeds its requested storage.");
        if (HasPerkPage && _binding.Perks.Count == 0)
            throw new NotSupportedException("A source perk choice is due, but no perk choices were bound.");
    }

    private FalloutLevelUpMenuSession(FalloutPlayerAdvancementSource source, FalloutLevelUpMenuSnapshot state,
        FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding, Action prepareCompletion)
    {
        _source = source; _generation = state.Generation; Level = state.Level; Budget = state.Budget;
        _rules = rules; _binding = binding with { SkillOrder = binding.SkillOrder.ToArray(), Perks = binding.Perks.ToArray() }; _prepareCompletion = prepareCompletion;
        ValidateBinding();
        if (state.Source != source || state.Generation == 0 || state.Level <= 1 || state.Budget < 0 ||
            state.Rules != rules || !Enum.IsDefined(state.Page) || state.AllocatedPoints is null ||
            state.AllocatedDeltas is null || state.UnmodifiedSkills is null ||
            state.AssignedSkillPoints < 0 || state.Error is null && state.AssignedSkillPoints > state.Budget ||
            state.AllocatedPoints.Any(pair => !_binding.SkillOrder.Contains(pair.Key)) ||
            state.AllocatedDeltas.Any(pair => !_binding.SkillOrder.Contains(pair.Key)) ||
            state.AllocatedPoints.Values.Any(value => value < 0) || state.AllocatedDeltas.Values.Any(value => value < 0) ||
            !state.AllocatedPoints.Keys.Order().SequenceEqual(state.AllocatedDeltas.Keys.Order()) ||
            state.Error is null && state.AssignedSkillPoints != state.AllocatedPoints.Values.Sum(value => (long)value) ||
            state.UnmodifiedSkills.Count != _binding.SkillOrder.Count || _binding.SkillOrder.Any(value =>
                !state.UnmodifiedSkills.TryGetValue(value, out var saved) || !float.IsFinite(saved) ||
                saved != _binding.ReadUnmodifiedSkill(value)) ||
            state.Operation is not null && (state.Error is null || !Enum.IsDefined(state.Operation.Kind) ||
                state.Operation.Before is { } before && !float.IsFinite(before) ||
                state.Operation.Requested is { } requested && !float.IsFinite(requested)) ||
            state.PerkAttempted && state.SelectedPerk is null ||
            state.PerkCommitted && (!state.PerkAttempted || state.PerkRankBefore is null) ||
            state.Page == FalloutLevelUpPage.Complete && (state.Error is not null || !state.CompletionPrepared) ||
            state.Error is { Length: 0 })
            throw new InvalidDataException("Saved menu differs from its source or consumed skill state.");
        foreach (var pair in state.AllocatedPoints) _allocated.Add(pair.Key, pair.Value);
        foreach (var pair in state.AllocatedDeltas) _deltas.Add(pair.Key, pair.Value);
        _assigned = state.AssignedSkillPoints;
        Page = state.Page; _selected = state.SelectedPerk; _rankBefore = state.PerkRankBefore;
        _completionPrepared = state.CompletionPrepared;
        _perkAttempted = state.PerkAttempted; _perkCommitted = state.PerkCommitted;
        _operation = state.Operation; Error = state.Error;
        if (_selected is not null && !_binding.Perks.Contains(_selected))
            throw new InvalidDataException("Saved perk differs from its winning source choice.");
        if (Page == FalloutLevelUpPage.Perks && !HasPerkPage)
            throw new InvalidDataException("Saved perk page differs from its source cadence/choices.");
        if (Page == FalloutLevelUpPage.Complete && (Assigned != Budget || HasPerkPage && (!_perkCommitted || _selected is null)))
            throw new InvalidDataException("Saved completion has an unconsumed allocation/perk.");
        if (_perkCommitted && _binding.PerkRank(_selected!.Form) != _rankBefore + 1)
            throw new InvalidDataException("Saved commit differs from the shared acquired perk rank.");
        if (state.PerkObservedRank is { } rank && (_selected is null || _binding.PerkRank(_selected.Form) != rank))
            throw new InvalidDataException("Saved receipt differs from its observed acquired rank.");
    }

    private void ValidateBinding()
    {
        _source.Validate(); _rules.Validate();
        if (_source.SkillRate != _rules.SkillRate || _generation == 0 || Level <= 1 ||
            _binding.SkillOrder is null || _binding.SkillOrder.Count == 0 ||
            _binding.SkillOrder.Distinct().Count() != _binding.SkillOrder.Count || _binding.Perks is null ||
            _binding.Perks.Select(choice => choice.Form).Distinct().Count() != _binding.Perks.Count ||
            _binding.Perks.Any(choice => choice.Form.ObjectId == 0 || choice.MaximumRank is < 1 or > byte.MaxValue ||
                !FalloutPlayerAdvancementSource.Hash(choice.SourceSha256)))
            throw new InvalidDataException("Level-up source skill/perk binding is invalid.");
        if (_binding.UnsupportedPerk is null)
            throw new NotSupportedException("Level-up requires its perk-effect admission callback.");
    }

    internal bool CanChangeSkill(int skill, int direction)
    {
        if (!_binding.SkillOrder.Contains(skill) || direction is not (-1 or 1))
            throw new InvalidDataException("Skill operation is outside its source rows.");
        return Error is null && Page == FalloutLevelUpPage.Skills && (direction > 0 ?
            Assigned < Budget && _binding.DisplayedSkill(skill) < 100 :
            _deltas.GetValueOrDefault(skill) > 0 && _binding.DisplayedSkill(skill) > 0);
    }

    internal bool ChangeSkill(int skill, int direction) => Mutate(() => ChangeSkillCore(skill, direction));

    private bool ChangeSkillCore(int skill, int direction)
    {
        RequireHealthy();
        if (!CanChangeSkill(skill, direction)) return false;
        var step = _binding.Tagged(skill) ? _rules.TaggedSkillMultiplier : 1;
        // The arrow tests the displayed value before applying the whole tagged
        // step. Do not discard unmodified overflow above the displayed cap.
        var delta = checked(_deltas.GetValueOrDefault(skill) + direction * step);
        var before = _binding.ReadUnmodifiedSkill(skill);
        var changed = before + direction * step;
        if (!float.IsFinite(before) || !float.IsFinite(changed))
            throw new InvalidDataException("Level-up skill storage is non-finite.");
        _allocated[skill] = checked(_allocated.GetValueOrDefault(skill) + direction);
        _deltas[skill] = delta;
        _assigned = checked(_assigned + direction);
        WriteSkill(skill, before, changed, FalloutLevelUpOperationKind.SkillClick);
        return true;
    }

    internal void Reset() => Mutate(() => { ResetCore(); return true; });

    private void ResetCore()
    {
        RequireHealthy();
        if (Page == FalloutLevelUpPage.Skills)
        {
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
            catch (Exception error) { Error ??= error.Message; throw; }
        }
        else if (Page == FalloutLevelUpPage.Perks) _selected = null;
    }

    private void WriteSkill(int skill, float before, float requested, FalloutLevelUpOperationKind kind)
    {
        _operation = new(kind, skill, null, before, requested);
        try
        {
            if (!float.IsFinite(before) || !float.IsFinite(requested))
                throw new InvalidDataException("Level-up skill storage is non-finite.");
            _binding.WriteUnmodifiedSkill(skill, requested);
            if (_binding.ReadUnmodifiedSkill(skill) != requested)
                throw new InvalidDataException("The shared skill owner did not publish the operation.");
            _operation = null;
        }
        catch (Exception error) { Error ??= error.Message; throw; }
    }

    internal bool SelectPerk(FalloutFormKey form) => Mutate(() => SelectPerkCore(form));

    private bool SelectPerkCore(FalloutFormKey form)
    {
        RequireHealthy();
        var choice = _binding.Perks.SingleOrDefault(choice => choice.Form == form) ??
            throw new InvalidDataException("Perk is outside the source menu choices.");
        if (Page != FalloutLevelUpPage.Perks || !_binding.PerkEnabled(form) ||
            _binding.PerkRank(form) >= choice.MaximumRank) return false;
        if (UnsupportedPerk(form) is { } unsupported) throw new NotSupportedException(unsupported);
        _selected = _selected == choice ? null : choice;
        return true;
    }

    internal bool Back() => Mutate(() => BackCore());

    private bool BackCore()
    {
        RequireHealthy();
        if (Page != FalloutLevelUpPage.Perks) return false;
        Page = FalloutLevelUpPage.Skills;
        return true;
    }

    internal void Cancel() => throw new NotSupportedException(
        "The original level-up menu has no admitted abandon action. Reset/Back retain the earned allocation obligation.");

    internal bool Continue() => Mutate(() => ContinueCore());

    private bool ContinueCore()
    {
        RequireHealthy();
        if (!(Page == FalloutLevelUpPage.Skills ? Assigned == Budget :
            Page == FalloutLevelUpPage.Perks && _selected is not null && PerkEnabled(_selected.Form))) return false;
        if (Page == FalloutLevelUpPage.Skills && HasPerkPage)
        {
            Page = FalloutLevelUpPage.Perks;
            return true;
        }
        if (_completionPrepared) throw new InvalidOperationException("Completion scheduling has already been attempted.");
        _operation = new(FalloutLevelUpOperationKind.CompletionScheduling, null, null, 0, 1);
        try { _prepareCompletion(); _completionPrepared = true; _operation = null; }
        catch (Exception error) { Error ??= error.Message; throw; }
        if (Page == FalloutLevelUpPage.Perks)
        {
            if (_perkAttempted) throw new InvalidOperationException("Perk acquisition has already been attempted.");
            try
            {
                _operation = new(FalloutLevelUpOperationKind.PerkRankRead, null, _selected!.Form, null, null);
                var rankBefore = _binding.PerkRank(_selected.Form);
                _rankBefore = rankBefore;
                if (rankBefore < 0 || rankBefore >= _selected.MaximumRank)
                    throw new InvalidDataException("The selected perk has no remaining source rank.");
                if (!_binding.PerkEnabled(_selected.Form))
                    throw new NotSupportedException("The selected source perk no longer meets its live requirements.");
                if (UnsupportedPerk(_selected.Form) is { } unsupported) throw new NotSupportedException(unsupported);
                _perkAttempted = true;
                _operation = new(FalloutLevelUpOperationKind.PerkAcquisition, null, _selected.Form, rankBefore, rankBefore + 1);
                _binding.AcquirePerkRank(_selected.Form, checked(rankBefore + 1));
                if (_binding.PerkRank(_selected.Form) != rankBefore + 1)
                    throw new InvalidDataException("The shared perk owner did not publish the acquired rank.");
                _perkCommitted = true; _operation = null;
            }
            catch (Exception error) { Error ??= error.Message; throw; }
        }
        Page = FalloutLevelUpPage.Complete;
        return true;
    }

    private T Mutate<T>(Func<T> action)
    {
        RequireHealthy();
        if (_busy) throw new InvalidOperationException("Level-up already owns a pending operation.");
        _busy = true;
        try { return action(); }
        catch (Exception error) { Error ??= error.Message; throw; }
        finally { _busy = false; }
    }

    private void RequireHealthy()
    {
        if (Error is not null) throw new InvalidOperationException("Level-up retains a consumed failure: " + Error);
        if (Completed) throw new InvalidOperationException("Level-up is already complete.");
    }

    internal FalloutLevelUpMenuSnapshot Capture() => new(_source, _generation, Level, _rules, Budget, _assigned, Page,
        new Dictionary<int, int>(_allocated), new Dictionary<int, int>(_deltas),
        _binding.SkillOrder.ToDictionary(skill => skill, _binding.ReadUnmodifiedSkill), _selected, _rankBefore,
        _selected is null ? null : _binding.PerkRank(_selected.Form), _completionPrepared, _perkAttempted, _perkCommitted,
        _operation, Error);

    internal static FalloutLevelUpMenuSession Restore(FalloutPlayerAdvancementSource source,
        FalloutLevelUpMenuSnapshot state, FalloutLevelUpRules rules, FalloutLevelUpMenuBinding binding,
        Action prepareCompletion) => new(source, state, rules, binding, prepareCompletion);
}
