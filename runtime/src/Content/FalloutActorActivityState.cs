namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorActivitySnapshot(bool Alerted, bool Attacked, bool WeaponDrawn,
    bool Running, bool Sneaking, bool InCombat, long Revision)
{
    internal void Validate()
    {
        if (Revision < 0 || Revision == long.MaxValue)
            throw new InvalidDataException("Saved actor activity revision is invalid.");
    }
}

/// <summary>Mutable activity flags for a freshly instantiated actor.</summary>
internal sealed class FalloutActorActivityState
{
    private bool _alerted;
    private Func<bool>? _readAlerted;
    private Action<bool>? _writeAlerted;
    private Func<long>? _alertRevision;
    internal bool Alerted => _readAlerted?.Invoke() ?? _alerted;
    internal bool Attacked { get; private set; }
    internal bool WeaponDrawn { get; private set; }
    internal bool Running { get; private set; }
    internal bool Sneaking { get; private set; }
    internal bool InCombat { get; private set; }
    // Native process initialization is clear. This is the victim panic
    // notification, not possession of a grenade or generic combat activity.
    internal bool PlantedExplosive { get; private set; }
    internal bool Swimming { get; private set; }
    internal bool ForceHitReaction { get; private set; }
    private long _revision;
    internal long Revision => checked(_revision + (_alertRevision?.Invoke() ?? 0));

    internal FalloutActorActivitySnapshot Capture()
    {
        if (PlantedExplosive)
            throw new NotSupportedException("Active planted-explosive notification requires its projectile continuation owner.");
        if (Swimming || ForceHitReaction)
            throw new NotSupportedException("Active swimming/forced-hit process requires its exact continuation owner.");
        return new(Alerted, Attacked, WeaponDrawn, Running, Sneaking, InCombat, Revision);
    }

    // The planted-projectile action itself remains unimplemented. Its eventual
    // notification/retirement must drive this same process lane; inventory
    // contents, ordinary projectiles and idle selection cannot synthesize it.
    internal void NotifyPlantedExplosive()
    {
        if (PlantedExplosive) return;
        PlantedExplosive = true;
        _revision++;
    }

    internal void SetSwimming(bool value)
    {
        if (Swimming == value) return;
        Swimming = value; _revision++;
    }

    internal void SetForceHitReaction(bool value)
    {
        if (ForceHitReaction == value) return;
        ForceHitReaction = value; _revision++;
    }

    internal void RetirePlantedExplosive()
    {
        if (!PlantedExplosive) return;
        PlantedExplosive = false;
        _revision++;
    }

    internal void Restore(FalloutActorActivitySnapshot snapshot)
    {
        if (Swimming || ForceHitReaction)
            throw new NotSupportedException("Active swimming/forced-hit process must retire before activity restoration.");
        snapshot.Validate();
        if (PlantedExplosive)
            throw new NotSupportedException("An active planted projectile must retire before restoring actor activity.");
        if (_readAlerted is not null && Alerted != snapshot.Alerted)
            throw new InvalidDataException("Saved actor activity differs from its shared alert owner.");
        var revision = checked(snapshot.Revision - (_alertRevision?.Invoke() ?? 0));
        _alerted = snapshot.Alerted; Attacked = snapshot.Attacked; WeaponDrawn = snapshot.WeaponDrawn;
        Running = snapshot.Running; Sneaking = snapshot.Sneaking; InCombat = snapshot.InCombat;
        // The world owns its own alert generation. Preserve the observed total
        // without writing that owner or replaying any activity transition.
        _revision = revision;
    }

    internal void BindAlerted(Func<bool> read, Action<bool> write, Func<long> revision)
    {
        if (_readAlerted is not null) throw new InvalidOperationException("Actor alert state already has a world owner.");
        _readAlerted = read ?? throw new ArgumentNullException(nameof(read));
        _writeAlerted = write ?? throw new ArgumentNullException(nameof(write));
        _alertRevision = revision ?? throw new ArgumentNullException(nameof(revision));
    }

    internal void SetAlerted(bool value)
    {
        if (Alerted == value) return;
        _writeAlerted?.Invoke(value);
        _alerted = value;
        _revision++;
    }

    internal void RecordAttack()
    {
        if (Attacked) return;
        Attacked = true;
        _revision++;
    }

    internal void SetWeaponDrawn(bool value)
    {
        if (WeaponDrawn == value) return;
        WeaponDrawn = value;
        _revision++;
    }

    internal void SetMovement(bool running, bool sneaking)
    {
        if (Running == running && Sneaking == sneaking) return;
        Running = running;
        Sneaking = sneaking;
        _revision++;
    }

    internal void SetCombat(bool value)
    {
        if (InCombat == value) return;
        InCombat = value;
        _revision++;
    }
}
