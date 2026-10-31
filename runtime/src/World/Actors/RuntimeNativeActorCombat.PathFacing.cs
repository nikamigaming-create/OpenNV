using Godot;
using OpenNV.Runtime.Content;
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
