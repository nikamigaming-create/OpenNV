using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunDataHandlerLoadingPreservationContracts()
    {
        using var fixture = new DataHandlerLoadingFixture();
        var source = FalloutDataHandlerLoadingPreservationSource.Reviewed(fixture.Assignment);
        var loading = fixture.Create();
        var read = fixture.Records.LoadReceipt;
        Reject(() => loading.PreserveIndexedRecordPrefix(source, fixture.Records, read with { Returned = false }, () => { }));
        var preservation = loading.PreserveIndexedRecordPrefix(source, fixture.Records, read, fixture.RequireIndexed);
        Require(preservation.Before == 0 && preservation.After == 0 && preservation.AvailableAfter &&
            fixture.Read(loading).Value == 0 && loading.Capture().Writers.Count == 0,
            "Returned indexed input fabricated a zero store or failed to preserve the genuine shared constructor byte.");
        var saved = loading.Capture();
        FalloutSourceDataHandlerLoadingState.Validate(saved, fixture.Assignment, fixture.Stack);
        Reject(() => FalloutSourceDataHandlerLoadingState.Validate(saved with
        { RecordPreservations = [preservation with { After = 1 }] }, fixture.Assignment, fixture.Stack));
        Reject(() => loading.PreserveIndexedRecordPrefix(source, fixture.Records, read, fixture.RequireIndexed));
        var originalPrefix = JsonSerializer.Serialize(saved.RecordPrefixes);
        var originalPreservation = JsonSerializer.Serialize(saved.RecordPreservations);
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            loading = fixture.Create(saved);
            loading.BindCurrentIndexedRecords(fixture.Records, read, fixture.RequireIndexed);
            var current = loading.Capture();
            Require(current.Process != saved.Process && current.Factory != saved.Factory && current.RuntimeProcess != saved.RuntimeProcess &&
                current.Constructor != saved.Constructor && current.Sequence == saved.Sequence && fixture.Read(loading).Value == 0 &&
                JsonSerializer.Serialize(current.RecordPrefixes) == originalPrefix &&
                JsonSerializer.Serialize(current.RecordPreservations) == originalPreservation && current.Writers.Count == 0,
                "Cold DataHandler replayed a records prefix/admission/store or reused a source field lifetime.");
            saved = current;
        }
        fixture.Writer(loading, FalloutDataHandlerLoadingIngress.NativeCallback);
        Reject(() => fixture.Read(loading));
        var changed = loading.Capture();
        var writer = changed.Writers.Single();
        var retired = changed.Publications[0];
        Reject(() => FalloutSourceDataHandlerLoadingState.Validate(changed with
        { Writers = [writer with { Factory = retired.Factory, Invocation = writer.Invocation with { RuntimeProcess = retired.RuntimeProcess } }] },
            fixture.Assignment, fixture.Stack));

        using (var prior = new DataHandlerLoadingFixture())
        {
            var state = prior.Create(); prior.Writer(state, FalloutDataHandlerLoadingIngress.NativeCallback);
            var proof = state.PreserveIndexedRecordPrefix(FalloutDataHandlerLoadingPreservationSource.Reviewed(prior.Assignment),
                prior.Records, prior.Records.LoadReceipt, prior.RequireIndexed);
            Require(!proof.AvailableAfter && proof.Before == proof.After && state.SaveBlocker!.Contains("NativeCallback", StringComparison.Ordinal),
                "Records preservation erased a prior native writer instead of preserving its actual unknown state.");
            Reject(() => prior.Read(state)); _ = state.Capture();
        }
        using (var one = new DataHandlerLoadingFixture())
        {
            var state = one.Create(); one.Writer(state, FalloutDataHandlerLoadingIngress.DataHandlerUnload);
            var proof = state.PreserveIndexedRecordPrefix(FalloutDataHandlerLoadingPreservationSource.Reviewed(one.Assignment),
                one.Records, one.Records.LoadReceipt, one.RequireIndexed);
            Require(proof.Before == 1 && proof.After == 1 && one.Read(state).Value == 1 && state.Capture().Writers.Count == 1,
                "Records preservation forced a literal zero instead of retaining the genuine entered-one writer.");
        }
        using (var reentry = new DataHandlerLoadingFixture())
        {
            var state = reentry.Create(); var guards = 0;
            Reject(() => state.PreserveIndexedRecordPrefix(FalloutDataHandlerLoadingPreservationSource.Reviewed(reentry.Assignment),
                reentry.Records, reentry.Records.LoadReceipt, () =>
                {
                    reentry.RequireIndexed();
                    if (++guards == 3) { try { _ = state.Capture(); } catch (InvalidOperationException) { } }
                }));
            Require(state.Capture() is { RecordPrefixes.Count: 1, RecordPreservations.Count: 0, Available: false },
                "Swallowed preservation reentry admitted the byte or erased the real unreturned input prefix.");
            Reject(() => reentry.Read(state));
        }
        Console.WriteLine("OPENNV_DATA_HANDLER_LOADING_PRESERVATION_PASS actualReaderReceipt=true noZeroStore=true " +
            "priorAndLaterWritersHeld=true coldCycles=3 recordAndKindReplay=false originalLoader=UNEXECUTED");
    }

    private sealed class DataHandlerLoadingFixture : IDisposable
    {
        private readonly ReferencePositionFixture _reference = new();
        private FalloutDataHandlerLoadingInvocation? _entered;
        private bool _disposed;
        internal FalloutPluginStack Records => _reference.Records;
        internal string Stack => Records.LoadReceipt.StackIdentity;
        internal FalloutActorPackageAssignmentSource Assignment => FalloutActorPackageAssignmentSource.Reviewed(_reference.Fields.Source);
        internal FalloutSourceDataHandlerLoadingState Create(FalloutDataHandlerLoadingSnapshot? saved = null) =>
            new(Assignment, Stack, Guid.NewGuid(), Guid.NewGuid(), invocation =>
            {
                if (_disposed || invocation != _entered)
                    throw new InvalidOperationException("Authored DataHandler field left its actual runtime/caller scope.");
            }, saved);
        internal void RequireIndexed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Records.RequireLiveLoadReceipt(Records.LoadReceipt);
        }
        internal FalloutDataHandlerLoadingRead Read(FalloutSourceDataHandlerLoadingState state)
            => Ingress(state, FalloutDataHandlerLoadingIngress.PackageCleanup, state.Read);
        internal void Writer(FalloutSourceDataHandlerLoadingState state, FalloutDataHandlerLoadingIngress ingress)
            => _ = Ingress(state, ingress, state.ObserveEnteredWriter);
        private T Ingress<T>(FalloutSourceDataHandlerLoadingState state, FalloutDataHandlerLoadingIngress ingress,
            Func<FalloutDataHandlerLoadingInvocation, T> operation)
        {
            if (_entered is not null) throw new InvalidOperationException("Authored DataHandler caller reentered.");
            var common = _reference.Common.ReadSceneQueries(_reference.Actor.Reference, 1);
            _entered = new(Guid.NewGuid(), state.RuntimeProcess, common.Process, _reference.Actor, 1,
                Environment.CurrentManagedThreadId, ingress, ingress == FalloutDataHandlerLoadingIngress.PackageCleanup ? Guid.NewGuid() : null);
            try { return operation(_entered); } finally { _entered = null; }
        }
        public void Dispose() { _disposed = true; _reference.Dispose(); }
    }
}
