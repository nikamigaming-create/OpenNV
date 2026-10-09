using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// The same native source rig can serve a placed actor or the real engine player.
// It does not allocate a proxy reference, health state or actor record.
internal sealed record FalloutNativeRagdollOwner(FalloutFormKey Reference, Func<bool> Dead,
    Func<bool> KnockedDown, Func<FalloutActorRagdollState?> ReadState, Action<FalloutActorRagdollState?> WriteState,
    Func<Func<FalloutActorRagdollState>?> ReadCapture, Action<Func<FalloutActorRagdollState>?> WriteCapture,
    Func<IReadOnlyList<byte>> SeveredParts)
{
    internal static FalloutNativeRagdollOwner Actor(FalloutReferenceInstance state) => new(state.Reference,
        () => state.Injury?.Dead == true, () => state.KnockedDown, () => state.Ragdoll, value => state.Ragdoll = value,
        () => state.CaptureRagdoll, value => state.CaptureRagdoll = value, () => state.Injury?.SeveredParts ?? []);
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Native ragdoll has no real source reference owner.");
        ArgumentNullException.ThrowIfNull(Dead); ArgumentNullException.ThrowIfNull(KnockedDown);
        ArgumentNullException.ThrowIfNull(ReadState); ArgumentNullException.ThrowIfNull(WriteState);
        ArgumentNullException.ThrowIfNull(ReadCapture); ArgumentNullException.ThrowIfNull(WriteCapture);
        ArgumentNullException.ThrowIfNull(SeveredParts);
    }
}
