using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedDataHandlerLoadingPreservation(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerMovementControlSource controls)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Loading preservation audit needs actual owned records.");
        var assignment = FalloutExecutableStringTable.ReadActorPackageAssignment(owned.FalloutExecutablePath,
            FalloutPackageCommandFieldsSource.Reviewed(controls));
        var footprint = FalloutExecutableStringTable.ReadDataHandlerLoadingPreservation(owned.FalloutExecutablePath, assignment);
        var process = common.ReadSceneQueries(records.RuntimeFormKey(0x14), 1);
        FalloutDataHandlerLoadingInvocation? entered = null;
        var state = New(null);
        var proof = state.PreserveIndexedRecordPrefix(footprint, records, records.LoadReceipt, ActualInput);
        var saved = state.Capture();
        Require(proof.Before == 0 && proof.After == 0 && Read().Value == 0 && saved.Writers.Count == 0,
            "Owned loading proof fabricated a store instead of preserving the actual constructed byte.");
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            state = New(saved); state.BindCurrentIndexedRecords(records, records.LoadReceipt, ActualInput);
            var current = state.Capture();
            Require(current.Sequence == saved.Sequence && current.Writers.Count == saved.Writers.Count &&
                current.RecordPrefixes!.SequenceEqual(saved.RecordPrefixes!) &&
                current.RecordPreservations!.SequenceEqual(saved.RecordPreservations!) &&
                current.Factory != saved.Factory && Read().Value == 0,
                "Owned cold DataHandler replayed original loading/input effects or reused a retired field factory.");
            saved = current;
        }
        return new { source = footprint.Identity, recordsReceipt = records.LoadReceipt.Factory,
            plugins = records.LoadReceipt.InputOrder.Count, byteValue = saved.Value, actualByteStores = saved.Writers.Count,
            preservationReceipts = saved.RecordPreservations!.Count, coldCycles = 3,
            originalLoader = "UNEXECUTED", originalRecordFinalizers = "UNEXECUTED", wholePlayer = "UNRETURNED" };

        FalloutSourceDataHandlerLoadingState New(FalloutDataHandlerLoadingSnapshot? old) =>
            new(assignment, owned.StackId, Guid.NewGuid(), Guid.NewGuid(), invocation =>
            { if (invocation != entered) throw new InvalidOperationException("Owned component loading caller left its explicit scope."); }, old);
        void ActualInput() => records.RequireLiveLoadReceipt(records.LoadReceipt);
        FalloutDataHandlerLoadingRead Read()
        {
            entered = new(Guid.NewGuid(), state.RuntimeProcess, process.Process, process.Actor, 1, Environment.CurrentManagedThreadId,
                FalloutDataHandlerLoadingIngress.PackageCleanup, Guid.NewGuid());
            try { return state.Read(entered); } finally { entered = null; }
        }
    }
}
