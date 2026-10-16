using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedInitialPlayerModel(string dataRoot)
    {
        using var content = RuntimeLiveContentSource.Open(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var appearance = FalloutPlayerSourceAppearance.Select(
            FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7)), content, true, records);
        var prepared = FalloutNpcPreparedGeometry.Read(appearance, content);
        var scene = prepared.SourceModel.Instantiate().ConstructLogicalScene();
        try
        {
            var sources = new[] { prepared.SourceModel.Skeleton.Source }
                .Concat(prepared.SourceModel.Parts.Select(part => part.Physical.Source)).ToArray();
            var controllers = scene.Objects.Where(node => node.Source is FalloutNifNode)
                .SelectMany(node => FalloutNifNodeControllerChain.Read(sources[node.Key.Asset], (FalloutNifNode)node.Source)).ToArray();
            using var fixture = new PlayerReturnTailFixture();
            fixture.EnterLoad((load, guard) => scene.UpdateInitialPlayerModel(load, guard));
            var saved = scene.Capture();
            Require(saved.InitialModelUpdate is { Returned: true, WorldUpdated: true } update &&
                update.Controllers.Count == controllers.Length && update.Controllers.All(call => call.Returned) &&
                update.Controllers.Any(call => call.Interpolator == -1) && update.Controllers.Any(call => call.Components != FalloutNifTransformComponents.None),
                "Owned initial update did not execute its actual null and sampled source controllers.");
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!;
                var cold = prepared.SourceModel.Instantiate().ConstructLogicalScene(saved);
                try
                {
                    Require(JsonSerializer.Serialize(cold.Capture().InitialModelUpdate) == JsonSerializer.Serialize(saved.InitialModelUpdate) &&
                        cold.Capture().Objects.Select(node => JsonSerializer.Serialize(node.Local)).SequenceEqual(
                            saved.Objects.Select(node => JsonSerializer.Serialize(node.Local))),
                        "Owned cold model replayed its initial clock/pose publication.");
                    saved = cold.Capture();
                }
                finally { cold.Retire(); }
            }
            Console.WriteLine($"OPENNV_OWNED_INITIAL_PLAYER_MODEL_PASS controllers={controllers.Length} " +
                $"null={saved.InitialModelUpdate!.Controllers.Count(call => call.Interpolator == -1)} " +
                $"sampled={saved.InitialModelUpdate.Controllers.Count(call => call.Components != FalloutNifTransformComponents.None)} " +
                "componentCold3=true sourceFilesUnchanged=true loaderInvocation=AUTHORED gameplay=UNEXECUTED");
        }
        finally { scene.Retire(); }
    }

    private static void RunInitialPlayerModelContracts()
    {
        using var fixture = new PlayerReturnTailFixture();
        var prepared = FloatExtraModel(InitialControllerSkeleton()); var scene = prepared.ConstructLogicalScene();
        try
        {
            fixture.EnterLoad((load, guard) => scene.UpdateInitialPlayerModel(load, guard));
            var saved = scene.Capture(); var update = saved.InitialModelUpdate!;
            Require(update is { Returned: true, WorldUpdated: true, Controllers.Count: 2 } &&
                update.Controllers[0] is { ScaledTime: 0xff7fffff, Interpolator: -1, Components: FalloutNifTransformComponents.None } &&
                update.Controllers[1].ScaledTime == BitConverter.SingleToUInt32Bits(2) &&
                scene.Objects.Single(node => node.Key.Block == 1).Local is { Translation: { X: 4, Y: 5, Z: 6 }, Scale: 2 },
                "Initial controllers lost the reversed empty range, clamped sample, null branch or absolute scale store.");
            fixture.EnterLoad((load, guard) => Reject(() => scene.UpdateInitialPlayerModel(load, guard)));
            var shortened = saved with { InitialModelUpdate = update with { Controllers = [update.Controllers[0]] } };
            Reject(() => FloatExtraModel(InitialControllerSkeleton()).ConstructLogicalScene(shortened));
            Reject(() => FalloutActorLogicalScene.Validate(saved with
            { InitialModelUpdate = update with { Controllers = [update.Controllers[1], update.Controllers[0]] } }));
            Reject(() => FloatExtraModel(InitialControllerSkeleton(stop: 1)).ConstructLogicalScene(saved));
            var cold = FloatExtraModel(InitialControllerSkeleton()).ConstructLogicalScene(
                JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!);
            try { Require(cold.Objects.Single(node => node.Key.Block == 1).PoseStores == 1, "Cold initial model replayed a pose store."); }
            finally { cold.Retire(); }
        }
        finally { scene.Retire(); }
        var failed = FloatExtraModel(InitialControllerSkeleton(unsupportedSecond: true)).ConstructLogicalScene();
        try
        {
            fixture.EnterLoad((load, guard) => Reject(() => failed.UpdateInitialPlayerModel(load, guard)));
            Require(failed.Capture().InitialModelUpdate is { Returned: false, WorldUpdated: false, Controllers.Count: 1, Failure: not null },
                "Unsupported following controller erased the earlier null-controller clock prefix.");
            FalloutActorLogicalScene.Validate(failed.Capture());
        }
        finally { failed.Retire(); }
        var reentered = FloatExtraModel(InitialControllerSkeleton()).ConstructLogicalScene();
        try
        {
            var checks = 0;
            fixture.EnterLoad((load, guard) => Reject(() => reentered.UpdateInitialPlayerModel(load, actual =>
            {
                guard(actual);
                if (++checks == 2) { try { _ = reentered.Capture(); } catch (InvalidOperationException) { } }
            })));
            Require(reentered.Capture().InitialModelUpdate is { Returned: false, Controllers.Count: 0 },
                "Initial model update swallowed a rejected scene reentry.");
        }
        finally { reentered.Retire(); }
        Console.WriteLine("OPENNV_INITIAL_PLAYER_MODEL_CONTRACT_PASS nullAndSampled=true orderedFailure=true coldNoReplay=true reentryRejected=true");
    }

    private static FalloutNifFile InitialControllerSkeleton(bool unsupportedSecond = false, float stop = 3) =>
        FloatExtraNif(["Root", "Child"],
            ("NiNode", FloatExtraBytes(writer => Node(writer, 0, 2, [1]))),
            ("NiNode", FloatExtraBytes(writer => Node(writer, 1, 3, []))),
            ("NiTransformController", FloatExtraBytes(writer => Controller(writer, 0, -1, float.MaxValue, float.MinValue, false))),
            ("NiTransformController", FloatExtraBytes(writer => Controller(writer, 1, 4, 2, stop, unsupportedSecond))),
            ("NiTransformInterpolator", FloatExtraBytes(writer =>
            {
                foreach (var value in new float[] { 4, 5, 6, 1, 0, 0, 0, -2 }) writer.Write(value);
                writer.Write(-1);
            })));

    private static void Node(BinaryWriter writer, int name, int controller, int[] children)
    {
        writer.Write(name); writer.Write(0); writer.Write(controller); writer.Write(0U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0); writer.Write(-1); writer.Write(children.Length);
        foreach (var child in children) writer.Write(child);
        writer.Write(0);
    }
    private static void Controller(BinaryWriter writer, int target, int interpolator, float start, float stop, bool unsupported)
    {
        writer.Write(-1); writer.Write((ushort)(unsupported ? 72 : 76)); writer.Write(1f); writer.Write(0f);
        writer.Write(start); writer.Write(stop); writer.Write(target); writer.Write(interpolator);
    }
}
