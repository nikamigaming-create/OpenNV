namespace OpenNV.Runtime.Content;

// Head targets have independent stored references and enabled flags. The
// cached selection is used for new-target notification, not as the selection
// authority. In particular StopLook does not immediately refresh that cache.
internal sealed class FalloutHeadTrackingState
{
    private readonly FalloutFormKey?[] _targets = new FalloutFormKey?[6];
    private readonly bool[] _enabled = new bool[6];
    private readonly float _defaultHoldSeconds;

    internal FalloutHeadTrackingState(float defaultHoldSeconds)
    {
        if (!float.IsFinite(defaultHoldSeconds) || defaultHoldSeconds < 0)
            throw new InvalidDataException("Head-tracking default hold duration is invalid.");
        _defaultHoldSeconds = defaultHoldSeconds;
    }

    internal float DefaultHoldSeconds { get; private set; }
    internal FalloutFormKey? CachedTarget { get; private set; }
    internal long Revision { get; private set; }
    internal long TargetRevision { get; private set; }
    internal IEnumerable<(int Priority, FalloutFormKey? Target, bool Enabled)> Slots =>
        _targets.Select((target, index) => (index, target, _enabled[index]));
    internal FalloutFormKey? SelectedTarget => Enumerable.Range(0, _targets.Length).Reverse()
        .Where(index => _enabled[index]).Select(index => _targets[index]).FirstOrDefault(target => target is not null);
    internal bool CanSelectDefault => DefaultHoldSeconds <= 0 && !_enabled.Skip(1).Any(value => value);

    internal void SetTarget(int priority, FalloutFormKey? target)
    {
        if ((uint)priority >= _targets.Length) throw new ArgumentOutOfRangeException(nameof(priority));
        _targets[priority] = target;
        _enabled[priority] = target is not null;
        Revision++;
        if (target is not null) RefreshSelection();
    }

    internal void Look(FalloutFormKey target) => SetTarget(2, target);

    internal void StopLook()
    {
        var previous = _targets[2];
        _targets[2] = null;
        _enabled[2] = false;
        _targets[0] = previous;
        // Preserve slot zero's enabled flag; copying the reference alone does
        // not acquire the missing automatic/default head-target owner.
        DefaultHoldSeconds = _defaultHoldSeconds;
        Revision++;
    }

    internal void Advance(float seconds, Func<FalloutFormKey, bool> isLoaded)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (DefaultHoldSeconds > 0) DefaultHoldSeconds -= seconds;
        var invalidated = false;
        for (var index = 0; index < _targets.Length; index++)
        {
            if (!_enabled[index] || _targets[index] is not { } target || isLoaded(target)) continue;
            _enabled[index] = false;
            invalidated = true;
        }
        if (!invalidated) return;
        Revision++;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        var selected = SelectedTarget;
        if (selected == CachedTarget) return;
        CachedTarget = selected;
        TargetRevision++;
    }
}
