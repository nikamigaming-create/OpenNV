namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPipBoyLightSnapshot(bool Enabled, string SourceHash);

// Held input is transient; the actual light state is shared and persistent.
internal sealed class FalloutPipBoyLightState
{
    internal bool Enabled { get; private set; }
    internal bool Pressed { get; private set; }
    private bool _handled;
    private double _heldSeconds;
    internal void Press()
    {
        if (Pressed) return;
        Pressed = true; _handled = false; _heldSeconds = 0;
    }
    internal void Advance(double seconds, float threshold)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !float.IsFinite(threshold) || threshold <= 0)
            throw new InvalidDataException("Pip-Boy hold timing is invalid.");
        if (!Pressed || _handled) return;
        _heldSeconds += seconds;
        if (_heldSeconds < threshold) return;
        Enabled = !Enabled; _handled = true;
    }
    internal bool Release()
    {
        var tap = Pressed && !_handled;
        Cancel(); return tap;
    }
    internal void Cancel() { Pressed = false; _handled = false; _heldSeconds = 0; }
    internal FalloutPipBoyLightSnapshot Capture(string hash) => new(Enabled, hash);
    internal void Restore(FalloutPipBoyLightSnapshot state, string hash)
    {
        if (state.SourceHash != hash) throw new InvalidDataException("Saved Pip-Boy light source changed.");
        Enabled = state.Enabled; Cancel();
    }
}
