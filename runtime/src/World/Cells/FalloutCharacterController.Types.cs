using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCharacterControllerFactory(FalloutCharacterControllerSource Source, string Stack,
    Guid Process, long ProcessEpoch, FalloutDetectionProcessLevel Level, FalloutCombatActorIdentity Actor,
    FalloutActorProcessBodyBinding Body, string Owner);
internal sealed record FalloutCharacterControllerHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousController, Guid CurrentController, long Changed);
internal sealed record FalloutCharacterControllerFieldStore(Guid Main, Guid Request, uint Bits, long Changed, string Owner,
    Guid Process, Guid Controller, long ProcessEpoch);
internal sealed record FalloutCharacterControllerSnapshot(string Schema, FalloutCharacterControllerFactory Factory,
    Guid Identity, uint ScalarBits, long Sequence, FalloutCharacterControllerFieldStore? LastStore,
    string? FailureType, string? Error, IReadOnlyList<FalloutCharacterControllerHandoff> Handoffs);

internal interface IFalloutCharacterControllerBody
{
    FalloutCharacterControllerFactory Factory { get; }
    string Owner { get; }
    // These verify the actual physical body, source model provider and process
    // epoch. A registered callback is never evidence of a living controller.
    void VerifyCurrent();
    FalloutActorProcessFact<uint> ControllerPositionZ();
}

internal sealed record FalloutPlayerControllerScalarReceipt(Guid Main, Guid Request, uint Bits, Guid ControllerOwner,
    Guid SourceProcess, long ProcessEpoch, long Changed, string Owner);
internal interface IFalloutPlayerTransferController
{
    Guid Identity { get; }
    Guid SourceProcess { get; }
    long ProcessEpoch { get; }
    string Owner { get; }
    void StoreScalar(FalloutMainPlayerCellInvocation invocation, Guid request, uint bits);
    uint ReadScalarBits();
}
