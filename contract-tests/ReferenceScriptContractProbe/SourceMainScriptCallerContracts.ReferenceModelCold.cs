using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferenceModelCold()
    {
        T CopyModel<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
            ?? throw new InvalidDataException("Authored model cold value vanished.");
        FalloutReferenceParentCellRead Parent(MoverDeltaFixture fixture) => new("authored-model-ParentCELL",
            fixture.Stack, Guid.NewGuid(), Guid.NewGuid(), fixture.Scene.Constructor, fixture.Actor, 0, null,
            fixture.Source.Controller.Fields.Controls.Main.EngineSha256);
        var releases = 0;
        FalloutReferenceModelDataNode Node(MoverDeltaFixture fixture, FalloutActorLogicalScene graph, ulong authoredNativeId)
        {
            var released = false;
            return new(new(fixture.Scene.Process, graph.Factory, authoredNativeId), graph,
                () => { fixture.RequireLife(); if (released) throw new InvalidOperationException("Authored bridge already released its model."); },
                () => { released = true; releases++; });
        }
        using var first = new MoverDeltaFixture(); first.AllocateData();
        var model = PlayerOpacityContracts.CreateReferenceColdModel(); var logical = model.ConstructLogicalScene();
        logical.SetAlpha(Guid.NewGuid(), BitConverter.SingleToUInt32Bits(.25f));
        logical.PublishLocalPose(new(0, 2), FalloutNifSceneMath.Identity with { Translation = new(3, 0, 0) });
        logical.UpdateWorldAndBounds(); logical.PublishMaterialAlpha(new(0, 7), BitConverter.SingleToUInt32Bits(.875f));
        var node = Node(first, logical, 1);
        first.DataCall(FalloutReferenceModelDataIngress.ModelSetter, invocation => first.Data.StoreReturnedModel(invocation, Parent(first), node));
        var saved = CopyModel(first.Data.Capture());
        var oldRead = first.ReadFrameLeaf(frame => first.Data.ReadForMover(frame, first.RequireFrame));
        FalloutReferenceModelDataState.ValidateReads(saved, [oldRead]);
        Require(saved.ModelScene is not null && saved.ModelScene.Objects.Count == 5 && saved.ModelScene.Properties.Count == 3,
            "Nonnull source model snapshot omitted its full reader-owned graph.");
        Reject(() => FalloutReferenceModelDataState.Validate(saved with { ModelScene = null }));
        var originalCalls = saved.Calls.Count; var prior = saved;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new MoverDeltaFixture(CopyModel(prior));
            var beforeRelease = releases; var prepared = cold.Data.PreparedColdModel ??
                throw new InvalidDataException("Cold nonnull source model omitted its prepared actual graph.");
            Reject(() => cold.AllocateData());
            Require(cold.Data.Capture().Calls.Count == originalCalls && releases == beforeRelease,
                "Unbound cold source model replayed a 3D constructor or released an absent native lease.");
            var freshModel = PlayerOpacityContracts.CreateReferenceColdModel();
            var freshGraph = freshModel.ConstructLogicalScene(CopyModel(prepared));
            FalloutActorLogicalScene.RequireColdRestoration(prepared, freshGraph.Capture());
            var fresh = Node(cold, freshGraph, checked((ulong)cycle + 10));
            cold.DataCall(FalloutReferenceModelDataIngress.ColdBinding, invocation => cold.Data.BindColdModel(invocation, fresh));
            var bound = CopyModel(cold.Data.Capture()); FalloutReferenceModelDataState.Validate(bound);
            var bindings = bound.ColdBindings ?? throw new InvalidDataException("Cold model omitted binding history.");
            var capturedGraph = bound.ModelScene ?? throw new InvalidDataException("Cold model omitted its complete current graph.");
            Require(bound.Calls.Count == originalCalls && bindings.Count == cycle + 1 && bound.Fields!.Model == fresh.Node &&
                capturedGraph.Factory == freshGraph.Factory && capturedGraph.RestoredLifetime == prepared.Lifetime &&
                capturedGraph.Sequence == prepared.Sequence + 1 && releases == beforeRelease && cold.Data.PreparedColdModel is null,
                "Cold model replayed a source setter, released old native state, or lost its current complete graph binding.");
            // A historical prior-process read must remain valid even though the
            // latest field now has a different real process/model identity.
            Require(bound.Fields != oldRead.Fields, "Authored cold exercise failed to change the actual pointer binding.");
            FalloutReferenceModelDataState.ValidateReads(bound, [CopyModel(oldRead)]);
            var current = cold.ReadFrameLeaf(frame => cold.Data.ReadForMover(frame, cold.RequireFrame));
            FalloutReferenceModelDataState.ValidateReads(bound, [current]);
            Reject(() => FalloutReferenceModelDataState.ValidateReads(bound, [current with { Factory = Guid.NewGuid() }]));
            Reject(() => FalloutReferenceModelDataState.ValidateReads(bound, [oldRead with { Fields = oldRead.Fields! with { ScalarBits = 0 } }]));
            Reject(() => FalloutReferenceModelDataState.Validate(bound with { ColdBindings = bindings.Take(cycle).ToArray() }));
            var changed = bindings.ToArray(); changed[^1] = changed[^1] with { ModelRetained = false };
            Reject(() => FalloutReferenceModelDataState.Validate(bound with { ColdBindings = changed }));
            Reject(() => FalloutReferenceModelDataState.Validate(bound with { ModelScene = capturedGraph with { Lifetime = Guid.NewGuid() } }));
            cold.DataCall(FalloutReferenceModelDataIngress.ColdBinding, invocation =>
                Reject(() => cold.Data.BindColdModel(invocation, fresh)));
            // Later genuine graph writers may change poses/material state. A
            // future cold rebind preserves that state, not its old initial pose.
            freshGraph.PublishMaterialAlpha(new(0, 7), BitConverter.SingleToUInt32Bits(.5f + cycle / 8f));
            prior = CopyModel(cold.Data.Capture());
            var retainedGraph = prior.ModelScene ?? throw new InvalidDataException("Later graph writer lost its current model scene.");
            Require(retainedGraph.Properties.Single(property => property.Key == new FalloutNifSceneKey(0, 7)).MaterialStores == cycle + 2,
                "Cold model lost its genuine later logical property writer.");
            // Supply the same valid full graph under changed source resources:
            // the real constructor must reject before any native binding.
            Reject(() => PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene(retainedGraph with
            { Assets = retainedGraph.Assets.Select((asset, index) => index == 0 ? asset with { Sha256 = new string('0', 64) } : asset).ToArray() }));
        }
        Require(releases == 3, "Three fresh model owners did not retire their actual retained leases exactly once.");
        using (var stopped = new MoverDeltaFixture(CopyModel(prior)))
        {
            var unrestored = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene();
            var attempted = Node(stopped, unrestored, 90);
            stopped.DataCall(FalloutReferenceModelDataIngress.ColdBinding, invocation =>
                Reject(() => stopped.Data.BindColdModel(invocation, attempted)));
            var failed = CopyModel(stopped.Data.Capture());
            Require(failed.Failure is not null && failed.Fields!.Model == prior.Fields!.Model && failed.Calls.Count == originalCalls &&
                failed.ColdBindings![^1] is { Returned: false, ModelStored: false, ModelRetained: false } && releases == 3,
                "Failed cold graph restoration invented model adoption/release or discarded its genuine prefix.");
            stopped.ReadFrameLeaf(frame => { Reject(() => stopped.Data.ReadForMover(frame, stopped.RequireFrame)); return true; });
            unrestored.Retire();
        }
        RunReferenceModelFailureBindings(prior);
        Console.WriteLine("OPENNV_REFERENCE_MODEL_COLD_PASS actualAuthoredNifGraph=true nonnullModel=true freshGraph3=true fieldsAndMaterialsPreserved=true " +
            "noSourceCtorOrSetterReplay=true noOldNativeRelease=true historicalReadJoin=true tamperRefusal=true nativeFactoryAndCampaignContinue=UNEXECUTED");
    }

    private static void RunReferenceModelFailureBindings(FalloutReferenceModelDataSnapshot saved)
    {
        foreach (var cold in new[] { false, true })
        {
            using var fixture = new MoverDeltaFixture(cold ? saved : null);
            if (!cold) fixture.AllocateData();
            var graph = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene(fixture.Data.PreparedColdModel);
            var reads = 0; var releases = 0; var retirements = 0; var bound = true;
            var node = new FalloutReferenceModelDataNode(new(fixture.Scene.Process, graph.Factory, cold ? 102UL : 101UL), graph,
                () => { fixture.RequireLife(); if (++reads == 3) throw new IOException("Actual authored retain entry fails after pointer store."); },
                () => releases++, () => { if (!bound) throw new InvalidOperationException("Binding retired twice."); bound = false; retirements++; });
            var ingress = cold ? FalloutReferenceModelDataIngress.ColdBinding : FalloutReferenceModelDataIngress.ModelSetter;
            fixture.DataCall(ingress, invocation => Reject(() =>
            {
                if (cold) fixture.Data.BindColdModel(invocation, node);
                else fixture.Data.StoreReturnedModel(invocation, new("authored-model-ParentCELL", fixture.Stack,
                    Guid.NewGuid(), Guid.NewGuid(), fixture.Scene.Constructor, fixture.Actor, 0, null,
                    fixture.Source.Controller.Fields.Controls.Main.EngineSha256), node);
            }));
            var failed = fixture.Data.Capture();
            Require(failed.Failure is not null && failed.Fields!.Model == node.Node && bound && releases == 0,
                "Failed retain lost the actual committed model or fabricated a source release.");
            var preserved = false; var freed = false;
            fixture.Data.CleanupUnpublishedModel(node.Node, () => preserved = true, () => { freed = true; graph.Retire(); });
            Require(preserved && !freed && fixture.Data.Capture().ModelScene?.Factory == graph.Factory,
                "Unpublished assembly cleanup destroyed its actually adopted failed-prefix graph.");
            fixture.Data.Retire(); fixture.Data.Retire();
            Require(!bound && retirements == 1 && releases == 0,
                "Zero-retain binding leaked or retirement invented a source reference release.");
            Reject(node.RequireCurrent); graph.Retire();
        }

        using var owner = new MoverDeltaFixture(); owner.AllocateData();
        var adoptedGraph = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene();
        var bindingRetired = 0; var sourceReleased = 0; var presentationFailed = false;
        var adopted = new FalloutReferenceModelDataNode(new(owner.Scene.Process, adoptedGraph.Factory, 103), adoptedGraph,
            () => { owner.RequireLife(); if (presentationFailed) throw new InvalidOperationException("Actual authored presentation failed."); },
            () => sourceReleased++, () => bindingRetired++, owner.RequireLife);
        owner.DataCall(FalloutReferenceModelDataIngress.ModelSetter, invocation => owner.Data.StoreReturnedModel(invocation,
            new("authored-model-ParentCELL", owner.Stack, Guid.NewGuid(), Guid.NewGuid(), owner.Scene.Constructor,
                owner.Actor, 0, null, owner.Source.Controller.Fields.Controls.Main.EngineSha256), adopted));
        var kept = false; var destroyed = false;
        try { throw new IOException("Authored later assembly child fails after successful source model store."); }
        catch (IOException)
        {
            owner.Data.CleanupUnpublishedModel(adopted.Node, () => kept = true, () => { destroyed = true; adoptedGraph.Retire(); });
        }
        Require(kept && !destroyed && owner.Data.Capture().ModelScene?.Factory == adoptedGraph.Factory,
            "Later assembly failure freed a successfully retained source model.");
        var otherGraph = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene(); var otherFreed = false;
        owner.Data.CleanupUnpublishedModel(new(owner.Scene.Process, otherGraph.Factory, 104),
            () => throw new InvalidOperationException("An unadopted graph acquired source ownership."),
            () => { otherFreed = true; otherGraph.Retire(); });
        Require(otherFreed && owner.Data.Capture().Fields!.Model == adopted.Node,
            "Unadopted cleanup altered the actual source-owned graph.");
        adoptedGraph.RetainUnownedPose("authored actual later logical animation failure");
        presentationFailed = true;
        Reject(adopted.RequireCurrent);
        owner.Data.Retire(); owner.Data.Retire();
        Require(sourceReleased == 1 && bindingRetired == 1, "Returned source retain or native binding retired more than once.");
        Require(adoptedGraph.Capture().Failure == "authored actual later logical animation failure",
            "Retirement erased the actual logical failure while releasing its real binding.");
        adoptedGraph.Retire();
        Console.WriteLine("OPENNV_REFERENCE_MODEL_FAILURE_LIFETIME_PASS warmAndColdStoredBeforeRetain=true noFalseSourceRelease=true " +
            "laterAssemblyFailurePreserved=true unadoptedGraphRetired=true bindingRetiredOnce=true nativeAssembly=UNEXECUTED");
    }
}

internal static partial class PlayerOpacityContracts
{
    internal static FalloutActorSourceModel CreateReferenceColdModel() => Model();
}
