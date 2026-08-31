using System.Text;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

/// <summary>
/// Shared clean-room mapping from Fallout's encoded SLS lighting inputs to
/// Godot's light nodes and custom spatial-light processor.
/// </summary>
internal static class RetailLighting
{
    internal const float GodotOmniDecayForRetailRemap = 0.0f;

    internal static float PointShaderRadius(float authoredRadius) =>
        authoredRadius * Mathf.Sqrt(2.0f);

    internal static Vector3 SurfaceToLightFromXcllDegrees(
        float rotationYDegrees,
        float rotationZDegrees)
    {
        if (!float.IsFinite(rotationYDegrees) || !float.IsFinite(rotationZDegrees))
            throw new InvalidOperationException(
                "Retail XCLL directional rotations must be finite.");
        var rotationY = Mathf.DegToRad(rotationYDegrees);
        var rotationZ = Mathf.DegToRad(rotationZDegrees);
        var gamebryo = new Vector3(
            Mathf.Sin(rotationY) * Mathf.Cos(rotationZ),
            Mathf.Sin(rotationY) * Mathf.Sin(rotationZ),
            Mathf.Cos(rotationY));
        return GamebryoCoordinate.ConvertVector(gamebryo).Normalized();
    }

    internal static Basis DirectionalLightBasis(Vector3 surfaceToLight)
    {
        var lightAxis = surfaceToLight.Normalized();
        if (!lightAxis.IsFinite() || lightAxis == Vector3.Zero)
            throw new InvalidOperationException(
                "Retail surface-to-light vector is not finite and nonzero.");
        // DirectionalLight3D uses positive local Z as its surface-to-light
        // vector. Choose the zero-roll basis used by the retail conversion.
        var right = Vector3.Up.Cross(lightAxis);
        if (right == Vector3.Zero)
            right = Vector3.Right.Cross(lightAxis);
        right = right.Normalized();
        var up = lightAxis.Cross(right).Normalized();
        return new Basis(right, up, lightAxis);
    }

    internal static void AppendDiffuseLightFunction(StringBuilder source)
    {
        source.AppendLine("void light() {");
        source.AppendLine("    float retail_attenuation = ATTENUATION;");
        source.AppendLine("    if (!LIGHT_IS_DIRECTIONAL) {");
        source.AppendLine(
            "        float godot_edge_root = sqrt(clamp(ATTENUATION, 0.0, 1.0));");
        source.AppendLine(
            "        float normalized_distance_squared = sqrt(clamp(1.0 - godot_edge_root, 0.0, 1.0));");
        source.AppendLine(
            "        retail_attenuation = 1.0 - normalized_distance_squared;");
        source.AppendLine("    }");
        source.AppendLine(
            "    DIFFUSE_LIGHT += LIGHT_COLOR * max(dot(NORMAL, LIGHT), 0.0) * retail_attenuation / PI;");
        source.AppendLine("}");
    }
}
