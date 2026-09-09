using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicBlockoutPolicy(string Schema, float PixelsPerMeter, float MinimumWallHeight,
    float MaximumWallHeight, string StoneColor, float AmbientEnergy, float CameraDistance, float CameraPitch,
    float CameraFieldOfView, int FloorTexturePixels, float RoofHeight = 3.5f, float WallCutawayRadius = 1.25f,
    float SourceLightEnergy = 0.8f, float FixtureLightHeight = 2.4f, float FireLightHeight = 0.45f,
    float FloorRelief = 0.12f, float AmbientOcclusionRadius = 1.25f,
    float CameraMaximumDistance = 320, float CameraFramePadding = 1.15f,
    float VaultWallThickness = 0.18f, float WallBaselineTolerancePixels = 1.5f,
    float VaultWallHeight = 2.5f, float WallJoinTolerance = 0.025f, float CameraMinimumDistance = 1.25f);

/// <summary>
/// Source MAP/PRO/FRM presentation for any owned Fallout 1 map/elevation.
/// An optional classic player consumes this presentation; map scripts remain unbound.
/// </summary>
internal sealed partial class ClassicWorldPreview : Node3D
{
    private readonly List<string> _unresolved = [];
    private readonly Dictionary<string, int> _spriteGaps = new(StringComparer.OrdinalIgnoreCase);
    private ClassicOwnedWorldAssets? _assets;
    private bool _ownsAssets;
    private ClassicAuthoredScenery? _authored;
    private bool _ownsAuthored;
    private ClassicRoofPresentation? _roofs;
    private readonly List<ShaderMaterial> _wallMaterials = [];
    private Vector3? _playerPosition;
    internal int PresentedObjects { get; private set; }
    internal int WallHexes { get; private set; }
    internal string MapName { get; private set; } = "";
    internal IReadOnlyList<string> Unresolved => _unresolved;
    internal int SpriteFallbackObjects => _spriteGaps.Values.Sum();
    internal int MissingModels => SpriteFallbackObjects + _groundVisuals.Values.Count(row => row.Model is null) + (_playerModelMissing ? 1 : 0);
    private bool _playerModelMissing;
    internal void PlayerModelMissing(bool missing) { _playerModelMissing = missing; RepresentationChanged?.Invoke(); }
    internal ClassicWorldCamera Camera { get; private set; } = null!;
    internal ClassicBlockoutPolicy Policy { get; private set; } = null!;
    internal Label Caption { get; private set; } = null!;
    internal ClassicMapNavigation Navigation { get; private set; } = null!;
    internal Aabb LevelBounds { get; private set; }
    private readonly List<(Node3D Sprite, Node3D Model)> _analogs = [];
    internal bool ShowModels => _assets?.Prefer3D ?? true;
    internal const uint SharedLayer = 1, ModelLayer = 2, SpriteLayer = 4;
    private readonly List<(int Serial, Vector3 Position, string Art)> _actors = [];
    private int _actorIndex = -1;
    private readonly List<(int Serial, Vector3 Position)> _items = [];
    private int _itemIndex = -1;
    internal void InspectNextItem()
    {
        for (var attempt = 0; attempt < _items.Count; attempt++)
        {
            _itemIndex = (_itemIndex + 1) % _items.Count;
            var item = _items[_itemIndex];
            var node = GetChildren().OfType<Node3D>().FirstOrDefault(node => node.Name == $"SourceObject_{item.Serial}");
            if (node is null || !node.IsVisibleInTree()) continue;
            var bounds = node is Sprite3D ? new Aabb(item.Position, Vector3.One) : ClassicSceneryPlacement.Bounds(node);
            Camera.StopShot(); Camera.Focus = bounds.GetCenter();
            Camera.SubjectHeight = Math.Max(0.5f, bounds.Size.Length() * 0.5f);
            Camera.Distance = Math.Clamp(bounds.Size.Length() * 2.5f, 3, 10); Camera.Pitch = 0.85f; Camera.SaveView();
            GD.Print($"OPENNV_CLASSIC_INSPECT_ITEM map={MapName} serial={item.Serial}"); return;
        }
    }
    internal void InspectNextActor()
    {
        if (_actors.Count == 0) return;
        _actorIndex = (_actorIndex + 1) % _actors.Count;
        var actor = _actors[_actorIndex];
        var creature = GetChildren().OfType<ClassicCreatureBody>().FirstOrDefault(node => node.GetMeta("source_serial").AsInt32() == actor.Serial);
        var height = creature?.PresentationHeight ?? 1.875f;
        Camera.StopShot(); Camera.Focus = actor.Position + Vector3.Up * height * 0.4f;
        Camera.SubjectHeight = height;
        Camera.Distance = Math.Clamp(height * 4, 3, 9); Camera.Pitch = 0.62f; Camera.SaveView();
        GD.Print($"OPENNV_CLASSIC_INSPECT_ACTOR map={MapName} serial={actor.Serial} art={actor.Art}");
    }
    internal event Action? RepresentationChanged;
    internal event Action<bool>? PresentationChromeChanged;
    private bool _captionBeforeHide;
    internal void ShowPresentationChrome(bool visible)
    {
        if (!visible) { _captionBeforeHide = Caption.Visible; Caption.Visible = false; }
        else Caption.Visible = _captionBeforeHide;
        PresentationChromeChanged?.Invoke(visible);
    }
    internal ClassicPlayerBody? PlayerAnalog(string path, ClassicCharacterDraft choice, ClassicInventoryEntry? equipped = null) => _assets?.Humanoid(path, choice, equipped: equipped);
    internal void RemovePair(Node3D sprite) => _analogs.RemoveAll(pair => pair.Sprite == sprite);
    internal void ToggleRepresentation()
    {
        if (_assets is null) return;
        _assets.Prefer3D = !_assets.Prefer3D;
        Camera.CullMask = SharedLayer | (ShowModels ? ModelLayer : SpriteLayer);
        RepresentationChanged?.Invoke();
        GD.Print($"OPENNV_CLASSIC_REPRESENTATION map={MapName} models={ShowModels} pairs={_analogs.Count} remaining={SpriteFallbackObjects}");
    }

    internal void RegisterPair(Node3D sprite, Node3D model)
    {
        void Layer(Node node, uint layer)
        {
            if (node is VisualInstance3D visual) visual.Layers = layer;
            foreach (var child in node.GetChildren()) Layer(child, layer);
        }
        Layer(sprite, SpriteLayer); Layer(model, ModelLayer); _analogs.Add((sprite, model));
    }

    private Sprite3D PairOriginal(Node3D model, ClassicArtCache art, Fallout1NativeMapObject placed,
        string path, int frame, Vector3 anchor)
    {
        var decoded = art.Frame(path, placed.Rotation, frame);
        Sprite3D sprite = placed.Prototype.ObjectType == 1 ? new ClassicSourceActorSprite() : new ClassicScenerySprite();
        sprite.Name = $"Original_{placed.Serial}"; sprite.Texture = decoded.Texture;
        sprite.PixelSize = 1 / Policy.PixelsPerMeter; sprite.Position = anchor + Vector3.Up * 0.02f;
        sprite.Offset = new(decoded.Frame.DirectionX + decoded.Frame.FrameX, decoded.Frame.Height / 2f - decoded.Frame.DirectionY - decoded.Frame.FrameY);
        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled; sprite.Shaded = false;
        sprite.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass; sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        foreach (var node in new Node3D[] { model, sprite })
        {
            node.SetMeta("source_serial", placed.Serial); node.SetMeta("source_tile", placed.Tile);
            node.SetMeta("source_fid", placed.Fid.ToString("x8")); node.SetMeta("source_pid", placed.Pid);
            node.SetMeta("source_script", placed.ScriptId.ToString("x8")); node.SetMeta("source_art", path);
        }
        AddChild(sprite);
        if (sprite is ClassicSourceActorSprite actor) { actor.Configure(art, this, placed, path, frame); actor.BindAnalog(model); }
        if (sprite is ClassicScenerySprite scenery) scenery.Configure(art, path, placed);
        RegisterPair(sprite, model);
        return sprite;
    }

    internal void ReportPresentationFailure(string failure)
    {
        if (_unresolved.Contains(failure)) return;
        _unresolved.Add(failure);
        SetMeta("runtime_presentation_failures", _unresolved.ToArray());
        if (_unresolved.Count == 1) Caption.Text += "\n1 unresolved presentation bindings";
        else Caption.Text = Caption.Text.Replace($"{_unresolved.Count - 1} unresolved presentation bindings",
            $"{_unresolved.Count} unresolved presentation bindings", StringComparison.Ordinal);
        const string animationWarning = "\nSource animation unavailable; see reported source identity.";
        if (!Caption.Text.Contains(animationWarning, StringComparison.Ordinal)) Caption.Text += animationWarning;
        GD.Print("OPENNV_CLASSIC_PRESENTATION_UNBOUND " + failure);
    }

    internal static ClassicWorldPreview Build(Node parent, IFalloutClassicOwnedSource source,
        string mapPath = "maps/v13ent.map", int? selectedElevation = null, string? appearanceRoot = null,
        ClassicOwnedWorldAssets? sharedAssets = null, ClassicAuthoredScenery? sharedAuthored = null)
    {
        var policy = JsonSerializer.Deserialize<ClassicBlockoutPolicy>(
            Godot.FileAccess.GetFileAsString("res://config/classic-blockout-v1.json"))
            ?? throw new InvalidDataException("Classic blockout presentation policy is absent.");
        if (policy.Schema != "opennv-classic-blockout/v1" || policy.PixelsPerMeter <= 0 ||
            policy.MinimumWallHeight <= 0 || policy.MaximumWallHeight < policy.MinimumWallHeight)
            throw new InvalidDataException("Classic blockout presentation policy is invalid.");
        var level = source is ClassicMapCatalog catalog ? catalog.Load(mapPath) : null;
        var resource = level is null ? source.Read(mapPath, out _) : null;
        var map = level?.Map ?? Fallout1NativeMapReader.Read(resource!);
        var graph = level?.Objects ?? Fallout1NativeObjectGraphReader.Read(resource!, map, source);
        var elevation = selectedElevation ?? map.EnteringElevation;
        if (!map.Elevations.TryGetValue(elevation, out var tiles))
            throw new InvalidDataException($"{map.Name} has no source elevation {elevation}.");
        var result = new ClassicWorldPreview
        {
            Name = "ClassicOwnedWorld",
            MapName = map.Name,
            Policy = policy,
            Navigation = new(map, graph, elevation)
        };
        parent.AddChild(result);
        try
        {
            var art = new ClassicArtCache(path => source.Read(path, out _));
            result._ownsAuthored = sharedAuthored is null;
            result._authored = sharedAuthored ?? new ClassicAuthoredScenery();
            result._ownsAssets = sharedAssets is null && !string.IsNullOrWhiteSpace(appearanceRoot);
            result._assets = sharedAssets ?? (result._ownsAssets ? new ClassicOwnedWorldAssets(appearanceRoot) : null);
            var assets = result._assets;
            Aabb? levelBounds = null;
            void Include(Vector3 point) => levelBounds = levelBounds?.Expand(point) ?? new Aabb(point, Vector3.Zero);
            foreach (var tile in result.Navigation.FloorBacked)
                foreach (var corner in Fo1HexMath.Corners(tile)) Include(corner);
            var floorBounds = levelBounds;
            levelBounds = null;
            var caveWalls = new Dictionary<int, float>();
            Material WallMaterial(StandardMaterial3D sourceMaterial, bool sourceUv = false)
            {
                var material = ClassicWallCutaway.From(sourceMaterial, policy.WallCutawayRadius, sourceUv);
                result._wallMaterials.Add(material); return material;
            }
            var vaultMaterial = assets is null ? null : WallMaterial(assets.Material(assets.Recipe.VaultWall));
            var wallFaces = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            var vaultShells = new List<Aabb>();
            var levelWalls = graph.TopLevelObjects.Where(row => row.Elevation == elevation && row.Prototype.ObjectType == 3).ToArray();
            bool CaveWall(Fallout1NativeMapObject row)
            {
                try { return assets!.IsCaveWall(Fallout1NativePrototypeReader.ResolveArt(source, row.Fid)); }
                catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
                { return false; } // The source placement loop below reports the exact unresolved FID.
            }
            bool VisibleWall(Fallout1NativeMapObject row)
            {
                if (row.Tile < 0 || (row.Flags & 1) != 0) return false;
                try
                {
                    var path = Fallout1NativePrototypeReader.ResolveArt(source, row.Fid);
                    return art.Frame(path, row.Rotation, row.Frame).Frame.PaletteIndexes.Any(index => index != 0);
                }
                catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
                { return false; }
            }
            // Transparent collision markers such as block.frm are part of
            // navigation, not the room's visible material population.
            var visibleWalls = levelWalls.Where(VisibleWall).ToArray();
            var caveGround = assets is not null && visibleWalls.Count(CaveWall) > visibleWalls.Length / 2;
            var tileNames = Fallout1NativeLists.Read(art.Read("art/tiles/tiles.lst"));
            result._roofs = new ClassicRoofPresentation(); result.AddChild(result._roofs);
            result._roofs.Build(tiles, tileNames, art, policy.RoofHeight, policy.FloorTexturePixels);
            var floorIds = tiles.Select(value => (int)(value & 0xfff)).ToArray();
            var groundPalette = !caveGround && assets is not null && floorIds.Distinct().Any(id => id < tileNames.Count && assets.HasGround(tileNames[id]))
                ? ClassicGroundLayers.Palette(art, tileNames, floorIds, 16) : null;
            if (caveGround) result.AddChild(ClassicHexBlockout.Floor(result.Navigation.FloorBacked, assets!.Material(assets.Recipe.CaveFloor)));
            foreach (var group in Enumerable.Range(0, floorIds.Length).Where(index => !caveGround && floorIds[index] != 1)
                         .GroupBy(index => floorIds[index]))
            {
                if (group.Key >= tileNames.Count) throw new InvalidDataException("Floor art index exceeds tiles.lst.");
                var floorPath = $"art/tiles/{tileNames[group.Key]}";
                if (!art.Frame(floorPath).Frame.PaletteIndexes.Any(color => color != 0)) continue;
                var texture = art.Floor(floorPath, policy.FloorTexturePixels);
                var indices = group.ToArray();
                var instances = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = groundPalette is not null,
                    Mesh = ClassicHexBlockout.FloorPatch(assets?.Ground(floorPath, groundPalette ?? texture) ?? new StandardMaterial3D
                    {
                        AlbedoTexture = texture,
                        Roughness = 0.95f,
                        NormalEnabled = true,
                        NormalTexture = art.FloorNormal(floorPath, policy.FloorTexturePixels),
                        NormalScale = policy.FloorRelief,
                        MetallicSpecular = 0.15f,
                        CullMode = BaseMaterial3D.CullModeEnum.Back,
                        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    }),
                    InstanceCount = indices.Length,
                };
                for (var index = 0; index < indices.Length; index++)
                {
                    instances.SetInstanceTransform(index, new Transform3D(Basis.Identity, Fo1HexMath.FloorPatchCenter(indices[index])));
                    if (groundPalette is not null)
                    {
                        var side = (int)Math.Sqrt(floorIds.Length);
                        instances.SetInstanceCustomData(index, new Color((indices[index] % side) / (float)side, (indices[index] / side) / (float)side, 1f / side, 1f / side));
                    }
                }
                var floor = new MultiMeshInstance3D { Name = $"Floor_{group.Key}", Multimesh = instances };
                floor.SetMeta("source_art", floorPath); result.AddChild(floor);
            }

            foreach (var placed in graph.TopLevelObjects.Where(row => row.Elevation == elevation && row.Tile >= 0))
            {
                if (placed.Prototype.ObjectType == 5 || (placed.Flags & 1) != 0) continue;
                try
                {
                    var path = placed.Prototype.ObjectType == 1
                        ? art.Critter(placed.Fid, placed.Frame)
                        : (Fallout1NativePrototypeReader.ResolveArt(source, placed.Fid), placed.Frame);
                    var decoded = art.Frame(path.Item1, placed.Rotation, path.Item2);
                    ClassicSourceLighting.Add(result, placed, path.Item1, policy);
                    if (!decoded.Frame.PaletteIndexes.Any(index => index != 0)) continue;
                    var sourceOffset = ClassicMapProjection.World(4816 + placed.PixelX, 11 + placed.PixelY);
                    var anchor = Fo1HexMath.Center(placed.Tile) + new Vector3((float)sourceOffset.X, 0, (float)sourceOffset.Z);
                    if (placed.Prototype.ObjectType == 1) result._actors.Add((placed.Serial, anchor, path.Item1));
                    if (placed.Prototype.ObjectType == 0) result._items.Add((placed.Serial, anchor));
                    Include(anchor);
                    Include(anchor + Vector3.Up * Math.Min(policy.MaximumWallHeight, decoded.Frame.Height / policy.PixelsPerMeter));
                    if (placed.Prototype.ObjectType == 1 && assets is not null)
                    {
                        try
                        {
                            Node3D? analog = assets.Creature(path.Item1, art, placed, policy);
                            if (analog is null && ((placed.Fid >> 16) & 0xff) == 0)
                                analog = assets.Humanoid(path.Item1, placed: placed);
                            if (analog is { } creature)
                            {
                                creature.Name = $"SourceCreature_{placed.Serial}"; creature.Position = anchor;
                                result.AddChild(creature);
                                result.PairOriginal(creature, art, placed, path.Item1, path.Item2, anchor);
                                result.PresentedObjects++; continue;
                            }
                        }
                        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
                        { result._unresolved.Add($"serial={placed.Serial} 3D creature unavailable; original FRM retained: {error.Message}"); }
                    }
                    if (placed.Prototype.ObjectType == 3 && assets is not null && assets.IsCaveWall(path.Item1))
                    {
                        caveWalls[placed.Tile] = Math.Max(caveWalls.GetValueOrDefault(placed.Tile),
                            Math.Clamp(decoded.Frame.Height / policy.PixelsPerMeter, policy.MinimumWallHeight, policy.MaximumWallHeight));
                    }
                    else
                    {
                        Node3D? prop = null;
                        var vaultDoor = placed.Prototype.ObjectType == 2 && placed.Prototype.Subtype == 0 &&
                            Path.GetFileName(path.Item1).StartsWith('v');
                        var vaultFrame = placed.Prototype.ObjectType == 3 && assets is not null && assets.IsVaultFrame(path.Item1);
                        if (assets is not null && (placed.Prototype.ObjectType == 3 && assets.IsVaultWall(path.Item1) || vaultDoor || vaultFrame))
                        {
                            var key = $"{path.Item1}:{placed.Rotation}:{path.Item2}";
                            if (!wallFaces.TryGetValue(key, out var face))
                                wallFaces.Add(key, face = WallMaterial(new StandardMaterial3D
                                { AlbedoTexture = decoded.Texture, Roughness = 0.85f }, true));
                            var wall = vaultFrame ? ClassicArtExtrusion.Build(decoded.Frame, anchor, policy, vaultMaterial!, face) :
                                ClassicSourceWall.Build(decoded.Frame, anchor, policy, vaultMaterial!, face,
                                    vaultDoor ? null : vaultShells, vaultDoor);
                            if (wall is not null)
                            {
                                wall.Name = $"SourceWall_{placed.Serial}";
                                wall.SetMeta("source_serial", placed.Serial); wall.SetMeta("source_tile", placed.Tile);
                                wall.SetMeta("source_art", path.Item1); result.AddChild(wall);
                                result.PairOriginal(wall, art, placed, path.Item1, path.Item2, anchor);
                                result.PresentedObjects++; continue;
                            }
                            result._unresolved.Add($"serial={placed.Serial} wall contour requires geometry; original FRM retained: {path.Item1}");
                        }
                        try
                        {
                            if (placed.Prototype.ObjectType is 0 or 2)
                                prop = result._authored.Prop(path.Item1, decoded.Texture, decoded.Frame, policy.PixelsPerMeter, assets, placed.Rotation)
                                    ?? assets?.Prop(path.Item1, decoded.Frame, policy.PixelsPerMeter, placed.Rotation, placed.Pid, (source as ClassicMapCatalog)?.Campaign);
                        }
                        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
                        {
                            // An unavailable donor does not erase the original
                            // source object. Keep its FRM and report the gap.
                            result._unresolved.Add($"serial={placed.Serial} 3D donor unavailable; original FRM retained: {error.Message}");
                        }
                        if (prop is not null)
                        {
                            prop.Name = $"SourceObject_{placed.Serial}";
                            prop.Rotation = new(0, -placed.Rotation * Mathf.Pi / 3, 0);
                            prop.Position = anchor + ClassicSceneryPlacement.ArtOffset(prop, decoded.Frame, policy.PixelsPerMeter);
                            prop.SetMeta("source_serial", placed.Serial); prop.SetMeta("source_tile", placed.Tile);
                            prop.SetMeta("source_fid", placed.Fid.ToString("x8")); prop.SetMeta("source_script", placed.ScriptId.ToString("x8"));
                            result.AddChild(prop); result.PairOriginal(prop, art, placed, path.Item1, path.Item2, anchor);
                            result.PresentedObjects++; continue;
                        }
                        Sprite3D sprite = placed.Prototype.ObjectType == 1 ? new ClassicSourceActorSprite() : new ClassicScenerySprite();
                        sprite.Name = $"SourceObject_{placed.Serial}"; sprite.Texture = decoded.Texture;
                        sprite.PixelSize = 1f / policy.PixelsPerMeter; sprite.Position = anchor + Vector3.Up * 0.02f;
                        sprite.Offset = new(decoded.Frame.DirectionX + decoded.Frame.FrameX,
                            -(decoded.Frame.DirectionY + decoded.Frame.FrameY) + decoded.Frame.Height / 2f);
                        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled; sprite.Shaded = false;
                        sprite.Layers = SpriteLayer;
                        sprite.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass; sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                        sprite.SetMeta("source_serial", placed.Serial);
                        sprite.SetMeta("source_tile", placed.Tile);
                        sprite.SetMeta("source_fid", placed.Fid.ToString("x8"));
                        sprite.SetMeta("source_script", placed.ScriptId.ToString("x8"));
                        sprite.SetMeta("source_art", path.Item1);
                        sprite.SetMeta("presentation", "source-sprite-reference; 3D-replacement-missing");
                        result._spriteGaps[path.Item1] = result._spriteGaps.GetValueOrDefault(path.Item1) + 1;
                        result.AddChild(sprite);
                        if (sprite is ClassicSourceActorSprite actor) actor.Configure(art, result, placed, path.Item1, path.Item2);
                        if (sprite is ClassicScenerySprite scenery) scenery.Configure(art, path.Item1, placed);
                    }
                    result.PresentedObjects++;
                }
                catch (Exception error) when (error is FileNotFoundException or InvalidDataException or NotSupportedException)
                {
                    result._unresolved.Add($"serial={placed.Serial} fid={placed.Fid:x8}: {error.Message}");
                }
            }
            result.WallHexes = levelWalls.Select(row => row.Tile).Where(tile => tile >= 0).Distinct().Count();
            if (caveWalls.Count > 0) result.AddChild(ClassicHexBlockout.Build(caveWalls, new Color(policy.StoneColor), assets!.Recipe.WallShape,
                WallMaterial(assets.Material(assets.Recipe.CaveWall))));
            if (vaultShells.Count > 0)
            {
                var joins = ClassicWallJoiner.Build(vaultShells, vaultMaterial!, policy.WallJoinTolerance);
                result.AddChild(joins);
                foreach (var mesh in joins.FindChildren("*", "", true, false).OfType<VisualInstance3D>()) mesh.Layers = ModelLayer;
                if (joins is VisualInstance3D visual) visual.Layers = ModelLayer;
            }
            var grid = ClassicHexBlockout.Grid(result.Navigation.Walkable);
            grid.Visible = false;
            result.AddChild(grid);
            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("111516"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b7afa0"),
                AmbientLightEnergy = policy.AmbientEnergy,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
                SsaoRadius = policy.AmbientOcclusionRadius,
                SsaoIntensity = 1.35f,
                SsaoPower = 1.4f,
                SsaoLightAffect = 0.65f,
                VolumetricFogEnabled = true,
                VolumetricFogDensity = caveGround ? 0.008f : 0.0035f,
                VolumetricFogAlbedo = new Color("c4b99d"),
                VolumetricFogLength = 90,
                VolumetricFogAmbientInject = 0.3f,
                VolumetricFogAnisotropy = 0.45f,
            };
            result.AddChild(new WorldEnvironment { Environment = environment });
            result.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new(-48, -28, 0),
                LightColor = new Color("eedcbd"),
                LightEnergy = caveGround ? 0.35f : 0.9f,
                ShadowEnabled = true,
                DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
                DirectionalShadowMaxDistance = 65,
                ShadowBias = 0.02f,
                ShadowNormalBias = 0.18f
            });
            result.Camera = new ClassicWorldCamera
            {
                Name = "ClassicWorldCamera",
                Current = true,
                Focus = Fo1HexMath.Center(map.EnteringTile),
                Distance = policy.CameraDistance,
                Pitch = policy.CameraPitch,
                Fov = policy.CameraFieldOfView,
                MaximumDistance = policy.CameraMaximumDistance,
                MinimumDistance = policy.CameraMinimumDistance,
                FramePadding = policy.CameraFramePadding,
            };
            result.Camera.ToggleGrid += () => grid.Visible = !grid.Visible;
            result.Camera.ToggleRoofs += result._roofs.Toggle;
            result.AddChild(result.Camera);
            result.LevelBounds = levelBounds ?? floorBounds ?? new Aabb(Fo1HexMath.Center(map.EnteringTile), Vector3.Zero);
            if (elevation != map.EnteringElevation || !result.Navigation.FloorBacked.Contains(map.EnteringTile))
                result.FrameLevel();
            var ui = new CanvasLayer(); result.AddChild(ui);
            result.Caption = new Label
            {
                Position = new(20, 44),
                Text = $"FALLOUT  /  {map.Name} · Map inspection\n" +
                    "WASD pan · right-drag orbit · wheel zoom · G hex grid · R roofs" +
                    (result.Unresolved.Count == 0 ? "" : $"\n{result.Unresolved.Count} unresolved presentation bindings"),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }; ui.AddChild(result.Caption);
            result.Camera.CullMask = SharedLayer | (result.ShowModels ? ModelLayer : SpriteLayer);
            var presentation = new ClassicPresentationControls(); result.AddChild(presentation); presentation.Configure(result);
            result.SetMeta("source_map", mapPath);
            result.SetMeta("source_elevation", elevation);
            result.SetMeta("script_execution", "unimplemented");
            result.SetMeta("unresolved_objects", result.Unresolved.Count);
            result.SetMeta("sprite_reference_objects", result.SpriteFallbackObjects);
            result.SetMeta("sprite_replacements_missing", JsonSerializer.Serialize(result._spriteGaps));
            var authored = result.GetChildren().OfType<Node3D>().Where(node => node.HasMeta("authored_model"))
                .GroupBy(node => node.GetMeta("authored_model").AsString()).ToDictionary(group => group.Key, group => group.Count());
            var creatures = result.GetChildren().OfType<ClassicCreatureBody>().ToArray();
            GD.Print($"OPENNV_CLASSIC_MODELS map={map.Name} elevation={elevation} authored={JsonSerializer.Serialize(authored)} " +
                $"creatures={creatures.Length} spriteReferences={result.SpriteFallbackObjects}");
            if (result._spriteGaps.Count != 0)
                GD.Print($"OPENNV_CLASSIC_3D_GAPS map={map.Name} elevation={elevation} assets={JsonSerializer.Serialize(result._spriteGaps)}");
            GD.Print($"OPENNV_FO1_WORLD_PREVIEW map={map.Name} elevation={elevation} objects={result.PresentedObjects} " +
                $"wallHexes={result.WallHexes} unresolved={result.Unresolved.Count} scriptPrograms={map.LiveScripts.Count} gameplay=unimplemented");
            foreach (var failure in result.Unresolved) GD.Print($"OPENNV_FO1_WORLD_UNRESOLVED {failure}");
            return result;
        }
        catch { parent.RemoveChild(result); result.Free(); throw; }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
        if (_ownsAuthored) _authored?.Dispose();
    }

    internal void FrameLevel() => Camera.FrameLevel(LevelBounds);

    internal void RevealPlayer(Vector3 position) => _playerPosition = position;

    public override void _Process(double delta)
    {
        if (Camera is null) return;
        _roofs?.Reveal(_playerPosition ?? Camera.Focus);
        foreach (var material in _wallMaterials)
        {
            material.SetShaderParameter("reveal_player", _playerPosition.HasValue);
            material.SetShaderParameter("player_world", (_playerPosition ?? Camera.Focus) + Vector3.Up);
            material.SetShaderParameter("camera_world", Camera.GlobalPosition);
        }
    }
}
