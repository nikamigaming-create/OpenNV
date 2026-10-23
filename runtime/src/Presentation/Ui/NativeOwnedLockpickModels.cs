using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Ui;

// Owned geometry, source projection operands and pose sampling. Camera basis,
// effective clips, lighting and blends remain separate acceptance lanes.
internal sealed partial class NativeOwnedLockpickModels : Control
{
    internal const string LockPath = "meshes/terminals/LockInterface01.NIF";
    internal const string PinPath = "meshes/terminals/BobbyPin01.NIF";
    internal NativeOwnedNifMenuSurface Surface { get; }
    private readonly NativeOwnedNifMenuModelHandle _lock, _pin;
    private readonly float _lockStop;

    internal NativeOwnedLockpickModels()
    {
        try
        {
            ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned lockpick content is absent.");
            FalloutNifFile Read(string path)
            {
                if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
                SetMeta(path == LockPath ? "opennv_lock_source" : "opennv_pin_source", identity);
                return FalloutNifFile.Read(bytes);
            }
            var sourceLock = Read(LockPath); var sourcePin = Read(PinPath);
            if (new[] { sourceLock, sourcePin }.Select(FalloutNifBounds.ReadStatic).Any(bound => !float.IsFinite(bound.Radius) || bound.Radius <= 0))
                throw new InvalidDataException("Owned lockpick model bounds are invalid.");
            var center = GamebryoCoordinate.ConvertVector(new(125, 0, 0));
            var slope = ProjectionSlope(FalloutInstallationSettings.Read(content).Number("Display", "fDefaultFOV"));
            // Projection uses the qualified ordinary-display operands. This
            // first-party look direction and clip policy are not retail parity.
            var camera = Transform3D.Identity.LookingAt(center, Vector3.Up);
            Surface = new([
                new(LockPath, sourceLock, Transform3D.Identity),
                new(PinPath, sourcePin, new(Basis.Identity, GamebryoCoordinate.ConvertVector(new(0, 0, -1.6f))))],
                new(camera, _ => slope, .1f, 2000, 3, Vector3.One, ModelRelativeFallbackLight: true));
            AddChild(Surface);
            var c = MathF.Cos(MathF.PI / 2); var s = MathF.Sin(MathF.PI / 2);
            var a = new float[] { 1, 0, 0, 0, c, s, 0, -s, c };
            var b = new float[] { c, s, 0, -s, c, 0, 0, 0, 1 };
            var product = new float[9];
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    for (var axis = 0; axis < 3; axis++)
                        product[row * 3 + column] += a[row * 3 + axis] * b[axis * 3 + column];
            Surface.ModelsRoot.Transform = new(GamebryoCoordinate.ConvertBasis(product, 1, "lockpick model group"), center);
            _lock = Surface.Model(LockPath); _pin = Surface.Model(PinPath);
            foreach (var model in new[] { _lock, _pin })
            {
                var animation = model.Animation;
                _ = animation.SequenceRange("Forward");
                var backward = animation.SequenceRange("Backward");
                if (backward.StartTime != 0) throw new NotSupportedException("Lockpick pose sampling requires a zero-start Backward sequence.");
                animation.PlaySourceSequence("Backward");
                animation.SampleClampedSourcePose(0);
            }
            _lockStop = _lock.Animation.SequenceRange("Backward").StopTime;
            SetMeta("opennv_unbound_presentation", "retail-camera,lighting,opening-and-replacement-blends,break-animation,visual-jitter,matched-pixels");
        }
        catch { Free(); throw; }
    }

    internal void Publish(float position, float width, float cylinderAngle)
    {
        if (!float.IsFinite(position) || !float.IsFinite(width) || width <= 0 || position < 0 || position > width ||
            !float.IsFinite(cylinderAngle) || cylinderAngle < 0 || cylinderAngle > 90)
            throw new InvalidDataException("Lockpick model pose is invalid.");
        var theta = PinAngle(position, width);
        var c = MathF.Cos(theta); var s = MathF.Sin(theta);
        var basis = GamebryoCoordinate.ConvertBasis([c, 0, -s, 0, 1, 0, s, 0, c], 1, "lockpick pin pivot");
        var p = GamebryoCoordinate.ConvertVector(new(0, 0, -1.6f));
        var q = GamebryoCoordinate.ConvertVector(new(0, 0, .6f));
        var v = -p + q;
        var transform = new Transform3D(basis, p + v - basis * v);
        _pin.Pose.Transform = transform;
        _lock.Animation.SampleClampedSourcePose((float)(_lockStop * (cylinderAngle / 90)));
        _pin.Animation.SetProcess(false);
        // Both root placement and animated descendants contribute to the
        // current geometry bound used by first-party fallback illumination.
        Surface.RefreshLighting();
    }

    internal static float PinAngle(float position, float width) =>
        (float)(((double)position / width * -180 + 90) * 0.01745329238474369);

    internal static float ProjectionSlope(float fov)
    {
        var angle = (float)(fov * 0.01745329238474369 * 0.15000000596046448);
        var slope = (float)Math.Tan(angle) * .75f;
        return float.IsFinite(fov) && float.IsFinite(slope) && slope > 0 ? slope :
            throw new InvalidDataException("Lockpick projection operands are invalid.");
    }

    public override void _Ready()
    {
        // Godot enables overridden _Process callbacks when nodes enter the
        // tree, so enforce the pose-owned clock after both children are ready.
        _lock.Animation.SetProcess(false); _pin.Animation.SetProcess(false);
        Layout();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsInsideTree()) Layout();
    }
    private void Layout() => Surface.SetExtent(Size);
}
