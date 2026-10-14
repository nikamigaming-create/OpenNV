using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Build once for a retained aggregate, then validate its current and historical
// borrows without repeatedly scanning the reference call history.
internal sealed class FalloutReferenceFollowerValidation
{
    private readonly FalloutReferencePositionSnapshot _reference;
    private readonly string _source;
    private readonly Dictionary<(Guid Process, Guid Factory, Guid Constructor, long Sequence), Boundary> _boundaries = [];
    private sealed record Boundary(Guid? Extra, Guid? Payload, bool Available);

    internal FalloutReferenceFollowerValidation(FalloutReferencePositionSnapshot reference)
    {
        FalloutSourceReferencePositionState.Validate(reference, reference.Source, reference.Stack, reference.Actor);
        _reference = reference; _source = FalloutReferenceFollowerSource.Reviewed(reference.Source).Identity;
        var grouped = FalloutSourceReferencePositionState.PositionHistory(reference).GroupBy(item => item.Process)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Guid? extra = null, payload = null; var available = false;
        foreach (var publication in reference.Publications)
        {
            Store(publication.PublishedSequence!.Value);
            foreach (var item in grouped.GetValueOrDefault(publication.Process) ?? [])
                foreach (var call in item.Call is { } ordinary ? new[] { ordinary } :
                    item.Frames!.FirstCalls.Concat(item.Frames.LastCalls).DistinctBy(row => row.Changed))
                {
                    extra = call.After.FollowerExtra; payload = call.After.FollowerPayload;
                    if (call.Prefix is { Returned: true }) available = true;
                    if (call.InvalidatedLane == FalloutReferencePositionLane.FollowerExtra) available = false;
                    Store(call.Changed);
                }
            void Store(long changed) => _boundaries.Add((publication.Process, publication.Factory, publication.Constructor, changed),
                new(extra, payload, available));
        }
    }

    internal void ValidateRead(FalloutActorFollowerExtraRead read, FalloutPackageCommandFieldInvocation? command = null)
    {
        if (read.Source != _source || read.Actor != _reference.Actor || read.ReferenceInvocation is not { } invocation ||
            invocation.Actor != read.Actor || invocation.ReferenceProcess != read.Process || invocation.ReferenceConstructor != read.Constructor ||
            invocation.Ingress != FalloutReferencePositionIngress.PackageCommand || invocation.Call == Guid.Empty ||
            invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId < 1 ||
            !_boundaries.TryGetValue((read.Process, read.Factory, read.Constructor, read.Sequence), out var boundary) ||
            !boundary.Available || read.Present != (boundary.Extra is not null) || read.Extra != boundary.Extra || read.Payload != boundary.Payload ||
            command is not null && (invocation.PackageCommand != command.SourceCommand || invocation.CommonProcess != command.Process ||
                invocation.Actor != command.Actor || invocation.Epoch != command.Epoch || invocation.ThreadId != command.ThreadId))
            throw new InvalidDataException("Follower Extra read has no identical available reference membership/publication/caller boundary.");
        invocation.ValidatePackageCaller();
    }
}
