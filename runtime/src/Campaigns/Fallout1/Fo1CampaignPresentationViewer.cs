using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal partial class Fo1CampaignPresentationViewer : Node3D
{
    private Fo1CampaignPresentationCatalog _catalog = null!;
    private Fo1CameraProfile _cameraProfile = null!;
    private Node3D _mapRoot = null!;
    private Node3D _spriteRoot = null!;
    private Node3D _yawPivot = null!;
    private Node3D _pitchPivot = null!;
    private Camera3D _camera = null!;
    private Label _status = null!;
    private CanvasLayer _statusLayer = null!;
    private Fo1CampaignMapPresentation _currentMap = null!;
    private readonly Dictionary<string, Texture2D> _textureCache = new(StringComparer.Ordinal);
    private int _mapIndex;
    private int _elevationIndex;
    private float _targetSize;
    private float _targetYaw;
    private float _targetPitch;
    private bool _orbitDragging;
    private bool _panDragging;
    private Fo1CampaignMapViewCoverage _coverage = null!;

    internal Fo1CampaignMapViewCoverage Configure(
        Fo1CampaignPresentationCatalog catalog,
        string? requestedMap,
        int? requestedElevation)
    {
        _catalog = catalog;
        _cameraProfile = catalog.RuntimeProfile.Camera;
        Name = "Fo1CampaignPresentationViewer";
        _mapIndex = requestedMap is null
            ? Math.Max(0, catalog.Maps.ToList().FindIndex(
                row => row.Id.Equals(
                    catalog.Viewer.DefaultMapId,
                    StringComparison.OrdinalIgnoreCase)))
            : catalog.Maps.ToList().FindIndex(
                row => row.Id.Equals(requestedMap, StringComparison.OrdinalIgnoreCase));
        if (_mapIndex < 0)
            throw new InvalidOperationException($"Fallout campaign map is absent: {requestedMap}");
        BuildEnvironment();
        BuildCamera();
        BuildStatusUi();
        LoadMap(_mapIndex, requestedElevation);
        return _coverage;
    }

    internal Fo1CampaignMapViewCoverage LoadForProof(
        Fo1CampaignMapPresentation map,
        int elevationIndex)
    {
        _mapIndex = _catalog.Maps.ToList().FindIndex(
            row => row.Id.Equals(map.Id, StringComparison.OrdinalIgnoreCase));
        if (_mapIndex < 0 || elevationIndex is < 0 || elevationIndex >= map.Elevations.Count)
            throw new InvalidOperationException(
                $"Fallout campaign proof selection is invalid: {map.Id}/{elevationIndex}");
        _currentMap = map;
        _elevationIndex = elevationIndex;
        BuildMap(map, map.Elevations[elevationIndex]);
        ResetCamera();
        return _coverage;
    }

    internal void SetStatusVisible(bool visible) => _statusLayer.Visible = visible;

    internal void SetCaptureSize(float sizeMeters)
    {
        var profile = _cameraProfile.Tactical;
        _targetSize = Math.Clamp(sizeMeters, profile.MinimumSizeMeters, profile.MaximumSizeMeters);
        _camera.Size = _targetSize;
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        var profile = _cameraProfile.Tactical;
        var weight = Math.Clamp((float)delta * _cameraProfile.SmoothingPerSecond, 0.0f, 1.0f);
        _yawPivot.Rotation = new Vector3(
            0.0f,
            Mathf.LerpAngle(_yawPivot.Rotation.Y, _targetYaw, weight),
            0.0f);
        _pitchPivot.Rotation = new Vector3(
            Mathf.LerpAngle(_pitchPivot.Rotation.X, _targetPitch, weight),
            0.0f,
            0.0f);
        _camera.Size = Mathf.Lerp(_camera.Size, _targetSize, weight);

        var keyboard = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            keyboard.X -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            keyboard.X += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            keyboard.Y += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            keyboard.Y -= 1.0f;
        if (keyboard.LengthSquared() > 0.0f)
        {
            keyboard = keyboard.Normalized();
            var speed = profile.KeyboardPanMetersPerSecond *
                (Input.IsKeyPressed(Key.Shift) ? profile.FastPanMultiplier : 1.0f);
            var right = _yawPivot.GlobalBasis.X;
            var forward = -_yawPivot.GlobalBasis.Z;
            right.Y = 0.0f;
            forward.Y = 0.0f;
            _yawPivot.Position +=
                (right.Normalized() * keyboard.X + forward.Normalized() * keyboard.Y) *
                speed * (float)delta;
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        var profile = _cameraProfile.Tactical;
        if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.F6)
                SwitchMap(-1);
            else if (key.PhysicalKeycode == Key.F7)
                SwitchMap(1);
            else if (key.PhysicalKeycode == Key.F8)
                SwitchElevation();
            else if (key.PhysicalKeycode == Key.Q &&
                _catalog.Viewer.Scene.SourceReferenceOrbitEnabled)
                _targetYaw += Mathf.DegToRad(profile.KeyboardYawStepDegrees);
            else if (key.PhysicalKeycode == Key.E &&
                _catalog.Viewer.Scene.SourceReferenceOrbitEnabled)
                _targetYaw -= Mathf.DegToRad(profile.KeyboardYawStepDegrees);
            else if (key.PhysicalKeycode == Key.Home)
                ResetCamera();
            else if (key.PhysicalKeycode == Key.V)
            {
                _spriteRoot.Visible = !_spriteRoot.Visible;
                UpdateStatus();
            }
            else if (key.PhysicalKeycode == Key.Escape)
            {
                _orbitDragging = false;
                _panDragging = false;
            }
            return;
        }

        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle)
                _orbitDragging = button.Pressed &&
                    _catalog.Viewer.Scene.SourceReferenceOrbitEnabled;
            else if (button.ButtonIndex == MouseButton.Right)
                _panDragging = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                _targetSize = Math.Clamp(
                    _targetSize * profile.CursorZoomFactor,
                    profile.MinimumSizeMeters,
                    profile.MaximumSizeMeters);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                _targetSize = Math.Clamp(
                    _targetSize / profile.CursorZoomFactor,
                    profile.MinimumSizeMeters,
                    profile.MaximumSizeMeters);
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
            return;
        if (_catalog.Viewer.Scene.SourceReferenceOrbitEnabled &&
            (_orbitDragging || Input.IsPhysicalKeyPressed(Key.Ctrl)))
        {
            _targetYaw -= motion.Relative.X * profile.OrbitRadiansPerPixel;
            _targetPitch = Math.Clamp(
                _targetPitch + motion.Relative.Y * profile.OrbitRadiansPerPixel,
                Mathf.DegToRad(profile.MinimumPitchDegrees),
                Mathf.DegToRad(profile.MaximumPitchDegrees));
        }
        else if (_panDragging)
            PanByPixels(motion.Relative);
    }

    private void LoadMap(int mapIndex, int? requestedElevation)
    {
        _mapIndex = (mapIndex % _catalog.Maps.Count + _catalog.Maps.Count) % _catalog.Maps.Count;
        var source = Fo1CampaignPresentationContract.LoadMap(
            _catalog,
            _catalog.Maps[_mapIndex].Id);
        _currentMap = source;
        _elevationIndex = requestedElevation.HasValue
            ? source.Elevations.ToList().FindIndex(row => row.Elevation == requestedElevation.Value)
            : source.Elevations.ToList().FindIndex(row => row.Elevation == source.Entry.Elevation);
        if (_elevationIndex < 0)
            throw new InvalidOperationException(
                $"Fallout campaign elevation is absent: {source.Id}/{requestedElevation}");
        BuildMap(source, source.Elevations[_elevationIndex]);
        ResetCamera();
    }

    private void BuildMap(
        Fo1CampaignMapPresentation map,
        Fo1CampaignElevationPresentation elevation)
    {
        if (IsInstanceValid(_mapRoot))
        {
            RemoveChild(_mapRoot);
            _mapRoot.QueueFree();
        }
        _mapRoot = new Node3D
        {
            Name = $"FO1_CAMPAIGN_{NodeIdentifier(map.Id)}_ELEVATION_{elevation.Elevation}",
        };
        AddChild(_mapRoot);
        var floorRoot = new Node3D { Name = "SourceFloorArt" };
        _mapRoot.AddChild(floorRoot);
        var renderedFloors = BuildFloor(floorRoot, elevation.FloorIds);
        var wallRoot = new Node3D { Name = "ConnectedWallGeometry" };
        _mapRoot.AddChild(wallRoot);
        var wallCoverage = Fo1CampaignWallGeometry.Build(wallRoot, _catalog, elevation);
        _spriteRoot = new Node3D { Name = "SourceObjectSprites" };
        _spriteRoot.Visible = _catalog.Viewer.Scene.SourceReferenceVisibleByDefault;
        _mapRoot.AddChild(_spriteRoot);
        var mobSerials = elevation.Mobs.Select(row => row.Serial).ToHashSet();
        foreach (var placement in elevation.Placements)
            BuildSprite(_spriteRoot, placement, mobSerials.Contains(placement.Serial));
        BuildPlayer(_spriteRoot, map.Entry, elevation.Elevation == map.Entry.Elevation);
        _coverage = new Fo1CampaignMapViewCoverage(
            map.Id,
            map.SourceFile,
            elevation.Elevation,
            renderedFloors,
            elevation.Placements.Count,
            elevation.Mobs.Count,
            elevation.Doors.Count,
            elevation.Blockers.Count,
            wallCoverage.RenderedWallHexes,
            wallCoverage.ConnectedComponents,
            wallCoverage.BoundaryEdges,
            wallCoverage.Triangles,
            wallCoverage.BlockingCollisionHexes,
            elevation.ProvisionalWalkableHexes,
            elevation.SkippedPlacements.Count,
            map.Entry.Tile,
            map.Entry.Elevation == elevation.Elevation);
        UpdateStatus();
    }

    private int BuildFloor(Node3D root, IReadOnlyList<int> floorIds)
    {
        var rendered = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Count)
                     .Where(index => floorIds[index] != 1)
                     .GroupBy(index => floorIds[index]))
        {
            var indices = group.ToArray();
            var artifact = _catalog.TileArtifacts[group.Key];
            var material = new StandardMaterial3D
            {
                AlbedoTexture = LoadTexture(artifact.Path, artifact.Width, artifact.Height),
                AlbedoColor = _catalog.RuntimeProfile.Scene.SourceFloor.AlbedoColor *
                    _catalog.Viewer.Scene.SourceColorMultiplier,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            };
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new PlaneMesh
                {
                    Size = new Vector2(
                        Fo1HexMath.ColumnSpacingMeters * 2.0f,
                        Fo1HexMath.FlatToFlatMeters * 2.0f),
                    Material = material,
                },
                InstanceCount = indices.Length,
            };
            for (var instance = 0; instance < indices.Length; instance++)
            {
                var center = Fo1HexMath.FloorPatchCenter(indices[instance]);
                center.Y = _catalog.RuntimeProfile.Scene.SourceFloor.YOffsetMeters;
                multiMesh.SetInstanceTransform(instance, new Transform3D(Basis.Identity, center));
            }
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"FloorArt_{group.Key:D4}_{indices.Length}",
                Multimesh = multiMesh,
            });
            rendered += indices.Length;
        }
        return rendered;
    }

    private void BuildSprite(Node3D root, Fo1CampaignPlacement placement, bool isMob)
    {
        var artifact = _catalog.SpriteArtifacts[placement.ArtifactId];
        var offset = new Vector2(
            placement.PixelOffset.X + artifact.FrameOffset.X,
            -(placement.PixelOffset.Y + artifact.FrameOffset.Y) + artifact.Height / 2.0f);
        root.AddChild(new Sprite3D
        {
            Name = $"Object_{placement.Serial}_{NodeIdentifier(placement.ArtFilename)}",
            Texture = LoadTexture(artifact.Path, artifact.Width, artifact.Height),
            PixelSize = 1.0f / _catalog.PixelsPerMeter,
            Position = placement.WorldMeters + Vector3.Up * _catalog.GroundAnchorMeters,
            Offset = offset,
            Billboard = _catalog.Viewer.Scene.SourceSpriteOrientation ==
                "camera-facing-source-reference" || isMob
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            RotationDegrees = _catalog.Viewer.Scene.SourceSpriteOrientation ==
                "camera-facing-source-reference" || isMob
                ? Vector3.Zero
                : new Vector3(0.0f, _catalog.StaticWorldYawDegrees, 0.0f),
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Modulate = _catalog.Viewer.Scene.SourceColorMultiplier,
        });
    }

    private void BuildPlayer(Node3D root, Fo1CampaignMapEntry entry, bool visible)
    {
        var artifact = _catalog.SpriteArtifacts[entry.PlayerArtifactId];
        root.AddChild(new Sprite3D
        {
            Name = "VaultDwellerSourceEntry",
            Texture = LoadTexture(artifact.Path, artifact.Width, artifact.Height),
            PixelSize = 1.0f / _catalog.PixelsPerMeter,
            Position = entry.WorldMeters + Vector3.Up * _catalog.GroundAnchorMeters,
            Offset = new Vector2(
                artifact.FrameOffset.X,
                -artifact.FrameOffset.Y + artifact.Height / 2.0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Modulate = _catalog.Viewer.Scene.SourceColorMultiplier,
            Visible = visible,
        });
    }

    private Texture2D LoadTexture(string path, int expectedWidth, int expectedHeight)
    {
        if (_textureCache.TryGetValue(path, out var cached))
            return cached;
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != expectedWidth || image.GetHeight() != expectedHeight)
            throw new InvalidOperationException($"Fallout campaign texture could not be loaded: {path}");
        var texture = ImageTexture.CreateFromImage(image);
        _textureCache.Add(path, texture);
        return texture;
    }

    private void BuildEnvironment()
    {
        var profile = _catalog.RuntimeProfile.Scene.Atmosphere;
        AddChild(new WorldEnvironment
        {
            Name = "Fo1CampaignWorldEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = profile.BackgroundColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = profile.AmbientColor,
                AmbientLightEnergy = profile.AmbientEnergy,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = _catalog.Viewer.Scene.TonemapExposure,
                FogEnabled = true,
                FogLightColor = profile.FogColor,
                FogLightEnergy = profile.FogLightEnergy,
                FogDensity = _catalog.Viewer.Scene.FogDensity,
                FogAerialPerspective = _catalog.Viewer.Scene.FogAerialPerspective,
                FogSkyAffect = profile.FogSkyAffect,
            },
        });
        AddChild(new DirectionalLight3D
        {
            Name = "Fo1CampaignDirectionalLight",
            RotationDegrees = profile.DirectionalLight.RotationDegrees,
            LightColor = profile.DirectionalLight.Color,
            LightEnergy = profile.DirectionalLight.Energy,
            ShadowEnabled = false,
        });
    }

    private void BuildCamera()
    {
        var profile = _cameraProfile.Tactical;
        _targetSize = profile.HomeSizeMeters;
        _targetYaw = Mathf.DegToRad(profile.HomeYawDegrees);
        _targetPitch = Mathf.DegToRad(profile.HomePitchDegrees);
        _yawPivot = new Node3D
        {
            Name = "Fo1CampaignCameraYaw",
            Rotation = new Vector3(0.0f, _targetYaw, 0.0f),
        };
        AddChild(_yawPivot);
        _pitchPivot = new Node3D
        {
            Name = "Fo1CampaignCameraPitch",
            Rotation = new Vector3(_targetPitch, 0.0f, 0.0f),
        };
        _yawPivot.AddChild(_pitchPivot);
        _camera = new Camera3D
        {
            Name = "Fo1CampaignOrthographicCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = _targetSize,
            Position = new Vector3(
                0.0f,
                0.0f,
                MathF.Max(
                    profile.MinimumCameraDistanceMeters,
                    profile.HomeSizeMeters * profile.HomeDistanceScale)),
            Near = profile.NearClipMeters,
            Far = profile.FarClipMeters,
            Current = true,
        };
        _pitchPivot.AddChild(_camera);
        _camera.AddChild(new DirectionalLight3D
        {
            Name = "Fo1CampaignCameraFill",
            LightColor = profile.FillLightColor,
            LightEnergy = profile.FillLightEnergy,
            ShadowEnabled = false,
        });
    }

    private void BuildStatusUi()
    {
        var profile = _catalog.Viewer.StatusPanel;
        _statusLayer = new CanvasLayer { Name = "Fo1CampaignBrowserUi" };
        AddChild(_statusLayer);
        var panel = new ColorRect
        {
            Name = "CampaignStatusPanel",
            Color = profile.PanelColor,
            OffsetLeft = profile.LeftPixels,
            OffsetTop = profile.TopPixels,
            OffsetRight = profile.RightPixels,
            OffsetBottom = profile.BottomPixels,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statusLayer.AddChild(panel);
        _status = new Label
        {
            OffsetLeft = profile.TextLeftPixels,
            OffsetTop = profile.TextTopPixels,
            OffsetRight = profile.TextRightPixels,
            OffsetBottom = profile.TextBottomPixels,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _status.AddThemeColorOverride("font_color", profile.FontColor);
        _status.AddThemeFontSizeOverride("font_size", profile.FontSizePixels);
        panel.AddChild(_status);
    }

    private void ResetCamera()
    {
        var profile = _cameraProfile.Tactical;
        _yawPivot.Position = _currentMap.Entry.WorldMeters + Vector3.Up *
            profile.EntryFraming.FocusHeightMeters;
        _targetSize = profile.HomeSizeMeters;
        _targetYaw = Mathf.DegToRad(profile.HomeYawDegrees);
        _targetPitch = Mathf.DegToRad(profile.HomePitchDegrees);
        _yawPivot.Rotation = new Vector3(0.0f, _targetYaw, 0.0f);
        _pitchPivot.Rotation = new Vector3(_targetPitch, 0.0f, 0.0f);
        _camera.Size = _targetSize;
    }

    private void SwitchMap(int direction)
    {
        LoadMap(_mapIndex + direction, null);
    }

    private void SwitchElevation()
    {
        var next = (_elevationIndex + 1) % _currentMap.Elevations.Count;
        _elevationIndex = next;
        BuildMap(_currentMap, _currentMap.Elevations[next]);
        ResetCamera();
    }

    private void PanByPixels(Vector2 pixels)
    {
        var viewportHeight = Math.Max(1.0f, GetViewport().GetVisibleRect().Size.Y);
        var metersPerPixel = _camera.Size / viewportHeight;
        var right = _yawPivot.GlobalBasis.X;
        var forward = -_yawPivot.GlobalBasis.Z;
        right.Y = 0.0f;
        forward.Y = 0.0f;
        _yawPivot.Position +=
            (-right.Normalized() * pixels.X + forward.Normalized() * pixels.Y) *
            metersPerPixel;
    }

    private void UpdateStatus()
    {
        if (_status is null || _coverage is null)
            return;
        _status.Text =
            $"FO1 CONNECTED 3D MAP  •  {_coverage.MapId.ToUpperInvariant()}  " +
            $"ELEVATION {_coverage.Elevation}  •  {_mapIndex + 1}/{_catalog.Maps.Count}\n" +
            $"FLOOR {_coverage.RenderedFloorPatches}  WALL HEXES {_coverage.RenderedWallHexes}  " +
            $"JOINED {_coverage.WallComponents}  EDGES {_coverage.WallBoundaryEdges}  " +
            $"COLLISION {_coverage.BlockingCollisionWallHexes}\n" +
            $"SOURCE CARDS {(_spriteRoot.Visible ? "DEBUG ON" : "OFF")}  •  " +
            $"MOBS {_coverage.Mobs}  DOORS {_coverage.Doors}\n" +
            (_catalog.Viewer.Scene.SourceReferenceOrbitEnabled
                ? "F6/F7 maps  •  F8 elevation  •  MMB/Ctrl orbit  •  RMB pan  •  wheel zoom  •  " +
                    "WASD/arrows pan  •  Q/E rotate  •  Home entry  •  V source debug"
                : "F6/F7 maps  •  F8 elevation  •  fixed authentic isometric view  •  " +
                    "RMB pan  •  wheel zoom  •  WASD/arrows pan  •  Home entry  •  V source debug");
    }

    private static string NodeIdentifier(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}

internal sealed record Fo1CampaignMapViewCoverage(
    string MapId,
    string SourceFile,
    int Elevation,
    int RenderedFloorPatches,
    int SpritePlacements,
    int Mobs,
    int Doors,
    int Blockers,
    int RenderedWallHexes,
    int WallComponents,
    int WallBoundaryEdges,
    int WallTriangles,
    int BlockingCollisionWallHexes,
    int ProvisionalWalkableHexes,
    int SkippedSpriteObjects,
    int EntryTile,
    bool PlayerEntryVisible);
