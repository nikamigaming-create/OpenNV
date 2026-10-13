namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutLivingWorldBodySnapshot(string Key, string SourceSha256, uint EntityId,
    int Island, byte Motion, IReadOnlyList<string> Masters, IReadOnlyList<string> Slaves,
    IReadOnlyList<string?> Links);

internal sealed record FalloutLivingWorldConstraintSnapshot(string Key, string First, string Second,
    string Master, string Slave, int MasterIndex, int SlaveIndex, uint Priority, int? DataType);

// This captures the real C# structural registry. Solver islands, dynamic
// contacts and matched source world/cold parity remain independent evidence.
internal sealed record FalloutLivingWorldSnapshot(int Schema, string Key, uint NextEntityId,
    IReadOnlyList<FalloutLivingWorldBodySnapshot> Bodies,
    IReadOnlyList<FalloutLivingWorldConstraintSnapshot> Constraints)
{
    internal const int CurrentSchema = 1;
    internal const int FixedIsland = 0;

    internal void Validate()
    {
        if (Schema != CurrentSchema || string.IsNullOrWhiteSpace(Key) || Bodies is null || Constraints is null ||
            Bodies.Any(body => body is null || string.IsNullOrWhiteSpace(body.Key) || body.SourceSha256 is not { Length: 64 } ||
                !body.SourceSha256.All(Uri.IsHexDigit) || body.Island < 0 || body.Motion is < 1 or > 8 ||
                (body.Island == FixedIsland) != (body.Motion == FalloutLivingPhysicalWorld.FixedMotion) ||
                body.Masters is null || body.Slaves is null || body.Links is null) ||
            Constraints.Any(constraint => constraint is null || string.IsNullOrWhiteSpace(constraint.Key) ||
                string.IsNullOrWhiteSpace(constraint.First) || string.IsNullOrWhiteSpace(constraint.Second) ||
                string.IsNullOrWhiteSpace(constraint.Master) || string.IsNullOrWhiteSpace(constraint.Slave)) ||
            Bodies.Select(body => body.Key).Distinct(StringComparer.Ordinal).Count() != Bodies.Count ||
            Constraints.Select(constraint => constraint.Key).Distinct(StringComparer.Ordinal).Count() != Constraints.Count)
            throw new InvalidDataException("Living structural world source/schema/body/constraint identities are invalid.");
        var bodies = Bodies.ToDictionary(body => body.Key, StringComparer.Ordinal);
        var constraints = Constraints.ToDictionary(constraint => constraint.Key, StringComparer.Ordinal);
        var admission = Constraints.Select((constraint, index) => (constraint.Key, index)).ToDictionary(row => row.Key, row => row.index, StringComparer.Ordinal);
        var masterCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var slaveCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var islands = new HashSet<int> { FixedIsland };
        var nextIsland = FixedIsland + 1;
        foreach (var body in Bodies)
            if (islands.Add(body.Island) && body.Island != nextIsland++)
                throw new InvalidDataException("Living world island slots changed first-body admission order.");
        foreach (var constraint in Constraints)
        {
            if (!bodies.TryGetValue(constraint.First, out var first) || !bodies.TryGetValue(constraint.Second, out var second) ||
                first.Key == second.Key || !bodies.TryGetValue(constraint.Master, out var master) || !bodies.TryGetValue(constraint.Slave, out var slave) ||
                master.Key != (first.Motion == FalloutLivingPhysicalWorld.FixedMotion ? second.Key : first.Key) ||
                slave.Key != (master.Key == first.Key ? second.Key : first.Key) || master.Motion == FalloutLivingPhysicalWorld.FixedMotion ||
                constraint.MasterIndex < 0 || constraint.MasterIndex >= master.Masters.Count || master.Masters[constraint.MasterIndex] != constraint.Key ||
                constraint.SlaveIndex < 0 || constraint.SlaveIndex >= slave.Slaves.Count || slave.Slaves[constraint.SlaveIndex] != constraint.Key ||
                first.Island != FixedIsland && second.Island != FixedIsland && first.Island != second.Island)
                throw new InvalidDataException("Living world constraint endpoints, current islands or master/slave indices are inconsistent.");
        }
        foreach (var body in Bodies)
        {
            FalloutLivingWorldConstraintSnapshot? priorMaster = null;
            var priorSlave = -1;
            foreach (var key in body.Masters)
            {
                if (key is null || !constraints.TryGetValue(key, out var constraint) || constraint.Master != body.Key ||
                    priorMaster is not null && (Compare(priorMaster, constraint, bodies) > 0 ||
                        Compare(priorMaster, constraint, bodies) == 0 && admission[priorMaster.Key] >= admission[key]))
                    throw new InvalidDataException("Living world master array changed its priority/unsigned-UID/equal-key admission order.");
                priorMaster = constraint; masterCounts[key] = masterCounts.GetValueOrDefault(key) + 1;
            }
            foreach (var key in body.Slaves)
            {
                if (key is null || !constraints.TryGetValue(key, out var constraint) || constraint.Slave != body.Key || admission[key] <= priorSlave)
                    throw new InvalidDataException("Living world slave list changed actual constraint admission order.");
                priorSlave = admission[key]; slaveCounts[key] = slaveCounts.GetValueOrDefault(key) + 1;
            }
            if (body.Links.Any(key => key is not null && string.IsNullOrWhiteSpace(key)))
                throw new InvalidDataException("Living world linked-wrapper identity is invalid.");
        }
        if (Constraints.Any(constraint => masterCounts.GetValueOrDefault(constraint.Key) != 1 || slaveCounts.GetValueOrDefault(constraint.Key) != 1))
            throw new InvalidDataException("Living world constraint did not occur exactly once in each current membership direction.");
    }

    private static int Compare(FalloutLivingWorldConstraintSnapshot first, FalloutLivingWorldConstraintSnapshot second,
        IReadOnlyDictionary<string, FalloutLivingWorldBodySnapshot> bodies)
    {
        var priority = first.Priority.CompareTo(second.Priority);
        if (priority != 0) return priority;
        var firstId = bodies[first.First].EntityId.CompareTo(bodies[second.First].EntityId);
        return firstId != 0 ? firstId : bodies[first.Second].EntityId.CompareTo(bodies[second.Second].EntityId);
    }
}
