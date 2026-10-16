using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunReferenceModelRetirementRetryContracts()
    {
        using (var fixture = new MoverDeltaFixture())
        {
            var binding = new ReferenceDataRetirementBinding(fixture, null, failRetain: false);
            StoreReferenceRetirementModel(fixture, binding, cold: false);
            var original = fixture.Data.Capture();
            binding.Graph.PublishLocalPose(new(0, 2), FalloutNifSceneMath.Identity with { Translation = new(13, 7, 2) });
            binding.Graph.UpdateWorldAndBounds();
            var before = fixture.Data.Capture(); var expectedGraph = JsonSerializer.Serialize(before.ModelScene);
            Require(expectedGraph != JsonSerializer.Serialize(original.ModelScene),
                "The retirement regression did not exercise a genuine later logical pose writer.");
            RejectReferenceRetirement(fixture.Data.Retire, "authored-final-binding-failed-after-graph-retirement");
            Reject(() => binding.Graph.Capture());
            var failed = fixture.Data.Capture();
            Require(failed is { RetirementStarted: true, Retired: false,
                    RetirementFailure: "authored-final-binding-failed-after-graph-retirement" } &&
                binding.SourceReleases == 1 && binding.BindingCalls == 1 && fixture.Data.RetainsReturnedModel(binding.Node.Node) &&
                JsonSerializer.Serialize(failed.ModelScene) == expectedGraph &&
                JsonSerializer.Serialize(failed.Calls) == JsonSerializer.Serialize(before.Calls),
                "Failed binding retirement lost the original current graph/field history or replayed its source release.");
            var preserved = false;
            fixture.Data.CleanupUnpublishedModel(binding.Node.Node, () => preserved = true,
                () => throw new InvalidOperationException("Pending actual binding was mistaken for an unadopted model."));
            Require(preserved, "Pending final binding lost its actual source pointer during finally cleanup.");
            RequireReferenceRetirementBlocksGameplay(fixture, failed);
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                failed = JsonSerializer.Deserialize<FalloutReferenceModelDataSnapshot>(JsonSerializer.Serialize(failed))!;
                FalloutReferenceModelDataState.Validate(failed);
                Require(JsonSerializer.Serialize(failed.ModelScene) == expectedGraph,
                    "Diagnostic JSON changed the first pre-retirement graph.");
            }
            Reject(() => FalloutReferenceModelDataState.Validate(failed with { RetirementStarted = false }));
            Reject(() => FalloutReferenceModelDataState.Validate(failed with { RetirementFailure = " " }));
            Reject(() => FalloutReferenceModelDataState.Validate(failed with { ModelScene = failed.ModelScene! with { Retired = true } }));
            fixture.Data.Retire(); fixture.Data.Retire();
            var retired = fixture.Data.Capture();
            Require(retired is { RetirementStarted: true, Retired: true,
                    RetirementFailure: "authored-final-binding-failed-after-graph-retirement" } &&
                binding.SourceReleases == 1 && binding.BindingCalls == 2 &&
                !fixture.Data.RetainsReturnedModel(binding.Node.Node) && fixture.Data.SaveBlocker is not null &&
                JsonSerializer.Serialize(retired.ModelScene) == expectedGraph,
                "Retry recaptured a retired graph, released source twice, lost its failure or failed to complete only the pending binding.");
            Reject(() => _ = fixture.Data.PreparedColdModel);
            RejectReferenceRetirementCold(fixture, retired);
            Reject(() => FalloutReferenceModelDataState.Validate(original with { Retired = true }));
        }

        var saved = CreateReferenceRetirementColdBaseline();
        foreach (var cold in new[] { false, true })
        {
            using var fixture = new MoverDeltaFixture(cold ? saved : null);
            var binding = new ReferenceDataRetirementBinding(fixture, fixture.Data.PreparedColdModel, failRetain: true);
            Reject(() => StoreReferenceRetirementModel(fixture, binding, cold));
            var pointerStored = fixture.Data.Capture();
            Require(pointerStored.Failure is not null && pointerStored.Fields!.Model == binding.Node.Node &&
                (cold ? pointerStored.ColdBindings![^1] is { ModelStored: true, ModelRetained: false, Returned: false } :
                    pointerStored.Calls[^1] is { Returned: false } call && call.Stores[^1].Field == "model") && binding.SourceReleases == 0,
                "Failed retain did not retain its actual stored pointer without inventing an acquired source reference.");
            binding.Graph.PublishLocalPose(new(0, 2), FalloutNifSceneMath.Identity with { Translation = new(31, 4, 9) });
            binding.Graph.UpdateWorldAndBounds();
            var current = fixture.Data.Capture(); var expectedGraph = JsonSerializer.Serialize(current.ModelScene);
            if (cold)
                Require(current.ModelScene!.Factory != saved.ModelScene!.Factory && current.ModelScene.RestoredLifetime == saved.ModelScene.Lifetime,
                    "Cold retirement regression failed to bind a genuinely fresh current graph.");
            RejectReferenceRetirement(fixture.Data.Retire, "authored-final-binding-failed-after-graph-retirement");
            var failed = fixture.Data.Capture();
            Require(JsonSerializer.Serialize(failed.ModelScene) == expectedGraph &&
                JsonSerializer.Serialize(failed.Calls) == JsonSerializer.Serialize(current.Calls) &&
                JsonSerializer.Serialize(failed.ColdBindings) == JsonSerializer.Serialize(current.ColdBindings) &&
                binding.SourceReleases == 0 && binding.BindingCalls == 1,
                "Warm/cold failed retain substituted the saved baseline, erased its failed prefix or fabricated a source release.");
            fixture.Data.Retire(); fixture.Data.Retire();
            Require(fixture.Data.Capture().Retired && binding.SourceReleases == 0 && binding.BindingCalls == 2 &&
                JsonSerializer.Serialize(fixture.Data.Capture().ModelScene) == expectedGraph,
                "Zero-retain retirement retry recaptured the retired graph or fabricated a source decrement.");
        }

        using (var fixture = new MoverDeltaFixture())
        {
            var binding = new ReferenceDataRetirementBinding(fixture, null, failRetain: false, failBinding: false);
            binding.Node.Retain(() => { }); // A distinct real source owner shares this node.
            StoreReferenceRetirementModel(fixture, binding, cold: false);
            var before = fixture.Data.Capture(); var expectedGraph = JsonSerializer.Serialize(before.ModelScene);
            RejectReferenceRetirement(fixture.Data.Retire, "Reference model binding retains source references.");
            Require(binding.SourceReleases == 0 && binding.BindingCalls == 0 && fixture.Data.Capture().RetirementStarted,
                "Reference data retired a binding while another actual source reference remained.");
            binding.Node.Release(() => { });
            Reject(() => binding.Graph.Capture());
            fixture.Data.Retire();
            Require(binding.SourceReleases == 1 && binding.BindingCalls == 1 && fixture.Data.Capture().Retired &&
                JsonSerializer.Serialize(fixture.Data.Capture().ModelScene) == expectedGraph,
                "Shared-node final-owner release forced graph recapture or repeated the committed source decrement.");
        }
        Console.WriteLine("OPENNV_REFERENCE_MODEL_RETIREMENT_RETRY_PASS actualAuthoredNifNode=true graphRetiredBeforeFailure=true " +
            "firstPreRetirementGraphRetained=true finalBindingRetryOnly=true failedRetainWarmAndCold=true sharedSourceReleaseOnce=true " +
            "diagnosticJsonCycles=3 gameplayAndColdRefused=true nativeGameplay=UNEXECUTED");
    }

    private static void StoreReferenceRetirementModel(MoverDeltaFixture fixture, ReferenceDataRetirementBinding binding, bool cold) =>
        fixture.DataCall(cold ? FalloutReferenceModelDataIngress.ColdBinding : FalloutReferenceModelDataIngress.ModelSetter, invocation =>
        {
            if (cold) fixture.Data.BindColdModel(invocation, binding.Node);
            else fixture.Data.StoreReturnedModel(invocation, new("authored-current-reference-retirement-ParentCELL", fixture.Stack,
                Guid.NewGuid(), Guid.NewGuid(), fixture.Scene.Constructor, fixture.Actor, 0, null,
                fixture.Source.Controller.Fields.Controls.Main.EngineSha256), binding.Node);
        });

    private static FalloutReferenceModelDataSnapshot CreateReferenceRetirementColdBaseline()
    {
        using var fixture = new MoverDeltaFixture();
        var binding = new ReferenceDataRetirementBinding(fixture, null, failRetain: false, failBinding: false);
        StoreReferenceRetirementModel(fixture, binding, cold: false);
        return JsonSerializer.Deserialize<FalloutReferenceModelDataSnapshot>(JsonSerializer.Serialize(fixture.Data.Capture()))!;
    }

    private static void RequireReferenceRetirementBlocksGameplay(MoverDeltaFixture fixture, FalloutReferenceModelDataSnapshot saved)
    {
        var before = JsonSerializer.Serialize(saved.Calls);
        fixture.ReadFrameLeaf(frame => { Reject(() => fixture.Data.ReadForMover(frame, fixture.RequireFrame)); return true; });
        Reject(() => fixture.DataCall(FalloutReferenceModelDataIngress.ModelSetter,
            invocation => fixture.Data.StoreReturnedModel(invocation, null, null)));
        Reject(() => _ = fixture.Data.PreparedColdModel);
        Require(fixture.Data.SaveBlocker is not null && JsonSerializer.Serialize(fixture.Data.Capture().Calls) == before,
            "Retirement admitted a new gameplay read/setter, save or constructor replay.");
        RejectReferenceRetirementCold(fixture, saved);
    }

    private static void RejectReferenceRetirementCold(MoverDeltaFixture fixture, FalloutReferenceModelDataSnapshot saved) =>
        Reject(() => new FalloutReferenceModelDataState(fixture.Source, fixture.Records, fixture.Stack, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), fixture.Actor, 1, _ => { }, fixture.RequireLife, saved));

    private static void RejectReferenceRetirement(Action action, string expected)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            Require(error.Message == expected, "Reference retirement substituted another source failure: " + error.Message);
            return;
        }
        throw new InvalidOperationException("Expected the actual reference retirement failure: " + expected);
    }

    private sealed class ReferenceDataRetirementBinding
    {
        internal readonly FalloutActorLogicalScene Graph;
        internal readonly FalloutReferenceModelDataNode Node;
        internal int SourceReleases, BindingCalls;
        private int _reads;
        internal ReferenceDataRetirementBinding(MoverDeltaFixture fixture, FalloutActorLogicalSceneSnapshot? saved,
            bool failRetain, bool failBinding = true)
        {
            var model = PlayerOpacityContracts.CreateReferenceColdModel(); Graph = model.ConstructLogicalScene(saved);
            Node = new(new(fixture.Scene.Process, Graph.Factory, 201), Graph,
                () =>
                {
                    fixture.RequireLife();
                    // Constructor, supplied-pointer validation, then the real
                    // Retain entry. This failure occurs before its increment.
                    if (++_reads == 3 && failRetain) throw new IOException("authored-retain-entry-after-pointer-store");
                }, () => ++SourceReleases,
                () =>
                {
                    fixture.RequireLife(); ++BindingCalls;
                    Graph.Retire(); // Real logical teardown commits before the following failure.
                    if (failBinding && BindingCalls == 1)
                        throw new InvalidOperationException("authored-final-binding-failed-after-graph-retirement");
                }, fixture.RequireLife);
        }
    }
}
