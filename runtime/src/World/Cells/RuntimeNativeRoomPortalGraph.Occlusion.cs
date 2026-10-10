using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeRoomPortalGraph
{
    private FalloutOcclusionGraph _occlusion = null!;
    private RuntimeNativeSourceOcclusionPlane[] _occlusionPlanes = [];

    private static FalloutOcclusionGraph ConstructOcclusion(FalloutPluginStack records,
        FalloutFormKey cell, FalloutPortalCullingSource source, RuntimeNativeSourcePortalPrimitive[] rooms,
        RuntimeNativeSourceOcclusionPlane[] planes, FalloutRoomPortalSnapshot? saved)
    {
        var state = new FalloutOcclusionGraph(FalloutOcclusionCullingSource.Read(source.EngineSha256), cell);
        try
        {
            foreach (var plane in planes) state.Register(plane.Source, FalloutRoomMembership.Read(records, plane.Source.Reference));
            state.Seal(key => rooms.Any(row => row.Source.Reference == key && row.Source.Role == FalloutDynamicPrimitiveRole.Room));
            if (saved is not null)
            {
                if (saved.Occlusion is not { } cold || cold.Cell != cell || cold.LastNativeGeneration != saved.LastNativeGeneration)
                    throw new InvalidDataException("Cold CELL graph omitted or split its actual occlusion frame/source authority.");
                state.Restore(cold);
            }
            return state;
        }
        catch (Exception original)
        {
            try { state.Retire(); }
            catch (Exception error) { throw new AggregateException("Occlusion construction and retirement both failed.", original, error); }
            throw;
        }
    }
    private void ObserveOcclusion(IReadOnlyList<FalloutRoomCamera> cameras)
    {
        foreach (var plane in _occlusionPlanes) plane.RequireNative();
        _occlusion.Observe(cameras, _occlusionPlanes.Select(plane => plane.Geometry()).ToArray(), _units, _state.Generation);
        if (_occlusion.Views.Count != _state.Views.Count || _occlusion.Generation != _state.Generation)
            throw new InvalidDataException("Portal and occlusion consumers split the genuine camera/frame publication.");
    }
    private bool JointVisibility(FalloutFormKey? actualRoom, FalloutVisibilitySphere sphere)
    {
        if (_occlusion.Generation != _state.Generation || _occlusion.Views.Count != _state.Views.Count)
            throw new NotSupportedException("Source CELL visibility lost its correlated occlusion/portal view.");
        // Union complete eye results. Independent unions of portal and
        // occlusion results could incorrectly combine two different eyes.
        for (var view = 0; view < _state.Views.Count; view++)
            if (_state.SeesView(view, actualRoom, sphere, actualRoom is not null) &&
                _occlusion.Sees(view, actualRoom, sphere)) return true;
        return false;
    }
    internal void RequirePublished(RuntimeNativeSourceOcclusionPlane primitive)
    {
        if (!_occlusionPlanes.Contains(primitive) || primitive.GetParent() != _root)
            throw new InvalidDataException("Source plane borrowed a foreign native visibility graph.");
        if (!_published) Publish();
        RequireCurrent();
    }
    private FalloutOcclusionSnapshot CaptureOcclusion()
    {
        foreach (var plane in _occlusionPlanes) plane.RequireNative();
        foreach (var source in _occlusion.Registrations)
        {
            var record = _records.GetEffective(source.Primitive.Reference);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.ReadData())).ToLowerInvariant();
            if (record.IsDeleted || hash != source.Primitive.SourceSha256 ||
                FalloutRoomMembership.Read(_records, source.Primitive.Reference) != source.Membership)
                throw new InvalidDataException("Current occlusion source/member declaration changed before capture.");
        }
        var saved = _occlusion.Capture();
        if (saved.LastNativeGeneration != _state.Generation)
            throw new InvalidDataException("Occlusion capture crossed its actual native CELL frame.");
        return saved;
    }
}
