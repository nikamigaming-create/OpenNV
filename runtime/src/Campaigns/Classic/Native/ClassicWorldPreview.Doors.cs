using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicWorldPreview
{
    private readonly Dictionary<int, (Node3D Root, Sprite3D Sprite, Node3D Model)> _doors = [];

    internal void RefreshDoor(ClassicPlayerSession player, ClassicArtCache art, int serial)
    {
        var definition = player.Doors.Definition(serial); var placed = definition.Object;
        if (placed.Elevation != player.Elevation || (placed.Flags & 1) != 0) return;
        if (_doors.Remove(serial, out var previous))
        { RemovePair(previous.Sprite); RemoveChild(previous.Root); previous.Root.QueueFree(); }
        else
        {
            foreach (var old in GetChildren().OfType<Node3D>().Where(node => node.HasMeta("source_serial") && node.GetMeta("source_serial").AsInt32() == serial).ToArray())
            {
                if (old is Sprite3D && old.HasMeta("presentation") && old.HasMeta("source_art"))
                {
                    var path = old.GetMeta("source_art").AsString();
                    if (_spriteGaps.TryGetValue(path, out var count))
                    { if (count == 1) _spriteGaps.Remove(path); else _spriteGaps[path] = count - 1; }
                }
                RemovePair(old); RemoveChild(old); old.QueueFree();
            }
        }
        var pose = player.Doors.Pose(serial);
        var decoded = art.Frame(definition.Art, placed.Rotation, pose.Frame);
        var reference = art.Frame(definition.Art, placed.Rotation, 0);
        var offset = ClassicMapProjection.World(4816 + placed.PixelX, 11 + placed.PixelY);
        var anchor = Fo1HexMath.Center(placed.Tile) + new Vector3((float)offset.X, 0, (float)offset.Z);
        var root = new Node3D { Name = $"InteractiveDoor_{serial}" };
        root.SetMeta("source_serial", serial); root.SetMeta("source_tile", placed.Tile); root.SetMeta("source_pid", placed.Pid);
        root.SetMeta("source_door_frame", pose.Frame); root.SetMeta("source_door_open", pose.Open); root.SetMeta("source_door_locked", pose.Locked);
        var face = new StandardMaterial3D
        {
            AlbedoTexture = decoded.Texture,
            Roughness = 0.85f,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var edge = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.25f, 0.23f), Roughness = 0.9f };
        // Every animation frame uses the closed frame's ground plane. Source
        // offsets move the panel within that plane without moving its hex.
        var model = ClassicArtExtrusion.Build(decoded.Frame, anchor, Policy, edge, face, reference.Frame);
        root.AddChild(model);
        var sprite = new Sprite3D
        {
            Name = "OriginalDoor",
            Texture = decoded.Texture,
            PixelSize = 1 / Policy.PixelsPerMeter,
            Position = anchor + Vector3.Up * 0.02f,
            Offset = new(decoded.Frame.DirectionX + decoded.Frame.FrameX, decoded.Frame.Height / 2f - decoded.Frame.DirectionY - decoded.Frame.FrameY),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        root.AddChild(sprite); AddChild(root); RegisterPair(sprite, model); _doors.Add(serial, (root, sprite, model));
        RepresentationChanged?.Invoke();
    }
}
