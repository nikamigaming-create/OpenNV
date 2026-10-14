using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferencePositionIngress
{ ControllerConstruction, ConstructorStartup, ConfiguredStartup, ActorPosition, PackageCommand, CellAttachment, ReferenceScale, ReferenceMode, CopyOrLoad, BaseAssignment }
internal sealed record FalloutReferencePositionInvocation(Guid Call, FalloutCombatActorIdentity Actor,
    Guid ReferenceProcess, Guid ReferenceConstructor, Guid CommonProcess, long Epoch, int ThreadId,
    FalloutReferencePositionIngress Ingress, Guid? PackageCommand = null)
{
    internal void ValidatePackageCaller()
    {
        if ((Ingress == FalloutReferencePositionIngress.PackageCommand) != (PackageCommand is not null) || PackageCommand == Guid.Empty)
            throw new InvalidDataException("Reference child lost its original entered package command.");
    }
}
internal enum FalloutReferencePositionLane { Position, PositionMode, Scale, PersistentCell, ActorYawMode, ActorAssignmentByte }
internal enum FalloutReferencePositionStep
{
    Entered, SourcePrefix, ConfiguredCell, ConfiguredX, InitialPlacement, PersistentPredicate, KindPredicate,
    Worldspace, Detached, PositionX, PositionY, PositionZ, ReferenceChange, ControllerReset, ModelTranslation,
    ParentCell, PositionMode, Scale, PersistentCell, Returned, UnownedWriter, ActorYawMode, ActorAssignmentByte
}
internal sealed record FalloutReferencePositionValues(FalloutPackageRawPosition Position, byte PositionMode,
    uint Scale, FalloutFormKey? PersistentCell, bool PersistentCellPresent, uint ActorYawMode = 0, byte ActorAssignmentByte = 0);
internal sealed record FalloutReferencePositionStore(FalloutReferencePositionStep Step, long Changed,
    uint? Bits = null, FalloutFormKey? Form = null, bool? Present = null);
internal sealed record FalloutReferencePositionPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null, long? PublishedSequence = null);
internal sealed record FalloutReferencePositionRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, byte Mode, FalloutPackageRawPosition Position);
internal sealed record FalloutReferenceConstructorPositionRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, FalloutPackageRawPosition Position);
internal sealed record FalloutReferenceParentCellRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, FalloutFormKey? Cell, string ExecutableSha256);
internal sealed record FalloutReferenceScaleRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, uint Bits, bool ConstructorOnly = false);
internal sealed record FalloutReferenceActorYawModeRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, uint Mode);
internal sealed record FalloutReferenceFlagRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid FieldsFactory, Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch, long Sequence, uint Flags);
internal sealed record FalloutReferenceWorldspaceRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, FalloutReferenceParentCellRead Parent,
    FalloutReferenceFlagRead? Flags, bool PersistentLookup, bool PersistentPresent, FalloutFormKey? PersistentCell,
    FalloutFormKey? Worldspace);
internal sealed record FalloutReferencePositionPrefix(string Source, Guid Factory, Guid Constructor,
    FalloutReferencePositionInvocation Invocation, string BaseSha256, string? ConfiguredCell, string? ConfiguredX,
    bool ReferenceRecordPresent, bool Returned, string? Failure,
    IReadOnlyList<FalloutReferencePositionDependency>? Dependencies = null);
internal sealed record FalloutReferencePositionDependency(FalloutFormKey Form, string Signature, uint Flags, string Sha256);
internal sealed record FalloutReferencePositionCall(FalloutReferencePositionInvocation Invocation, string Source,
    Guid Factory, long Entered, long Changed, string Kind, FalloutReferencePositionStep Step,
    FalloutReferencePositionValues Before, FalloutReferencePositionValues After,
    IReadOnlyList<FalloutReferencePositionStore> Stores, bool Returned, string? Failure,
    FalloutReferenceFlagRead? Flags = null, byte? BaseKind = null, bool? Persistent = null,
    FalloutReferenceWorldspaceRead? Worldspace = null, FalloutReferenceChangeCall? Change = null,
    FalloutReferencePositionPrefix? Prefix = null, FalloutReferencePositionLane? InvalidatedLane = null,
    string? WriterBoundary = null);
internal sealed record FalloutReferencePositionSnapshot(string Schema, FalloutReferencePositionSource Source,
    string Stack, FalloutCombatActorIdentity Actor, Guid Process, Guid Factory, Guid Constructor, long Sequence,
    FalloutReferencePositionValues Values, IReadOnlyList<FalloutReferencePositionPublication> Publications,
    IReadOnlyList<FalloutReferencePositionCall> Calls,
    IReadOnlyDictionary<FalloutReferencePositionLane, string> UnownedWriters, bool Retired);

internal interface IFalloutReferencePositionConsumers
{
    Guid Factory { get; }
    void RequireCurrent(FalloutReferencePositionInvocation invocation);
    FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation);
    // The source predicate is independent of BaseForm kind. Its current form-file
    // branch belongs to the real reference/file/DataHandler owner.
    bool ReadPersistentReference(FalloutReferencePositionInvocation invocation, FalloutReferenceFlagRead flags);
    byte ReadBaseFormKind(FalloutReferencePositionInvocation invocation);
    FalloutReferenceParentCellRead ReadCurrentParentCell(FalloutReferencePositionInvocation invocation);
    FalloutFormKey? ReadCellWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey cell);
    void DetachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace);
    void AttachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace);
    FalloutReferenceChangeCall ReferenceChanged(FalloutReferencePositionInvocation invocation, uint mask);
    // This consumes the actual current Common controller and model fields. Its
    // receipt is independent of the returned base setter and keeps failed resources.
    void ResetCurrentControllerAndModel(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition position);
    // Current TES CELL/grid selection and yaw are actual startup children, not
    // ParentCELL, a pending destination, native pose or presentation residency.
    FalloutPackageRawPosition SelectInitialPlacement(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition supplied);
    void SetInitialPlacementYaw(FalloutReferencePositionInvocation invocation);
    void ClearStartupChangeSuppression(FalloutReferencePositionInvocation invocation);
}
