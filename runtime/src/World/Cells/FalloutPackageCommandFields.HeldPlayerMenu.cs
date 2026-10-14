namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal FalloutHeldPlayerMenuFirstContext ReadHeldPlayerMenuFirstContext(FalloutMainPlayerCellInvocation invocation,
        Action<FalloutMainPlayerCellInvocation> requireActualHeldChild)
    {
        ArgumentNullException.ThrowIfNull(requireActualHeldChild); invocation.Require(FalloutMainPlayerCellStep.HeldChild);
        requireActualHeldChild(invocation);
        RequireLane(FalloutPackageCommandFieldLane.Animation);
        if (!Actor.EnginePlayer || invocation.Main.Process == Guid.Empty)
            throw new InvalidOperationException("Held first-person pointer query lost its actual current Player/Main thread.");
        var result = new FalloutHeldPlayerMenuFirstContext(Source, Stack, Process, Factory, Constructor, Actor, Epoch,
            invocation.Main.Identity, invocation.Main.Ordinal, _values.FirstPersonAnimation);
        requireActualHeldChild(invocation); invocation.Require(FalloutMainPlayerCellStep.HeldChild); return result;
    }
}
