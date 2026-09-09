using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicWorldPreview
{
    internal Node3D? InventoryAnalog(ClassicInventoryEntry item, ClassicArtCache art, ClassicPlayerSession player)
        => InventoryAnalog(item.Object, art, player);

    internal Node3D? InventoryAnalog(Fallout1NativeMapObject placed, ClassicArtCache art, ClassicPlayerSession player)
    {
        if (placed.Prototype.ObjectType == 1)
        {
            var actorPath = art.Critter(placed.Fid, placed.Frame).Path;
            return _assets?.Creature(actorPath, art, placed, Policy);
        }
        var path = Fallout1NativePrototypeReader.ResolveArt(player.Catalog, placed.Fid);
        var decoded = art.Frame(path, placed.Rotation, placed.Frame);
        return _authored?.Prop(path, decoded.Texture, decoded.Frame, Policy.PixelsPerMeter, _assets, placed.Rotation)
            ?? _assets?.Prop(path, decoded.Frame, Policy.PixelsPerMeter, placed.Rotation, placed.Pid, player.Choice.Campaign);
    }

    internal ClassicInventoryModelSlot InventorySlot(ClassicInventoryEntry item, Rect2 bounds, ClassicArtCache art, ClassicPlayerSession player) =>
        new($"item:{item.Object.Pid}:{item.Object.Fid}:{item.Object.Frame}", item.Definition.Name, bounds, () => InventoryAnalog(item, art, player));
}
