using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeLandscapeTransport ConstructNativeSourceTerrain(Node3D root, FalloutFormKey cell,
        FalloutLandscapeTransport source)
    {
        BindActualNativeQueuedCallerThread();
        var owner = RequireNativeSourceCellAttachment(root);
        RuntimeNativeLandscapeTransport? land = null;
        RuntimeNativeLandscapeConstruction? construction = null;
        try
        {
            land = RuntimeNativeLandscapeTransportBuilder.Build(source, _configuration.World.GameUnitsToMeters, _nativeLandscapeTextures,
                actual =>
                {
                    construction = actual;
                    if (actual.Phase == RuntimeNativeLandscapeConstructionPhase.Entered) owner.RegisterTerrainConstruction(actual);
                });
            owner.EnterTerrainConstructionPublication(land.Construction);
            root.AddChild(land); owner.NativeTerrainReturned(cell); return land;
        }
        catch (Exception original)
        {
            var first = construction?.OriginalFailure ?? original;
            construction?.MarkCallerFailure(first);
            var failures = new List<Exception> { original };
            try
            {
                owner.NativeTerrainFailed(cell, first, construction?.StillOwnedNodes ??
                (land is not null && GodotObject.IsInstanceValid(land) ? [land.GetInstanceId()] : []));
            }
            catch (Exception error) { failures.Add(error); }
            // A returned factory value with no parent is still this invocation's
            // actual allocation. Root-owned values retire through the CELL owner.
            if (land is not null && GodotObject.IsInstanceValid(land) && land.GetParent() is null)
                try { land.Free(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1) throw new AggregateException("Terrain construction retains original source and actual allocation retirement failures.", failures);
            throw;
        }
    }
}
