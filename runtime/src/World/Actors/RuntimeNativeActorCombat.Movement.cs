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
    private string? _movementBlock;
    private string? _stepBlock;
    private (float One, float Two)? _crippledLegSpeedSettings;
    private int? _crippledLegCount;
    private float _crippledLegSpeedMultiplier = 1;
    private string? _crippledLegMovementError;
    private object MotionObservation => new
    {
        radius = _radius,
        waypoints = _pursuitPath.Length,
        cursor = _pursuitCursor,
        waypoint = _pursuitCursor < _pursuitPath.Length ? new float[]
            { _pursuitPath[_pursuitCursor].X, _pursuitPath[_pursuitCursor].Y, _pursuitPath[_pursuitCursor].Z } : null,
        onFloor = _mover?.IsOnFloor(),
        velocity = _mover is { } body ? new float[] { body.Velocity.X, body.Velocity.Y, body.Velocity.Z } : null,
        routeError = _routeError,
        routeRequests = _routeRequests,
        routePlanning = _routeSearch is not null,
        routeMilliseconds = _routeMilliseconds,
        routeSourceMilliseconds = _routeSourceMilliseconds,
        routeMaximumSliceMilliseconds = _routeMaximumSliceMilliseconds,
        blocked = _movementBlock,
        stepBlocked = _stepBlock,
        crippledLegs = _crippledLegCount,
        crippledLegSpeedMultiplier = _crippledLegSpeedMultiplier,
        limbMovementError = _crippledLegMovementError,
        source = "source-BBX-envelope,KF-accumulation,NAVM,BPTD-limb-thresholds;Godot-capsule-and-step-resolver"
    };

    private void PrepareMovement()
    {
        if (_mover is not null) return;
        var mover = (CharacterBody3D)_actor;
        var source = _skeleton.Source;
        var bound = source.Roots.Select(source.ReadNode).SelectMany(node => node.ExtraData).Where(index => index >= 0)
            .Select(source.ReadObject).OfType<FalloutNifBound>().SingleOrDefault(value => value.Name == "BBX") ??
            throw new NotSupportedException("Actor movement requires its source BBX envelope.");
        var scale = _skeleton.UnitsToMetres * _skeleton.Node.Scale.X;
        var radius = Math.Max(bound.Dimensions.X, bound.Dimensions.Y) * scale;
        var height = bound.Dimensions.Z * scale * 2;
        if (radius <= 0 || height < radius * 2) throw new NotSupportedException("Source BBX cannot define an upright movement capsule.");
        _radius = radius * _actor.Scale.X;
        var stats = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2, _state.Templates);
        var acbs = stats.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (acbs.Length != 24) throw new InvalidDataException("Actor speed configuration extent is invalid.");
        _motionScale = scale * _actor.Scale.X * BinaryPrimitives.ReadUInt16LittleEndian(acbs[14..]) / 100;
        if (_actor is RuntimeNativeCreature)
        {
            var model = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 64, _state.Templates);
            var turning = model.ReadSubrecords().Single(field => field.Signature == "TNAM").Data.Span;
            if (turning.Length != 4) throw new InvalidDataException("Creature turn speed extent is invalid.");
            _turnSpeed = Mathf.DegToRad(FalloutProjectile.Number(turning, 0));
        }
        else _turnSpeed = Mathf.DegToRad(FalloutGameSettingFloats.Read(_records, "fCharacterDefaultTurningSpeed"));
        if (_motionScale <= 0 || _turnSpeed <= 0) throw new NotSupportedException("Actor movement/turn speed is not positive.");
        mover.CollisionLayer = 0; mover.CollisionMask = _mask;
        mover.FloorSnapLength = radius;
        mover.AddChild(new CollisionShape3D
        {
            Name = "SourceActorMovementEnvelope",
            Position = Vector3.Up * height / 2,
            Shape = new CapsuleShape3D { Radius = radius, Height = height }
        });
        _mover = mover;
    }

    internal float PreparePortalArrival() { PrepareMovement(); return _radius; }

    private void TurnToward(Vector3 point, double delta)
    {
        var direction = point - _actor.GlobalPosition; direction.Y = 0;
        if (direction.LengthSquared() < .000001f) return;
        var destination = Basis.LookingAt(direction.Normalized(), Vector3.Up).GetRotationQuaternion();
        var current = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var angle = current.AngleTo(destination);
        _actor.GlobalBasis = new Basis(current.Slerp(destination, angle <= .00001f ? 1 : Math.Min(1, _turnSpeed * (float)delta / angle))).Scaled(_actor.Scale);
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
        if (NativeCharacterStep.TryStep(body, motion, _context.StepHeight, out _stepBlock))
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
            var maximumHealth = _world.Health(_state.Reference).Base;
            var count = _world.BodyParts(_state.Reference).CrippledMobilityCount(maximumHealth, limbDamage);
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

}
