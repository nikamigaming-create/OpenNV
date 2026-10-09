using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPendingConsumerContracts()
    {
        PendingRawCellsAndMarkers(); PendingOwnedChildOrder(); EmptyPendingFurnitureAndCold(); ExteriorLoaderCancellationOrder();
        Console.WriteLine("OPENNV_SOURCE_PLAYER_PENDING_CONSUMERS_PASS rawPointerCells=true scalarAliasBits=true " +
            "emptyFurnitureTail=true loaderDuplicateBeforeFactory=true bucketHeadOrder=true retainedRefcountFailure=true " +
            "newProcessEmptyMap=true nativeTaskTlsController=UNEXECUTED");
    }
    private static FalloutPlayerTransferPayload AuthoredPendingPayload(FalloutMainPlayerPendingSource source,
        FalloutFormKey? reference = null, FalloutFormKey? furniture = null) => new(source.Contract,
            "authored-complete-pending-allocation", null, null, reference, 0, 0, 0x80000000, 0, 0, 0, 0, null, furniture);
    private static void PendingRawCellsAndMarkers()
    {
        using var fixture = new Fixture();
        var source = FalloutMainPlayerPendingSource.Read(FalloutMainPlayerCellSource.Read(fixture.Host.Source));
        var payload = AuthoredPendingPayload(source) with { Cell = new("Authored.esm", 50), Worldspace = new("Authored.esm", 2),
            Reference = new("Authored.esm", 30), PositionZ = 0x7fc01234 };
        payload.Validate(source);
        Require(payload.Target == FalloutPlayerTransferTarget.Reference && payload.TargetForm == payload.Reference &&
            payload.Cell is not null && payload.Worldspace is not null && payload.ControllerScalarBits == 0x7fc01234,
            "Pending tagged selection dropped losing raw pointer cells or canonicalized its Float32 scalar alias.");
        Require((payload with { Reference = null }).Target == FalloutPlayerTransferTarget.Worldspace &&
            (payload with { Reference = null, Worldspace = null }).Target == FalloutPlayerTransferTarget.Cell,
            "Original source reference/worldspace/CELL pointer priority changed.");
        Reject(() => (payload with { SourceContract = new('f', 64) }).Validate(source));
        Reject(() => payload.RequireRole(FalloutPlayerPendingKind.Empty));
        Require(source.PackCellKey(-1, -2) == 0xfffffffe && source.PackCellKey(65536, 1) == 1,
            "Original loader key lost independent sixteen-bit truncation/signed coordinate fields.");
        Require(FalloutPendingFurnitureSource.FirstMarker((1u << 2) | (1u << 7), 1u << 2, 1u << 7, true) == 7 &&
            FalloutPendingFurnitureSource.FirstMarker((1u << 2) | (1u << 7), 1u << 2, 1u << 7, false) == -1 &&
            FalloutPendingFurnitureSource.FirstMarker(0xc0000000, 0, 0, true) == -1,
            "Pending furniture did not select the first enabled unused marker or consumed the excluded reserved/kind bits.");
        Require(FalloutPendingFurnitureSource.Kind(1) == OpenNV.Runtime.Gameplay.State.FalloutPlayerFurnitureKind.Sitting &&
            FalloutPendingFurnitureSource.Kind(0xc0000001) == OpenNV.Runtime.Gameplay.State.FalloutPlayerFurnitureKind.Sleeping,
            "Pending furniture consumed normal activation's independent kind gate instead of the source high-bit Boolean.");
        var slot = new FalloutPlayerPendingSlot();
        var callback = payload with { Callback = new(Guid.NewGuid(), source.Contract, "authored-native-callback", new("Authored.esm", 30)) };
        var request = slot.StoreSource(FalloutPlayerPendingKind.ReferenceTravel, null, null, "authored-raw-callback", callback);
        Require(slot.SaveBlocker is not null, "A registered callback Guid certified native cold-context reconstruction.");
        var snapshot = slot.Capture(); var cold = new FalloutPlayerPendingSlot();
        Reject(() => cold.Restore(snapshot)); Require(cold.Capture().Revision == 0 && slot.Next == request,
            "Rejected native callback cold admission mutated the new slot or discarded the live original payload.");
        slot.Retire();
    }
    private static void PendingOwnedChildOrder()
    {
        using var fixture = new Fixture();
        var source = FalloutMainPlayerPendingSource.Read(FalloutMainPlayerCellSource.Read(fixture.Host.Source));
        var process = Guid.NewGuid(); var state = new FalloutMainPlayerPendingState(source, "authored-refcount-owner", process);
        var log = new List<string>(); var a = new AuthoredOwnedChild(source, process, "a", log); var b = new AuthoredOwnedChild(source, process, "b", log);
        state.PublishOwnedChild(a, "authored-source-writer-a"); state.PublishOwnedChild(a, "authored-identical-pointer-noop");
        state.PublishOwnedChild(b, "authored-source-writer-b");
        Require(log.SequenceEqual(new[] { "acquire:a", "release:a", "acquire:b" }) && a.References == 0 && b.References == 1,
            "Source smart pointer changed release/store/acquire order or incremented an identical pointer.");
        state.Retire(); Require(b.References == 0 && state.Capture().ChildMutation is { After: null, Returned: true },
            "Actual source destructor did not release its still-owned successful child independently.");
        var failed = new FalloutMainPlayerPendingState(source, "authored-failed-refcount", process);
        var old = new AuthoredOwnedChild(source, process, "old", log) { RefuseRelease = true };
        var next = new AuthoredOwnedChild(source, process, "unpublished", log);
        failed.PublishOwnedChild(old, "authored-retained-old-owner");
        Reject(() => failed.PublishOwnedChild(next, "authored-failing-release-before-store"));
        Require(old.References == 1 && next.References == 0 && failed.SaveBlocker is not null,
            "A failed release forgot the old source owner or acquired the uncommitted replacement.");
        RejectPendingRetirement(failed.Retire);
        Require(old.ReleaseAttempts == 1 && next.References == 0,
            "Retirement blindly repeated an opaque failed refcount release or pretended a null-store completion.");
    }
    private static void EmptyPendingFurnitureAndCold()
    {
        using var fixture = new Fixture(); var slot = new FalloutPlayerPendingSlot();
        var source = FalloutMainPlayerCellSource.Read(fixture.Host.Source);
        fixture.Owner.ConstructMainPlayerCell(source, slot, null);
        var state = fixture.Owner.MainPlayerPendingConsumers; var consumer = new AuthoredPendingTail(source, state);
        fixture.Host.AwaitPlayer = fixture.Owner.ExecuteMainPlayerCell;
        var request = slot.StoreSource(FalloutPlayerPendingKind.Empty, null, null, "authored-empty-source-allocation",
            AuthoredPendingPayload(FalloutMainPlayerPendingSource.Read(source), furniture: new("Authored.esm", 44)));
        using (fixture.Owner.BindMainPlayerCell(consumer, "authored-current-pending-tail"))
        using (fixture.Bind()) fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        var saved = fixture.Owner.CaptureMainPlayerCell();
        Require(consumer.FurnitureCalls == 1 && consumer.DeferredCalls == 1 && !slot.Pending &&
            slot.Capture().Completion?.Identity == request.Identity && state.Capture().LastFurnitureRequest == request.Identity &&
            !saved.LastCall!.Children.Any(child => child.Step is FalloutMainPlayerCellStep.PendingSceneScalar or FalloutMainPlayerCellStep.PendingCallback),
            "Empty target skipped original furniture/deferred/free/null order or entered scalar/callback without a returned destination.");
        var missing = saved with { LastCall = saved.LastCall! with { Children = saved.LastCall.Children
            .Where(child => child.Step != FalloutMainPlayerCellStep.PendingFurniture).ToArray() } };
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainPlayerCell(missing));
        using var cold = new Fixture(fixture.Owner.Capture(), fixture.Owner.CaptureMainScriptFrameEvidence(),
            fixture.Owner.CaptureMainScriptCaller(), fixture.Fade.Capture());
        cold.Owner.ConstructMainPlayerCell(source, new FalloutPlayerPendingSlot(), saved);
        var resumed = cold.Owner.CaptureMainPlayerCell();
        Require(resumed.PendingConsumers.CapturedProcess != saved.PendingConsumers.CapturedProcess &&
            resumed.PendingConsumers.Handoff?.PreviousProcess == saved.PendingConsumers.CapturedProcess &&
            resumed.PendingConsumers.LastFurnitureRequest == request.Identity && resumed.Pending.Completion == saved.Pending.Completion,
            "Cold pending owner replayed consumed furniture, lost its exact null-store receipt or promoted the old process.");
    }
    private static void ExteriorLoaderCancellationOrder()
    {
        using var fixture = new Fixture(); var slot = new FalloutPlayerPendingSlot();
        var source = FalloutMainPlayerCellSource.Read(fixture.Host.Source);
        fixture.Owner.ConstructMainPlayerCell(source, slot, null); var state = fixture.Owner.MainPlayerPendingConsumers;
        var consumer = new AuthoredPendingTail(source, state); fixture.Host.AwaitPlayer = fixture.Owner.ExecuteMainPlayerCell;
        var tasks = new List<AuthoredExteriorTask>(); var log = new List<uint>(); var factories = 0;
        consumer.DuringTransfer = invocation =>
        {
            foreach (var y in new[] { 1, 38, 2 })
            {
                var cell = new FalloutCellProcessIdentity(new("Authored.esm", (uint)(100 + y)), 0, new('c', 64), new("Authored.esm", 2), new('d', 64));
                state.ExteriorLoaders.ReadOrCreate(invocation, cell, 0, y, key =>
                {
                    factories++;
                    var task = new AuthoredExteriorTask(new(Guid.NewGuid(), key, cell, 0, y, invocation.Main.Identity,
                        "authored-source-ExteriorCellLoaderTask"), log); tasks.Add(task); return task;
                });
            }
            var existing = tasks.Single(task => task.Source.Key == 38);
            var duplicate = state.ExteriorLoaders.ReadOrCreate(invocation, existing.Source.Cell, 0, 38,
                _ => throw new InvalidOperationException("Duplicate source lookup illegally entered another task constructor."));
            Require(duplicate == existing.Source && factories == 3, "Source duplicate lookup allocated/discarded another owned task.");
        };
        using (fixture.Owner.BindMainPlayerCell(consumer, "authored-source-loader-callers"))
        using (fixture.Bind())
        {
            var header = AuthoredPendingPayload(FalloutMainPlayerPendingSource.Read(source), reference: new("Authored.esm", 30));
            slot.StoreSource(FalloutPlayerPendingKind.ReferenceTravel, null, null, "authored-loader-first-source-transfer", header);
            fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
            consumer.DuringTransfer = null;
            slot.StoreSource(FalloutPlayerPendingKind.ReferenceTravel, null, null, "authored-loader-second-source-transfer", header);
            fixture.Owner.ExecuteMainScriptCaller(2, .02f).GetAwaiter().GetResult();
        }
        Require(log.SequenceEqual(new uint[] { 38, 1, 2 }), "Original cancellation lost bucket-ascending/new-head entry order.");
        Reject(() => state.ExteriorLoaders.Capture());
        foreach (var task in tasks) { task.Retired = true; state.ExteriorLoaders.RetireReturnedTask(task); }
        var saved = fixture.Owner.CaptureMainPlayerCell();
        Require(saved.PendingConsumers.ExteriorLoaders.LastCancellation is { Returned: 3, Error: null },
            "Actual task cancellation completion was replaced by map emptiness or scheduled retirement.");
        using var cold = new Fixture(fixture.Owner.Capture(), fixture.Owner.CaptureMainScriptFrameEvidence(),
            fixture.Owner.CaptureMainScriptCaller(), fixture.Fade.Capture());
        cold.Owner.ConstructMainPlayerCell(source, new FalloutPlayerPendingSlot(), saved);
        Require(cold.Owner.CaptureMainPlayerCell().PendingConsumers.ExteriorLoaders.Handoff?.PreviousProcess ==
            saved.PendingConsumers.ExteriorLoaders.Process && log.Count == 3,
            "Cold empty source map replayed native cancellation or reused the old process owner.");
    }
    private static void RejectPendingRetirement(Action action)
    {
        try { action(); }
        catch (AggregateException failure) when (failure.InnerExceptions.All(error => error is InvalidOperationException)) { return; }
        throw new InvalidOperationException("Expected retained independent actual child ownership refusal.");
    }
    private sealed class AuthoredOwnedChild(FalloutMainPlayerPendingSource source, Guid process, string name, List<string> log) : IFalloutPlayerOwnedChild
    {
        public Guid Identity { get; } = Guid.NewGuid();
        public Guid Process => process;
        public string SourceContract => source.Contract;
        public string Owner => "authored-refcount-child/" + name;
        internal int References, ReleaseAttempts;
        internal bool RefuseRelease;
        public void Acquire() { log.Add("acquire:" + name); References++; }
        public void Release()
        {
            ReleaseAttempts++; log.Add("release:" + name);
            if (RefuseRelease) throw new IOException("authored-retained-source-refcount-failure");
            if (References != 1) throw new InvalidOperationException("Actual child release lost its owned reference.");
            References--;
        }
    }
    private sealed class AuthoredExteriorTask(FalloutExteriorCellLoaderTaskSource source, List<uint> log) : IFalloutExteriorCellLoaderTask
    {
        public FalloutExteriorCellLoaderTaskSource Source => source;
        public bool Retired { get; set; }
        public void RequestSourceCancellation(FalloutMainPlayerCellInvocation invocation)
        { invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude); log.Add(source.Key); }
    }
    private sealed class AuthoredPendingTail(FalloutMainPlayerCellSource source, FalloutMainPlayerPendingState state) : IFalloutMainPlayerCellConsumers
    {
        private readonly AuthoredPlayerCell _interior = new(source);
        public FalloutMainPlayerCellSource Source => source;
        public string Owner => "authored-pending-tail-current-field-consumers";
        internal int FurnitureCalls, DeferredCalls;
        internal Action<FalloutMainPlayerCellInvocation>? DuringTransfer;
        public void PendingWorldPrelude(FalloutMainPlayerCellInvocation invocation) => state.ResetExteriorLoaders(invocation);
        public void ReleasePendingOwnedChild(FalloutMainPlayerCellInvocation invocation) => state.ReleaseOwnedChild(invocation);
        public Task<bool> TransferPending(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request)
        {
            invocation.Require(FalloutMainPlayerCellStep.PendingDestination); var header = state.Payload(request);
            DuringTransfer?.Invoke(invocation); return Task.FromResult(header.Target != FalloutPlayerTransferTarget.Empty);
        }
        public void PendingFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) =>
            state.ConsumeFurniture(invocation, request, _ => FurnitureCalls++);
        public void RetirePendingDeferredChildren(FalloutMainPlayerCellInvocation invocation)
        { invocation.Require(FalloutMainPlayerCellStep.PendingDeferredDestruction); DeferredCalls++; }
        public bool PendingFlagQueries(FalloutMainPlayerCellInvocation invocation) =>
            state.ReadFinalFlagQueries(invocation, () => 2u); // Independently authored held manager word; not a production producer.
        public void PendingFlagChild(FalloutMainPlayerCellInvocation invocation) => state.StoreFinalFlag(invocation);
        public void StorePendingSceneScalar(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw new InvalidOperationException("Excluded authored scalar arm.");
        public void InvokePendingCallback(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request) => throw new InvalidOperationException("Excluded authored callback arm.");
        public bool SceneMode(FalloutMainPlayerCellInvocation invocation) => _interior.SceneMode(invocation);
        public bool ScenePresent(FalloutMainPlayerCellInvocation invocation) => _interior.ScenePresent(invocation);
        public FalloutMainPlayerSourceCell? ParentCell(FalloutMainPlayerCellInvocation invocation) => _interior.ParentCell(invocation);
        public FalloutMainPlayerSourcePosition Position(FalloutMainPlayerCellInvocation invocation) => _interior.Position(invocation);
        public bool HeldInterface(FalloutMainPlayerCellInvocation invocation) => _interior.HeldInterface(invocation);
        public void HeldChild(FalloutMainPlayerCellInvocation invocation) => _interior.HeldChild(invocation);
        public void SceneClock(FalloutMainPlayerCellInvocation invocation) => _interior.SceneClock(invocation);
        public void SceneChild(FalloutMainPlayerCellInvocation invocation, bool alternate) => _interior.SceneChild(invocation, alternate);
        public FalloutMainPlayerCellTarget? TargetCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerSourceCell before,
            FalloutMainPlayerSourcePosition position) => _interior.TargetCell(invocation, before, position);
        public Task LoadTarget(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target,
            FalloutMainPlayerSourcePosition position) => _interior.LoadTarget(invocation, target, position);
        public void SetWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value) => _interior.SetWorldBracket(invocation, value);
        public void SetPlayerBracket(FalloutMainPlayerCellInvocation invocation, bool value) => _interior.SetPlayerBracket(invocation, value);
        public void AttachCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => _interior.AttachCell(invocation, target);
        public void StoreRoot(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target) => _interior.StoreRoot(invocation, target);
        public void OptionalTreeChild(FalloutMainPlayerCellInvocation invocation) => _interior.OptionalTreeChild(invocation);
    }
}
