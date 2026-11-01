namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifControllerPlayer
{
    private bool _objectDefaultStartup;
    private string? _objectDefaultSequence, _objectStartupUnbound;

    internal string? ObjectStartupUnbound => _scriptSelected ? null : _objectStartupUnbound;
    internal object ObjectStartupState => new
    {
        owner = SourceController < 0 ? "direct-controller" :
            _objectDefaultStartup ? "canonical-object-group" : "caller",
        selectedDefault = _objectDefaultSequence,
        explicitRequest = _scriptSelected,
        unbound = ObjectStartupUnbound,
        nativeInitialPhase = "unverified",
    };

    internal void ConfigureObjectDefaultStartup()
    {
        // Direct APP_TIME/morph/material clocks retain their own admission.
        if (SourceController < 0 || _objectDefaultStartup) return;
        // Nested ADDN/effect models can already have an independent playback
        // owner. Main placed managers are deferred by the placement factory;
        // this default selector must not steal an existing caller's clock.
        if (_active is not null && !_scriptSelected)
            return;
        _objectDefaultStartup = true;
        _autoStartManagedLoops = false;
        if (_scriptSelected) return;
        // Native named defaults are case-sensitive. A unique arbitrary loop
        // does not authorize ambient startup (for example lowercase idle).
        var defaults = _sequences.Values.Where(sequence => sequence.DirectClock is null &&
            sequence.Name is "Idle" or "SpecialIdle").ToArray();
        if (defaults.Length > 1)
        {
            _objectStartupUnbound = "Idle/SpecialIdle startup priority is unqualified.";
            return;
        }
        if (defaults.Length == 0) return;
        if (defaults[0].CycleType != 0)
        {
            _objectStartupUnbound = "Nonlooping object default startup is unqualified.";
            return;
        }
        _objectDefaultSequence = defaults[0].Name;
        PlaySourceSequence(_objectDefaultSequence);
    }

    private bool CanRestoreImplicitLoop(RuntimeNifControllerSequence sequence) =>
        sequence.DirectClock is not null || (_objectDefaultStartup
            ? sequence.Name.Equals(_objectDefaultSequence, StringComparison.Ordinal)
            : _autoStartManagedLoops);
}
