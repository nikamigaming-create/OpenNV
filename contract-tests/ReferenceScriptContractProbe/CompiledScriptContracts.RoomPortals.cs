using System.Numerics;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunRoomPortals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-room-portal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Bytecode.esm");
        var source = FalloutPortalCullingSource.Read("c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e");
        try
        {
            byte[] Primitive(float x, float y, float z, uint type) => Join(BitConverter.GetBytes(x),
                BitConverter.GetBytes(y), BitConverter.GetBytes(z), new byte[16], U32(type));
            byte[] Ref(uint id, uint basis, params byte[][] fields) => Record("REFR", id,
                Field("NAME", U32(basis)), Field("DATA", new byte[24]), Join(fields));
            File.WriteAllBytes(path, Join(Tes4(), Record("STAT", 0x1f), Record("STAT", 0x20),
                Record("STAT", 0x90), Record("CELL", 0x800, Field("DATA", [1])), PrimitiveSourceGroup(
                    Ref(0x91a, 0x1f, Field("XPRM", Primitive(10, 10, 10, 1))),
                    Ref(0x914, 0x1f, Field("XPRM", Primitive(10, 10, 10, 1))),
                    Ref(0x927, 0x20, Field("XPRM", Primitive(2, 1, 2, 3)), Field("XPOD", Join(U32(0x914), U32(0x91a)))),
                    Ref(0x92d, 0x90, Field("XMBR", U32(0x914))))));
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            var scene = FalloutCellSceneReader.Read(records, Key(0x800));
            var registrations = scene.References.Where(row => row.Base != Key(0x90))
                .Select(row => FalloutDynamicPrimitive.Read(FalloutDynamicPrimitiveSource.Read(source.EngineSha256), records, row)).ToArray();
            FalloutRoomPortalGraph Graph()
            {
                var result = new FalloutRoomPortalGraph(source, scene.Cell.FormKey);
                foreach (var row in registrations) result.Register(row);
                result.Seal(key => key);
                return result;
            }
            var graph = Graph();
            try
            {
                var roomA = Key(0x91a); var roomB = Key(0x914); var portal = Key(0x927);
                FalloutVisibilityBound Bound(Vector3 center) => new(center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
                    new(10), false);
                FalloutRoomGeometry[] rooms = [new(roomA, true, Bound(Vector3.Zero)), new(roomB, true, Bound(new(0, 0, -20)))];
                Vector3[] rectangle = [new(-2, -2, -10), new(2, -2, -10), new(2, 2, -10), new(-2, 2, -10)];
                FalloutPortalGeometry[] portals = [new(portal, true, rectangle)];
                FalloutVisibilityPlane[] frustum = [new(Vector3.UnitX, -100), new(-Vector3.UnitX, -100),
                    new(Vector3.UnitY, -100), new(-Vector3.UnitY, -100), new(-Vector3.UnitZ, .1f), new(Vector3.UnitZ, -100)];
                var camera = new FalloutRoomCamera(Vector3.Zero, new(0, 0, -.1f), frustum);
                graph.Observe([camera], rooms, portals, 1);
                Require(graph.Views.Single().CachedRoom == roomA &&
                    graph.Views.Single().Traversal.SequenceEqual([roomA, roomB]) &&
                    graph.Views.Single().Portals.SequenceEqual([portal]) &&
                    graph.Sees(roomB, new(new(0, 0, -20), .1f)) && !graph.Sees(roomB, new(new(8, 0, -20), .1f)),
                    "Original physical slots, ordered traversal or camera-edge window differs from authored geometry.");
                Require(FalloutRoomMembership.Read(records, Key(0x92d)).Room == roomB,
                    "Room member did not use its winning original XMBR.");
                Reject(() => graph.Sees(Key(0x999), new(Vector3.Zero, 1)));
                graph.Observe([camera], rooms, [portals[0] with { Enabled = false }], 1);
                Require(!graph.Sees(roomB, new(new(0, 0, -20), .1f)), "Disabled native portal still traversed its source connection.");
                graph.Observe([camera with { Origin = new(0, 0, -20), NearPoint = new(0, 0, -20.1f) }], rooms, portals, 1);
                Require(graph.Views.Single().CachedRoom == roomB, "Actual camera near point did not replace the cached room.");
                var saved = graph.Capture();
                var cold = Graph();
                try
                {
                    cold.Restore(saved); Reject(() => cold.Capture());
                    cold.Observe([camera with { NearPoint = new(0, 0, -10) }], rooms, portals, 1);
                    Require(cold.Views.Single().CachedRoom == roomB && cold.Generation == saved.LastNativeGeneration + 1,
                        "Cold native observation lost the still-containing room cache or invented/replayed a draw.");
                }
                finally { cold.Retire(); }
                var drift = Graph();
                try { Reject(() => drift.Restore(saved with { RegistrationSha256 = new('f', 64) })); }
                finally { drift.Retire(); }
                var targetDrift = new FalloutRoomPortalGraph(source, scene.Cell.FormKey);
                try
                {
                    foreach (var row in registrations) targetDrift.Register(row);
                    targetDrift.Seal(_ => roomA);
                    Reject(() => targetDrift.Restore(saved));
                }
                finally { targetDrift.Retire(); }
                var newTopology = Graph();
                try
                {
                    newTopology.Restore(saved); newTopology.Observe([camera, camera], rooms, portals, 1);
                    Require(newTopology.ColdNativeTopologyRebuilt && newTopology.Views.Count == 2 &&
                        newTopology.Views.All(view => view.CachedRoom == roomA),
                        "A fresh native view topology borrowed old view-index caches instead of actual source room selection.");
                }
                finally { newTopology.Retire(); }
                var external = new FalloutRoomPortalGraph(source, scene.Cell.FormKey);
                try
                {
                    foreach (var row in registrations)
                        external.Register(row.Reference == portal ? row with { PortalRooms = [null, roomA] } : row);
                    external.Seal(key => key);
                    external.Observe([camera with { NearPoint = new(0, 0, 1000) }], rooms, portals, 1);
                    Require(external.Views.Single().CachedRoom is null &&
                        external.Views.Single().Traversal.SequenceEqual([null, roomA]) &&
                        external.Views.Single().Portals.SequenceEqual([portal]), "External null slot or its genuine traversal was lost.");
                }
                finally { external.Retire(); }
                Require(FalloutPortalVisibilityMath.Cone(new(0, 0, -5), rectangle, 5).Length == 0 &&
                    FalloutPortalVisibilityMath.Cone(new(0, 0, -4), rectangle, 5).Length == 5,
                    "Selected coplanar interval or five active cone planes changed.");
                Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Room graph mutated its authored source file.");
            }
            finally { graph.Retire(); }
            Console.WriteLine("OPENNV_SOURCE_ROOM_PORTALS_PASS fullReader=true physicalSlots=true orderedTraversal=true " +
                "cameraNearPoint=true portalWindow=true sourceDisabled=true externalNull=true coldFreshNativeRequired=true " +
                "sourceDriftRefused=true sourceUnchanged=true pixels=UNMEASURED");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
