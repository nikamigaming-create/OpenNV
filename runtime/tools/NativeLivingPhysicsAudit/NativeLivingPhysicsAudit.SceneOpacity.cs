using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeLivingPhysicsAudit
{
    private static void ExerciseSceneOpacity()
    {
        ExerciseSceneOpacityRetirement();
        ExerciseSceneOpacityCallerRetirement();
        // Real native material instances may share a shader program. Borrowing
        // one material must preserve the other instance and the program itself.
        const string code = "shader_type spatial; uniform vec4 base_factor; void fragment() { ALBEDO = base_factor.rgb; ALPHA = base_factor.a; }";
        using var program = new Shader { Code = code };
        using var first = new ShaderMaterial { Shader = program, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        using var second = new ShaderMaterial { Shader = program, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        first.SetShaderParameter("base_factor", new Vector4(.2f, .3f, .4f, .375f));
        second.SetShaderParameter("base_factor", new Vector4(.7f, .8f, .9f, .625f));
        var firstConsumer = new NativeNifSceneAlphaMaterial(first); var secondConsumer = new NativeNifSceneAlphaMaterial(second);
        try
        {
            Reject(() => _ = new NativeNifSceneAlphaMaterial(first));
            firstConsumer.BorrowMaterialAlpha(0x3ec00000);
            firstConsumer.Borrow(0x3e800000);
            Require(first.Shader != program && first.GetShaderParameter(NativeNifSceneAlphaMaterial.Parameter).AsDouble() == .25 &&
                first.GetShaderParameter("base_factor").AsVector4() == new Vector4(.2f, .3f, .4f, .375f) &&
                second.Shader == program && second.GetShaderParameter("base_factor").AsVector4().W == .625f && program.Code == code,
                "Native fade changed shared programs, another actual material or independent material alpha.");
            var changed = firstConsumer.Stores; firstConsumer.Borrow(0x3e800000);
            Require(firstConsumer.Stores == changed, "Unchanged logical alpha repeated native renderer writes.");
            first.SetShaderParameter("base_factor", new Vector4(.2f, .3f, .4f, .5f));
            firstConsumer.BorrowMaterialAlpha(0x3f000000);
            firstConsumer.Borrow(0x80000000);
            Require(first.GetShaderParameter("base_factor").AsVector4().W == .5f &&
                BitConverter.SingleToUInt32Bits((float)first.GetShaderParameter(NativeNifSceneAlphaMaterial.Parameter).AsDouble()) == 0x80000000,
                "Native signed-zero fade replaced or clamped independent material animation alpha.");
            changed = firstConsumer.Stores;
            Reject(() => firstConsumer.Borrow(0x7fc00003));
            Require(firstConsumer.Stores == changed && firstConsumer.LastBits == 0x80000000,
                "Native unordered-alpha refusal erased its preceding actual renderer store.");
            firstConsumer.Borrow(0x3f800000);
            Require(first.Shader == program && first.GetShaderParameter("base_factor").AsVector4().W == .5f,
                "Logical one failed to restore the original shader or replayed material alpha.");
            firstConsumer.Retire();
            var fresh = new NativeNifSceneAlphaMaterial(first);
            try { fresh.Borrow(0x3f000000); Require(fresh.Stores == 1 && firstConsumer.Stores > 1, "Native retirement did not release a fresh actual borrower."); }
            finally { fresh.Retire(); }
            secondConsumer.Borrow(0x3f000000);
            Require(second.GetShaderParameter("base_factor").AsVector4().W == .625f && first.Shader == program,
                "A later independent borrower reused another material's native alpha state.");
            using var opaque = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(1.0); }" };
            using var opaqueMaterial = new ShaderMaterial { Shader = opaque, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
            var opaqueConsumer = new NativeNifSceneAlphaMaterial(opaqueMaterial);
            try
            {
                opaqueConsumer.Borrow(0x3f000000);
                Require(opaqueMaterial.Shader.Code.Contains("ALPHA = " + NativeNifSceneAlphaMaterial.Parameter, StringComparison.Ordinal),
                    "Opaque original material could not consume a genuine changing logical alpha field.");
            }
            finally { opaqueConsumer.Retire(); }
            GD.Print("OPENNV_NATIVE_SCENE_OPACITY_PASS actualShaderMaterials=true exclusiveBorrowers=true sharedProgramUnchanged=true " +
                "materialAlphaIndependent=true signedZero=true nonfinitePrefix=true retirementFresh=true finalPixels=UNVERIFIED");
        }
        finally { firstConsumer.Retire(); secondConsumer.Retire(); }
    }
    private static void ExerciseSceneOpacityRetirement()
    {
        using var program = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(1.0); }" };
        using var disposed = new ShaderMaterial { Shader = program, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        using var surviving = new ShaderMaterial { Shader = program, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        var deadBorrower = new NativeNifSceneAlphaMaterial(disposed); var liveBorrower = new NativeNifSceneAlphaMaterial(surviving);
        try
        {
            deadBorrower.Borrow(0x3f000000); liveBorrower.Borrow(0x3f000000);
            disposed.Dispose();
            var finalCleanup = false; AggregateException? failure = null;
            try
            {
                NativeNifSceneRetirement.Drain("authored-native-disposal-failure",
                    [() => deadBorrower.Borrow(0x3e800000), deadBorrower.Retire, liveBorrower.Retire, () => finalCleanup = true]);
            }
            catch (AggregateException error) { failure = error; }
            Require(failure?.InnerExceptions.Count == 1 && failure.InnerExceptions[0] is ObjectDisposedException && finalCleanup && surviving.Shader == program,
                "An actual disposed material failure stopped later native leases or final owner cleanup.");
            var fresh = new NativeNifSceneAlphaMaterial(surviving);
            try { fresh.Borrow(0x3e800000); Require(fresh.Stores == 1, "Failed drain retained the surviving actual material lease."); }
            finally { fresh.Retire(); }
            using var obsolete = new Shader { Code = program.Code };
            using var orphan = new ShaderMaterial { Shader = obsolete, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
            var orphanBorrower = new NativeNifSceneAlphaMaterial(orphan);
            try
            {
                orphanBorrower.Borrow(0x3f000000); obsolete.Dispose(); orphanBorrower.Retire();
                Require(orphan.Shader is null, "Disposed original shader left a retiring material pointing at the released fade program.");
            }
            finally { orphanBorrower.Retire(); }
        }
        finally { deadBorrower.Retire(); liveBorrower.Retire(); }
    }
}
