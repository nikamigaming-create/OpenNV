using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private readonly Dictionary<int, FalloutNifParticleSpawn> _spawnOwners = [];
    private readonly Dictionary<long, int> _particleIndices = [];
    private readonly Queue<long> _collisionWork = [];
    private long _nextParticleIdentity;
    private long _stepOrdinal;
    private bool _advancing;
    internal long PrimaryBirthCount { get; private set; }
    internal long SpawnBirthCount { get; private set; }
    internal long SpawnAttempts { get; private set; }
    internal long SpawnCapacityRefusals { get; private set; }
    internal long EmitterCapacityRefusals { get; private set; }
    internal long LifecycleEventsWithoutSink { get; private set; }
    internal event Action<FalloutNifParticleLifecycleEvent>? LifecycleEvent;

    private object LifecycleObservation => new
    {
        sourceSha256 = _sourceFile!.Sha256,
        PrimaryBirthCount,
        SpawnBirthCount,
        SpawnAttempts,
        SpawnCapacityRefusals,
        EmitterCapacityRefusals,
        step = _stepOrdinal,
        eventSink = LifecycleEvent is null ? "unbound" : "bound-unverified",
        LifecycleEventsWithoutSink,
        live = _particles.Take(ActiveCount).Select(particle => new
        { particle.Identity, particle.ParentIdentity, particle.Generation, particle.SourceBirth }).ToArray(),
        sourceSpawners = _spawnOwners.Values.Select(source => new
        {
            block = source.Block.Index,
            source.Name,
            source.Order,
            source.Target,
            active = _active[source.Name],
            source.Generations,
            source.Probability,
            source.Minimum,
            source.Maximum,
            source.SpeedVariation,
            source.DirectionVariation,
            source.Life,
            source.LifeVariation,
            variationOwner = source.SpeedVariation == 0 && source.DirectionVariation == 0 && source.LifeVariation == 0
                ? "no-variation" : "unowned",
        }).ToArray(),
        engineBirthTimeAndOrdering = "unverified",
        retailIdentityAndRandomSequence = "unverified",
    };

    private void ConfigureSpawning(FalloutNifFile file)
    {
        foreach (var source in _modifiers.OfType<FalloutNifParticleSpawn>()) Register(source);
        foreach (var age in _modifiers.OfType<FalloutNifParticleAgeDeath>())
            Bind(age.Spawn, age.SpawnOnDeath, $"age/death modifier {age.Block.Index}");
        foreach (var colliders in _colliders.Values)
            foreach (var collider in colliders)
                Bind(collider.Spawn, collider.SpawnOnCollide, $"collider {collider.Block.Index}");

        void Bind(int index, bool required, string declaringOwner)
        {
            if (index == -1)
            {
                if (required) throw new InvalidDataException($"Particle {declaringOwner} has no declared spawn modifier.");
                return;
            }
            if (file.ReadObject(index) is not FalloutNifParticleSpawn source)
                throw new InvalidDataException($"Particle {declaringOwner} does not reference a spawn modifier.");
            Register(source);
            if (required) FalloutNifParticleSpawnRules.RequireExecutionOwner(source);
        }
        void Register(FalloutNifParticleSpawn source)
        {
            if (_spawnOwners.ContainsKey(source.Block.Index)) return;
            FalloutNifParticleSpawnRules.Validate(source);
            if (source.Target != _source.Block.Index)
                throw new InvalidDataException($"Particle spawn modifier {source.Block.Index} belongs to a different system.");
            // An event may reference a spawner outside the ordered update list.
            // Its declared target/name/active state still owns that callback.
            if (!_modifiers.Any(modifier => modifier.Block.Index == source.Block.Index) &&
                !_active.TryAdd(source.Name, source.Active))
                throw new InvalidDataException("Linked particle spawn modifier has a duplicate source name.");
            _spawnOwners.Add(source.Block.Index, source);
        }
    }

    private void AgeAndRetire(FalloutNifParticleAgeDeath owner, float delta)
    {
        // Snapshot the prior index range: descendants born by this event are
        // not aged again by the same modifier invocation.
        for (var index = ActiveCount - 1; index >= 0; index--)
        {
            _particles[index].Age += delta;
            if (_particles[index].Age < _particles[index].Life) continue;
            var parent = RetireParticle(index, owner.Block.Index, "age-death");
            if (owner.SpawnOnDeath) Spawn(parent, owner.Spawn, owner.Block.Index, "age-death", delta);
        }
    }

    private void Spawn(Particle parent, int sourceIndex, int declaringOwner, string cause, float motionSeconds,
        Queue<long>? collisionWork = null)
    {
        if (!_spawnOwners.TryGetValue(sourceIndex, out var source))
            throw new InvalidDataException("Particle spawn event has no bound source owner.");
        if (!_active[source.Name]) return;
        SpawnAttempts = checked(SpawnAttempts + 1);
        var copies = FalloutNifParticleSpawnRules.Copies(source, parent.Generation, _random.NextDouble(), _random.NextDouble());
        var available = Math.Min(copies, _particles.Length - ActiveCount);
        SpawnCapacityRefusals = checked(SpawnCapacityRefusals + copies - available);
        for (var copy = 0; copy < available; copy++)
        {
            var child = parent;
            child.Identity = 0;
            child.ParentIdentity = parent.Identity;
            child.Generation = checked((ushort)(parent.Generation + 1));
            child.SourceBirth = source.Block.Index;
            child.Age = 0;
            child.Life = source.Life;
            child.MotionSeconds = motionSeconds;
            AppendParticle(child, true, declaringOwner, cause);
            collisionWork?.Enqueue(_particles[ActiveCount - 1].Identity);
        }
    }

    private void AppendParticle(Particle particle, bool spawned, int declaringOwner, string cause)
    {
        if (ActiveCount >= _particles.Length) throw new InvalidOperationException("Particle append exceeded source capacity.");
        particle.Identity = checked(_nextParticleIdentity + 1);
        _nextParticleIdentity = particle.Identity;
        _particles[ActiveCount] = particle;
        _particleIndices.Add(particle.Identity, ActiveCount++);
        BirthCount = checked(BirthCount + 1);
        if (spawned) SpawnBirthCount = checked(SpawnBirthCount + 1);
        else PrimaryBirthCount = checked(PrimaryBirthCount + 1);
        RecordLifecycle("birth", particle, declaringOwner, cause);
    }

    private Particle RetireParticle(int index, int declaringOwner, string cause)
    {
        var particle = _particles[index];
        if (!_particleIndices.Remove(particle.Identity))
            throw new InvalidOperationException("Particle retirement has no live identity owner.");
        var last = --ActiveCount;
        if (index != last)
        {
            _particles[index] = _particles[last];
            _particleIndices[_particles[index].Identity] = index;
        }
        _particles[last] = default;
        DeathCount = checked(DeathCount + 1);
        RecordLifecycle("death", particle, declaringOwner, cause);
        return particle;
    }

    private void RecordLifecycle(string kind, Particle particle, int declaringOwner, string cause)
    {
        if (LifecycleEvent is null) LifecycleEventsWithoutSink = checked(LifecycleEventsWithoutSink + 1);
        else LifecycleEvent(new(kind, _source.Block.Index, particle.Identity, particle.ParentIdentity,
            particle.Generation, particle.SourceBirth, declaringOwner, cause, _stepOrdinal,
            SimulatedSeconds, particle.Position, particle.Velocity, particle.Age, particle.Life));
    }

    private void ReindexParticles()
    {
        for (var index = 0; index < ActiveCount; index++) _particleIndices[_particles[index].Identity] = index;
    }

    private void ResetLifecycle()
    {
        if (_particleIndices.Count != 0) throw new InvalidOperationException("Completed particle reset has live identity capabilities.");
        foreach (var source in _spawnOwners.Values) _active[source.Name] = source.Active;
        _collisionWork.Clear(); _nextParticleIdentity = _stepOrdinal = 0;
        PrimaryBirthCount = SpawnBirthCount = SpawnAttempts = SpawnCapacityRefusals = EmitterCapacityRefusals = 0;
        LifecycleEventsWithoutSink = 0;
    }

    private void EmitScheduled(FalloutNifParticleModifier modifier, float delta)
    {
        var births = _remainders[modifier.Name] + (double)_rates[modifier.Name] * delta;
        if (!double.IsFinite(births) || births < 0 || births >= long.MaxValue)
            throw new InvalidDataException("Particle emission count exceeds its numeric lifecycle owner.");
        var count = (long)Math.Floor(births);
        _remainders[modifier.Name] = births - count;
        var available = (int)Math.Min(count, _particles.Length - ActiveCount);
        EmitterCapacityRefusals = checked(EmitterCapacityRefusals + count - available);
        for (var birth = 0; birth < available; birth++)
        {
            Emit(modifier);
            _particles[ActiveCount - 1].MotionSeconds = delta;
        }
    }
}

internal sealed record FalloutNifParticleLifecycleEvent(string Kind, int System, long Identity, long ParentIdentity,
    ushort Generation, int SourceBirth, int DeclaringOwner, string Cause, long Step, double Seconds,
    Vector3 Position, Vector3 Velocity, float Age, float Life);
