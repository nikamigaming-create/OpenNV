namespace OpenNV.Runtime.Content;

internal static class FalloutSandboxProcedure
{
    // Public values use the retained data phase, not PACK type or motion.
    internal static int Read(int phase, FalloutSandboxActivity? activity) => phase switch
    {
        0 => 45,
        1 => 21,
        2 => 0,
        3 => activity switch
        {
            OpenNV.Runtime.Gameplay.State.FalloutSandboxActivityProgress.NoActivity => -1,
            FalloutSandboxActivity.Furniture => 48,
            FalloutSandboxActivity.Sleep => 8,
            FalloutSandboxActivity.Eat => 10,
            FalloutSandboxActivity.Wander => 7,
            FalloutSandboxActivity.IdleMarker => 46,
            FalloutSandboxActivity.Dialogue => 4,
            _ => throw new NotSupportedException("Active Sandbox phase has no qualified activity.")
        },
        _ => throw new NotSupportedException("Sandbox procedure has no qualified native data phase.")
    };
}
