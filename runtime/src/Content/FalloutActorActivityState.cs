namespace OpenNV.Runtime.Content;

/// <summary>Mutable activity flags for a freshly instantiated actor.</summary>
internal sealed class FalloutActorActivityState
{
    internal bool Alerted { get; private set; }
    internal bool Attacked { get; private set; }
    internal bool WeaponDrawn { get; private set; }
    internal long Revision { get; private set; }

    internal void SetAlerted(bool value)
    {
        if (Alerted == value) return;
        Alerted = value;
        Revision++;
    }

    internal void RecordAttack()
    {
        if (Attacked) return;
        Attacked = true;
        Revision++;
    }

    internal void SetWeaponDrawn(bool value)
    {
        if (WeaponDrawn == value) return;
        WeaponDrawn = value;
        Revision++;
    }
}
