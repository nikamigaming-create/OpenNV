using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseSceneConstructionLifetime()
    {
        var refused = new List<(byte[] Bytes, string Error)>
        {
            (SceneLifetimeSharedSkin(), "shared by multiple skin instances"),
            (SceneLifetimeHinges(missingEndpoint: true), "no independent pair of model bodies"),
            (SceneLifetimeSkin(explicitWeights: false), "does not carry explicit vertex weights"),
        };
        foreach (var type in new[] { "BSRangeNode", "BSBlastNode", "BSDamageStage" })
            refused.Add((SceneLifetimeRange(type, inverted: true), "damage range is inverted"));
        foreach (var (bytes, error) in refused) RequireSceneLifetimeRefusal(bytes, error);
        RequireIndependentSceneLifetimeSkins();
        await ExerciseIndependentSceneLifetimeHinges();
        await SettleCollisionLifetimeResources();
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        await ExerciseSceneLifetimeRefusalsWithLiveSibling(refused);
        RequireIndependentSceneLifetimeSkins();
        foreach (var type in new[] { "BSRangeNode", "BSBlastNode", "BSDamageStage" })
            await ExerciseIndependentCollisionLifetime(SceneLifetimeRange(type, inverted: false));
        await ExerciseIndependentSceneLifetimeHinges();
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before ||
            Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Released scene construction proofs retained native nodes or resources.");
        GD.Print("OPENNV_NIF_SCENE_CONSTRUCTION_LIFETIME_PASS discoveryRefusal=true invertedRange=true " +
            "rangeKinds=3 lateMissingHingeEndpoint=true configuredHinge=true independentSkin=true independentJoint=true " +
            "liveSibling=unchanged sourceBytes=unchanged orphanNodes=unchanged nativeResourceGrowth=0 " +
            "configureException=untriggered");
    }

    private async Task ExerciseSceneLifetimeRefusalsWithLiveSibling(IReadOnlyList<(byte[] Bytes, string Error)> refused)
    {
        var sibling = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(GeometryCollisionLifetimeFixture(false)), .1f);
        try
        {
            sibling.Root.Position = new(4, 0, 0); AddChild(sibling.Root);
            var mesh = sibling.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var body = mesh.GetChildren().OfType<PhysicsBody3D>().Single();
            var shape = body.GetChildren().OfType<CollisionShape3D>().First().Shape;
            var meshRid = mesh.Mesh.GetRid(); var bodyRid = body.GetRid(); var shapeRid = shape.GetRid();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var liveResources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
            using var ray = PhysicsRayQueryParameters3D.Create(new(4, 3, 0), new(4, -3, 0), 1);
            foreach (var (bytes, error) in refused)
            {
                RequireSceneLifetimeRefusal(bytes, error);
                if (!GodotObject.IsInstanceValid(body) || mesh.Mesh.GetRid() != meshRid || body.GetRid() != bodyRid ||
                    shape.GetRid() != shapeRid || body.GetWorld3D().DirectSpaceState.IntersectRay(ray)["collider"].AsGodotObject() != body)
                    throw new InvalidDataException("A rejected scene retired or changed the independent live mesh/collision sibling.");
            }
            await SettleCollisionLifetimeResources();
            if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != liveResources)
                throw new InvalidDataException("Scene discovery, range or late constraint rejection retained native resources.");
        }
        finally { sibling.Root.Free(); }
    }

    private static void RequireSceneLifetimeRefusal(byte[] bytes, string expected)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = FalloutNifFile.Read(bytes);
        if (expected == "shared by multiple skin instances" &&
            (source.ReadGeometry(2).SkinInstance != 4 || source.ReadGeometry(7).SkinInstance != 8 ||
                source.ReadObject(4) is not FalloutNifSkinInstance first || source.ReadObject(8) is not FalloutNifSkinInstance second ||
                first.Bones.Length != 1 || !first.Bones.SequenceEqual(second.Bones)))
            throw new InvalidDataException("Scene discovery fixture does not declare distinct skins sharing one source bone.");
        if (expected == "damage range is inverted" && source.ReadNode(6).Range is not { Minimum: 3, Maximum: 1, Current: 2 })
            throw new InvalidDataException("Range refusal fixture did not retain its source range bytes.");
        if (expected == "no independent pair of model bodies" &&
            (source.ReadHingeConstraint(10).Header.EntityB != 7 || source.ReadHingeConstraint(11).Header.EntityB != 9 ||
                source.ReadNode(0).Children.Contains(9)))
            throw new InvalidDataException("Late hinge fixture does not declare its valid joint followed by an absent model endpoint.");
        RequireCollisionBuildRefusal(source, expected);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Scene construction changed its original source bytes.");
    }

    private static void RequireIndependentSceneLifetimeSkins()
    {
        var bytes = SceneLifetimeSkin();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = FalloutNifFile.Read(bytes);
        var geometry = source.ReadMeshData(source.ReadGeometry(2).Data);
        var instance = (FalloutNifSkinInstance)source.ReadObject(4);
        var data = (FalloutNifSkinData)source.ReadObject(instance.Data);
        var partition = (FalloutNifSkinPartition)source.ReadObject(instance.SkinPartition);
        var binding = FalloutNifOneBoneSkin.Validate(instance, data, partition, geometry.Vertices.Length);
        if (geometry.Vertices.Length != 3 || !data.HasVertexWeights ||
            !data.Bones.Single().VertexWeights.SequenceEqual(new FalloutNifSkinWeight[] { new(0, 1), new(1, 1), new(2, 1) }) ||
            !instance.BodyPartitions.SequenceEqual(new FalloutNifBodyPartition[] { new(0x101, 1) }))
            throw new InvalidDataException("Positive skin lifetime fixture did not decode its authored complete vertex weights and body partition.");
        RuntimeNativeNifScene? first = null, second = null;
        try
        {
            first = RuntimeNativeNifMeshBuilder.Build(source, .1f); second = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            var firstMesh = first.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var secondMesh = second.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            if (first.Surfaces != 1 || second.Surfaces != 1 || firstMesh.Skin is null || secondMesh.Skin is null ||
                firstMesh.Skin.GetInstanceId() == secondMesh.Skin.GetInstanceId() || firstMesh.Mesh.GetRid() == secondMesh.Mesh.GetRid())
                throw new InvalidDataException("Successful scene discovery did not create independently owned skins and meshes.");
            foreach (var mesh in new[] { firstMesh, secondMesh })
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(0);
                if (!arrays[(int)Mesh.ArrayType.Bones].AsInt32Array().SequenceEqual(binding.BoneIndices) ||
                    !arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array().SequenceEqual(binding.Weights))
                    throw new InvalidDataException("Native skin lifetime proof lost its reader-backed vertex influences.");
            }
            first.Root.Free(); first = null;
            if (!GodotObject.IsInstanceValid(secondMesh) || secondMesh.Skin.GetBindCount() != 1 || secondMesh.Mesh.GetSurfaceCount() != 1)
                throw new InvalidDataException("Releasing one skin scene retired its independent sibling resources.");
        }
        finally { first?.Root.Free(); second?.Root.Free(); }
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Successful skin construction changed its original source bytes.");
    }

    private async Task ExerciseIndependentSceneLifetimeHinges()
    {
        var bytes = SceneLifetimeHinges();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = FalloutNifFile.Read(bytes);
        RuntimeNativeNifScene? first = null, second = null;
        try
        {
            first = RuntimeNativeNifMeshBuilder.Build(source, .1f); second = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            first.Root.Position = new(-4, 0, 0); second.Root.Position = new(4, 0, 0);
            AddChild(first.Root); AddChild(second.Root);
            var joint = first.Root.GetChildren().OfType<RuntimeNifHingeJoint>().Single();
            var other = second.Root.GetChildren().OfType<RuntimeNifHingeJoint>().Single();
            var bodies = first.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().ToArray();
            var siblings = second.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().ToArray();
            if (first.CollisionBodies != 2 || second.CollisionBodies != 2 || !joint.Bound || !other.Bound ||
                !joint.Unrestricted || !other.Unrestricted || bodies.Select(body => body.GetRid()).Intersect(siblings.Select(body => body.GetRid())).Any())
                throw new InvalidDataException("Source hinge Configure did not bind independent native model bodies and joints.");
            RemoveChild(first.Root);
            if (joint.Bound || !other.Bound) throw new InvalidDataException("Detaching one hinge changed the independent sibling joint.");
            AddChild(first.Root);
            if (!joint.Bound) throw new InvalidDataException("A reentered source model failed to restore its owned native joint.");
            first.Root.Free(); first = null;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (joint.Bound || !other.Bound || siblings.Any(body => !GodotObject.IsInstanceValid(body)))
                throw new InvalidDataException("Releasing one hinge retained its joint or retired its independent sibling.");
            second.Root.Free(); second = null;
            if (other.Bound) throw new InvalidDataException("Released source model retained its native hinge RID.");
        }
        finally { first?.Root.Free(); second?.Root.Free(); }
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Native hinge construction changed its original source bytes.");
    }

    private static byte[] SceneLifetimeSharedSkin()
    {
        var bytes = ActorSkinFixture("LifetimeSkin", true);
        var ranges = new List<FalloutNifReadRange>();
        var source = FalloutNifFile.Read(bytes, ranges.Add); var geometry = source.ReadGeometry(2);
        var skin = ranges.Single(range => range.Owner == "NIF block 2 (NiTriShape)" && range.Field == "skin instance");
        var blocks = source.Blocks.Select(block => (Type: block.TypeName, Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var duplicate = (byte[])blocks[2].Bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(duplicate.AsSpan(skin.Offset - geometry.Block.Offset, sizeof(int)), 8);
        blocks.Add(("NiTriShape", duplicate)); blocks.Add(("NiSkinInstance", (byte[])blocks[4].Bytes.Clone()));
        blocks[0] = ("NiNode", SceneLifetimeNode(0, -1, [1, 2, 7]));
        return CollisionLifetimeFile(blocks, source.Strings);
    }

    private static byte[] SceneLifetimeSkin(bool explicitWeights = true)
    {
        var bytes = ActorSkinFixture("LifetimeSkin", true); var source = FalloutNifFile.Read(bytes);
        var blocks = source.Blocks.Select(block => (Type: block.TypeName, Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var visualBytes = SurfaceFixture(0x80000100); var ranges = new List<FalloutNifReadRange>();
        var visual = FalloutNifFile.Read(visualBytes, ranges.Add); _ = visual.ReadGeometry(1); _ = visual.ReadObject(3);
        var geometry = visual.Blocks[1]; var shader = visual.Blocks[3];
        var geometryBytes = visualBytes.AsSpan(geometry.Offset, geometry.Size).ToArray();
        var shaderBytes = visualBytes.AsSpan(shader.Offset, shader.Size).ToArray();
        foreach (var (field, value) in new (string Field, int Value)[]
        {
            ("NiTriShape name", 2), ("NiTriShape properties 0", 7), ("NiTriShape properties 1", 9),
            ("geometry data", 3), ("skin instance", 4),
        })
        {
            var fieldRange = ranges.Single(range => range.Owner == "NIF block 1 (NiTriShape)" && range.Field == field);
            BinaryPrimitives.WriteInt32LittleEndian(geometryBytes.AsSpan(fieldRange.Offset - geometry.Offset, sizeof(int)), value);
        }
        var texture = ranges.Single(range => range.Owner == "NIF block 3 (BSShaderPPLightingProperty)" && range.Field == "shader texture set");
        BinaryPrimitives.WriteInt32LittleEndian(shaderBytes.AsSpan(texture.Offset - shader.Offset, sizeof(int)), 8);
        blocks[2] = ("NiTriShape", geometryBytes);
        blocks[3] = ("NiTriShapeData", visualBytes.AsSpan(visual.Blocks[2].Offset, visual.Blocks[2].Size).ToArray());
        blocks[4] = ("BSDismemberSkinInstance", Bytes(writer =>
        {
            writer.Write(blocks[4].Bytes); writer.Write(1); writer.Write((ushort)0x101); writer.Write((ushort)1);
        }));
        blocks[5] = ("NiSkinData", Bytes(writer =>
        {
            static void Identity(BinaryWriter output)
            { foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }) output.Write(value); }
            Identity(writer); writer.Write(1); writer.Write(explicitWeights); Identity(writer);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(2f);
            writer.Write(explicitWeights ? (ushort)3 : (ushort)0);
            if (explicitWeights)
                for (ushort vertex = 0; vertex < 3; vertex++) { writer.Write(vertex); writer.Write(1f); }
        }));
        blocks.Add((shader.TypeName, shaderBytes));
        foreach (var index in new[] { 4, 5 })
            blocks.Add((visual.Blocks[index].TypeName, visualBytes.AsSpan(visual.Blocks[index].Offset, visual.Blocks[index].Size).ToArray()));
        return CollisionLifetimeFile(blocks, source.Strings);
    }

    private static byte[] SceneLifetimeRange(string type, bool inverted)
    {
        var bytes = CollisionLifetimeFixture(false); var source = FalloutNifFile.Read(bytes);
        var blocks = source.Blocks.Select(block => (Type: block.TypeName, Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        blocks[0] = ("NiNode", SceneLifetimeNode(0, 1, [6]));
        blocks.Add((type, [.. SceneLifetimeNode(1, -1, []), .. new byte[] { inverted ? (byte)3 : (byte)0, inverted ? (byte)1 : (byte)3, 2 }]));
        return CollisionLifetimeFile(blocks, ["LifetimeRoot", "Range"]);
    }

    private static byte[] SceneLifetimeHinges(bool missingEndpoint = false)
    {
        var bytes = CollisionLifetimeFixture(true); var ranges = new List<FalloutNifReadRange>();
        var source = FalloutNifFile.Read(bytes, ranges.Add); _ = source.ReadObject(2);
        var body = source.Blocks[2];
        var shape = ranges.Single(range => range.Owner == "NIF block 2 (bhkRigidBody)" && range.Field == "rigid-body shape");
        var count = ranges.Single(range => range.Owner == "NIF block 2 (bhkRigidBody)" && range.Field == "rigid-body constraints count");
        byte[] Body(int shapeIndex, int[] constraints)
        {
            var prefix = bytes.AsSpan(body.Offset, count.Offset - body.Offset).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(shape.Offset - body.Offset, sizeof(int)), shapeIndex);
            return Bytes(writer => { writer.Write(prefix); writer.Write(constraints.Length); foreach (var index in constraints) writer.Write(index); writer.Write(0U); });
        }
        byte[] Attachment(int target, int bodyIndex) => Bytes(writer => { writer.Write(target); writer.Write((ushort)1); writer.Write(bodyIndex); });
        byte[] Hinge(int second) => Bytes(writer =>
        {
            writer.Write(2U); writer.Write(3); writer.Write(second); writer.Write(1U);
            for (var endpoint = 0; endpoint < 2; endpoint++)
                foreach (var vector in new float[][] { [0, 0, 1], [1, 0, 0], [0, 1, 0], [0, 0, 0] })
                { foreach (var component in vector) writer.Write(component); writer.Write(0U); }
        });
        var sphere = bytes.AsSpan(source.Blocks[4].Offset, source.Blocks[4].Size).ToArray();
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("NiNode", SceneLifetimeNode(0, -1, [1, 5])), ("NiNode", SceneLifetimeNode(1, 2, [])),
            ("bhkCollisionObject", Attachment(1, 3)), ("bhkRigidBody", Body(4, missingEndpoint ? [10, 11] : [10])),
            ("bhkSphereShape", sphere), ("NiNode", SceneLifetimeNode(2, 6, [])),
            ("bhkCollisionObject", Attachment(5, 7)), ("bhkRigidBody", Body(8, [])),
            ("bhkSphereShape", (byte[])sphere.Clone()), ("bhkRigidBody", Body(8, [])), ("bhkHingeConstraint", Hinge(7)),
        };
        if (missingEndpoint) blocks.Add(("bhkHingeConstraint", Hinge(9)));
        return CollisionLifetimeFile(blocks, ["LifetimeRoot", "First", "Second"]);
    }

    private static byte[] SceneLifetimeNode(int name, int collision, int[] children) => Bytes(writer =>
    {
        writer.Write(name); writer.Write(0); writer.Write(-1); writer.Write(14U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0); writer.Write(collision); writer.Write(children.Length);
        foreach (var child in children) writer.Write(child); writer.Write(0);
    });
}
