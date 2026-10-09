namespace OpenNV.Runtime.Gameplay.State;

internal sealed record RuntimeSaveProcessIdentity(Guid Boot, int ProcessId, long StartedUtcTicks)
{
    internal static RuntimeSaveProcessIdentity Current { get; } = Create();
    private static RuntimeSaveProcessIdentity Create()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new(Guid.NewGuid(), Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks);
    }
    internal void Validate()
    {
        if (Boot == Guid.Empty || ProcessId <= 0 || StartedUtcTicks <= 0 || StartedUtcTicks > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Save queue process identity is incomplete.");
    }
    internal bool SameNativeProcess(RuntimeSaveProcessIdentity other) =>
        ProcessId == other.ProcessId && StartedUtcTicks == other.StartedUtcTicks;
}

internal enum RuntimeSaveRequestHandoffKind { SameProcessSession, ColdProcess }
