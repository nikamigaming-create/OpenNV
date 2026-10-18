using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

/// <summary>Private owned-source rig diagnosis; component poses are not headset acceptance.</summary>
public partial class NativePlayerRigAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args is ["--materials", .. var materialArgs])
            {
                await AuditMaterials(materialArgs); GetTree().Quit(); return;
            }
            if (args.Length is not (4 or 5)) throw new ArgumentException("Expected owned Data, native save, weapon editor ID, private report path and optional private capture directory.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
            var player = records.RuntimeFormKey(7);
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
                equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, player, saved.Character));
            var weapon = records.EffectiveRecords("WEAP").Single(record => !record.IsDeleted && record.ReadSubrecords()
                .Any(field => field.Signature == "EDID" && System.Text.Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0')
                    .Equals(args[2], StringComparison.OrdinalIgnoreCase))).FormKey;
            var first = new RuntimeNativePlayerActor(records, content, appearance, weapon, true, .0142875f, Colors.White);
            var body = new RuntimeNativePlayerActor(records, content, appearance, weapon, false, .0142875f, Colors.White);
            AddChild(first); AddChild(body);
            Camera3D? camera = null;
            if (args.Length == 5)
            {
                if (Directory.Exists(args[4])) throw new IOException("Use a fresh private capture directory.");
                Directory.CreateDirectory(args[4]);
                camera = new Camera3D { Current = true, Near = .03f, Fov = 75, CullMask = uint.MaxValue & ~RuntimeNativePlayerActor.SelfHeadLayer };
                AddChild(camera);
                AddChild(new DirectionalLight3D { RotationDegrees = new(-35, -25, 0), LightEnergy = 1 });
                AddChild(new WorldEnvironment
                {
                    Environment = new Godot.Environment
                    {
                        BackgroundMode = Godot.Environment.BGMode.Color,
                        BackgroundColor = new(.08f, .09f, .11f),
                        AmbientLightSource = Godot.Environment.AmbientSource.Color,
                        AmbientLightColor = Colors.White,
                        AmbientLightEnergy = .5f
                    }
                });
            }
            var frames = new List<object>();
            first.SetDrawn(true); body.SetDrawn(true);
            first.Advance(0, Vector3.Zero, true, false); body.Advance(0, Vector3.Zero, true, false);
            await Frames();
            frames.Add(Observe(first, "source-aim-first", camera is not null)); frames.Add(Observe(body, "source-aim-world", camera is not null));
            first.EnableTrackedArms(); body.EnableTrackedArms(first); body.ShowTrackedBody(first);
            var head = new Transform3D(Basis.Identity, new(0, 1.68f, 0));
            foreach (var pose in new[] { (Name: "forward", Grip: new Vector3(.15f, 1.4f, -.35f)),
                (Name: "near-eye", Grip: new Vector3(.1f, 1.58f, -.11f)), (Name: "crossed", Grip: new Vector3(-.15f, 1.5f, -.25f)) })
                foreach (var angle in new[] { 0f, .7f, -.7f })
                {
                    first.Advance(0, Vector3.Zero, true, false); body.Advance(0, Vector3.Zero, true, false);
                    body.SetTrackedBodyFrame(Basis.Identity, Vector3.Zero); body.PrepareTrackedBody(head);
                    first.AdoptTrackedTorso(body);
                    var aim = new Basis(Vector3.Forward, angle);
                    var right = new Transform3D(aim, pose.Grip);
                    var left = first.XrSupportGrip is { } support ? right * support : new Transform3D(Basis.Identity, new(-.2f, 1.4f, -.5f));
                    var leftContact = first.XrLeftArm.ConstrainHandTarget(first.XrLeftArm.HandTarget(left));
                    var rightContact = first.XrRightArm.ConstrainHandTarget(first.XrRightArm.HandTarget(right, aim));
                    first.PoseTrackedArms(1, head, left, right, true, true, 1, 1, 1, 1, true, true, true, aim, leftContact, rightContact, supporting: first.XrSupportGrip is not null, bodyPrepared: true);
                    body.PoseTrackedArms(1, head, left, right, true, true, 1, 1, 1, 1, true, true, true, aim, leftContact, rightContact, supporting: first.XrSupportGrip is not null, bodyPrepared: true);
                    CheckSegments(first); CheckSegments(body);
                    if (first.ProjectileTransform().Basis.Z.Normalized().Dot(aim.Z) < .999f)
                        throw new InvalidDataException("Tracked source muzzle left the supplied aim frame.");
                    await Frames();
                    frames.Add(Observe(first, $"{pose.Name}-first-{angle:R}", camera is not null)); frames.Add(Observe(body, $"{pose.Name}-world-{angle:R}", camera is not null));
                    if (camera is not null && angle == 0)
                    {
                        foreach (var view in new[] { "eye", "front" })
                        {
                            camera.GlobalTransform = head;
                            if (view == "front") { camera.GlobalPosition = new(.9f, 1.5f, -1.1f); camera.LookAt(new(0, 1.4f, -.2f)); }
                            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            using var pixels = GetViewport().GetTexture().GetImage();
                            var error = pixels.SavePng(Path.Combine(args[4], pose.Name + "-" + view + ".png"));
                            if (error != Error.Ok) throw new IOException("Rig diagnostic capture failed: " + error);
                        }
                    }
                }
            CheckSegmentNegative(body);
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { weapon = weapon.ToString(), first = first.State, body = body.State, frames },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("OPENNV_PLAYER_RIG_DIAGNOSTIC_COMPLETE report=" + args[3] + " sourceLengths=true changedLengthNegative=true muzzleAim=true componentPoses=9 finalEyes=unverified physicalHeadset=unverified recording=off");
            first.Free(); body.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OPENNV_PLAYER_RIG_DIAGNOSTIC_FAIL " + error); GetTree().Quit(1); }
    }

    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void CheckSegments(RuntimeNativePlayerActor actor)
    {
        var skeleton = actor.Skeleton.Node;
        foreach (var side in new[] { "L", "R" })
        {
            var bones = new[] { "UpperArm", "Forearm", "Hand" }.Select(name => actor.Skeleton.BoneIndex($"Bip01 {side} {name}")).ToArray();
            for (var index = 0; index < 2; index++)
            {
                var expected = skeleton.GetBoneGlobalRest(bones[index]).Origin.DistanceTo(skeleton.GetBoneGlobalRest(bones[index + 1]).Origin);
                var actual = skeleton.GetBoneGlobalPose(bones[index]).Origin.DistanceTo(skeleton.GetBoneGlobalPose(bones[index + 1]).Origin);
                if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > .0001f)
                    throw new InvalidDataException($"{actor.Name} {side} segment {index} changed source length: {expected:R} -> {actual:R}");
            }
        }
    }

    private static void CheckSegmentNegative(RuntimeNativePlayerActor actor)
    {
        var skeleton = actor.Skeleton.Node; var bone = actor.Skeleton.BoneIndex("Bip01 R Forearm");
        var retained = skeleton.GetBonePose(bone);
        try
        {
            var moved = retained; moved.Origin *= 1.5f; skeleton.SetBonePose(bone, moved);
            try { CheckSegments(actor); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Changed-length negative fixture was accepted.");
        }
        finally { skeleton.SetBonePose(bone, retained); }
    }

    private static object Observe(RuntimeNativePlayerActor actor, string label, bool requireRenderer)
    {
        var skeleton = actor.Skeleton.Node;
        var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(bone => new
        {
            name = skeleton.GetBoneName(bone).ToString(),
            parent = skeleton.GetBoneParent(bone),
            rest = Matrix(skeleton.GetBoneGlobalRest(bone)),
            pose = Matrix(skeleton.GetBoneGlobalPose(bone)),
            local = Matrix(skeleton.GetBonePose(bone)),
            localRest = Matrix(skeleton.GetBoneRest(bone))
        }).ToArray();
        var meshes = new List<object>();
        foreach (var part in actor.Actor.Parts)
            foreach (var mesh in part.Root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                if (mesh.Skin is not { } skin) continue;
                var palette = Enumerable.Range(0, skin.GetBindCount()).Select(index =>
                    skeleton.GetBoneGlobalPose(skin.GetBindBone(index)) * skin.GetBindPose(index)).ToArray();
                var rest = Enumerable.Range(0, skin.GetBindCount()).Select(index =>
                    skeleton.GetBoneGlobalRest(skin.GetBindBone(index)) * skin.GetBindPose(index)).ToArray();
                if (mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) != skeleton)
                    throw new InvalidDataException("Actual source skin has a different skeleton owner.");
                var reference = mesh.GetSkinReference();
                var rid = reference?.GetSkeleton() ?? default;
                var renderCount = rid.IsValid ? RenderingServer.SkeletonGetBoneCount(rid) : 0;
                if (requireRenderer && renderCount != palette.Length || renderCount != 0 && renderCount != palette.Length)
                    throw new InvalidDataException("Actual renderer palette has no matching source extent.");
                for (var index = 0; index < renderCount; index++)
                    if (Matrix(RenderingServer.SkeletonBoneGetTransform(rid, index)).Zip(Matrix(palette[index]))
                        .Any(pair => !float.IsFinite(pair.First) || MathF.Abs(pair.First - pair.Second) > .0001f))
                        throw new InvalidDataException("Actual renderer palette differs from the published source skin pose.");
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var influences = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    var extent = weights.Length / vertices.Length;
                    var posed = new Vector3[vertices.Length]; var neutral = new Vector3[vertices.Length];
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                        for (var input = 0; input < extent; input++)
                        {
                            var offset = vertex * extent + input; var bind = influences[offset];
                            posed[vertex] += palette[bind] * vertices[vertex] * weights[offset];
                            neutral[vertex] += rest[bind] * vertices[vertex] * weights[offset];
                        }
                    var edges = new List<(int A, int B, float Neutral, float Posed)>();
                    for (var triangle = 0; triangle < indices.Length; triangle += 3)
                        for (var corner = 0; corner < 3; corner++)
                        {
                            var a = indices[triangle + corner]; var b = indices[triangle + (corner + 1) % 3];
                            var length = neutral[a].DistanceTo(neutral[b]);
                            if (length > .0001f) edges.Add((a, b, length, posed[a].DistanceTo(posed[b])));
                        }
                    meshes.Add(new
                    {
                        model = part.Root.GetMeta("opennv_source_model").AsString(),
                        name = mesh.Name.ToString(),
                        mesh.Visible,
                        mesh.Layers,
                        transform = Matrix(mesh.GlobalTransform),
                        palette = Enumerable.Range(0, skin.GetBindCount()).Select(index => new
                        { name = skin.GetBindName(index).ToString(), bind = Matrix(skin.GetBindPose(index)) }).ToArray(),
                        vertexCount = vertices.Length,
                        rendererPaletteCount = renderCount,
                        largestStretch = edges.OrderByDescending(edge => edge.Posed / edge.Neutral).Take(8).Select(edge => new
                        {
                            edge.A,
                            edge.B,
                            edge.Neutral,
                            edge.Posed,
                            ratio = edge.Posed / edge.Neutral,
                            restA = Vector(neutral[edge.A]),
                            restB = Vector(neutral[edge.B]),
                            posedA = Vector(posed[edge.A]),
                            posedB = Vector(posed[edge.B]),
                            weightsA = Weights(edge.A),
                            weightsB = Weights(edge.B)
                        }).ToArray()
                    });
                    object[] Weights(int vertex) => Enumerable.Range(0, extent).Where(input => weights[vertex * extent + input] > 0)
                        .Select(input => (object)new { bone = skin.GetBindName(influences[vertex * extent + input]).ToString(), weight = weights[vertex * extent + input] }).ToArray();
                }
            }
        return new { label, skeletonSha256 = actor.Skeleton.Source.Sha256, transform = Matrix(actor.GlobalTransform), skeleton = Matrix(skeleton.GlobalTransform), bones, meshes };
    }
    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
    private static float[] Matrix(Transform3D value) => [value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z,
        value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z,
        value.Origin.X, value.Origin.Y, value.Origin.Z];
}
