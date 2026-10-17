using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutStandaloneReferenceOwnersSnapshot(
    FalloutEarlyControllerFactorySnapshot EarlyController,
    FalloutCharacterCollisionGroupsSnapshot CollisionGroups,
    IReadOnlyList<FalloutSourceThreadKindScope> PhysicsScopes,
    FalloutReferencePositionSnapshot Position,
    FalloutReferenceChangeSnapshot Changes,
    FalloutSourceThreadReferenceFlagsSnapshot ThreadReferenceFlags,
    string? HelperFailure,
    FalloutReferenceBaseAssignmentCall? BaseAssignment,
    byte? ActorResponse,
    FalloutReferenceFormFileSnapshot FormFiles,
    FalloutInitialPlacementCellSnapshot InitialCells,
    FalloutReferenceParentCellSnapshot? Parent = null,
    FalloutDataHandlerLoadingSnapshot? DataHandlerLoading = null,
    FalloutReferenceModelDataSnapshot? ModelData = null,
    FalloutPlayerMoverSpeedCacheSnapshot? MoverSpeed = null,
    FalloutActorRagdollControllerFieldSnapshot? RagdollControllerField = null);
