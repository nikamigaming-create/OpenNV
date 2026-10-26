using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal partial class RuntimeNifRigidBody
{
    private FalloutObjectPhysicsSnapshot? _pendingPhysics;
    private bool _physicsExited;
    private FalloutObjectPhysicsSnapshot? _exitPhysics;
    private Exception? _exitPhysicsFailure;
    internal int SourceBody => GetMeta("opennv_nif_collision_body", -1).AsInt32();
    internal string SourceSha256 => GetMeta("opennv_nif_source_sha256", "").AsString();

    public override void _EnterTree()
    {
        _physicsExited = false;
        _exitPhysics = null;
        _exitPhysicsFailure = null;
    }

    public override void _ExitTree()
    {
        // Sibling presentation owners may retire after this body leaves the
        // tree or is freed. Retain the final live state before native teardown;
        // an earlier campaign snapshot cannot represent this continuation.
        if (!Freeze)
            try { _exitPhysics = CapturePhysics(); }
            catch (Exception error) { _exitPhysicsFailure = error; }
        _physicsExited = true;
    }

    internal FalloutObjectPhysicsSnapshot CapturePhysics()
    {
        if (_physicsExited)
            return _exitPhysics ?? throw new InvalidOperationException(
                "Object physics retirement could not retain its final live state.", _exitPhysicsFailure);
        if (!IsInsideTree() || _visual is null || Freeze)
            throw new InvalidOperationException("Object physics capture requires its live dynamic body.");
        if (_grab is not null)
            throw new NotSupportedException("Release the held object before capturing its physical continuation.");
        var pose = GlobalTransform;
        var state = new FalloutObjectPhysicsSnapshot(SourceBody, SourceSha256,
            [pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z, pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z,
                pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z, pose.Origin.X, pose.Origin.Y, pose.Origin.Z],
            [LinearVelocity.X, LinearVelocity.Y, LinearVelocity.Z],
            [AngularVelocity.X, AngularVelocity.Y, AngularVelocity.Z], Sleeping, _windRandom?.State);
        state.Validate();
        return state;
    }

    internal void ValidatePhysics(FalloutObjectPhysicsSnapshot state)
    {
        state.Validate();
        if (state.Body != SourceBody || !state.Sha256.Equals(SourceSha256, StringComparison.OrdinalIgnoreCase) || Freeze)
            throw new InvalidDataException("Saved object physics differs from its winning dynamic body.");
    }

    internal void RestorePhysics(FalloutObjectPhysicsSnapshot state)
    {
        ValidatePhysics(state);
        if (_visual is null) { _pendingPhysics = state.Copy(); return; }
        var values = state.Transform;
        var pose = new Transform3D(new Basis(new(values[0], values[1], values[2]),
            new(values[3], values[4], values[5]), new(values[6], values[7], values[8])), new(values[9], values[10], values[11]));
        GlobalTransform = pose;
        LinearVelocity = new(state.LinearVelocity[0], state.LinearVelocity[1], state.LinearVelocity[2]);
        AngularVelocity = new(state.AngularVelocity[0], state.AngularVelocity[1], state.AngularVelocity[2]);
        _windRandom = state.WindRandomState is { } random ? new FalloutSoundRandomState(random) : null;
        _visual.GlobalTransform = pose * _bodyToVisual;
        Sleeping = state.Sleeping;
        ResetPhysicsInterpolation();
        _visual.ResetPhysicsInterpolation();
    }
}
