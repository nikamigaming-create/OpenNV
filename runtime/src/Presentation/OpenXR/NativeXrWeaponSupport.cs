using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

/// <summary>Proximity engagement at the equipped animation's original off-hand grip.</summary>
internal sealed class NativeXrWeaponSupport(Transform3D primaryFromSupport)
{
    private float _dwell;
    internal bool Engaged { get; private set; }
    internal float Blend { get; private set; }
    internal object State => new
    {
        engaged = Engaged,
        blend = Blend,
        dwellSeconds = _dwell,
        sourceOffset = new[] { primaryFromSupport.Origin.X, primaryFromSupport.Origin.Y, primaryFromSupport.Origin.Z }
    };

    internal Basis Advance(double delta, Transform3D primary, Transform3D support, bool allowed)
    {
        var seconds = (float)Math.Clamp(delta, 0, .05);
        if (!allowed) { Release(); return primary.Basis; }
        var socket = primary * primaryFromSupport;
        var offset = support.Origin - primary.Origin;
        var length = primaryFromSupport.Origin.Length();
        if (length < .05f || offset.Length() < .05f) { Release(); return primary.Basis; }
        if (Engaged)
        {
            if (MathF.Abs(offset.Length() - length) > .2f) { Engaged = false; _dwell = 0; }
        }
        else
        {
            _dwell = socket.Origin.DistanceTo(support.Origin) <= .1f ? _dwell + seconds : 0;
            Engaged = _dwell >= .15f;
        }
        Blend = Mathf.MoveToward(Blend, Engaged ? 1 : 0, seconds / .18f);
        if (Blend == 0) return primary.Basis;
        var direction = primary.Basis * primaryFromSupport.Origin;
        var rotation = new Basis(new Quaternion(direction.Normalized(), offset.Normalized()));
        return primary.Basis.Slerp(rotation * primary.Basis, Blend);
    }

    internal Transform3D SupportPose(Transform3D resolvedPrimary, Transform3D trackedSupport)
        => trackedSupport.InterpolateWith(resolvedPrimary * primaryFromSupport, Blend);
    internal Transform3D SocketPose(Transform3D primary) => primary * primaryFromSupport;

    internal void Release() { Engaged = false; _dwell = 0; Blend = 0; }
}
