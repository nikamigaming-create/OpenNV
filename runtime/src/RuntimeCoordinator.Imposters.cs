using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void EnterNativeImposterCell(RuntimeNativeReferencePresentation presentation, Node3D root,
        FalloutCellScene scene, IReadOnlyList<float>? position)
    {
        _ = root;
        if ((_options.ContainsKey("post-screenshots") || _options.ContainsKey("development-gallery")) &&
            FalloutPlayerRegionMembership.Enter(_nativePluginStack!, scene.Cell, position).Current.Any(region =>
                FalloutRegionImposters.Read(_nativePluginStack!, region)?.References.Count > 0))
            throw new NotSupportedException("Camera-only diagnostics have no authoritative player CELL-attachment owner for active regional imposters.");
        presentation.EnterImposterCell(_nativePluginStack ??
            throw new InvalidOperationException("Region entry has no source stack."), scene.Cell, position);
    }
}
