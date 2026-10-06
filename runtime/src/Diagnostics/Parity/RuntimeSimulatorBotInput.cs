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
    private BotCombatInput? _combat;
    private bool _firePending, _reloadPending, _combatRelease, _pausePending;
    private long _combatUntil, _menuUntil;
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
        if (_combat is not null && intent.Combat is null)
        {
            _firePending = _reloadPending = false; _combatUntil = 0; _combatRelease = true;
        }
        _combat = intent.Combat;
        _firePending |= intent.Combat?.Fire == true;
        _reloadPending |= intent.Combat?.Reload == true;
        _pausePending |= intent.Pause;
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
        if (_combatRelease)
        {
            LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
            { hand = 1, trigger = 0, buttonB = 0, primary = 0, buttonA = 0, grip = 0, thumbstickX = 0, thumbstickY = 0, leaseMilliseconds = 200 }));
            _combatRelease = false; _pendingHands &= ~2;
            return;
        }
        if (_firePending || _reloadPending)
        {
            // Preserve the actual accepted source muzzle/controller ray. Only
            // native action edges change; delivery is not a combat receipt.
            LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
            {
                hand = 1,
                trigger = _firePending ? 1 : 0,
                buttonB = _reloadPending ? 1 : 0,
                primary = 0,
                buttonA = 0,
                grip = 0,
                thumbstickX = 0,
                thumbstickY = 0,
                leaseMilliseconds = 200
            }));
            _firePending = _reloadPending = false;
            _combatUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
            return;
        }
        if (_combatUntil != 0)
        {
            if (Stopwatch.GetTimestamp() < _combatUntil) return;
            LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
            { hand = 1, trigger = 0, buttonB = 0, primary = 0, buttonA = 0, grip = 0, thumbstickX = 0, thumbstickY = 0, leaseMilliseconds = 200 }));
            _combatUntil = 0;
            return;
        }
        if (_pausePending || _menuUntil != 0)
        {
            if (_pausePending)
            {
                LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
                { hand = 0, menu = 1, grip = 0, trigger = 0, thumbstickX = 0, thumbstickY = 0, leaseMilliseconds = 200 }));
                _pausePending = false; _menuUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
            }
            else if (Stopwatch.GetTimestamp() >= _menuUntil)
            {
                LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
                { hand = 0, menu = 0, grip = 0, trigger = 0, thumbstickX = 0, thumbstickY = 0, leaseMilliseconds = 200 }));
                _menuUntil = 0; _pendingHands &= ~1;
            }
            return;
        }
        // Preserve the exact tracked ray that passed the bot's target check.
        // Input edges must not also reposition the controller before the game
        // consumes Activate. Button release keeps that same pose as well.
        if (_activation || _primaryUntil != 0)
        {
            var now = Stopwatch.GetTimestamp();
            if (_activation)
            {
                _primaryUntil = now + Stopwatch.Frequency / 10; _activation = false;
                LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
                { hand = 1, primary = 1, thumbstickY = 0, leaseMilliseconds = 500 }));
            }
            else if (now >= _primaryUntil)
            {
                _primaryUntil = 0;
                LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
                { hand = 1, primary = 0, thumbstickY = 0, leaseMilliseconds = 500 }));
            }
            else if ((_pendingHands & 1) != 0)
            {
                LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
                { hand = 0, thumbstickX = 0, thumbstickY = 0, leaseMilliseconds = 500 }));
                _pendingHands &= ~1;
            }
            return;
        }
        if ((_pendingHands & (1 << _hand)) == 0) _hand = 1 - _hand;
        if ((_pendingHands & (1 << _hand)) == 0) return;
        if (_combat is not null && _intent.AimAt is null)
        {
            LiveHarnessAtomicFile.Write(controllerPath, JsonSerializer.Serialize(new
            {
                hand = _hand,
                trigger = 0,
                buttonB = 0,
                primary = 0,
                buttonA = 0,
                grip = 0,
                menu = 0,
                thumbstickX = 0,
                thumbstickY = 0,
                leaseMilliseconds = 200
            }));
            _pendingHands &= ~(1 << _hand); _hand = 1 - _hand;
            return;
        }
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
            primary = 0,
            grip = 0,
            trigger = 0,
            buttonA = 0,
            buttonB = 0,
            menu = 0,
            leaseMilliseconds = _combat is null ? 500 : 200,
        }));
        _pendingHands &= ~(1 << _hand); _hand = 1 - _hand;
    }
}
