using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerFirstModelRetirementContracts()
    {
        RunReferenceModelRetirementRetryContracts();
        using (var fixture = new PlayerReturnTailFixture())
            fixture.EnterLoad((load, guard) =>
            {
                var source = AuthoredModelContinuation(fixture.State.Source);
                var globals = new FalloutPlayerModelLoadGlobals(source, load.ReferenceProcess, guard);
                var field = new FalloutPlayerFirstModelState(source, load.ReferenceProcess, load.ReferenceConstructor, globals, guard);
                var adopted = new FirstModelBinding(load.ReferenceProcess, 31);
                field.PublishAndInitialize(load, adopted.Node, (_, _) => throw new InvalidOperationException("Authored named nodes exist."));
                adopted.Healthy = false;
                Require(field.RetainsModel(adopted.Node.Node.Factory) && !field.CleanupUnpublished(adopted.Node) && adopted.BindingCalls == 0,
                    "Later gameplay failure allowed finally cleanup to free an actually adopted first model.");
                field.Retire(); field.Retire();
                Require(!field.RetainsModel(adopted.Node.Node.Factory) && adopted.SourceReleases == 1 && adopted.BindingCalls == 1 && field.Capture().Retired,
                    "First-model cleanup borrowed gameplay health or released its field/native binding twice.");
            });
        using (var fixture = new PlayerReturnTailFixture())
            fixture.EnterLoad((load, guard) =>
            {
                var source = AuthoredModelContinuation(fixture.State.Source);
                var field = new FalloutPlayerFirstModelState(source, load.ReferenceProcess, load.ReferenceConstructor,
                    new(source, load.ReferenceProcess, guard), guard);
                var unpublished = new FirstModelBinding(load.ReferenceProcess, 32) { FailBinding = true };
                Reject(() => field.CleanupUnpublished(unpublished.Node));
                Require(field.RetainsModel(unpublished.Node.Node.Factory) && unpublished.SourceReleases == 0 && unpublished.BindingCalls == 1,
                    "Failed unpublished native retirement lost its actual zero-reference pending owner.");
                unpublished.FailBinding = false; field.Retire();
                Require(unpublished.BindingCalls == 2 && unpublished.SourceReleases == 0 && field.Capture().Retired,
                    "Unpublished retry fabricated a source retain/release or repeated successful native teardown.");
            });
        using (var fixture = new PlayerReturnTailFixture())
        {
            FalloutPlayerFirstModelState? field = null;
            var a = new FirstModelBinding(fixture.Scene.Process, 41);
            var b = new FirstModelBinding(fixture.Scene.Process, 42);
            var c = new FirstModelBinding(fixture.Scene.Process, 43);
            a.Node.Retain(() => { }); b.Node.Retain(() => { });
            foreach (var binding in new[] { a, b, c })
                fixture.EnterLoad((load, guard) =>
                {
                    var source = AuthoredModelContinuation(fixture.State.Source);
                    field ??= new(source, load.ReferenceProcess, load.ReferenceConstructor, new(source, load.ReferenceProcess, guard), guard);
                    field.PublishAndInitialize(load, binding.Node, (_, _) => throw new InvalidOperationException("Authored named nodes exist."));
                });
            var owner = field ?? throw new InvalidOperationException("Actual authored first field was not constructed.");
            a.FailBinding = b.FailBinding = true;
            Reject(() => a.Node.Release(() => { })); Reject(() => b.Node.Release(() => { }));
            b.FailBinding = false; c.FailBinding = true;
            try
            {
                owner.Retire();
                throw new InvalidOperationException("Expected the two independent native retirement failures.");
            }
            catch (AggregateException failure)
            {
                Require(failure.InnerExceptions.Count == 2 && failure.InnerExceptions.All(error =>
                    error is InvalidOperationException { Message: "authored-native-binding-retirement" }),
                    "Independent first-model retirement lost its two actual native failures.");
            }
            Require(a.BindingCalls == 2 && b.BindingCalls == 2 && c.BindingCalls == 1 &&
                a.SourceReleases == 1 && b.SourceReleases == 1 && c.SourceReleases == 1 &&
                owner.Capture() is { Current: null, FieldRetained: false, Retired: false, PendingBindings.Count: 2 },
                "One failed first binding prevented independent pending cleanup or repeated a committed source release.");
            a.FailBinding = c.FailBinding = false; owner.Retire(); owner.Retire();
            Require(a.BindingCalls == 3 && b.BindingCalls == 2 && c.BindingCalls == 2 && owner.Capture().Retired &&
                a.SourceReleases == 1 && b.SourceReleases == 1 && c.SourceReleases == 1,
                "Pending-only retry repeated a source release or revisited an already retired independent binding.");
        }
        using (var fixture = new PlayerReturnTailFixture())
            fixture.EnterLoad((load, guard) =>
            {
                var source = AuthoredModelContinuation(fixture.State.Source);
                var field = new FalloutPlayerFirstModelState(source, load.ReferenceProcess, load.ReferenceConstructor,
                    new(source, load.ReferenceProcess, guard), guard);
                var failed = new FirstModelBinding(load.ReferenceProcess, 51) { FailRelease = true };
                field.PublishAndInitialize(load, failed.Node, (_, _) => throw new InvalidOperationException("Authored named nodes exist."));
                RejectFirstModelRetirement(field.Retire, "authored-sticky-source-release");
                RejectFirstModelRetirement(field.Retire, "First-model field retains a failed source release; that callback cannot replay.");
                Require(failed.SourceReleases == 1 && failed.BindingCalls == 0 && field.RetainsModel(failed.Node.Node.Factory) &&
                    !field.CleanupUnpublished(failed.Node) && field.Capture() is { FieldRetained: true, Retired: false, RetirementFailure: not null },
                    "Sticky failed source release was retried or used as permission to free the retained native model.");
            });
        Console.WriteLine("OPENNV_FIRST_MODEL_RETIREMENT_PASS adoptedFinallyPreserved=true gameplayHealthIndependent=true independentPendingDrain=true sourceReleaseOnce=true pendingBindingRetryOnly=true nativeGameplay=UNEXECUTED");
    }

    private static void RejectFirstModelRetirement(Action retire, string message)
    {
        try { retire(); }
        catch (AggregateException failure)
        {
            Require(failure.InnerExceptions is [InvalidOperationException actual] && actual.Message == message,
                "First-model retirement changed its exact retained source failure.");
            return;
        }
        throw new InvalidOperationException("Expected a retained first-model retirement failure.");
    }

    private sealed class FirstModelBinding
    {
        internal readonly FalloutReferenceModelDataNode Node;
        internal bool Healthy = true, FailRelease, FailBinding;
        internal int SourceReleases, BindingCalls;
        internal FirstModelBinding(Guid process, ulong nativeRoot)
        {
            var model = PlayerOpacityContracts.CreateReferenceColdModel(); var graph = model.ConstructLogicalScene();
            Node = new(new(process, model.Factory, nativeRoot), graph,
                () => { if (!Healthy) throw new InvalidOperationException("authored-later-gameplay-child-failed"); },
                () => { ++SourceReleases; if (FailRelease) throw new InvalidOperationException("authored-sticky-source-release"); },
                () => { ++BindingCalls; if (FailBinding) throw new InvalidOperationException("authored-native-binding-retirement"); graph.Retire(); },
                () => { });
        }
    }
}
