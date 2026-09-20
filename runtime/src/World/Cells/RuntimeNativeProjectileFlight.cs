using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal readonly record struct RuntimeNativeProjectileContact(
    Godot.Collections.Dictionary Collision, Node? Collider, Vector3 Point, Vector3 Normal, Vector3 Direction);

/// <summary>Source-speed missile, lobber and flame flight with continuous ray collision and bounded bounce response.</summary>
internal sealed partial class RuntimeNativeProjectileFlight : Node3D
{
    private readonly FalloutProjectile _source;
    private readonly Vector3 _origin;
    private readonly Vector3 _initialVelocity;
    private readonly Vector3 _gravity;
    private readonly float _rangeMeters;
    private readonly uint _collisionMask;
    private readonly Godot.Collections.Array<Rid> _exclusions = [];
    private Vector3 _velocity;
    private float _travelledMeters;
    private int _contacts, _detonations;
    private int _bounces;
    private bool _active;
    private string _status = "prepared";

    internal Action<RuntimeNativeProjectileContact>? OnContact { get; set; }
    internal Action<RuntimeNativeProjectileFlight>? OnFinished { get; set; }
    internal string? Error { get; private set; }
    internal string Status => _status;
    internal int Contacts => _contacts;
    internal bool IsFinished => _status is not ("prepared" or "in-flight");
    internal object Observation => new
    {
        projectile = _source.Form.ToString(),
        status = _status,
        position = Vector3Array(GlobalPosition),
        velocity = Vector3Array(_velocity),
        travelledMeters = _travelledMeters,
        rangeMeters = _rangeMeters,
        contacts = _contacts,
        detonations = _detonations,
        bounces = _bounces,
        error = Error,
        boundary = "missile-lobber-and-flame-flight;gravity,source-speed,bounce,flame-actor-pass-through-and-source-explosion-radius-damage;flame-audio-and-travel-time,explosion-distance-attenuation,force,radiation,projectile-beam-visuals,rotation,tracer-and-retail-parity-unmatched"
    };

    internal RuntimeNativeProjectileFlight(FalloutProjectile source, Node3D model, float unitsToMeters,
        float gravityMetersPerSecondSquared, Vector3 origin, Vector3 direction, uint collisionMask,
        IEnumerable<Rid> exclusions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(exclusions);
        if (source.Hitscan || source.Type is not (1 or 2 or 8) || source.Speed <= 0 || source.Model is null ||
            (source.Flags & 0x0800) != 0 || source.HasExplicitRotation)
            throw new NotSupportedException($"Projectile {source.Form} is outside the missile/lobber owner or needs an explosion owner.");
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0 ||
            !float.IsFinite(gravityMetersPerSecondSquared) || gravityMetersPerSecondSquared <= 0 ||
            !origin.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < .99f ||
            collisionMask == 0)
            throw new InvalidDataException("Projectile flight inputs are invalid.");

        _source = source;
        _origin = origin;
        _rangeMeters = source.Range * unitsToMeters;
        _initialVelocity = direction.Normalized() * (source.Speed * unitsToMeters);
        _gravity = Vector3.Down * (source.Gravity * gravityMetersPerSecondSquared);
        _collisionMask = collisionMask;
        foreach (var exclusion in exclusions) _exclusions.Add(exclusion);
        if (!float.IsFinite(_rangeMeters) || _rangeMeters <= 0 || !_initialVelocity.IsFinite() || !_gravity.IsFinite())
            throw new InvalidDataException("Projectile flight motion is invalid.");

        Name = "SourceProjectileFlight";
        TopLevel = true;
        foreach (var collider in model.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>())
        {
            collider.CollisionLayer = 0;
            collider.CollisionMask = 0;
        }
        AddChild(model);
    }

    internal Action<Vector3>? OnDetonate { get; set; }

    internal void Start()
    {
        if (!IsInsideTree() || _active || IsFinished) throw new InvalidOperationException("Projectile flight is not ready to start.");
        if (_source.ExplosionSource is not null && OnDetonate is null)
            throw new InvalidOperationException("Explosive projectile has no runtime detonation owner.");
        GlobalPosition = _origin;
        _velocity = _initialVelocity;
        _active = true;
        _status = "in-flight";
        OrientToVelocity();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active) return;
        if (!double.IsFinite(delta) || delta <= 0)
        {
            Finish("invalid-frame");
            return;
        }

        var seconds = (float)delta;
        var start = GlobalPosition;
        var displacement = _velocity * seconds + _gravity * (.5f * seconds * seconds);
        var length = displacement.Length();
        var remaining = _rangeMeters - _travelledMeters;
        if (!float.IsFinite(length) || remaining <= 0)
        {
            Finish("range-ended");
            return;
        }
        if (length <= .000001f)
        {
            _velocity += _gravity * seconds;
            OrientToVelocity();
            return;
        }
        if (length > remaining) displacement = displacement / length * remaining;
        var destination = start + displacement;
        if (!destination.IsFinite())
        {
            Finish("invalid-trajectory");
            return;
        }

        using var query = PhysicsRayQueryParameters3D.Create(start, destination, _collisionMask, _exclusions);
        query.CollideWithAreas = true;
        using var collision = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (collision.Count != 0)
        {
            var point = collision.TryGetValue("position", out var value) ? value.AsVector3() : destination;
            var normal = collision.TryGetValue("normal", out value) ? value.AsVector3() : Vector3.Zero;
            var direction = displacement.Normalized();
            if (!point.IsFinite() || !normal.IsFinite() || direction.LengthSquared() < .99f)
            {
                Finish("invalid-contact");
                return;
            }
            _travelledMeters += start.DistanceTo(point);
            var collider = collision.TryGetValue("collider", out value) ? value.AsGodotObject() as Node : null;
            var contact = new RuntimeNativeProjectileContact(collision, collider, point, normal, direction);
            if (_source.PassesThroughActors && collider is not null && RuntimeNativeActorCombat.Find(collider) is { } actor)
            {
                var added = 0;
                foreach (var body in actor.CollisionRids)
                {
                    if (_exclusions.Contains(body)) continue;
                    _exclusions.Add(body);
                    added++;
                }
                if (added == 0)
                {
                    Error = "Actor collision did not add any source collision bodies to the projectile exclusion set.";
                    GD.PushError($"OPENNV_PROJECTILE_FLAME_UNBOUND projectile={_source.Form} {Error}");
                    Finish("actor-pass-through-unbound");
                    return;
                }
                var segmentLength = start.DistanceTo(destination);
                var fraction = segmentLength > .000001f
                    ? Mathf.Clamp(start.DistanceTo(point) / segmentLength, 0, 1)
                    : 1;
                _velocity += _gravity * (seconds * fraction);
                if (!NotifyContact(contact))
                {
                    Finish("contact-error");
                    return;
                }
                const float clearActorMeters = .001f;
                _travelledMeters += clearActorMeters;
                GlobalPosition = point + direction * clearActorMeters;
                OrientToVelocity();
                if (_travelledMeters >= _rangeMeters - .0001f) Finish("range-ended");
                return;
            }
            if (_source.BouncyMultiplier > 0)
            {
                if (normal.LengthSquared() < .99f)
                {
                    Finish("invalid-contact-normal");
                    return;
                }
                var segmentLength = start.DistanceTo(destination);
                var fraction = segmentLength > .000001f
                    ? Mathf.Clamp(start.DistanceTo(point) / segmentLength, 0, 1)
                    : 1;
                _velocity = (_velocity + _gravity * (seconds * fraction)).Bounce(normal.Normalized()) * _source.BouncyMultiplier;
                if (!_velocity.IsFinite())
                {
                    Finish("invalid-bounce");
                    return;
                }
                _bounces++;
                GlobalPosition = point + normal.Normalized() * .005f;
                OrientToVelocity();
                if (!NotifyContact(contact))
                {
                    Finish("contact-error");
                    return;
                }
                if (_velocity.LengthSquared() < .0004f) Finish("stopped");
            }
            else
            {
                GlobalPosition = point;
                OrientTo(direction);
                Finish("hit", contact);
            }
            return;
        }

        _travelledMeters += start.DistanceTo(destination);
        GlobalPosition = destination;
        _velocity += _gravity * seconds;
        OrientToVelocity();
        if (_travelledMeters >= _rangeMeters - .0001f) Finish("range-ended");
    }

    private void Finish(string status, RuntimeNativeProjectileContact? contact = null)
    {
        if (IsFinished) return;
        _active = false;
        _status = status;
        if (contact is { } hit && !NotifyContact(hit)) _status = "contact-error";
        if (_source.ExplosionSource is not null && contact is { } impact && !NotifyDetonation(impact.Point))
            _status = "detonation-error";
        try { OnFinished?.Invoke(this); }
        catch (Exception error)
        {
            Error ??= error.Message;
            GD.PushError($"OPENNV_PROJECTILE_FINISH_UNBOUND projectile={_source.Form} {Error}");
        }
        QueueFree();
    }

    private bool NotifyDetonation(Vector3 point)
    {
        _detonations++;
        try
        {
            OnDetonate?.Invoke(point);
            return true;
        }
        catch (Exception error)
        {
            Error = error.Message;
            GD.PushError($"OPENNV_PROJECTILE_DETONATION_UNBOUND projectile={_source.Form} {Error}");
            return false;
        }
    }

    private bool NotifyContact(RuntimeNativeProjectileContact contact)
    {
        _contacts++;
        try
        {
            OnContact?.Invoke(contact);
            return true;
        }
        catch (Exception error)
        {
            Error = error.Message;
            GD.PushError($"OPENNV_PROJECTILE_CONTACT_UNBOUND projectile={_source.Form} {Error}");
            return false;
        }
    }

    private void OrientToVelocity()
    {
        if (_velocity.LengthSquared() > .0001f) OrientTo(_velocity.Normalized());
    }

    private void OrientTo(Vector3 direction)
    {
        var up = Mathf.Abs(direction.Dot(Vector3.Up)) > .999f ? Vector3.Right : Vector3.Up;
        LookAt(GlobalPosition + direction, up);
    }

    private static float[] Vector3Array(Vector3 value) => [value.X, value.Y, value.Z];
}
