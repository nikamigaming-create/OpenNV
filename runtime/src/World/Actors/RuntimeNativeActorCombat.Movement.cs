using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private CharacterBody3D? _mover;
    private NativeCharacterStep.QueryResources? _stepQueries;
    private NativeCapsuleNavigation.EdgeQuery? _movementEdge;
    private CollisionShape3D? _movementEnvelope;
    private readonly List<CollisionShape3D> _movementEnvelopeParts = [];
    private NativeActorMovementEnvelope? _sourceMovementEnvelope;
    private NativeHumanoidMovementEnvelope? _humanoidMovementEnvelope;
    private float _movementSkeletonScale, _movementActorScale;
    private Vector3 _movementWorldScale;
    private float _radius, _motionScale, _turnSpeed;
    private float? _creatureTurnDegrees;
    private float _pathHeadingMotionScale = 1;
    private string? _movementBlock;
    private string? _stepBlock;
    private (float One, float Two)? _crippledLegSpeedSettings;
    private int? _crippledLegCount;
    private float _crippledLegSpeedMultiplier = 1;
    private string? _crippledLegMovementError;
    private object MotionObservation => new
    {
        radius = _radius,
        sourceEnvelope = _humanoidMovementEnvelope?.Observation ?? (object?)_sourceMovementEnvelope,
        routeDoor = DoorNavigationObservation,
        waypoints = _pursuitPath.Length,
        cursor = _pursuitCursor,
        waypoint = _pursuitCursor < _pursuitPath.Length ? new float[]
            { _pursuitPath[_pursuitCursor].X, _pursuitPath[_pursuitCursor].Y, _pursuitPath[_pursuitCursor].Z } : null,
        onFloor = _mover?.IsOnFloor(),
        collisionLayer = _mover?.CollisionLayer,
        collisionMask = _mover?.CollisionMask,
        envelopeDisabled = _movementEnvelope?.Disabled,
        envelopePosition = _movementEnvelope is { } envelope ? new[] { envelope.Position.X, envelope.Position.Y, envelope.Position.Z } : null,
        bodyScale = _mover is { } mover ? new[] { mover.Scale.X, mover.Scale.Y, mover.Scale.Z } : null,
        velocity = _mover is { } body ? new float[] { body.Velocity.X, body.Velocity.Y, body.Velocity.Z } : null,
        routeError = _routeError,
        routeRequests = _routeRequests,
        packageNativeArrivals = _packageRouteArrivals,
        packageRouteReceipt = _packageRouteReceipt is { } receipt && ReferenceEquals(receipt.Path, _pursuitPath) ? new
        {
            package = receipt.Request.Identity.Package.ToString(),
            sourceSha256 = receipt.Request.Identity.SourceSha256,
            stage = receipt.Request.Intent.ReferenceApproach ? "reference-query-complete" : "source-prefix-query-complete",
            requested = new[] { receipt.Request.Identity.Target.X, receipt.Request.Identity.Target.Y, receipt.Request.Identity.Target.Z },
            tolerance = receipt.Request.Identity.Radius,
            endpoint = new[] { receipt.Path[^1].X, receipt.Path[^1].Y, receipt.Path[^1].Z },
            actualDistance = _actor.GlobalPosition.DistanceTo(receipt.Path[^1]),
            receipt.Request.SourcePoints,
            receipt.Request.Intent.Resume
        } : null,
        routePlanning = _routeSearch is not null,
        routeWork = NativeCapsuleNavigation.Work(_routeSearch) ?? _routeWork,
        routeFailures = _routeFailures,
        routeRetrySeconds = Math.Max(0, _routeClock),
        retiredPursuitSearches = _retiredPursuitSearches,
        routeIntent = _routeIntent is { } intent ? new
        {
            scope = intent.ReferenceApproach ? "reference-approach" : "source-corridor",
            target = new[] { intent.Target.X, intent.Target.Y, intent.Target.Z },
            intent.Resume,
            intent.ArrivalRadius,
            corridorPoints = intent.Corridor.Count
        } : null,
        routeSpacing = _routeSpacing,
        routeArrivalRadius = _routeArrivalRadius,
        routeRefinements = _routeRefinements,
        routeRefinementRequests = _routeRefinementRequests,
        coarseRouteError = _coarseRouteError,
        routeMilliseconds = _routeMilliseconds,
        routeSourceMilliseconds = _routeSourceMilliseconds,
        routeMaximumSliceMilliseconds = _routeMaximumSliceMilliseconds,
        animationAccumulationAwaitingTranslation = PackageAccumulationAwaitingTranslation,
        blocked = _movementBlock,
        stepBlocked = _stepBlock,
        crippledLegs = _crippledLegCount,
        crippledLegSpeedMultiplier = _crippledLegSpeedMultiplier,
        limbMovementError = _crippledLegMovementError,
        hitKnockback = _state.HitKnockback,
        hitKnockbackError = _hitKnockbackError,
        source = "source-standard-humanoid-convex-or-BBX-horizontal-controller,KF-accumulation,NAVM,BPTD-limb-thresholds;creature-vertical-custom-envelope-and-floor-tilt-parity-unqualified"
    };

    private void PrepareMovement()
    {
        if (_mover is not null && Mathf.IsEqualApprox(_movementSkeletonScale, _skeleton.Node.Scale.X) &&
            Mathf.IsEqualApprox(_movementActorScale, _actor.Scale.X)) return;
        var refreshing = _mover is not null;
        var mover = (CharacterBody3D)_actor;
        var source = _skeleton.Source;
        var bound = source.Roots.Select(source.ReadNode).SelectMany(node => node.ExtraData).Where(index => index >= 0)
            .Select(source.ReadObject).OfType<FalloutNifBound>().SingleOrDefault(value => value.Name == "BBX") ??
            throw new NotSupportedException("Actor movement requires its source BBX envelope.");
        var scale = _skeleton.UnitsToMetres * _skeleton.Node.Scale.X;
        var radius = Math.Max(bound.Dimensions.X, bound.Dimensions.Y) * scale;
        var height = bound.Dimensions.Z * scale * 2;
        _humanoidMovementEnvelope = _actor is RuntimeNativeNpc ? new(_skeleton.UnitsToMetres) : null;
        _sourceMovementEnvelope = _humanoidMovementEnvelope is null
            ? NativeActorMovementEnvelope.Horizontal(bound.Center, bound.Dimensions,
                _skeleton.Node.Scale.X * _actor.Scale.X, _skeleton.UnitsToMetres) : null;
        if (_humanoidMovementEnvelope is { } humanoid) radius = humanoid.Radius / _actor.Scale.X;
        if (_sourceMovementEnvelope is { } horizontal) radius = horizontal.Radius / _actor.Scale.X;
        if (!float.IsFinite(radius) || !float.IsFinite(height) || radius <= 0 || height <= 0)
            throw new InvalidDataException("Source BBX movement dimensions are invalid.");
        _radius = radius * _actor.Scale.X;
        var stats = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2, _state.Templates);
        var acbs = stats.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (acbs.Length != 24) throw new InvalidDataException("Actor speed configuration extent is invalid.");
        _motionScale = scale * _actor.Scale.X * BinaryPrimitives.ReadUInt16LittleEndian(acbs[14..]) / 100;
        if (_actor is RuntimeNativeCreature)
        {
            _creatureTurnDegrees = FalloutCreatureTurnSpeed.ReadAuthoredDegrees(_records,
                _records.GetEffective(_state.Base), _state.Templates);
            _turnSpeed = Mathf.DegToRad(FalloutCreatureTurnSpeed.ResolveDegrees(_records, _creatureTurnDegrees.Value));
        }
        else _turnSpeed = Mathf.DegToRad(FalloutGameSettingFloats.ReadRetained(_records, "fCharacterDefaultTurningSpeed", nameof(RuntimeNativeActorCombat)));
        if (_motionScale <= 0 || _turnSpeed <= 0) throw new NotSupportedException("Actor movement/turn speed is not positive.");
        GamebryoReferenceEnableRuntime.SetCollisionFilter(mover, 0, _mask);
        mover.FloorSnapLength = _radius;
        mover.FloorMaxAngle = Mathf.DegToRad(_context?.MaximumWalkableSlopeDegrees ?? PlayerConfiguration.DefaultMaximumWalkableSlopeDegrees);
        if (_movementEnvelope is null)
        {
            _movementEnvelope = new() { Name = "SourceActorMovementEnvelope" };
            mover.AddChild(_movementEnvelope);
        }
        foreach (var part in _movementEnvelopeParts) part.Free();
        _movementEnvelopeParts.Clear();
        if (_humanoidMovementEnvelope is { } humanoidEnvelope)
        {
            humanoidEnvelope.Publish(_movementEnvelope, _actor.Scale.X);
        }
        else if (_sourceMovementEnvelope is { } sourceEnvelope)
        {
            sourceEnvelope.Capsules[0].Publish(_movementEnvelope, _actor.Scale.X);
            foreach (var capsule in sourceEnvelope.Capsules.Skip(1))
            {
                var part = new CollisionShape3D { Name = "SourceActorMovementEnvelopePart" };
                capsule.Publish(part, _actor.Scale.X);
                mover.AddChild(part);
                _movementEnvelopeParts.Add(part);
            }
        }
        else
        {
            _movementEnvelope.Position = Vector3.Up * height / 2;
            _movementEnvelope.Quaternion = Quaternion.Identity;
            _movementEnvelope.Shape = height >= radius * 2
                ? new CapsuleShape3D { Radius = radius, Height = height }
                : new CylinderShape3D { Radius = radius, Height = height };
        }
        NativeCharacterStep.SetRootContactOffset(mover, _sourceMovementEnvelope?.RootContactOffset ?? 0);
        NativeCapsuleNavigation.SetDirectionalEnvelope(mover, _sourceMovementEnvelope is not null || _humanoidMovementEnvelope is not null);
        _movementSkeletonScale = _skeleton.Node.Scale.X;
        _movementActorScale = _actor.Scale.X;
        _movementWorldScale = _actor.IsInsideTree() ? _actor.GlobalBasis.Scale : _actor.Basis.Scale;
        _mover = mover;
        if (refreshing)
        {
            _stepQueries?.Dispose(); _stepQueries = null;
            _movementEdge?.Dispose(); _movementEdge = null;
            _movementCorridorFloor = null;
            _routeTurning = _routeFacingPending = false;
            ResetDoorNavigation();
            _routeSearch?.Dispose(); _routeSearch = null;
            _pursuitPath = []; _pursuitCursor = 0;
            _routeClock = 0; _routeStall = 0; _waypointDistance = float.PositiveInfinity;
        }
    }

    internal void RefreshAppearanceMovement() { if (_mover is not null) PrepareMovement(); }

    internal float PreparePortalArrival() { PrepareMovement(); return _radius; }

    private float SourceTurnRadians() => _creatureTurnDegrees is 0
        ? Mathf.DegToRad(FalloutCreatureTurnSpeed.ResolveDegrees(_records, 0)) : _turnSpeed;

    private void TurnToward(Vector3 point, double delta, bool stationary = true)
    {
        var direction = point - _actor.GlobalPosition; direction.Y = 0;
        if (direction.LengthSquared() < .000001f) return;
        // Standing/restored procedures can face before their first movement.
        // Initialize the source turn rate and world scale before applying either.
        PrepareMovement();
        var destination = Basis.LookingAt(direction.Normalized(), Vector3.Up).GetRotationQuaternion();
        var current = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var angle = current.AngleTo(destination);
        // A nonzero authored rate is immutable for this prepared appearance.
        // Zero instead follows the live game setting and must be resolved again.
        var sourceRate = SourceTurnRadians();
        // The moving path owner has a separate rate from stationary facing.
        // Rates above 45 degrees also need its source lookahead/speed factor;
        // retain the existing rate there until that branch is bound.
        var movingFactor = sourceRate <= Mathf.DegToRad(45) ? 3 : 1;
        var turnSpeed = sourceRate * (stationary ? SourceStationaryTurnScale() : movingFactor);
        var budget = turnSpeed * (float)delta;
        _pathHeadingMotionScale = !stationary && angle > budget
            ? angle > 2.7f ? .05f : angle > 2 ? .1f : angle > 1.6f ? .2f : 1
            : 1;
        var reached = angle <= .00001f || !stationary && angle < PathTurnAdmissionRadians;
        _actor.GlobalBasis = new Basis(current.Slerp(destination, reached ? 1 :
            Math.Min(1, budget / angle)).Normalized()).Scaled(_movementWorldScale);
    }

    private void MoveActor(Vector3 localMotion, double delta, Vector3? waypoint = null)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.ActorMove);
        var body = _mover!;
        var motion = body.GlobalBasis.Orthonormalized() * localMotion * _motionScale *
            CrippledLegMovementSpeedMultiplier();
        motion.Y = 0;
        // Preserve the signed KF direction after actual body yaw. Waypoints
        // cap distance; the actual trajectory must independently pass the same
        // whole-capsule and source-floor admission as the route that led here.
        string? pathBlock = null;
        if (waypoint is { } destination)
        {
            motion *= _pathHeadingMotionScale;
            motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, motion, destination);
            if (!motion.IsZeroApprox())
            {
                if (_movementCorridorFloor is null) pathBlock = "source-corridor-floor-unbound";
                else
                {
                    _movementEdge ??= new(body, _context!.StepHeight, Math.Max(.3f, _radius * 2), _context.Resident);
                    if (!_movementEdge.ValidateSegment(body.GlobalPosition, body.GlobalPosition + motion,
                        _movementCorridorFloor, out _, out pathBlock)) motion = Vector3.Zero;
                }
                if (pathBlock is not null) motion = Vector3.Zero;
            }
        }
        var knockback = AdvanceHitKnockbackMotion(delta);
        motion += new Vector3(knockback.X, 0, knockback.Z) * (float)delta;
        if (!_context!.Resident(body.GlobalPosition + motion)) { _movementBlock = "unloaded-collision"; return; }
        var velocity = delta > 0 ? motion / (float)delta : Vector3.Zero;
        velocity.Y = (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) :
            body.Velocity.Y - _previousKnockbackVertical - _context.Gravity * (float)delta) + knockback.Y;
        _previousKnockbackVertical = knockback.Y;
        body.Velocity = velocity;
        if (NativeCharacterStep.TryStep(body, motion, _context.StepHeight, out _stepBlock,
                _stepQueries ??= new(body)))
        { body.Velocity = Vector3.Down * .01f; _previousKnockbackVertical = 0; body.MoveAndSlide(); }
        else body.MoveAndSlide();
        if (body.IsOnFloor() || body.IsOnCeiling()) _previousKnockbackVertical = 0;
        _movementBlock = pathBlock;
        if (body.IsOnWall() && body.GetSlideCollisionCount() > 0 && body.GetSlideCollision(0).GetCollider() is Node collider)
        {
            using var path = collider.GetPath();
            _movementBlock = path.ToString();
        }
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
                FalloutGameSettingFloats.ReadRetained(_records, "fMoveOneCrippledLegSpeedMult", nameof(RuntimeNativeActorCombat)),
                FalloutGameSettingFloats.ReadRetained(_records, "fMoveTwoCrippledLegsSpeedMult", nameof(RuntimeNativeActorCombat)));
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
