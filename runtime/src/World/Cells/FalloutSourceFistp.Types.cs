using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutSourceFistpSite { PlayerContainment, PlayerTargetCell, PlayerPendingWorldspace }
internal sealed record FalloutSourceFistpContext(Guid Process, Guid MainInvocation, long MainOrdinal,
    FalloutMainPlayerCellStep Child, FalloutSourceFistpSite Site, Guid? PendingRequest);
internal sealed record FalloutSourceFistpOperand(uint Bits, FalloutFistpObservation? Native,
    int? Integer, int? Grid, long Entered, long? Returned, string? Failure);
internal sealed record FalloutSourceFistpPair(FalloutSourceFistpContext Context, FalloutFistpHostBinding? Host,
    long Entered, long Changed, IReadOnlyList<FalloutSourceFistpOperand> Operands, bool Returned, string? Failure);
internal sealed record FalloutModelKeyFistpCall(Guid Process, FalloutReferenceModelDataInvocation Load, uint Input,
    FalloutFistpHostBinding? Host, long Entered, long Changed, FalloutFistpObservation? Native, int? Integer, bool Returned, string? Failure);
internal sealed record FalloutSourceFistpSnapshot(string Schema, string Contract, string ExecutableSha256,
    string Stack, Guid CapturedProcess, long Sequence, long Pairs, long Conversions,
    FalloutFistpHostBinding? LastBinding, bool Bound, FalloutSourceFistpPair? Last,
    FalloutActorProcessRuntimeHandoff? ColdHandoff, long ModelKeys = 0, FalloutModelKeyFistpCall? LastModelKey = null, long ModelKeyConversions = 0);
