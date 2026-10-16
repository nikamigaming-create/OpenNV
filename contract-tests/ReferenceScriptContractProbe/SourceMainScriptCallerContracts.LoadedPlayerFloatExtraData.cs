using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLoadedPlayerFloatExtraDataContracts()
    {
        var model = FloatExtraModel(FloatExtraSkeleton()); var scene = model.ConstructLogicalScene();
        var file = FloatExtraSequence(); var sequence = file.ReadControllerSequence(file.Roots.Single());
        var resource = FloatExtraResource(file, sequence); var binding = new FalloutLoadedAnimationSequenceBinding(resource, scene);
        Require(binding.TransformChannelCount == 1 && binding.FloatExtraDataChannelCount == 1 &&
            binding.UnboundChannels.Count == 0 && binding.AbsentSourceTargets.Count == 0,
            "A declared float channel did not bind its actual logical field beside the source transform channel.");
        binding.ApplySourceTime(2);
        var saved = scene.Capture(); var scalar = saved.FloatExtraData.Single();
        Require(scene.ReadFloatExtraData(new(0, 0), "Channel") == 4 && scene.ReadFloatExtraData(new(0, 1), "Channel") == 4 &&
            scalar is { Stores: 1, ValueBits: 0x40800000 } && scalar.Changed < saved.Sequence &&
            saved.Objects.Single(node => node.Key.Block == 1).Local.Read().Translation == new FalloutNifVector3(1, 2, 3),
            "Current float sampling lost the actual quadratic time, shared block identity or original channel order.");
        binding.ApplySourceTime(3); saved = scene.Capture();
        Require(scene.ReadSkeletonFloatExtraData("Root", "Channel") == 6 &&
            scene.ReadSkeletonFloatExtraDataValues().Count == 2 && saved.FloatExtraData.Single().Stores == 2,
            "Skeleton readers did not borrow the same current logical source field.");
        var beforeBorrow = saved.Sequence;
        scene.BorrowRestoredSkeletonFloatExtraData("Root", "Channel", 6);
        Require(scene.Capture().Sequence == beforeBorrow && scene.Capture().FloatExtraData.Single().Stores == 2,
            "Native cold borrowing replayed a source float store.");
        Reject(() => scene.BorrowRestoredSkeletonFloatExtraData("Root", "Channel", 5));
        Reject(() => scene.BindFloatExtraDataAnimation(new(0, 1), file, sequence.ControlledBlocks[0]));
        Reject(() => scene.ReadFloatExtraData(new(1, 0), "Channel"));
        Reject(() => scene.ReadFloatExtraData(new(0, 0), "Other"));

        for (var cycle = 0; cycle < 3; cycle++)
        {
            saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!;
            var cold = FloatExtraModel(FloatExtraSkeleton()).ConstructLogicalScene(saved);
            Require(cold.Factory != saved.Factory && cold.Lifetime != saved.Lifetime &&
                cold.ReadFloatExtraData(new(0, 0), "Channel") == 6 && cold.ReadFloatExtraData(new(0, 1), "Channel") == 6 &&
                cold.Capture().FloatExtraData.Single().Stores == 2 && cold.Capture().FloatExtraData.Single().Changed == saved.FloatExtraData.Single().Changed,
                "Cold logical construction reset, split, replayed or replaced its shared current float field.");
            saved = cold.Capture(); cold.Retire();
        }
        Reject(() => FalloutActorLogicalScene.Validate(saved with { FloatExtraData = [] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { FloatExtraData = [saved.FloatExtraData[0], saved.FloatExtraData[0]] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { FloatExtraData = [saved.FloatExtraData[0] with { ValueBits = 0x7fc00001 }] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { FloatExtraData = [saved.FloatExtraData[0] with { Changed = saved.Sequence + 1 }] }));
        Reject(() => FalloutActorLogicalScene.Validate(saved with { Schema = "opennv-actor-logical-scene/v1" }));
        Reject(() => FloatExtraModel(FloatExtraSkeleton()).ConstructLogicalScene(saved with
        {
            FloatExtraData = [], Objects = saved.Objects.Select(node => node with { FloatExtraData = [] }).ToArray()
        }));
        Reject(() => FloatExtraModel(FloatExtraSkeleton()).ConstructLogicalScene(saved with
            { FloatExtraData = [saved.FloatExtraData[0] with { InitialBits = 0x3f000000 }] }));
        Reject(() => FloatExtraModel(FloatExtraSkeleton(controllerName: "Other")).ConstructLogicalScene(saved));
        Unbound(FloatExtraSkeleton(rootController: false), sequence.ControlledBlocks[0]);
        Unbound(FloatExtraSkeleton(controllerTarget: 1), sequence.ControlledBlocks[0]);
        Unbound(FloatExtraSkeleton(controllerName: "Other"), sequence.ControlledBlocks[0]);
        Unbound(FloatExtraSkeleton(), sequence.ControlledBlocks[0] with { PropertyType = "NiMaterialProperty" });
        Unbound(FloatExtraSkeleton(), sequence.ControlledBlocks[0] with { Variable2 = "unexpected" });
        Unbound(FloatExtraSkeleton(), sequence.ControlledBlocks[0] with { Variable1 = "missing" });
        Unbound(FloatExtraSkeleton(), sequence.ControlledBlocks[0] with { Interpolator = 3 });
        Unbound(FloatExtraSkeleton(), sequence.ControlledBlocks[0] with { ControllerType = "NiVisController" });
        var duplicateScene = FloatExtraModel(FloatExtraSkeleton()).ConstructLogicalScene();
        var duplicate = sequence with { ControlledBlocks = [sequence.ControlledBlocks[0], sequence.ControlledBlocks[0] with { NodeName = "Branch" }] };
        var duplicateBinding = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file, duplicate), duplicateScene);
        Require(duplicateBinding.UnboundChannels.Count == 1 && duplicateScene.Capture().FloatExtraData.Single().Stores == 0,
            "Two node references to the same float object bypassed its real blend requirement.");
        Reject(() => duplicateBinding.ApplySourceTime(2)); duplicateScene.Retire(); scene.Retire();
        Reject(() => binding.ApplySourceTime(2));
        var prefixScene = FloatExtraModel(FloatExtraSkeleton()).ConstructLogicalScene();
        var failingFile = FloatExtraSequence(zeroScale: true); var failingSequence = failingFile.ReadControllerSequence(failingFile.Roots.Single());
        var failingBinding = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(failingFile, failingSequence), prefixScene);
        Require(failingBinding.UnboundChannels.Count == 0, "The authored runtime failure did not enter its real sampled channel.");
        Reject(() => failingBinding.ApplySourceTime(2));
        Require(prefixScene.Capture().FloatExtraData.Single() is { Stores: 1, ValueBits: 0x40800000 } &&
            prefixScene.Capture().Objects.Single(node => node.Key.Block == 1).PoseStores == 0,
            "A later sampled transform failure erased its preceding ordered float store or returned the invalid transform.");
        prefixScene.Retire();
        Console.WriteLine("OPENNV_LOADED_PLAYER_FLOAT_EXTRA_CONTRACT_PASS sharedLogicalField=true originalTimeAndOrder=true exactController=true cold3=true borrowedNativeRestore=true wrongOwnerRefused=true gameplay=UNEXECUTED");

        void Unbound(FalloutNifFile skeleton, FalloutNifControllerLink link)
        {
            var failedScene = FloatExtraModel(skeleton).ConstructLogicalScene();
            var failed = new FalloutLoadedAnimationSequenceBinding(FloatExtraResource(file,
                sequence with { ControlledBlocks = [link] }), failedScene);
            Require(failed.UnboundChannels.Count == 1 && failedScene.Capture().FloatExtraData.Single().Stores == 0,
                "Missing, foreign or unsupported float source ownership was silently admitted.");
            Reject(() => failed.ApplySourceTime(2));
            Require(failedScene.Capture().FloatExtraData.Single().Stores == 0, "An unbound float failure mutated the logical field.");
            failedScene.Retire();
        }
    }

    private static FalloutActorSourceModel FloatExtraModel(FalloutNifFile file, string path = "meshes/authored/float-extra-skeleton.nif")
    {
        var key = new FalloutFormKey("FloatExtraFixture.esm", 7); var face = new FalloutNpcFaceGen(key, [], [], []);
        var appearance = new FalloutNpcAppearance(key, null, key, key, key, false, new("FloatExtraFixture.esm", 8),
            path, 1, 1, [], [], [], [], face, face, [], [], [], [], [], [], null, null);
        return new(appearance, FalloutNifPhysicalModel.Read(file), []);
    }
    private static FalloutLoadedAnimationResource FloatExtraResource(FalloutNifFile file, FalloutNifControllerSequence sequence) =>
        new(new("authored-pure-source-channel-audit", Guid.NewGuid(), "meshes/authored/idle.kf", file.Sha256,
            0, sequence.Block.Index, sequence.Name, 0, "authored-original-NIF-reader"), file, sequence);

    private static FalloutNifFile FloatExtraSkeleton(bool rootController = true, int controllerTarget = 0, string controllerName = "Channel")
    {
        string[] names = ["Root", "Branch", "Channel", "Other"];
        return FloatExtraNif(names,
            ("NiNode", FloatExtraBytes(writer => Node(writer, 0, rootController ? 3 : -1, [1]))),
            ("NiNode", FloatExtraBytes(writer => Node(writer, 1, 4, []))),
            ("NiFloatExtraData", FloatExtraBytes(writer => { writer.Write(2); writer.Write(.25f); })),
            ("NiFloatExtraDataController", FloatExtraBytes(writer => Controller(writer, controllerTarget, controllerName == "Channel" ? 2 : 3))),
            ("NiFloatExtraDataController", FloatExtraBytes(writer => Controller(writer, 1, 2))));
        static void Node(BinaryWriter writer, int name, int controller, int[] children)
        {
            writer.Write(name); writer.Write(1); writer.Write(2); writer.Write(controller); writer.Write(0U);
            writer.Write(0f); writer.Write(0f); writer.Write(0f);
            foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) writer.Write(value);
            writer.Write(1f); writer.Write(0); writer.Write(-1); writer.Write(children.Length);
            foreach (var child in children) writer.Write(child);
            writer.Write(0);
        }
        static void Controller(BinaryWriter writer, int target, int name)
        {
            writer.Write(-1); writer.Write((ushort)76); writer.Write(1f); writer.Write(0f);
            writer.Write(float.MaxValue); writer.Write(float.MinValue); writer.Write(target); writer.Write(-1); writer.Write(name);
        }
    }
    private static FalloutNifFile FloatExtraSequence(bool zeroScale = false) => FloatExtraNif(
        ["Idle", "Root", "Branch", "Channel", "NiFloatExtraDataController", "NiTransformController"],
        ("NiControllerSequence", FloatExtraBytes(writer =>
        {
            writer.Write(0); writer.Write(2); writer.Write(0U);
            Link(1, 1, 4, 3); Link(3, 2, 5, -1);
            writer.Write(1f); writer.Write(-1); writer.Write(0U); writer.Write(1f); writer.Write(1f); writer.Write(3f);
            writer.Write(-1); writer.Write(1); writer.Write((ushort)0);
            void Link(int interpolator, int node, int controller, int variable)
            {
                writer.Write(interpolator); writer.Write(-1); writer.Write((byte)10);
                writer.Write(node); writer.Write(-1); writer.Write(controller); writer.Write(variable); writer.Write(-1);
            }
        })),
        ("NiFloatInterpolator", FloatExtraBytes(writer => { writer.Write(float.MinValue); writer.Write(2); })),
        ("NiFloatData", FloatExtraBytes(writer =>
        {
            writer.Write(2); writer.Write(2U);
            foreach (var (time, value) in new[] { (1f, 2f), (3f, 6f) })
            { writer.Write(time); writer.Write(value); writer.Write(0f); writer.Write(0f); }
        })),
        ("NiTransformInterpolator", FloatExtraBytes(writer =>
        {
            writer.Write(1f); writer.Write(2f); writer.Write(3f);
            for (var index = 0; index < 4; index++) writer.Write(float.MinValue);
            writer.Write(zeroScale ? 0f : float.MinValue);
            writer.Write(-1);
        })));

    private static byte[] FloatExtraBytes(Action<BinaryWriter> emit)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); emit(writer); return stream.ToArray(); }
    private static FalloutNifFile FloatExtraNif(string[] names, params (string Type, byte[] Data)[] blocks) =>
        FalloutNifFile.Read(FloatExtraBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length); foreach (var block in blocks) Text(block.Type);
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Data.Length);
            writer.Write(names.Length); writer.Write(names.Max(value => value.Length)); foreach (var name in names) Text(name);
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Data); writer.Write(1U); writer.Write(0);
            void Text(string text) { writer.Write(text.Length); writer.Write(Encoding.ASCII.GetBytes(text)); }
        }));
}
