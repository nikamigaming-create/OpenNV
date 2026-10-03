using System.Text.Json;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseManagedMorph()
    {
        var source = FalloutNifFile.Read(MorphFixture(managed: true));
        var prototype = new RuntimeNativeNifPrototype(source, 1);
        var first = prototype.Instantiate(); var second = prototype.Instantiate();
        try
        {
            var mesh = first.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var other = second.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var clock = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var cold = second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var arrays = ((ArrayMesh)mesh.Mesh).SurfaceGetArrays(0);
            if (arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()[0] != Vector3.Zero || ((ArrayMesh)mesh.Mesh).GetBlendShapeCount() != 2)
                throw new InvalidDataException("Managed morph lost its source base or relative targets.");
            clock.TextKeyHandler = cold.TextKeyHandler = _ => "audit-bound";
            clock.RequestSourceSequence("Forward", 1); clock._Process(.25);
            Check(mesh, .25f);
            if (other.GetBlendShapeValue(0) != 0 || other.GetBlendShapeValue(1) != 0)
                throw new InvalidDataException("Managed morph changed an independent instance.");
            var saved = JsonSerializer.Deserialize<OpenNV.Runtime.Content.FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(clock.CaptureScriptState()))!;
            cold.RestoreScriptState(saved); Check(other, .25f);
            clock._Process(.5); cold._Process(.5); Check(mesh, .75f); Check(other, .75f);
            Reject(() => RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(MorphFixture(true, missingTarget: true)), 1));
            Reject(() => RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(MorphFixture(true, wrongController: true)), 1));
            GD.Print("OPENNV_MANAGED_MORPH_PASS sourceBase=true relativeTargets=true sourceScalar=true sourceClock=true instancesIndependent=true coldPhase=true invalidBindingsRejected=true pixels=unverified");
        }
        finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }

        void Check(MeshInstance3D mesh, float time)
        {
            for (var index = 0; index < 2; index++)
                if (MathF.Abs(mesh.GetBlendShapeValue(index) - new FalloutNifFloatAnimation(source, 13 + index).Sample(time)) > .000001f)
                    throw new InvalidDataException("Managed morph scalar publication differs from the source channel.");
        }
        static void Reject(Func<RuntimeNativeNifScene> build)
        {
            try { var admitted = build(); admitted.Root.Free(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Managed morph admitted an undeclared target or controller.");
        }
    }
}
