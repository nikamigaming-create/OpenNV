using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeEarlyControllerShapeAudit : Node
{
    public override async void _Ready()
    {
        RuntimeLiveContentSource? content = null;
        FalloutPluginStack? records = null;
        FalloutActorProcessCommonState? common = null;
        var exit = 1;
        try
        {
            if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Early physical resource audit requires headless Godot.");
            FalloutCombatActorIdentity actor;
            FalloutPhysicalEffectiveSettings settings;
            var stack = new string('4', 64); var executable = FalloutPhysicalConstructorInputs.ReviewedEngineSha256;
            var args = OS.GetCmdlineUserArgs();
            if (args is [var dataRoot])
            {
                RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
                content = RuntimeLiveContentSource.Current!; records = FalloutPluginStack.Load(content.PluginSources);
                var declaration = FalloutCombatGroupDeclaration.ReadExecutable(content.FalloutExecutablePath);
                actor = FalloutCombatActorSource.Read(records, declaration, records.RuntimeFormKey(0x14));
                executable = declaration.ExecutableSha256; stack = content.StackId;
                settings = FalloutPhysicalEffectiveSettings.Read(records.NumericSettings.Float);
            }
            else if (args.Length == 0)
            {
                actor = new(new("ShapeFixture.esm", 0x14), new("ShapeFixture.esm", 7), "ENGINE_PLAYER", 0, executable,
                    "NPC_", 0, new string('5', 64), true);
                settings = new(0, 0);
            }
            else throw new ArgumentException("Expected zero authored arguments or one owned Fallout3 Data root.");
            common = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(executable), stack, _ => actor,
                _ => throw new InvalidOperationException("Shape construction cannot invent unrelated gameplay pools."),
                _ => throw new InvalidOperationException("Early shapes cannot borrow a late NIF/body factory."));
            common.Construct(actor.Reference, 1, FalloutDetectionProcessLevel.High);
            var actual = common.ReadSceneQueries(actor.Reference, 1);
            var inputs = new FalloutPhysicalConstructorInputs(executable, settings);
            await Exercise(inputs, actual.Actor, actual.Process);
            GD.Print($"OPENNV_NATIVE_EARLY_CONTROLLER_SHAPE_PASS ownedSource={content is not null} actualNativeShapes=true " +
                "selectedSlot1=true roundedFaceEdgeVertex=true supportBounds=true convexCacheReused=true freshCapsule=true sourceRadiusPreserved=true " +
                "nativeRetirement=true constructorScope=authored originalController=UNEXECUTED canonicalBytes=UNQUALIFIED gameplay=UNEXECUTED recording=off");
            exit = 0;
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_EARLY_CONTROLLER_SHAPE_FAIL " + error); }
        finally
        {
            foreach (var release in new Action[] { () => common?.Dispose(), () => records?.Dispose(),
                         () => { if (content is not null) RuntimeLiveContentSource.Clear(); } })
                try { release(); }
                catch (Exception error) { GD.PushError("OPENNV_EARLY_SHAPE_CLEANUP_FAIL " + error); exit = 1; }
            GetTree().Quit(exit);
        }
    }

    private async Task Exercise(FalloutPhysicalConstructorInputs inputs, FalloutCombatActorIdentity actor, Guid process)
    {
        FalloutEarlyControllerShapeInvocation? entered = null;
        var constructor = Guid.NewGuid(); var thread = System.Environment.CurrentManagedThreadId;
        var cache = new FalloutEarlyControllerShapeCache(inputs, .0142875f, invocation =>
        {
            if (invocation != entered || invocation.Actor != actor || invocation.Process != process || invocation.Epoch != 1 ||
                invocation.Constructor != constructor || invocation.ThreadId != thread || System.Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Early shape audit left its actual C# Actor/Common and authored constructor scope.");
        });
        FalloutEarlyControllerShapeLease? first = null, second = null;
        using var world = new World3D(); var body = default(Rid);
        Exception? primaryFailure = null;
        try
        {
            ExerciseFailures(inputs, actor, process, constructor, thread);
            var player = inputs.ProjectPlayer(default, default);
            entered = new(Guid.NewGuid(), actor, process, 1, constructor, thread); first = cache.Acquire(entered, player); entered = null;
            entered = new(Guid.NewGuid(), actor, process, 1, constructor, thread); second = cache.Acquire(entered, player); entered = null;
            var shapes = second.Slot(1); var geometry = second.Geometry;
            Require(first.Slot(0).Single().NativeId != second.Slot(0).Single().NativeId &&
                first.Slot(1).Select(value => value.NativeId).SequenceEqual(shapes.Select(value => value.NativeId)) &&
                cache.LastCall is { Returned: true, ReusedConvex: true, Allocated.Count: 1 } && shapes.Count > 1 &&
                geometry.Points.Count == 18 && geometry.Faces.Count >= 4 && geometry.Edges.Count > 0,
                "Actual source slots/cache were replaced by metadata or a capsule-only resource.");
            Reject(cache.Retire);
            body = PhysicsServer3D.BodyCreate(); PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
            PhysicsServer3D.BodySetCollisionLayer(body, 1); PhysicsServer3D.BodySetCollisionMask(body, 1);
            foreach (var shape in shapes) PhysicsServer3D.BodyAddShape(body, shape.Shape.GetRid(), shape.Transform);
            Require(PhysicsServer3D.BodyGetShapeCount(body) == shapes.Count && !PhysicsServer3D.BodyGetSpace(body).IsValid,
                "Shape audit silently attached source construction to a default world.");
            // Only the independent test body enters this explicit audit space.
            // This does not attach a product controller or publish its proxy.
            PhysicsServer3D.BodySetSpace(body, world.Space);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var state = PhysicsServer3D.SpaceGetDirectState(world.Space);
            using var query = new PhysicsPointQueryParameters3D { CollisionMask = 1, CollideWithAreas = false, CollideWithBodies = true };
            var scale = second.PhysicalToNative; var radius = BitConverter.UInt32BitsToSingle(geometry.RadiusBits);
            var delta = .00005f / scale; var probes = 0;
            foreach (var face in geometry.Faces)
            {
                var center = FalloutEarlyControllerConvexGeometry.FaceCenter(face, geometry.Points);
                Boundary(center, face.OutwardNormal);
            }
            foreach (var edge in geometry.Edges)
            {
                var faces = geometry.Faces.Where(face => face.Vertices.Contains(edge.First) && face.Vertices.Contains(edge.Second)).ToArray();
                Require(faces.Length == 2, "Geometric edge has no two source boundary regions.");
                Boundary(Midpoint(geometry.Points[edge.First], geometry.Points[edge.Second]), Normalize(Add(faces[0].OutwardNormal, faces[1].OutwardNormal)));
            }
            foreach (var vertex in geometry.Vertices)
            {
                var direction = geometry.Faces.Where(face => face.Vertices.Contains(vertex)).Select(face => face.OutwardNormal)
                    .Aggregate(default(FalloutPhysicalVector3), Add);
                Boundary(geometry.Points[vertex], Normalize(direction));
            }
            foreach (var direction in new[] { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
                         new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) })
            {
                var physical = FalloutPhysicalVector3.FromValues(direction.X, direction.Y, direction.Z);
                var support = geometry.Support(physical);
                Contains(Offset(support, physical, -delta), true); Contains(Offset(support, physical, delta), false);
            }
            first.Dispose(); first = null;
            foreach (var face in geometry.Faces) Boundary(FalloutEarlyControllerConvexGeometry.FaceCenter(face, geometry.Points), face.OutwardNormal);
            var changedInvocation = new FalloutEarlyControllerShapeInvocation(Guid.NewGuid(), actor, process, 1, constructor, thread);
            entered = changedInvocation;
            Reject(() => cache.Acquire(changedInvocation, inputs.ProjectPlayer(default, FalloutPhysicalVector3.FromValues(3, 5, 0))));
            entered = null;
            Require(cache.LastCall is { Returned: false, ReusedConvex: true, Allocated.Count: 1, ReturnedNativeShapes.Count: 0 } &&
                cache.SaveBlocker == "source-shared-standard-controller-shape-current-input-writer-unbound" &&
                second.Slot(1).Select(value => value.NativeId).SequenceEqual(shapes.Select(value => value.NativeId)),
                "Changed current shape inputs lost their failed native prefix or retired another real borrower.");
            GD.Print($"OPENNV_EARLY_SHAPE_GEOMETRY nativeLeaves={shapes.Count} faces={geometry.Faces.Count} edges={geometry.Edges.Count} " +
                $"vertices={geometry.Vertices.Count} pointProbes={probes} sourceRadiusBits={geometry.RadiusBits:X8} probeBody={body.Id}");

            void Boundary(FalloutPhysicalVector3 center, FalloutPhysicalVector3 normal)
            { Contains(Offset(center, normal, radius - delta), true); Contains(Offset(center, normal, radius + delta), false); }
            void Contains(FalloutPhysicalVector3 point, bool expected)
            {
                query.Position = new(point.XValue * scale, point.ZValue * scale, -point.YValue * scale);
                var actual = state.IntersectPoint(query, 128).Any(hit => hit["rid"].AsRid() == body);
                ++probes;
                Require(actual == expected, $"Native rounded shape changed face/edge/vertex/bounds occupancy at {query.Position}: expected={expected}, actual={actual}.");
            }
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            entered = null;
            var cleanup = new List<Exception>();
            foreach (var release in new Action[] {
                         () => { if (body.IsValid) PhysicsServer3D.BodySetSpace(body, default); },
                         () => { if (body.IsValid) PhysicsServer3D.FreeRid(body); },
                         () => first?.Dispose(), () => second?.Dispose(), cache.Retire })
                try { release(); } catch (Exception error) { cleanup.Add(error); }
            if (cleanup.Count != 0)
            {
                if (primaryFailure is null) throw new AggregateException("Early shape native retirement failed.", cleanup);
                foreach (var error in cleanup) GD.PushError("OPENNV_EARLY_SHAPE_CLEANUP_FAIL " + error);
            }
        }
    }

    private static void ExerciseFailures(FalloutPhysicalConstructorInputs inputs, FalloutCombatActorIdentity actor,
        Guid process, Guid constructor, int thread)
    {
        var player = inputs.ProjectPlayer(default, default);
        var invocation = new FalloutEarlyControllerShapeInvocation(Guid.NewGuid(), actor, process, 1, constructor, thread);
        var guardCount = 0;
        var failed = new FalloutEarlyControllerShapeCache(inputs, .0142875f, entered =>
        {
            Require(entered == invocation && System.Environment.CurrentManagedThreadId == thread, "Failure fixture lost its actual source scope.");
            if (++guardCount == 3) throw new InvalidOperationException("authored-after-native-capsule-allocation");
        });
        try
        {
            Reject(() => failed.Acquire(invocation, player));
            Require(failed.LastCall is { Returned: false, Allocated.Count: 1, ReturnedNativeShapes.Count: 0 } call &&
                call.Allocated[0] != 0 && failed.SaveBlocker == "authored-after-native-capsule-allocation",
                "Failed actual native allocation lost its original failure or resource prefix.");
            var retained = failed.LastCall;
            Reject(() => failed.Acquire(invocation, player));
            Require(ReferenceEquals(retained, failed.LastCall), "A failed construction was replaced by a successful or replayed call.");
        }
        finally { failed.Retire(); }

        FalloutEarlyControllerShapeCache? reentry = null;
        reentry = new(inputs, .0142875f, entered =>
        {
            Require(entered == invocation, "Reentry fixture lost its actual source invocation.");
            Reject(() => reentry!.Acquire(invocation, player));
        });
        try
        {
            Reject(() => reentry.Acquire(invocation, player));
            Require(reentry.LastCall is { Returned: false, Allocated.Count: 0 } &&
                reentry.SaveBlocker == "Early shape guard swallowed reentry or replaced its current source owner.",
                "Swallowed reentry admitted an unrelated native shape construction.");
        }
        finally { reentry.Retire(); }
    }
    private static FalloutPhysicalVector3 Add(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second) =>
        FalloutPhysicalVector3.FromValues(first.XValue + second.XValue, first.YValue + second.YValue, first.ZValue + second.ZValue);
    private static FalloutPhysicalVector3 Midpoint(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second) =>
        FalloutPhysicalVector3.FromValues((first.XValue + second.XValue) * .5f, (first.YValue + second.YValue) * .5f, (first.ZValue + second.ZValue) * .5f);
    private static FalloutPhysicalVector3 Offset(FalloutPhysicalVector3 point, FalloutPhysicalVector3 normal, float value) =>
        FalloutPhysicalVector3.FromValues(point.XValue + normal.XValue * value, point.YValue + normal.YValue * value, point.ZValue + normal.ZValue * value);
    private static FalloutPhysicalVector3 Normalize(FalloutPhysicalVector3 value)
    {
        var normal = new Vector3(value.XValue, value.YValue, value.ZValue).Normalized();
        return FalloutPhysicalVector3.FromValues(normal.X, normal.Y, normal.Z);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Native early shape audit expected an actual lifetime refusal.");
    }
}
