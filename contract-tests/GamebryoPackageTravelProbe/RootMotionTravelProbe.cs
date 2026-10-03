using Godot;
using OpenNV.Runtime.World.Actors;

internal static class RootMotionTravelProbe
{
    internal static void Exercise()
    {
        var initial = new Vector3(2, 3, 4);
        var immediate = new GamebryoRootMotionTravel([initial, initial]);
        var zero = immediate.Advance(initial, 0);
        if (zero.Position != initial || immediate.Active || immediate.Cursor != 2 || !immediate.ArrivalPending)
            throw new InvalidOperationException("Initial source root sample lost its zero-length arrival.");
        immediate.Advance(initial, 100);
        if (!immediate.TakeArrival() || immediate.TakeArrival())
            throw new InvalidOperationException("A source arrival was lost before binding or delivered twice.");

        var path = new[] { Vector3.Zero, new Vector3(3, 0, 0), new Vector3(3, 4, 0) };
        var travel = new GamebryoRootMotionTravel(path);
        path[^1] = Vector3.One; // The caller cannot mutate an admitted corridor.
        var first = travel.Advance(Vector3.Zero, 2);
        if (first.Position != new Vector3(2, 0, 0) || travel.Cursor != 1 || travel.TakeArrival())
            throw new InvalidOperationException("Source displacement completed a route prematurely.");
        var second = travel.Advance(first.Position, 3);
        if (second.Position != new Vector3(3, 2, 0) || second.Direction != new Vector3(0, 4, 0) || !travel.Active)
            throw new InvalidOperationException("Source displacement was not conserved across a turn.");
        foreach (var distance in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            if (!Rejects(() => travel.Advance(second.Position, distance)) || travel.Cursor != 2 || !travel.Active)
                throw new InvalidOperationException("Invalid accumulation changed the route prefix.");
        }
        var final = travel.Advance(second.Position, 20);
        if (final.Position != new Vector3(3, 4, 0) || travel.Active || !travel.TakeArrival() || travel.TakeArrival())
            throw new InvalidOperationException("Source travel overshot or repeated its arrival.");

        var restored = GamebryoRootMotionTravel.RestoreCompleted([Vector3.Zero, initial]);
        if (restored.Active || restored.Cursor != restored.Waypoints || restored.TakeArrival() ||
            restored.Advance(initial, 100).Position != initial || restored.TakeArrival())
            throw new InvalidOperationException("A retained completed route restarted motion or replayed its consumed arrival.");

        var cancelled = new GamebryoRootMotionTravel([initial]);
        cancelled.Advance(initial, 0);
        cancelled.Cancel();
        if (cancelled.Active || cancelled.TakeArrival())
            throw new InvalidOperationException("Replacement delivered the cancelled route's arrival.");
        var interrupted = new GamebryoRootMotionTravel([Vector3.One]);
        interrupted.Cancel();
        if (interrupted.Advance(Vector3.Zero, 100).Position != Vector3.Zero || interrupted.TakeArrival())
            throw new InvalidOperationException("An interrupted route moved or completed.");
        if (!Rejects(() => new GamebryoRootMotionTravel([])) ||
            !Rejects(() => new GamebryoRootMotionTravel([new(float.NaN, 0, 0)])) ||
            !Rejects(() => new GamebryoRootMotionTravel([initial]).Advance(new(float.NaN, 0, 0), 1)))
            throw new InvalidOperationException("Malformed source corridors were admitted.");
        Console.WriteLine("OPENNV_ROOT_MOTION_TRAVEL_PASS zeroSampleArrival=true turnDistanceConserved=true exactOnce=true completedColdNoReplay=true cancellation=true malformedRejected=true");
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}
