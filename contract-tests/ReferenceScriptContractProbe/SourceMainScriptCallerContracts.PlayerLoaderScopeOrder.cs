using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerLoaderScopeOrderContracts()
    {
        using var fixture = ReferencePositionFixture.CreatePlayerLoadScopeRecords();
        var fields = fixture.Source.Fields;
        var tail = FalloutPlayerReturnTailSource.Reviewed(FalloutPlayerActorMovementSource.Reviewed(
            FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(fields, fixture.Thread.Source)),
            FalloutInitialPlacementCellsSource.Reviewed(fixture.Source), FalloutPlayerWorldOrientationSource.Reviewed(fields.Controls)),
            FalloutStandalonePlayerSceneSource.Read(fields.Controls.Main));
        var source = AuthoredLoadedAnimation(tail);
        fixture.Initialize();
        FalloutReferenceModelDataInvocation? current = new(Guid.NewGuid(), fixture.Actor, fixture.Thread.Process,
            fixture.Fields.Constructor, fixture.Fields.Process, 1, Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
        var invocation = current ?? throw new InvalidOperationException("Authored source load was not entered.");
        void Guard(FalloutReferenceModelDataInvocation actual)
        { if (current != actual) throw new InvalidOperationException("Authored loader phase left its actual invocation."); }
        var facade = new FalloutPlayerLoadedAnimationState(source, fixture.Records, fixture.Fields, fixture.Common,
            fixture.Initializer, fixture.Thread, Guard, () => fixture.Records.RequireLiveLoadReceipt(fixture.Records.LoadReceipt));
        facade.EnterPlayerLoad(invocation);
        var entered = facade.Capture();
        Require(entered.Scope is { Scope.EnteredKind: 52, Scope.Returned: false } && entered.FirstSkeleton is null &&
            entered.FirstPointer is null && fixture.Fields.Capture().AnimationTail is null,
            "TLS52 entry eagerly read a later skeleton or invalidated the first context before the gates.");
        var gate = fixture.Changes.ReadPlayerModelLoadFlags(invocation,
            new(AuthoredModelContinuation(tail), fixture.Source,
                new(new("sCharGenQuest:General", FalloutIniCollection.Main, 0), "00000880"), FalloutPlayerModelLoadPrerequisiteSource.ContractDigest), Guard);
        Require(gate.Flags == 1 && facade.Capture().Scope is { Scope.EnteredKind: 52, Scope.Returned: false },
            "Actual prerequisite flags ran before the entered source kind or restored an unreturned scope.");
        var skeleton = facade.ReadFirstSkeletonForPlayerLoad(invocation);
        Require(skeleton.Value == "meshes/authored/opacity-skeleton.nif" &&
            fixture.Fields.Capture().AnimationTail is null,
            "Current first-skeleton read fabricated a first AnimData writer.");
        var model = PlayerOpacityContracts.CreateReferenceColdModel(); var graph = model.ConstructLogicalScene();
        var node = new FalloutReferenceModelDataNode(new(invocation.ReferenceProcess, model.Factory, 61), graph,
            () => Guard(invocation), () => throw new InvalidOperationException("Unbound context acquired no source root retain."),
            graph.Retire, () => { });
        facade.ConstructFirst(invocation, node);
        Reject(() => facade.ReturnPlayerLoad(invocation));
        Require(facade.Capture().Scope is { Scope.Returned: false, Scope.EnteredKind: 52 },
            "An attempted early Player return restored its kind before the actual independent context binds.");
        Require(fixture.Fields.Capture().AnimationTail!.LoadWriters.Count == 2 &&
            fixture.Fields.Capture().AnimationTail!.Membership is null &&
            facade.Capture().FirstPointer is { Returned: true } && facade.Capture().Scope is { Scope.Returned: false },
            "Actual first constructor/store missed its entered writer or returned the enclosing loader.");
        FalloutActorPackageCommandFields.Validate(fixture.Fields.Capture(), fixture.Fields.Source, fixture.Fields.Stack, fixture.Actor);
        var auxiliary = fixture.Initializer.ReadControllerConstructorExtra(0x7e, invocation, Guard);
        Require(auxiliary is { Extra: null, Subtype: 0x7e } && auxiliary.Initializer == fixture.Initializer.Factory &&
            auxiliary.Fields == fixture.Fields.Factory, "Controller lookup substituted another list's constructor absence.");
        Reject(() => fixture.Initializer.ReadControllerConstructorExtra(0x0d, invocation, Guard));
        Reject(() => fixture.Initializer.ReadControllerConstructorExtra(0x7e, invocation with { Epoch = 2 }, _ => { }));
        Reject(() => facade.EnterPlayerLoad(invocation));
        facade.Retire(); node.RetireBinding(); current = null;
        fixture.InvalidateControllerExtraMembership();
        current = invocation;
        Reject(() => fixture.Initializer.ReadControllerConstructorExtra(0x7e, invocation, Guard));
        Reject(() => fixture.Initializer.ReadControllerConstructorExtra(0x5f, invocation, Guard));
        current = null;
        Console.WriteLine("OPENNV_PLAYER_LOADER_SCOPE_ORDER_PASS kindBeforeGates=true noEagerSkeleton=true firstWriterAtConstructor=true failedScopeUnrestored=true wholeLoader=UNRETURNED");
    }
    private sealed partial class ReferencePositionFixture
    {
        internal void InvalidateControllerExtraMembership()
        {
            _script = new(Guid.NewGuid(), Actor, Fields.Process, 1, Fields.Constructor, Environment.CurrentManagedThreadId);
            try { _ = Initializer.ObserveUnownedWriter(_script, FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad, "authored-controller-Extra-copy"); }
            finally { _script = null; }
        }
        internal static ReferencePositionFixture CreatePlayerLoadScopeRecords() => new(appendedRows:
            Row("GMST", 0x883, Part("EDID", "sFirstPersonSkeleton\0"u8.ToArray()),
                Part("DATA", "meshes/authored/opacity-skeleton.nif\0"u8.ToArray())));
    }
}
