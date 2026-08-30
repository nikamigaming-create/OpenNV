using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed class OpeningRaceSexRenderedDeviceHost
{
    private readonly OpeningRaceSexRenderedDevice _source;
    private readonly Control _host;
    private readonly Vector2 _canvasSize;
    private readonly Dictionary<string, RenderedDeviceGlow> _glows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _creatorModeHitTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private MeshInstance3D? _deviceMesh;
    private Camera3D? _deviceCamera;
    private RenderedDeviceFrame? _deviceFrame;
    private int _creatorButtonSurface = -1;
    private int _creatorGlowSurface = -1;
    private int _shellSurface = -1;

    internal Control ScreenRoot { get; }
    internal SubViewport ScreenViewport { get; }
    internal SubViewport DeviceViewport { get; }
    internal Rect2 FacePresentationRect { get; private set; }
    internal Rect2 MenuPresentationRect { get; private set; }

    internal OpeningRaceSexRenderedDeviceHost(
        OpeningRaceSexRenderedDevice source,
        Control host,
        Vector2 canvasSize,
        RuntimeConfiguration configuration,
        CellContentLoader.LightingContract lighting,
        float unitsToMeters)
    {
        if (!source.Bool("enabled") ||
            canvasSize.X <= 0.0f ||
            canvasSize.Y <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device state is not enabled or has no canvas.");
        _source = source;
        _host = host;
        _canvasSize = canvasSize;
        var viewportSize = new Vector2I(
            Mathf.RoundToInt(canvasSize.X),
            Mathf.RoundToInt(canvasSize.Y));
        var screenViewport = new SubViewport
        {
            Name = "OwnedRaceSexScreenViewport",
            Size = viewportSize,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
        };
        host.AddChild(screenViewport);
        ScreenViewport = screenViewport;
        ScreenRoot = new Control
        {
            Name = "OwnedRaceSexScreenCanvas",
            Size = canvasSize,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        screenViewport.AddChild(ScreenRoot);

        var deviceViewport = new SubViewport
        {
            Name = "OwnedRaceSexDeviceViewport",
            Size = viewportSize,
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        host.AddChild(deviceViewport);
        DeviceViewport = deviceViewport;
        BuildDevice(
            deviceViewport,
            screenViewport.GetTexture(),
            canvasSize,
            configuration,
            lighting,
            unitsToMeters);
        var deviceTexture = new TextureRect
        {
            Name = "OwnedRaceSexRenderedDevice",
            Texture = deviceViewport.GetTexture(),
            Position = source.Framing.Alignment.DeviceTranslationCanvasUnits,
            Size = canvasSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        host.AddChild(deviceTexture);
    }

    internal void SetActiveList(string activeList)
    {
        var role = activeList.ToLowerInvariant() switch
        {
            "sex" => "sexGlow",
            "race" => "raceGlow",
            "face" or "facegeometry" => "faceGlow",
            "hair" or "eyes" or "body" => "hairGlow",
            _ => throw new InvalidOperationException(
                $"Owned RaceSex rendered-device list role is unsupported: {activeList}"),
        };
        foreach (var (candidate, glow) in _glows)
            glow.Mesh.SetSurfaceOverrideMaterial(
                glow.Surface,
                candidate.Equals(role, StringComparison.OrdinalIgnoreCase)
                    ? glow.Active
                    : glow.Inactive);
    }

    private void BuildDevice(
        SubViewport viewport,
        Texture2D screenTexture,
        Vector2 canvasSize,
        RuntimeConfiguration configuration,
        CellContentLoader.LightingContract lighting,
        float unitsToMeters)
    {
        var contract = _source.Device;
        VerifiedGltfLoader.VerifyHash(contract.ModelPath, contract.ModelSha256);
        VerifiedGltfLoader.VerifyHash(contract.SidecarPath, contract.SidecarSha256);
        VerifiedGltfLoader.VerifyHash(contract.BufferPath, contract.BufferSha256);
        var loaded = VerifiedGltfLoader.Load(contract.ModelPath, contract.SidecarPath);
        loaded.CollisionScene?.Free();
        if (!loaded.SourceSha256.Equals(
                contract.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device source identity changed.");
        var model = loaded.Scene;
        model.Name = "OwnedRaceSexInterface01";
        var scale = _source.Float("raceSexScale");
        if (!float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device scale is invalid.");
        model.Scale = Vector3.One * scale;
        viewport.AddChild(model);

        VerifiedGltfLoader.VerifyHash(
            contract.MaterialManifestPath,
            contract.MaterialManifestSha256);
        using var document = JsonDocument.Parse(
            File.ReadAllText(contract.MaterialManifestPath));
        var materialManifest = document.RootElement;
        if (materialManifest.GetProperty("schema").GetString() !=
                "opennv-static-material-manifest/v1")
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device material manifest changed.");
        var textures = RuntimeMaterialLoader.LoadTextures(
            materialManifest,
            configuration.Renderer);
        var materialBindings = RuntimeMaterialLoader.Apply(
            model,
            materialManifest.GetProperty("asset"),
            textures,
            configuration.Renderer,
            configuration.ContentCompiler.RetailGrass);
        var surfaces = Descendants<MeshInstance3D>(model)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(surface => (Mesh: mesh, Surface: surface)))
            .ToArray();
        if (surfaces.Length != contract.Surfaces || materialBindings != contract.Surfaces)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device surface/material coverage changed.");
        var screen = ResolveSurface(surfaces, contract.ScreenSurface, "screen");
        var creatorButton = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["hairButton"],
            "creator-button-source");
        var creatorGlow = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["hairGlow"],
            "creator-glow-source");
        var shell = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["deviceShell2"],
            "creator-extension-shell-source");
        if (creatorButton.Mesh != screen.Mesh || creatorGlow.Mesh != screen.Mesh ||
            shell.Mesh != screen.Mesh)
            throw new InvalidOperationException(
                "Owned Reflectron creator-button source surfaces are not one modeled device.");
        _deviceMesh = screen.Mesh;
        _creatorButtonSurface = creatorButton.Surface;
        _creatorGlowSurface = creatorGlow.Surface;
        _shellSurface = shell.Surface;
        (FacePresentationRect, MenuPresentationRect) = DerivePresentationRects(
            screen.Mesh,
            screen.Surface,
            canvasSize);
        ScreenRoot.AddChild(new ColorRect
        {
            Name = "OwnedRaceSexFacePresentationBackground",
            Position = FacePresentationRect.Position,
            Size = FacePresentationRect.Size,
            Color = new Color(
                lighting.FogColor.R,
                lighting.FogColor.G,
                lighting.FogColor.B,
                1.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        screen.Mesh.SetSurfaceOverrideMaterial(
            screen.Surface,
            new StandardMaterial3D
            {
                ResourceName = "OpenNV_OwnedRaceSexDynamicScreen",
                AlbedoTexture = screenTexture,
                AlbedoTextureForceSrgb = false,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
        foreach (var role in new[] { "sexGlow", "raceGlow", "faceGlow", "hairGlow" })
        {
            var surface = ResolveSurface(surfaces, contract.SurfaceRoles[role], role);
            var active = surface.Mesh.GetSurfaceOverrideMaterial(surface.Surface)
                ?? throw new InvalidOperationException(
                    $"Owned RaceSex rendered-device glow has no material: {role}");
            _glows.Add(
                role,
                new RenderedDeviceGlow(
                    surface.Mesh,
                    surface.Surface,
                    active,
                    new StandardMaterial3D
                    {
                        ResourceName = $"OpenNV_Inactive_{role}",
                        AlbedoColor = Colors.Transparent,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                    }));
        }

        var frame = DeriveFrame(model, screen.Mesh, screen.Surface);
        _deviceFrame = frame;
        RuntimeMaterialLoader.ApplyRetailAmbientDirectionalMenuLighting(
            model,
            lighting.AmbientColor);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = lighting.AmbientColor,
                AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
                TonemapMode = RuntimeRendering.ParseToneMapper(
                    configuration.Renderer.ToneMapper),
            },
        });
        var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
            lighting.DirectionalRotationDegrees.X,
            lighting.DirectionalRotationDegrees.Y);
        viewport.AddChild(new DirectionalLight3D
        {
            Name = "OwnedRaceSexDirectional",
            Basis = RetailLighting.DirectionalLightBasis(surfaceToLight),
            LightColor = lighting.DirectionalColor,
            LightEnergy = lighting.DirectionalFade *
                configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = configuration.ActorReview.DirectionalShadows,
        });
        var screenLightIntensity = _source.Float("screenLightBaseIntensity");
        var screenLightRadius = _source.Float("screenLightRadius");
        var screenLightColor = new Color(
            _source.Float("screenLightColorRed"),
            _source.Float("screenLightColorGreen"),
            _source.Float("screenLightColorBlue"));
        if (!float.IsFinite(screenLightIntensity) || screenLightIntensity <= 0.0f ||
            !float.IsFinite(screenLightRadius) || screenLightRadius <= 0.0f ||
            screenLightColor.R is < 0.0f or > 1.0f ||
            screenLightColor.G is < 0.0f or > 1.0f ||
            screenLightColor.B is < 0.0f or > 1.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device screen-light settings are invalid.");
        viewport.AddChild(new OmniLight3D
        {
            Name = "OwnedRaceSexScreenLight",
            Position = frame.Center,
            LightColor = screenLightColor,
            LightEnergy = screenLightIntensity,
            OmniRange = screenLightRadius,
            ShadowEnabled = false,
        });
        var fovHalfTangent = _source.Float("terminalFov");
        var ownedZoom = _source.Float("raceSexZoom");
        var zoom = (float)_source.Framing.SolvedZoomGameUnits;
        if (!float.IsFinite(fovHalfTangent) || fovHalfTangent <= 0.0f ||
            !float.IsFinite(ownedZoom) ||
            Math.Abs(ownedZoom - _source.Framing.CurrentZoomGameUnits) > 1.0e-5 ||
            !float.IsFinite(zoom) || zoom <= frame.Radius)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device camera settings are invalid.");
        var fovRadians = 2.0f * Mathf.Atan(fovHalfTangent);
        if (!float.IsFinite(fovRadians) || fovRadians >= Mathf.Pi)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device projection is invalid.");
        var target = frame.Center +
            frame.Right * _source.Float("raceSexHorizontalPosition") +
            frame.Up * _source.Float("raceSexVerticalPosition");
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = Mathf.RadToDeg(fovRadians),
            Position = target + frame.Normal * zoom,
            Near = zoom - frame.Radius,
            Far = zoom + frame.Radius,
            Current = true,
            KeepAspect = Camera3D.KeepAspectEnum.Width,
        };
        viewport.AddChild(camera);
        _deviceCamera = camera;
        camera.LookAt(target, frame.Up);
        SetActiveList("sex");
        GD.Print(
            "OPENNV_NEW_GAME_RACESEX_DEVICE_READY " +
            $"source={contract.LogicalPath} surfaces={contract.Surfaces} " +
            $"vertices={contract.Vertices} textures={contract.Textures} " +
            $"screen={contract.ScreenSurface} scale={scale:R} " +
            $"fovHalfTangent={fovHalfTangent:R} fovDegrees={Mathf.RadToDeg(fovRadians):R} " +
            $"front={frame.Normal} ownedZoom={ownedZoom:R} solvedZoom={zoom:R} " +
            $"framingStatus={_source.Framing.Status} " +
            $"projectionScale={_source.Framing.ProjectionScale:R} " +
            $"retailFrameSha256={_source.Framing.RetailFrameSha256} " +
            $"currentFrameSha256={_source.Framing.CurrentFrameSha256} " +
            $"screenLightIntensity={screenLightIntensity:R} " +
            $"screenLightRadius={screenLightRadius:R} " +
            $"screenLightColor={screenLightColor}");
    }

    internal void ConfigureCreatorModeControls(
        OwnedBitmapFont sourceFont,
        Action showThreeDimensional,
        Action showTwoDimensional,
        Action toggleBody)
    {
        if (_deviceMesh is null || _deviceCamera is null || _deviceFrame is not { } frame ||
            _creatorButtonSurface < 0 || _creatorGlowSurface < 0 || _shellSurface < 0 ||
            _creatorModeHitTargets.Count != 0)
            throw new InvalidOperationException(
                "Reflectron creator-mode physical controls are not in a buildable state.");
        var sourceMesh = _deviceMesh;
        var sourceButtonCenter = SurfaceCenter(sourceMesh, _creatorButtonSurface);
        var sourceButtonMaterial = sourceMesh.GetSurfaceOverrideMaterial(_creatorButtonSurface)
            ?? throw new InvalidOperationException(
                "Reflectron creator-mode source button has no owned material.");
        var sourceGlowMaterial = sourceMesh.GetSurfaceOverrideMaterial(_creatorGlowSurface)
            ?? throw new InvalidOperationException(
                "Reflectron creator-mode source glow has no owned material.");
        var shellMaterial = sourceMesh.GetSurfaceOverrideMaterial(_shellSurface)
            ?? throw new InvalidOperationException(
                "Reflectron creator extension has no owned shell material.");
        var inactive = new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronCreatorModeInactive",
            AlbedoColor = new Color(0.08f, 0.015f, 0.01f, 0.86f),
            EmissionEnabled = true,
            Emission = new Color(0.08f, 0.01f, 0.005f),
            EmissionEnergyMultiplier = 0.35f,
            Roughness = 0.58f,
            Metallic = 0.32f,
        };
        var active = new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronCreatorModeActive",
            AlbedoColor = new Color(0.42f, 0.045f, 0.025f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.06f, 0.025f),
            EmissionEnergyMultiplier = 2.8f,
            Roughness = 0.42f,
            Metallic = 0.18f,
        };
        var transparent = new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronCreatorModeHiddenSurface",
            AlbedoColor = Colors.Transparent,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };

        var plateCenter = frame.Center - frame.Up * (frame.Radius * 0.69f) +
            frame.Normal * (frame.Radius * 0.055f);
        var plate = new MeshInstance3D
        {
            Name = "ReflectronCreatorModeExtensionPlate",
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    frame.Radius * 0.66f,
                    frame.Radius * 0.18f,
                    frame.Radius * 0.045f),
            },
            Transform = new Transform3D(
                new Basis(frame.Right, frame.Up, frame.Normal),
                plateCenter),
        };
        plate.SetSurfaceOverrideMaterial(0, shellMaterial);
        DeviceViewport.AddChild(plate);

        var font = OwnedUiTheme.BuildFont(sourceFont);
        var labels = new[] { "3D", "2D", "BODY" };
        var actions = new Action[]
        {
            showThreeDimensional,
            showTwoDimensional,
            toggleBody,
        };
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            var horizontal = (index - 1) * frame.Radius * 0.215f;
            var target = plateCenter + frame.Right * horizontal +
                frame.Up * (frame.Radius * 0.005f) +
                frame.Normal * (frame.Radius * 0.038f);
            var offset = target - sourceButtonCenter;
            var buttonMesh = CloneSingleSurface(
                sourceMesh,
                _creatorButtonSurface,
                transparent,
                $"ReflectronCreatorModeButton_{label}",
                offset);
            buttonMesh.SetSurfaceOverrideMaterial(_creatorButtonSurface, sourceButtonMaterial);
            DeviceViewport.AddChild(buttonMesh);
            var glowMesh = CloneSingleSurface(
                sourceMesh,
                _creatorGlowSurface,
                transparent,
                $"ReflectronCreatorModeGlow_{label}",
                offset);
            glowMesh.SetSurfaceOverrideMaterial(
                _creatorGlowSurface,
                index == 0 ? active : inactive);
            DeviceViewport.AddChild(glowMesh);
            _glows.Add(
                $"creator-{label}",
                new RenderedDeviceGlow(
                    glowMesh,
                    _creatorGlowSurface,
                    active,
                    inactive));
            AddCreatorButtonLabel(
                sourceFont,
                font,
                label,
                target - frame.Up * (frame.Radius * 0.085f) +
                    frame.Normal * (frame.Radius * 0.015f),
                frame);
            AddCreatorModeHitTarget(label, target, actions[index], frame);
        }
        SetCreatorModeState("3D", bodyEnabled: false);
        GD.Print(
            "OPENNV_REFLECTRON_CREATOR_MODE_CONTROLS_READY " +
            "buttons=3 modeled=true texturedLabels=true labels=3D,2D,BODY " +
            "authority=owned-button-geometry-and-material-derived-first-party-extension");
    }

    internal void SetCreatorModeState(string previewMode, bool bodyEnabled)
    {
        if (_creatorModeHitTargets.Count == 0)
            return;
        foreach (var label in new[] { "3D", "2D", "BODY" })
        {
            var selected = label == "BODY"
                ? bodyEnabled
                : label.Equals(previewMode, StringComparison.OrdinalIgnoreCase);
            var glow = _glows[$"creator-{label}"];
            glow.Mesh.SetSurfaceOverrideMaterial(
                glow.Surface,
                selected ? glow.Active : glow.Inactive);
            _creatorModeHitTargets[label].ButtonPressed = selected;
        }
    }

    private MeshInstance3D CloneSingleSurface(
        MeshInstance3D source,
        int visibleSurface,
        Material transparent,
        string name,
        Vector3 offset)
    {
        var clone = new MeshInstance3D
        {
            Name = name,
            Mesh = source.Mesh,
            Transform = source.GlobalTransform.Translated(offset),
        };
        for (var surface = 0; surface < (source.Mesh?.GetSurfaceCount() ?? 0); surface++)
            clone.SetSurfaceOverrideMaterial(
                surface,
                surface == visibleSurface
                    ? source.GetSurfaceOverrideMaterial(surface) ?? transparent
                    : transparent);
        return clone;
    }

    private void AddCreatorButtonLabel(
        OwnedBitmapFont sourceFont,
        FontFile font,
        string text,
        Vector3 center,
        RenderedDeviceFrame frame)
    {
        var labelViewport = new SubViewport
        {
            Name = $"ReflectronCreatorModeLabelTexture_{text}",
            Size = new Vector2I(256, 64),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        _host.AddChild(labelViewport);
        var label = new Label
        {
            Text = text,
            Position = Vector2.Zero,
            Size = new Vector2(256, 64),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", font.FixedSize);
        label.AddThemeColorOverride("font_color", new Color(0.68f, 1.0f, 0.48f));
        labelViewport.AddChild(label);
        var labelPlane = new MeshInstance3D
        {
            Name = $"ReflectronCreatorModeLabelSurface_{text}",
            Mesh = new QuadMesh
            {
                Size = new Vector2(frame.Radius * 0.16f, frame.Radius * 0.045f),
            },
            Transform = new Transform3D(
                new Basis(frame.Right, frame.Up, frame.Normal),
                center),
        };
        labelPlane.SetSurfaceOverrideMaterial(
            0,
            new StandardMaterial3D
            {
                ResourceName = $"OpenNV_ReflectronCreatorModeLabel_{text}",
                AlbedoTexture = labelViewport.GetTexture(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
        DeviceViewport.AddChild(labelPlane);
    }

    private void AddCreatorModeHitTarget(
        string label,
        Vector3 worldCenter,
        Action action,
        RenderedDeviceFrame frame)
    {
        var camera = _deviceCamera ?? throw new InvalidOperationException(
            "Reflectron creator-mode hit testing has no device camera.");
        var center = camera.UnprojectPosition(worldCenter) +
            _source.Framing.Alignment.DeviceTranslationCanvasUnits;
        var edge = camera.UnprojectPosition(
            worldCenter + frame.Right * (frame.Radius * 0.055f)) +
            _source.Framing.Alignment.DeviceTranslationCanvasUnits;
        var radius = MathF.Max(18.0f, center.DistanceTo(edge));
        if (!center.IsFinite() || !float.IsFinite(radius) ||
            center.X < -radius || center.Y < -radius ||
            center.X > _canvasSize.X + radius || center.Y > _canvasSize.Y + radius)
            throw new InvalidOperationException(
                $"Reflectron creator-mode button projects outside its canvas: {label}.");
        var button = new Button
        {
            Name = $"ReflectronCreatorModeHitTarget_{label}",
            Position = center - Vector2.One * radius,
            Size = Vector2.One * radius * 2.0f,
            Flat = true,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = label,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            button.AddThemeStyleboxOverride(state, empty);
        button.Pressed += action;
        _host.AddChild(button);
        _creatorModeHitTargets.Add(label, button);
    }

    private static Vector3 SurfaceCenter(MeshInstance3D mesh, int surface)
    {
        var arrays = mesh.Mesh?.SurfaceGetArrays(surface) ?? throw new InvalidOperationException(
            "Reflectron creator-mode source surface has no mesh arrays.");
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (vertices.Length == 0)
            throw new InvalidOperationException(
                "Reflectron creator-mode source surface has no vertices.");
        var bounds = new Aabb(vertices[0], Vector3.Zero);
        foreach (var vertex in vertices.Skip(1))
            bounds = bounds.Expand(vertex);
        return mesh.GlobalTransform * bounds.GetCenter();
    }

    internal Control CreateFacePresentationHost()
    {
        if (FacePresentationRect.Size.X <= 0.0f ||
            FacePresentationRect.Size.Y <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex face-presentation UV region is unavailable.");
        var result = new Control
        {
            Name = "OwnedRaceSexFacePresentation",
            Position = FacePresentationRect.Position,
            Size = FacePresentationRect.Size,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ScreenRoot.AddChild(result);
        return result;
    }

    internal Control CreateMenuPresentationHost(Rect2 sourcePanel)
    {
        if (MenuPresentationRect.Size.X <= 0.0f ||
            MenuPresentationRect.Size.Y <= 0.0f ||
            sourcePanel.Size.X <= 0.0f ||
            sourcePanel.Size.Y <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex menu-presentation region is unavailable.");
        var clip = new Control
        {
            Name = "OwnedRaceSexMenuPresentationClip",
            Position = MenuPresentationRect.Position,
            Size = MenuPresentationRect.Size,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        ScreenRoot.AddChild(clip);
        var projectedScreen = _source.Framing.Alignment
            .ProjectedCurrentRightScreenBoundsPixels;
        var correction = new Control
        {
            Name = "OwnedRaceSexMenuEvidenceAlignment",
            Position = _source.Framing.Alignment.ContentTranslationWithinScreenPixels *
                (MenuPresentationRect.Size / projectedScreen.Size),
            Size = MenuPresentationRect.Size,
            Scale = _source.Framing.Alignment.ContentScale,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        clip.AddChild(correction);
        var scale = MenuPresentationRect.Size / sourcePanel.Size;
        var result = new Control
        {
            Name = "OwnedRaceSexMenuPresentation",
            Position = -sourcePanel.Position * scale,
            Size = ScreenRoot.Size,
            Scale = scale,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        correction.AddChild(result);
        return result;
    }

    private static (Rect2 Face, Rect2 Menu) DerivePresentationRects(
        MeshInstance3D screenMesh,
        int screenSurface,
        Vector2 canvasSize)
    {
        var mesh = screenMesh.Mesh
            ?? throw new InvalidOperationException(
                "Owned RaceSex dynamic screen has no mesh.");
        var arrays = mesh.SurfaceGetArrays(screenSurface);
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (uvs.Length < 3 || indices.Length < 3 || indices.Length % 3 != 0 ||
            indices.Any(value => value < 0 || value >= uvs.Length))
            throw new InvalidOperationException(
                "Owned RaceSex dynamic screen UV topology is incomplete.");
        var parents = Enumerable.Range(0, uvs.Length).ToArray();
        int Find(int value)
        {
            while (parents[value] != value)
            {
                parents[value] = parents[parents[value]];
                value = parents[value];
            }
            return value;
        }
        void Union(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left != right)
                parents[right] = left;
        }
        for (var offset = 0; offset < indices.Length; offset += 3)
        {
            Union(indices[offset], indices[offset + 1]);
            Union(indices[offset], indices[offset + 2]);
        }
        var components = indices.Distinct()
            .GroupBy(Find)
            .Select(group => group.Select(index => uvs[index]).ToArray())
            .ToArray();
        if (components.Length != 2)
            throw new InvalidOperationException(
                "Owned RaceSex dynamic screen no longer has two UV islands.");
        var presentationRects = components
            .Select(values =>
            {
                var minimum = new Vector2(
                    values.Min(value => value.X),
                    values.Min(value => value.Y));
                var maximum = new Vector2(
                    values.Max(value => value.X),
                    values.Max(value => value.Y));
                return new Rect2(
                    minimum * canvasSize,
                    (maximum - minimum) * canvasSize);
            })
            .OrderBy(value => value.Position.X)
            .ToArray();
        if (presentationRects.Any(value =>
                value.Size.X <= 0.0f || value.Size.Y <= 0.0f))
            throw new InvalidOperationException(
                "Owned RaceSex presentation UV bounds are invalid.");
        return (presentationRects[0], presentationRects[1]);
    }

    private static RenderedDeviceFrame DeriveFrame(
        Node3D model,
        MeshInstance3D screenMesh,
        int screenSurface)
    {
        var mesh = screenMesh.Mesh
            ?? throw new InvalidOperationException(
                "Owned RaceSex rendered-device screen has no mesh.");
        var arrays = mesh.SurfaceGetArrays(screenSurface);
        var localVertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var localNormals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        if (localVertices.Length < 3 ||
            localNormals.Length != localVertices.Length ||
            uvs.Length != localVertices.Length)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device screen lacks position/normal/UV evidence.");
        var vertices = localVertices.Select(screenMesh.ToGlobal).ToArray();
        var center = vertices.Aggregate(Vector3.Zero, (sum, value) => sum + value) /
            vertices.Length;
        var meanUv = uvs.Aggregate(Vector2.Zero, (sum, value) => sum + value) /
            uvs.Length;
        var normal = localNormals
            .Select(value => (screenMesh.GlobalBasis * value).Normalized())
            .Aggregate(Vector3.Zero, (sum, value) => sum + value)
            .Normalized();
        var uAxis = Vector3.Zero;
        var vAxis = Vector3.Zero;
        foreach (var index in Enumerable.Range(0, vertices.Length))
        {
            uAxis += (vertices[index] - center) * (uvs[index].X - meanUv.X);
            vAxis += (vertices[index] - center) * (uvs[index].Y - meanUv.Y);
        }
        uAxis -= normal * uAxis.Dot(normal);
        vAxis -= normal * vAxis.Dot(normal);
        if (normal.LengthSquared() <= 0.0f ||
            uAxis.LengthSquared() <= 0.0f ||
            vAxis.LengthSquared() <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device frame cannot be derived.");
        var right = uAxis.Normalized();
        var points = Descendants<MeshInstance3D>(model)
            .SelectMany(value =>
            {
                var bounds = value.GetAabb();
                return new[]
                {
                    new Vector3(bounds.Position.X, bounds.Position.Y, bounds.Position.Z),
                    new Vector3(bounds.End.X, bounds.Position.Y, bounds.Position.Z),
                    new Vector3(bounds.Position.X, bounds.End.Y, bounds.Position.Z),
                    new Vector3(bounds.End.X, bounds.End.Y, bounds.Position.Z),
                    new Vector3(bounds.Position.X, bounds.Position.Y, bounds.End.Z),
                    new Vector3(bounds.End.X, bounds.Position.Y, bounds.End.Z),
                    new Vector3(bounds.Position.X, bounds.End.Y, bounds.End.Z),
                    bounds.End,
                }.Select(value.ToGlobal);
            })
            .ToArray();
        if (points.Length == 0)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device model has no bounds.");
        var bounds = new Aabb(points[0], Vector3.Zero);
        foreach (var point in points.Skip(1))
            bounds = bounds.Expand(point);
        var outward = center - bounds.GetCenter();
        outward -= right * outward.Dot(right);
        if (outward.LengthSquared() <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device front side is ambiguous.");
        if (normal.Dot(outward) < 0.0f)
            normal = -normal;
        var up = -vAxis;
        up -= normal * up.Dot(normal);
        up -= right * up.Dot(right);
        if (up.LengthSquared() <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device vertical axis is ambiguous.");
        up = up.Normalized();
        var radius = points.Max(point => center.DistanceTo(point));
        if (!float.IsFinite(radius) || radius <= 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device radius is invalid.");
        return new RenderedDeviceFrame(center, normal, right, up, radius);
    }

    private static (MeshInstance3D Mesh, int Surface) ResolveSurface(
        IReadOnlyList<(MeshInstance3D Mesh, int Surface)> surfaces,
        string identity,
        string role)
    {
        var matches = surfaces.Where(surface =>
                RuntimeMaterialLoader.SourceSurfaceIdentity(
                        surface.Mesh,
                        surface.Surface)
                    ?.Equals(identity, StringComparison.Ordinal) == true)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Owned RaceSex rendered-device surface is ambiguous: " +
                $"{role}={identity} matches={matches.Length}");
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private readonly record struct RenderedDeviceFrame(
        Vector3 Center,
        Vector3 Normal,
        Vector3 Right,
        Vector3 Up,
        float Radius);

    private readonly record struct RenderedDeviceGlow(
        MeshInstance3D Mesh,
        int Surface,
        Material Active,
        Material Inactive);
}
