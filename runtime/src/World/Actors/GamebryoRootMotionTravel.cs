using Godot;

namespace OpenNV.Runtime.World.Actors;

/// <summary>Source root displacement advances the corridor; arrival survives the initial zero-distance sample.</summary>
internal sealed class GamebryoRootMotionTravel
{
    private readonly Vector3[] _waypoints;
    internal int Waypoints => _waypoints.Length;
    internal int Cursor { get; private set; }
    internal bool Active { get; private set; } = true;
    internal bool ArrivalPending { get; private set; }

    internal GamebryoRootMotionTravel(IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints.Count == 0 || waypoints.Any(value => !value.IsFinite()))
            throw new InvalidDataException("Source travel requires a finite, nonempty corridor.");
        _waypoints = waypoints.ToArray();
    }

    internal (Vector3 Position, Vector3? Direction) Advance(Vector3 position, float distance)
    {
        if (!Active) return (position, null);
        if (!position.IsFinite() || !float.IsFinite(distance) || distance < -0.00001f)
            throw new InvalidDataException("Locomotion accumulation has an invalid position or backwards displacement.");
        distance = Math.Max(0, distance);
        Vector3? direction = null;
        while (Cursor < _waypoints.Length)
        {
            var offset = _waypoints[Cursor] - position;
            var length = offset.Length();
            if (!float.IsFinite(length)) throw new InvalidDataException("Source travel segment exceeds its finite extent.");
            if (length > 0) direction = offset;
            if (length > distance) return (position + offset / length * distance, direction);
            position = _waypoints[Cursor++];
            distance -= length;
        }
        Active = false;
        ArrivalPending = true;
        return (position, direction);
    }

    internal bool TakeArrival()
    {
        if (!ArrivalPending) return false;
        // Delivery consumes the completed prefix before calling event/presentation owners.
        // A failed consumer cannot repeat its source event on a later frame.
        ArrivalPending = false;
        return true;
    }

    internal void Cancel()
    {
        Active = false;
        ArrivalPending = false;
    }
}
