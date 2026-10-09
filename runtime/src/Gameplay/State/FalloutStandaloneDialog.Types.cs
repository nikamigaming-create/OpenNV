using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutStandaloneDialogStep
{
    Constructed, RootStored, MenuIdentityStored, PushCommitted, SceneByteEntered, SelectionCleared,
    ControlsRequired, SpeakerStored, ActorPreparationEntered, RootVisibleStored, TransitionEntered,
    OpeningClockEntered, ShowReturned, CloseEntered, Retired,
}
internal sealed record FalloutStandaloneDialogTile(Guid Identity, int CallbackOrdinal, uint Id, string Path, string Kind);
internal sealed record FalloutStandaloneDialogState(Guid Object, Guid Process, FalloutStandaloneDialogDeclaration Declaration,
    long Constructed, Guid? Root, uint MenuIdentity, uint Lifecycle, ushort Flags,
    IReadOnlyList<Guid?> Controls, IReadOnlyList<FalloutStandaloneDialogTile> ReturnedTiles,
    FalloutFormKey? Speaker, FalloutStandaloneDialogStep Step, long Changed, long? Retired,
    uint? RootVisibleBits, string? Boundary, string? Failure);
internal sealed record FalloutStandaloneMenuStackWrite(Guid Dialog, long Changed, IReadOnlyList<uint> Before,
    IReadOnlyList<uint> After, uint BeforeMode, uint AfterMode, int Slot, bool Returned,
    bool RequiresSceneByte, string? Failure);
internal sealed record FalloutStandaloneMenuTransition(Guid Identity, Guid Menu, long Requested,
    uint ElapsedBits, uint DurationBits, long Changed);
internal sealed record FalloutStandaloneMenuTransitionSample(Guid Identity, string Owner, uint Kind,
    uint DeltaBits, uint? DivisorBits);
internal sealed record FalloutStandaloneMenusSnapshot(IReadOnlyList<FalloutStandaloneDialogState> Dialogs,
    IReadOnlyList<FalloutStandaloneMenuStackWrite> StackWrites, IReadOnlyList<FalloutStandaloneMenuTransition> Transitions,
    Guid? SelectedMenu, Guid? SelectedTile);

// Callback products have typed real owners. They cannot be replaced by a
// Godot pause/menu Boolean, a registered callback, or an empty successful arm.
internal interface IFalloutStandaloneDialogChildren
{
    string Owner { get; }
    string Source { get; }
    void StoreMenuSceneByte(FalloutStandaloneDialogInvocation invocation, byte value);
    void StoreTileSelectionLocus(FalloutStandaloneDialogInvocation invocation, Guid tile, uint valueBits);
    void PrepareActors(FalloutStandaloneDialogInvocation invocation, FalloutFormKey speaker);
}
internal sealed class FalloutStandaloneDialogInvocation
{
    internal FalloutStandaloneInterfaceState Owner { get; }
    internal Guid Dialog { get; }
    internal Guid Identity { get; } = Guid.NewGuid();
    internal Guid Process { get; }
    internal FalloutStandaloneDialogInvocation(FalloutStandaloneInterfaceState owner, Guid dialog)
    { Owner = owner; Dialog = dialog; Process = owner.Process; }
    internal void Require(FalloutStandaloneDialogStep step) => Owner.RequireDialogChild(this, step);
}
