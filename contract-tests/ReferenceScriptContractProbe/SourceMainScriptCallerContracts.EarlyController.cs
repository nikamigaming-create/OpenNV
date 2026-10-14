using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunEarlyControllerContracts()
    {
        ContactNumbers(); ManagerAndArrays(); SharedCurrentInputs(); PhysicsThreadScopes(); EarlyControllerColdOwners();
        ControllerPostProcessConsumers();
        Console.WriteLine("OPENNV_EARLY_CONTROLLER_CONTRACTS_PASS contactFloat32Fixtures=12 proxyDistinctSlope=true " +
            "actualStateObjects=true realArrayStorage=true nullableCurrentBase=true independentWriterBarriers=true " +
            "physicsKind16=true failureAndColdScopes=true coldFactoryCycles=3 retainedTypedPayloadRefused=true " +
            "nonnullContextNullBodies=true nullObjectForcedGetter=true " +
            "nativeResources=SEPARATE_AUDIT canonicalBytes=UNQUALIFIED");
    }
    private static void ContactNumbers()
    {
        var contact = new FalloutControllerContactState(new(FalloutPhysicalVector3.FromValues(0, 0, 1), 0),
            FalloutEarlyControllerSource.InitialSlopeBits, FalloutPhysicalConstructorInputs.InitialStepBits).Snapshot;
        Require(contact.AngleBits == 0x3f51ff7e && contact.CosineBits == 0x3f2e9772 && contact.SineBits == 0x3f3b39ff &&
            contact.CoefficientBits == 0x40842b38 && contact.ContactCount == 0 && contact.Flags == 0,
            "Reached contact input lost separate angle/cosine/sine/divide/multiply Float32 stores.");
        (uint Angle, uint Cosine, uint Sine, uint Coefficient)[] fixtures = [
            (0x3f51ff7d, 0x3f2e9772, 0x3f3b39fe, 0x40842b39),
            (0x3f51ff7e, 0x3f2e9772, 0x3f3b39ff, 0x40842b38),
            (0x3f51ff7f, 0x3f2e9771, 0x3f3b3a00, 0x40842b37),
            (0x3fc90fda, 0x33a22169, 0x3f800000, 0x34b386b8),
            (0x3fc90fdb, 0xb33bbd2e, 0x3f800000, 0xb44fe1e3),
            (0x3fc90fdc, 0xb42eef4c, 0x3f800000, 0xb541b44e),
            (0x3f490fda, 0x3f3504f4, 0x3f3504f3, 0x408dbbd6),
            (0x3f490fdb, 0x3f3504f3, 0x3f3504f3, 0x408dbbd5),
            (0x3f490fdc, 0x3f3504f2, 0x3f3504f4, 0x408dbbd3),
            (0xbf51ff7d, 0x3f2e9772, 0xbf3b39fe, 0xc0842b39),
            (0xbf51ff7e, 0x3f2e9772, 0xbf3b39ff, 0xc0842b38),
            (0xbf51ff7f, 0x3f2e9771, 0xbf3b3a00, 0xc0842b37) ];
        foreach (var fixture in fixtures)
        {
            var value = FalloutControllerContactState.TrigFields(fixture.Angle, FalloutPhysicalConstructorInputs.InitialStepBits);
            Require(value == (fixture.Cosine, fixture.Sine, fixture.Coefficient),
                $"Bounded default-control contact Float32 fixture changed at angle {fixture.Angle:X8}.");
        }
        Require(FalloutControllerContactState.MaximumSlopeCosine(FalloutEarlyControllerSource.MaximumSlopeBits) == 0xb33bbd2e,
            "Proxy maximum-slope cosine was replaced by contact cosine or an invented zero.");
        Reject(() => FalloutControllerContactState.TrigFields(0x7fc00000, FalloutPhysicalConstructorInputs.InitialStepBits));
        Reject(() => FalloutControllerContactState.MaximumSlopeCosine(0x7f800000));
    }
    private static void ManagerAndArrays()
    {
        var manager = new FalloutCharacterStateManager();
        var context = manager.ConstructContext(FalloutEarlyControllerSource.InitialState);
        var saved = manager.Capture();
        Require(saved.Slots.Count == FalloutEarlyControllerSource.ManagerSlots &&
            saved.RegistrationOrder.SequenceEqual(new byte[] { 0, 5, 1, 2, 3, 4, 6 }) &&
            saved.Slots.Count(value => value is not null) == 7 && saved.Slots[0] is { Enabled: true } &&
            saved.Slots[2] is { ScalarBits: FalloutEarlyControllerSource.State2ScalarBits } &&
            context.Snapshot.Current == 0 && context.Snapshot.Previous == 0 && context.Snapshot.Manager == manager.Identity,
            "Actual shared manager registrations or retained state context were replaced by empty callbacks.");
        Require(manager.Read(3).GetType() != manager.Read(4).GetType(), "Distinct source states lost their derived factories.");
        Reject(() => manager.Read(0).Update()); Reject(() => manager.Read(7)); Reject(manager.Retire);
        context.Dispose(); manager.Retire();
        Require(manager.Capture().ActiveContexts == 0 && manager.Capture().RegistrationOrder.SequenceEqual(saved.RegistrationOrder),
            "Retirement lost the actual constructed state/factory history.");
        var array = new FalloutControllerDynamicArray<object>("authored-first"); var first = new object(); var second = new object();
        Require(array.Capture() is { Count: 0, RetainedCapacity: 0, CapacityFlags: FalloutEarlyControllerSource.ArrayEmptyFlag },
            "Proxy array acquired a fabricated default storage capacity.");
        array.Append(first); array.Append(second);
        Require(array.Capture() is { Count: 2, RetainedCapacity: 2, CapacityFlags: 2 }, "Source array growth lost its real count-sized allocation.");
        array.ClearCount();
        Require(array.Count == 0 && ReferenceEquals(array.ReadRetainedStorage(0), first) && ReferenceEquals(array.ReadRetainedStorage(1), second),
            "Count clear disposed actual entries or erased retained storage.");
        array.Append(second); Require(ReferenceEquals(array.Read(0), second) && array.Capacity == 2,
            "Source append after reset discarded the retained array capacity.");
        array.Retire();
    }
    private static void SharedCurrentInputs()
    {
        using var fixture = new ReferenceScriptInitializationFixture();
        var source = FalloutEarlyControllerSource.Reviewed(fixture.FieldSource, fixture.ThreadContext.Source);
        var current = new FalloutEarlyControllerSharedFields(source);
        Require(current.ReadCenter() == default && !current.ReadHelperFlag() && current.ReadSlope() == FalloutEarlyControllerSource.InitialSlopeBits,
            "Distinct source loader declarations were borrowed from Actor or rendered transforms.");
        var scale = BitConverter.SingleToUInt32Bits(.75f); var heightReads = 0;
        Require(current.ActorScale(scale, null, _ => throw new InvalidOperationException("Unassigned current Base cannot borrow winning NPC height.")) == scale,
            "Early current-null Base did not retain the independent reference scale.");
        var actualBase = fixture.Records.GetEffective(fixture.Actor.Base);
        var combined = current.ActorScale(scale, actualBase, actual =>
        {
            Require(ReferenceEquals(actual, actualBase), "Scale provider read another current Base owner."); ++heightReads;
            return BitConverter.SingleToUInt32Bits(1.25f);
        });
        Require(heightReads == 1 && combined == BitConverter.SingleToUInt32Bits(.9375f),
            "Source current Base scale did not use its actual separate Float32 multiplication.");
        current.ObserveUnownedWriter("helperFlag"); Reject(() => current.ReadHelperFlag());
        Require(current.ReadCenter() == default && current.ReadSlope() == FalloutEarlyControllerSource.InitialSlopeBits,
            "An independent helper-flag writer invalidated unrelated source inputs.");
        current.ObserveUnownedWriter("center"); Reject(() => current.ReadCenter());
        current.ObserveUnownedWriter("slope"); Reject(() => current.ReadSlope());
        Require(current.Capture() is { CenterAvailable: false, SlopeAvailable: false, HelperFlagAvailable: false },
            "Unknown shared field writers were lost from their real current owner.");
    }
    private static void PhysicsThreadScopes()
    {
        using var fixture = new ReferenceScriptInitializationFixture(); var thread = fixture.ThreadContext;
        var factory = Guid.NewGuid(); var id = Guid.NewGuid();
        var model = thread.ReadEarlyControllerModelCache(id, factory, fixture.Actor.Reference, () => { });
        Require(!model.Matched && model.Model is null && model.Process == thread.Process && thread.Entry is null,
            "Constructor model read manufactured Main/Player entry or replaced the actual thread cache.");
        Reject(() => thread.ReadEarlyControllerModelCache(id, factory, fixture.Actor.Reference, () => { }));
        var scope = thread.EnterPhysicsInitialization(id, factory, () => { });
        Require(scope.EnteredKind == 16 && scope.BeforeKind == thread.Source.InitialKind && scope.Initialized == 1,
            "Physics constructor borrowed menu13/Script21 or an invented prior kind.");
        Reject(() => thread.EnterMenuAllocation(id, factory, () => { }));
        Reject(() => thread.EnterScriptInitialization(id, factory, () => { }));
        Reject(() => thread.EnterMenuAllocation(Guid.NewGuid(), factory, () => { }));
        scope = thread.ReturnPhysicsInitialization(scope, () => { });
        Require(scope.Returned && scope.RestoredKind == scope.BeforeKind && thread.Entry is null,
            "Physics success lost source prior-kind restoration or fabricated Main entry.");
        var menu = thread.EnterMenuAllocation(Guid.NewGuid(), factory, () => { });
        Require(menu.BeforeKind == scope.BeforeKind && menu.EnteredKind == 13, "Actual menu did not borrow the restored shared cells.");
        thread.ReturnMenuAllocation(menu, () => { });
        var script = thread.EnterScriptInitialization(Guid.NewGuid(), factory, () => { });
        Require(script.EnteredKind == 21 && script.BeforeKind == scope.BeforeKind, "Independent Script did not borrow actual current-kind cells.");
        thread.ReturnScriptInitialization(script, () => { });
        Reject(() => FalloutPlayerThreadContextState.ValidatePhysicsInitializationScopes([scope with { EnteredKind = 13 }], thread.Source));
        var saved = thread.CapturePhysicsInitializationScopes();
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var cold = new FalloutPlayerThreadContextState(thread.Source, Guid.NewGuid());
            try
            {
                cold.RetainColdPhysicsInitializationScopes(saved);
                Require(cold.Entry is null && cold.CapturePhysicsInitializationScopes().Single() == scope,
                    "Cold physics history replayed a native constructor or old Main/thread stores.");
                Reject(() => cold.EnterPhysicsInitialization(id, factory, () => { }));
                var fresh = cold.EnterMenuAllocation(Guid.NewGuid(), factory, () => { });
                Require(fresh.BeforeKind == cold.Source.InitialKind, "Cold source thread manufactured an old historical kind.");
                cold.ReturnMenuAllocation(fresh, () => { });
            }
            finally { cold.Retire(); }
        }
        var failed = thread.EnterPhysicsInitialization(Guid.NewGuid(), factory, () => { });
        failed = thread.RetainPhysicsInitializationFailure(failed, new InvalidOperationException("authored-entered-physics-child"));
        Reject(() => thread.ReturnPhysicsInitialization(failed, () => { }));
        Reject(() => thread.EnterMenuAllocation(Guid.NewGuid(), factory, () => { }));
        Require(thread.CapturePhysicsInitializationScopes().Last() is { Returned: false, RestoredKind: null, EnteredKind: 16,
            Failure: "authored-entered-physics-child" }, "Failed physics construction invented a source kind restore.");
    }
}
