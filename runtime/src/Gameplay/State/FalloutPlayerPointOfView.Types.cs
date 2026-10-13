using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerPointOfViewStep { FirstPersonField, DesiredViewStore, SharedScalarStore, CameraChild, Returned }
internal sealed record FalloutPlayerPointOfViewCall(Guid Observation, Guid Process, Guid Constructor,
    long Entered, long Changed, FalloutPlayerPointOfViewStep Step, bool GetterReturned,
    FalloutSourceSceneNode? FirstPerson, byte BeforeDesiredView, uint BeforeSharedScalarBits,
    long? DesiredViewStored, long? SharedScalarStored, bool Returned, string? Failure);
internal sealed record FalloutPlayerPointOfViewSnapshot(FalloutPlayerPointOfViewSource Source, string Stack,
    Guid Process, Guid Constructor, byte DesiredView, uint SharedScalarBits, long DesiredViewStores,
    long SharedScalarStores, IReadOnlyList<FalloutPlayerPointOfViewCall> Calls,
    IReadOnlyList<FalloutPlayerPointOfViewLifetime> Lifetimes);
internal sealed record FalloutPlayerPointOfViewLifetime(Guid Process, Guid Constructor, long Published);
