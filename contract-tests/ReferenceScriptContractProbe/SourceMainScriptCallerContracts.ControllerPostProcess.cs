using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    private static void ControllerPostProcessConsumers()
    {
        using var fixture = new ReferenceScriptInitializationFixture();
        var source = FalloutEarlyControllerSource.Reviewed(fixture.FieldSource, fixture.ThreadContext.Source);
        var sceneSource = FalloutStandalonePlayerSceneSource.Read(source.Fields.Controls.Main);
        var constructor = Guid.NewGuid(); var player = fixture.ThreadContext.Process; var runtime = Guid.NewGuid();
        var common = Guid.NewGuid(); var controller = Guid.NewGuid(); var main = Guid.NewGuid();
        var lineage = new FalloutEarlyControllerLineage(source.Identity, fixture.Stack, Guid.NewGuid(), common, runtime,
            player, fixture.ThreadContext.Factory, fixture.Actor, 1, FalloutDetectionProcessLevel.High, constructor, Environment.CurrentManagedThreadId);
        var process = new FalloutProcessSceneQuery(common, fixture.Stack, source.Fields.Controls.Main.EngineSha256,
            fixture.Actor, 1, FalloutDetectionProcessLevel.High, Guid.NewGuid(), 0, null, null);
        var query = new FalloutProcessSceneContextQuery(process, controller);
        var invocation = new FalloutControllerPostProcessInvocation(main, 1, player, runtime, constructor,
            Environment.CurrentManagedThreadId, query, 0);
        var fields = FalloutControllerPostProcessFields.Constructed;
        var order = new List<FalloutControllerPostProcessField>(); var referenceCalls = 0;
        var first = FalloutPlayerControllerContextConsumers.Execute(invocation, Read,
            () => { ++referenceCalls; throw new InvalidOperationException("Equal bodies must not enter node/reference conversion."); }, _ => { });
        Require(first.Returned && first.ComparedEqual && first.Fields.Count == 2 && !first.NullObjectReferenceCalled && referenceCalls == 0 &&
            order.SequenceEqual(new[] { FalloutControllerPostProcessField.FirstBody, FalloutControllerPostProcessField.SecondBody }),
            "A genuine nonnull controller lost its independent equal-null body arm or executed skipped node consumers.");
        order.Clear(); invocation = invocation with { QueryOrdinal = 1 };
        var graph = new ControllerNullReferenceGraph(sceneSource, player);
        var second = FalloutPlayerControllerContextConsumers.Execute(invocation, Read, () =>
        {
            ++referenceCalls; return FalloutSourceSceneReferenceQuery.Read(graph, null);
        }, _ => { });
        Require(second.Returned && second.NullObjectReferenceCalled && second.Fields.Count == 1 && referenceCalls == 1 &&
            graph.ForcedFirstPersonReads == 1 && order.SequenceEqual(new[] { FalloutControllerPostProcessField.Object }),
            "Null object conversion skipped its real forced-first-person getter or read an independent body/model field.");
        order.Clear(); FalloutPlayerControllerContextTrace? failed = null;
        Reject(() => FalloutPlayerControllerContextConsumers.Execute(invocation, Read,
            () => throw new InvalidOperationException("authored-returned-field-next-reference-child-failure"), actual => failed = actual));
        Require(failed is { Returned: false, NullObjectReferenceCalled: true, Fields.Count: 1 } &&
            failed.Fields[0].Returned && failed.Fields[0].Failure is null &&
            failed.Failure == "authored-returned-field-next-reference-child-failure",
            "A failed converter erased its actually returned controller field prefix or fabricated parent return.");
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            fields = JsonSerializer.Deserialize<FalloutControllerPostProcessFields>(JsonSerializer.Serialize(fields))!;
            FalloutEarlyController.ValidatePostProcessFields(fields);
            Require(fields == FalloutControllerPostProcessFields.Constructed,
                "Cold controller fields replayed a body/object writer or changed their independent constructor values.");
        }
        Reject(() => FalloutEarlyController.ValidatePostProcessFields(fields with { FirstBody = Guid.NewGuid() }));
        Reject(() => FalloutEarlyController.ValidatePostProcessFields(fields with { Object = Guid.NewGuid() }));
        Reject(() => FalloutEarlyController.ValidatePostProcessFields(fields with { SecondBodyBoundary = " " }));
        FalloutEarlyController.ValidatePostProcessFields(fields with { FirstBodyBoundary = "authored-first-body-writer-unowned" });
        FalloutEarlyController.RequireCompletePostProcessFields(fields);
        Reject(() => FalloutEarlyController.RequireCompletePostProcessFields(fields with
        { FirstBodyBoundary = "authored-first-body-writer-unowned" }));
        ControllerPostActionConsumers(invocation, lineage, sceneSource, player);

        FalloutControllerPostProcessRead Read(FalloutControllerPostProcessInvocation actual, FalloutControllerPostProcessField selected,
            Action<FalloutControllerPostProcessRead> observe)
        {
            Require(actual == invocation && actual.Context.Context == controller, "Authored controller reader escaped its actual Main/context.");
            order.Add(selected);
            var value = selected switch
            {
                FalloutControllerPostProcessField.FirstBody => fields.FirstBody,
                FalloutControllerPostProcessField.SecondBody => fields.SecondBody,
                FalloutControllerPostProcessField.Object => fields.Object,
                _ => throw new InvalidDataException("Authored context selected no field."),
            };
            var result = new FalloutControllerPostProcessRead(actual, lineage, controller, selected, value, true, null);
            observe(result); return result;
        }
    }
    private sealed class ControllerNullReferenceGraph(FalloutStandalonePlayerSceneSource source, Guid process) : IFalloutSourceSceneReferenceGraph
    {
        public FalloutStandalonePlayerSceneSource Source => source;
        public Guid Process => process;
        internal int ForcedFirstPersonReads;
        public void RequireCurrent() { }
        public FalloutSourceSceneNode? ReadForcedFirstPerson()
        { ++ForcedFirstPersonReads; return new(process, Guid.NewGuid(), 1); }
        public FalloutFormKey ReadPlayerReference() => throw new InvalidOperationException("Null object query cannot read an absent Player mapping.");
        public FalloutSourceSceneReference? ReadNodeReference(FalloutSourceSceneNode node) => throw new InvalidOperationException("Null object query cannot inspect a native node.");
        public FalloutSourceSceneNode? ReadParent(FalloutSourceSceneNode node) => throw new InvalidOperationException("Null object query cannot walk an absent parent.");
    }
}
