using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourceSceneReferenceQueryContracts()
    {
        var graph = new AuthoredSceneReferenceGraph();
        Require(FalloutSourceSceneReferenceQuery.Read(graph, null) is null && graph.Reads.SequenceEqual(new[] { "first" }),
            "Null source input skipped the mandatory forced Player getter.");
        graph.Reads.Clear(); graph.FailFirst = true;
        Reject(() => FalloutSourceSceneReferenceQuery.Read(graph, null)); graph.FailFirst = false;
        var first = graph.Node(); graph.First = first;
        Require(FalloutSourceSceneReferenceQuery.Read(graph, first) == graph.Player && !graph.Reads.Contains("node"),
            "Actual first-person root did not return Player before entering a node virtual.");
        var outer = graph.Node(); var leaf = graph.Node();
        var reference = new FalloutFormKey("authored.esm", 0x20);
        graph.Nodes.Add(leaf, (outer, null)); graph.Nodes.Add(outer, (null, new(reference, null)));
        Require(FalloutSourceSceneReferenceQuery.Read(graph, leaf) == reference,
            "Source query did not walk the real parent after the leaf's null node-reference return.");
        var inner = graph.Node(); var innerReference = new FalloutFormKey("authored.esm", 0x21);
        graph.Nodes.Add(inner, (outer, new(innerReference, FalloutSourceSceneReferenceQuery.ParentPreferredBaseKind)));
        graph.Reads.Clear();
        Require(FalloutSourceSceneReferenceQuery.Read(graph, inner) == reference && graph.Reads.Count(read => read == "first") == 2,
            "Special source base kind lost recursive parent preference or skipped its second forced getter.");
        graph.Nodes[inner] = (null, new(innerReference, FalloutSourceSceneReferenceQuery.ParentPreferredBaseKind));
        Require(FalloutSourceSceneReferenceQuery.Read(graph, inner) == innerReference,
            "Null recursive parent result erased the source's original reference fallback.");
        graph.Nodes[leaf] = (leaf, null); Reject(() => FalloutSourceSceneReferenceQuery.Read(graph, leaf));
        Reject(() => FalloutSourceSceneReferenceQuery.Read(graph, first with { Process = Guid.NewGuid() }));

        var scene = new FalloutStandalonePlayerSceneState(graph.Source, "authored-source-query", Guid.NewGuid());
        var native = new AuthoredPlayerSceneObject(); var owned = scene.PublishFirstPerson(native.Publication(scene), native.ReadLiving);
        var before = scene.Capture();
        var cold = new FalloutStandalonePlayerSceneState(graph.Source, before.Stack, Guid.NewGuid(), before);
        var coldMain = Guid.NewGuid(); cold.StoreSceneClock(coldMain, 1, 0x3c800000); cold.EnterSceneVirtual(coldMain, 1);
        Reject(() => cold.ExecuteInitialVirtualPrefix(coldMain, 1, () => { }));
        Require(cold.Capture().ReferenceQuery is { GetterReturned: false, Returned: false } &&
            cold.Capture().Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.ReferenceQuery,
            "Null query input bypassed the failed getter of an unrebound historical first-person field.");
        var failedGetter = cold.Capture();
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failedGetter with { ReferenceQuery = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failedGetter with
        { ReferenceQuery = failedGetter.ReferenceQuery! with { FirstPerson = new(cold.Process, Guid.NewGuid(), 1) } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failedGetter with
        { ReferenceQuery = failedGetter.ReferenceQuery! with { GetterReturned = true } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failedGetter with
        {
            ReferenceQuery = failedGetter.ReferenceQuery! with { GetterReturned = true, Returned = true, Failure = null },
            Prelude = failedGetter.Prelude! with { FailedAt = FalloutStandaloneScenePreludeStep.ReferenceFrame }
        }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failedGetter with
        { ReferenceQuery = failedGetter.ReferenceQuery! with { Process = before.CapturedProcess } }));
        cold.Retire();
        var reboundScene = new FalloutStandalonePlayerSceneState(graph.Source, before.Stack, Guid.NewGuid(), before);
        var reboundNative = new AuthoredPlayerSceneObject();
        var reboundOwner = reboundScene.PublishFirstPerson(reboundNative.Publication(reboundScene), reboundNative.ReadLiving);
        var reboundMain = Guid.NewGuid(); reboundScene.StoreSceneClock(reboundMain, 1, 0x3c800000); reboundScene.EnterSceneVirtual(reboundMain, 1);
        Reject(() => reboundScene.ExecuteInitialVirtualPrefix(reboundMain, 1, () => { }));
        var reboundQuery = reboundScene.Capture();
        Require(reboundQuery.ReferenceQuery is { GetterReturned: true, Returned: true } &&
            reboundQuery.ReferenceQuery.FirstPerson?.Factory == reboundOwner.Publication.Factory,
            "A genuine cold factory replacement did not own the forced getter.");
        FalloutStandalonePlayerSceneState.Validate(reboundQuery with { FirstPersonHistory = reboundQuery.FirstPersonHistory.Reverse().ToArray() });
        reboundScene.ReleaseFirstPerson(reboundOwner); reboundScene.Retire(); reboundNative.Retire();
        var releasedScene = new FalloutStandalonePlayerSceneState(graph.Source, before.Stack, Guid.NewGuid(), before);
        var releasedNative = new AuthoredPlayerSceneObject();
        var releasedOwner = releasedScene.PublishFirstPerson(releasedNative.Publication(releasedScene), releasedNative.ReadLiving);
        releasedScene.ReleaseFirstPerson(releasedOwner);
        var releasedMain = Guid.NewGuid(); releasedScene.StoreSceneClock(releasedMain, 1, 0x3c800000); releasedScene.EnterSceneVirtual(releasedMain, 1);
        Reject(() => releasedScene.ExecuteInitialVirtualPrefix(releasedMain, 1, () => { }));
        Require(releasedScene.Capture().ReferenceQuery is { GetterReturned: true, Returned: true, FirstPerson: null },
            "Releasing the latest cold field resurrected an older unreleased historical factory.");
        releasedScene.Retire(); releasedNative.Retire();
        var main = Guid.NewGuid(); scene.StoreSceneClock(main, 1, 0x3c800000); scene.EnterSceneVirtual(main, 1);
        var releaseCount = 0; Reject(() => scene.ExecuteInitialVirtualPrefix(main, 1, () => releaseCount++));
        var stopped = scene.Capture();
        Require(releaseCount == 1 && stopped.ReferenceQuery is { Returned: true, GetterReturned: true, Result: null } query &&
            query.FirstPerson == new FalloutSourceSceneNode(scene.Process, owned.Publication.Factory, owned.Publication.NativeRoot) &&
            stopped.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.ReferenceFrame && stopped.SceneBracket == 1,
            "Actual scene prefix did not use its same living forced-first-person field before the independent HUD refusal.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        { ReferenceQuery = stopped.ReferenceQuery! with { FirstPerson = null } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        { ReferenceQuery = stopped.ReferenceQuery! with { GetterReturned = false } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with { ReferenceQuery = null }));
        var overlap = owned with
        {
            Identity = Guid.NewGuid(), Stored = owned.Stored + 1,
            Publication = owned.Publication with { Factory = Guid.NewGuid() }
        };
        Reject(() => FalloutStandalonePlayerSceneState.Validate(stopped with
        {
            FirstPersonHistory = [owned, overlap],
            ReferenceQuery = stopped.ReferenceQuery! with
            { FirstPerson = new(scene.Process, overlap.Publication.Factory, overlap.Publication.NativeRoot) }
        }));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(stopped));
        Reject(() => scene.ExecuteInitialVirtualPrefix(main, 1, () => releaseCount++));
        Require(releaseCount == 1, "Failed HUD prefix replayed the actual shared-child release.");
        scene.ReleaseFirstPerson(owned);
        Require(scene.Capture().ReferenceQuery == stopped.ReferenceQuery,
            "Independent native release erased or rebound the historical query's returned root.");
        scene.Retire(); native.Retire();
        Console.WriteLine("OPENNV_FO3_SOURCE_SCENE_REFERENCE_QUERY_PASS authoredOnly=true forcedGetterBeforeNull=true " +
            "parentWalk=true specialParentPreference=true actualScenePrefix=true separateHudRefusal=true nativeGameplay=UNEXECUTED");
    }

    private sealed class AuthoredSceneReferenceGraph : IFalloutSourceSceneReferenceGraph
    {
        public FalloutStandalonePlayerSceneSource Source { get; } = FalloutStandalonePlayerSceneSource.Read(
            FalloutMainScriptCallerSource.Read(new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3,
                new('1', 64), FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3))));
        public Guid Process { get; } = Guid.NewGuid();
        internal readonly FalloutFormKey Player = new("authored.esm", 0x14);
        internal FalloutSourceSceneNode? First;
        internal bool FailFirst;
        internal readonly List<string> Reads = [];
        internal readonly Dictionary<FalloutSourceSceneNode, (FalloutSourceSceneNode? Parent, FalloutSourceSceneReference? Reference)> Nodes = [];
        private ulong _next;
        internal FalloutSourceSceneNode Node() => new(Process, Guid.NewGuid(), ++_next);
        public void RequireCurrent() { }
        public FalloutSourceSceneNode? ReadForcedFirstPerson()
        { Reads.Add("first"); if (FailFirst) throw new NotSupportedException("authored actual getter failure"); return First; }
        public FalloutFormKey ReadPlayerReference() { Reads.Add("player"); return Player; }
        public FalloutSourceSceneReference? ReadNodeReference(FalloutSourceSceneNode node) { Reads.Add("node"); return Nodes[node].Reference; }
        public FalloutSourceSceneNode? ReadParent(FalloutSourceSceneNode node) { Reads.Add("parent"); return Nodes[node].Parent; }
    }
}
