using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private CharacterBody3D? _mover;
    private float _radius, _motionScale, _turnSpeed;
    private Vector3[] _pursuitPath = [];
    private int _pursuitCursor;
    private Vector3 _routeTarget;
    private double _routeClock;
    private string? _movementBlock;
    private (float One, float Two)? _crippledLegSpeedSettings;
    private int? _crippledLegCount;
    private float _crippledLegSpeedMultiplier = 1;
    private string? _crippledLegMovementError;
    private object MotionObservation => new
    {
        radius = _radius,
        waypoints = _pursuitPath.Length,
        cursor = _pursuitCursor,
        blocked = _movementBlock,
        crippledLegs = _crippledLegCount,
        crippledLegSpeedMultiplier = _crippledLegSpeedMultiplier,
        limbMovementError = _crippledLegMovementError,
        source = "source-BBX-envelope,KF-accumulation,NAVM,BPTD-limb-thresholds;Godot-capsule-and-step-resolver"
    };

    private void PrepareMovement()
    {
        _mover = (CharacterBody3D)_actor;
        var source = _skeleton.Source;
        var bound = source.Roots.Select(source.ReadNode).SelectMany(node => node.ExtraData).Where(index => index >= 0)
            .Select(source.ReadObject).OfType<FalloutNifBound>().SingleOrDefault(value => value.Name == "BBX") ??
            throw new NotSupportedException("Actor movement requires its source BBX envelope.");
        var scale = _skeleton.UnitsToMetres * _skeleton.Node.Scale.X;
        var radius = Math.Max(bound.Dimensions.X, bound.Dimensions.Y) * scale;
        var height = bound.Dimensions.Z * scale * 2;
        if (radius <= 0 || height < radius * 2) throw new NotSupportedException("Source BBX cannot define an upright movement capsule.");
        _radius = radius * _actor.Scale.X;
        _mover.CollisionLayer = 0; _mover.CollisionMask = _mask;
        _mover.FloorSnapLength = radius;
        _mover.AddChild(new CollisionShape3D
        {
            Name = "SourceActorMovementEnvelope",
            Position = Vector3.Up * height / 2,
            Shape = new CapsuleShape3D { Radius = radius, Height = height }
        });
        var stats = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2);
        var acbs = stats.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (acbs.Length != 24) throw new InvalidDataException("Actor speed configuration extent is invalid.");
        _motionScale = scale * _actor.Scale.X * BinaryPrimitives.ReadUInt16LittleEndian(acbs[14..]) / 100;
        if (_actor is RuntimeNativeCreature)
        {
            var model = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 64);
            var turning = model.ReadSubrecords().Single(field => field.Signature == "TNAM").Data.Span;
            if (turning.Length != 4) throw new InvalidDataException("Creature turn speed extent is invalid.");
            _turnSpeed = Mathf.DegToRad(FalloutProjectile.Number(turning, 0));
        }
        else _turnSpeed = Mathf.DegToRad(FalloutGameSettingFloats.Read(_records, "fActorTurnDegree"));
        if (_motionScale <= 0 || _turnSpeed <= 0) throw new NotSupportedException("Actor movement/turn speed is not positive.");
    }

    private void TurnToward(Vector3 point, double delta)
    {
        var direction = point - _actor.GlobalPosition; direction.Y = 0;
        if (direction.LengthSquared() < .000001f) return;
        var destination = Basis.LookingAt(direction.Normalized(), Vector3.Up).GetRotationQuaternion();
        var current = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var angle = current.AngleTo(destination);
        _actor.GlobalBasis = new Basis(current.Slerp(destination, angle <= .00001f ? 1 : Math.Min(1, _turnSpeed * (float)delta / angle))).Scaled(_actor.Scale);
    }

    private Vector3 PursuitTarget(Vector3 target, double delta)
    {
        _routeClock -= delta;
        if (_routeClock <= 0 && (_pursuitCursor >= _pursuitPath.Length || target.DistanceTo(_routeTarget) > _radius))
        {
            _pursuitPath = _context!.Route(_actor.GlobalPosition, target);
            _pursuitCursor = 0; _routeTarget = target; _routeClock = .5;
        }
        while (_pursuitCursor < _pursuitPath.Length)
        {
            var offset = _pursuitPath[_pursuitCursor] - _actor.GlobalPosition;
            if (new Vector2(offset.X, offset.Z).Length() > _radius * .5f) return _pursuitPath[_pursuitCursor];
            _pursuitCursor++;
        }
        return target;
    }

    private void MoveActor(Vector3 localMotion, double delta)
    {
        var body = _mover!;
        var motion = body.GlobalBasis.Orthonormalized() * localMotion * _motionScale *
            CrippledLegMovementSpeedMultiplier();
        motion.Y = 0;
        if (!_context!.Resident(body.GlobalPosition + motion)) { _movementBlock = "unloaded-collision"; return; }
        var velocity = delta > 0 ? motion / (float)delta : Vector3.Zero;
        velocity.Y = body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - _context.Gravity * (float)delta;
        body.Velocity = velocity;
        if (NativeCharacterStep.TryStep(body, motion, _context.StepHeight))
        { body.Velocity = Vector3.Down * .01f; body.MoveAndSlide(); }
        else body.MoveAndSlide();
        _movementBlock = body.IsOnWall() && body.GetSlideCollisionCount() > 0
            ? (body.GetSlideCollision(0).GetCollider() as Node)?.GetPath().ToString() : null;
    }

    private float CrippledLegMovementSpeedMultiplier()
    {
        if (_state.Injury?.LimbDamage is not { Count: > 0 } limbDamage)
        {
            _crippledLegCount = 0;
            _crippledLegSpeedMultiplier = 1;
            _crippledLegMovementError = null;
            return 1;
        }

        try
        {
            var parts = _world.BodyParts(_state.Reference).Parts;
            var left = parts.Where(part => part.HealthPercent > 0 && LegRegion(part) == "left").ToArray();
            var right = parts.Where(part => part.HealthPercent > 0 && LegRegion(part) == "right").ToArray();
            if (left.Length != 1 || right.Length != 1)
                throw new NotSupportedException("Actor locomotion requires one source part for each leg.");

            var maximumHealth = _world.Health(_state.Reference).Base;
            static bool Crippled(FalloutBodyPart part, float maximumHealth, IReadOnlyDictionary<byte, float> damage)
            {
                var threshold = maximumHealth * part.HealthPercent / 100.0f;
                return threshold > 0 && damage.GetValueOrDefault(part.Type) >= threshold;
            }
            var count = (Crippled(left[0], maximumHealth, limbDamage) ? 1 : 0) +
                (Crippled(right[0], maximumHealth, limbDamage) ? 1 : 0);
            _crippledLegCount = count;
            if (count == 0)
            {
                _crippledLegSpeedMultiplier = 1;
                _crippledLegMovementError = null;
                return 1;
            }

            _crippledLegSpeedSettings ??= (
                FalloutGameSettingFloats.Read(_records, "fMoveOneCrippledLegSpeedMult"),
                FalloutGameSettingFloats.Read(_records, "fMoveTwoCrippledLegsSpeedMult"));
            var multiplier = count == 1 ? _crippledLegSpeedSettings.Value.One : _crippledLegSpeedSettings.Value.Two;
            if (!float.IsFinite(multiplier) || multiplier < 0)
                throw new InvalidDataException("Source crippled-leg movement multiplier is invalid.");
            _crippledLegSpeedMultiplier = multiplier;
            _crippledLegMovementError = null;
            return multiplier;
        }
        catch (Exception error)
        {
            _crippledLegCount = null;
            _crippledLegSpeedMultiplier = 1;
            if (!string.Equals(_crippledLegMovementError, error.Message, StringComparison.Ordinal))
                GD.PushError($"OPENNV_ACTOR_LIMB_MOVEMENT_UNBOUND reference={_state.Reference} {error.Message}");
            _crippledLegMovementError = error.Message;
            return 1;
        }
    }

    private static string? LegRegion(FalloutBodyPart part)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var name = Normalize(part.Name);
        var node = Normalize(part.Node);
        var left = name.Contains("left", StringComparison.Ordinal) || node.Contains("bip01l", StringComparison.Ordinal) ||
            name.StartsWith("lleg", StringComparison.Ordinal);
        var right = name.Contains("right", StringComparison.Ordinal) || node.Contains("bip01r", StringComparison.Ordinal) ||
            name.StartsWith("rleg", StringComparison.Ordinal);
        var leg = name.Contains("leg", StringComparison.Ordinal) || name.Contains("thigh", StringComparison.Ordinal) ||
            name.Contains("calf", StringComparison.Ordinal) || name.Contains("foot", StringComparison.Ordinal) ||
            node.Contains("thigh", StringComparison.Ordinal) || node.Contains("calf", StringComparison.Ordinal) ||
            node.Contains("foot", StringComparison.Ordinal);
        return leg && left ? "left" : leg && right ? "right" : null;
    }
}
