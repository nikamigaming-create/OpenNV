using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// A coordinate-space binding to existing source nodes. It creates no actor,
// source hierarchy, pose, bone, or audio emitter substitute.
internal sealed record NativeOwnedSoundSourceScene(string? Identity, Node3D Root,
    Node3D AudioReference, Func<Transform3D>? WorldFromSource = null, Action? RequirePublishedPose = null)
{
    internal Vector3 Position(Node3D emitter)
    {
        RequireEmitter(emitter);
        RuntimeNativeNifSoundEmitters.Refresh(emitter);
        var point = emitter.GlobalPosition;
        if (WorldFromSource is { } frame) point = frame() * point;
        if (!point.IsFinite()) throw new InvalidDataException("Source sound has a nonfinite audio-space position.");
        return point;
    }

    internal void RequireEmitter(Node3D emitter)
    {
        if (!GodotObject.IsInstanceValid(Root) || !Root.IsInsideTree() ||
            !GodotObject.IsInstanceValid(AudioReference) || !AudioReference.IsInsideTree() ||
            !GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree() ||
            emitter != Root && !Root.IsAncestorOf(emitter))
            throw new NotSupportedException("Sound source scene lost its actual resident source emitter or audio reference.");
        if (Root.GetViewport() != AudioReference.GetViewport() && WorldFromSource is null)
            throw new NotSupportedException("Sound source crosses viewports without its actual coordinate-space owner.");
    }
}
