namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutVatsAction : uint { Pistol = 3, Rifle = 4, Handle = 5, Launcher = 6 }

internal static class FalloutVatsActions
{
    // Special/power actions have separate producers, not inferred categories.
    internal static FalloutVatsAction OrdinaryRanged(uint weaponType) => weaponType switch
    {
        3 or 4 => FalloutVatsAction.Pistol,
        5 or 6 or 7 => FalloutVatsAction.Rifle,
        8 => FalloutVatsAction.Handle,
        9 => FalloutVatsAction.Launcher,
        _ => throw new NotSupportedException("VATS action requires its separate melee/thrown/special producer.")
    };
}
