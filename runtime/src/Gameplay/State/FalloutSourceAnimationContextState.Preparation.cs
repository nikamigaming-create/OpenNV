using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSourceAnimationContextState
{
    internal FalloutAnimationPreparationSlotRead ReadPreparationSlot(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequirePreparation(source, frame, requireActual);
        var selected = _selected[FalloutPlayerPostAnimationSource.PreparationGroupSlot];
        RequirePreparation(source, frame, requireActual);
        return new(source.Identity, frame, Identity, Factory, _sequence,
            FalloutPlayerPostAnimationSource.PreparationGroupSlot, selected, Construction);
    }
    internal FalloutAnimationPreparationGroupRead ReadPreparationGroup(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequirePreparation(source, frame, requireActual);
        var group = _currentGroups[FalloutPlayerPostAnimationSource.PreparationGroupSlot];
        RequirePreparation(source, frame, requireActual);
        return new(source.Identity, frame, Identity, Factory, _sequence,
            FalloutPlayerPostAnimationSource.PreparationGroupSlot, group, Construction);
    }
    private void RequirePreparation(FalloutPlayerPostAnimationSource source, FalloutPlayerLateFrameInvocation frame,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireMovementFrame(frame, requireActual);
        if (source.Tail != Source)
            throw new InvalidOperationException("Animation preparation replaced its actual current AnimData source.");
    }
}
