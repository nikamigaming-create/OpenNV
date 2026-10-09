using OpenNV.Runtime.Content;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCellProcessIdentity(FalloutFormKey Cell, uint Flags, string Sha256,
    FalloutFormKey? Worldspace, string? WorldspaceSha256);
internal sealed record FalloutCellProcessReference(FalloutFormKey Reference, FalloutFormKey SourceCell,
    string Signature, uint Flags, string Sha256, FalloutFormKey Base, string? BaseSha256);
internal sealed record FalloutCellProcessData(FalloutCellProcessIdentity Source,
    IReadOnlyList<FalloutCellProcessReference> References, string GraphSha256);
internal enum FalloutCellProcessOperation
{
    Construct, BeginLoad, CompleteLoad, BeginAttach, CompleteAttach,
    BeginDetach, CompleteDetach, BeginRelease, CompleteRelease, AttachmentRebind
}
internal sealed record FalloutCellProcessTransition(long Sequence, FalloutFormKey Cell,
    FalloutCellProcessOperation Operation, FalloutCellProcessPhase Before, FalloutCellProcessPhase After,
    long CellEpoch, Guid? Attachment, string Owner);
internal sealed record FalloutCellProcessEntry(FalloutCellProcessIdentity Source, FalloutCellProcessPhase Phase,
    long Epoch, FalloutCellProcessData? Data, string? Failure);
internal enum FalloutCellProcessChildPhase { Pending, Published, SourceDisabled, SourceNoDraw, Retired, Failed }
internal sealed record FalloutCellProcessPlacedChild(FalloutCellProcessReference Source, FalloutReferencePlacement Placement);
internal sealed record FalloutCellProcessChild(FalloutCellProcessReference Source,
    FalloutReferencePlacement Placement, FalloutCellProcessChildPhase Phase,
    IReadOnlyList<ulong> NativeObjects, string? Owner, string? Failure);
internal sealed record FalloutCellProcessAttachment(Guid Identity, Guid Process,
    [property: JsonConverter(typeof(FalloutCellProcessEpochsJson))] IReadOnlyDictionary<FalloutFormKey, long> CellEpochs, ulong NativeRoot,
    IReadOnlyList<FalloutCellProcessChild> Children, IReadOnlyList<FalloutCellNativeConsumers> CellConsumers,
    bool RootPublished, bool Retired, string? Failure);
internal sealed record FalloutCellProcessHandoff(Guid PreviousProcess, Guid CurrentProcess, long Sequence,
    IReadOnlyList<Guid> AwaitingNativeAttachments, IReadOnlyList<FalloutCellProcessAttachment> PreviousAttachments);
internal sealed record FalloutCellProcessesSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutCellProcessEntry> Cells,
    IReadOnlyList<FalloutCellProcessTransition> Transitions, IReadOnlyList<FalloutCellProcessAttachment> Attachments,
    FalloutCellProcessHandoff? ColdHandoff, IReadOnlyList<FalloutCellSharedGraphChange> SharedGraphs);
