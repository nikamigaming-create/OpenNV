using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutActorMoverField
{
    InheritedVectorXY, InheritedVectorZ, SecondaryVectorXY, SecondaryVectorZ,
    InheritedFlags, ResetWord, ResetByte, PlayerVectorXY, PlayerVectorZ,
    PlayerFlags, PlayerTimer, PlayerSecondaryFlags
}
internal enum FalloutActorMoverLane { DefaultVector, InheritedVector, InheritedFlags, ResetByte, PlayerFields }
internal sealed record FalloutActorMoverVector(uint X, uint Y, uint Z)
{
    internal static FalloutActorMoverVector Default => new(0, 0, 0);
}
internal sealed record FalloutActorMoverValues(FalloutActorMoverVector InheritedVector,
    FalloutActorMoverVector SecondaryVector, uint InheritedFlags, byte ResetByte, FalloutPlayerMoverFields Player);
internal sealed record FalloutActorMoverStore(FalloutActorMoverField Field, long Changed, uint Bits, uint? SecondBits = null);
internal sealed record FalloutActorMoverAvailability(FalloutActorMoverLane Lane, bool Available, string? Boundary);
internal sealed record FalloutActorMoverPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal enum FalloutActorMoverResetChild { LinkedReference, HighReset }
internal sealed record FalloutActorMoverResetChildReceipt(FalloutActorMoverResetChild Child, Guid Identity,
    FalloutPackageCommandFieldInvocation Invocation, FalloutProcessSceneQuery Process, long Entered, long Changed,
    bool Returned, string? Failure);
internal sealed record FalloutActorMoverResetJoin(FalloutPackageStateMutation PackageStore,
    FalloutActorMoverResetChildReceipt LinkedReference, FalloutActorMoverResetChildReceipt HighReset);
internal sealed record FalloutActorMoverOperation(FalloutPackageCommandFieldInvocation Invocation,
    Guid Process, Guid Factory, string Kind, long Entered, long Changed,
    IReadOnlyList<FalloutActorMoverStore> Stores, bool Returned, string? Failure,
    FalloutActorMoverResetJoin? Join = null, FalloutActorMoverLane? InvalidatedLane = null,
    string? InvalidationBoundary = null);
internal sealed record FalloutActorMoverSnapshot(string Schema, FalloutActorMoverSource Source, string Stack,
    Guid Process, Guid Factory, Guid Constructor, long Sequence, FalloutActorMoverValues Values,
    IReadOnlyList<FalloutActorMoverStore> ConstructorStores,
    IReadOnlyList<FalloutActorMoverAvailability> Availability,
    IReadOnlyList<FalloutActorMoverPublication> Publications,
    IReadOnlyList<FalloutActorMoverOperation> Operations, bool Retired,
    FalloutCombatActorIdentity? Actor = null);
