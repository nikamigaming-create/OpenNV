using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private const float PathTurnAdmissionRadians = MathF.PI / 180;
    private bool _routeFacingPending;
    private bool _routeTurning;
    private readonly Dictionary<string, NativeActorCombatAnimation> _pathTurnAnimations = [];

    private float SourceStationaryTurnScale()
    {
        var combat = _state.Engagement is not null;
        var name = _actor is RuntimeNativeCreature
            ? combat ? "fCreatureCombatTurnSpeedScale:Pathfinding" : "fCreatureTurnSpeedScale:Pathfinding"
            : combat ? "fAICombatTurnSpeedScale:Pathfinding" : "fAITurnSpeedScale:Pathfinding";
        var value = _records.IniSettings.Float(name);
        if (!float.IsFinite(value) || value < 0)
            throw new InvalidDataException("Source actor stationary turn scale is invalid.");
        return value;
    }

    private static float WrappedHeading(float angle)
    {
        angle %= Mathf.Tau;
        return angle > Mathf.Pi ? angle - Mathf.Tau : angle < -Mathf.Pi ? angle + Mathf.Tau : angle;
    }

    // Returns true for the whole stationary-turn tick, including its final
    // yaw publication. Forward accumulation starts on the following tick.
    private bool FacePath(Vector3 target, double delta, FalloutActorPathTurn? retained,
        out FalloutActorPathTurn? continuation, out bool left)
    {
        PrepareMovement();
        var forward = -_actor.GlobalBasis.Orthonormalized().Z;
        var current = MathF.Atan2(-forward.X, -forward.Z);
        var offset = target - _actor.GlobalPosition; offset.Y = 0;
        var desired = offset.IsZeroApprox() ? current : MathF.Atan2(-offset.X, -offset.Z);
        var turn = retained;
        // Disposable route reconstruction does not replace an already chosen
        // facing latch. Finish it before admitting the new path's first leg.
        if (_routeFacingPending && turn is null)
        {
            _routeFacingPending = false;
            var difference = WrappedHeading(desired - current);
            if (turn is null && MathF.Abs(difference) > PathTurnAdmissionRadians)
            {
                var minimum = _records.NumericSettings.Float("fActorTurnAnimMinTime");
                if (!float.IsFinite(minimum) || minimum < 0)
                    throw new InvalidDataException("Source actor minimum turn time is invalid.");
                turn = new(target.X, target.Y, target.Z, desired, difference > 0, minimum);
            }
        }
        if (turn is null)
        {
            continuation = null; left = false; _routeTurning = false;
            TurnToward(target, delta, stationary: false);
            return false;
        }
        turn.Validate();
        left = turn.Left;
        var remaining = WrappedHeading(turn.HeadingRadians - current);
        var budget = SourceTurnRadians() * SourceStationaryTurnScale() * (float)delta;
        var reached = MathF.Abs(remaining) <= PathTurnAdmissionRadians || budget > MathF.Abs(remaining);
        var yaw = reached ? turn.HeadingRadians : WrappedHeading(current + (left ? budget : -budget));
        _actor.GlobalBasis = new Basis(Vector3.Up, yaw).Scaled(_movementWorldScale);
        continuation = reached && turn.MinimumRemainingSeconds <= 0 ? null : turn with
        { MinimumRemainingSeconds = Math.Max(0, turn.MinimumRemainingSeconds - (float)delta) };
        _routeTurning = true;
        return true;
    }

    internal static float SourceArrivalHeading(Basis parentFrame, float sourceHeading)
    {
        if (!float.IsFinite(sourceHeading)) throw new InvalidDataException("Arrival heading is not finite.");
        var source = GamebryoCoordinate.ConvertReferenceEuler(new(0, 0, sourceHeading), 1);
        var forward = -(parentFrame.Orthonormalized() * source).Z;
        forward.Y = 0;
        if (!forward.IsFinite() || forward.IsZeroApprox())
            throw new NotSupportedException("Arrival heading has no planar world-frame direction.");
        return MathF.Atan2(-forward.X, -forward.Z);
    }

    // The source Travel procedure requests its authored end heading before
    // advancing to Done. Admission changes no root/clock; ordinary physics
    // consumes the independent turn even after the package end event fires.
    internal void CompleteTravelWithHeading(FalloutPluginRecord package, FalloutTravelProgress progress,
        Vector3 endpoint, float sourceHeading, bool sneaking)
    {
        if (!progress.Complete || !float.IsFinite(sourceHeading) || !endpoint.IsFinite())
            throw new InvalidDataException("Travel arrival heading has invalid source input.");
        if (_state.PackageMotion is not { } motion || motion.Package != package.FormKey)
            throw new InvalidOperationException("Travel arrival heading has no matching motion owner.");
        if (OwnsPose || !PackageMovementReady || motion.ArrivalTurn is not null)
            throw new NotSupportedException("Travel arrival heading has a competing mover owner.");
        if (_routeError is not null)
            throw new NotSupportedException("Travel arrival heading cannot substitute for a failed source route.");
        PrepareMovement();
        var forward = -_actor.GlobalBasis.Orthonormalized().Z;
        var current = MathF.Atan2(-forward.X, -forward.Z);
        var desired = SourceArrivalHeading(_actor.GetParent<Node3D>().GlobalBasis, sourceHeading);
        var difference = WrappedHeading(desired - current);
        FalloutActorPathTurn? goal = null;
        if (difference != 0)
        {
            var minimum = MathF.Abs(difference) > PathTurnAdmissionRadians
                ? _records.NumericSettings.Float("fActorTurnAnimMinTime") : 0;
            if (!float.IsFinite(minimum) || minimum < 0)
                throw new InvalidDataException("Source actor minimum turn time is invalid.");
            if (MathF.Abs(difference) > PathTurnAdmissionRadians) _ = PathTurnClip(difference > 0, sneaking);
            goal = new(endpoint.X, endpoint.Y, endpoint.Z, desired, difference > 0, minimum);
            goal.Validate();
        }
        // One publication after all admission/asset checks. Source end-event
        // callbacks can observe this pending goal and the still-current yaw.
        DisposeRouteSearch();
        SetTravelProgress(progress);
        _state.PackageMotion = _state.PackageMotion! with { PathTurn = null, ArrivalTurn = goal };
    }

    private NativeActorCombatAnimation PathTurnClip(bool left, bool sneaking = false)
    {
        var group = (sneaking ? "sneak" : "") + (left ? "mtturnleft" : "mtturnright");
        if (_pathTurnAnimations.TryGetValue(group, out var existing)) return existing;
        string[] folders = _actor is RuntimeNativeNpc npc
            ? ["", "locomotion/" + (npc.Appearance.Female ? "female" : "male"), "locomotion"]
            : ["", "locomotion"];
        var selected = _content.ActorAnimations.ResolveTurn(
            _skeletonPath[.._skeletonPath.LastIndexOf('/')], left, sneaking, folders);
        var clip = Clip(selected.Path, true);
        _pathTurnAnimations.Add(group, clip);
        return clip;
    }

    private bool IsPathClip(string path) => path.Equals(_movementPath, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(_combatIdle?.Path, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(PathTurnClip(true).Path, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(PathTurnClip(false).Path, StringComparison.OrdinalIgnoreCase);
}
