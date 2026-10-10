namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainNativeLeaseRole { Caller, Fade }

// These are actual, process-local delivery handles. They are neither a source
// Main return receipt nor serializable cold state.
internal interface IFalloutMainNativeDeliveryLease : IDisposable
{
    Guid Process { get; }
    Guid Identity { get; }
    string? SourceIdentity { get; }
    int ManagedThread { get; }
    FalloutMainNativeLeaseRole Role { get; }
}

internal sealed record FalloutMainNativeDeliveryBindings(Guid Process, string? SourceIdentity,
    Guid Caller, Guid Fade, Guid? EnteredMain, string? FailedPrefix, bool ProcessRetired)
{
    internal bool Closed => Caller == Guid.Empty && Fade == Guid.Empty && EnteredMain is null;
}
