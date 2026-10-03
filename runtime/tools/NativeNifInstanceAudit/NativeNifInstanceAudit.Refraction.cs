using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseDirectRefraction()
    {
        var file = FalloutNifFile.Read(SurfaceFixture(0x82018000, refractionFlags: 0x4c));
        var property = (FalloutNifShaderProperty)file.ReadObject(3);
        var first = Material(); var second = Material();
        var firstClock = new RuntimeNifControllerPlayer(); var secondClock = new RuntimeNifControllerPlayer();
        try
        {
            firstClock.Configure([NativeNifRefractionMaterial.DirectChannel(file, property, [first])]);
            secondClock.Configure([NativeNifRefractionMaterial.DirectChannel(file, property, [second])]);
            firstClock.PlaySourceSequence(firstClock.SequenceNames.Single());
            secondClock.PlaySourceSequence(secondClock.SequenceNames.Single());
            Check(first, .15f, .25f); Check(second, .15f, .25f);
            firstClock._Process(.125); Check(first, .2f, .5f); Check(second, .15f, .25f);
            firstClock._Process(2); Check(first, .3f, 1);
            var invalidTarget = FalloutNifFile.Read(SurfaceFixture(0x82018000, refractionFlags: 0x4c, refractionTarget: 1));
            Reject(invalidTarget);
            Reject(FalloutNifFile.Read(SurfaceFixture(0x82018000, refractionFlags: 0x6c)));
            Reject(FalloutNifFile.Read(SurfaceFixture(0x82018000, refractionFlags: 0x5c)));
            GD.Print("OPENNV_DIRECT_REFRACTION_CONTROLLER_PASS sourceClock=true phase=true frequency=true clamp=true instancesIndependent=true invalidOwnersRejected=true distortionPixels=unverified");
        }
        finally { firstClock.Free(); secondClock.Free(); first.Dispose(); second.Dispose(); }

        void Reject(FalloutNifFile rejected)
        {
            try
            {
                _ = NativeNifRefractionMaterial.DirectChannel(rejected, (FalloutNifShaderProperty)rejected.ReadObject(3), [first]);
                throw new InvalidDataException("Invalid source refraction controller acquired a direct clock.");
            }
            catch (NotSupportedException) { }
        }
        static void Check(ShaderMaterial material, float strength, float time)
        {
            if (Math.Abs(material.GetShaderParameter("source_strength").AsSingle() - strength) > .000001f ||
                Math.Abs(material.GetShaderParameter("source_time").AsSingle() - time) > .000001f)
                throw new InvalidDataException("Refraction lost the source scalar value or controller time.");
        }
        static ShaderMaterial Material() => new()
        {
            ResourceName = NativeNifRefractionMaterial.ResourceIdentity,
            Shader = new Shader { Code = "shader_type spatial; uniform float source_strength; uniform float source_time;" }
        };
    }
}
