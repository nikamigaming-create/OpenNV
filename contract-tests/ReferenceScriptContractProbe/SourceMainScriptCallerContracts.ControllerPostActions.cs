using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    private static void ControllerPostActionConsumers(FalloutControllerPostProcessInvocation previous,
        FalloutEarlyControllerLineage lineage, FalloutStandalonePlayerSceneSource source, Guid player)
    {
        var invocation = previous with { QueryOrdinal = 0, SourceScope = FalloutControllerPlayerSourceScope.PostActions };
        var graph = new ControllerNullReferenceGraph(source, player);
        var bodyReads = 0; var flagsReads = 0;
        var first = FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, ReadFlags,
            () => FalloutSourceSceneReferenceQuery.Read(graph, null), _ => { });
        Require(first is { Returned: true, RemoveEight: true, ClearContextFlag: false, NullBodyReferenceCalled: true, Flags: null } &&
            first.Body is { Returned: true, Field: FalloutControllerPostProcessField.FirstBody, Value: null } &&
            graph.ForcedFirstPersonReads == 1 && bodyReads == 1 && flagsReads == 0,
            "Later nonnull context skipped the genuine first-body null converter before action eight removal.");
        FalloutPlayerControllerPostActionsTrace? failure = null;
        Reject(() => FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, ReadFlags,
            () => throw new InvalidOperationException("authored-post-action-converter-failure"), actual => failure = actual));
        Require(failure is { Returned: false, RemoveEight: false, NullBodyReferenceCalled: true, Body.Returned: true } &&
            failure.Failure == "authored-post-action-converter-failure",
            "Post-action failed conversion erased a returned body or invented the following action removal.");
        Reject(() => FalloutPlayerControllerContextConsumers.ExecutePostActions(previous, ReadBody, ReadFlags,
            () => throw new InvalidOperationException("Wrong source scope cannot execute conversion."), _ => { }));
        Require(bodyReads == 2 && flagsReads == 0, "Post-action scope borrowed an earlier getter invocation.");
        invocation = invocation with { QueryOrdinal = 1 };
        var bits = FalloutEarlyControllerSource.HelperAlwaysFlag | FalloutEarlyControllerSource.HelperConditionalFlag;
        var second = FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, ReadCurrentFlags,
            () => throw new InvalidOperationException("Flags branch must not query body reference."), _ => { });
        Require(second is { Returned: true, ClearContextFlag: true, RemoveEight: false, NullBodyReferenceCalled: false, Body: null } &&
            second.Flags is { Returned: true, Revision: 7 } && second.Flags.Flags == bits && bodyReads == 2 && flagsReads == 1,
            "Post-action flags read replaced the real full helper flag word with a new Boolean/null field.");
        bits |= FalloutEarlyControllerSource.PostActionContextFlag;
        failure = null;
        Reject(() => FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, ReadCurrentFlags,
            () => throw new InvalidOperationException("True flags still cannot enter body query."), actual => failure = actual));
        Require(failure is { Returned: false, ClearContextFlag: false, Flags.Returned: true } && failure.Flags.Flags == bits,
            "Reached notification branch lost its real full flags or forged the independent byte/effect return.");
        failure = null;
        Reject(() => FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, (actual, observe) =>
        {
            observe(new(actual, lineage, actual.Context.Context!.Value, null, 7, false, "authored-flags-reader-failure"));
            throw new InvalidOperationException("authored-flags-reader-failure");
        }, () => null, actual => failure = actual));
        Require(failure is { Returned: false, ClearContextFlag: false, Flags: { Returned: false, Flags: null, Revision: 7 } } &&
            failure.Failure == "authored-flags-reader-failure", "Failed flags read omitted its actual entered partial receipt.");
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var cold = JsonSerializer.Deserialize<FalloutPlayerControllerPostActionsTrace>(JsonSerializer.Serialize(second))!;
            Require(cold == second && cold.Flags!.Invocation.SourceScope == FalloutControllerPlayerSourceScope.PostActions,
                "Cold post-action trace lost its independent source scope/full flags/revision or replayed a consumer.");
            second = cold;
        }
        Reject(() => FalloutPlayerControllerContextConsumers.ExecutePostActions(invocation, ReadBody, (actual, observe) =>
        {
            var foreign = new FalloutControllerPostProcessFlagsRead(actual, lineage with { RuntimeProcess = Guid.NewGuid() },
                actual.Context.Context!.Value, 0, 7, true, null);
            observe(foreign); return foreign;
        }, () => null, _ => { }));
        Reject(() => FalloutPlayerControllerContextConsumers.Execute(invocation, ReadBody, () => null, _ => { }));

        FalloutControllerPostProcessRead ReadBody(FalloutControllerPostProcessInvocation actual,
            FalloutControllerPostProcessField field, Action<FalloutControllerPostProcessRead> observe)
        {
            Require(actual == invocation && field == FalloutControllerPostProcessField.FirstBody,
                "Post-action selected another current controller/body scope.");
            ++bodyReads; var result = new FalloutControllerPostProcessRead(actual, lineage, actual.Context.Context!.Value, field, null, true, null);
            observe(result); return result;
        }
        FalloutControllerPostProcessFlagsRead ReadFlags(FalloutControllerPostProcessInvocation actual,
            Action<FalloutControllerPostProcessFlagsRead> observe) => throw new InvalidOperationException("First post-action query cannot read flags.");
        FalloutControllerPostProcessFlagsRead ReadCurrentFlags(FalloutControllerPostProcessInvocation actual,
            Action<FalloutControllerPostProcessFlagsRead> observe)
        {
            Require(actual == invocation && actual.QueryOrdinal == 1, "Post-action flags selected an earlier body query scope.");
            ++flagsReads; var result = new FalloutControllerPostProcessFlagsRead(actual, lineage, actual.Context.Context!.Value, bits, 7, true, null);
            observe(result); return result;
        }
    }
}
