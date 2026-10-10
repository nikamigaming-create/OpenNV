using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task ExerciseSerializedSourceOcclusion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-serialized-plane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Contact.esm");
        Node3D? root = null; RuntimeNativeRoomPortalGraph? graph = null; Camera3D? camera = null; BoxMesh? mesh = null;
        var identities = new List<ulong>();
        var previous = GetViewport().GetCamera3D();
        using var query = new Expression();
        Require(query.Parse("is_instance_id_valid(id)", ["id"]) == Error.Ok, "Occlusion retirement query failed to parse.");
        const string engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
        byte[] F(float value) => BitConverter.GetBytes(value);
        byte[] U(uint value) => BitConverter.GetBytes(value);
        byte[] JoinPlane(params byte[][] values) => values.SelectMany(value => value).ToArray();
        byte[] Primitive() => JoinPlane(F(99), F(1), F(77), new byte[16], U(3));
        byte[] Placed(uint id, uint basis, float nativeDepth, params byte[][] fields) => Record("REFR", id,
            Field("NAME", U(basis)), Field("DATA", JoinPlane(F(0), F(-nativeDepth), F(0), new byte[12])), JoinPlane(fields));
        try
        {
            var header = new byte[12]; System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var pose = JoinPlane(F(2), F(3), F(0), F(-10), F(0), F(1), F(0), F(0), F(MathF.PI * .5f));
            var bodies = JoinPlane(Placed(0x900, 0x17, 0, Field("XPRM", Primitive()), Field("XOCP", pose)),
                Placed(0x901, 0x100, 20), Placed(0x902, 0x100, 2), Placed(0x903, 0x100, 10));
            var group = new byte[24 + bodies.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6); bodies.CopyTo(group, 24);
            File.WriteAllBytes(path, JoinPlane(Record("TES4", 0, Field("HEDR", header)), Record("STAT", 0x17),
                Record("STAT", 0x100), Record("CELL", 0x800, Field("DATA", [1])), group));
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var scene = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(scene);
            root = new Node3D(); root.Name = "AuthoredSourceOcclusionCell"; AddChild(root); identities.Add(root.GetInstanceId());
            var presentation = new Node3D(); root.AddChild(presentation); identities.Add(presentation.GetInstanceId());
            var source = FalloutDynamicPrimitive.Read(FalloutDynamicPrimitiveSource.Read(engine), records,
                scene.References.Single(row => row.FormKey == Key(0x900)));
            var plane = RuntimeNativeSourceOcclusionPlane.Create(source, world, Transform3D.Identity, 1);
            root.AddChild(plane); identities.Add(plane.GetInstanceId());
            camera = new Camera3D(); camera.Near = .25f; camera.Far = 50; camera.Rotation = new(0, MathF.PI, 0);
            root.AddChild(camera); camera.MakeCurrent(); identities.Add(camera.GetInstanceId());
            mesh = new BoxMesh(); mesh.Size = new(.25f, .25f, .25f); identities.Add(mesh.GetInstanceId());
            var keys = new[] { Key(0x901), Key(0x902), Key(0x903) };
            var positions = keys.Select(key => scene.References.Single(row => row.FormKey == key).Position)
                .Select(value => new Vector3(value[0], value[2], -value[1])).ToArray();
            var members = new Dictionary<FalloutFormKey, Node3D>(); var native = new List<MeshInstance3D>();
            for (var ordinal = 0; ordinal < keys.Length; ordinal++)
            {
                var instance = new MeshInstance3D(); instance.Mesh = mesh; instance.Position = positions[ordinal];
                root.AddChild(instance); native.Add(instance); members.Add(keys[ordinal], instance); identities.Add(instance.GetInstanceId());
            }
            graph = RuntimeNativeRoomPortalGraph.Attach(root, world, records, scene, FalloutPortalCullingSource.Read(engine),
                presentation, () => members, 1, error => throw new InvalidDataException("Actual authored occlusion draw failed.", error));
            identities.Add(graph.GetInstanceId()); plane.RequirePublished();
            Require(!graph.NativeVisibility(native[0]) && graph.NativeVisibility(native[1]) && graph.NativeVisibility(native[2]),
                "Actual native source cone failed its background, foreground or crossing RID visibility publication.");
            Require(native.All(node => node.Visible && node.IsInsideTree()) &&
                !plane.FindChildren("*", "CollisionObject3D", true, false).Any(),
                "Occlusion substituted gameplay enable/collision or detached geometry for a render consumer.");
            var snapshot = graph.Capture();
            var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot);
            var restored = System.Text.Json.JsonSerializer.Deserialize<FalloutRoomPortalSnapshot>(serialized)!;
            Require(restored.Occlusion is { Planes.Count: 1 } && restored.Occlusion.Planes[0].Reference == Key(0x900),
                "Current save serialization dropped its real occlusion source authority.");
            graph.Retire();
            Require(native.All(node => node.Visible), "Independent render retirement changed ordinary node visibility.");
            root.Free(); root = null; graph = null; camera = null;
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Native plane changed source bytes.");
            GD.Print("OPENNV_NATIVE_SERIALIZED_OCCLUSION_PASS fullReader=true sourceAxisAngle=true actualCamera=true " +
                "negativeRIDPublication=true independentSibling=true gameplayEnableUnchanged=true coldField=true pixels=UNMEASURED");
        }
        finally
        {
            var failures = new List<Exception>();
            try { graph?.Retire(); } catch (Exception error) { failures.Add(error); }
            try { root?.Free(); } catch (Exception error) { failures.Add(error); }
            try { mesh?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try
            {
                if (previous is not null && GodotObject.IsInstanceValid(previous) && previous.IsInsideTree()) previous.MakeCurrent();
            }
            catch (Exception error) { failures.Add(error); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            var clock = Stopwatch.StartNew();
            ulong[] retained;
            do
            {
                retained = identities.Where(id => ShapePhantomInstanceAlive(query, id)).ToArray();
                if (retained.Length == 0 || clock.Elapsed.TotalSeconds >= 2) break;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            } while (true);
            if (retained.Length != 0) failures.Add(new InvalidDataException("Serialized plane retained native owners: " + string.Join(",", retained)));
            try { File.Delete(path); Directory.Delete(directory); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Serialized plane failed independent retirement.", failures);
        }
    }
}
