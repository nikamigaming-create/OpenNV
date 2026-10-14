using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal enum FalloutEarlyControllerConstructionPhase
{
    Entered, GroupAllocated, Descriptor, StateContext, Contact, ThreadScope, Shapes,
    PhantomAllocated, PhantomInitialized, ProxyAllocated, ProxyRetained, ContactListener,
    Controller, RestoredThread, Returned
}
internal sealed record FalloutEarlyControllerInvocation(Guid Initialization, FalloutCombatActorIdentity Actor,
    string Stack, Guid CommonProcess, Guid RuntimeProcess, Guid ThreadProcess, long Epoch,
    FalloutDetectionProcessLevel Level, Guid Constructor, int ThreadId);
internal sealed record FalloutEarlyControllerColdInvocation(Guid Binding, FalloutEarlyControllerInvocation Current);
internal sealed record FalloutEarlyControllerColdBinding(FalloutEarlyControllerColdInvocation Invocation,
    Guid PreviousFactory, Guid PreviousController, Guid PreviousCommonProcess, Guid PreviousRuntimeProcess,
    Guid PreviousThreadProcess, Guid PreviousThreadFactory, Guid PreviousManager, Guid PreviousPhantom, Guid PreviousProxy,
    ulong PreviousNativeArea, IReadOnlyList<ulong> PreviousNativeShapes, FalloutEarlyControllerLineage Current,
    Guid? Controller, Guid? Manager, Guid? Phantom, Guid? Proxy, ulong? NativeArea, IReadOnlyList<ulong> NativeShapes,
    ushort GroupCounterBefore, ushort GroupCounterAfter, int OriginalCallsBefore, int OriginalCallsAfter,
    int OriginalScopesBefore, int OriginalScopesAfter, bool Returned, string? Failure);
internal sealed record FalloutEarlyControllerLineage(string Source, string Stack, Guid Factory,
    Guid CommonProcess, Guid RuntimeProcess, Guid ThreadProcess, Guid ThreadFactory,
    FalloutCombatActorIdentity Actor, long Epoch, FalloutDetectionProcessLevel Level, Guid Constructor, int Thread);
internal sealed record FalloutEarlyControllerResetInvocation(Guid Call, FalloutCombatActorIdentity Actor,
    Guid CommonProcess, Guid RuntimeProcess, long Epoch, Guid Constructor, int ThreadId);
internal sealed record FalloutEarlyControllerModelRead(FalloutEarlyControllerInvocation Invocation,
    FalloutPlayerThreadModelCacheRead Cache, FalloutSourceSceneNode? Model);
internal sealed record FalloutEarlyControllerDescriptorSnapshot(FalloutPlayerControllerConstructionInputs Player,
    uint AllocatedFilter, uint ShapeScaleBits, uint DynamicFrictionBits, uint StaticFrictionBits,
    uint ContactToleranceBits, uint KeepDistanceBits, uint KeepContactDistanceBits, int ContactIterations,
    uint StrengthBits, uint MaximumBoundBits, uint MaximumSlopeBits, uint SpeedFactorBits, int UserPlanes,
    FalloutPhysicalVector4 Up, byte State, byte ShapeSlot, bool AuxiliaryA, bool AuxiliaryB,
    string? RequestedWorld, FalloutEarlyControllerModelRead Model);
internal sealed record FalloutControllerContactSnapshot(FalloutPhysicalVector4 Up, uint SlopeBits,
    uint AngleBits, uint CosineBits, uint SineBits, uint StepBits, uint CoefficientBits,
    int ContactCount, byte Flags, string NumericalLane);
internal sealed record FalloutCharacterStateSnapshot(byte Kind, bool? Enabled, uint? ScalarBits);
internal sealed record FalloutCharacterStateManagerSnapshot(Guid Factory,
    IReadOnlyList<FalloutCharacterStateSnapshot?> Slots, IReadOnlyList<byte> RegistrationOrder, int ActiveContexts);
internal sealed record FalloutCharacterStateContextSnapshot(Guid Manager, byte Current, byte Previous,
    bool Enabled, uint ScalarABits, uint ScalarBBits, uint MultiplierBits);
internal sealed record FalloutControllerArraySnapshot(string Role, uint CapacityFlags, int Count, int RetainedCapacity);
internal sealed record FalloutControllerPropertySnapshot(uint Key, string Kind, Guid? Proxy,
    FalloutCombatActorIdentity? Actor, string? ActorOwnerType);
internal sealed record FalloutEarlyControllerPhantomSnapshot(Guid Identity, ulong NativeId, uint Filter,
    FalloutCharacterControllerProxyTransform Transform, uint? TranslationW, string? RequestedWorld,
    string? CurrentWorld, IReadOnlyList<FalloutControllerPropertySnapshot> Properties,
    IReadOnlyList<ulong> ShapeNativeIds, bool Retired);
internal sealed record FalloutEarlyControllerProxySnapshot(Guid Identity, Guid Phantom,
    FalloutPhysicalVector4 Up, FalloutPhysicalVector4 Velocity, uint MaximumSlopeCosineBits,
    IReadOnlyList<FalloutControllerArraySnapshot> Arrays, int ContactListeners, bool Retired);
internal sealed record FalloutEarlyControllerStore(long Sequence, string Field, uint? Bits,
    FalloutPhysicalVector3? Vector, uint? FourthWord, int? Count);
internal sealed record FalloutEarlyControllerResetCall(FalloutEarlyControllerResetInvocation Invocation,
    FalloutPhysicalVector3 RawXYZ, long Entered, long Exited, bool Returned, string? Failure);
internal sealed record FalloutEarlyControllerSnapshot(string Schema, FalloutEarlyControllerLineage Lineage,
    Guid Identity, FalloutEarlyControllerDescriptorSnapshot Descriptor,
    FalloutCharacterStateContextSnapshot State, FalloutControllerContactSnapshot Contact,
    FalloutEarlyControllerPhantomSnapshot Phantom, FalloutEarlyControllerProxySnapshot Proxy,
    uint ResetScalarBits, uint Flags, uint PlayerPropertySelector, uint? ActorScaleBits,
    IReadOnlyList<FalloutEarlyControllerStore> Stores, IReadOnlyList<FalloutEarlyControllerResetCall> Resets,
    string? Failure, bool Retired, string CanonicalLane);
internal sealed record FalloutEarlyControllerConstructionCall(FalloutEarlyControllerInvocation Invocation,
    FalloutEarlyControllerLineage Lineage, FalloutEarlyControllerDescriptorSnapshot? Descriptor,
    FalloutSourceThreadKindScope? ThreadScope, ulong? PhantomNativeId, Guid? Controller,
    bool Returned, string? Failure, FalloutEarlyControllerConstructionPhase ReachedPhase);
internal sealed record FalloutEarlyControllerSharedFieldsSnapshot(FalloutPhysicalVector3 StandardCenter, uint SlopeBits,
    byte HelperFlag, bool CenterAvailable, bool SlopeAvailable, bool HelperFlagAvailable);
internal sealed record FalloutEarlyControllerFailedPrefix(FalloutCharacterStateContextSnapshot? State,
    FalloutControllerContactSnapshot? Contact, FalloutEarlyControllerPhantomSnapshot? Phantom,
    FalloutEarlyControllerProxySnapshot? Proxy);
internal sealed record FalloutEarlyControllerFactorySnapshot(string Source, Guid Factory, FalloutCharacterStateManagerSnapshot? Manager,
    IReadOnlyList<FalloutEarlyControllerConstructionCall> Calls, IReadOnlyList<FalloutEarlyControllerSnapshot> Controllers,
    IReadOnlyList<FalloutEarlyControllerFailedPrefix> FailedPrefixes,
    FalloutEarlyControllerSharedFieldsSnapshot Shared, string? Failure, bool Retired,
    FalloutCharacterCollisionGroupsSnapshot Groups, IReadOnlyList<FalloutSourceThreadKindScope> PhysicsScopes,
    IReadOnlyList<FalloutEarlyControllerShapeCall> ShapeCalls, IReadOnlyList<FalloutEarlyControllerColdShapeCall> ColdShapeCalls,
    IReadOnlyList<FalloutEarlyControllerColdBinding> ColdBindings, IReadOnlyList<Guid> PendingColdControllers);

// Each real lease holds the same actual controller and its native resource
// lifetime. Common's setter owns its field lease independently of the caller's
// supplied temporary; equal-identity stores release that temporary too.
internal interface IFalloutEarlyControllerLease : IDisposable
{
    FalloutEarlyController Controller { get; }
    IFalloutEarlyControllerLease Retain();
    void RequireCurrent();
}
