using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutPackageExtraKind { Form, Reference }
internal sealed record FalloutPackageExtraReference(FalloutFormKey Form, FalloutPackageExtraKind Kind)
{
    internal void Validate()
    {
        if (Form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || string.IsNullOrWhiteSpace(Form.OwnerPlugin) || !Enum.IsDefined(Kind))
            throw new InvalidDataException("Package Extra has no typed original form/reference identity.");
    }
}
internal enum FalloutPackageRuntimeMutationKind { Retain, Release, Procedure, ScriptAssignedFlag, UnownedProcedure, UnownedFlags, UnownedLifetime }
internal sealed record FalloutPackageRuntimeMutation(long Sequence, FalloutPackageCommandFieldInvocation Invocation,
    FalloutFormKey Package, FalloutPackageRuntimeMutationKind Kind, int BeforeCount, int AfterCount,
    int BeforeProcedure, int AfterProcedure, uint BeforeFlags, uint AfterFlags, string Producer);
internal sealed record FalloutPackageRuntimePublication(Guid Factory, Guid? PreviousFactory);
internal sealed record FalloutPackageCountAuthorityObservation(long Sequence, FalloutPackageCommandFieldInvocation Invocation, string Producer);
internal sealed record FalloutPackageRuntimeSnapshot(FalloutFormKey Form, string RecordSha256, sbyte Kind, uint SourceFlags,
    int Procedure, uint Flags, int LifetimeCount, bool ProcedureAvailable, bool FlagsAvailable, bool CountAvailable,
    IReadOnlyList<FalloutPackageRuntimeMutation> History);
internal sealed record FalloutPackageRegistrySnapshot(string Schema, string Source, string Stack, Guid Factory,
    long Sequence, IReadOnlyList<FalloutPackageRuntimeSnapshot> Packages, IReadOnlyList<FalloutPackageRuntimePublication> Publications,
    bool CountAuthorityAvailable, IReadOnlyList<FalloutPackageCountAuthorityObservation> CountAuthorityObservations);
internal sealed record FalloutPackageGameHourRead(FalloutFormKey? Global, uint Bits, string Producer);
internal enum FalloutPackageStateOperation { Set, Clear }
internal enum FalloutPackageStateStep
{
    Entered, Equality, SpecialPlayerPackage, Release, TemporaryPackagePointerClear,
    TemporaryDeletion, PackagePointerClear, ContextDestruction, ContextPointerClear, TimeReset, IndexReset, FlagsReset,
    ExtraReset, Retain, GameHour, TimeStore, PackageStore, ContextFactory, ContextStore, IndexStore, ExtraStore, Returned, Failed
}
internal sealed record FalloutPackageStateStore(FalloutPackageStateStep Step, FalloutProcessPackageWords Before,
    FalloutProcessPackageWords After, long Changed);
internal sealed record FalloutPackageStateMutation(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, Guid Factory, FalloutProcessPackageSlot Slot, FalloutPackageStateOperation Operation,
    FalloutFormKey? RequestedPackage, FalloutPackageExtraReference? DerivedExtra, long Entered, long Changed,
    FalloutPackageStateStep Step, FalloutPackageStateStep? FailedAt, FalloutProcessPackageWords Before,
    FalloutProcessPackageWords After, IReadOnlyList<FalloutPackageStateStore> Stores, FalloutPackageGameHourRead? Hour,
    bool Equal, int? ReleasedCount, int? RetainedCount, string? Failure);

// Each callback is a real command-time child. Defaults are supplied by the
// composition owner, not a fabricated context, clock, form deletion or Player.
internal interface IFalloutPackageStateServices
{
    void RequireCurrent(FalloutPackageCommandFieldInvocation invocation);
    FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation);
    void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected);
    FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation);
    Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package);
    void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context);
    void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package);
    void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra);
}
