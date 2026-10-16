using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerModelLoadContinuationContracts()
    {
        WeaponEqualityTail(); FirstModelFields(); SharedShaderProperty(); WornSourceSlots(); MagicCurrentLists();
        Console.WriteLine("OPENNV_PLAYER_MODEL_LOAD_CONTINUATION_CONTRACT_PASS equalityHasTail=true firstFieldIndependent=true identityRotation=true sharedActualShader=true sourceWornSlots=true wholeLoader=UNRETURNED");
    }
    private static FalloutPlayerModelLoadContinuationSource AuthoredModelContinuation(FalloutPlayerReturnTailSource tail) =>
        new(AuthoredLoadedAnimation(tail), FalloutPlayerModelLoadContinuationSource.ContractDigest,
            "Camera3rd", "SkinAttachment", "BIP") { FirstNodeNames = ["Root", "Branch", "A"] };

    private static void WeaponEqualityTail()
    {
        using var fixture = new PlayerReturnTailFixture(); var source = AuthoredModelContinuation(fixture.State.Source);
        fixture.EnterLoad((load, guard) =>
        {
            var outer = fixture.Scene.ThreadContext.EnterAnimationInitialization(Guid.NewGuid(), Guid.NewGuid(), FalloutLoadedAnimationScopeKind.PlayerLoad, () => guard(load));
            var globals = new FalloutPlayerModelLoadGlobals(source, load.ReferenceProcess, guard);
            var read = fixture.Common.ReadPlayerLoadWeapon(load, guard);
            var call = fixture.Common.SetPlayerLoadWeapon(source, load, read, globals, fixture.Scene.ThreadContext, guard);
            Require(call.Returned && call.CallerRead.Entry is null && call.SetterRead?.Entry is null &&
                call.Stores.Select(row => row.Field).SequenceEqual(new[] { "weapon-word", "weapon-scalar", "weapon-selection" }) &&
                call.Stores.Select(row => row.After).SequenceEqual(new uint[] { 0, 0, uint.MaxValue }) &&
                call.Global is { BeforeKnown: false, After: null } && call.Scope is { Scope.Returned: true, Scope.BeforeKind: 52, Scope.EnteredKind: 50, Scope.RestoredKind: 52 },
                "Equal source weapon skipped its mandatory scalar/global/selection tail or borrowed an old zero.");
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                var saved = JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(fixture.Common.Capture()))!;
                FalloutActorProcessCommonState.Validate(saved);
                Require(saved.Current.Single().SceneFields!.ModelLoad!.Stores.Count == 3, "Common JSON history replayed/dropped actual model-loader stores.");
            }
            Reject(() => fixture.Common.SetPlayerLoadWeapon(source, load, read with { Owner = read.Owner with { Process = Guid.NewGuid() } }, globals, fixture.Scene.ThreadContext, guard));
            outer = fixture.Scene.ThreadContext.ReturnAnimationInitialization(outer, () => guard(load));
        });
        using var reentry = new PlayerReturnTailFixture(); var otherSource = AuthoredModelContinuation(reentry.State.Source);
        reentry.EnterLoad((load, guard) =>
        {
            _ = reentry.Scene.ThreadContext.EnterAnimationInitialization(Guid.NewGuid(), Guid.NewGuid(), FalloutLoadedAnimationScopeKind.PlayerLoad, () => guard(load));
            var globals = new FalloutPlayerModelLoadGlobals(otherSource, load.ReferenceProcess, guard);
            var read = reentry.Common.ReadPlayerLoadWeapon(load, guard); var checks = 0;
            Reject(() => reentry.Common.SetPlayerLoadWeapon(otherSource, load, read, globals, reentry.Scene.ThreadContext, actual =>
            {
                guard(actual);
                if (++checks == 3) { try { _ = reentry.Common.Capture(); } catch (InvalidOperationException) { } }
            }));
            Require(reentry.Common.LastPlayerLoadWeaponSetter is { Returned: false, Failure: not null, Stores.Count: 0, Scope.Scope.Returned: false },
                "Actual Common reentry was swallowed or restored a failed entered weapon scope.");
        });
    }
    private static void FirstModelFields()
    {
        using var fixture = new PlayerReturnTailFixture(); var source = AuthoredModelContinuation(fixture.State.Source);
        fixture.EnterLoad((load, guard) =>
        {
            var model = PlayerOpacityContracts.CreateReferenceColdModel(); var graph = model.ConstructLogicalScene();
            graph.PublishLocalPose(graph.Root.Key, graph.Root.Local with { Translation = new(0, 0, 9) });
            var released = 0; var retired = 0;
            var node = new FalloutReferenceModelDataNode(new(load.ReferenceProcess, model.Factory, 17), graph, () => guard(load), () => ++released, () => ++retired, () => { });
            var globals = new FalloutPlayerModelLoadGlobals(source, load.ReferenceProcess, guard);
            var owner = new FalloutPlayerFirstModelState(source, load.ReferenceProcess, load.ReferenceConstructor, globals, guard);
            Require(owner.ReadCurrent() is null && owner.Capture().Height is null, "First model borrowed a reference model or guessed uninitialized height.");
            var call = owner.PublishAndInitialize(load, node, (_, _) => throw new InvalidOperationException("Authored named first nodes exist."));
            Require(call.Returned && call.PointerStored && call.Retained && call.Globals.Count == 3 && call.Height == BitConverter.SingleToUInt32Bits(9) &&
                call.IdentityRotationStored && graph.Root.Local.RotationRowMajor.SequenceEqual(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) &&
                ReferenceEquals(owner.ReadForLoader(load), node) && globals.Capture().FirstNamed.All(row => row.Known),
                "First model changed field retain/named query/world-height/identity store order or used presentation values.");
            owner.Retire(); owner.Retire(); Require(released == 1 && retired == 1, "First field did not release its actual retained factory once.");
        });
    }
    private static void SharedShaderProperty()
    {
        using var fixture = new PlayerReturnTailFixture(); var source = AuthoredModelContinuation(fixture.State.Source);
        fixture.EnterLoad((load, guard) =>
        {
            var graph = PlayerOpacityContracts.CreateReferenceColdModel().ConstructLogicalScene();
            var query = new FalloutPlayerModelNamedRead(source.SkinAttachment, graph.Factory, graph.Lifetime, null, null, null, null);
            var selected = graph.SelectPlayerFirstShaderProperty(source, query, null, load, guard);
            Require(selected.Property is not null && selected.Read.SelectedRoot == new FalloutNifSceneKey(0, 2) && selected.Property.Source.Property == new FalloutNifSceneKey(0, 6),
                "Source reverse immediate-child shader probe did not select the actual shared property.");
            var property = selected.Property!;
            var first = fixture.Common.StorePlayerLoadedShader(load, property, guard);
            var second = fixture.Common.StorePlayerLoadedShader(load, property, guard);
            Require(first is { Returned: true, PointerStored: true, FieldRetained: true } && second is { Returned: true, PointerStored: false, FieldRetained: false } &&
                property.CommonLeases == 1 && ReferenceEquals(property.Property, graph.RequireProperty(new(0, 6))),
                "Common shader setter borrowed the controller context or retained an equal pointer twice.");
            property.Property.StoreShaderAlpha(0x3e800000);
            Require(graph.RequireProperty(new(0, 6)).ShaderAlphaBits == 0x3e800000, "Common property became a metadata copy instead of the same mutable logical shader.");
            var saved = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(saved);
            Reject(() => fixture.Common.UpdatePostActions(fixture.Actor.Reference, 1, () => 0));
            _ = fixture.Common.StorePlayerLoadedShader(load, null, guard);
            Require(property.CommonLeases == 0 && fixture.Common.Capture().Current.Single().SceneFields!.PostActionContext is null,
                "Common shader null store failed to release the actual previous property.");
            fixture.Common.RetirePlayerLoadedShaderFields();
        });
    }
    private static void WornSourceSlots()
    {
        using var fixture = ReferencePositionFixture.CreateModelLoadInventoryRecords();
        var inventory = new FalloutPlayerInventory();
        inventory.Add(fixture.Records, fixture.Records.RuntimeFormKey(0x7a0), 1, 1, true); inventory.Equip(fixture.Records, fixture.Records.RuntimeFormKey(0x7a0));
        Require(inventory.ReadModelLoadWornEntry(fixture.Records, 0)?.FormKey == fixture.Records.RuntimeFormKey(0x7a0) &&
            inventory.ReadModelLoadWornEntry(fixture.Records, 2) is null,
            "Worn source query missed BIPL alternate mask or fabricated another occupied slot.");
    }
    private static void MagicCurrentLists()
    {
        using var fixture = ReferencePositionFixture.CreateModelLoadInventoryRecords(); fixture.Initialize();
        var mover = FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(fixture.Fields.Source, fixture.Thread.Source));
        var movement = FalloutPlayerActorMovementSource.Reviewed(mover, FalloutInitialPlacementCellsSource.Reviewed(fixture.Source),
            FalloutPlayerWorldOrientationSource.Reviewed(fixture.Fields.Source.Controls));
        var source = AuthoredModelContinuation(FalloutPlayerReturnTailSource.Reviewed(movement,
            FalloutStandalonePlayerSceneSource.Read(fixture.Thread.Source.Main)));
        var basis = fixture.Records.GetEffective(fixture.Actor.Base);
        FalloutPluginRecord? assigned = null;
        var assignment = fixture.Call(FalloutReferencePositionIngress.BaseAssignment, invocation =>
        {
            var before = assigned?.FormKey; assigned = basis;
            var inputs = FalloutReferenceBaseAssignmentInputs.Read(fixture.Source, fixture.Records, fixture.Actor);
            _ = fixture.Initializer.SetBaseDestructibleFlag(invocation, inputs, fixture.RequireCurrent);
            return new FalloutReferenceBaseAssignmentCall(invocation, before, assigned.FormKey, inputs, true, true, inputs.ActorResponse ? (byte)1 : (byte)0, true, null);
        });
        var load = new FalloutReferenceModelDataInvocation(Guid.NewGuid(), fixture.Actor, fixture.Thread.Process,
            fixture.Fields.Constructor, fixture.Fields.Process, 1, Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
        var inventory = new FalloutPlayerInventory();
        void Guard(FalloutReferenceModelDataInvocation current) { if (current != load) throw new InvalidOperationException("Authored current model load was replaced."); }
        var scope = fixture.Thread.EnterAnimationInitialization(Guid.NewGuid(), Guid.NewGuid(), FalloutLoadedAnimationScopeKind.PlayerLoad, () => Guard(load));
        var owner = new FalloutActorModelMagicResetState(source, fixture.Records, inventory, fixture.Thread, Guard,
            actual => { if (!ReferenceEquals(actual, inventory)) throw new InvalidOperationException("Borrowed another current inventory."); });
        var race = fixture.Records.GetEffective(fixture.Records.RuntimeFormKey(0x19)); var raceReads = 0;
        var call = owner.Reset(load, _ => new(assigned!, assignment), _ => { ++raceReads; return race; });
        Require(call.Returned && raceReads == 2 && call.Slots.Count == 5 && call.Slots.All(row => row.Scope is { EnteredKind: 54, Returned: true, RestoredKind: 52 }),
            "Magic reset invented an empty list/slot census or skipped actual repeated race and five typed scope calls.");
        owner.ObserveEnteredWriter("authored-current-spell-list-copy", () => Guard(load));
        Reject(() => owner.Reset(load, _ => new(assigned!, assignment), _ => race));
        scope = fixture.Thread.ReturnAnimationInitialization(scope, () => Guard(load));
    }
    internal static void AuditOwnedPlayerModelLoadContinuation(string executable, FalloutLoadedAnimationSource animation)
    {
        var source = FalloutExecutableStringTable.ReadPlayerModelLoadContinuation(executable, animation);
        Require(ReferenceEquals(source, FalloutExecutableStringTable.ReadPlayerModelLoadContinuation(executable, animation)),
            "The unchanged selected image did not reuse its qualified model-load declaration.");
        AuditOwnedPlayerModelLoadPrerequisites(executable, source);
        Require(source.CameraNode == "Camera3rd" && source.SkinAttachment == "SkinAttachment" && source.BipedPrefix == "BIP" &&
            source.FirstNodeNames.SequenceEqual(new[] { "Bip01 Looking", "Camera1st", "Bip" }), "Owned loader changed its actual source names.");
        Console.WriteLine($"OPENNV_OWNED_PLAYER_MODEL_LOAD_CONTINUATION_SOURCE_PASS source={source.Identity} names=true weaponTail=true magicOrder=true sharedShader=true wholeLoader=UNEXECUTED");
    }
    private sealed partial class ReferencePositionFixture
    {
        internal static ReferencePositionFixture CreateModelLoadInventoryRecords() => new(appendedRows: Join(
            Row("ARMO", 0x7a0, Part("EDID", "AuthoredWorn\0"u8.ToArray()), Part("DATA", new byte[12]), Part("BMDT", new byte[8]), Part("BIPL", BitConverter.GetBytes(0x7a1u))),
            Row("FLST", 0x7a1, Part("LNAM", BitConverter.GetBytes(0x7a2u))),
            Row("ARMA", 0x7a2, Part("BMDT", Join(BitConverter.GetBytes(1u), new byte[4])))));
    }
}
