using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutReferenceLockState RequirePickLock(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(instance.Base).Signature is not ("DOOR" or "CONT"))
            throw new NotSupportedException("Picking requires an actual DOOR or CONT; terminal hacking is separate.");
        if (!CanActivate(reference)) throw new InvalidOperationException("The locked reference is unavailable.");
        var (state, declaration) = EffectiveLock(reference);
        if (state is not { Locked: true }) throw new InvalidOperationException("The reference is no longer locked.");
        if (((declaration?.Flags ?? 0) & 4) != 0)
            throw new NotSupportedException("Leveled picking needs its encounter-level owner.");
        if (state.FailedPickAttempts is null || state.PickRewardCount is null)
            throw new NotSupportedException("Picking history is unknown for this saved or overriding lock.");
        if (Ownership(reference).Owner is not null)
            throw new NotSupportedException("This pick reaches an ownership/crime effect that is not yet bound.");
        return state;
    }

    internal void FailForceLock(FalloutFormKey reference, FalloutReferenceLockState expected)
    {
        if (RequirePickLock(reference) != expected) throw new InvalidOperationException("The active lock changed.");
        Get(LockOwner(reference)).LockState = expected with
        {
            FailedPickAttempts = unchecked(expected.FailedPickAttempts!.Value + 1)
        };
    }

    internal void CompletePickLock(FalloutFormKey reference, FalloutReferenceLockState expected, Action reward)
    {
        if (RequirePickLock(reference) != expected) throw new InvalidOperationException("The active lock changed.");
        var instance = Get(LockOwner(reference));
        var count = expected.PickRewardCount!.Value;
        if (count == 0)
        {
            try { reward(); }
            catch
            {
                // An external XP notification may throw after publishing XP.
                // Keep that uncertain prefix visible and refuse another award.
                instance.LockState = expected with { PickRewardCount = null };
                throw;
            }
            ++count;
        }
        instance.LockState = expected with { Locked = false, FailedPickAttempts = 0, PickRewardCount = count };
        instance.Unlocked = true;
    }
}
