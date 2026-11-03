using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// The physical rig serves both placed actors and the engine-instantiated player.
// Its state adapter never creates a world reference or a second actor.
internal abstract class RuntimeNativeRagdollStateOwner
{
    internal abstract FalloutFormKey Reference { get; }
    internal abstract bool Dead { get; }
    internal abstract bool KnockedDown { get; }
    internal abstract IReadOnlyList<byte> SeveredParts { get; }
    internal abstract FalloutActorRagdollState? Ragdoll { get; set; }
    internal abstract Func<FalloutActorRagdollState>? CaptureRagdoll { get; set; }

    internal sealed class Placed(FalloutReferenceInstance state) : RuntimeNativeRagdollStateOwner
    {
        internal override FalloutFormKey Reference => state.Reference;
        internal override bool Dead => state.Injury?.Dead == true;
        internal override bool KnockedDown => state.KnockedDown;
        internal override IReadOnlyList<byte> SeveredParts => state.Injury?.SeveredParts ?? [];
        internal override FalloutActorRagdollState? Ragdoll { get => state.Ragdoll; set => state.Ragdoll = value; }
        internal override Func<FalloutActorRagdollState>? CaptureRagdoll
        { get => state.CaptureRagdoll; set => state.CaptureRagdoll = value; }
    }
}
