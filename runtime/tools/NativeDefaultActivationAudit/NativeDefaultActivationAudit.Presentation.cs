using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeDefaultActivationAudit
{
    private async Task CorpsePhysicsSync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await Deferred();
    }

    // The diagnostic observer changes its own view. It never changes the
    // authored actor/reference, body transforms, source shapes or floor.
    private async Task ExerciseCorpseRayActivation(RuntimeNativeNpc actor, RuntimeNativeReferenceEvents events,
        FalloutFormKey identity, RuntimeLiveContentSource content, string phase)
    {
        var configuration = RuntimeConfiguration.Load();
        DesktopInputMap.Configure(configuration.Player.DesktopInput);
        var settings = FalloutInstallationSettings.Read(content);
        var controls = new FalloutInputControls(name => settings.Require("Controls", name));
        var probe = new RuntimeNativePlayer(); AddChild(probe);
        var root = actor.GlobalTransform;
        var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
        var bodies = rig.GetChildren().OfType<RigidBody3D>().ToArray();
        var source = bodies.Select(body => body.GlobalTransform).ToArray();
        var calls = 0; var accepted = 0; var feedback = 0;
        StaticBody3D? blocker = null;
        try
        {
            probe.Configure(configuration, new(Basis.Identity, actor.GlobalPosition), FalloutCameraProjection.Read(settings));
            probe.ConfigureInputControls(controls, _ => false);
            probe.SetProcess(false); probe.SetPhysicsProcess(false);
            probe.ActivateReference = contact => { calls++; var result = events.TryActivate(contact); if (result) accepted++; return result; };
            probe.NoActivationFeedback = () => feedback++;
            var key = controls.Get(5); if (key < 0) key = controls.Get(5, 1);
            Require(key >= 0, "Source Activate has no admitted keyboard/mouse binding.");
            void Input()
            {
                using var input = NativeScriptKeys.Create(key);
                if (input is InputEventKey keyboard) keyboard.Pressed = true;
                else if (input is InputEventMouseButton mouse) mouse.Pressed = true;
                else throw new NotSupportedException("Source activation event has no pressed native adapter.");
                probe._UnhandledInput(input);
            }
            bool Corpse(Node? node) => node is RigidBody3D && actor.IsAncestorOf(node) &&
                events.AimedReference(node)?.FormKey == identity;
            void View(Vector3 from, Vector3 target)
            {
                probe.Camera.GlobalPosition = from;
                var direction = (target - from).Normalized();
                probe.Camera.LookAt(target, MathF.Abs(direction.Dot(Vector3.Up)) > .9f ? Vector3.Forward : Vector3.Up);
            }
            await CorpsePhysicsSync();
            Require(rig.Active && bodies.Length > 0 && bodies.All(body => body.Freeze &&
                (body.CollisionLayer & (probe.CollisionMask | probe.CollisionLayer)) != 0),
                "Real held source corpse colliders are not in the ordinary player ray filter.");
            Vector3 selected = default; Vector3 observer = default; Node? hit = null;
            Vector3[] directions = [Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back, Vector3.Up, Vector3.Down];
            foreach (var point in rig.AimPoints)
            {
                foreach (var direction in directions)
                {
                    var from = point + direction * (configuration.Player.ActivationDistanceMeters * .65f);
                    View(from, point);
                    if (probe.AimedObject() is not { } candidate || !Corpse(candidate)) continue;
                    selected = point; observer = from; hit = candidate; break;
                }
                if (hit is not null) break;
            }
            Require(hit is not null, "No real source ragdoll body was pickable through the ordinary source-filtered ray.");
            var extent = 0f;
            foreach (var shape in bodies.SelectMany(body => body.GetChildren().OfType<CollisionShape3D>()))
            {
                Require(shape.Shape is not null && !shape.Disabled, "Source corpse lost an admitted live collision shape.");
                var bound = shape.Shape!.GetDebugMesh().GetAabb();
                for (var corner = 0; corner < 8; corner++)
                    extent = Math.Max(extent, (shape.GlobalTransform * bound.GetEndpoint(corner)).DistanceTo(selected));
            }
            var away = (observer - selected).Normalized();
            View(selected + away * (extent + configuration.Player.ActivationDistanceMeters + .25f), selected);
            Require(!Corpse(probe.AimedObject()), "Out-of-range ordinary ray still hit the source corpse.");
            Input(); Require(calls == 0 && accepted == 0 && feedback == 1,
                "Out-of-range ordinary activation reached the reference queue.");
            View(observer, selected);
            blocker = new StaticBody3D { CollisionLayer = probe.CollisionMask | probe.CollisionLayer, CollisionMask = 0 };
            blocker.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * .08f } });
            AddChild(blocker); blocker.GlobalPosition = observer.Lerp(selected, .1f);
            await CorpsePhysicsSync();
            Require(probe.AimedObject() == blocker, "Ordinary corpse ray bypassed the explicit diagnostic occluder.");
            Input(); Require(calls == 1 && accepted == 0 && feedback == 2,
                "Occluded ordinary activation was admitted through an unrelated collider.");
            blocker.Free(); blocker = null; await CorpsePhysicsSync();
            Require(Corpse(probe.AimedObject()), "Removing only the diagnostic occluder did not restore the actual corpse ray.");
            Input(); Input();
            Require(calls == 3 && accepted == 1 && feedback == 3,
                "Source-bound native input did not queue exactly once or rejected its real body/default owner.");
            Require(actor.GlobalTransform == root && bodies.Select(body => body.GlobalTransform).SequenceEqual(source),
                "Corpse ray/input fixture moved a source reference or source body.");
            GD.Print($"OPENNV_CORPSE_ORDINARY_RAY_PASS phase={phase} reference={identity} sourceControl={key} " +
                $"sourceBodies={bodies.Length} distance={configuration.Player.ActivationDistanceMeters:R} " +
                "outOfRangeRefused=true occlusionRefused=true actualBody=true pendingOnce=true sourcePoseUnchanged=true observerOnly=true campaign=false");
        }
        finally { if (blocker is not null) blocker.Free(); probe.Free(); }
    }

    private sealed record SkinBoneRow(string Name, int Parent, float[] Global);
    private sealed record BodyBoneRow(int SourceBody, int SourceNode, string Bone, float[] ActualWorld, float[] ExpectedWorld);
    private sealed record SkinVertexRow(int Surface, int Vertex, int SourceVertex, float[] World);
    private sealed record SkinMeshRow(string Identity, string Model, string ModelSha256, string[] Bones,
        float[][] ExpectedPalette, float[][]? RendererPalette, SkinVertexRow[] Vertices, float[] WorldBounds, float[] WorldTransform);
    private sealed record CorpseSkinFrame(BodyBoneRow[] BodyBindings, SkinBoneRow[] Bones, SkinMeshRow[] Meshes,
        int RendererRows, int MissingRendererMeshes);

    private async Task<CorpseSkinFrame> ObserveCorpseSkin(RuntimeNativeNpc actor, RuntimeLiveContentSource content,
        string phase, bool requireRenderer)
    {
        var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
        Require(rig.Active && actor.Combat?.Dead == true && actor.AnimationError is null && actor.AppearanceError is null,
            "Source corpse has no error-free active native skeleton publication owner.");
        // Exercise the existing publisher against the held real source body
        // transforms. Do not create a collapse, fake bone pose or alternate rig.
        await Deferred(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await Deferred();
        // Natural initial physics can advance during the waits. Publish and
        // inspect one current body generation without yielding between them.
        rig._Process(0);
        var skeleton = actor.Skeleton.Node;
        var bodyBindings = new List<BodyBoneRow>();
        foreach (var body in rig.GetChildren().OfType<RigidBody3D>())
        {
            var sourceBody = body.GetMeta("opennv_nif_collision_body").AsInt32();
            var name = body.GetMeta("opennv_nif_collision_bone").AsString();
            var owners = actor.Skeleton.Source.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
                .Select(block => actor.Skeleton.Source.ReadNode(block.Index)).Where(node => node.CollisionObject >= 0 &&
                    ((FalloutNifCollisionObject)actor.Skeleton.Source.ReadObject(node.CollisionObject)).Body == sourceBody).ToArray();
            Require(owners.Length == 1 && owners[0].Name == name, "Actual corpse body lacks its unique exact source bone owner.");
            var collision = (FalloutNifCollisionObject)actor.Skeleton.Source.ReadObject(owners[0].CollisionObject);
            var built = NativeNifCollisionBuilder.Build(actor.Skeleton.Source, collision, actor.Skeleton.UnitsToMetres, 2);
            try
            {
                var expected = body.GlobalTransform * built.Body.Transform.AffineInverse();
                var actual = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(actor.Skeleton.BoneIndex(name));
                if (!Near(Matrix(expected), Matrix(actual)))
                    GD.Print("OPENNV_CORPSE_BODY_BONE_DIVERGENCE " + System.Text.Json.JsonSerializer.Serialize(new
                    {
                        phase,
                        name,
                        sourceBody,
                        sourceNode = owners[0].Block.Index,
                        body = Matrix(body.GlobalTransform),
                        attachment = Matrix(built.Body.Transform),
                        expected = Matrix(expected),
                        actual = Matrix(actual),
                        skeleton = Matrix(skeleton.GlobalTransform),
                        frozen = body.Freeze,
                        sleeping = body.Sleeping,
                    }));
                Require(Near(Matrix(expected), Matrix(actual)),
                    "Published real corpse skeleton still differs from its actual body/source attachment frame: " + name);
                bodyBindings.Add(new(sourceBody, owners[0].Block.Index, name, Matrix(actual), Matrix(expected)));
            }
            finally { built.Body.Free(); }
        }
        Require(bodyBindings.Count > 0 && bodyBindings.Select(row => row.SourceBody).Distinct().Count() == bodyBindings.Count,
            "Real corpse body/source-bone graph is incomplete or duplicated.");
        var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index =>
            new SkinBoneRow(skeleton.GetBoneName(index).ToString(), skeleton.GetBoneParent(index), Matrix(skeleton.GetBoneGlobalPose(index)))).ToArray();
        var rows = new List<SkinMeshRow>(); var rendererRows = 0; var missing = 0;
        foreach (var part in actor.Parts)
        {
            var model = part.Root.GetMeta("opennv_source_model").AsString();
            if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            foreach (var mesh in part.Root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                if (mesh.Skin is not { } skin) continue;
                Require(mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) == skeleton && skin.GetBindCount() > 0,
                    "A source hardware skin does not resolve the real corpse skeleton.");
                var reference = mesh.GetSkinReference();
                Require(reference is not null && reference.GetSkin().GetInstanceId() == skin.GetInstanceId(),
                    "Actual mesh has no matching registered native SkinReference.");
                var bindNames = new string[skin.GetBindCount()]; var palette = new Transform3D[skin.GetBindCount()];
                for (var index = 0; index < palette.Length; index++)
                {
                    var bone = skin.GetBindBone(index); bindNames[index] = skin.GetBindName(index).ToString();
                    Require(bone >= 0 && bone < bones.Length && skeleton.GetBoneName(bone).ToString() == bindNames[index],
                        "Source palette name/index does not identify the same actual corpse bone.");
                    palette[index] = skeleton.GetBoneGlobalPose(bone) * skin.GetBindPose(index);
                }
                var rid = reference!.GetSkeleton();
                var count = rid.IsValid ? RenderingServer.SkeletonGetBoneCount(rid) : 0;
                Require(count == 0 || count == palette.Length, "Renderer skeleton buffer has a different source palette extent.");
                Transform3D[]? renderer = null;
                if (count == 0) { missing++; Require(!requireRenderer, "Renderer skin buffer is unavailable; no renderer claim can be made."); }
                else
                {
                    renderer = Enumerable.Range(0, count).Select(index => RenderingServer.SkeletonBoneGetTransform(rid, index)).ToArray();
                    Require(renderer.Zip(palette).All(pair => Near(Matrix(pair.First), Matrix(pair.Second))),
                        "Native render palette disagrees with the real corpse skeleton/bind publication.");
                    rendererRows += count;
                }
                var sampled = new List<SkinVertexRow>();
                var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    Require(vertices.Length > 0 && indices.Length == weights.Length && indices.Length % vertices.Length == 0,
                        "Actual source hardware arrays have no complete influence owner.");
                    var influences = indices.Length / vertices.Length;
                    Require(influences is 4 or 8, "Actual source hardware skin has an unowned influence extent.");
                    var map = mesh.GetMeta("opennv_nif_skin_vertex_map").AsInt32Array();
                    Require(map.Length == vertices.Length, "Actual partition lost its exact original vertex mapping.");
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var expected = Vector3.Zero; var rendered = Vector3.Zero; var total = 0f;
                        for (var influence = 0; influence < influences; influence++)
                        {
                            var offset = vertex * influences + influence; var bind = indices[offset]; var weight = weights[offset];
                            Require(float.IsFinite(weight) && weight >= 0 && bind >= 0 && bind < palette.Length,
                                "Sampled source vertex has an invalid actual palette influence.");
                            expected += (palette[bind] * vertices[vertex]) * weight; total += weight;
                            if (renderer is not null) rendered += (renderer[bind] * vertices[vertex]) * weight;
                        }
                        Require(MathF.Abs(total - 1) < .001f && (renderer is null || rendered.DistanceTo(expected) < .0001f),
                            "Native weighted corpse vertex disagrees with its actual source palette.");
                        var world = mesh.GlobalTransform * expected;
                        Require(world.IsFinite(), "Actual source weighted corpse vertex is not finite.");
                        minimum = minimum.Min(world); maximum = maximum.Max(world);
                        if (vertex == 0 || vertex == vertices.Length / 2 || vertex == vertices.Length - 1)
                            sampled.Add(new(surface, vertex, map[vertex], [world.X, world.Y, world.Z]));
                    }
                }
                Require(mesh.HasMeta("opennv_nif_geometry_block") && mesh.HasMeta("opennv_nif_skin_instance") &&
                    mesh.HasMeta("opennv_nif_skin_partition"), "Actual hardware skin lost its source block/partition identity.");
                var geometryBlock = mesh.GetMeta("opennv_nif_geometry_block").AsInt32();
                var skinInstance = mesh.GetMeta("opennv_nif_skin_instance").AsInt32();
                var partition = mesh.GetMeta("opennv_nif_skin_partition").AsInt32();
                Require(geometryBlock >= 0 && skinInstance >= 0 && partition >= 0, "Actual source skin identity is invalid.");
                var identity = model.ToLowerInvariant() + "/" + part.Root.GetMeta("opennv_source_part").AsString() +
                    $"/geometry:{geometryBlock}/skin:{skinInstance}/partition:{partition}";
                rows.Add(new(identity, model, hash, bindNames, palette.Select(Matrix).ToArray(), renderer?.Select(Matrix).ToArray(), sampled.ToArray(),
                    [minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z], Matrix(mesh.GlobalTransform)));
            }
        }
        Require(rows.Count > 0 && rows.Select(row => row.Identity).Distinct().Count() == rows.Count,
            "Source corpse has no uniquely identified actual hardware skin mesh.");
        var result = new CorpseSkinFrame(bodyBindings.OrderBy(row => row.SourceBody).ToArray(), bones,
            rows.OrderBy(row => row.Identity, StringComparer.Ordinal).ToArray(), rendererRows, missing);
        GD.Print($"OPENNV_CORPSE_SKIN_OWNER phase={phase} meshes={rows.Count} bones={bones.Length} " +
            $"sourceBodyBindings={bodyBindings.Count} weightedSamples={rows.Sum(row => row.Vertices.Length)} rendererRows={rendererRows} unavailableRendererMeshes={missing} " +
            "actualSkinReference=true sharedSkeleton=true sourceHash=true finalPixels=unverified");
        return result;
    }

    private static void CompareCorpseSkin(CorpseSkinFrame warm, CorpseSkinFrame cold)
    {
        Require(warm.BodyBindings.Length == cold.BodyBindings.Length && warm.BodyBindings.Zip(cold.BodyBindings).All(pair =>
            pair.First.SourceBody == pair.Second.SourceBody && pair.First.SourceNode == pair.Second.SourceNode &&
            pair.First.Bone == pair.Second.Bone && Near(pair.First.ActualWorld, pair.Second.ActualWorld) &&
            Near(pair.First.ExpectedWorld, pair.Second.ExpectedWorld)),
            "Cold source body/bone attachment graph or world publication differs from the warm owner.");
        Require(warm.Bones.Length == cold.Bones.Length && warm.Bones.Zip(cold.Bones).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.Parent == pair.Second.Parent && Near(pair.First.Global, pair.Second.Global)),
            "Cold real corpse has a different source skeleton hierarchy or body-driven pose.");
        bool SameMesh(SkinMeshRow left, SkinMeshRow right) =>
            left.Identity == right.Identity && left.ModelSha256 == right.ModelSha256 &&
            Near(left.WorldBounds, right.WorldBounds) &&
            left.Bones.SequenceEqual(right.Bones) && left.ExpectedPalette.Length == right.ExpectedPalette.Length &&
            left.ExpectedPalette.Zip(right.ExpectedPalette).All(bind => Near(bind.First, bind.Second)) &&
            left.Vertices.Length == right.Vertices.Length && left.Vertices.Zip(right.Vertices).All(vertex =>
                vertex.First.Surface == vertex.Second.Surface && vertex.First.SourceVertex == vertex.Second.SourceVertex &&
                Near(vertex.First.World, vertex.Second.World));
        if (warm.Meshes.Length != cold.Meshes.Length)
            GD.Print($"OPENNV_CORPSE_SKIN_COLD_DIVERGENCE warmMeshes={warm.Meshes.Length} coldMeshes={cold.Meshes.Length}");
        foreach (var (pair, index) in warm.Meshes.Zip(cold.Meshes).Select((pair, index) => (pair, index)))
        {
            if (SameMesh(pair.First, pair.Second)) continue;
            var palette = pair.First.ExpectedPalette.Zip(pair.Second.ExpectedPalette)
                .Select((bind, bindIndex) => (bind, bindIndex)).FirstOrDefault(value => !Near(value.bind.First, value.bind.Second));
            var vertex = pair.First.Vertices.Zip(pair.Second.Vertices).FirstOrDefault(value =>
                value.First.Surface != value.Second.Surface || value.First.SourceVertex != value.Second.SourceVertex ||
                !Near(value.First.World, value.Second.World));
            GD.Print("OPENNV_CORPSE_SKIN_COLD_DIVERGENCE " + System.Text.Json.JsonSerializer.Serialize(new
            {
                index,
                warmIdentity = pair.First.Identity,
                coldIdentity = pair.Second.Identity,
                warmHash = pair.First.ModelSha256,
                coldHash = pair.Second.ModelSha256,
                warmBounds = pair.First.WorldBounds,
                coldBounds = pair.Second.WorldBounds,
                warmTransform = pair.First.WorldTransform,
                coldTransform = pair.Second.WorldTransform,
                warmBindings = pair.First.Bones,
                coldBindings = pair.Second.Bones,
                warmPaletteCount = pair.First.ExpectedPalette.Length,
                coldPaletteCount = pair.Second.ExpectedPalette.Length,
                paletteIndex = palette.bindIndex,
                warmPalette = palette.bind.First,
                coldPalette = palette.bind.Second,
                warmVertexCount = pair.First.Vertices.Length,
                coldVertexCount = pair.Second.Vertices.Length,
                warmVertex = vertex.First,
                coldVertex = vertex.Second,
            }));
            break;
        }
        Require(warm.Meshes.Length == cold.Meshes.Length && warm.Meshes.Zip(cold.Meshes).All(pair =>
            pair.First.Identity == pair.Second.Identity && pair.First.ModelSha256 == pair.Second.ModelSha256 &&
            Near(pair.First.WorldBounds, pair.Second.WorldBounds) &&
            pair.First.Bones.SequenceEqual(pair.Second.Bones) && pair.First.ExpectedPalette.Length == pair.Second.ExpectedPalette.Length &&
            pair.First.ExpectedPalette.Zip(pair.Second.ExpectedPalette).All(bind => Near(bind.First, bind.Second)) &&
            pair.First.Vertices.Length == pair.Second.Vertices.Length && pair.First.Vertices.Zip(pair.Second.Vertices).All(vertex =>
                vertex.First.Surface == vertex.Second.Surface && vertex.First.SourceVertex == vertex.Second.SourceVertex &&
                Near(vertex.First.World, vertex.Second.World))),
            "Cold actual skin source/domain/palette or weighted world samples differ from the warm owner.");
    }

    private static bool Near(float[] left, float[] right) => left.Length == right.Length &&
        left.Zip(right).All(pair => float.IsFinite(pair.First) && float.IsFinite(pair.Second) && MathF.Abs(pair.First - pair.Second) < .0001f);
    private static float[] Matrix(Transform3D value) => [value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z,
        value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z,
        value.Origin.X, value.Origin.Y, value.Origin.Z];
}
