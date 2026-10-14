using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    private static void EarlyControllerColdOwners()
    {
        using var fixture = new ReferenceScriptInitializationFixture();
        var source = FalloutEarlyControllerSource.Reviewed(fixture.FieldSource, fixture.ThreadContext.Source);
        var inputs = new FalloutPhysicalConstructorInputs(source.Fields.Controls.Main.EngineSha256, new(0, 0));
        var savedContact = new FalloutControllerContactState(new(FalloutPhysicalVector3.FromValues(0, 0, 1), 0),
            FalloutEarlyControllerSource.InitialSlopeBits, FalloutPhysicalConstructorInputs.InitialStepBits).Snapshot;
        var manager = new FalloutCharacterStateManager(); var originalContext = manager.ConstructContext(0);
        var contextHistory = originalContext.Snapshot; originalContext.Dispose(); manager.Retire();
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var coldManager = new FalloutCharacterStateManager();
            var context = coldManager.RebindContext(contextHistory);
            Require(context.Snapshot.Manager != contextHistory.Manager &&
                (context.Snapshot with { Manager = contextHistory.Manager }) == contextHistory &&
                new FalloutControllerContactState(savedContact).Snapshot == savedContact,
                "Cold manager/contact rebind lost actual current fields or reused its old manager identity.");
            contextHistory = context.Snapshot; context.Dispose(); coldManager.Retire();
        }
        var shared = new FalloutEarlyControllerSharedFields(source); shared.ObserveUnownedWriter("center");
        var restoredShared = new FalloutEarlyControllerSharedFields(source); restoredShared.Restore(shared.Capture());
        Reject(() => restoredShared.ReadCenter());
        Require(!restoredShared.ReadHelperFlag() && restoredShared.ReadSlope() == FalloutEarlyControllerSource.InitialSlopeBits &&
            restoredShared.SaveBlocker == "source-standard-controller-center-current-writer-unbound",
            "Cold shared fields lost their independent current-writer barrier.");
        FalloutEarlyControllerFactorySnapshot saved;
        using (var groups = new FalloutCharacterCollisionGroups(inputs))
        {
            var original = new FalloutEarlyControllerFactory(source, inputs, groups, fixture.ThreadContext, .0142875f,
                _ => throw new InvalidOperationException("Empty managed fixture executes no native constructor."),
                _ => throw new InvalidOperationException("Empty managed fixture reads no rendered/reference model."));
            saved = JsonSerializer.Deserialize<FalloutEarlyControllerFactorySnapshot>(JsonSerializer.Serialize(original.Capture()))!;
            original.Retire();
        }
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var thread = new FalloutPlayerThreadContextState(source.Thread, Guid.NewGuid());
            using var groups = new FalloutCharacterCollisionGroups(inputs, saved.Groups);
            var cold = new FalloutEarlyControllerFactory(source, inputs, groups, thread, .0142875f,
                _ => throw new InvalidOperationException("Cold resource binding cannot replay the source constructor callback."),
                _ => throw new InvalidOperationException("Cold resource binding cannot replay its source model callback."), saved);
            try
            {
                thread.RetainColdPhysicsInitializationScopes(saved.PhysicsScopes);
                var invocation = new FalloutEarlyControllerInvocation(Guid.NewGuid(), fixture.Actor, fixture.Stack,
                    Guid.NewGuid(), Guid.NewGuid(), thread.Process, 1,
                    FalloutDetectionProcessLevel.High, Guid.NewGuid(), Environment.CurrentManagedThreadId);
                Reject(() => cold.Construct(invocation, inputs.ProjectPlayer(default, cold.Shared.ReadCenter())));
                var current = cold.Capture();
                Require(current.Factory != saved.Factory && current.Groups == saved.Groups && current.Calls.Count == 0 &&
                    current.Controllers.Count == 0 && current.ShapeCalls.Count == 0 && current.ColdShapeCalls.Count == 0 &&
                    current.ColdBindings.Count == 0 && current.PhysicsScopes.Count == 0 && thread.Entry is null && cold.SaveBlocker is null,
                    "Cold empty factory replayed constructor/group/thread/native stores.");
                FalloutEarlyControllerFactory.ValidateColdFactory(current, source, inputs);
                Reject(() => FalloutEarlyControllerFactory.ValidateColdFactory(current with
                    { Groups = current.Groups with { Counter = 0 } }, source, inputs));
                saved = JsonSerializer.Deserialize<FalloutEarlyControllerFactorySnapshot>(JsonSerializer.Serialize(current))!;
            }
            finally { cold.Retire(); thread.Retire(); }
        }
        var proxy = new FalloutEarlyControllerProxySnapshot(Guid.NewGuid(), Guid.NewGuid(), default, default, 0,
            [new("first", FalloutEarlyControllerSource.ArrayEmptyFlag, 0, 0), new("contact-listeners", 1, 1, 1),
             new("third", FalloutEarlyControllerSource.ArrayEmptyFlag, 0, 0), new("fourth", FalloutEarlyControllerSource.ArrayEmptyFlag, 0, 0)], 1, false);
        FalloutEarlyControllerFactory.RequireColdArrays(proxy);
        var arrays = proxy.Arrays.ToArray(); arrays[0] = new("first", 1, 0, 1);
        Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(proxy with { Arrays = arrays }));
        arrays[0] = new("first", 1, 1, 1);
        Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(proxy with { Arrays = arrays }));
        arrays[0] = proxy.Arrays[0]; arrays[1] = new("contact-listeners", 2, 2, 2);
        Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(proxy with { Arrays = arrays }));
    }
}
