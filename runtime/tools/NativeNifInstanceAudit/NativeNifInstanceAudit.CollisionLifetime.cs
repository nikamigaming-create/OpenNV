using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseCollisionLifetime()
    {
        RequireCollisionLifetimeTelemetry();
        foreach (var dynamic in new[] { false, true })
        {
            var refused = new (byte[] Bytes, string Error)[]
            {
                (CollisionLifetimeFixture(dynamic, secondRadius: -1), "sphere radius"),
                (CollisionLifetimeFixture(dynamic, unsupportedSecond: true), "bhkUnownedCollisionShape"),
                (CollisionLifetimeFixture(dynamic, children: []), "list shape 3 is empty"),
                (CollisionLifetimeFixture(dynamic, children: [4, 3]), "cycle at block 3"),
                (CollisionLifetimeFixture(dynamic, missingShape: true), "has no shape"),
                (CollisionLifetimeFixture(dynamic, invalidRotation: true), "quaternion is not normalized"),
            };
            foreach (var (bytes, error) in refused)
                RequireCollisionBuildRefusal(FalloutNifFile.Read(bytes), error);
            await ExerciseIndependentCollisionLifetime(CollisionLifetimeFixture(dynamic));
        }
        await ExerciseGeometryCollisionLifetime();
        await ExerciseSceneConstructionLifetime();
        GD.Print("OPENNV_NIF_COLLISION_LIFETIME_PASS static=true dynamic=true malformedShape=true " +
            "missingShape=true unsupportedChild=true emptyList=true cycle=true invalidBodyTransform=true " +
            "partialSiblingCleanup=true independentBodies=true independentShapes=true liveQueries=true orphanNodes=unchanged");
    }

    private async Task ExerciseIndependentCollisionLifetime(byte[] bytes, bool geometry = false)
    {
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        RuntimeNativeNifScene? first = null, second = null;
        try
        {
            var source = FalloutNifFile.Read(bytes);
            first = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            second = RuntimeNativeNifMeshBuilder.Build(source, .1f);
            first.Root.Position = new(-4, 0, 0); second.Root.Position = new(4, 0, 0);
            AddChild(first.Root); AddChild(second.Root);
            var firstBody = first.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Single();
            var secondBody = second.Root.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Single();
            var firstShapes = firstBody.GetChildren().OfType<CollisionShape3D>().ToArray();
            var secondShapes = secondBody.GetChildren().OfType<CollisionShape3D>().ToArray();
            if (geometry && (first.Surfaces != 1 || second.Surfaces != 1 ||
                firstBody.GetParent() is not MeshInstance3D || secondBody.GetParent() is not MeshInstance3D))
                throw new InvalidDataException("Geometry lifetime fixture did not reach an actual collision-owning mesh.");
            if (first.CollisionBodies != 1 || second.CollisionBodies != 1 ||
                first.CollisionShapes != 2 || second.CollisionShapes != 2 ||
                firstShapes.Length != 2 || secondShapes.Length != 2 || firstBody.GetRid() == secondBody.GetRid() ||
                firstShapes.Where((shape, index) => shape.Shape.GetRid() == secondShapes[index].Shape.GetRid()).Any())
                throw new InvalidDataException("Successful source collision builds shared native bodies or shapes.");
            ((SphereShape3D)firstShapes[0].Shape).Radius = .25f;
            if (!Mathf.IsEqualApprox(((SphereShape3D)secondShapes[0].Shape).Radius, .7f))
                throw new InvalidDataException("Mutating one built source shape changed its sibling instance.");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var world = secondBody.GetWorld3D().DirectSpaceState;
            using var ray = PhysicsRayQueryParameters3D.Create(new(4, 3, 0), new(4, -3, 0), 1);
            if (world.IntersectRay(ray)["collider"].AsGodotObject() != secondBody)
                throw new InvalidDataException("The successful collision build has no live independent physics body.");
            first.Root.Free(); first = null;
            if (!GodotObject.IsInstanceValid(secondBody) || world.IntersectRay(ray)["collider"].AsGodotObject() != secondBody)
                throw new InvalidDataException("Releasing one collision build retired its sibling body or shapes.");
            second.Root.Free(); second = null;
            if (world.IntersectRay(ray).Count != 0)
                throw new InvalidDataException("Released source collision still participates in the physics world.");
        }
        finally { first?.Root.Free(); second?.Root.Free(); }
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before)
            throw new InvalidDataException("Successful source collision disposal orphaned native nodes.");
    }

    private static void RequireCollisionBuildRefusal(FalloutNifFile source, string expected,
        RuntimeLiveContentSource? content = null)
    {
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var hash = source.Sha256;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Exception? rejection = null;
            try
            {
                var scene = RuntimeNativeNifMeshBuilder.Build(source, .1f, contentSource: content);
                scene.Root.Free();
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { rejection = error; }
            if (rejection is null || !rejection.Message.Contains(expected, StringComparison.Ordinal))
                throw new InvalidDataException($"Source collision did not retain its expected refusal: {expected}; " +
                    $"actual={rejection?.Message ?? "accepted"}.");
            if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before)
                throw new InvalidDataException($"Rejected source collision orphaned native nodes: {rejection.Message}");
        }
        if (source.Sha256 != hash) throw new InvalidDataException("Collision construction changed source identity.");
    }

    private static void RequireCollisionLifetimeTelemetry()
    {
        if (!OS.IsDebugBuild())
            throw new NotSupportedException("Collision lifetime proof requires Godot's native orphan-node telemetry.");
    }

    private async Task ExerciseGeometryCollisionLifetime()
    {
        var refused = new List<(FalloutNifFile Source, string Error)>();
        foreach (var dynamic in new[] { false, true })
        {
            refused.Add((FalloutNifFile.Read(GeometryCollisionLifetimeFixture(dynamic, unsupportedSecond: true)), "bhkUnownedCollisionShape"));
            refused.Add((FalloutNifFile.Read(GeometryCollisionLifetimeFixture(dynamic, secondRadius: -1)), "sphere radius"));
        }
        // Warm the shared immutable shader before measuring native resources.
        // Every warm-up still must reject and release its native nodes.
        foreach (var (source, error) in refused) RequireCollisionBuildRefusal(source, error);
        await SettleCollisionLifetimeResources();
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        foreach (var (source, error) in refused) RequireCollisionBuildRefusal(source, error);
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Rejected mesh collision construction retained native resources after collection.");
        foreach (var dynamic in new[] { false, true })
            await ExerciseIndependentCollisionLifetime(GeometryCollisionLifetimeFixture(dynamic), geometry: true);
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Released mesh collision instances retained native resources after collection.");
        GD.Print($"OPENNV_NIF_GEOMETRY_COLLISION_LIFETIME_PASS nativeResourceCount={resources} " +
            "actualMesh=true unsupportedCollision=true malformedSecondChild=true partialSiblingCleanup=true " +
            "static=true dynamic=true independentMeshBodies=true orphanNodes=unchanged nativeResourceGrowth=0");
    }

    private async Task SettleCollisionLifetimeResources()
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect(); GC.WaitForPendingFinalizers();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task ExerciseOwnedCollisionLifetime(string root, string model, string expected, string[] options)
    {
        RequireCollisionLifetimeTelemetry();
        if (string.IsNullOrWhiteSpace(expected)) throw new ArgumentException("An expected source refusal is required.");
        FalloutModStackSelection? selection = options switch
        {
            [] => null,
            ["--mod-stack", var path] => FalloutModStackSelection.ReadOptions(
                new Dictionary<string, string> { ["mod-stack"] = File.ReadAllText(Path.GetFullPath(path)) }),
            _ => throw new ArgumentException("Owned collision lifetime accepts only an optional --mod-stack JSON file."),
        };
        var campaign = NativeGameInstallation.Detect(root).Game switch
        {
            NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
            NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
            _ => throw new NotSupportedException("Collision lifetime proof requires a Fallout 3 or New Vegas installation."),
        };
        using var content = selection is null ? RuntimeLiveContentSource.Open(root, campaign) : selection.Resolve(root).OpenSource();
        if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
        var source = FalloutNifFile.Read(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        RequireCollisionBuildRefusal(source, expected, content);
        await SettleCollisionLifetimeResources();
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        RequireCollisionBuildRefusal(source, expected, content);
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Repeated rejected owned collision retained native resources.");
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Rejected collision build changed the read-only owned source bytes.");
        GD.Print($"OPENNV_OWNED_NIF_COLLISION_LIFETIME_PASS source={identity} sha256={hash} " +
            $"stack={content.StackId} refusal={expected} repeatedBuild=true sourceBytes=unchanged orphanNodes=unchanged " +
            $"nativeResources={resources} nativeResourceGrowth=0 sourceBehavior=unsupported");
    }

    private static byte[] GeometryCollisionLifetimeFixture(bool dynamic, float secondRadius = 2, bool unsupportedSecond = false)
    {
        var bytes = SurfaceFixture(0x80000100);
        var ranges = new List<FalloutNifReadRange>();
        var visual = FalloutNifFile.Read(bytes, ranges.Add);
        var geometry = visual.ReadGeometry(1);
        var collisionField = ranges.Single(range => range.Owner == "NIF block 1 (NiTriShape)" && range.Field == "NiTriShape collision");
        if (collisionField.Length != sizeof(int) || geometry.CollisionObject != -1 || visual.ReadNode(0).CollisionObject != -1)
            throw new InvalidDataException("The geometry lifetime fixture has an unexpected source attachment layout.");
        var blocks = visual.Blocks.Select(block => (Type: block.TypeName,
            Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var collisionBytes = CollisionLifetimeFixture(dynamic, secondRadius, unsupportedSecond);
        var collision = FalloutNifFile.Read(collisionBytes);
        var attachment = blocks.Count;
        blocks.AddRange(collision.Blocks.Skip(1).Select(block => (Type: block.TypeName,
            Bytes: collisionBytes.AsSpan(block.Offset, block.Size).ToArray())));
        BinaryPrimitives.WriteInt32LittleEndian(blocks[1].Bytes.AsSpan(collisionField.Offset - geometry.Block.Offset), attachment);
        blocks[attachment] = ("bhkCollisionObject", Bytes(writer =>
        { writer.Write(1); writer.Write((ushort)1); writer.Write(attachment + 1); }));
        BinaryPrimitives.WriteInt32LittleEndian(blocks[attachment + 1].Bytes, attachment + 2);
        BinaryPrimitives.WriteInt32LittleEndian(blocks[attachment + 2].Bytes.AsSpan(4), attachment + 3);
        BinaryPrimitives.WriteInt32LittleEndian(blocks[attachment + 2].Bytes.AsSpan(8), attachment + 4);
        return CollisionLifetimeFile(blocks, visual.Strings);
    }

    private static byte[] CollisionLifetimeFixture(bool dynamic, float secondRadius = 2,
        bool unsupportedSecond = false, int[]? children = null, bool missingShape = false, bool invalidRotation = false)
    {
        var body = new byte[236];
        BinaryPrimitives.WriteInt32LittleEndian(body, missingShape ? -1 : 3);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(80), invalidRotation ? 2 : 1);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(180), dynamic ? 1 : 0);
        body[212] = dynamic ? (byte)1 : (byte)6;
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiNode", Bytes(writer =>
            {
                writer.Write(0); writer.Write(0); writer.Write(-1); writer.Write(14U);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
                writer.Write(0); writer.Write(1); writer.Write(0); writer.Write(0);
            })),
            ("bhkCollisionObject", Bytes(writer => { writer.Write(0); writer.Write((ushort)1); writer.Write(2); })),
            (invalidRotation ? "bhkRigidBodyT" : "bhkRigidBody", body),
            ("bhkListShape", Bytes(writer =>
            {
                var references = children ?? [4, 5];
                writer.Write(references.Length); foreach (var child in references) writer.Write(child);
                writer.Write(0U); writer.Write(new byte[24]); writer.Write(0);
            })),
            ("bhkSphereShape", Bytes(writer => { writer.Write(0U); writer.Write(1f); })),
            (unsupportedSecond ? "bhkUnownedCollisionShape" : "bhkSphereShape",
                unsupportedSecond ? [] : Bytes(writer => { writer.Write(0U); writer.Write(secondRadius); })),
        };
        return CollisionLifetimeFile(blocks, ["LifetimeCollision"]);
    }

    private static byte[] CollisionLifetimeFile(IReadOnlyList<(string Type, byte[] Bytes)> blocks, IReadOnlyList<string> names)
    {
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8);
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Count); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Count);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            writer.Write(names.Count); writer.Write(names.Max(name => name.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0); foreach (var block in blocks) writer.Write(block.Bytes);
            writer.Write(1); writer.Write(0);
        });
    }
}
