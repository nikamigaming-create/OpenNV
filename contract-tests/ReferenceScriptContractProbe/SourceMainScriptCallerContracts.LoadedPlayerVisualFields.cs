using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLoadedPlayerVisualFieldContracts()
    {
        var model = FloatExtraModel(VisualFieldSkeleton()); var scene = model.ConstructLogicalScene();
        var file = VisualFieldSequence(); var sequence = file.ReadControllerSequence(0);
        var binding = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file, sequence), scene);
        Require(binding is { VisibilityChannelCount: 1, MorphChannelCount: 2, TransformChannelCount: 1 } &&
            binding.UnboundChannels.Count == 0, "Declared visual channels did not bind their same logical source fields.");
        var before = scene.Capture(); var otherFlags = before.Objects.Single(node => node.Key.Block == 1).Flags & ~1U;
        Require(scene.ReadVisibility(new(0, 1)) && scene.ReadMorphWeight(new(0, 2), "Base") == 1 &&
            scene.ReadMorphWeight(new(0, 2), "Flex") == .25f,
            "Visual field construction changed the source culling flag or ordered relative morph defaults.");
        binding.ApplySourceTime(2); scene.UpdateWorldAndBounds(); var saved = scene.Capture();
        var visibility = saved.Visibility.Single(field => field.Target.Block == 1); var morph = saved.Morphs.Single();
        Require(!scene.ReadVisibility(new(0, 1)) && scene.ReadMorphWeight(new(0, 2), "Flex") == 4 &&
            scene.ReadMorphWeight(new(0, 2), "Base") == 1 && visibility.Stores == 1 &&
            saved.Objects.Single(node => node.Key.Block == 1).Flags == (otherFlags | 1) &&
            visibility.Changed < morph.Weights[1].Changed && morph.Weights[1].Changed < morph.Weights[0].Changed &&
            saved.Objects.Single(node => node.Key.Block == 1).Local.Read().Translation == new FalloutNifVector3(1, 2, 3),
            "Visual sampling lost exact step/quadratic time, independent base weight, other flags or source channel order.");
        binding.ApplySourceTime(3); scene.UpdateWorldAndBounds(); saved = scene.Capture();
        Require(scene.ReadVisibility(new(0, 1)) && scene.ReadMorphWeight(new(0, 2), "Flex") == 6 &&
            scene.ReadSkeletonMorphWeights().Single(weight => weight.Name == "Flex").Weight == 6,
            "Current morph/visibility consumers borrowed stale presentation values.");
        var sourceField = scene.RequireMorphField(new(0, 2)); var oldSequence = saved.Sequence;
        Reject(() => scene.ReadMorphWeight(new(1, 2), "Flex"));
        Reject(() => scene.ReadMorphWeight(new(0, 2), "Absent"));
        Reject(() => binding.ApplySourceTime(float.NaN));
        Reject(() => Task.Run(() => binding.ApplySourceTime(2)).GetAwaiter().GetResult());
        Require(scene.Capture().Sequence == oldSequence && sourceField.Weights[1].Value == 6,
            "A wrong thread, source field or invalid time changed the current logical fields.");
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!;
            var cold = FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene(saved);
            var after = cold.Capture(); FalloutActorLogicalScene.RequireColdRestoration(saved, after);
            Require(cold.ReadVisibility(new(0, 1)) && cold.ReadMorphWeight(new(0, 2), "Flex") == 6 &&
                after.Visibility.Single(field => field.Target.Block == 1).Stores == 2 &&
                after.Morphs.Single().Weights.All(weight => weight.Stores == 2),
                "Cold visual fields reset, replayed or lost their current source values/history.");
            saved = after; cold.Retire();
        }
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Visibility = [] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Morphs = [saved.Morphs[0], saved.Morphs[0]] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with
        { Visibility = saved.Visibility.Select(field => field.Target.Block == 1 ? field with { Changed = saved.Sequence + 1 } : field).ToArray() }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Morphs = [saved.Morphs[0] with
        { Weights = saved.Morphs[0].Weights.Select(weight => weight.Index == 1 ? weight with { ValueBits = 0x7fc00001 } : weight).ToArray() }] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Morphs = [saved.Morphs[0] with
        { Weights = saved.Morphs[0].Weights.Select(weight => weight.Index == 0 ? weight with { ValueBits = 0x40000000 } : weight).ToArray() }] }));
        Reject(() => FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene(saved with
        { Morphs = [saved.Morphs[0] with { Controller = new(0, 4) }] }));
        Reject(() => FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene(saved with
        { Morphs = [saved.Morphs[0] with { Data = new(0, 3) }] }));
        Reject(() => FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene(saved with
        { Morphs = [saved.Morphs[0] with { Weights = saved.Morphs[0].Weights.Select(weight => weight.Index == 1 ? weight with { Name = "Other" } : weight).ToArray() }] }));
        Reject(() => FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene(saved with
        { Visibility = saved.Visibility.Select(field => field.Target.Block == 1 ? field with { InitialVisible = false } : field).ToArray() }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Schema = "opennv-actor-logical-scene/v2" }));

        Unbound(VisualFieldSkeleton(visibilityController: false), sequence.ControlledBlocks[0]);
        Unbound(VisualFieldSkeleton(visibilityTarget: 0), sequence.ControlledBlocks[0]);
        Unbound(VisualFieldSkeleton(), sequence.ControlledBlocks[0] with { Interpolator = 3 });
        Unbound(VisualFieldSkeleton(), sequence.ControlledBlocks[0] with { Variable2 = "Unexpected" });
        Unbound(VisualFieldSkeleton(), sequence.ControlledBlocks[1] with { Variable2 = "Absent" });
        Unbound(VisualFieldSkeleton(), sequence.ControlledBlocks[1] with { Variable1 = "Unexpected" });
        Unbound(VisualFieldSkeleton(duplicateNames: true), sequence.ControlledBlocks[1]);
        Reject(() => FloatExtraModel(VisualFieldSkeleton(morphTarget: 1)).ConstructLogicalScene());
        var duplicateScene = FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene();
        var duplicate = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file,
            sequence with { ControlledBlocks = [sequence.ControlledBlocks[0], sequence.ControlledBlocks[0]] }), duplicateScene);
        Require(duplicate.UnboundChannels.Count == 1, "Two visibility links silently bypassed the real blend requirement.");
        Reject(() => duplicate.ApplySourceTime(2));
        Require(duplicateScene.Capture().Visibility.All(field => field.Stores == 0), "Unresolved blend changed source visibility.");
        duplicateScene.Retire();

        var prefixScene = FloatExtraModel(VisualFieldSkeleton()).ConstructLogicalScene();
        var failedFile = VisualFieldSequence(zeroScale: true);
        var failed = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(failedFile, failedFile.ReadControllerSequence(0)), prefixScene);
        Require(failed.UnboundChannels.Count == 0, "Authored failure never reached actual sampled visual stores.");
        Reject(() => failed.ApplySourceTime(2)); var prefix = prefixScene.Capture();
        Require(!prefixScene.ReadVisibility(new(0, 1)) && prefixScene.ReadMorphWeight(new(0, 2), "Flex") == 4 &&
            prefix.Morphs.Single().Weights.All(weight => weight.Stores == 1) &&
            prefix.Objects.Single(node => node.Key.Block == 1).PoseStores == 0,
            "A later transform failure erased earlier visibility/morph stores or returned an invalid transform.");
        prefixScene.Retire(); scene.Retire(); Reject(() => binding.ApplySourceTime(2));
        Console.WriteLine("OPENNV_LOADED_PLAYER_VISUAL_FIELDS_PASS sourceFlags=true orderedMorphs=true originalTime=true cold3=true failedPrefix=true wrongOwnerRefused=true gameplay=UNEXECUTED");

        void Unbound(FalloutNifFile source, FalloutNifControllerLink link)
        {
            var target = FloatExtraModel(source).ConstructLogicalScene();
            var unbound = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file,
                sequence with { ControlledBlocks = [link] }), target);
            Require(unbound.UnboundChannels.Count == 1, "Visual channel admitted a missing/foreign/ambiguous source owner.");
            Reject(() => unbound.ApplySourceTime(2));
            Require(target.Capture().Visibility.All(field => field.Stores == 0) && target.Capture().Morphs.All(field => field.Weights.All(weight => weight.Stores == 0)),
                "Refused visual binding entered a source store.");
            target.Retire();
        }
    }

    private static FalloutNifFile VisualFieldSkeleton(bool visibilityController = true, int visibilityTarget = 1,
        int morphTarget = 2, bool duplicateNames = false)
    {
        string[] names = ["Root", "Pivot", "Surface", "Base", "Flex"];
        return FloatExtraNif(names,
            ("NiNode", FloatExtraBytes(w => { Av(w, 0, -1); Refs(w, [1]); Refs(w, []); })),
            ("NiNode", FloatExtraBytes(w => { Av(w, 1, visibilityController ? 4 : -1); Refs(w, [2]); Refs(w, []); })),
            ("NiTriShape", FloatExtraBytes(w => { Av(w, 2, 6); w.Write(3); w.Write(-1); w.Write(0U); w.Write(-1); w.Write(false); })),
            ("NiTriShapeData", FloatExtraBytes(w =>
            {
                w.Write(0); w.Write((ushort)3); w.Write((byte)0); w.Write((byte)0); w.Write(true);
                foreach (var value in new float[] { -1, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(value);
                w.Write((byte)0); w.Write((byte)0); w.Write(false);
                w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(false);
                w.Write((ushort)0); w.Write(-1); w.Write((ushort)1); w.Write(3U); w.Write(true);
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2); w.Write((ushort)0);
            })),
            ("NiTransformController", FloatExtraBytes(w => { Time(w, 1, 5); w.Write(-1); })),
            ("NiVisController", FloatExtraBytes(w => { Time(w, visibilityTarget, -1); w.Write(9); })),
            ("NiTransformController", FloatExtraBytes(w => { Time(w, 2, 7); w.Write(-1); })),
            ("NiGeomMorpherController", FloatExtraBytes(w =>
            {
                Time(w, morphTarget, -1); w.Write((ushort)0); w.Write(8); w.Write((byte)0); w.Write(duplicateNames ? 3 : 2);
                w.Write(-1); w.Write(0f); w.Write(-1); w.Write(.25f);
                if (duplicateNames) { w.Write(-1); w.Write(.5f); }
            })),
            ("NiMorphData", FloatExtraBytes(w =>
            {
                w.Write(duplicateNames ? 3 : 2); w.Write(3); w.Write((byte)1);
                for (var index = 0; index < (duplicateNames ? 3 : 2); ++index)
                {
                    w.Write(index == 0 ? 3 : 4);
                    foreach (var value in index == 0 ? new float[] { -1, 0, 0, 1, 0, 0, 0, 1, 0 } : new float[] { 0, 0, .2f, 0, 0, .2f, 0, 0, .2f }) w.Write(value);
                }
            })),
            ("NiBoolInterpolator", FloatExtraBytes(w => { w.Write((byte)1); w.Write(-1); })));
        static void Av(BinaryWriter w, int name, int controller)
        {
            w.Write(name); Refs(w, []); w.Write(controller); w.Write(0U);
            w.Write(0f); w.Write(0f); w.Write(0f);
            foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(value);
            w.Write(1f); Refs(w, []); w.Write(-1);
        }
        static void Refs(BinaryWriter w, int[] values) { w.Write(values.Length); foreach (var value in values) w.Write(value); }
        static void Time(BinaryWriter w, int target, int next)
        { w.Write(next); w.Write((ushort)76); w.Write(1f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write(target); }
    }
    private static FalloutNifFile VisualFieldSequence(bool zeroScale = false) => FloatExtraNif(
        ["Idle", "Pivot", "Surface", "NiVisController", "NiGeomMorpherController", "NiTransformController", "Base", "Flex"],
        ("NiControllerSequence", FloatExtraBytes(w =>
        {
            w.Write(0); w.Write(4); w.Write(0U);
            Link(1, 1, 3, -1); Link(3, 2, 4, 7); Link(5, 2, 4, 6); Link(6, 1, 5, -1);
            w.Write(1f); w.Write(-1); w.Write(0U); w.Write(1f); w.Write(1f); w.Write(3f); w.Write(-1); w.Write(1); w.Write((ushort)0);
            void Link(int interpolator, int node, int controller, int target)
            { w.Write(interpolator); w.Write(-1); w.Write((byte)10); w.Write(node); w.Write(-1); w.Write(controller); w.Write(-1); w.Write(target); }
        })),
        ("NiBoolInterpolator", FloatExtraBytes(w => { w.Write((byte)2); w.Write(2); })),
        ("NiBoolData", FloatExtraBytes(w => { w.Write(2); w.Write(5U); w.Write(1f); w.Write(false); w.Write(3f); w.Write(true); })),
        ("NiFloatInterpolator", FloatExtraBytes(w => { w.Write(float.MinValue); w.Write(4); })),
        ("NiFloatData", FloatExtraBytes(w =>
        {
            w.Write(2); w.Write(2U);
            foreach (var (time, value) in new[] { (1f, 2f), (3f, 6f) }) { w.Write(time); w.Write(value); w.Write(0f); w.Write(0f); }
        })),
        ("NiFloatInterpolator", FloatExtraBytes(w => { w.Write(-20f); w.Write(-1); })),
        ("NiTransformInterpolator", FloatExtraBytes(w =>
        {
            w.Write(1f); w.Write(2f); w.Write(3f); for (var index = 0; index < 4; ++index) w.Write(float.MinValue);
            w.Write(zeroScale ? 0f : float.MinValue); w.Write(-1);
        })));
}
