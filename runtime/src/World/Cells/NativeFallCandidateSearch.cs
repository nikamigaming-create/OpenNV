using Godot;

namespace OpenNV.Runtime.World.Cells;

// A scheduled candidate only. Source-floor membership, consequences, control
// fidelity and actual execution remain separate owners.
internal sealed record NativeFallCandidate(NativeGroundDeparture Departure, NativeAirContact Contact);

internal sealed class NativeFallCandidateSearch : IDisposable
{
    private readonly CharacterBody3D _body;
    private readonly Vector3 _horizontal;
    private readonly float _gravity, _delta, _supportHeight;
    private readonly int _maximumAirFrames;
    private readonly Func<Vector3, bool> _resident;
    private IEnumerator<NativeGroundDeparture?>? _ground;
    private IEnumerator<NativeAirContact?>? _air;
    private NativeGroundDeparture? _departure;
    private bool _disposed;

    internal NativeFallCandidateSearch(CharacterBody3D body, Vector3 horizontal, float gravity, float delta,
        int maximumGroundFrames, int maximumAirFrames, float supportHeight, Func<Vector3, bool> resident)
    {
        if (!float.IsFinite(gravity) || gravity <= 0 || maximumAirFrames <= 0 ||
            !float.IsFinite(supportHeight) || supportHeight <= 0)
            throw new InvalidDataException("Fall candidate requires positive gravity, support and frame bounds.");
        _body = body; _horizontal = horizontal; _gravity = gravity; _delta = delta;
        _maximumAirFrames = maximumAirFrames; _supportHeight = supportHeight; _resident = resident;
        _ground = NativeCapsuleNavigation.GroundDeparture(body, horizontal, delta, maximumGroundFrames, resident).GetEnumerator();
    }

    internal bool Advance(out NativeFallCandidate? candidate)
    {
        if (_disposed) throw new InvalidOperationException("Fall candidate query has retired.");
        candidate = null;
        try
        {
            if (_ground is { } ground)
            {
                if (!NativeCapsuleNavigation.AdvanceDeparture(ground, out var departure)) return false;
                _departure = departure ?? throw new InvalidOperationException("Ground departure returned no result.");
                ground.Dispose(); _ground = null;
                _air = NativeCapsuleNavigation.AirborneContact(_body, _departure.Position, _horizontal, _departure.Velocity.Y,
                    _gravity, _delta, _maximumAirFrames, _supportHeight, _resident).GetEnumerator();
            }
            if (!NativeCapsuleNavigation.AdvanceAirborne(_air!, out var contact)) return false;
            candidate = new(_departure!, contact ?? throw new InvalidOperationException("Airborne contact returned no result."));
            Dispose();
            return true;
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _ground?.Dispose(); }
        finally { _air?.Dispose(); _ground = null; _air = null; }
    }
}
