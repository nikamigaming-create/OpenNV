using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.Presentation.OpenXR;

namespace OpenNV.Runtime.Diagnostics.Parity;

// This adapter sends device input to the explicitly selected simulator. The
// game continues to consume OpenXR poses/actions through its normal runtime.
internal sealed class RuntimeSimulatorBotInput
{
    private readonly string _directory;
    private readonly NativeXrRig _rig;
    private SteeringIntent _intent;
    private bool _activation;
    private long _primaryUntil;
    private int _hand;
    private int _pendingHands;
    private bool _headPending;
    private Vector3 _headPosition;
    private Basis _headBasis;
    internal static string? DirectoryPath => System.Environment.GetEnvironmentVariable("OPENXR_SIMULATOR_DATA_DIR");

    internal RuntimeSimulatorBotInput(NativeXrRig rig)
    {
        var directory = DirectoryPath;
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new NotSupportedException("Bot XR input requires an explicitly selected simulator directory.");
        if (!LiveHarnessAtomicFile.TryRead(Path.Combine(directory, "runtime_status.json"), out var status, out var error))
            throw new InvalidOperationException("Simulator capability status is unavailable: " + error);
        using var document = JsonDocument.Parse(status);
        if (!document.RootElement.TryGetProperty("controller_input_leases", out var leases) || !leases.GetBoolean())
            throw new NotSupportedException("Simulator must support bounded controller input leases before bot control.");
        _directory = directory; _rig = rig;
    }

    internal void Submit(SteeringIntent intent, bool activate)
    {
        _intent = intent; _activation |= activate;
        _headPosition = _rig.Camera.Position;
        var global = new Basis(Vector3.Up, intent.YawRadians) * _rig.Camera.GlobalBasis;
        global = new Basis(global.X.Normalized(), intent.PitchRadians) * global;
        _headBasis = _rig.Origin.GlobalBasis.Inverse() * global;
        _headPending = intent.YawRadians != 0 || intent.PitchRadians != 0;
        _pendingHands = 3;
    }

    internal void Pump()
    {
        var headPath = Path.Combine(_directory, "head_pose_command.json");
        var forward = -_headBasis.Z;
        var yaw = -MathF.Atan2(forward.X, -forward.Z);
        var pitch = MathF.Asin(Math.Clamp(forward.Y, -1, 1));
        if (_headPending && !File.Exists(headPath))
        {
            LiveHarnessAtomicFile.Write(headPath, JsonSerializer.Serialize(new
            { x = _headPosition.X, y = _headPosition.Y, z = _headPosition.Z, yaw, pitch, roll = 0 }));
            _headPending = false;
        }
        var controllerPath = Path.Combine(_directory, "controller_pose_command.json");
        if (File.Exists(controllerPath)) return;
        if (_activation) _hand = 1;
        else if ((_pendingHands & (1 << _hand)) == 0) _hand = 1 - _hand;
        var releasingPrimary = _primaryUntil != 0 && Stopwatch.GetTimestamp() >= _primaryUntil;
        if (releasingPrimary) { _hand = 1; _pendingHands |= 2; }
        if ((_pendingHands & (1 << _hand)) == 0 && !_activation) return;
        if (_activation)
        {
            _primaryUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
            _activation = false;
        }
        var primary = _primaryUntil != 0 && !releasingPrimary;
        if (releasingPrimary) _primaryUntil = 0;
        // A simulated standing user's wrists stay below/in front of their eyes
        // as they turn. These are controller poses, never player/body writes.
        var wrist = _headPosition + _headBasis.X * (_hand == 0 ? -.22f : .22f) + forward * .32f + Vector3.Down * .35f;
        // Head and right-hand aim are independent tracked devices. A parallel
        // ray from an offset wrist misses the reference at close range.
        if (_hand == 1 && _intent.AimAt is { } target)
        {
            var localTarget = _rig.Origin.ToLocal(new(target.X, target.Y, target.Z));
            var direction = localTarget - wrist;
            if (direction.LengthSquared() > .0001f)
            {
                yaw = -MathF.Atan2(direction.X, -direction.Z);
                pitch = MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
            }
        }
        LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
        {
            hand = _hand,
            space = "local",
            posX = wrist.X,
            posY = wrist.Y,
            posZ = wrist.Z,
            yaw,
            pitch,
            roll = 0,
            thumbstickX = 0,
            thumbstickY = _hand == 0 && _intent.Forward ? 1 : 0,
            thumbstickClick = 0,
            primary = _hand == 1 && primary ? 1 : 0,
            grip = 0,
            trigger = 0,
            leaseMilliseconds = 500,
        }));
        _pendingHands &= ~(1 << _hand); _hand = 1 - _hand;
    }
}
