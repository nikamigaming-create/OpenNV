using Godot;
using OpenNV.Runtime.Content;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeSourceOcclusionPlane
{
    private RuntimeNativeRoomPortalGraph? _graph;
    private float _units;
    private Transform3D _birthReferenceTransform;
    internal FalloutDynamicPrimitive Source => _source;

    internal void Bind(RuntimeNativeRoomPortalGraph graph)
    {
        if (_graph is not null || GetParent() != graph.GetParent())
            throw new InvalidOperationException("Source plane is already borrowed by another native CELL graph.");
        _graph = graph;
    }
    internal FalloutOcclusionGeometry Geometry()
    {
        RequireNative();
        V3[] corners;
        if (_source.SerializedPlane is { } serialized)
        {
            // Stored plane coordinates belong to the source CELL, independent
            // of the placed reference's DATA center/Euler angles and scale.
            var root = (Node3D)GetParent();
            corners = serialized.SourceCorners().Select(point => N(root.GlobalTransform *
                (new Vector3(point.X, point.Z, -point.Y) * _units))).ToArray();
        }
        else
        {
            var x = _source.Primitive.X * _units; var y = _source.Primitive.Z * _units;
            var transform = GlobalTransform;
            corners = [N(transform * new Vector3(x, y, 0)), N(transform * new Vector3(-x, y, 0)),
                N(transform * new Vector3(-x, -y, 0)), N(transform * new Vector3(x, -y, 0))];
        }
        // Automatic is the actual selected plane constructor's initial mode.
        // A script/native mutation needs its own declared producer before it
        // may publish a different mode into this current/cold authority.
        return new(_source.Reference, _world.IsEnabled(_source.Reference), FalloutOcclusionCullMode.Automatic,
            Array.AsReadOnly(corners));
    }
    internal void RequireNative()
    {
        if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D ||
            _world.Get(_source.Reference).Base != _source.Base || _world.Placement(_source.Reference).Cell != _source.Cell ||
            Visible != _world.IsEnabled(_source.Reference))
            throw new NotSupportedException("Source plane lost its genuine native/reference/enable lifetime.");
        if (_source.SerializedPlane is not null)
        {
            var placement = _world.Placement(_source.Reference);
            var bits = placement.Position.Concat(placement.RotationRadians).Select(BitConverter.SingleToUInt32Bits).ToArray();
            if (_source.DeclaredReferencePose.Count != 6 || !bits.SequenceEqual(_source.DeclaredReferencePose) ||
                Transform != _birthReferenceTransform)
                throw new NotSupportedException("Serialized plane reference motion requires its source pose-update producer.");
        }
    }
    private static V3 N(Vector3 point) => new(point.X, point.Y, point.Z);
}
