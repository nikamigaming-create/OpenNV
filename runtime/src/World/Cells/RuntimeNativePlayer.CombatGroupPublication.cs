namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    // The source count reads the actual engine player. Its publication lease
    // is independent of current collision residency, sleeping, and knockdown.
    // Those facts have their own consumers and cannot manufacture a zero list.
    internal bool CombatGroupPlayerPublished => IsInsideTree() &&
        _playerPhysical is { Published: true, Failure: null };
}
