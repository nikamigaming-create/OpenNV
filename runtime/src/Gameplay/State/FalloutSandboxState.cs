using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSandboxCandidate(FalloutFormKey? Reference, int Action, int Multiplier,
    FalloutFormKey? Base = null, string? ReferenceSha256 = null, string? BaseSha256 = null)
{
    internal void Validate()
    {
        if (Action is < 0 or > 5 || Multiplier < 0 ||
            Reference is { } form && (string.IsNullOrWhiteSpace(form.OwnerPlugin) || form.ObjectId == 0) ||
            Action is 0 or 1 or 4 or 5 && Reference is null || Action == 3 && Reference is not null)
            throw new InvalidDataException("Sandbox action candidate has an invalid source identity or weight.");
        if (Base is not null || ReferenceSha256 is not null || BaseSha256 is not null)
            RequireSourceIdentity();
    }
    internal void RequireSourceIdentity()
    {
        if (Reference is null || Base is not { } basis || string.IsNullOrWhiteSpace(basis.OwnerPlugin) || basis.ObjectId == 0 ||
            !FalloutAdvancementRuntimeReceipt.Digest(ReferenceSha256) || !FalloutAdvancementRuntimeReceipt.Digest(BaseSha256))
            throw new InvalidDataException("Sandbox reference candidate lost its winning reference/base byte identities.");
    }
}

internal sealed record FalloutSandboxSnapshot(FalloutFormKey Package, string PackageSha256,
    FalloutSandboxArea Area, bool AtLocation, byte[] ActionWeights,
    IReadOnlyList<FalloutSandboxCandidate> Candidates, int? SelectedIndex, float Remaining,
    bool ActionEntered, string? Failure, FalloutFollowElection? Election = null, FalloutSandboxNativeRoute? Route = null,
    FalloutSandboxRegistrySnapshot? Registry = null, bool RetirementEntered = false)
{
    internal void Validate()
    {
        Area.Validate(); Election?.Validate(); Route?.Validate(); Registry?.Validate();
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 ||
            PackageSha256 is not { Length: 64 } || !PackageSha256.All(Uri.IsHexDigit) ||
            ActionWeights is not { Length: 6 } || Candidates is null ||
            SelectedIndex is { } index && (index < 0 || index >= Candidates.Count) ||
            !float.IsFinite(Remaining) || Remaining < 0 ||
            SelectedIndex is null && (Remaining != 0 || ActionEntered || RetirementEntered) ||
            RetirementEntered && (!ActionEntered || Remaining != 0) ||
            !AtLocation && (SelectedIndex is not null || Candidates.Count != 0) ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Sandbox continuation has an invalid consumed selection or clock.");
        foreach (var candidate in Candidates) candidate.Validate();
    }
    internal FalloutSandboxSnapshot Copy() => this with
    { Area = Area.Copy(), ActionWeights = (byte[])ActionWeights.Clone(), Candidates = Candidates.ToArray(), Route = Route?.Copy(), Registry = Registry?.Copy() };
}

// Selection consumes the actual registered candidate order. World discovery,
// duration inputs and native action completion are independent living owners.
internal sealed class FalloutSandboxState
{
    private readonly FalloutSandboxPackage _source;
    private readonly Func<uint, uint> _random;
    private FalloutSandboxSnapshot _state;
    internal bool AtLocation => _state.AtLocation;
    internal FalloutSandboxArea Area => _state.Area;
    internal FalloutSandboxCandidate? Selected => _state.SelectedIndex is { } index ? _state.Candidates[index] : null;
    internal string? Failure => _state.Failure;
    internal FalloutSandboxActionRegistry Registry { get; }
    internal bool RetirementEntered => _state.RetirementEntered;
    internal int[] ActionWeights(FalloutSandboxActionContext context, FalloutSandboxAvailability availability)
        => context.Weights(_state.ActionWeights, availability);

    internal FalloutSandboxState(FalloutSandboxPackage source, string packageSha256, FalloutSandboxArea area,
        Func<uint, uint> random, FalloutSandboxSnapshot? saved = null)
    {
        _source = source; _random = random ?? throw new ArgumentNullException(nameof(random));
        area.Validate(); Registry = new(saved?.Registry);
        if (saved is not null)
        {
            saved.Validate();
            if (saved.Package != source.Form || saved.PackageSha256 != packageSha256 || saved.Area.Cell != area.Cell ||
                saved.Area.Radius != area.Radius || (saved.Area.Center is null) != (area.Center is null) ||
                saved.Area.Center is { } center && !center.SequenceEqual(area.Center!))
                throw new InvalidDataException("Cold Sandbox differs from its winning source area or declaration.");
            _state = saved.Copy();
        }
        else _state = new(source.Form, packageSha256, area.Copy(), false, [128, 128, 128, 128, 128, 128], [], null, 0, false, null);
    }

    internal void ObserveLocation(bool inside)
    {
        RequireHealthy();
        if (!inside && _state.SelectedIndex is not null)
            throw new NotSupportedException("Sandbox active action left its source area; action cancellation must retire before relocation.");
        _state = _state with { AtLocation = inside };
    }

    internal void ObserveSourceArea(FalloutSandboxArea area)
    {
        RequireHealthy(); area.Validate();
        if (_state.Area.Cell == area.Cell && _state.Area.Radius == area.Radius &&
            (_state.Area.Center is null) == (area.Center is null) &&
            (_state.Area.Center is null || _state.Area.Center.SequenceEqual(area.Center!))) return;
        if (Selected is not null) throw new NotSupportedException("Moved Sandbox source location requires actual active-action cancellation.");
        _state = _state with { Area = area.Copy(), AtLocation = false, Candidates = [] };
    }

    internal void Select(IReadOnlyList<FalloutSandboxCandidate> originalOrder, Func<FalloutSandboxCandidate, float> duration,
        int[]? contextWeights = null, FalloutFormKey? repeated = null)
    {
        RequireHealthy();
        if (!_state.AtLocation || Selected is not null) throw new InvalidOperationException("Sandbox action election has no stationary empty owner.");
        try
        {
            var candidates = originalOrder.ToArray();
            if (contextWeights is not null && contextWeights.Length != 6)
                throw new InvalidDataException("Sandbox context lost its six source action classes.");
            uint Weight(FalloutSandboxCandidate candidate)
            {
                if (candidate.Reference is not null && candidate.Reference == repeated) return 0;
                var value = contextWeights is null ? _state.ActionWeights[candidate.Action] : contextWeights[candidate.Action];
                var weight = (long)candidate.Multiplier * value;
                if (weight > int.MaxValue) throw new NotSupportedException("Sandbox source candidate weight exceeds the signed accumulator.");
                return (uint)Math.Max(0, weight);
            }
            ulong total = 0;
            foreach (var candidate in candidates)
            {
                candidate.Validate();
                if (candidate.Action == 0 && _source.NoFurniture || candidate.Action == 1 && _source.NoSleeping ||
                    candidate.Action == 2 && _source.NoEating || candidate.Action == 3 && _source.NoWandering ||
                    candidate.Action == 4 && _source.NoIdleMarkers || candidate.Action == 5 && _source.NoConversation)
                    throw new InvalidDataException("Sandbox discovery returned a source-vetoed action.");
                total += Weight(candidate);
            }
            if (total == 0) { _state = _state with { Candidates = candidates }; return; }
            if (total > int.MaxValue) throw new NotSupportedException("Sandbox candidate accumulation exceeds the owned signed source selection extent.");
            var draw = _random((uint)total);
            if (draw >= total) throw new InvalidDataException("Sandbox random owner returned an out-of-bound draw.");
            var selected = -1;
            for (var index = 0; index < candidates.Length; ++index)
            {
                var weight = Weight(candidates[index]);
                if (draw < weight) { selected = index; break; }
                draw -= weight;
            }
            if (selected < 0) throw new InvalidDataException("Sandbox weighted selection lost its consumed source draw.");
            // Retain the chosen action before its independently owned duration or
            // native producer enters. An exception cannot redraw the prefix.
            _state = _state with { Candidates = candidates, SelectedIndex = selected };
            var seconds = duration(candidates[selected]);
            if (!float.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Sandbox source duration is invalid.");
            var weights = _state.ActionWeights.Select(value => (byte)Math.Min(byte.MaxValue, value + 1)).ToArray();
            weights[candidates[selected].Action] = 1;
            _state = _state with { Remaining = seconds, ActionWeights = weights };
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    internal void EnterAction(Action<FalloutSandboxCandidate> actualProducer)
    {
        RequireHealthy();
        if (Selected is not { } selected || _state.ActionEntered)
            throw new InvalidOperationException("Sandbox action has no fresh consumed selection.");
        _state = _state with { ActionEntered = true };
        try { actualProducer(selected); }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    internal bool Advance(float seconds, bool actualActionReturned, Action<FalloutSandboxCandidate> retire)
    {
        RequireHealthy();
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Selected is not { } selected || !_state.ActionEntered) return false;
        _state = _state with { Remaining = MathF.Max(0, _state.Remaining - seconds) };
        if (!actualActionReturned || _state.Remaining != 0) return false;
        try { retire(selected); }
        catch (Exception error) { RetainFailure(error); throw; }
        _state = _state with { SelectedIndex = null, ActionEntered = false, Remaining = 0 };
        return true; // Only the action retires; the Sandbox PACK remains active.
    }

    internal bool AdvanceNative(float seconds, Action<FalloutSandboxCandidate> requestRetirement,
        Func<FalloutSandboxCandidate, bool> observeRetired, Action<FalloutSandboxCandidate> retainedReturn)
    {
        RequireHealthy();
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Selected is not { } selected || !_state.ActionEntered) return false;
        _state = _state with { Remaining = MathF.Max(0, _state.Remaining - seconds) };
        if (_state.Remaining != 0) return false;
        try
        {
            if (!_state.RetirementEntered)
            {
                _state = _state with { RetirementEntered = true };
                requestRetirement(selected);
            }
            if (!observeRetired(selected)) return false;
            retainedReturn(selected);
            _state = _state with { SelectedIndex = null, ActionEntered = false, Remaining = 0, RetirementEntered = false };
            return true;
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    internal bool CancelNative(Action<FalloutSandboxCandidate> requestRetirement,
        Func<FalloutSandboxCandidate, bool> observeRetired)
    {
        RequireHealthy();
        if (Selected is not { } selected) return true;
        if (!_state.ActionEntered) throw new NotSupportedException("Sandbox cancellation retains a selected action whose producer did not enter.");
        _state = _state with { Remaining = 0 };
        try
        {
            if (!_state.RetirementEntered)
            {
                _state = _state with { RetirementEntered = true };
                requestRetirement(selected);
            }
            if (!observeRetired(selected)) return false;
            _state = _state with { SelectedIndex = null, ActionEntered = false, Remaining = 0, RetirementEntered = false };
            return true;
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    internal void RetainFailure(Exception error) => _state = _state with
    { Failure = _state.Failure ?? (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message) };
    internal FalloutSandboxSnapshot Capture(FalloutFollowElection election)
    { election.Validate(); var result = _state.Copy() with { Election = election, Registry = Registry.Capture() }; result.Validate(); return result; }
    private void RequireHealthy()
    { if (_state.Failure is { } failure) throw new NotSupportedException("Sandbox retains an entered failed suffix: " + failure); }
}
