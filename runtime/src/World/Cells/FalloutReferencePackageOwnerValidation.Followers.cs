namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferencePackageOwnerValidation
{
    private void ValidateActorFollowerBorrows()
    {
        FalloutReferenceFollowerValidation? validation = null;
        foreach (var entry in _common.Current.Concat(_common.Retired))
            foreach (var call in entry.ActorAssignment?.Calls ?? [])
                if (call.Followers is { } read)
                {
                    validation ??= new(_position);
                    validation.ValidateRead(read, call.Invocation);
                    RequirePositionBoundary(read.Process, read.Factory, read.Constructor, read.Sequence, call.Invocation);
                    if (!_commands.TryGetValue(call.Invocation.SourceCommand, out var parent) || parent.Invocation != call.Invocation)
                        throw new InvalidDataException("Follower read has no identical actual entered package parent.");
                }
    }
}
