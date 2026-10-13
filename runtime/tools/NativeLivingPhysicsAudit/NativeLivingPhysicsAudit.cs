using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Physics;

public partial class NativeLivingPhysicsAudit : Node
{
    public override void _Ready()
    {
        try
        {
            ExerciseSynthetic();
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 0)
            {
                if (args.Length < 4 || args[0] != "--owned-physical-model")
                    throw new ArgumentException("Expected --owned-physical-model DataRoot campaign sourceModel [additionalDataRoots...].");
                RuntimeLiveContentSource.Configure(args[1], args[2], args.Skip(4).ToArray());
                try
                {
                    var content = RuntimeLiveContentSource.Current!;
                    if (!content.TryRead(args[3], null, out var bytes, out var identity)) throw new FileNotFoundException(args[3]);
                    var physical = FalloutNifPhysicalModel.Read(FalloutNifFile.Read(bytes));
                    ExerciseModel(physical, FixtureAppearance(args[3]), "owned-model", true);
                    Require(content.TryRead(args[3], null, out var after, out _) && bytes.SequenceEqual(after), "Owned physical source changed.");
                    GD.Print($"OPENNV_OWNED_LIVING_PHYSICS_PASS source={identity} sha256={physical.Source.Sha256} " +
                        $"bodies={physical.Bodies.Count} joints={physical.Constraints.Count} actualPhysicalResources=true detachedAndAdmitted=true " +
                        "currentDynamics=true coldBodyReconstruction=true coldStructuralRegistry=true sourceUnchanged=true sourceWorldAndGameplayParityUnverified=true");
                }
                finally { RuntimeLiveContentSource.Clear(); }
            }
            GD.Print("OPENNV_NATIVE_LIVING_PHYSICS_PASS actualPhysicsServer=true sourceLinks=true currentMemberships=true retirement=true coldBodies=true " +
                "coldStructuralRegistry=true allocatorPreserved=true malformedMassRejectedBeforePublication=true sourceWorldAndGameplayParityUnverified=true");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void ExerciseSynthetic()
    {
        var physical = FalloutNifPhysicalModel.Read(FalloutNifFile.Read(FalloutLivingPhysicsFixture.Create()));
        using var nativeWorld = new World3D();
        var backend = new RuntimeNativeLivingPhysicsBackend("synthetic-world", () => nativeWorld.Space, .02f);
        var inputs = Inputs();
        var construct = SourceBodyFactory(physical);
        using var model = FalloutLivingActorModel.Construct("synthetic-model", new(FixtureAppearance("authored.nif"), physical, []),
            inputs, backend, construct);
        using var world = new FalloutLivingPhysicalWorld(backend, uint.MaxValue - 1);
        var bodies = model.Bodies.ToDictionary(body => body.Source.Block.Index);
        var constraints = model.Constraints.ToDictionary(value => value.Source.Header.Block.Index);
        Require(bodies.Count == 3 && constraints.Count == 4 && bodies.Values.All(body => body.Backend.HasPhysicalObject && !body.Backend.IsWorldAttached &&
            body.CurrentWorld is null && body.Island is null && body.IncomingConstraints.Count == 0 && body.OutgoingConstraints.Count == 0),
            "Source declarations substituted for real detached bodies or current memberships.");
        Require(bodies.Values.Select(body => body.Wrapper.Instance).Distinct().Count() == bodies.Count &&
            bodies.Values.All(body => ReferenceEquals(body.CollisionProperty.CurrentWrapper, body.Wrapper) &&
                ReferenceEquals(body.Wrapper.CurrentBody, body)), "Living collision/wrapper/body factories collapsed distinct current owners.");
        Require(model.Roots.Single()!.Children.Count == 4 && model.Roots.Single()!.Children[1] is null,
            "Actual scene lost a null authored child slot.");
        Require(bodies[9].LinkedConstraints.Select(value => ((FalloutLivingPhysicalConstraint)value!).Source.Header.Block.Index)
            .SequenceEqual(new[] { 14, 13, 12, 11 }), "Current stream list lost reverse source link order or admitted a null slot.");
        Reject(() => world.AddConstraint(constraints[11]));
        foreach (var body in model.Bodies) world.AddBody(body);
        Require(bodies[8].EntityId == uint.MaxValue && bodies[9].EntityId == 0 && bodies[10].EntityId == 1 &&
            bodies[8].Island!.Fixed && !bodies[9].Island!.Fixed, "Actual entity allocator or fixed-island admission changed.");
        foreach (var index in new[] { 13, 14, 12, 11 }) world.AddConstraint(constraints[index]);
        Require(constraints[11].Master == bodies[9] && constraints[11].Slave == bodies[8], "Fixed endpoint A did not select B as actual master.");
        Require(bodies[9].IncomingConstraints.Select(value => ((FalloutLivingPhysicalConstraint)value).Source.Header.Block.Index)
            .SequenceEqual(new[] { 14, 12, 11, 13 }), "Master priority/unsigned-UID/equal-key order changed.");
        Require(bodies[10].OutgoingConstraints.Select(value => ((FalloutLivingPhysicalConstraint)value).Source.Header.Block.Index)
            .SequenceEqual(new[] { 13, 14, 12 }), "Slave memberships did not append in admission order.");
        Require(ReferenceEquals(bodies[9].Island!.Find(), bodies[10].Island!.Find()) &&
            !ReferenceEquals(bodies[8].Island!.Find(), bodies[9].Island!.Find()), "Real structural world cohorts did not merge independently of the fixed island.");
        Reject(() => world.AddConstraint(constraints[12]));
        var structural = JsonSerializer.Deserialize<FalloutLivingWorldSnapshot>(JsonSerializer.Serialize(world.Capture()))!;
        var islandIdentities = world.Bodies.Select(body => body.Island!.Find().Identity).ToHashSet();
        Reject(world.RequireSourceContactProduction);
        Reject(() => world.Capture());
        var filter = bodies[9].CollisionFilter;
        Reject(() => bodies[9].PublishCollisionFilter(filter ^ 0x4000u));
        Require(bodies[9].CollisionFilter == filter, "Unowned native filter-flag publication mutated authoritative state.");
        Require(Task.Run(() =>
        {
            try { _ = bodies[9].ReadTransform(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult(), "Foreign-thread physical callback was admitted.");
        // These are actual PhysicsServer3D bodies and constraints. No rendered
        // nodes or query Area3D were constructed for the fixture.
        Require(model.Bodies.All(body => body.Backend.IsWorldAttached) && model.Constraints.All(value => value.Backend.IsWorldAttached),
            "Logical membership was published without underlying physical residency.");
        var snapshot = JsonSerializer.Deserialize<FalloutLivingModelSnapshot>(JsonSerializer.Serialize(model.Capture()))!;
        var active = bodies[9]; var before = active.ReadLinearVelocity();
        active.Activate(); active.ApplyPointImpulse(FalloutPhysicalVector3.FromValues(1, 0, 0), active.ReadCenterOfMass());
        Require(active.ReadLinearVelocity().XValue > before.XValue, "Corrective impulse had no physical velocity effect.");
        world.RetireModel(model);
        Require(model.Bodies.All(body => !body.Backend.IsWorldAttached && body.CurrentWorld is null && body.EntityId is null &&
            body.IncomingConstraints.Count == 0 && body.OutgoingConstraints.Count == 0) && model.Constraints.All(value => !value.Backend.IsWorldAttached),
            "Current physical model retirement retained residency or memberships.");
        var generation = model.Generation; model.Dispose();
        Reject(() => model.Roots.ToArray());
        using var cold = FalloutLivingActorModel.Construct("synthetic-model", new(FixtureAppearance("authored.nif"), physical, []),
            inputs, backend, construct);
        ExerciseMalformedMassRestores(cold, snapshot);
        cold.RestoreDetached(snapshot);
        Require(cold.Generation != generation && SameBodies(snapshot, cold.Capture()), "Cold physics reused runtime identity or failed current body reconstruction.");
        using var coldWorld = new FalloutLivingPhysicalWorld(backend);
        var wrongLinks = structural with
        {
            Bodies = structural.Bodies.Select((body, index) => index == 1 ?
            body with { Links = body.Links.Reverse().ToArray() } : body).ToArray()
        };
        Reject(() => coldWorld.Restore(wrongLinks, [cold]));
        Require(cold.Bodies.All(body => !body.Backend.IsWorldAttached && body.CurrentWorld is null), "Cold structural refusal partially published body residency.");
        coldWorld.Restore(structural, [cold]);
        Require(JsonSerializer.Serialize(coldWorld.Capture()) == JsonSerializer.Serialize(structural) &&
            coldWorld.NextEntityId == structural.NextEntityId && coldWorld.Bodies.All(body => !islandIdentities.Contains(body.Island!.Find().Identity)),
            "Cold structural registry lost UID allocation, memberships/link order, or reused runtime island identity.");
        using var allocation = FalloutLivingActorModel.Construct("allocation-after-cold", new(FixtureAppearance("authored.nif"), physical, []),
            inputs, backend, construct);
        coldWorld.AddBody(allocation.Bodies.First());
        Require(allocation.Bodies.First().EntityId == unchecked(structural.NextEntityId + 1),
            "Cold structural registry restarted its entity allocator.");
        coldWorld.RetireModel(allocation);
        var badIndex = structural with
        {
            Constraints = structural.Constraints.Select((constraint, index) => index == 0 ?
            constraint with { SlaveIndex = constraint.SlaveIndex + 1 } : constraint).ToArray()
        };
        Reject(badIndex.Validate);
        var badIsland = structural with
        {
            Bodies = structural.Bodies.Select((body, index) => index == 1 ?
            body with { Island = body.Island + 1 } : body).ToArray()
        };
        Reject(badIsland.Validate);
        using var queued = FalloutLivingActorModel.Construct("queued-model", new(FixtureAppearance("authored.nif"), physical, []),
            inputs, backend, construct);
        var queuedWorld = new FalloutLivingPhysicalWorld(backend);
        queuedWorld.EnterCriticalOperations(); queuedWorld.AddBody(queued.Bodies.First());
        Require(queuedWorld.PendingBodies.Count == 1 && queued.Bodies.First().PendingWorld == queuedWorld &&
            !queued.Bodies.First().Backend.IsWorldAttached, "Queued body masqueraded as an immediate admission.");
        Reject(queuedWorld.LeaveCriticalOperations); Reject(() => queued.Capture());
        Reject(() => queuedWorld.Capture());
        // The refusal receipt above is preserved. Explicit diagnostic cleanup
        // clears only this never-admitted authored fixture, without claiming a
        // source drain or producing gameplay/save state.
        queued.Bodies.First().PendingWorld = null;
    }

    private static void ExerciseMalformedMassRestores(FalloutLivingActorModel cold, FalloutLivingModelSnapshot snapshot)
    {
        var before = JsonSerializer.Serialize(cold.Capture());
        var resources = cold.Bodies.Select(body => body.Backend.PhysicalObjectId).ToArray();
        var massTwo = BitConverter.SingleToUInt32Bits(2f);
        var half = BitConverter.SingleToUInt32Bits(.5f);
        // A valid changed prefix precedes each later malformed row. Refusal
        // must validate the whole model before changing any real native body.
        (int Index, byte Motion, uint Mass, uint Inverse)[] malformed =
        [
            (1, 1, massTwo, 0),
            (1, 1, massTwo, BitConverter.SingleToUInt32Bits(.25f)),
            (0, 7, massTwo, half),
            (2, 6, massTwo, half),
            (0, 7, massTwo, BitConverter.SingleToUInt32Bits(-0f)),
            (2, 6, massTwo, BitConverter.SingleToUInt32Bits(-0f)),
        ];
        foreach (var invalid in malformed)
        {
            var rows = snapshot.Bodies.ToArray();
            rows[0] = rows[0] with
            {
                Dynamics = rows[0].Dynamics with
                {
                    Transform = rows[0].Dynamics.Transform with { Translation = FalloutPhysicalVector3.FromValues(10, 20, 30) },
                    Sleeping = !rows[0].Dynamics.Sleeping,
                }
            };
            rows[invalid.Index] = rows[invalid.Index] with
            {
                Motion = invalid.Motion,
                Dynamics = rows[invalid.Index].Dynamics with { MassBits = invalid.Mass, InverseMassBits = invalid.Inverse },
            };
            RejectMalformedMass(() => cold.RestoreDetached(snapshot with { Bodies = rows }));
            Require(JsonSerializer.Serialize(cold.Capture()) == before &&
                cold.Bodies.Select(body => body.Backend.PhysicalObjectId).SequenceEqual(resources) &&
                cold.Bodies.All(body => !body.Backend.IsWorldAttached && body.CurrentWorld is null),
                "Malformed cold mass changed native body state, identity or residency before refusal.");
        }
        var rigid = cold.Bodies.ElementAt(1);
        var nativeMalformed = rigid.Backend.ReadDynamics() with
        {
            MassBits = massTwo,
            InverseMassBits = 0,
            Transform = snapshot.Bodies[0].Dynamics.Transform with { Translation = FalloutPhysicalVector3.FromValues(30, 20, 10) },
        };
        RejectMalformedMass(() => rigid.Backend.RestoreDynamics(nativeMalformed));
        Require(JsonSerializer.Serialize(cold.Capture()) == before, "Direct native malformed-mass restore partially changed physical state.");
        var mixedRows = snapshot.Bodies.ToArray();
        mixedRows[0] = mixedRows[0] with { Dynamics = mixedRows[0].Dynamics with { MassBits = BitConverter.SingleToUInt32Bits(5f), InverseMassBits = 0 } };
        mixedRows[1] = mixedRows[1] with
        {
            Dynamics = mixedRows[1].Dynamics with { MassBits = BitConverter.SingleToUInt32Bits(3f), InverseMassBits = BitConverter.SingleToUInt32Bits(1f / 3f) }
        };
        mixedRows[2] = mixedRows[2] with { Motion = 6, Dynamics = mixedRows[2].Dynamics with { InverseMassBits = 0 } };
        var mixed = snapshot with { Bodies = mixedRows };
        cold.RestoreDetached(mixed);
        Require(SameBodies(mixed, cold.Capture()), "Valid fixed, kinematic and rigid inverse mass failed native cold reconstruction.");
    }

    private static void RejectMalformedMass(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed inverse mass was admitted as a native cold state.");
    }

    private static void ExerciseModel(FalloutNifPhysicalModel physical, FalloutNpcAppearance appearance, string key, bool requireNonempty)
    {
        using var nativeWorld = new World3D();
        var backend = new RuntimeNativeLivingPhysicsBackend("owned-component-world", () => nativeWorld.Space, .02f);
        using var model = FalloutLivingActorModel.Construct(key, new(appearance, physical, []), Inputs(), backend,
            SourceBodyFactory(physical));
        if (requireNonempty) Require(model.Bodies.Count > 0 && model.Constraints.Count > 0, "Owned native body proof requires a real nonempty physical source model.");
        using var world = new FalloutLivingPhysicalWorld(backend);
        Require(model.Bodies.All(body => body.Backend.HasPhysicalObject && !body.Backend.IsWorldAttached), "Owned source created metadata bodies.");
        foreach (var body in model.Bodies) world.AddBody(body);
        var admitted = new HashSet<FalloutLivingPhysicalConstraint>();
        foreach (var body in model.Bodies)
            foreach (var link in body.LinkedConstraints)
                if (link is FalloutLivingPhysicalConstraint constraint && admitted.Add(constraint)) world.AddConstraint(constraint);
        Require(model.Bodies.All(body => body.Backend.IsWorldAttached && body.CurrentWorld == world && body.EntityId is not null && body.Island is not null) &&
            model.Constraints.All(value => value.Backend.IsWorldAttached && value.MasterIndex is not null && value.SlaveIndex is not null),
            "Owned physical declaration count substituted for actual current physical resources.");
        var originalIds = model.Bodies.Select(body => body.Backend.PhysicalObjectId).ToHashSet();
        Require(originalIds.Count == model.Bodies.Count && model.Bodies.All(body => body.Backend.PhysicalShapeCount > 0),
            "Owned physical bodies did not have distinct actual server resources and attached shapes.");
        GD.Print("OPENNV_PRIVATE_LIVING_PHYSICAL_RESOURCES " + JsonSerializer.Serialize(new
        {
            bodies = model.Bodies.Select(body => new
            {
                source = body.Source.Block.Index,
                native = body.Backend.PhysicalObjectId,
                shapes = body.Backend.PhysicalShapeCount,
                uid = body.EntityId,
                world = body.WorldKey,
                motion = body.Motion
            }).ToArray(),
            joints = model.Constraints.Select(value => new
            {
                source = value.Source.Header.Block.Index,
                native = value.Backend.PhysicalObjectId,
                master = value.Master!.Source.Block.Index,
                slave = value.Slave!.Source.Block.Index
            }).ToArray(),
            boundary = backend.Boundary,
        }));
        foreach (var body in model.Bodies)
        {
            Require(body.ReadBounds().Minimum.XValue <= body.ReadBounds().Maximum.XValue, "Owned physical body bounds are invalid.");
            _ = body.ReadTransform(); _ = body.ReadCenterOfMass(); _ = body.ReadInverseMassBits(); _ = body.ReadTerminalShape();
        }
        var captured = model.Capture(); var generation = model.Generation;
        var structural = world.Capture();
        var islandIdentities = world.Bodies.Select(body => body.Island!.Find().Identity).ToHashSet();
        world.RetireModel(model); model.Dispose();
        using var cold = FalloutLivingActorModel.Construct(key, new(appearance, physical, []), Inputs(), backend,
            SourceBodyFactory(physical));
        cold.RestoreDetached(captured);
        Require(cold.Generation != generation && cold.Bodies.All(body => !originalIds.Contains(body.Backend.PhysicalObjectId)) &&
            SameBodies(captured, cold.Capture()), "Owned cold body state failed reconstruction with fresh physical identities.");
        using var coldWorld = new FalloutLivingPhysicalWorld(backend);
        coldWorld.Restore(structural, [cold]);
        Require(JsonSerializer.Serialize(coldWorld.Capture()) == JsonSerializer.Serialize(structural) &&
            coldWorld.Bodies.All(body => !islandIdentities.Contains(body.Island!.Find().Identity)), "Owned cold structural registry failed exact reconstruction with fresh runtime islands.");
    }

    private static bool SameBodies(FalloutLivingModelSnapshot first, FalloutLivingModelSnapshot second) =>
        first.Key == second.Key && first.SourceSha256 == second.SourceSha256 && first.Bodies.Count == second.Bodies.Count &&
        first.Bodies.Zip(second.Bodies).All(pair => pair.First.Key == pair.Second.Key && pair.First.Motion == pair.Second.Motion &&
            pair.First.CollisionFilter == pair.Second.CollisionFilter && Near(pair.First.Dynamics.Transform.Translation, pair.Second.Dynamics.Transform.Translation) &&
            Near(pair.First.Dynamics.Transform.Rotation.Column0, pair.Second.Dynamics.Transform.Rotation.Column0) &&
            Near(pair.First.Dynamics.Transform.Rotation.Column1, pair.Second.Dynamics.Transform.Rotation.Column1) &&
            Near(pair.First.Dynamics.Transform.Rotation.Column2, pair.Second.Dynamics.Transform.Rotation.Column2) &&
            Near(pair.First.Dynamics.CenterOfMass, pair.Second.Dynamics.CenterOfMass) && Near(pair.First.Dynamics.LinearVelocity, pair.Second.Dynamics.LinearVelocity) &&
            Near(pair.First.Dynamics.AngularVelocity, pair.Second.Dynamics.AngularVelocity) && pair.First.Dynamics.InverseMassBits == pair.Second.Dynamics.InverseMassBits &&
            pair.First.Dynamics.MassBits == pair.Second.Dynamics.MassBits && pair.First.Dynamics.Sleeping == pair.Second.Dynamics.Sleeping);
    private static bool Near(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second) =>
        MathF.Abs(first.XValue - second.XValue) < .00001f && MathF.Abs(first.YValue - second.YValue) < .00001f && MathF.Abs(first.ZValue - second.ZValue) < .00001f;

    private static Func<FalloutNifPhysicalSceneObject, FalloutLivingBodyConstruction> SourceBodyFactory(FalloutNifPhysicalModel model)
    {
        var identity = new FalloutPhysicalRotation(FalloutPhysicalVector3.FromValues(1, 0, 0),
            FalloutPhysicalVector3.FromValues(0, 1, 0), FalloutPhysicalVector3.FromValues(0, 0, 1));
        var constructors = FalloutLivingModelConstruction.ReadSourceRestPoses(model, Inputs(), new(identity, default), 1, false, _ => false);
        return scene => constructors[scene.Declaration.Block.Index];
    }
    private static FalloutPhysicalConstructorInputs Inputs() => new(FalloutPhysicalConstructorInputs.ReviewedEngineSha256, new(0, 0));
    private static FalloutNpcAppearance FixtureAppearance(string path)
    {
        var key = new FalloutFormKey("AuthoredFixture.esm", 1); var face = new FalloutNpcFaceGen(key, [], [], []);
        return new(key, null, key, key, key, false, key, path, 1, 1, [], [], [], [], face, face, [], [], [], [], [], [], null, null);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or ObjectDisposedException) { return; }
        throw new InvalidDataException("Invalid current physical state was admitted.");
    }
}
