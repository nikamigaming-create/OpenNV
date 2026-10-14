using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPackageCommandFieldInvocation(Guid SourceCommand, FalloutCombatActorIdentity Actor,
    Guid Process, long Epoch, int ThreadId);
internal sealed record FalloutPackageCommandFieldConstruction(string Producer,
    bool PrecedingWritersClosed = false, string? Boundary = null,
    IReadOnlyList<FalloutPackageCommandFieldLane>? ClosedLanes = null);
internal enum FalloutPackageCommandFieldLane
{ Position, RotationZ, PositionMode, ParentCell, ScriptExtra, SavedPosition, Animation }
internal enum FalloutPackageCommandField
{
    PositionX, PositionY, PositionZ, RotationZ, PositionMode, ParentCell,
    ScriptConstruction, ScriptPresence, ScriptForm, ScriptConstructorEventList, ScriptEventList, EventMask,
    SavedConstruction, SavedPresence, SavedSpace, SavedX, SavedY, SavedZ, SavedRotationZ,
    View, AnimationOverride, FirstPersonAnimation
}
internal sealed record FalloutPackageRawPosition(uint X, uint Y, uint Z);
internal sealed record FalloutPackageSavedPosition(Guid Identity, FalloutFormKey? Space,
    FalloutPackageRawPosition Position, uint RotationZ);
internal sealed record FalloutPackageScriptEventRow(Guid Identity, FalloutFormKey? Form, uint Mask);
internal sealed record FalloutPackageScriptExtra(Guid Identity, FalloutFormKey? Script, Guid? EventList,
    Guid? EventFactory, IReadOnlyList<FalloutPackageScriptEventRow?> Rows);
internal sealed record FalloutPackageCommandFieldValues(FalloutPackageRawPosition Position, uint RotationZ,
    byte PositionMode, FalloutFormKey? ParentCell, FalloutPackageScriptExtra? ScriptExtra,
    FalloutPackageSavedPosition? SavedPosition, byte View, Guid? AnimationOverride, Guid? FirstPersonAnimation,
    FalloutPackageScriptExtra? PendingScriptExtra = null, FalloutPackageSavedPosition? PendingSavedPosition = null)
{
    internal static FalloutPackageCommandFieldValues Constructed => new(new(0, 0, 0), 0, 0, null, null, null, 0, null, null);
}
internal sealed record FalloutPackageCommandFieldAvailability(FalloutPackageCommandFieldLane Lane, bool Available, string? Boundary);
internal sealed record FalloutPackageCommandFieldPublication(Guid Process, Guid Factory, Guid Constructor, long Epoch,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPackageCommandFieldStore(FalloutPackageCommandField Field, long Changed,
    uint? Bits = null, FalloutFormKey? Form = null, Guid? Object = null, Guid? Row = null);
internal sealed record FalloutPackageCommandFieldOperation(FalloutPackageCommandFieldInvocation Invocation, string Kind,
    long Entered, long Changed, IReadOnlyList<FalloutPackageCommandFieldStore> Stores, bool Returned, string? Failure,
    FalloutPackageCommandFieldLane? InvalidatedLane = null, string? InvalidationBoundary = null,
    IReadOnlyList<FalloutPackageScriptEventRow?>? PublishedRows = null, Guid? EventFactory = null,
    FalloutPackageCommandEventMark? EventMark = null, FalloutPackageCommandAnimationRead? AnimationRead = null,
    FalloutPackageCommandSavedPositionCall? SavedPositionCall = null,
    FalloutPackageCommandAnimationInitialization? AnimationInitialization = null);
internal sealed record FalloutPackageCommandFieldSnapshot(string Schema, FalloutPackageCommandFieldsSource Source,
    string Stack, FalloutCombatActorIdentity Actor, Guid Process, Guid Factory, Guid Constructor, long Epoch,
    FalloutPackageCommandFieldConstruction Construction, long Sequence, FalloutPackageCommandFieldValues Values,
    IReadOnlyList<FalloutPackageCommandFieldAvailability> Availability,
    IReadOnlyList<FalloutPackageCommandFieldPublication> Publications,
    IReadOnlyList<FalloutPackageCommandFieldOperation> Operations, bool Retired,
    FalloutReferenceScriptExtraInitializerSnapshot? ScriptInitialization = null,
    IReadOnlyList<FalloutScriptExtraInitializationFieldOperation>? ScriptInitializationOperations = null);
internal sealed record FalloutPackageCommandEventMark(FalloutPackageCommandFieldInvocation Invocation,
    FalloutFormKey Package, uint Mask, long Entered, long Changed, bool? Result,
    IReadOnlyList<FalloutPackageCommandFieldStore> Stores, bool Returned, string? Failure);
internal enum FalloutPackageAnimationSelection { Override, FirstPerson, Process }
// Main/scene and Common are independent current process lifetimes. The scene
// getter's own command guard and the aggregate cold owner join these receipts.
internal sealed record FalloutPackageCommandAnimationViewRead(Guid Process, string Stack,
    string ExecutableSha256, Guid Constructor, byte View);
internal sealed record FalloutPackageCommandAnimationInitialization(FalloutPackageCommandFieldInvocation Invocation,
    string Source, Guid FieldsFactory, Guid Constructor, long Entered, long Changed,
    string? ConfiguredCell, string? ConfiguredGridX, Guid? OverrideContext, Guid? FirstPersonContext,
    bool Returned, string? Failure);
internal sealed record FalloutPackageCommandAnimationRead(FalloutPackageCommandFieldInvocation Invocation,
    long Entered, long Changed, FalloutPackageAnimationSelection? Selection, Guid? Context,
    FalloutProcessSceneQuery? Common, bool Returned, string? Failure,
    FalloutPackageCommandAnimationViewRead? ViewRead = null);
internal sealed record FalloutPackageCommandSavedPositionCall(FalloutPackageCommandFieldInvocation Invocation,
    long Entered, long Changed, FalloutPackageRawPosition? Position, uint? RotationZ, FalloutFormKey? ParentCell,
    bool ParentCellReturned, FalloutFormKey? Worldspace, bool WorldspaceReturned, Guid? Consumer,
    FalloutPackageSavedPosition? Before, FalloutPackageSavedPosition? After,
    IReadOnlyList<FalloutPackageCommandFieldStore> Stores, bool ChangeEntered, bool Returned, string? Failure,
    FalloutPackageCommandRawRotationRead? RotationRead = null);
internal sealed record FalloutPackageCommandRawRotationRead(string Source, string Stack, Guid Process,
    Guid Factory, Guid Constructor, FalloutFormKey Reference, FalloutFormKey Base, long Sequence, uint ZBits);

internal interface IFalloutPackageReferenceFieldConsumers
{
    FalloutPackageCommandFieldsSource Source { get; }
    string Stack { get; }
    FalloutCombatActorIdentity Actor { get; }
    Guid Process { get; }
    long Epoch { get; }
    Guid Factory { get; }
    void RequireCurrent(FalloutPackageCommandFieldInvocation invocation);
    FalloutPackageCommandRawRotationRead? ReadRawRotationZ(FalloutPackageCommandFieldInvocation invocation) => null;
    FalloutFormKey? ReadWorldspace(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? parentCell);
    void ReferenceChanged(FalloutPackageCommandFieldInvocation invocation, uint mask);
}
