using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeEarlyControllerAudit : Node
{
    private const float AuthoredUnitsToMetres = .0142875f;
    public override void _Ready()
    {
        RuntimeLiveContentSource? content = null; FalloutPluginStack? records = null;
        FalloutAdvancementRuntimeSource? runtimeSource = null; var exit = 1;
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Detached controller audit requires headless Godot.");
            var args = OS.GetCmdlineUserArgs(); FalloutMainScriptCallerSource main;
            FalloutCombatActorIdentity actor; FalloutPhysicalEffectiveSettings settings; var stack = new string('4', 64);
            if (args is [var dataRoot])
            {
                RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
                content = RuntimeLiveContentSource.Current!; records = FalloutPluginStack.Load(content.PluginSources);
                runtimeSource = FalloutAdvancementRuntimeSource.Open(records);
                main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtimeSource.Receipt));
                var declaration = FalloutCombatGroupDeclaration.ReadExecutable(content.FalloutExecutablePath);
                actor = FalloutCombatActorSource.Read(records, declaration, records.RuntimeFormKey(0x14));
                settings = FalloutPhysicalEffectiveSettings.Read(records.NumericSettings.Float); stack = content.StackId;
            }
            else if (args.Length == 0)
            {
                main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                    FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
                actor = new(new("ControllerFixture.esm", 0x14), new("ControllerFixture.esm", 7), "ENGINE_PLAYER", 0,
                    main.EngineSha256, "NPC_", 0, new string('5', 64), true); settings = new(0, 0);
            }
            else throw new ArgumentException("Expected zero authored arguments or one owned Fallout3 Data root.");
            var fields = FalloutPackageCommandFieldsSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            var thread = FalloutPlayerThreadContextSource.Reviewed(main);
            var source = content is null ? FalloutEarlyControllerSource.Reviewed(fields, thread) :
                FalloutExecutableStringTable.ReadEarlyController(content.FalloutExecutablePath, fields, thread);
            var inputs = new FalloutPhysicalConstructorInputs(main.EngineSha256, settings);
            Exercise(source, inputs, actor, stack);
            ExerciseConstructorFailure(source, inputs, actor, stack, reentry: false);
            ExerciseConstructorFailure(source, inputs, actor, stack, reentry: true);
            ExerciseColdResources(source, inputs, actor, stack);
            ExerciseMoverDelta(source, inputs);
            ExerciseControlledActorMovement(source, inputs);
            GD.Print($"OPENNV_NATIVE_EARLY_CONTROLLER_PASS ownedSource={content is not null} nativeArea=true sourceShapes=91 " +
                "detachedNoDefaultSpace=true actualCommonPointer=true threeIndependentProcessOwners=true realManagerAndProxy=true " +
                "propertyOwners=true constructorAndResetFailurePrefixes=true sourceKind16=true nativeRetirement=true " +
                "currentBase=AUTHORED_NULL originalWholeConstructor=UNQUALIFIED canonicalBytes=UNQUALIFIED coldResourceCycles=3 " +
                "retainedTypedPayloadRefused=true completeSave=UNQUALIFIED gameplay=UNEXECUTED recording=off");
            exit = 0;
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_EARLY_CONTROLLER_FAIL " + error); }
        finally
        {
            foreach (var release in new Action[] { () => runtimeSource?.Dispose(), () => records?.Dispose(),
                         () => { if (content is not null) RuntimeLiveContentSource.Clear(); } })
                try { release(); } catch (Exception error) { GD.PushError("OPENNV_EARLY_CONTROLLER_CLEANUP_FAIL " + error); exit = 1; }
            GetTree().Quit(exit);
        }
    }

    private static void Exercise(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
        FalloutCombatActorIdentity actor, string stack)
    {
        using var fixture = new NativeFixture(source, inputs, actor, stack);
        IFalloutEarlyControllerLease? caller = null, borrowed = null;
        Exception? originalFailure = null;
        try
        {
            caller = fixture.Construct(); var controller = caller.Controller;
            var call = fixture.Factory.LastCall!; var before = controller.Capture();
            Require(call is { Returned: true, ThreadScope.Returned: true, PhantomNativeId: not null } &&
                call.Controller == controller.Identity && call.Lineage.CommonProcess == fixture.Invocation.CommonProcess &&
                call.Lineage.RuntimeProcess == fixture.Runtime.CharacterControllerSourceProcess &&
                call.Lineage.ThreadProcess == fixture.Scene.ThreadContext.Process &&
                call.Lineage.CommonProcess != call.Lineage.RuntimeProcess && call.Lineage.RuntimeProcess != call.Lineage.ThreadProcess &&
                before.Phantom.NativeId != 0 && before.Phantom.Transform.Translation == default && before.Phantom.TranslationW == 0 &&
                before.Descriptor.RequestedWorld is null && before.Phantom.CurrentWorld is null && before.Phantom.RequestedWorld is null &&
                before.Proxy.ContactListeners == 1 && before.Proxy.Arrays[1] is { Count: 1, RetainedCapacity: 1, CapacityFlags: 1 } &&
                before.ActorScaleBits is null && before.Phantom.Filter == ((11u << 16) | FalloutCharacterCollisionGroups.CharacterLayer),
                "Returned actual detached constructor lost its real native/pointer/thread/descriptor association.");
            var area = controller.Phantom.NativeResource;
            Require(area.Id == before.Phantom.NativeId, "Native phantom audit borrowed another actual resource.");
            Require(!PhysicsServer3D.AreaGetSpace(area).IsValid && PhysicsServer3D.AreaGetShapeCount(area) == 91 &&
                PhysicsServer3D.AreaGetTransform(area) == Transform3D.Identity,
                "Native phantom substituted metadata, initial Actor XYZ or a default physics world.");
            fixture.Common.PublishEarlyController(fixture.Invocation, caller, fixture.RequireConstruction);
            borrowed = fixture.Common.BorrowEarlyController(actor.Reference, 1)!;
            Require(ReferenceEquals(borrowed.Controller, controller) && fixture.Common.ReadSceneContext(actor.Reference, 1).Context == controller.Identity,
                "Common and scene getters retained separate controller fields.");
            controller.SetActorProperty(fixture.Invocation, fixture.Runtime, fixture.RequireActor);
            controller.StoreActorScale(fixture.Invocation, fixture.Factory.Shared.ActorScale(FalloutEarlyControllerSource.PositiveOneBits, null,
                _ => throw new InvalidOperationException("Authored pre-load Base is null and has no winning NPC height query.")));
            controller.SetHelperFlags(fixture.Invocation, fixture.Factory.Shared.ReadHelperFlag());
            controller.SetPlayerPropertySelector(fixture.Invocation); controller.SetPlayerGroup(fixture.Invocation);
            var configured = controller.Capture();
            Require(configured.Phantom.Filter == (((uint)FalloutEarlyControllerSource.PlayerCollisionGroup << 16) |
                FalloutCharacterCollisionGroups.CharacterLayer) && configured.Flags == FalloutEarlyControllerSource.HelperAlwaysFlag &&
                configured.ActorScaleBits == FalloutEarlyControllerSource.PositiveOneBits && configured.PlayerPropertySelector == FalloutEarlyControllerSource.ActorPropertyKey &&
                configured.Phantom.Properties.Count == 2 && ReferenceEquals(controller.Phantom.Property(FalloutEarlyControllerSource.ActorPropertyKey), fixture.Runtime) &&
                ReferenceEquals(controller.Phantom.Property(FalloutEarlyControllerSource.ProxyPropertyKey), controller.Proxy),
                "Actual property/filter/flag stores fabricated proxy identities or borrowed final resolved Base scale.");
            var retained = new object(); controller.Proxy.FirstArray.Append(retained);
            var xyz = FalloutPhysicalVector3.FromValues(2048, 2048, 0);
            var reset = fixture.ResetInvocation(); controller.ResetForPosition(reset, xyz, fixture.RequireReset);
            var after = controller.Capture();
            Require(after.Resets.Last() is { Returned: true } && after.Phantom.TranslationW is null && after.ResetScalarBits == 0 &&
                after.Proxy.Velocity == default && after.Proxy.Arrays[0] is { Count: 0, RetainedCapacity: 1 } &&
                ReferenceEquals(controller.Proxy.FirstArray.ReadRetainedStorage(0), retained) && controller.ReadPositionZBits() == 0 &&
                after.Phantom.Transform.Translation != before.Phantom.Transform.Translation && !PhysicsServer3D.AreaGetSpace(area).IsValid,
                "Startup reset lost native XYZ, actual retained storage, four-zero velocity or explicit unwritten W.");
            var native = PhysicsServer3D.AreaGetTransform(area);
            var scale = FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits), AuthoredUnitsToMetres);
            Require(native.Origin == new Vector3(after.Phantom.Transform.Translation.XValue * scale,
                after.Phantom.Transform.Translation.ZValue * scale, -after.Phantom.Transform.Translation.YValue * scale),
                "Native detached transform diverged from authoritative source XYZ/units.");
            var count = after.Stores.Count;
            var failed = fixture.ResetInvocation(); fixture.FailReset = true;
            Reject(() => controller.ResetForPosition(failed, FalloutPhysicalVector3.FromValues(-12, 8, 4), fixture.RequireReset));
            var partial = controller.Capture();
            Require(partial.Stores.Count == count + 1 && partial.Resets.Last() is { Returned: false, Failure: "authored-after-native-XYZ" } &&
                partial.Phantom.Transform.Translation != after.Phantom.Transform.Translation &&
                controller.SaveBlocker == "authored-after-native-XYZ" && fixture.Factory.SaveBlocker == controller.SaveBlocker,
                "Reset failure rolled back native XYZ, admitted subsequent stores or replaced its original failure.");
            Require(fixture.Factory.Capture().Controllers.Single().Identity == controller.Identity,
                "Failed controller source capture lost its live actual Common child.");
            GD.Print($"OPENNV_DETACHED_CONTROLLER_NATIVE area={area.Id} proxy={configured.Proxy.Identity} " +
                $"manager={configured.State.Manager} allocatedFilter={before.Phantom.Filter:X8} currentFilter={configured.Phantom.Filter:X8} " +
                "startupXYZ=true rawW=UNKNOWN sourceScope=AUTHORED canonicalBytes=UNQUALIFIED");
        }
        catch (Exception error) { originalFailure = fixture.OriginalFailure = error; throw; }
        finally
        {
            ReleasePrefix(originalFailure, () => borrowed?.Dispose(), () => caller?.Dispose(), fixture.Common.Dispose, fixture.Factory.Retire);
            if (originalFailure is null)
                Require(fixture.Factory.Capture() is { Retired: true } retired && retired.Controllers.Single().Retired &&
                    retired.Manager?.ActiveContexts == 0, "Actual controller/native retirement lost its typed source history.");
        }
    }

    private static void ExerciseConstructorFailure(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
        FalloutCombatActorIdentity actor, string stack, bool reentry)
    {
        using var fixture = new NativeFixture(source, inputs, actor, stack);
        fixture.FailConstruction = !reentry; fixture.ReenterConstruction = reentry;
        Reject(() => fixture.Construct()); var call = fixture.Factory.LastCall!; var snapshot = fixture.Factory.Capture();
        if (reentry)
            Require(call is
            {
                Returned: false, ReachedPhase: FalloutEarlyControllerConstructionPhase.StateContext,
                PhantomNativeId: null
            } && snapshot.Failure == "Controller construction guard swallowed a source reentry refusal.",
                "Swallowed constructor reentry admitted another actual source resource.");
        else
            Require(call is
            {
                Returned: false, ReachedPhase: FalloutEarlyControllerConstructionPhase.PhantomAllocated,
                PhantomNativeId: not null, ThreadScope: { Returned: false, RestoredKind: null, EnteredKind: 16 }
            } &&
                call.PhantomNativeId != 0 && snapshot.FailedPrefixes.Single().Phantom?.NativeId == call.PhantomNativeId &&
                snapshot.Manager?.ActiveContexts == 1 && snapshot.Failure == "authored-after-real-area-allocation",
                "Native allocation failure lost the real entered kind16/resource/manager prefix.");
        var retained = fixture.Factory.LastCall; Reject(() => fixture.Construct());
        Require(ReferenceEquals(retained, fixture.Factory.LastCall), "Failed constructor was replayed or replaced by a later refusal.");
        fixture.Factory.Retire();
        Require(fixture.Factory.Capture() is { Retired: true } retired && retired.Manager?.ActiveContexts == 0 &&
            retired.FailedPrefixes.Count == 1 && (retired.FailedPrefixes[0].Phantom is null || retired.FailedPrefixes[0].Phantom!.Retired),
            "Serial failed resource retirement lost its original source/lifetime diagnostics.");
    }

    private sealed class NativeFixture : IDisposable
    {
        internal readonly FalloutActorProcessCommonState Common;
        internal readonly FalloutActorProcessRuntimeState Runtime;
        internal readonly FalloutStandalonePlayerSceneState Scene;
        internal readonly FalloutEarlyControllerFactory Factory;
        internal readonly FalloutEarlyControllerInvocation Invocation;
        private readonly FalloutCharacterCollisionGroups _groups;
        private readonly FalloutPlayerControllerConstructionInputs _player;
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private Guid? _reset;
        private int _resetGuards;
        internal bool FailConstruction, ReenterConstruction, FailReset;
        internal Exception? OriginalFailure;
        internal NativeFixture(FalloutEarlyControllerSource source, FalloutPhysicalConstructorInputs inputs,
            FalloutCombatActorIdentity actor, string stack, FalloutEarlyControllerFactorySnapshot? factoryState = null,
            FalloutProcessCommonSnapshot? commonState = null)
        {
            var declaration = FalloutActorProcessRuntimeDeclaration.ForExecutable(inputs.EngineSha256);
            Common = new(declaration, stack, _ => actor,
                _ => throw new InvalidOperationException("Detached audit cannot invent Actor gameplay pools."),
                _ => throw new InvalidOperationException("Detached audit cannot borrow the late NIF body factory."), commonState);
            if (commonState is null) Common.Construct(actor.Reference, 1, FalloutDetectionProcessLevel.High);
            Runtime = new(declaration, stack, actor.Reference, _ => actor);
            Scene = new(FalloutStandalonePlayerSceneSource.Read(source.Fields.Controls.Main), stack, Guid.NewGuid());
            var current = Common.ReadSceneQueries(actor.Reference, 1); _groups = new(inputs, factoryState?.Groups);
            if (factoryState is not null) Scene.ThreadContext.RetainColdPhysicsInitializationScopes(factoryState.PhysicsScopes);
            Invocation = new(Guid.NewGuid(), actor, stack, current.Process, Runtime.CharacterControllerSourceProcess,
                Scene.ThreadContext.Process, 1, current.Level, Scene.Constructor, _thread);
            Factory = new(source, inputs, _groups, Scene.ThreadContext, AuthoredUnitsToMetres, RequireConstruction, _ => null, factoryState);
            _player = inputs.ProjectPlayer(default, Factory.Shared.ReadCenter());
        }
        internal IFalloutEarlyControllerLease Construct() => Factory.Construct(Invocation, _player);
        internal void RequireConstruction(FalloutEarlyControllerInvocation invocation)
        {
            if (invocation != Invocation || invocation.Constructor != Scene.Constructor || invocation.ThreadId != _thread ||
                _thread != System.Environment.CurrentManagedThreadId || invocation.RuntimeProcess != Runtime.CharacterControllerSourceProcess ||
                invocation.ThreadProcess != Scene.ThreadContext.Process || !Runtime.HasActor(invocation.Actor.Reference))
                throw new InvalidOperationException("Detached audit lost its actual C# Common/Runtime/Scene/Actor and authored constructor scope.");
            Common.RequireEarlyControllerAssociation(invocation);
            if (FailConstruction && Factory?.ConstructionPhase == FalloutEarlyControllerConstructionPhase.PhantomAllocated)
                throw new InvalidOperationException("authored-after-real-area-allocation");
            if (ReenterConstruction && Factory?.ConstructionPhase == FalloutEarlyControllerConstructionPhase.StateContext)
                Reject(() => Factory.Construct(invocation, _player));
        }
        internal void RequireActor()
        { RequireConstruction(Invocation); Require(Runtime.HasActor(Invocation.Actor.Reference), "Phantom Actor property has no actual Actor runtime owner."); }
        internal FalloutEarlyControllerResetInvocation ResetInvocation()
        {
            _reset = Guid.NewGuid(); _resetGuards = 0;
            return new(_reset.Value, Invocation.Actor, Invocation.CommonProcess, Invocation.RuntimeProcess, Invocation.Epoch,
                Invocation.Constructor, _thread);
        }
        internal void RequireReset(FalloutEarlyControllerResetInvocation invocation)
        {
            if (invocation.Call != _reset || invocation.Actor != Invocation.Actor || invocation.CommonProcess != Invocation.CommonProcess ||
                invocation.RuntimeProcess != Runtime.CharacterControllerSourceProcess || invocation.Constructor != Scene.Constructor ||
                invocation.ThreadId != _thread || System.Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Detached audit reset lost its actual retained controller/source caller.");
            if (FailReset && ++_resetGuards == 2) throw new InvalidOperationException("authored-after-native-XYZ");
        }
        public void Dispose() => ReleasePrefix(OriginalFailure, Common.Dispose, Factory.Retire, _groups.Dispose, Scene.Retire, Runtime.Dispose);
    }
    private static void ReleasePrefix(Exception? original, params Action[] releases)
    {
        var failed = new List<Exception>();
        foreach (var release in releases) try { release(); } catch (Exception error) { failed.Add(error); }
        if (failed.Count == 0) return;
        if (original is null) throw new AggregateException("Detached controller actual resource retirement failed.", failed);
        foreach (var error in failed) GD.PushError("OPENNV_EARLY_CONTROLLER_CLEANUP_FAIL " + error);
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Authored unowned detached-controller child unexpectedly returned.");
    }
}
