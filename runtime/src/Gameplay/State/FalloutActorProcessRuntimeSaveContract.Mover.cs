using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutActorProcessRuntimeSaveContract
{
    internal static void ValidateMoverAssignmentParents(FalloutActorMoverSnapshot mover, FalloutProcessCommonSnapshot common)
    {
        var parents = new Dictionary<(FalloutPackageCommandFieldInvocation Invocation, Guid Linked, Guid High),
            FalloutActorPackageAssignmentCall>();
        foreach (var fields in common.Current.Concat(common.Retired).Select(entry => entry.ActorAssignment).OfType<FalloutProcessActorAssignmentFields>())
            foreach (var call in fields.Calls)
            {
                if (!call.MoverReturned) continue;
                if (call is not
                    {
                        Returned: true, Failure: null, PackageMutation: { } package, Linked: { Returned: true } linked,
                        HighReset: { Returned: true } high
                    })
                    throw new NotSupportedException("Current save retains an unreturned Actor assignment after its mover reset.");
                var key = (call.Invocation, linked.Identity, high.Identity);
                if (parents.TryGetValue(key, out var previous))
                {
                    if (previous.Factory != call.Factory || previous.Package != call.Package ||
                        previous.Linked!.MoverReceipt != linked.MoverReceipt || previous.HighReset!.MoverReceipt != high.MoverReceipt ||
                        !SamePackageMutation(previous.PackageMutation!, package))
                        throw new InvalidDataException("Saved mover parent identity is shared by conflicting Actor assignments.");
                }
                else parents.Add(key, call);
            }
        foreach (var operation in mover.Operations)
        {
            if (operation.Join is not { } join) continue;
            if (!operation.Returned || operation.Failure is not null ||
                !parents.TryGetValue((operation.Invocation, join.LinkedReference.Identity, join.HighReset.Identity), out var parent) ||
                parent.Invocation.Actor != mover.Actor || !SamePackageMutation(parent.PackageMutation!, join.PackageStore) ||
                parent.Linked!.MoverReceipt != join.LinkedReference || parent.HighReset!.MoverReceipt != join.HighReset)
                throw new InvalidDataException("Saved mover reset lost its actual returned package, linked-reference or High-reset parent.");
        }
    }

    private static bool SamePackageMutation(FalloutPackageStateMutation left, FalloutPackageStateMutation right) =>
        left with { Stores = right.Stores } == right && left.Stores.SequenceEqual(right.Stores);
}
