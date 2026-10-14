using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal FalloutPackageCommandRawRotationRead ReadPackageCommandRawRotationZ(
        FalloutPackageCommandFieldInvocation invocation,
        Action<FalloutPackageCommandFieldInvocation> requireActualCommand)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(requireActualCommand);
        requireActualCommand(invocation); RequireIdle();
        var orientation = _referenceOrientation ?? throw new NotSupportedException("source-package-command-raw-reference-orientation-owner-unbound");
        orientation.RequireControlFields(_controlFields);
        if (!invocation.Actor.EnginePlayer || invocation.Actor.Reference != orientation.Binding.Reference ||
            invocation.Actor.Base != orientation.Binding.Base || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Package rotation read left its actual Player reference and command thread.");
        var read = orientation.ReadPackageCommandRawRotationZ(_constructor);
        requireActualCommand(invocation); RequireIdle();
        return read;
    }
}
