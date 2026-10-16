using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseLogicalVisualFields()
    {
        var source = FalloutNifFile.Read(MorphFixture());
        var key = new FalloutFormKey("LogicalVisualFixture.esm", 7); var face = new FalloutNpcFaceGen(key, [], [], []);
        var appearance = new FalloutNpcAppearance(key, null, key, key, key, false, new("LogicalVisualFixture.esm", 8),
            "meshes/authored/logical-visual-skeleton.nif", 1, 1, [], [], [], [], face, face, [], [], [], [], [], [], null, null);
        var model = new FalloutActorSourceModel(appearance, FalloutNifPhysicalModel.Read(source), []);
        var scene = model.ConstructLogicalScene();
        try
        {
            var animation = FalloutNifFile.Read(MorphFixture(managed: true));
            var link = new FalloutNifControllerLink("SyntheticFace", "", "NiGeomMorpherController", "", "ExpressionA", 13, -1, 0);
            var skeleton = new RuntimeNativeNifSkeleton(model.Skeleton, 1);
            try
            {
                AddChild(skeleton.Node);
                // A real clip may exist before the source logical graph publishes.
                // Its retained callback must borrow that graph when later invoked.
                var retained = skeleton.BindVisualChannel(animation, link) ?? throw new InvalidDataException("Authored morph did not bind its actual source geometry.");
                skeleton.BindLogicalScene(scene);
                retained(.5f);
                var mesh = skeleton.Node.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
                var attachment = (BoneAttachment3D)mesh.GetParent();
                if (scene.ReadMorphWeight(new(0, 1), "ExpressionA") != .375f || mesh.GetBlendShapeValue(0) != .375f)
                    throw new InvalidDataException("A prebound native clip wrote a parallel morph field after logical publication.");
                var saved = JsonSerializer.Serialize(scene.Capture());
                mesh.SetBlendShapeValue(0, -100); attachment.Visible = false;
                skeleton.BorrowCurrentSourcePoses();
                if (mesh.GetBlendShapeValue(0) != .375f || !attachment.Visible ||
                    skeleton.NamedMorphWeights.Single(weight => weight.Name == "ExpressionA").Weight != .375f)
                    throw new InvalidDataException("Native visual projection borrowed presentation caches instead of current C# fields.");
                skeleton.ResetMorphPublication();
                if (mesh.GetBlendShapeValue(0) != .375f || JsonSerializer.Serialize(scene.Capture()) != saved)
                    throw new InvalidDataException("Visual projection reset or replayed an authoritative source field.");
                var sourceApply = scene.BindMorphAnimation(new(0, 1), animation, link);
                sourceApply(1); skeleton.BorrowCurrentSourceVisualFields();
                if (mesh.GetBlendShapeValue(0) != .75f || scene.ReadMorphWeight(new(0, 1), "ExpressionA") != .75f)
                    throw new InvalidDataException("Bound native morph borrower retained a stale logical scalar.");
            }
            finally { skeleton.Node.Free(); }
            var state = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(scene.Capture()))!;
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                var coldModel = model.Instantiate(); var cold = coldModel.ConstructLogicalScene(state);
                var view = new RuntimeNativeNifSkeleton(coldModel.Skeleton, 1);
                try
                {
                    AddChild(view.Node); view.BindLogicalScene(cold); view.BorrowRestoredSourcePoses();
                    var mesh = view.Node.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
                    if (mesh.GetBlendShapeValue(0) != .75f || !((BoneAttachment3D)mesh.GetParent()).Visible)
                        throw new InvalidDataException("Cold native visual borrowing lost current C# fields.");
                    var after = cold.Capture(); FalloutActorLogicalScene.RequireColdRestoration(state, after);
                    state = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(after))!;
                    cold.Retire();
                    try
                    {
                        view.BorrowCurrentSourceVisualFields();
                        throw new InvalidDataException("Native visual borrower accepted a retired logical scene.");
                    }
                    catch (ObjectDisposedException) { }
                }
                finally { view.Node.Free(); cold.Retire(); }
            }
            GD.Print("OPENNV_LOGICAL_VISUAL_PROJECTION_PASS actualMeshes=true sourceUnchanged=true preboundClip=true currentMorph=true visibility=true cold3=true gameplay=UNEXECUTED");
        }
        finally { scene.Retire(); }
    }
}
