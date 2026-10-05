namespace OpenNV.Runtime.Content;

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
    private long _revision;
    internal long Revision => checked(_revision + (_alertRevision?.Invoke() ?? 0));

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
