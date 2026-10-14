using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferencePositionState
{
    internal FalloutActorFollowerExtraRead ReadFollowerExtra(FalloutReferencePositionInvocation invocation,
        FalloutReferenceFollowerSource source)
    {
        source.Validate(Source); RequireRead(invocation);
        if (invocation.Ingress != FalloutReferencePositionIngress.PackageCommand)
            throw new InvalidOperationException("Follower Extra read lost its actual Actor package caller.");
        RequireLane(FalloutReferencePositionLane.FollowerExtra);
        var read = new FalloutActorFollowerExtraRead(source.Identity, Actor, Process, Factory, Constructor,
            _sequence, _values.FollowerExtra is not null, _values.FollowerExtra, _values.FollowerPayload, invocation);
        RequireRead(invocation); return read;
    }
}
