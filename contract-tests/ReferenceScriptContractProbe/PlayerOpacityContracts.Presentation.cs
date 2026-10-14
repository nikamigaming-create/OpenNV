using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class PlayerOpacityContracts
{
    private static void PresentationShaderContracts()
    {
        const string opaque = "shader_type spatial; uniform vec4 base_factor; void fragment() { ALBEDO = base_factor.rgb; } void light() { DIFFUSE_LIGHT += vec3(1.0); }";
        var fade = NativeNifSceneAlphaMaterial.FadeCode(opaque);
        Require(fade.Contains("uniform float " + NativeNifSceneAlphaMaterial.Parameter + " = 1.0;", StringComparison.Ordinal) &&
            fade.Contains("ALPHA = " + NativeNifSceneAlphaMaterial.Parameter, StringComparison.Ordinal) &&
            fade.EndsWith("void light() { DIFFUSE_LIGHT += vec3(1.0); }", StringComparison.Ordinal),
            "Logical opaque-alpha consumer changed another shader stage or omitted the retained source cell.");
        const string blend = "shader_type spatial; void fragment() { if (true) { ALPHA = base_factor.a; } }";
        Require(NativeNifSceneAlphaMaterial.FadeCode(blend).Contains("ALPHA *= " + NativeNifSceneAlphaMaterial.Parameter, StringComparison.Ordinal),
            "Logical fade replaced the original material coverage instead of consuming it separately.");
        Reject(() => NativeNifSceneAlphaMaterial.FadeCode("shader_type spatial; void fragment() {"));
        Reject(() => NativeNifSceneAlphaMaterial.FadeCode(fade));
    }
}
