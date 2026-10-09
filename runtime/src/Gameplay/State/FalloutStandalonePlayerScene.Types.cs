using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandaloneFirstPersonPublication(Guid Factory, Guid Process, string Source,
    string Stack, ulong NativeRoot, string Resource, string ResourceSha256, string Owner);
internal sealed record FalloutStandaloneFirstPersonOwner(Guid Identity, long Stored,
    FalloutStandaloneFirstPersonPublication Publication, long? Released);
internal sealed record FalloutStandaloneSceneColdHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousConstructor, Guid CurrentConstructor, long Changed);
internal enum FalloutStandaloneCameraStep { SceneStore, HoldStore, InputManagerChild, Position, Height, Angles, Returned, Failed }
internal sealed record FalloutStandaloneCameraPosition(uint X, uint Y, uint Z);
internal sealed record FalloutStandaloneCameraAngles(uint First, uint Third);
internal sealed record FalloutStandaloneCameraCall(Guid Invocation, byte Argument, byte BeforeScene, byte BeforeHold,
    long Entered, long Changed, FalloutStandaloneCameraStep Step, string Owner, string? Failure, FalloutStandaloneCameraStep? FailedAt);
internal enum FalloutStandaloneSceneClockStep { Incremented, ArgumentStored, VirtualEntered, Returned, Failed }
internal sealed record FalloutStandaloneSceneClockCall(Guid Main, long MainOrdinal, byte BeforeBracket,
    byte Bracket, uint DeliveredBits, uint? ArgumentBits, long Entered, long Changed,
    FalloutStandaloneSceneClockStep Step, string? Failure);
internal enum FalloutStandaloneScenePreludeStep { ViewRequests, OwnedChildRelease, Countdown, ReferenceScratchStore, ReferenceQuery, Failed }
internal sealed record FalloutStandaloneScenePreludeCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutStandaloneScenePreludeStep Step, FalloutStandaloneScenePreludeStep? FailedAt, string? Failure);
internal sealed record FalloutStandalonePlayerSceneSnapshot(string Schema, FalloutStandalonePlayerSceneSource Source,
    string Stack, Guid CapturedProcess, Guid Constructor, long Sequence, byte SceneMode, byte MainHold,
    byte PlayerView, uint ClockKind, uint Numerator, uint Denominator, byte SceneBracket,
    FalloutStandaloneCameraPosition CameraPosition, FalloutStandaloneCameraAngles CameraAngles,
    FalloutStandaloneFirstPersonOwner? FirstPerson, IReadOnlyList<FalloutStandaloneFirstPersonOwner> FirstPersonHistory,
    FalloutStandaloneSceneClockCall? Clock, FalloutStandaloneCameraCall? Camera,
    FalloutStandaloneSceneColdHandoff? ColdHandoff, string? Boundary, string? Failure, bool Retired,
    byte ViewRequest, byte ViewRequestArgument, byte CountdownEnabled, uint CountdownBits, uint ReferenceScratch,
    FalloutStandaloneScenePreludeCall? Prelude);

// These are individual source children. A supplied delegate is not a completion
// receipt; the source call records its actual returned child and committed prefix.
internal interface IFalloutStandaloneFreeCameraConsumers
{
    string Owner { get; }
    void StoreInputManagerCameraByte(byte value);
    FalloutStandaloneCameraPosition ReadPosition();
    uint ReadScaledCameraHeightBits();
    FalloutStandaloneCameraAngles ReadAngles();
}
