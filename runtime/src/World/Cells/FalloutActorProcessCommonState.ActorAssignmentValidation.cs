using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    private void RepublishActorAssignmentFields()
    {
        foreach (var actor in _current.Keys.ToArray())
        {
            var current = _current[actor];
            if (current.ActorAssignment is not { } fields || current.Phase is FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired) continue;
            var changed = Next(); var old = fields.Publication;
            var publication = new FalloutActorAssignmentPublication(_process, Guid.NewGuid(), changed, old.Process, old.Factory);
            _current[actor] = current with
            {
                ActorAssignment = fields with
                {
                    Publication = publication,
                    Publications = AppendPackageCommandStore(fields.Publications, publication)
                },
                Changed = changed
            };
        }
    }

    private static void ValidateActorAssignmentFields(FalloutProcessCommonEntry entry, string executable, string stack,
        IReadOnlySet<Guid>? commonProcesses)
    {
        if (entry.ActorAssignment is not { } fields) return;
        fields.Source.Validate(); var constructor = fields.Construction;
        if (fields.Source.Command.Fields.Controls.Main.EngineSha256 != executable || constructor.Source != fields.Source.Identity ||
            constructor.Actor != entry.Source || constructor.Epoch != entry.Epoch || !constructor.Actor.EnginePlayer || constructor.Epoch != 1 ||
            !constructor.InitialLowCopy || constructor.Values != FalloutActorAssignmentValues.Constructed ||
            constructor.Factory == Guid.Empty || constructor.Process == Guid.Empty || constructor.Changed < 1 || constructor.Changed > entry.Changed ||
            constructor.CurrentPackageFactory != entry.Packages?.Current.Constructor.Owner || constructor.PrimaryPackageFactory != entry.Packages?.Primary?.Constructor.Owner ||
            fields.Publications is not { Count: > 0 } || fields.Stores is null || fields.Writers is null || fields.UnownedWriters is null ||
            fields.LinkedCalls is null || fields.HighCalls is null || fields.Calls is null)
            throw new InvalidDataException("Actor assignment fields replaced their genuine selected constructor/source/epoch.");
        var publications = new Dictionary<Guid, FalloutActorAssignmentPublication>(); var processes = new HashSet<Guid>();
        FalloutActorAssignmentPublication? previous = null;
        foreach (var publication in fields.Publications)
        {
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || !publications.TryAdd(publication.Factory, publication) ||
                !processes.Add(publication.Process) || commonProcesses?.Contains(publication.Process) != true || publication.Changed > entry.Changed ||
                publication.PreviousProcess != previous?.Process || publication.PreviousFactory != previous?.Factory ||
                (previous is null ? publication.Process != constructor.Process || publication.Factory != constructor.Factory || publication.Changed != constructor.Changed :
                    publication.Changed <= previous.Changed))
                throw new InvalidDataException("Actor assignment omitted or reused an actual Common/cold publication.");
            previous = publication;
        }
        if (previous != fields.Publication) throw new InvalidDataException("Actor assignment current factory differs from its cold lineage.");
        var unknown = new Dictionary<FalloutActorAssignmentLane, string>(); var values = constructor.Values;
        var scalars = FalloutProcessCommonScalars.Constructed; var prior = constructor.Changed; var sequences = new HashSet<long>();
        var writers = fields.Writers.ToDictionary(writer => writer.Changed);
        var stores = fields.Stores.ToDictionary(store => store.Changed);
        foreach (var changed in stores.Keys.Concat(writers.Keys).Order())
        {
            if (!sequences.Add(changed) || changed <= prior || changed > entry.Changed)
                throw new InvalidDataException("Actor assignment omitted or reordered its primitive writer prefix.");
            prior = changed;
            if (writers.TryGetValue(changed, out var writer))
            {
                Require(writer.Invocation, writer.Process, changed, null);
                if (!Enum.IsDefined(writer.Lane) || string.IsNullOrWhiteSpace(writer.Boundary))
                    throw new InvalidDataException("Actor assignment invalidation omitted its actual lane.");
                unknown[writer.Lane] = writer.Boundary; continue;
            }
            var store = stores[changed]; Require(store.Invocation, store.Process, changed, store.Factory);
            if (!Enum.IsDefined(store.Cell)) throw new InvalidDataException("Actor assignment writer has an unknown source field.");
            var lane = ActorAssignmentLane(store.Cell); var known = !unknown.ContainsKey(lane);
            if (store.BeforeKnown != known || store.BeforeBits != (known ? ActorAssignmentBits(values, scalars, store.Cell) : null))
            {
                // Common byte/word history is reconstructed with the existing
                // request-byte writers by ValidatePackageCommandFlags.
                if (lane is not (FalloutActorAssignmentLane.CommonFlags or FalloutActorAssignmentLane.CommonWord))
                    throw new InvalidDataException("Actor field store fabricated its before value or missing-writer availability.");
            }
            if (store.Cell == FalloutActorAssignmentCell.Adjustment)
            {
                if (!known || store.OperandBits is not { } operand || store.BeforeBits is not { } before ||
                    store.AfterBits != BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(before) + BitConverter.UInt32BitsToSingle(operand)))
                    throw new InvalidDataException("Actor adjustment changed its actual Float32 operand/add/store law.");
            }
            else if (store.OperandBits is not null) throw new InvalidDataException("An unrelated Actor store acquired an adjustment operand.");
            else if (store.Cell == FalloutActorAssignmentCell.LinkedReference)
            {
                if (!known || store.BeforeReference != values.LinkedReference || store.BeforeBits is not null || store.AfterBits is not null ||
                    store.BeforeObject is not null || store.AfterObject is not null)
                    throw new InvalidDataException("Linked pointer store omitted its same actual previous reference.");
                store.AfterReference?.Validate();
            }
            else if (lane == FalloutActorAssignmentLane.CommonFlags)
            {
                if (!known || store.BeforeBits is not { } before || before > byte.MaxValue ||
                    store.AfterBits != (byte)(before & ~ActorAssignmentFlagMask(store.Cell)))
                    throw new InvalidDataException("Actor reset changed independent Common flag bits.");
            }
            else
            {
                var expected = store.Cell == FalloutActorAssignmentCell.ResetMode ? FalloutActorPackageAssignmentSource.ResetMode :
                    store.Cell == FalloutActorAssignmentCell.TailIndexWord ? ushort.MaxValue : 0;
                if (store.AfterBits != expected || store.AfterReference is not null || store.AfterObject is not null)
                    throw new InvalidDataException("Actor reset changed an original constant store or pointer width.");
            }
            values = ApplyActorAssignment(values, store);
            if (store.Cell == FalloutActorAssignmentCell.CommonWord) scalars = scalars with { FirstWord = store.AfterBits!.Value };
            if (store.Cell == FalloutActorAssignmentCell.TailFinalByte || lane != FalloutActorAssignmentLane.ResetFields) unknown.Remove(lane);
        }
        if (values != fields.Values || unknown.Count != fields.UnownedWriters.Count ||
            unknown.Any(item => !fields.UnownedWriters.TryGetValue(item.Key, out var boundary) || boundary != item.Value))
            throw new InvalidDataException("Actor assignment current fields differ from constructor/store/writer history.");
        var linkedKeys = new HashSet<(Guid, FalloutActorLinkedOccurrence)>(); var childIds = new HashSet<Guid>();
        var linkedCalls = new Dictionary<Guid, FalloutActorLinkedCall>();
        var highCalls = new Dictionary<Guid, FalloutActorHighResetCall>();
        var retainedStores = fields.Stores.ToHashSet();
        foreach (var linked in fields.LinkedCalls)
        {
            Require(linked.Invocation, linked.Process, linked.Entered, linked.Factory);
            if (linked.Source != fields.Source.Identity || linked.Identity == Guid.Empty || !childIds.Add(linked.Identity) ||
                !linkedKeys.Add((linked.Invocation.SourceCommand, linked.Occurrence)) || !Enum.IsDefined(linked.Occurrence) ||
                linked.Changed < linked.Entered || linked.Changed > entry.Changed || linked.Stores is null ||
                linked.Returned != (linked.Step == FalloutActorLinkedStep.Returned) || linked.Returned != (linked.Failure is null) ||
                linked.Returned && (linked.Desired is not null || !linked.Equal || !linked.NullCleanupReturned || linked.Stores.Count != 0 || linked.Selected is not null))
                throw new InvalidDataException("Linked-null child erased its equality tail, replayed an occurrence or fabricated a return.");
            linkedCalls.Add(linked.Identity, linked);
            foreach (var store in linked.Stores) if (!retainedStores.Contains(store)) throw new InvalidDataException("Linked child borrowed another pointer store.");
        }
        foreach (var high in fields.HighCalls)
        {
            Require(high.Invocation, high.Process, high.Entered, high.Factory);
            var order = new[] { FalloutActorAssignmentCell.ResetMode, FalloutActorAssignmentCell.CommonWord, FalloutActorAssignmentCell.IndependentResetByte,
                FalloutActorAssignmentCell.CommonFlagTwo, FalloutActorAssignmentCell.CommonFlagOne, FalloutActorAssignmentCell.CommonFlagForty }.Concat(ActorHighResetTail.Select(item => item.Cell)).ToArray();
            if (high.Source != fields.Source.Identity || high.Identity == Guid.Empty || !childIds.Add(high.Identity) || high.Changed < high.Entered ||
                high.Changed > entry.Changed || high.Stores is null || high.Stores.Count > order.Length ||
                !high.Stores.Select(store => store.Cell).SequenceEqual(order.Take(high.Stores.Count)) || high.Stores.Any(store => !retainedStores.Contains(store)) ||
                high.Returned != (high.Step == FalloutActorHighResetStep.Returned) || high.Returned != (high.Failure is null) ||
                high.Returned && (high.Stores.Count != order.Length || high.Linked is not { Returned: true, Occurrence: FalloutActorLinkedOccurrence.SharedReset } ||
                    high.Loading is null || !high.InlineListEmpty || high.RequestFlag is not { Requested: false }))
                throw new InvalidDataException("High reset omitted ordered stores or crossed an unreturned Shared child.");
            if (high.Linked is { } linked && !HasLinked(linked)) throw new InvalidDataException("High reset borrowed a foreign linked-null occurrence.");
            if (high.RequestFlag is { } flag && !entry.RequestFlagStores.Contains(flag)) throw new InvalidDataException("High reset fabricated the mask20 writer.");
            highCalls.Add(high.Identity, high);
        }
        var commands = new HashSet<Guid>();
        foreach (var call in fields.Calls)
        {
            if (!commands.Add(call.Invocation.SourceCommand) || call.Source != fields.Source.Identity || !publications.ContainsKey(call.Factory) ||
                call.Entered <= constructor.Changed || call.Changed < call.Entered || call.Changed > entry.Changed || call.ReturnedChildren is null ||
                call.Returned != (call.Step == FalloutActorAssignmentStep.Returned) || call.Returned != (call.Failure is null) ||
                call.ReturnedChildren.Distinct().Count() != call.ReturnedChildren.Count ||
                call.Linked is { } linked && !HasLinked(linked) || call.HighReset is { } high && !HasHigh(high) ||
                call.MoverReturned && (call.PackageMutation is not { Step: FalloutPackageStateStep.Returned } || call.Linked is not { Returned: true } || call.HighReset is not { Returned: true }) ||
                call.Returned && call.InitialProcess is not null && call.Followers is null)
                throw new InvalidDataException("Actor assignment omitted an original command prefix or returned before its real children.");
            if (call.Extra is { } extra && (extra.TargetRead is not { } target || target.Invocation != call.Invocation || target.Package != call.Package ||
                target.RecordSha256 != extra.RecordSha256 || target.Source != FalloutPackageRuntimeTargetFields.Source ||
                target.Factory == Guid.Empty || target.Constructor == Guid.Empty || target.Registry == Guid.Empty || target.Sequence < 0 ||
                target.Object == Guid.Empty || (target.Object is null) != (target.Values is null) || target.Values?.Kind != extra.TargetKind ||
                target.Values is not { Kind: 0 or 3 } && extra.Extra is not null))
                throw new InvalidDataException("Actor Extra derivation omitted its actual returned mutable PACK target.");
            if (call.Followers is { } follower &&
                (follower.Source != FalloutReferenceFollowerSource.Reviewed(fields.Source.Reference).Identity || follower.Actor != call.Invocation.Actor ||
                follower.Process == Guid.Empty || follower.Factory == Guid.Empty || follower.Constructor == Guid.Empty || follower.Sequence < 0 ||
                follower.Extra == Guid.Empty || follower.Payload == Guid.Empty || follower.Present != (follower.Extra is not null) ||
                !follower.Present && follower.Payload is not null || call.Returned && follower.Payload is not null ||
                follower.ReferenceInvocation is not { } reference || reference.ReferenceProcess != follower.Process ||
                reference.ReferenceConstructor != follower.Constructor || reference.CommonProcess != call.Invocation.Process ||
                reference.Actor != call.Invocation.Actor || reference.PackageCommand != call.Invocation.SourceCommand ||
                reference.Ingress != FalloutReferencePositionIngress.PackageCommand || reference.ThreadId != call.Invocation.ThreadId ||
                reference.Epoch != call.Invocation.Epoch))
                throw new InvalidDataException("Actor follower read fabricated current reference membership or replaced its original caller.");
        }
        bool HasLinked(FalloutActorLinkedCall candidate) => linkedCalls.TryGetValue(candidate.Identity, out var retained) &&
            candidate with { Stores = retained.Stores } == retained && candidate.Stores.SequenceEqual(retained.Stores);
        bool HasHigh(FalloutActorHighResetCall candidate) => highCalls.TryGetValue(candidate.Identity, out var retained) &&
            candidate with { Stores = retained.Stores, Linked = retained.Linked } == retained &&
            candidate.Stores.SequenceEqual(retained.Stores) &&
            (candidate.Linked is null ? retained.Linked is null : retained.Linked is not null && HasLinked(candidate.Linked));

        void Require(FalloutPackageCommandFieldInvocation invocation, FalloutProcessSceneQuery process, long changed, Guid? factory)
        {
            if (invocation.SourceCommand == Guid.Empty || invocation.Actor != entry.Source || invocation.Process != process.Process ||
                invocation.Epoch != entry.Epoch || invocation.ThreadId < 1 || commonProcesses?.Contains(invocation.Process) != true ||
                process.Actor != entry.Source || process.Epoch != entry.Epoch || process.Level != entry.Level || process.Stack != stack ||
                process.ExecutableSha256 != executable || process.GameplayOwnership == Guid.Empty || process.Changed >= changed ||
                factory is { } id && (!publications.TryGetValue(id, out var publication) || publication.Process != invocation.Process || publication.Changed >= changed))
                throw new InvalidDataException("Actor assignment receipt lost its actual Common/source/Actor/thread/epoch.");
        }
    }
}
