using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

/// <summary>The complete source player keeps its head for world cameras and shadows.</summary>
internal static class NativePlayerSelfVisibility
{
    internal static GeometryInstance3D[] HeadGeometry(RuntimeNativeNpc actor) =>
        HeadGeometry(actor.Appearance.Models, actor.Parts);

    internal static GeometryInstance3D[] HeadGeometry(IReadOnlyList<FalloutNpcAppearancePart> models,
        IReadOnlyList<RuntimeNativeNifScene> parts)
    {
        if (models.Count != parts.Count)
            throw new InvalidDataException("Player appearance roles do not match its published source parts.");
        var result = new List<GeometryInstance3D>();
        for (var index = 0; index < parts.Count; index++)
        {
            var model = models[index];
            var root = parts[index].Root;
            if (!root.HasMeta("opennv_source_part") || root.GetMeta("opennv_source_part").AsString() != model.Role ||
                !root.HasMeta("opennv_source_form") || root.GetMeta("opennv_source_form").AsString() != model.Source.ToString() ||
                !root.HasMeta("opennv_source_model") || root.GetMeta("opennv_source_model").AsString() != model.ModelPath)
                throw new InvalidDataException("Player visibility has no exact published source appearance part.");
            if (!FalloutNpcAppearanceSelfView.RequireWholePartHead(model)) continue;
            result.AddRange(root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>());
            if (root is GeometryInstance3D geometry) result.Add(geometry);
        }
        return result.ToArray();
    }

    internal static void Apply(RuntimeNativeNpc actor)
    {
        // Resolve the whole source policy before changing any surface. A missing
        // role or combined partition cannot leave a partly masked body behind.
        var heads = HeadGeometry(actor);
        foreach (var mesh in heads) mesh.Layers = FalloutNpcAppearanceSelfView.SelfHeadLayer;
    }
}
