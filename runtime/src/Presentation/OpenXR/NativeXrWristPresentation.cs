using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Presentation.OpenXR;

// One optional presentation transform around the anatomical forearm axis.
// Its source geometry, controls, bone attachment and viewport remain the owners.
internal sealed class NativeXrWristPresentation : IDisposable
{
    internal const float FocusScale = 2.25f;
    private readonly Node3D _model;
    private readonly BoneAttachment3D _attachment;
    private readonly Node3D _pivot;
    private readonly Transform3D _screenInPivot;
    internal Vector3 CuffCenter { get; }
    internal Vector3 CuffAxis { get; }
    internal Transform3D PresentationTransform => _pivot.Transform;
    private float _focus;
    private float _roll;
    private float _rollStep;
    private bool _focused;
    internal object State => new
    {
        focus = _focus,
        scale = 1 + (FocusScale - 1) * _focus,
        owner = "whole-equipped-device",
        pivot = "anatomical-forearm-centerline",
        facing = "cuff-roll-during-grip",
        rollRadians = _roll
    };

    internal NativeXrWristPresentation(NativeOwnedDeviceSurface source)
    {
        var attachments = source.Root.GetChildren().OfType<BoneAttachment3D>().ToArray();
        if (attachments.Length != 1 || attachments[0].GetChildCount() != 1)
            throw new NotSupportedException("Wrist enlargement requires the equipped device's unique source bone attachment.");
        _attachment = attachments[0];
        _model = _attachment.GetChild<Node3D>(0);
        _screenInPivot = _attachment.GlobalTransform.AffineInverse() * source.Geometry(source.ScreenName).GlobalTransform *
            ScreenFrame(source.ScreenVertices, source.ScreenUvs);
        var skeleton = _attachment.GetSkeleton() ?? throw new InvalidOperationException("Wrist attachment has no source skeleton.");
        var forearm = skeleton.FindBone("Bip01 L Forearm");
        var hand = skeleton.FindBone("Bip01 L Hand");
        if (forearm < 0 || hand < 0 || _attachment.BoneIdx < 0)
            throw new NotSupportedException("Wrist cuff has no anatomical forearm/hand binding.");
        var toAttachment = skeleton.GetBoneGlobalRest(_attachment.BoneIdx).AffineInverse();
        var elbow = toAttachment * skeleton.GetBoneGlobalRest(forearm).Origin;
        var wrist = toAttachment * skeleton.GetBoneGlobalRest(hand).Origin;
        if (elbow.DistanceSquaredTo(wrist) < .0001f)
            throw new InvalidDataException("Wrist cuff forearm axis is degenerate.");
        CuffAxis = (wrist - elbow).Normalized();
        CuffCenter = elbow + CuffAxis * (_screenInPivot.Origin - elbow).Dot(CuffAxis);
        _pivot = new Node3D { Name = "WristPresentation" };
        _attachment.AddChild(_pivot);
        _model.Reparent(_pivot, keepGlobalTransform: false);
    }

    internal void Advance(double delta, bool focused)
    {
        _focused = focused;
        var step = (float)Math.Max(0, delta) / .16f;
        _focus = Mathf.MoveToward(_focus, focused ? 1 : 0, step);
        _rollStep = Mathf.Pi * step;
    }

    internal void Publish(Transform3D head)
    {
        if (_focus == 0) { _pivot.Transform = Transform3D.Identity; _roll = 0; return; }
        // Only pronation/supination is permitted. Neither the screen normal nor
        // the reader can pitch, yaw or translate the cuff away from its socket.
        var toward = (_attachment.GlobalTransform.AffineInverse() * head.Origin - CuffCenter).Slide(CuffAxis);
        var normal = _screenInPivot.Basis.Z.Slide(CuffAxis);
        var target = _focused ? _roll : 0;
        if (_focused && toward.LengthSquared() > .0001f && normal.LengthSquared() > .0001f)
            target = Mathf.Atan2(CuffAxis.Dot(normal.Cross(toward)), normal.Dot(toward));
        // Keep the shortest angular path across +/-pi, including release after
        // a full wrist turn. Multiplying an unwrapped angle by focus would spin.
        _roll = Mathf.Wrap(_roll + Mathf.Clamp(Mathf.AngleDifference(_roll, target), -_rollStep, _rollStep), -Mathf.Pi, Mathf.Pi);
        var basis = new Basis(CuffAxis, _roll)
            .Scaled(Vector3.One * (1 + (FocusScale - 1) * _focus));
        _pivot.Transform = new(basis, CuffCenter - basis * CuffCenter);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_model) && GodotObject.IsInstanceValid(_attachment))
            _model.Reparent(_attachment, keepGlobalTransform: false);
        if (GodotObject.IsInstanceValid(_pivot)) _pivot.Free();
    }

    internal static Transform3D ScreenFrame(IReadOnlyList<Vector3> vertices, IReadOnlyList<Vector2> uvs)
    {
        if (vertices.Count < 3 || vertices.Count != uvs.Count)
            throw new InvalidDataException("Wrist screen has no matching positions and UVs.");
        var center = Vector3.Zero; var uvCenter = Vector2.Zero;
        for (var i = 0; i < vertices.Count; i++) { center += vertices[i]; uvCenter += uvs[i]; }
        center /= vertices.Count; uvCenter /= vertices.Count;
        var uPosition = Vector3.Zero; var vPosition = Vector3.Zero;
        float uu = 0, vv = 0, uv = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var p = vertices[i] - center; var t = uvs[i] - uvCenter;
            uPosition += p * t.X; vPosition += p * t.Y;
            uu += t.X * t.X; vv += t.Y * t.Y; uv += t.X * t.Y;
        }
        var determinant = uu * vv - uv * uv;
        if (!float.IsFinite(determinant) || determinant < 1e-8f)
            throw new InvalidDataException("Wrist screen UV axes are degenerate.");
        var x = ((uPosition * vv - vPosition * uv) / determinant).Normalized();
        var down = (vPosition * uu - uPosition * uv) / determinant;
        var y = -(down - x * x.Dot(down)).Normalized();
        if (x.LengthSquared() < .99f || y.LengthSquared() < .99f)
            throw new InvalidDataException("Wrist screen has no independent readable axes.");
        return new(new Basis(x, y, x.Cross(y).Normalized()), center);
    }
}
