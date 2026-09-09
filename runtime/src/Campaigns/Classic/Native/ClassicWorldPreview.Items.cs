using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed partial class ClassicWorldPreview
{
    private sealed record GroundVisual(Node3D? Model, Sprite3D Sprite, ClassicItemLocation Location);
    private readonly Dictionary<string, GroundVisual> _groundVisuals = [];

    internal void ApplyCampaignInitialization(ClassicPlayerSession player)
    {
        if (player.Initialization is not { } initialization || initialization.Map != player.MapPath) return;
        SetMeta("campaign_initialization", initialization.Identity);
        SetMeta("script_execution", "campaign-start-header; object-events-and-world-clock-unbound");
        if (initialization.Light is not { } light) return;
        if (light is < 0 or > 100) throw new InvalidDataException("Source campaign light percentage is invalid.");
        var factor = light / 100f;
        foreach (var environment in GetChildren().OfType<WorldEnvironment>()) environment.Environment.AmbientLightEnergy = Policy.AmbientEnergy * factor;
        foreach (var sun in GetChildren().OfType<DirectionalLight3D>()) sun.LightEnergy *= factor;
        SetMeta("source_script_light_percent", light);
    }

    internal void RefreshItems(ClassicPlayerSession player, ClassicArtCache art)
    {
        var ground = player.Inventory.GroundItems;
        var represented = new HashSet<string>();
        // Existing MAP geometry remains usable while its stack is still at the
        // original location. Moved and split stacks use their live ledger owner.
        foreach (var node in GetChildren().OfType<Node3D>().Where(node => node.HasMeta("source_serial") && !node.HasMeta("dynamic_item")))
        {
            var serial = node.GetMeta("source_serial").AsInt32();
            node.Visible = !player.Inventory.Taken(player.MapPath, serial);
            var item = ground.FirstOrDefault(row => row.Origin.Map == player.MapPath && row.Object.Pid == row.Origin.Pid && row.Object.Serial == serial &&
                row.Location.Tile == row.Object.Tile && row.Location.Elevation == row.Object.Elevation);
            if (item is not null) { node.SetMeta("source_item_id", item.Id); represented.Add(item.Id); }
            else if (node.HasMeta("source_item_id")) node.RemoveMeta("source_item_id");
        }
        var desired = ground.Where(row => !represented.Contains(row.Id)).ToDictionary(row => row.Id);
        foreach (var (id, visual) in _groundVisuals.ToArray())
        {
            if (desired.TryGetValue(id, out var item) && visual.Location == item.Location) continue;
            _analogs.RemoveAll(pair => pair.Sprite == visual.Sprite);
            foreach (var node in new Node3D?[] { visual.Model, visual.Sprite })
                if (node is not null) { RemoveChild(node); node.QueueFree(); }
            _groundVisuals.Remove(id);
        }
        foreach (var (id, item) in desired)
        {
            if (_groundVisuals.ContainsKey(id)) continue;
            var placed = item.Object with { Tile = item.Location.Tile!.Value, Elevation = item.Location.Elevation!.Value, PixelX = 0, PixelY = 0 };
            var path = Fallout1NativePrototypeReader.ResolveArt(player.Catalog, placed.Fid);
            var decoded = art.Frame(path, placed.Rotation, placed.Frame);
            var anchor = Fo1HexMath.Center(placed.Tile);
            Node3D? model = null;
            try
            {
                model = _authored?.Prop(path, decoded.Texture, decoded.Frame, Policy.PixelsPerMeter, _assets, placed.Rotation)
                    ?? _assets?.Prop(path, decoded.Frame, Policy.PixelsPerMeter, placed.Rotation, placed.Pid, player.Choice.Campaign);
            }
            catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
            { ReportPresentationFailure($"item={id} art={path}: {error.Message}"); }
            Sprite3D sprite;
            if (model is not null)
            {
                model.Name = "DroppedItemModel";
                model.Rotation = new(0, -placed.Rotation * Mathf.Pi / 3, 0);
                model.Position = anchor + ClassicSceneryPlacement.ArtOffset(model, decoded.Frame, Policy.PixelsPerMeter);
                AddChild(model); sprite = PairOriginal(model, art, placed, path, placed.Frame, anchor);
            }
            else
            {
                var original = new ClassicScenerySprite
                {
                    Name = "DroppedItemOriginal",
                    Texture = decoded.Texture,
                    PixelSize = 1 / Policy.PixelsPerMeter,
                    Position = anchor + Vector3.Up * 0.02f,
                    Offset = new(decoded.Frame.DirectionX + decoded.Frame.FrameX, decoded.Frame.Height / 2f - decoded.Frame.DirectionY - decoded.Frame.FrameY),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Shaded = false,
                    Layers = SpriteLayer,
                    AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                };
                AddChild(original); original.Configure(art, path, placed); sprite = original;
                ReportPresentationFailure($"item={id} art={path}: 3D item model unavailable; original art is available in sprite view.");
            }
            foreach (var node in new Node3D?[] { model, sprite })
            {
                if (node is null) continue;
                node.SetMeta("dynamic_item", true); node.SetMeta("source_item_id", id);
                node.SetMeta("source_serial", item.Object.Serial); node.SetMeta("source_map", item.Origin.Map);
                node.SetMeta("source_tile", placed.Tile); node.SetMeta("source_pid", item.Object.Pid);
            }
            _groundVisuals.Add(id, new(model, sprite, item.Location));
        }
        RepresentationChanged?.Invoke();
    }
}
