using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunNativeMainRetirementContracts()
    {
        MainDeliveryLifetime(); StandaloneDeliveryWithoutSteam(); EnteredAndFailedMainDelivery();
        FailedActualLeaseRetry(false); FailedActualLeaseRetry(true); FailedFadeLeaseRetry();
        ForeignAndColdDeliveryHandles(); OpaqueAndWrongRoleDeliveryHandles();
        Console.WriteLine("OPENNV_SOURCE_MAIN_NATIVE_RETIREMENT_PASS authoredActualOwners=true callerBeforeFade=true " +
            "standaloneNoSteam=true enteredRefused=true failedPrefixRetained=true partialReleaseRetry=true " +
            "foreignColdOpaqueRoleRefused=true nativeQuitReloadGpu=UNEXECUTED");
    }

    private static void MainDeliveryLifetime()
    {
        using var fixture = new Fixture();
        IDisposable? caller = fixture.Bind(), fade = fixture.ActualFadeLease;
        var source = fixture.Owner.CaptureMainScriptCaller();
        var bound = fixture.Owner.ReadMainNativeDeliveryBindings();
        Require(!bound.Closed && bound.Caller != Guid.Empty && bound.Fade != Guid.Empty && !bound.ProcessRetired,
            "Actual Main binding was replaced with a registration/readiness fact.");
        Reject(() => fixture.Owner.RequireMainNativeDeliveryClosed());
        Reject(() => fixture.Owner.Dispose());
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        var closed = fixture.Owner.ReadMainNativeDeliveryBindings();
        Require(closed.Closed && caller is null && fade is null && !closed.ProcessRetired &&
            SameRetirementSnapshot(fixture.Owner.CaptureMainScriptCaller(), source),
            "Native handle retirement invented source Main completion, capture or a clock write.");
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        fixture.Owner.Dispose();
        Require(fixture.Owner.ReadMainNativeDeliveryBindings() is { Closed: true, ProcessRetired: true },
            "Source process retired before actual caller/fade release returned.");
    }

    private static void StandaloneDeliveryWithoutSteam()
    {
        using var fixture = new StandaloneFixture();
        IDisposable? caller = fixture.Owner.BindMainScriptCaller(fixture.Host, "authored-FO3-no-Steam-retirement"),
            fade = fixture.ActualFadeLease;
        var before = fixture.Owner.ReadMainNativeDeliveryBindings();
        Require(!fixture.Host.Source.HasNewVegasChildren && before.SourceIdentity == fixture.Host.Source.Identity &&
            before.Caller != Guid.Empty && before.Fade != Guid.Empty, "Standalone Main lost its independently constructed delivery lifetime.");
        Reject(() => fixture.Owner.Dispose());
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        Require(fixture.Owner.ReadMainNativeDeliveryBindings().Closed && fixture.Host.Log.Count == 0 &&
            fixture.Owner.Capture().StandaloneMain!.MainCaller.Calls == 0,
            "Standalone retirement required a Steam provider or replayed a source Player/Steam child.");
    }

    private static void EnteredAndFailedMainDelivery()
    {
        using var fixture = new Fixture(); var completion = new TaskCompletionSource();
        fixture.Host.AwaitPlayer = _ => completion.Task;
        IDisposable? caller = fixture.Bind(), fade = fixture.ActualFadeLease;
        var call = fixture.Owner.ExecuteMainScriptCaller(1, .02f);
        var entered = fixture.Owner.ReadMainNativeDeliveryBindings();
        Require(!call.IsCompleted && entered.EnteredMain is not null, "Actual awaited source child did not retain its invocation.");
        Reject(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade));
        Require(fixture.Owner.ReadMainNativeDeliveryBindings() == entered && caller is not null && fade is not null,
            "Entered retirement changed source fields, retired providers or erased actual handles.");
        var original = new IOException("authored-original-Player-child-failure");
        completion.SetException(original);
        try { call.GetAwaiter().GetResult(); throw new InvalidOperationException("Expected original child failure."); }
        catch (IOException actual) { Require(ReferenceEquals(actual, original), "Main replaced the actual child exception during unwind."); }
        var failed = fixture.Owner.CaptureMainScriptCaller();
        var blocker = fixture.Owner.MainScriptCallerSaveBlocker;
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        Require(fixture.Owner.ReadMainNativeDeliveryBindings().Closed && SameRetirementSnapshot(failed, fixture.Owner.CaptureMainScriptCaller()) &&
            blocker is not null && blocker == fixture.Owner.MainScriptCallerSaveBlocker &&
            failed.LastCall is { Disposition: FalloutMainScriptCallerDisposition.Failed } &&
            failed.LastCall.Children.Last() is { Step: FalloutMainScriptCallerStep.Player, Returned: null },
            "Actual native close cleared the failed entered prefix or manufactured source return/save allowance.");
    }

    private static void FailedActualLeaseRetry(bool afterCommit)
    {
        using var fixture = new Fixture();
        var failing = new FailingActualLease((IFalloutMainNativeDeliveryLease)fixture.Bind(), afterCommit);
        var observedFade = new FailingActualLease((IFalloutMainNativeDeliveryLease)fixture.ActualFadeLease, false, fail: false);
        IDisposable? caller = failing, fade = observedFade;
        _ = RetainedReleaseFailure(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade));
        var partial = fixture.Owner.ReadMainNativeDeliveryBindings();
        Require(ReferenceEquals(caller, failing) && ReferenceEquals(fade, observedFade) && observedFade.Calls == 0 &&
            partial.Fade != Guid.Empty && (partial.Caller == Guid.Empty) == afterCommit,
            "Failed caller release disposed its dependent fade or discarded a partially committed handle.");
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        Require(failing.Calls == 2 && observedFade.Calls == 1 && caller is null && fade is null &&
            fixture.Owner.ReadMainNativeDeliveryBindings().Closed,
            "Actual retry promoted failure to completion instead of returning from both real disposers.");
    }

    private static void FailedFadeLeaseRetry()
    {
        using var fixture = new Fixture();
        var failing = new FailingActualLease((IFalloutMainNativeDeliveryLease)fixture.ActualFadeLease, false);
        IDisposable? caller = fixture.Bind(), fade = failing;
        _ = RetainedReleaseFailure(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade));
        Require(caller is null && ReferenceEquals(fade, failing) && fixture.Owner.ReadMainNativeDeliveryBindings() is
            { Caller: var closed, Fade: var living } && closed == Guid.Empty && living != Guid.Empty,
            "Fade failure forgot its actual handle or incorrectly rebound the already closed caller.");
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
        Require(fade is null && failing.Calls == 2 && fixture.Owner.ReadMainNativeDeliveryBindings().Closed,
            "Fade retry did not retire the original retained source binding.");
    }

    private static void ForeignAndColdDeliveryHandles()
    {
        using var warm = new Fixture();
        IDisposable? warmCaller = warm.Bind(), warmFade = warm.ActualFadeLease;
        var oldCaller = warmCaller; var oldFade = warmFade;
        var runtime = warm.Owner.Capture(); var field = warm.Owner.CaptureMainScriptFrameEvidence();
        var main = warm.Owner.CaptureMainScriptCaller(); var interfaceState = warm.Fade.Capture();
        warm.Owner.CloseMainNativeDeliveryLeases(ref warmCaller, ref warmFade);
        using var cold = new Fixture(runtime, field, main, interfaceState);
        IDisposable? caller = cold.Bind(), fade = cold.ActualFadeLease;
        var current = cold.Owner.ReadMainNativeDeliveryBindings();
        var foreign = oldCaller;
        Reject(() => cold.Owner.CloseMainNativeDeliveryLeases(ref foreign, ref fade));
        foreign = oldFade;
        Reject(() => cold.Owner.CloseMainNativeDeliveryLeases(ref caller, ref foreign));
        Require(cold.Owner.ReadMainNativeDeliveryBindings() == current && current.Process != runtime.CapturedProcess &&
            current.SourceIdentity == warm.Owner.ReadMainNativeDeliveryBindings().SourceIdentity,
            "Cold process reused native handles or changed the exact selected source to force retirement.");
        cold.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
    }

    private static void OpaqueAndWrongRoleDeliveryHandles()
    {
        using var fixture = new Fixture();
        IDisposable? caller = fixture.Bind(), fade = fixture.ActualFadeLease;
        var before = fixture.Owner.ReadMainNativeDeliveryBindings();
        var opaque = new OpaqueRetirementLease(); IDisposable? wrong = opaque;
        Reject(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref wrong, ref fade));
        wrong = fade;
        Reject(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref wrong, ref fade));
        var missing = (IDisposable?)null;
        Reject(() => fixture.Owner.CloseMainNativeDeliveryLeases(ref missing, ref fade));
        Require(opaque.Calls == 0 && fixture.Owner.ReadMainNativeDeliveryBindings() == before,
            "Opaque, wrong-role or missing actual ownership was accepted before source release.");
        fixture.Owner.CloseMainNativeDeliveryLeases(ref caller, ref fade);
    }

    private sealed class FailingActualLease(IFalloutMainNativeDeliveryLease actual, bool afterCommit, bool fail = true)
        : IFalloutMainNativeDeliveryLease
    {
        public Guid Process => actual.Process;
        public Guid Identity => actual.Identity;
        public string? SourceIdentity => actual.SourceIdentity;
        public int ManagedThread => actual.ManagedThread;
        public FalloutMainNativeLeaseRole Role => actual.Role;
        internal int Calls;
        public void Dispose()
        {
            Calls++;
            if (fail && Calls == 1)
            {
                if (afterCommit) actual.Dispose();
                throw new IOException("authored-actual-handle-release-failure");
            }
            actual.Dispose();
        }
    }

    private sealed class OpaqueRetirementLease : IDisposable
    {
        internal int Calls;
        public void Dispose() => Calls++;
    }

    private static AggregateException RetainedReleaseFailure(Action release)
    {
        try { release(); }
        catch (AggregateException error)
        {
            Require(error.InnerExceptions.Count == 1 && error.InnerExceptions[0] is IOException
                { Message: "authored-actual-handle-release-failure" }, "Retirement lost the original actual disposer exception.");
            return error;
        }
        throw new InvalidOperationException("Expected actual source-handle retirement failure.");
    }

    private static bool SameRetirementSnapshot<T>(T before, T after) =>
        System.Text.Json.JsonSerializer.Serialize(before) == System.Text.Json.JsonSerializer.Serialize(after);
}
