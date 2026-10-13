using OpenNV.Runtime.Content;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCellProcessSummary(FalloutCellProcessIdentity Source,
    FalloutCellProcessPhase Phase, long Epoch, bool DataLoaded, int References, string? GraphSha256, string? Failure);
internal sealed record FalloutCellChildCounts(int Total, int Pending, int Published, int SourceDisabled,
    int SourceNoDraw, int Retired, int Failed, long NativeObjects);
internal sealed record FalloutCellChildFailure(FalloutFormKey Reference, FalloutCellProcessChildPhase Phase,
    string? Owner, string? Failure);
internal sealed record FalloutCellConsumerSummary(FalloutFormKey Cell, FalloutFormKey? Landscape,
    FalloutCellProcessChildPhase Phase, int NativeObjects, string Owner);
internal sealed record FalloutCellAttachmentSummary(Guid Identity, Guid Process,
    [property: JsonConverter(typeof(FalloutCellProcessEpochsJson))] IReadOnlyDictionary<FalloutFormKey, long> CellEpochs,
    ulong NativeRoot, bool RootPublished, bool Retired, string? Failure, FalloutCellChildCounts Children,
    IReadOnlyList<FalloutCellChildFailure> ChildFailures, IReadOnlyList<FalloutCellConsumerSummary> CellConsumers);
internal sealed record FalloutCellSharedGraphSummary(Guid Identity, Guid Attachment, Guid Process,
    ulong NativeRoot, FalloutFormKey BeforeActive, FalloutFormKey AfterActive, FalloutCellSharedGraphPhase Phase,
    long Entered, long Changed, string? Failure, FalloutCellChildCounts BeforeChildren,
    int TargetChildren, FalloutCellChildCounts PublishedChildren,
    IReadOnlyList<FalloutCellChildFailure> ChildFailures, int TargetCellSources,
    IReadOnlyList<FalloutCellConsumerSummary> BeforeCellConsumers,
    IReadOnlyList<FalloutCellConsumerSummary> PublishedCellConsumers, int DestroyedReferences, int DestroyedCellConsumers);
internal sealed record FalloutCellColdSummary(Guid PreviousProcess, Guid CurrentProcess, long Sequence,
    IReadOnlyList<Guid> AwaitingNativeAttachments, int PreviousAttachments);
internal sealed record FalloutCellProcessesSummary(string Source, Guid Process, long Sequence, bool Busy,
    IReadOnlyList<FalloutCellProcessSummary> Cells, IReadOnlyList<FalloutCellAttachmentSummary> Attachments,
    int TransitionCount, FalloutCellProcessTransition? LastTransition,
    IReadOnlyList<FalloutCellSharedGraphSummary> SharedGraphs, FalloutCellColdSummary? Cold,
    string? SaveBlocker, string Parity);

internal sealed partial class FalloutCellProcesses
{
    // Read current owners directly: no cold Capture, source reader, validation
    // mutation or detailed State graph is entered by live publication.
    internal FalloutCellProcessesSummary Summary => new(_declaration.Contract, _process, _sequence, _busy,
        _cells.Values.Select(cell => new FalloutCellProcessSummary(cell.Source, cell.Phase, cell.Epoch,
            cell.Data is not null, cell.Data?.References.Count ?? 0, cell.Data?.GraphSha256, cell.Failure)).ToArray(),
        _attachments.Values.Select(attachment => new FalloutCellAttachmentSummary(attachment.Identity,
            attachment.Process, CopyEpochs(attachment.CellEpochs), attachment.NativeRoot,
            attachment.RootPublished, attachment.Retired, attachment.Failure, CountChildren(attachment.Children),
            ChildFailures(attachment.Children), SummarizeConsumers(attachment.CellConsumers))).ToArray(),
        _transitions.Count, _transitions.LastOrDefault(),
        _sharedGraphs.Select(change => new FalloutCellSharedGraphSummary(change.Identity, change.Attachment,
            change.Process, change.NativeRoot, change.BeforeActive, change.AfterActive, change.Phase,
            change.Entered, change.Changed, change.Failure, CountChildren(change.BeforeChildren),
            change.TargetChildren.Count, CountChildren(change.PublishedChildren),
            ChildFailures(change.BeforeChildren).Concat(ChildFailures(change.PublishedChildren)).ToArray(),
            change.TargetCellSources.Count, SummarizeConsumers(change.BeforeCellConsumers),
            SummarizeConsumers(change.PublishedCellConsumers), change.DestroyedReferences.Count,
            change.DestroyedCellConsumers.Count)).ToArray(),
        _cold is null ? null : new(_cold.PreviousProcess, _cold.CurrentProcess, _cold.Sequence,
            _cold.AwaitingNativeAttachments.ToArray(), _cold.PreviousAttachments.Count), SaveBlocker, "unmeasured");

    private static FalloutCellChildCounts CountChildren(IReadOnlyList<FalloutCellProcessChild> children)
    {
        var pending = 0; var published = 0; var disabled = 0; var noDraw = 0; var retired = 0; var failed = 0;
        long native = 0;
        foreach (var child in children)
        {
            native += child.NativeObjects.Count;
            switch (child.Phase)
            {
                case FalloutCellProcessChildPhase.Pending: pending++; break;
                case FalloutCellProcessChildPhase.Published: published++; break;
                case FalloutCellProcessChildPhase.SourceDisabled: disabled++; break;
                case FalloutCellProcessChildPhase.SourceNoDraw: noDraw++; break;
                case FalloutCellProcessChildPhase.Retired: retired++; break;
                case FalloutCellProcessChildPhase.Failed: failed++; break;
                default: throw new InvalidDataException("CELL summary encountered an unknown actual child phase.");
            }
        }
        return new(children.Count, pending, published, disabled, noDraw, retired, failed, native);
    }
    private static FalloutCellChildFailure[] ChildFailures(IReadOnlyList<FalloutCellProcessChild> children) =>
        children.Where(child => child.Failure is not null || child.Phase == FalloutCellProcessChildPhase.Failed)
            .Select(child => new FalloutCellChildFailure(child.Source.Reference, child.Phase, child.Owner, child.Failure)).ToArray();
    private static FalloutCellConsumerSummary[] SummarizeConsumers(IReadOnlyList<FalloutCellNativeConsumers> consumers) =>
        consumers.Select(consumer => new FalloutCellConsumerSummary(consumer.Source.Cell.Cell,
            consumer.Source.Landscape, consumer.Phase, consumer.NativeObjects.Count, consumer.Owner)).ToArray();
}
