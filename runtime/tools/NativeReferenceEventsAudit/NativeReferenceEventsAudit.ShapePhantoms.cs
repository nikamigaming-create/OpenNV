using System.Text;
using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;

public partial class NativeReferenceEventsAudit
{
    private async Task ExerciseShapePhantoms()
    {
        GD.Print($"OPENNV_NATIVE_SHAPED_PHANTOM_ASSEMBLY mvid={GetType().Assembly.ManifestModule.ModuleVersionId} location={GetType().Assembly.Location}");
        Node3D? root = null; RuntimeNativeNifPrototype? prototype = null; RuntimeNativeNifPrototype? rebuiltPrototype = null;
        Node3D? warm = null; Node3D? rebuilt = null; StaticBody3D? contact = null;
        var nativeObjects = new Dictionary<string, ulong>(StringComparer.Ordinal);
        using var validity = new Expression();
        Require(validity.Parse("is_instance_id_valid(id)", ["id"]) == Error.Ok &&
            !ShapePhantomInstanceAlive(validity, 0), "Native nonbinding validity query was not admitted.");
        try
        {
            var bytes = AuthoredShapePhantom();
            var decoded = FalloutNifFile.Read(bytes);
            prototype = new(decoded, .1f);
            TrackShapePhantomGraph(prototype.Scene.Root, "warmSource", nativeObjects);
            root = new Node3D { Name = "AuthoredShapedPhantomNativeQuery" };
            AddChild(root);
            nativeObjects.Add("root", root.GetInstanceId());
            var placement = new Transform3D(new Basis(Vector3.Up, .37f), new(2.25f, -1.5f, 4.125f));
            warm = prototype.InstantiatePlaced(placement); root.AddChild(warm);
            var phantom = NativeNifCollisionBuilder.PlacedPhantoms(warm, 2).Single();
            TrackShapePhantomGraph(warm, "warm", nativeObjects);
            var expected = GamebryoCoordinate.ConvertVector(new(5, -7, 9)) * .7f;
            Require(phantom.Transform.Origin.IsEqualApprox(expected) &&
                phantom.CollisionLayer == 0 && phantom.CollisionMask == 2 &&
                warm.FindChildren("*", "", true, false).OfType<PhysicsBody3D>().Count() == 0 &&
                prototype.Scene.CollisionBodies == 0,
                "Source shaped phantom lost its matrix/filter or became solid collision.");
            using var shape = new BoxShape3D { Size = new(.1f, .1f, .1f) };
            contact = new StaticBody3D { CollisionLayer = 2, CollisionMask = 0 };
            contact.AddChild(new CollisionShape3D { Shape = shape }); root.AddChild(contact);
            contact.GlobalPosition = phantom.GlobalPosition;
            TrackShapePhantomGraph(contact, "contact", nativeObjects);
            await AwaitShapePhantomContact(phantom, contact);
            var contacts = phantom.GetOverlappingBodies();
            using (var owner = (Godot.Collections.Array)contacts)
                Require(contacts.Count == 1 && contacts[0] == contact,
                    "Native source query volume did not publish the actual overlapping body.");
            GamebryoReferenceEnableRuntime.Apply(warm, false);
            await ShapePhantomFrames();
            Require(phantom.CollisionMask == 0, "Source Disable did not suppress the real shaped-phantom filter.");
            GamebryoReferenceEnableRuntime.Apply(warm, true);
            await AwaitShapePhantomContact(phantom, contact);
            Require(phantom.CollisionMask == 2, "Source Enable did not restore the same authored query filter.");
            contacts = phantom.GetOverlappingBodies();
            using (var owner = (Godot.Collections.Array)contacts)
                Require(contacts.Count == 1 && contacts[0] == contact,
                    "Re-enabled shaped phantom lost its actual native contact.");

            var originalId = phantom.GetInstanceId();
            warm.Free(); warm = null;
            rebuiltPrototype = new RuntimeNativeNifPrototype(FalloutNifFile.Read(bytes), .1f);
            TrackShapePhantomGraph(rebuiltPrototype.Scene.Root, "coldSource", nativeObjects);
            rebuilt = rebuiltPrototype.InstantiatePlaced(placement);
            root.AddChild(rebuilt);
            var cold = NativeNifCollisionBuilder.PlacedPhantoms(rebuilt, 2).Single();
            TrackShapePhantomGraph(rebuilt, "cold", nativeObjects);
            Require(cold.GetInstanceId() != originalId && cold.Transform.Origin == expected &&
                cold.GetMeta("opennv_nif_phantom_source").AsString() ==
                decoded.Sha256 + ":3:0:2:1", "Native reconstruction changed its source geometry/identity.");
            await AwaitShapePhantomContact(cold, contact);
            contacts = cold.GetOverlappingBodies();
            using (var owner = (Godot.Collections.Array)contacts)
                Require(contacts.Count == 1 && contacts[0] == contact,
                    "Rebuilt source volume has no actual broadphase result.");
            contact.CollisionLayer = 0;
            using var ray = PhysicsRayQueryParameters3D.Create(cold.GlobalPosition + Vector3.Up * 10,
                cold.GlobalPosition - Vector3.Up * 10, uint.MaxValue);
            await ShapePhantomFrames();
            using var hit = root.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            Require(hit.Count == 0, "Non-solid source shaped phantom was substituted by floor geometry.");
            var sphere = RuntimeNativeReferenceEvents.CreateTrigger(new(3, 7, 11, 2, 12), .1f, 2);
            try
            {
                TrackShapePhantomGraph(sphere, "sphere", nativeObjects);
                using var sphereShape = sphere.GetChild<CollisionShape3D>(0).Shape;
                Require(sphereShape is SphereShape3D { Radius: .3f },
                    "Source sphere radius was replaced by an equality requirement on ignored stored bounds.");
            }
            finally { sphere.Free(); }
            foreach (var malformed in new[]
            {
                AuthoredShapePhantom(layer: 14), AuthoredShapePhantom(flags: 1),
                AuthoredShapePhantom(group: 1), AuthoredShapePhantom(broadPhase: 1),
                AuthoredShapePhantom(properties: 1), AuthoredShapePhantom(target: 1),
            })
            {
                Node3D? invalid = null;
                try
                {
                    invalid = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(malformed), .1f).Root;
                    throw new InvalidOperationException("Unsupported source phantom was admitted by its native factory.");
                }
                catch (Exception error) when (error is NotSupportedException or InvalidDataException) { }
                finally { invalid?.Free(); }
            }
            GD.Print("OPENNV_NATIVE_SHAPED_PHANTOM_PASS authoredFullReader=true actualOverlap=true " +
                "matrixPlacement=true currentEnable=true reconstructedGeometry=true solidFloor=false " +
                "firstBoundSphere=true unsupportedFiltersRefused=true activeContactColdMembership=unowned");
        }
        finally
        {
            root?.Free(); prototype?.Scene.Root.Free(); rebuiltPrototype?.Scene.Root.Free();
            GC.Collect(); GC.WaitForPendingFinalizers();
            var retirement = Stopwatch.StartNew();
            string[] retained;
            while (true)
            {
                retained = nativeObjects.Where(pair => ShapePhantomInstanceAlive(validity, pair.Value))
                    .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
                if (retained.Length == 0 || retirement.Elapsed.TotalSeconds >= 2) break;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(retained.Length == 0, "Native shaped-phantom owners remained live after source retirement: " +
                string.Join(", ", retained) + $"; wallSeconds={retirement.Elapsed.TotalSeconds:R}");
        }
        GD.Print($"OPENNV_NATIVE_SHAPED_PHANTOM_RETIREMENT_PASS nativeIds={nativeObjects.Count} allGone=true nonbindingValidity=true boundedWallSeconds=2");
    }

    private async Task ShapePhantomFrames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task AwaitShapePhantomContact(Area3D phantom, StaticBody3D expected)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var bodies = phantom.GetOverlappingBodies();
            using var owner = (Godot.Collections.Array)bodies;
            if (bodies.Count == 1 && bodies[0] == expected) return;
            Require(clock.Elapsed.TotalSeconds < 2, "Real shaped-phantom broadphase did not publish the authored body within two wall seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static bool ShapePhantomInstanceAlive(Expression query, ulong id)
    {
        using var inputs = new Godot.Collections.Array { unchecked((long)id) };
        using var result = query.Execute(inputs, showError: false);
        Require(!query.HasExecuteFailed() && result.VariantType == Variant.Type.Bool,
            "Native nonbinding shaped-phantom validity query failed: " + query.GetErrorText());
        return result.AsBool();
    }

    private static void TrackShapePhantomGraph(Node node, string name, IDictionary<string, ulong> ids)
    {
        ids.Add(name, node.GetInstanceId());
        if (node is CollisionShape3D collision)
        {
            using var shape = collision.Shape;
            if (shape is not null) ids.Add(name + "/shape", shape.GetInstanceId());
        }
        for (var index = 0; index < node.GetChildCount(); index++)
            TrackShapePhantomGraph(node.GetChild(index), name + "/" + index, ids);
    }

    private static byte[] AuthoredShapePhantom(byte layer = 12, byte flags = 0, ushort group = 0,
        byte broadPhase = 2, uint properties = 0, int target = 0)
    {
        byte[] Bytes(Action<BinaryWriter> write)
        { using var data = new MemoryStream(); using var writer = new BinaryWriter(data); write(writer); return data.ToArray(); }
        void Matrix(BinaryWriter writer)
        { foreach (var value in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5, -7, 9, 1 }) writer.Write(value); }
        var node = Bytes(writer =>
        {
            writer.Write(-1); writer.Write(0U); writer.Write(-1); writer.Write((ushort)0);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(0U); writer.Write(3); writer.Write(0U); writer.Write(0U);
        });
        var box = Bytes(writer =>
        {
            writer.Write(0U); writer.Write(0f); writer.Write(new byte[8]);
            writer.Write(2f); writer.Write(3f); writer.Write(4f); writer.Write(0f);
        });
        var phantom = Bytes(writer =>
        {
            writer.Write(1); writer.Write(layer); writer.Write(flags); writer.Write(group); writer.Write(0U);
            writer.Write(broadPhase); writer.Write(new byte[3]);
            writer.Write(0U); writer.Write(properties); writer.Write(0x80000000U); writer.Write(new byte[8]); Matrix(writer);
        });
        var attachment = Bytes(writer => { writer.Write(target); writer.Write((ushort)1); writer.Write(2); });
        (string Type, byte[] Data)[] blocks = [("NiNode", node), ("bhkBoxShape", box),
            ("bhkSimpleShapePhantom", phantom), ("bhkSPCollisionObject", attachment)];
        return Bytes(writer =>
        {
            void Text(string value) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(21U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length); foreach (var block in blocks) Text(block.Type);
            for (ushort index = 0; index < blocks.Length; index++) writer.Write(index);
            foreach (var block in blocks) writer.Write(block.Data.Length);
            writer.Write(0U); writer.Write(0U); writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Data);
            writer.Write(1U); writer.Write(0);
        });
    }
}
