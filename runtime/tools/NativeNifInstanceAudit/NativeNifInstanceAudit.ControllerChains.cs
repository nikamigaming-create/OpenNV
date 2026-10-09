using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseSourceControllerChains()
    {
        await SettleCollisionLifetimeResources();
        var orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        foreach (var mode in new[] { "blend-before", "blend-after", "blend-middle" })
        {
            var bytes = FalloutNifPhysicsFixture.ControllerChain(mode);
            var hash = SHA256.HashData(bytes);
            var source = FalloutNifFile.Read(bytes);
            RuntimeNativeNifScene? first = null, second = null;
            try
            {
                first = RuntimeNativeNifMeshBuilder.Build(source, 1);
                second = RuntimeNativeNifMeshBuilder.Build(source, 1);
                AddChild(first.Root); AddChild(second.Root);
                var node = first.Root.GetChild<Node3D>(0);
                var sibling = second.Root.GetChild<Node3D>(0);
                var expected = mode == "blend-middle" ? new[] { 1, 2, 3 } : new[] { 1, 2 };
                if (node.Position != new Vector3(2, 4, -3) || sibling.Position != node.Position ||
                    !node.GetMeta("opennv_nif_node_controller_chain").AsInt32Array().SequenceEqual(expected) ||
                    !node.HasMeta("opennv_nif_static_transform_controller") ||
                    !node.HasMeta("opennv_nif_blend_controller_source_dormant"))
                    throw new InvalidDataException("Actual native source chain skipped a static transform or a declared blend continuation.");
                foreach (var refused in new[] { "cycle", "foreign-target", "node-suffix", "unknown-keys", "active-blend", "wrong-flags", "unowned-float-suffix" })
                {
                    var negative = FalloutNifPhysicsFixture.ControllerChain(refused);
                    var negativeHash = SHA256.HashData(negative);
                    RuntimeNativeNifScene? unexpected = null;
                    try
                    {
                        try { unexpected = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(negative), 1); }
                        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                        {
                            var expectedError = refused switch
                            {
                                "cycle" => "controller chain repeats block",
                                "foreign-target" => "foreign or unknown target declaration",
                                "node-suffix" => "no time-controller chain owner",
                                "unowned-float-suffix" => "has no native node channel owner",
                                _ => "requires an active physics/animation blend owner",
                            };
                            if (!error.Message.Contains(expectedError, StringComparison.Ordinal))
                                throw new InvalidDataException("Native controller negative reached an unrelated refusal: " + refused, error);
                        }
                        if (unexpected is not null)
                            throw new InvalidDataException("Actual native source controller owner accepted " + refused);
                    }
                    finally { unexpected?.Root.Free(); }
                    if (!GodotObject.IsInstanceValid(sibling) || sibling.Position != new Vector3(2, 4, -3) ||
                        !SHA256.HashData(negative).SequenceEqual(negativeHash))
                        throw new InvalidDataException("Refused source chain changed its independent live sibling or source bytes.");
                }
                first.Root.Free(); first = null;
                if (!GodotObject.IsInstanceValid(sibling) || sibling.Position != new Vector3(2, 4, -3))
                    throw new InvalidDataException("Releasing one source controller scene retired its independent sibling.");
            }
            finally { first?.Root.Free(); second?.Root.Free(); }
            if (!SHA256.HashData(bytes).SequenceEqual(hash))
                throw new InvalidDataException("Native controller construction changed its authored source bytes.");
        }
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != orphans ||
            Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Source controller construction or refusal retained native nodes/resources.");
        GD.Print("OPENNV_NATIVE_NIF_CONTROLLER_CHAIN_PASS placements=3 sourceChain=true staticTransform=true " +
            "blendContinuation=true malformedTarget=true cycle=true activeBlendRefused=true " +
            "unownedSuffixRefused=true liveSibling=unchanged sourceBytes=unchanged orphanNodes=unchanged nativeResourceGrowth=0");
    }
}
