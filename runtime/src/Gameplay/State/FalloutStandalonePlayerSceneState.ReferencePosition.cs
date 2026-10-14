using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void StoreStartupReferenceYaw(FalloutReferencePositionInvocation invocation, uint bits,
        IFalloutReferenceOrientationChangeConsumer consumer,
        Action<FalloutReferencePositionInvocation> requireActual)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(consumer); ArgumentNullException.ThrowIfNull(requireActual);
        requireActual(invocation);
        if (invocation.ReferenceProcess != _process || invocation.ReferenceConstructor != _constructor ||
            invocation.Ingress != FalloutReferencePositionIngress.ConfiguredStartup ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Startup yaw left its actual reference constructor and caller.");
        var orientation = _referenceOrientation ??
            throw new NotSupportedException("source-startup-yaw-current-reference-orientation-owner-unbound");
        orientation.RequireControlFields(_controlFields);
        orientation.SetRawRotation(invocation.Call, "actual-source-configured-startup-yaw",
            FalloutReferenceRotationAxis.Z, bits, consumer);
        requireActual(invocation);
    }
}
