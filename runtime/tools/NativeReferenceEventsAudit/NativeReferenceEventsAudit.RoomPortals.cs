using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task ExerciseRoomPortals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-native-room-portals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Contact.esm");
        SubViewport? viewport = null; Node3D? root = null;
        BoxMesh? box = null; FalloutPluginStack? records = null;
        FalloutReferenceWorld? warm = null, cold = null;
        var owners = new Dictionary<string, ulong>(StringComparer.Ordinal);
        using var query = new Expression();
        Require(query.Parse("is_instance_id_valid(id)", ["id"]) == Error.Ok, "Portal native retirement query failed.");
        var failures = new List<Exception>();
        Exception? enteredFailure = null;
        try
        {
            byte[] Join(params byte[][] bytes) => bytes.SelectMany(value => value).ToArray();
            byte[] Primitive(float x, float y, float z, uint type) => Join(BitConverter.GetBytes(x),
                BitConverter.GetBytes(y), BitConverter.GetBytes(z), new byte[16], BitConverter.GetBytes(type));
            byte[] Placed(uint id, uint basis, float x, float y, params byte[][] fields)
            {
                var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, x);
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), y);
                return Record("REFR", id, Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", data), Join(fields));
            }
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var children = Join(Placed(0x91a, 0x1f, 0, 0, Field("XPRM", Primitive(10, 10, 10, 1))),
                Placed(0x914, 0x1f, 0, 20, Field("XPRM", Primitive(10, 10, 10, 1))),
                Placed(0x927, 0x20, 0, 10, Field("XPRM", Primitive(2, 1, 2, 3)),
                    Field("XPOD", Join(BitConverter.GetBytes(0x914u), BitConverter.GetBytes(0x91au)))),
                Placed(0x92d, 0x90, 0, 20, Field("XMBR", BitConverter.GetBytes(0x914u))),
                Placed(0x92e, 0x90, 8, 20, Field("XMBR", BitConverter.GetBytes(0x914u))));
            var group = new byte[24 + children.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6);
            children.CopyTo(group, 24);
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", header)), Record("STAT", 0x1f),
                Record("STAT", 0x20), Record("STAT", 0x90), Record("CELL", 0x800, Field("DATA", [1])), group));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            var stack = FalloutPluginStack.Load(directory, ["Contact.esm"]); records = stack;
            var scene = FalloutCellSceneReader.Read(stack, Key(0x800));
            var source = FalloutPortalCullingSource.Read("c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e");
            var liveWorld = new FalloutReferenceWorld(stack); warm = liveWorld; liveWorld.LoadCell(scene);
            var view = new SubViewport
            {
                Size = new(1280, 720),
                OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            viewport = view; owners.Add("viewport", view.GetInstanceId()); AddChild(view);
            var mesh = new BoxMesh { Size = new(.25f, .25f, .25f) }; box = mesh;
            owners.Add("authoredMesh", mesh.GetInstanceId());
            (RuntimeNativeRoomPortalGraph Graph, Camera3D Camera, Dictionary<FalloutFormKey, Node3D> Members) Build(
                FalloutReferenceWorld world, FalloutRoomPortalSnapshot? saved, Vector3 cameraPosition, string prefix)
            {
                root = new Node3D { Name = "AuthoredPortalCell" }; view.AddChild(root);
                var publication = new Node { Name = "AuthoredWinningMemberPublication" }; root.AddChild(publication);
                var members = new Dictionary<FalloutFormKey, Node3D>();
                foreach (var reference in scene.References)
                {
                    var position = GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2]));
                    Node3D node;
                    if (reference.Base == Key(0x90))
                        node = new MeshInstance3D { Name = "AuthoredRoomGeometry", Mesh = mesh, Position = position };
                    else
                        node = RuntimeNativeSourcePortalPrimitive.Create(FalloutDynamicPrimitive.Read(
                            FalloutDynamicPrimitiveSource.Read(source.EngineSha256), stack, reference), world, new(Basis.Identity, position), 1);
                    node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); root.AddChild(node);
                    members.Add(reference.FormKey, node); GamebryoReferenceEnableRuntime.Apply(node, world.IsEnabled(reference.FormKey));
                }
                var camera = new Camera3D { Position = cameraPosition, Near = .1f, Far = 100, Fov = 60, Current = true };
                root.AddChild(camera);
                var graph = RuntimeNativeRoomPortalGraph.Attach(root, world, stack, scene, source, publication,
                    () => members, 1, failures.Add, saved);
                TrackShapePhantomGraph(root, prefix, owners); graph.Publish();
                return (graph, camera, members);
            }
            async Task Wait(Func<bool> observed, string message)
            {
                var timeout = Stopwatch.StartNew();
                while (!observed() && failures.Count == 0 && timeout.Elapsed.TotalSeconds < 2)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(failures.Count == 0 && observed(), message + "; failures=" + string.Join(" | ", failures.Select(error => error.Message)));
            }
            var live = Build(liveWorld, null, Vector3.Zero, "warm");
            var visible = (MeshInstance3D)live.Members[Key(0x92d)]; var hidden = (MeshInstance3D)live.Members[Key(0x92e)];
            var layers = hidden.Layers; var process = hidden.ProcessMode;
            Require(live.Graph.NativeVisibility(visible) && !live.Graph.NativeVisibility(hidden) &&
                hidden.Visible && hidden.IsVisibleInTree() && hidden.Layers == layers && hidden.ProcessMode == process,
                "Native portal did not gate actual render RIDs independently of source enable/layer/processing.");
            liveWorld.SetEnabled(Key(0x927), false); liveWorld.AdvanceEnableChanges(0, new(1, 1), _ => false);
            GamebryoReferenceEnableRuntime.Apply(live.Members[Key(0x927)], false);
            await Wait(() => !live.Graph.NativeVisibility(visible), "Actual disabled portal remained traversable.");
            liveWorld.SetEnabled(Key(0x927), true); liveWorld.AdvanceEnableChanges(0, new(1, 1), _ => false);
            GamebryoReferenceEnableRuntime.Apply(live.Members[Key(0x927)], true);
            await Wait(() => live.Graph.NativeVisibility(visible), "Actual enabled portal lost its native window.");
            live.Camera.Position = new(0, 0, -20);
            await Wait(() => live.Graph.State.Views.Single().CachedRoom == Key(0x914), "Current native camera did not write its genuine room cache.");
            var saved = live.Graph.Capture(); var savedWorld = liveWorld.Capture();
            live.Graph.Retire(); root!.Free(); root = null;
            var coldWorld = new FalloutReferenceWorld(stack); cold = coldWorld;
            coldWorld.Restore(savedWorld); coldWorld.LoadCell(scene);
            var resumed = Build(coldWorld, saved, new(0, 0, -20), "cold");
            Require(resumed.Graph.State.Views.Single().CachedRoom == Key(0x914) &&
                resumed.Graph.State.Generation == saved.LastNativeGeneration + 1 &&
                resumed.Graph.NativeVisibility((MeshInstance3D)resumed.Members[Key(0x92d)]) &&
                original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                "Cold room graph failed fresh native reconstruction or changed its original source/cache.");
            resumed.Graph.Retire(); root!.Free(); root = null;
        }
        catch (Exception error)
        {
            enteredFailure = error;
            throw;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            void Cleanup(Action retire)
            {
                try { retire(); }
                catch (Exception error) { cleanupFailures.Add(error); }
            }
            Cleanup(() => root?.Free());
            Cleanup(() => viewport?.Free());
            Cleanup(() => box?.Dispose());
            Cleanup(() => cold?.Dispose());
            Cleanup(() => warm?.Dispose());
            Cleanup(() => records?.Dispose());
            Cleanup(() => File.Delete(path));
            Cleanup(() => Directory.Delete(directory));
            Cleanup(GC.Collect); Cleanup(GC.WaitForPendingFinalizers);
            try
            {
                var retirement = Stopwatch.StartNew(); string[] retained;
                while (true)
                {
                    retained = owners.Where(pair => ShapePhantomInstanceAlive(query, pair.Value))
                        .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
                    if (retained.Length == 0 || retirement.Elapsed.TotalSeconds >= 2) break;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Require(retained.Length == 0, "Native room graph retained owners: " + string.Join(", ", retained));
            }
            catch (Exception error) { cleanupFailures.Add(error); }
            if (cleanupFailures.Count != 0)
            {
                if (enteredFailure is not null) cleanupFailures.Insert(0, enteredFailure);
                throw new AggregateException("Room/portal fixture retained independent cleanup failures.",
                    cleanupFailures);
            }
        }
        GD.Print("OPENNV_NATIVE_SOURCE_ROOM_PORTALS_PASS fullReader=true actualCamera=true realRenderRidPublication=true " +
            "sourceEnableUnchanged=true sourceLayersUnchanged=true sourceDisabledPortal=true nativeColdReconstruction=true " +
            "allOwnersGone=true sourceUnchanged=true pixels=UNMEASURED");
    }
}
