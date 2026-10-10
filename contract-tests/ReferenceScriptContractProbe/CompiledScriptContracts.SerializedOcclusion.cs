using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using V3 = System.Numerics.Vector3;

internal static partial class CompiledScriptContracts
{
    internal static void RunSerializedOcclusion()
    {
        const string engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
        var source = FalloutDynamicPrimitiveSource.Read(engine);
        var algorithm = FalloutOcclusionCullingSource.Read(engine);
        var directory = Path.Combine(Path.GetTempPath(), "opennv-serialized-occlusion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Bytecode.esm");
        byte[] Primitive() => Join(BitConverter.GetBytes(99f), BitConverter.GetBytes(1f), BitConverter.GetBytes(77f), new byte[16], U32(3));
        byte[] Pose(float width = 2, float height = 3) => Join(new[] { width, height, 0, -10, 0, 1, 0, 0, MathF.PI * .5f }
            .Select(BitConverter.GetBytes).ToArray());
        byte[] Placed(uint id, params byte[][] fields) => Record("REFR", id, Field("NAME", U32(0x17)),
            Field("XPRM", Primitive()), Field("DATA", new byte[24]), Join(fields));
        byte[] Envelope(params byte[][] extra) => Join(Tes4(), Record("STAT", 0x17), Record("CELL", 0x800, Field("DATA", [1])),
            PrimitiveSourceGroup(Placed(0x900, extra), Placed(0x901, Field("XOCP", Pose())),
                Placed(0x902, Field("XOCP", Pose()))));
        try
        {
            var bytes = Envelope(Field("XOCP", Pose()), Field("XORD", Join(U32(0x902), U32(0), U32(0x901), U32(0x902))),
                Field("XPTL", Enumerable.Repeat((byte)0xff, 36).ToArray()));
            File.WriteAllBytes(path, bytes); var hash = SHA256.HashData(bytes);
            FalloutDynamicPrimitive first;
            using (var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]))
            {
                var scene = FalloutCellSceneReader.Read(records, Key(0x800));
                first = FalloutDynamicPrimitive.Read(source, records, scene.References.Single(row => row.FormKey == Key(0x900)));
                Require(first.SerializedPlane is { HalfWidth: 2, HalfHeight: 3 } &&
                    first.SerializedPlane.Axis == V3.UnitX && first.SerializedPlane.Angle > 1 &&
                    first.Primitive.X == 99 && first.DeclaredReferencePose.All(bits => bits == 0) &&
                    first.OrderedOcclusionPlanes.SequenceEqual([Key(0x902), null, Key(0x901), Key(0x902)]) &&
                    first.DiscardedPortalDeclaration is { Signature: "XPTL" },
                    "Full reader substituted source dimensions, a quaternion, reference pose, link order or discarded portal bytes.");
                var rotation = first.SerializedPlane!.ClockwiseRotation();
                Require(MathF.Abs(rotation[5] - 1) < .00001f && MathF.Abs(rotation[7] + 1) < .00001f,
                    "Declared source axis-angle used the opposite rotation direction.");
                var overrides = Path.Combine(directory, "Override.esp");
                File.WriteAllBytes(overrides, Join(Tes4("Bytecode.esm"), PrimitiveSourceGroup(Placed(0x900,
                    Field("XOCP", Pose(7, 11)), Field("XORD", Join(U32(0x901), U32(0), U32(0x902), U32(0x901)))))));
                using var winners = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Override.esp"]);
                var winnerScene = FalloutCellSceneReader.Read(winners, Key(0x800));
                var winner = FalloutDynamicPrimitive.Read(source, winners, winnerScene.References.Single(row => row.FormKey == Key(0x900)));
                Require(winner.SerializedPlane is { HalfWidth: 7, HalfHeight: 11 } &&
                    winner.Reference == first.Reference && winner.SourceSha256 != first.SourceSha256 &&
                    winner.OrderedOcclusionPlanes[0] == Key(0x901), "Winning override lost master-adjusted identity or original fields.");
            }
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Serialized reader changed authored source bytes.");
            foreach (var fields in new byte[][][]
            {
                [Field("XOCP", new byte[35])], [Field("XOCP", Pose()), Field("XOCP", Pose())],
                [Field("XOCP", Pose(float.NaN))], [Field("XOCP", Pose(0))],
                [Field("XORD", new byte[15])], [Field("XORD", Join(U32(0x17), U32(0), U32(0), U32(0)))],
                [Field("XORD", Join(U32(0x799), U32(0), U32(0), U32(0)))], [Field("XPLT", Pose())]
            })
            {
                File.WriteAllBytes(path, Envelope(fields));
                using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
                Reject(() => FalloutDynamicPrimitive.Read(source, records,
                    FalloutCellSceneReader.Read(records, Key(0x800)).References.Single(row => row.FormKey == Key(0x900))));
            }
            // Independent geometry/event observations exercise the pure owner;
            // they never count as an ordinary native camera or game capability.
            File.WriteAllBytes(path, Envelope(Field("XOCP", Pose())));
            using (var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]))
            {
                var scene = FalloutCellSceneReader.Read(records, Key(0x800));
                var descriptor = FalloutDynamicPrimitive.Read(source, records, scene.References.Single(row => row.FormKey == Key(0x900)));
                var state = new FalloutOcclusionGraph(algorithm, Key(0x800));
                state.Register(descriptor, FalloutRoomMembership.Read(records, descriptor.Reference)); state.Seal(_ => false);
                var corners = descriptor.SerializedPlane!.SourceCorners().Select(point => new V3(point.X, point.Z, -point.Y)).ToArray();
                var geometry = new FalloutOcclusionGeometry(Key(0x900), true, FalloutOcclusionCullMode.Automatic, corners);
                FalloutVisibilityPlane[] frustum = [new(V3.UnitZ, 1), new(-V3.UnitZ, -30), new(V3.UnitX, -20),
                    new(-V3.UnitX, -20), new(V3.UnitY, -20), new(-V3.UnitY, -20)];
                var camera = new FalloutRoomCamera(V3.Zero, V3.UnitZ, frustum);
                var behind = new FalloutVisibilitySphere(new(0, 0, 20), .25f);
                var front = new FalloutVisibilitySphere(new(0, 0, 2), .25f);
                var crossing = new FalloutVisibilitySphere(new(0, 0, 10), .25f);
                state.Observe([camera], [geometry], 1, 1);
                Require(!state.Sees(0, null, behind) && state.Sees(0, null, front) && state.Sees(0, null, crossing),
                    "Negative source cone hid an independent foreground/intersecting bound or failed its actual occlusion.");
                var saved = state.Capture(); saved.Validate();
                var cold = new FalloutOcclusionGraph(algorithm, Key(0x800));
                cold.Register(descriptor, FalloutRoomMembership.Read(records, descriptor.Reference)); cold.Seal(_ => false); cold.Restore(saved);
                Reject(() => cold.Sees(0, null, behind)); cold.Observe([camera], [geometry], 1, 2);
                Require(!cold.Sees(0, null, behind), "Cold occlusion lost its fresh native observation requirement or source identity.");
                state.Observe([camera], [geometry with { Enabled = false }], 1, 2);
                Require(state.Sees(0, null, behind), "Source-disabled plane still contributed a negative cone.");
                Reject(() => state.Observe([camera], [], 1, 3));
                Reject(() => cold.Restore(saved with { RegistrationSha256 = new('f', 64) }));
                state.Retire(); cold.Retire(); Reject(() => cold.Sees(0, null, behind));
            }
            Console.WriteLine("OPENNV_SOURCE_SERIALIZED_OCCLUSION_PASS fullReader=true sourceAxisAngle=true exactStoredPose=true " +
                "orderedLinks=true masterOverrides=true discardedXPTL=true negativeBounds=true coldFreshFrame=true native=UNEXECUTED pixels=UNMEASURED");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
