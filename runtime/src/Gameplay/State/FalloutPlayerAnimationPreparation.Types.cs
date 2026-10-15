using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerAnimationPreparationStep
{
    Life, Activity, View, SelectedAnimation, Controller, SelectedSlot, SelectedGroup,
    FirstAnimation, FirstSlot, FirstGroup, ActionSelection, Returned, Failed
}
internal sealed record FalloutPlayerSelectedAnimationRead(string Source, string Stack, Guid FieldsProcess,
    Guid FieldsFactory, Guid Constructor, long Epoch, long FieldsSequence, FalloutPlayerLateFrameInvocation Frame,
    Guid? Override, Guid? First, bool FirstRead, FalloutPlayerViewRead? View, FalloutPackageAnimationSelection Selection,
    FalloutProcessSceneQuery? Common, Guid? Context);
internal sealed record FalloutAnimationPreparationSlotRead(string Source, FalloutPlayerLateFrameInvocation Frame,
    Guid Context, Guid Factory, long Sequence, int Slot, Guid? Selected, FalloutSourceAnimationContextConstruction Construction);
internal sealed record FalloutAnimationPreparationGroupRead(string Source, FalloutPlayerLateFrameInvocation Frame,
    Guid Context, Guid Factory, long Sequence, int Slot, ushort Group, FalloutSourceAnimationContextConstruction Construction);
internal sealed record FalloutPlayerAnimationPreparationCall(string Source, string Stack,
    FalloutPlayerLateFrameInvocation Frame, Guid Factory, long Ordinal, long Entered, long Changed,
    FalloutPlayerAnimationPreparationStep Step, FalloutPlayerAnimationPreparationStep? FailedAt,
    FalloutPlayerFollowingLifeRead? Life, FalloutProcessPhysicalActivityQuery? Activity, FalloutPlayerViewRead? View,
    FalloutPlayerSelectedAnimationRead? Selected, FalloutPlayerMoverDeltaControllerRead? Controller,
    FalloutAnimationPreparationSlotRead? SelectedSlot, FalloutAnimationPreparationGroupRead? SelectedGroup,
    FalloutPlayerMoverDeltaFirstAnimationRead? First, FalloutAnimationPreparationSlotRead? FirstSlot,
    FalloutAnimationPreparationGroupRead? FirstGroup, bool Returned, string? Failure);
internal sealed record FalloutPlayerAnimationPreparationOwners(FalloutActorProcessRuntimeState Runtime,
    FalloutActorProcessCommonState Common, FalloutActorPackageCommandFields Fields,
    FalloutStandalonePlayerSceneState Scene, Func<Guid, FalloutSourceAnimationContextState> RequireContext);
