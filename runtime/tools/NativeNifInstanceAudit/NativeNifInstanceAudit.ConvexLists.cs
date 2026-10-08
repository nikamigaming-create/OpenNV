using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseConvexLists()
    {
        RequireCollisionLifetimeTelemetry();
        foreach (var dynamic in new[] { false, true })
            await RequireSyntheticConvexListShapes(dynamic);
        await RequireIndependentConvexLists(FalloutConvexListFixture.Create(parentMaterial: 11), false, materialUnbound: true);
        await RequireWideNativeConvexList();
        await SettleCollisionLifetimeResources();
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        foreach (var dynamic in new[] { false, true })
        {
            var empty = FalloutConvexListFixture.Create(dynamic, children: []);
            var cycle = FalloutConvexListFixture.Create(dynamic, children: [4, 3]);
            var concave = FalloutConvexListFixture.Create(dynamic, concaveSecond: true);
            var negative = FalloutConvexListFixture.Rewrite(FalloutConvexListFixture.Create(dynamic), 3,
                data => BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16), -1));
            foreach (var (bytes, error) in new[] { (empty, "convex list shape 3 is empty"), (cycle, "cycle at block 3"),
                (concave, "non-convex packed shape 5"), (negative, "negative shell radius") })
            {
                var original = SHA256.HashData(bytes);
                RequireCollisionBuildRefusal(FalloutNifFile.Read(bytes), error);
                if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(original))
                    throw new InvalidDataException("Rejected compound construction changed its original source bytes.");
            }
            await RequireSyntheticConvexListShapes(dynamic);
        }
        await RequireWideNativeConvexList();
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Repeated compound-convex construction/retirement retained native resources.");
        GD.Print("OPENNV_NATIVE_CONVEX_LIST_CONTRACT_PASS actualReader=true authoredLeaves=true convexGapClear=true " +
            "transformedContacts=true childTransformContacts=true nestedListContacts=true wide256Contacts=true static=true dynamicMassPreserved=true independentBodiesShapes=true " +
            "partialFailureCleanup=true cycleRefused=true concaveChildRefused=true parentMaterialDriftRefused=true " +
            "sourceBytes=unchanged nativeResourceGrowth=0 retailQueriesDynamicsPixels=unverified");
    }

    private async Task RequireSyntheticConvexListShapes(bool dynamic)
    {
        foreach (var transformed in new[] { false, true })
            await RequireIndependentConvexLists(FalloutConvexListFixture.Create(dynamic, transformed), transformed);
        await RequireIndependentConvexLists(FalloutConvexListFixture.Nested(dynamic), false, expectedPath: [3, 6]);
        await RequireIndependentConvexLists(FalloutConvexListFixture.ChildTransformed(dynamic), false,
            expectedCenters: [new(-2.1f, 0, -2.1f), new(2.1f, 0, 0)]);
    }

    private async Task RequireWideNativeConvexList()
    {
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var bytes = FalloutConvexListFixture.Wide(); var original = SHA256.HashData(bytes);
        var source = FalloutNifFile.Read(bytes);
        RuntimeNativeNifScene? scene = null;
        try
        {
            scene = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            var body = scene.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Single();
            var shapes = body.GetChildren().OfType<CollisionShape3D>().ToArray();
            if (scene.CollisionShapes != 256 || shapes.Length != 256 || shapes.Select(shape => shape.Shape.GetRid()).Distinct().Count() != 256)
                throw new InvalidDataException("Backed wide convex collection omitted or shared authored native leaves.");
            AddChild(scene.Root);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var world = body.GetWorld3D().DirectSpaceState;
            for (var index = 0; index < shapes.Length; ++index)
            {
                var center = new Vector3(index * 2.1f, 0, 0);
                using var query = PhysicsRayQueryParameters3D.Create(center + Vector3.Up * 4, center - Vector3.Up * 4, 1);
                var hit = world.IntersectRay(query);
                if (hit.Count == 0 || hit["collider"].AsGodotObject() != body ||
                    body.ShapeOwnerGetOwner(body.ShapeFindOwner(hit["shape"].AsInt32())) != shapes[index])
                    throw new InvalidDataException($"Wide source convex leaf {index} has no original native contact owner.");
            }
            scene.Root.Free(); scene = null;
        }
        finally { scene?.Root.Free(); }
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before || !SHA256.HashData(bytes).AsSpan().SequenceEqual(original))
            throw new InvalidDataException("Wide convex collection changed source bytes or orphaned native owners.");
    }

    private async Task RequireIndependentConvexLists(byte[] bytes, bool transformed, bool materialUnbound = false,
        int[]? expectedPath = null, Vector3[]? expectedCenters = null)
    {
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var hash = SHA256.HashData(bytes);
        RuntimeNativeNifScene? first = null, second = null;
        try
        {
            var source = FalloutNifFile.Read(bytes);
            var declaredBody = (FalloutNifRigidBody)source.ReadObject(2);
            first = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            second = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            var firstBody = first.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Single();
            var secondBody = second.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Single();
            if (firstBody is RigidBody3D firstRigid) firstRigid.Freeze = true;
            if (secondBody is RigidBody3D secondRigid) secondRigid.Freeze = true;
            if (declaredBody.Mass > 0 && (firstBody is not RuntimeNifRigidBody rigid || rigid.Mass != declaredBody.Mass))
                throw new InvalidDataException("Compound leaves duplicated/replaced the original dynamic mass owner.");
            var firstShapes = firstBody.GetChildren().OfType<CollisionShape3D>().ToArray();
            var secondShapes = secondBody.GetChildren().OfType<CollisionShape3D>().ToArray();
            if (first.CollisionBodies != 1 || second.CollisionBodies != 1 || first.CollisionShapes != 2 || second.CollisionShapes != 2 ||
                firstBody.GetRid() == secondBody.GetRid() || firstShapes.Length != 2 || secondShapes.Length != 2 ||
                firstShapes.Where((node, index) => node.Shape.GetRid() == secondShapes[index].Shape.GetRid()).Any() ||
                firstBody.GetMeta("opennv_collision_havok_filter").AsUInt32() != 0x45672407U ||
                firstBody.GetMeta("opennv_collision_havok_info_filter").AsUInt32() != 0x12343108U)
                throw new InvalidDataException("Compound lists shared native resources or lost their parent/source geometry owner.");
            foreach (var (node, index) in secondShapes.Select((node, index) => (node, index)))
                if (node.Shape is not ConvexPolygonShape3D || node.GetMeta("opennv_nif_shape_block").AsInt32() != index + 4 ||
                    !node.GetMeta("opennv_nif_convex_list_path").AsInt32Array().SequenceEqual(expectedPath ?? [3]))
                    throw new InvalidDataException("Compound source leaf/order/path was replaced with a merged hull or proxy.");
            var unchanged = ((ConvexPolygonShape3D)secondShapes[0].Shape).Points;
            ((ConvexPolygonShape3D)firstShapes[0].Shape).Points = ((ConvexPolygonShape3D)firstShapes[0].Shape).Points.Select(point => point * .9f).ToArray();
            if (!((ConvexPolygonShape3D)secondShapes[0].Shape).Points.SequenceEqual(unchanged))
                throw new InvalidDataException("Editing one native convex instance changed its independent sibling.");
            first.Root.Position = new(-10, 0, 0); second.Root.Position = new(10, 0, 0);
            AddChild(first.Root); AddChild(second.Root);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var world = secondBody.GetWorld3D().DirectSpaceState;
            var centers = expectedCenters ?? (transformed ? new[] { new Vector3(1.4f, 2.1f, 1.4f), new Vector3(1.4f, 2.1f, -2.8f) } :
                new[] { new Vector3(-2.1f, 0, 0), new Vector3(2.1f, 0, 0) });
            var origin = new Vector3(10, 0, 0);
            Godot.Collections.Dictionary Ray(Vector3 center)
            {
                using var query = PhysicsRayQueryParameters3D.Create(origin + center + Vector3.Up * 4,
                    origin + center - Vector3.Up * 4, 1);
                return world.IntersectRay(query);
            }
            foreach (var center in centers)
            {
                var hit = Ray(center);
                if (hit.Count == 0 || hit["collider"].AsGodotObject() != secondBody)
                    throw new InvalidDataException("An authored convex leaf has no real physics contact at its independently calculated pose.");
                try
                {
                    var material = NativeNifCollisionBuilder.HitMaterial(hit);
                    if (materialUnbound || material != 7) throw new InvalidDataException("Compound material ownership guessed native precedence.");
                }
                catch (NotSupportedException error) when (materialUnbound && error.Message.Contains("parent/leaf materials", StringComparison.Ordinal)) { }
            }
            if (Ray((centers[0] + centers[1]) / 2).Count != 0)
                throw new InvalidDataException("Compound collision filled the authored empty gap between convex leaves.");
            first.Root.Free(); first = null;
            if (Ray(centers[0]).Count == 0 || Ray(centers[1]).Count == 0)
                throw new InvalidDataException("Retiring one compound instance removed its independent sibling contacts.");
            second.Root.Free(); second = null;
            if (Ray(centers[0]).Count != 0 || Ray(centers[1]).Count != 0)
                throw new InvalidDataException("Retired convex leaves still collide in the native world.");
        }
        finally { first?.Root.Free(); second?.Root.Free(); }
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before || !SHA256.HashData(bytes).AsSpan().SequenceEqual(hash))
            throw new InvalidDataException("Compound native lifetime orphaned nodes or changed its source fixture.");
    }

    private async Task ExerciseOwnedConvexLists(string root, string model, string[] options)
    {
        RequireCollisionLifetimeTelemetry();
        using var content = OpenConvexListContent(root, options);
        if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
        var original = SHA256.HashData(bytes);
        var readRanges = new List<FalloutNifReadRange>();
        var source = FalloutNifFile.Read(bytes, readRanges.Add);
        var lists = source.Blocks.Where(block => block.TypeName == "bhkConvexListShape")
            .Select(block => (FalloutNifConvexListShape)source.ReadObject(block.Index)).ToArray();
        if (lists.Length == 0) throw new InvalidDataException("Selected owned model has no convex-list declaration.");
        foreach (var list in lists)
        {
            var ranges = readRanges.Where(range => range.Owner == $"NIF block {list.Block.Index} (bhkConvexListShape)").ToArray();
            if (list.Block.Size != checked(37 + sizeof(int) * list.Children.Length) ||
                ranges.Sum(range => range.Length) != list.Block.Size || ranges[0].Offset != list.Block.Offset ||
                ranges[^1].Offset + ranges[^1].Length != list.Block.Offset + list.Block.Size)
                throw new InvalidDataException("Owned convex-list field reads do not account for the complete original block.");
        }
        // Whole original model assembly is deliberately required. An independent
        // body-only diagnostic cannot silently accept another failed model owner.
        await RequireOwnedConvexInstances();
        await SettleCollisionLifetimeResources();
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        await RequireOwnedConvexInstances();
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources ||
            !content.TryRead(model, null, out var after, out var afterIdentity) || afterIdentity != identity ||
            !SHA256.HashData(after).AsSpan().SequenceEqual(original))
            throw new InvalidDataException("Repeated owned compound assembly grew native resources or changed source identity/bytes.");
        GD.Print($"OPENNV_OWNED_CONVEX_LIST_PASS source={identity} sha256={Convert.ToHexString(original)} stack={content.StackId} " +
            $"lists={lists.Length} children={lists.Sum(list => list.Children.Length)} readCoverage=complete originalModel=true actualContacts=true " +
            "independentInstances=true originalBodyMass=true sourceFiltersRetained=true sourceBytes=unchanged nativeResourceGrowth=0 " +
            "ordinaryDoorLightBehavior=false fullFilterRetailPhysicsMaterialNoiseQueriesPixels=unverified");

        async Task RequireOwnedConvexInstances()
        {
            var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            RuntimeNativeNifScene? first = null, second = null;
            try
            {
                first = RuntimeNativeNifMeshBuilder.Build(source, .0142875f, contentSource: content);
                second = RuntimeNativeNifMeshBuilder.Build(source, .0142875f, contentSource: content);
                first.Root.Position = new(-50, 0, 0); second.Root.Position = new(50, 0, 0);
                var firstBodies = first.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().ToArray();
                var secondBodies = second.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().ToArray();
                foreach (var body in firstBodies.Concat(secondBodies).OfType<RigidBody3D>()) body.Freeze = true;
                if (firstBodies.Length != secondBodies.Length || first.CollisionBodies != source.Blocks.Count(block =>
                    block.TypeName is "bhkCollisionObject" or "bhkSPCollisionObject" or "bhkBlendCollisionObject"))
                    throw new InvalidDataException("Whole owned model omitted an original independent collision owner.");
                for (var index = 0; index < secondBodies.Length; ++index)
                {
                    var native = secondBodies[index];
                    var declared = (FalloutNifRigidBody)source.ReadObject(native.GetMeta("opennv_nif_collision_body").AsInt32());
                    var filter = (uint)declared.Filter.Layer | ((uint)declared.Filter.Flags << 8) | ((uint)declared.Filter.Group << 16);
                    var infoFilter = (uint)declared.InfoFilter.Layer | ((uint)declared.InfoFilter.Flags << 8) | ((uint)declared.InfoFilter.Group << 16);
                    if (firstBodies[index].GetRid() == native.GetRid() || native.GetMeta("opennv_collision_havok_filter").AsUInt32() != filter ||
                        native.GetMeta("opennv_collision_havok_info_filter").AsUInt32() != infoFilter ||
                        native.GetMeta("opennv_nif_collision_mass").AsSingle() != declared.Mass ||
                        native is RigidBody3D dynamic && dynamic.Mass != declared.Mass)
                        throw new InvalidDataException("Owned compound lost an original mass/filter owner or shared its native body.");
                }
                var firstShapes = first.Root.FindChildren("*", "", true, false).OfType<CollisionShape3D>().ToArray();
                var secondShapes = second.Root.FindChildren("*", "", true, false).OfType<CollisionShape3D>().ToArray();
                if (firstShapes.Length != secondShapes.Length || firstShapes.Where((shape, index) => shape.Shape.GetRid() == secondShapes[index].Shape.GetRid()).Any())
                    throw new InvalidDataException("Whole owned model shared native collision resources.");
                foreach (var list in lists)
                {
                    var bound = secondShapes.Where(shape => shape.HasMeta("opennv_nif_convex_list_path") &&
                        shape.GetMeta("opennv_nif_convex_list_path").AsInt32Array().Contains(list.Block.Index)).ToArray();
                    if (bound.Length != CountLeaves(source, list.Block.Index, []))
                        throw new InvalidDataException("Owned compound omitted a source convex descendant.");
                }
                AddChild(first.Root); AddChild(second.Root);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var leaves = secondShapes.Where(shape => shape.HasMeta("opennv_nif_convex_list_path")).ToArray();
                if (leaves.Length == 0) throw new InvalidDataException("Owned compound published no native convex leaves.");
                var world = leaves[0].GetWorld3D().DirectSpaceState;
                var centers = leaves.Select(shape =>
                {
                    if (shape.Shape is not ConvexPolygonShape3D convex || convex.Points.Length < 4)
                        throw new NotSupportedException("This owned vertex-contact proof requires actual decoded convex-vertex leaves.");
                    return shape.GlobalTransform * (convex.Points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / convex.Points.Length);
                }).ToArray();
                using var sphere = new SphereShape3D { Radius = .005f };
                using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, CollisionMask = 1 };
                void Contacts()
                {
                    for (var index = 0; index < leaves.Length; ++index)
                    {
                        query.Transform = new(Basis.Identity, centers[index]);
                        var expectedBody = leaves[index].GetParent<PhysicsBody3D>();
                        if (!world.IntersectShape(query, 64).Any(hit => hit["collider"].AsGodotObject() == expectedBody &&
                            expectedBody.ShapeOwnerGetOwner(expectedBody.ShapeFindOwner(hit["shape"].AsInt32())) == leaves[index]))
                            throw new InvalidDataException("Owned convex leaf has no actual native contact at its source interior.");
                    }
                }
                Contacts(); first.Root.Free(); first = null; Contacts();
                second.Root.Free(); second = null;
                foreach (var center in centers)
                {
                    query.Transform = new(Basis.Identity, center);
                    if (world.IntersectShape(query, 64).Count != 0)
                        throw new InvalidDataException("Retired owned model retains native collision at its original source leaves.");
                }
            }
            finally { first?.Root.Free(); second?.Root.Free(); }
            if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before)
                throw new InvalidDataException("Owned compound lifetime orphaned model nodes.");
        }
    }

    private static RuntimeLiveContentSource OpenConvexListContent(string root, string[] options)
    {
        FalloutModStackSelection? selection = options switch
        {
            [] => null,
            ["--mod-stack", var path] => FalloutModStackSelection.ReadOptions(
                new Dictionary<string, string> { ["mod-stack"] = File.ReadAllText(Path.GetFullPath(path)) }),
            _ => throw new ArgumentException("Owned convex lists accept only optional --mod-stack JSON."),
        };
        var campaign = NativeGameInstallation.Detect(root).Game switch
        {
            NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
            NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
            _ => throw new NotSupportedException("Owned convex lists require Fallout 3 or New Vegas source."),
        };
        return selection is null ? RuntimeLiveContentSource.Open(root, campaign) : selection.Resolve(root).OpenSource();
    }

    private static int CountLeaves(FalloutNifFile source, int reference, HashSet<int> active)
    {
        if (!active.Add(reference)) throw new InvalidDataException("Owned convex descendant graph contains a cycle.");
        try
        {
            return source.ReadObject(reference) switch
            {
                FalloutNifConvexListShape list => list.Children.Sum(child => CountLeaves(source, child, active)),
                FalloutNifConvexTransformShape transform => CountLeaves(source, transform.Child, active),
                FalloutNifConvexVerticesShape or FalloutNifBoxShape or FalloutNifSphereShape or FalloutNifCapsuleShape => 1,
                _ => throw new NotSupportedException("Owned convex descendant has an independently unsupported contact owner."),
            };
        }
        finally { active.Remove(reference); }
    }
}
