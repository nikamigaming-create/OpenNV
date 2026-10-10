using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunProductRefusalContracts()
    {
        foreach (var step in new[] { FalloutMainScriptCallerStep.KindThreePrelude,
            FalloutMainScriptCallerStep.MainHold, FalloutMainScriptCallerStep.TimedContexts })
        {
            using var fixture = new Fixture();
            Exercise(fixture.Owner, fixture.Host, fixture.Bind, step);
        }
        foreach (var step in new[] { FalloutMainScriptCallerStep.KindThreePrelude,
            FalloutMainScriptCallerStep.TimedContexts })
        {
            using var fixture = new StandaloneFixture();
            Exercise(fixture.Owner, fixture.Host, fixture.Bind, step);
        }
        Console.WriteLine("OPENNV_SOURCE_MAIN_PRODUCT_REFUSAL_PASS actualCampaignConsumers=true " +
            "fnv3=true fo3Independent2=true retainedEnteredFailure=true noSecondSample=true noRetry=true nativeGameplay=UNEXECUTED");

        static void Exercise(FalloutActorProcessRuntimeState owner, Host host, Func<IDisposable> bind,
            FalloutMainScriptCallerStep step)
        {
            // Only the surrounding inputs are authored. The reached child is
            // the real campaign implementation, so a default/no-op regression
            // cannot pass through a fixture's intentionally successful callback.
            var product = new FalloutCampaignMainScriptConsumers(host.Source, _ => false);
            if (step == FalloutMainScriptCallerStep.KindThreePrelude)
            { host.ContextKind = 3; host.DuringKindThree = product.KindThreePrelude; }
            else if (step == FalloutMainScriptCallerStep.MainHold) host.ReadHold = product.MainHold;
            else host.DuringContexts = product.TimedContexts;
            using var lease = bind();
            Reject(() => owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
            var saved = owner.CaptureMainScriptCaller();
            var call = saved.LastCall ?? throw new InvalidOperationException("Product refusal lost its entered Main call.");
            Require(call.Disposition == FalloutMainScriptCallerDisposition.Failed &&
                call.FailureType == typeof(NotSupportedException).FullName &&
                call.Children.Last() is { Returned: null, Error: not null } child && child.Step == step &&
                child.Error.StartsWith("source-Main-", StringComparison.Ordinal) &&
                !call.Children.Any(child => child.Step == FalloutMainScriptCallerStep.SecondSample) &&
                owner.MainScriptCallerSaveBlocker is not null && saved.ContextTimeWrites == 0,
                "The product's unavailable Main child fabricated a result, completed a later sample, or admitted saving.");
            FalloutActorProcessRuntimeState.ValidateMainScriptCaller(saved);
            var logCount = host.Log.Count;
            Reject(() => owner.ExecuteMainScriptCaller(2, .02f).GetAwaiter().GetResult());
            Require(owner.CaptureMainScriptCaller().LastCall == call && host.Log.Count == logCount,
                "A later frame retried a failed product child or replaced its original receipt.");
        }
    }
}
