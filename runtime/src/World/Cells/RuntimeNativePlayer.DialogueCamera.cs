using Godot;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private Func<Vector3>? _dialogueViewTarget;
    private Vector3? _dialogueViewPoint;
    internal object DialogueViewState => new
    {
        active = _dialogueViewTarget is not null,
        target = _dialogueViewPoint is { } point ? new[] { point.X, point.Y, point.Z } : null,
        projection = _xr is null ? "flat-speaker-focus;zoom-unbound" : "OpenXR-tracked",
    };

    internal Action AcquireDialogueView(Func<Vector3> target)
    {
        // A dialogue target never rotates or translates a tracked headset.
        if (_xr is not null) return () => { };
        if (_dialogueViewTarget is not null) throw new InvalidOperationException("Dialogue view already has an owner.");
        _dialogueViewTarget = target;
        try { PublishDialogueView(); }
        catch { _dialogueViewTarget = null; _dialogueViewPoint = null; throw; }
        var released = false;
        return () =>
        {
            if (released) return;
            released = true;
            _dialogueViewTarget = null; _dialogueViewPoint = null;
            if (!IsInstanceValid(this) || !IsInsideTree() || !IsInstanceValid(_camera)) return;
            if (_sourceCamera is { } source) ApplySourceCamera(source);
            else PublishThirdPersonCamera();
        };
    }

    internal void PublishDialogueView()
    {
        if (_dialogueViewTarget is null || _xr is not null) return;
        var target = _dialogueViewTarget();
        var direction = target - _camera.GlobalPosition;
        if (!target.IsFinite() || !direction.IsFinite() || direction.LengthSquared() <= .000001f ||
            direction.Normalized().Cross(Vector3.Up).LengthSquared() <= .000001f)
            throw new NotSupportedException("Dialogue focus has no finite nondegenerate source target.");
        // The ordinary player/furniture owner retains the camera origin and
        // projection. Only dialogue aim replaces the previous activation angle.
        _camera.LookAt(target, Vector3.Up);
        _dialogueViewPoint = target;
    }
}
