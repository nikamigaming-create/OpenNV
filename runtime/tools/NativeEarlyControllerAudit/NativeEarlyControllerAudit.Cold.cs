using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeEarlyControllerAudit
{
    private static void ExerciseColdResources(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
        FalloutCombatActorIdentity actor, string stack)
    {
        FalloutEarlyControllerFactorySnapshot savedFactory;
        FalloutProcessCommonSnapshot savedCommon;
        FalloutEarlyControllerSnapshot savedController;
        using (var original = new NativeFixture(source, inputs, actor, stack))
        {
            using var caller = original.Construct();
            var controller = caller.Controller;
            original.Common.PublishEarlyController(original.Invocation, caller, original.RequireConstruction);
            controller.SetActorProperty(original.Invocation, original.Runtime, original.RequireActor);
            controller.StoreActorScale(original.Invocation, FalloutEarlyControllerSource.PositiveOneBits);
            controller.SetHelperFlags(original.Invocation, original.Factory.Shared.ReadHelperFlag());
            controller.SetPlayerPropertySelector(original.Invocation); controller.SetPlayerGroup(original.Invocation);
            controller.ResetForPosition(original.ResetInvocation(), FalloutPhysicalVector3.FromValues(1024, -768, 16), original.RequireReset);
            savedController = RoundTrip(controller.Capture());
            savedFactory = RoundTrip(original.Factory.Capture()); savedCommon = RoundTrip(original.Common.Capture());
        }
        var sourceCalls = JsonSerializer.Serialize(savedFactory.Calls);
        var sourceShapes = JsonSerializer.Serialize(savedFactory.ShapeCalls);
        var sourceScopes = JsonSerializer.Serialize(savedFactory.PhysicsScopes);
        for (var cycle = 1; cycle <= 3; ++cycle)
        {
            using var cold = new NativeFixture(source, inputs, actor, stack, savedFactory, savedCommon);
            var oldFactoryGroups = savedFactory.Groups;
            IFalloutEarlyControllerLease? supplied = null, common = null;
            Exception? originalFailure = null;
            try
            {
                Require(cold.Factory.SaveBlocker == "source-early-controller-cold-native-resources-unbound" &&
                    cold.Factory.Capture().PendingColdControllers.SequenceEqual(new[] { savedController.Identity }),
                    "Cold factory admitted old native resources before a fresh binding.");
                Reject(() => cold.Construct());
                Require(JsonSerializer.Serialize(cold.Factory.Capture().Calls) == sourceCalls,
                    "Cold Construct refusal replaced the retained original constructor history.");
                var invocation = new FalloutEarlyControllerColdInvocation(Guid.NewGuid(), cold.Invocation);
                var old = savedController;
                var retainedArrays = old.Proxy.Arrays.ToArray();
                retainedArrays[0] = retainedArrays[0] with { Count = 0, RetainedCapacity = 1, CapacityFlags = 1 };
                Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(old.Proxy with { Arrays = retainedArrays }));
                retainedArrays[0] = retainedArrays[0] with { Count = 1 };
                Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(old.Proxy with { Arrays = retainedArrays }));
                retainedArrays[0] = old.Proxy.Arrays[0];
                retainedArrays[1] = retainedArrays[1] with { Count = 2, RetainedCapacity = 2, CapacityFlags = 2 };
                Reject(() => FalloutEarlyControllerFactory.RequireColdArrays(old.Proxy with { Arrays = retainedArrays }));
                supplied = cold.Factory.RebindColdController(invocation, old, cold.Runtime, RequireCold, cold.RequireActor);
                var binding = cold.Factory.LastColdBinding!;
                var controller = supplied.Controller; var current = controller.Capture();
                Require(binding.Returned && binding.Failure is null && binding.Controller == current.Identity &&
                    binding.NativeArea == current.Phantom.NativeId && binding.PreviousController == old.Identity &&
                    current.Identity != old.Identity && current.State.Manager != old.State.Manager &&
                    current.Phantom.Identity != old.Phantom.Identity && current.Proxy.Identity != old.Proxy.Identity &&
                    current.Phantom.NativeId != old.Phantom.NativeId &&
                    !current.Phantom.ShapeNativeIds.Intersect(old.Phantom.ShapeNativeIds).Any() &&
                    current.Lineage.CommonProcess == cold.Invocation.CommonProcess &&
                    current.Lineage.RuntimeProcess == cold.Runtime.CharacterControllerSourceProcess &&
                    current.Lineage.ThreadProcess == cold.Scene.ThreadContext.Process &&
                    current.Lineage.CommonProcess != current.Lineage.RuntimeProcess &&
                    current.Lineage.RuntimeProcess != current.Lineage.ThreadProcess &&
                    current.Lineage.ThreadFactory == cold.Scene.ThreadContext.Factory,
                    "Cold binding reused native resources or conflated actual Common/Runtime/thread owners.");
                Require(current.Descriptor == old.Descriptor && current.Contact == old.Contact &&
                    (current.State with { Manager = old.State.Manager }) == old.State &&
                    current.Phantom.Transform == old.Phantom.Transform && current.Phantom.TranslationW == old.Phantom.TranslationW &&
                    current.Phantom.Filter == old.Phantom.Filter && current.ResetScalarBits == old.ResetScalarBits &&
                    current.Flags == old.Flags && current.PlayerPropertySelector == old.PlayerPropertySelector &&
                    current.ActorScaleBits == old.ActorScaleBits && current.Proxy.Up == old.Proxy.Up &&
                    current.Proxy.Velocity == old.Proxy.Velocity && current.Proxy.MaximumSlopeCosineBits == old.Proxy.MaximumSlopeCosineBits &&
                    JsonSerializer.Serialize(current.Stores) == JsonSerializer.Serialize(old.Stores) &&
                    JsonSerializer.Serialize(current.Resets) == JsonSerializer.Serialize(old.Resets),
                    "Cold resources replayed source helpers or lost retained current descriptor/scalar/XYZ/reset fields.");
                Require(ReferenceEquals(controller.Proxy.ReadContactListener(0), controller.Contact) &&
                    ReferenceEquals(controller.Phantom.Property(FalloutEarlyControllerSource.ProxyPropertyKey), controller.Proxy) &&
                    ReferenceEquals(controller.Phantom.Property(FalloutEarlyControllerSource.ActorPropertyKey), cold.Runtime),
                    "Cold properties/contact list retained historical objects instead of their actual fresh owners.");
                var area = controller.Phantom.NativeResource;
                Require(!PhysicsServer3D.AreaGetSpace(area).IsValid &&
                    PhysicsServer3D.AreaGetShapeCount(area) == current.Phantom.ShapeNativeIds.Count &&
                    PhysicsServer3D.AreaGetCollisionLayer(area) == 1u << (int)FalloutCharacterCollisionGroups.CharacterLayer,
                    "Cold native phantom changed its actual detached shape/filter resource.");
                cold.Common.RebindColdEarlyController(binding, supplied, RequireCold);
                common = cold.Common.BorrowEarlyController(actor.Reference, current.Lineage.Epoch)!;
                Require(ReferenceEquals(common.Controller, controller) &&
                    cold.Common.ReadSceneContext(actor.Reference, current.Lineage.Epoch).Context == controller.Identity,
                    "Cold Common and scene getter retained different actual controller fields.");
                Reject(() => cold.Factory.RebindColdController(invocation, old, cold.Runtime, RequireCold, cold.RequireActor));
                Require(ReferenceEquals(binding, cold.Factory.LastColdBinding), "Cold replay replaced its original returned binding receipt.");
                savedController = RoundTrip(current); savedFactory = RoundTrip(cold.Factory.Capture());
                savedCommon = RoundTrip(cold.Common.Capture());
                FalloutEarlyControllerFactory.ValidateColdFactory(savedFactory, source, inputs);
                Require(savedFactory.PendingColdControllers.Count == 0 && savedFactory.ColdBindings.Count == cycle &&
                    savedFactory.ColdShapeCalls.Count == cycle && savedFactory.Groups == oldFactoryGroups &&
                    JsonSerializer.Serialize(savedFactory.Calls) == sourceCalls &&
                    JsonSerializer.Serialize(savedFactory.ShapeCalls) == sourceShapes &&
                    JsonSerializer.Serialize(savedFactory.PhysicsScopes) == sourceScopes &&
                    binding.GroupCounterBefore == binding.GroupCounterAfter &&
                    binding.OriginalCallsBefore == binding.OriginalCallsAfter &&
                    binding.OriginalScopesBefore == binding.OriginalScopesAfter &&
                    cold.Scene.ThreadContext.Entry is null && cold.Factory.SaveBlocker is null,
                    "Cold native binding replayed collision-group, constructor or current-thread kind stores.");
                GD.Print($"OPENNV_EARLY_CONTROLLER_COLD cycle={cycle} nativeArea={current.Phantom.NativeId} " +
                    "freshShapes=true retainedXYZ=true originalHistory=true sourceKindReplay=false actualCommonPointer=true");

                void RequireCold(FalloutEarlyControllerColdInvocation entered)
                {
                    if (entered != invocation) throw new InvalidOperationException("Authored cold binding changed its actual resource invocation.");
                    cold.RequireConstruction(entered.Current);
                }
            }
            catch (Exception error) { originalFailure = cold.OriginalFailure = error; throw; }
            finally { ReleasePrefix(originalFailure, () => common?.Dispose(), () => supplied?.Dispose(), cold.Common.Dispose, cold.Factory.Retire); }

        }
        ExerciseColdResourceFailure(source, inputs, actor, stack, savedFactory, savedCommon, savedController, false);
        ExerciseColdResourceFailure(source, inputs, actor, stack, savedFactory, savedCommon, savedController, true);
    }

    private static void ExerciseColdResourceFailure(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
        FalloutCombatActorIdentity actor, string stack, FalloutEarlyControllerFactorySnapshot savedFactory,
        FalloutProcessCommonSnapshot savedCommon, FalloutEarlyControllerSnapshot savedController, bool reentry)
    {
        using var cold = new NativeFixture(source, inputs, actor, stack, savedFactory, savedCommon);
        var invocation = new FalloutEarlyControllerColdInvocation(Guid.NewGuid(), cold.Invocation);
        var calls = JsonSerializer.Serialize(savedFactory.Calls);
        var scopes = JsonSerializer.Serialize(savedFactory.PhysicsScopes);
        Reject(() => cold.Factory.RebindColdController(invocation, savedController, cold.Runtime, Guard, cold.RequireActor));
        var receipt = cold.Factory.LastColdBinding!;
        var partial = cold.Factory.Capture();
        Require(!receipt.Returned && partial.ColdBindings.Count == savedFactory.ColdBindings.Count + 1 &&
            JsonSerializer.Serialize(partial.Calls) == calls && JsonSerializer.Serialize(partial.PhysicsScopes) == scopes &&
            partial.Groups == savedFactory.Groups && partial.Manager?.ActiveContexts == 1,
            "Cold failure replaced source history or rolled back its actual fresh context prefix.");
        if (reentry)
            Require(receipt.NativeArea is null && cold.Factory.ColdConstructionPhase == FalloutEarlyControllerConstructionPhase.StateContext &&
                partial.Failure == "Cold controller guard swallowed source/resource reentry.",
                "Cold swallowed reentry admitted native resources.");
        else
            Require(receipt.NativeArea is > 0 && partial.FailedPrefixes.Last().Phantom?.NativeId == receipt.NativeArea &&
                partial.Failure == "authored-cold-after-real-area-allocation",
                "Cold resource failure lost its actual detached native allocation.");
        var retained = cold.Factory.LastColdBinding;
        Reject(() => cold.Factory.RebindColdController(invocation, savedController, cold.Runtime, Guard, cold.RequireActor));
        Require(ReferenceEquals(retained, cold.Factory.LastColdBinding), "Cold failed binding was replayed.");
        cold.Factory.Retire();
        Require(cold.Factory.Capture() is { Retired: true } retired && retired.Manager?.ActiveContexts == 0 &&
            (receipt.NativeArea is null || retired.FailedPrefixes.Last().Phantom is { Retired: true }),
            "Cold failed native resources did not retire serially with retained diagnostics.");

        void Guard(FalloutEarlyControllerColdInvocation entered)
        {
            if (entered != invocation) throw new InvalidOperationException("Authored cold failure changed its actual invocation.");
            cold.RequireConstruction(entered.Current);
            if (reentry && cold.Factory.ColdConstructionPhase == FalloutEarlyControllerConstructionPhase.StateContext)
                Reject(() => cold.Factory.RebindColdController(invocation, savedController, cold.Runtime, Guard, cold.RequireActor));
            if (!reentry && cold.Factory.ColdConstructionPhase == FalloutEarlyControllerConstructionPhase.PhantomAllocated)
                throw new InvalidOperationException("authored-cold-after-real-area-allocation");
        }
    }
    private static T RoundTrip<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? throw new InvalidDataException("Cold controller DTO round trip returned null.");
}
