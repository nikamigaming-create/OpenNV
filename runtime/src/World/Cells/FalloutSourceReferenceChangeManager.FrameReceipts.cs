using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferenceChangeManager
{
    private readonly FalloutReferenceFrameJournal<FalloutReferenceChangeCall> _frameReceipts = new(FalloutReferenceFrameReceipt.MaximumChangeCalls);
    private readonly Action? _retainFrameThreadRead;
    private string? _failure;

    private void PrepareFrameChange(FalloutReferencePositionInvocation invocation, string kind, uint mask, bool force)
    {
        if (invocation.Frame is { } frame && (kind != "reference-change" || mask != 2 || force || frame.Operation == FalloutReferenceFrameOperation.Borrow))
            throw new InvalidOperationException("Frame retention cannot replace a source command/change-manager writer.");
        if (invocation.Frame is not null && _retainFrameThreadRead is null)
            throw new NotSupportedException("source-frame-change-actual-thread-read-retention-binding-unbound");
        _frameReceipts.Prepare(invocation);
    }
    private void RetainChangeCall(FalloutReferenceChangeCall call)
    {
        if (call.Invocation.Frame is { } frame && call.Returned && IsFrameChange(call))
            _frameReceipts.Append(call, frame, call.Entered, call.Changed, call.Stores.Count);
        else
        {
            _frameReceipts.Seal(); _calls.Add(call);
            if (call.Invocation.Frame is not null) _retainFrameThreadRead!();
        }
    }
    private static bool IsFrameChange(FalloutReferenceChangeCall call) => call.Kind == "reference-change" &&
        call.Mask == 2 && !call.Force && call.ManagerBefore == call.ManagerAfter && call.UnownedWriter is null &&
        call.ThreadFlags is not null && call.Suppressed is not null &&
        (call.Suppressed == true ? call.Stores.Count == 0 && call.Before is null && call.After is null :
            call.Before is { } before && call.After is { } after && before.Identity == after.Identity &&
            before.SourceFormId == after.SourceFormId && before.Reference == after.Reference && before.SerializedData == after.SerializedData &&
            (before.SerializedData is null ? call.Stores is [{ Step: FalloutReferenceChangeStep.MaskStore, Bits: { } bits } store] &&
                store.Entry == before.Identity && store.Data is null && bits == (before.Flags | 2) && after.Flags == bits :
                call.Stores.Count == 0 && before == after));

    internal static bool SameFrameChange(FalloutReferenceChangeCall left, FalloutReferenceChangeCall right)
    {
        var empty = Array.Empty<FalloutReferenceChangeStore>();
        return (left with { Stores = empty }) == (right with { Stores = empty }) && left.Stores.SequenceEqual(right.Stores);
    }
    private static FalloutReferenceFrameRun<FalloutReferenceChangeCall> CloneChangeFrames(
        FalloutReferenceFrameRun<FalloutReferenceChangeCall> run) => run with
        {
            FirstCalls = Array.AsReadOnly(run.FirstCalls.Select(CloneCall).ToArray()),
            LastCalls = Array.AsReadOnly(run.LastCalls.Select(CloneCall).ToArray()),
            EffectCall = run.EffectCall is { } call ? CloneCall(call) : null
        };
    internal static IEnumerable<FalloutReferenceHistoryEvent<FalloutReferenceChangeCall>> ChangeHistory(
        FalloutReferenceChangeSnapshot saved) => FalloutReferenceFrameJournal<FalloutReferenceChangeCall>.Merge(
            saved.Calls, saved.FrameRuns, call => call.Invocation.ReferenceProcess, call => call.Entered, call => call.Changed);
    internal static IEnumerable<FalloutReferenceChangeCall> ChangeEndpoints(FalloutReferenceChangeSnapshot saved) =>
        ChangeHistory(saved).SelectMany(item => item.Call is { } call ? [call] : item.Frames!.FirstCalls.Concat(item.Frames.LastCalls)
            .Concat(item.Frames.EffectCall is { } effect ? [effect] : []).DistinctBy(row => (row.Factory, row.Changed)));

    private static void ValidateChangeFrames(FalloutReferenceFrameRun<FalloutReferenceChangeCall> run,
        FalloutReferenceChangeSnapshot saved, IReadOnlyDictionary<Guid, (Guid Factory, int Ordinal)> publications,
        IDictionary<Guid, int> threads, IDictionary<uint, FalloutReferenceChangeEntry> entries,
        ref int lastPublication, ref long sequence, uint manager, string? boundary)
    {
        FalloutReferenceFrameJournal<FalloutReferenceChangeCall>.ValidateShape(run, FalloutReferenceFrameReceipt.MaximumChangeCalls, call => call.Invocation,
            call => call.Entered, call => call.Changed, call => call.Stores.Count, SameFrameChange);
        if (run.Entered != sequence + 1 || run.Stores > run.Calls || boundary is not null ||
            !publications.TryGetValue(run.FirstFrame.Parent.Frame.PlayerProcess, out var publication) || publication.Ordinal < lastPublication)
            throw new InvalidDataException("Frame mask history crossed an invalidated manager or another publication.");
        var rows = run.FirstCalls.Concat(run.LastCalls).Concat(run.EffectCall is { } effect ? [effect] : []);
        foreach (var call in rows.DistinctBy(row => row.Changed))
        {
            var inv = call.Invocation; inv.ValidatePackageCaller();
            if (call.Source != saved.Source.Identity || call.Factory != publication.Factory || !call.Returned || call.Failure is not null ||
                call.Step != FalloutReferenceChangeStep.Returned || !IsFrameChange(call) || call.ManagerBefore != manager ||
                !FalloutReferenceFrameJournal<FalloutReferenceChangeCall>.SameOwner(inv.Frame!, run.FirstFrame) ||
                threads.TryGetValue(inv.ReferenceProcess, out var thread) && thread != inv.ThreadId)
                throw new InvalidDataException("Frame change endpoint altered its original map effect, caller or current flags.");
            var flags = call.ThreadFlags!;
            if (flags.Source != saved.Source.Identity || flags.Process != inv.ReferenceProcess || flags.Factory == Guid.Empty ||
                flags.ConsumerFactory == Guid.Empty || flags.Invocation != inv.Call || flags.Thread != inv.ThreadId || flags.Sequence < 1 || flags.Initialized != 1)
                throw new InvalidDataException("Frame change endpoint substituted the actual current-thread getter.");
            var early = ((flags.Flags | manager) & 1) != 0;
            if (!early)
            {
                var field = call.ReferenceFlags ?? throw new InvalidDataException("Frame mask child omitted its reached reference flags.");
                if (field.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(saved.Source.Fields).Identity || field.Stack != saved.Stack ||
                    field.Actor != inv.Actor || field.Process != inv.CommonProcess || field.Constructor != inv.ReferenceConstructor ||
                    field.Epoch != inv.Epoch || field.Factory == Guid.Empty || field.FieldsFactory == Guid.Empty || field.Sequence < 0)
                    throw new InvalidDataException("Frame change endpoint borrowed another reference/Common flag owner.");
            }
            var excluded = call.ReferenceFlags is { } reference && (reference.Flags & 0x4000) != 0;
            var loading = call.ReferenceFlags is { } loaded && (loaded.Flags & 0x20) != 0 && call.DataHandlerSuppressed == true;
            if (call.Suppressed != (early || excluded || loading) || early && call.ReferenceFlags is not null ||
                !early && !excluded && call.ReferenceFlags is { } dynamic && (dynamic.Flags & 0x20) != 0 && call.DataHandlerSuppressed is null)
                throw new InvalidDataException("Frame change endpoint invented a suppressed return.");
            if (call.Before is { } before)
            {
                if (!entries.TryGetValue(before.SourceFormId, out var current) || current.Identity != before.Identity ||
                    current.Reference != inv.Actor.Reference || current.Reference != before.Reference || current.SerializedData != before.SerializedData ||
                    before.Flags != current.Flags && before.Flags != (current.Flags | 2))
                    throw new InvalidDataException("Frame OR child replaced its actual current entry or retained data pointer.");
            }
            threads.TryAdd(inv.ReferenceProcess, inv.ThreadId);
        }
        if (run.EffectCall is { } actualEffect)
        {
            var before = actualEffect.Before!; var current = entries[before.SourceFormId];
            if (actualEffect.After!.Flags != (current.Flags | 2))
                throw new InvalidDataException("Frame mask interval changed an unrelated flag bit.");
            entries[before.SourceFormId] = current with { Flags = current.Flags | 2 };
        }
        sequence = run.Changed; lastPublication = publication.Ordinal;
    }
}
