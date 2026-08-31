using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;

using OpenNV.Runtime.SceneGraph;

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
    private readonly Dictionary<string, Button> _sourceSectionHitTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private MeshInstance3D? _deviceMesh;
    private Camera3D? _deviceCamera;
    private RenderedDeviceFrame? _deviceFrame;
    private int _creatorButtonSurface = -1;
    private int _creatorGlowSurface = -1;
    private int _sourceSexButtonSurface = -1;
    private int _creatorBodyAlignmentSurface = -1;
    private int _creatorFaceAlignmentSurface = -1;
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
        foreach (var (candidate, button) in _sourceSectionHitTargets)
            button.ButtonPressed = candidate.Equals(
                activeList,
                StringComparison.OrdinalIgnoreCase);
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
        var surfaces = NodeTraversal.Descendants<MeshInstance3D>(model)
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
        var sourceSexButton = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["sexButton"],
            "creator-sex-alignment-source");
        var creatorBodyAlignment = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["raceButton"],
            "creator-body-alignment-source");
        var creatorFaceAlignment = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["faceButton"],
            "creator-face-alignment-source");
        var shell = ResolveSurface(
            surfaces,
            contract.SurfaceRoles["deviceShell2"],
            "creator-extension-shell-source");
        if (creatorButton.Mesh != screen.Mesh || creatorGlow.Mesh != screen.Mesh ||
            sourceSexButton.Mesh != screen.Mesh ||
            creatorBodyAlignment.Mesh != screen.Mesh ||
            creatorFaceAlignment.Mesh != screen.Mesh || shell.Mesh != screen.Mesh)
            throw new InvalidOperationException(
                "Owned Reflectron creator-button source surfaces are not one modeled device.");
        _deviceMesh = screen.Mesh;
        _creatorButtonSurface = creatorButton.Surface;
        _creatorGlowSurface = creatorGlow.Surface;
        _sourceSexButtonSurface = sourceSexButton.Surface;
        _creatorBodyAlignmentSurface = creatorBodyAlignment.Surface;
        _creatorFaceAlignmentSurface = creatorFaceAlignment.Surface;
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
        camera.Transform = new Transform3D(Basis.Identity, camera.Position)
            .LookingAt(target, frame.Up);
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

    internal void ConfigureCharacterControls(
        OwnedBitmapFont sourceFont,
        Action showSex,
        Action showRace,
        Action showFace,
        Action showHair,
        Action showPortrait,
        Action toggleBody,
        Action toggleProjection)
    {
        if (_deviceMesh is null || _deviceCamera is null || _deviceFrame is not { } frame ||
            _creatorButtonSurface < 0 || _creatorGlowSurface < 0 ||
            _sourceSexButtonSurface < 0 ||
            _creatorBodyAlignmentSurface < 0 || _creatorFaceAlignmentSurface < 0 ||
            _shellSurface < 0 ||
            _creatorModeHitTargets.Count != 0 || _sourceSectionHitTargets.Count != 0 ||
            showSex is null || showRace is null || showFace is null || showHair is null ||
            showPortrait is null || toggleBody is null || toggleProjection is null)
            throw new InvalidOperationException(
                "Reflectron 2.0 character controls are not in a buildable state.");
        var sourceMesh = _deviceMesh;
        var sourceButtonMaterial = sourceMesh.GetSurfaceOverrideMaterial(_creatorButtonSurface)
            ?? throw new InvalidOperationException(
                "Reflectron 2.0 has no owned button material.");
        var sourceGlowMaterial = _glows["hairGlow"].Active;
        var shellMaterial = sourceMesh.GetSurfaceOverrideMaterial(_shellSurface)
            ?? throw new InvalidOperationException(
                "Reflectron 2.0 has no owned embossed-shell material.");
        var shellEmbossedMetal = SourceDerivedEmbossedMetal(shellMaterial);
        var raisedButtonMaterial = SourceAlphaMaskedRaisedButton(
            sourceButtonMaterial,
            shellMaterial);
        var transparent = new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronTwoPointZeroInactiveOwnedGlow",
            AlbedoColor = Colors.Transparent,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        var projectionInactive = SourceMaskedGlow(
            sourceGlowMaterial,
            "OpenNV_ReflectronTwoPointZeroProjectionGreenInactive",
            new Color(0.025f, 0.48f, 0.075f, 0.88f),
            0.85f);
        var projectionActive = SourceMaskedGlow(
            sourceGlowMaterial,
            "OpenNV_ReflectronTwoPointZeroProjectionGreenActive",
            new Color(0.035f, 0.96f, 0.14f, 1.0f),
            2.4f);
        var creatorInactive = SourceMaskedGlow(
            sourceGlowMaterial,
            "OpenNV_ReflectronTwoPointZeroCreatorRedInactive",
            new Color(0.58f, 0.035f, 0.025f, 0.92f),
            0.9f);
        var creatorActive = SourceMaskedGlow(
            sourceGlowMaterial,
            "OpenNV_ReflectronTwoPointZeroCreatorRedActive",
            new Color(1.0f, 0.06f, 0.035f, 1.0f),
            2.4f);
        const float creatorButtonScale = 0.50f;
        var sourceRowCenter = SurfaceCenter(sourceMesh, _creatorButtonSurface);
        var bodyHorizontalOffset =
            (SurfaceCenter(sourceMesh, _creatorBodyAlignmentSurface) - sourceRowCenter)
            .Dot(frame.Right);
        var faceHorizontalOffset =
            (SurfaceCenter(sourceMesh, _creatorFaceAlignmentSurface) - sourceRowCenter)
            .Dot(frame.Right);
        var bodyCenter = sourceRowCenter + frame.Right * bodyHorizontalOffset -
            frame.Up * (frame.Radius * 0.061f) +
            frame.Normal * (frame.Radius * 0.040f);
        var faceCenter = sourceRowCenter + frame.Right * faceHorizontalOffset -
            frame.Up * (frame.Radius * 0.061f) +
            frame.Normal * (frame.Radius * 0.040f);
        var lowerRowUp = ((bodyCenter + faceCenter) * 0.5f - frame.Center).Dot(frame.Up);
        var projectionCenter = frame.Center + frame.Right * (frame.Radius * 0.22f) +
            frame.Up * (lowerRowUp + frame.Radius * 0.012f) +
            frame.Normal * (frame.Radius * 0.040f);

        MeshInstance3D CloneSourceSurface(
            int surface,
            string name,
            Vector3 center,
            Material material,
            float scale)
        {
            var clone = CloneSingleSurface(
                sourceMesh,
                surface,
                transparent,
                name,
                center - SurfaceCenter(sourceMesh, surface));
            clone.SetSurfaceOverrideMaterial(surface, material);
            clone.Scale *= scale;
            clone.Position += center - SurfaceCenter(clone, surface);
            DeviceViewport.AddChild(clone);
            return clone;
        }

        _ = CloneSourceSurface(
            _creatorButtonSurface,
            "ReflectronTwoPointZeroOwnedBodyButton",
            bodyCenter,
            raisedButtonMaterial,
            creatorButtonScale);
        var bodyGlow = CloneSourceSurface(
            _creatorGlowSurface,
            "ReflectronTwoPointZeroOwnedBodyGlow",
            bodyCenter,
            transparent,
            creatorButtonScale);
        _glows.Add(
            "creator-BODY",
            new RenderedDeviceGlow(
                bodyGlow,
                _creatorGlowSurface,
                creatorActive,
                creatorInactive));

        _ = CloneSourceSurface(
            _creatorButtonSurface,
            "ReflectronTwoPointZeroOwnedFaceButton",
            faceCenter,
            raisedButtonMaterial,
            creatorButtonScale);
        var faceGlow = CloneSourceSurface(
            _creatorGlowSurface,
            "ReflectronTwoPointZeroOwnedFaceGlow",
            faceCenter,
            transparent,
            creatorButtonScale);
        _glows.Add(
            "creator-FACE",
            new RenderedDeviceGlow(
                faceGlow,
                _creatorGlowSurface,
                creatorActive,
                creatorInactive));

        _ = CloneSourceSurface(
            _creatorButtonSurface,
            "ReflectronTwoPointZeroOwnedProjectionButton",
            projectionCenter,
            raisedButtonMaterial,
            creatorButtonScale);
        var projectionGlow = CloneSourceSurface(
            _creatorGlowSurface,
            "ReflectronTwoPointZeroOwnedProjectionGlow",
            projectionCenter,
            projectionInactive,
            creatorButtonScale);
        _glows.Add(
            "creator-PROJECTION",
            new RenderedDeviceGlow(
                projectionGlow,
                _creatorGlowSurface,
                projectionActive,
                projectionInactive));

        AddEmbossedDeviceText(
            "BODY",
            bodyCenter - frame.Right * (frame.Radius * 0.030f) +
                frame.Normal * (frame.Radius * 0.0015f),
            frame,
            sourceFont,
            shellEmbossedMetal,
            pixelSize: frame.Radius * 0.00050f,
            depth: frame.Radius * 0.0018f);
        AddEmbossedDeviceText(
            "FACE",
            faceCenter + frame.Right * (frame.Radius * 0.030f) +
                frame.Normal * (frame.Radius * 0.0015f),
            frame,
            sourceFont,
            shellEmbossedMetal,
            pixelSize: frame.Radius * 0.00050f,
            depth: frame.Radius * 0.0018f);
        AddReflectronTwoPointZeroStamp(frame, sourceFont, shellEmbossedMetal);
        void BuildHitTargets()
        {
            AddSourceSectionHitTarget(
                "sex", _sourceSexButtonSurface, showSex, frame);
            AddSourceSectionHitTarget(
                "race", _creatorBodyAlignmentSurface, showRace, frame);
            AddSourceSectionHitTarget(
                "face", _creatorFaceAlignmentSurface, showFace, frame);
            AddSourceSectionHitTarget(
                "hair", _creatorButtonSurface, showHair, frame);
            AddCreatorModeHitTarget(
                "FACE", faceCenter, showPortrait, frame, creatorButtonScale);
            AddCreatorModeHitTarget(
                "BODY", bodyCenter, toggleBody, frame, creatorButtonScale);
            AddCreatorModeHitTarget(
                "PROJECTION",
                projectionCenter,
                toggleProjection,
                frame,
                creatorButtonScale);
            SetCreatorModeState(
                "BODY",
                bodyEnabled: false,
                projectionEnabled: false,
                faceEnabled: false);
        }
        if (_deviceCamera.IsInsideTree())
            BuildHitTargets();
        else
            _host.Ready += BuildHitTargets;
        GD.Print(
            "OPENNV_REFLECTRON_TWO_POINT_ZERO_READY " +
            "stamp=2.0 embossedSourceMaterial=true leftCreatorButtons=centered-lower-pair " +
            "bodyButtonEmbossed=true faceButtonEmbossed=true " +
            "projectionButton=owned-clone-green-straight-down-raised-from-left-row " +
            "creatorButtons=3 sourceSectionButtons=4 redCenters=visible " +
            $"raisedButtonMaterial=owned-alpha-masked-source buttonScale={creatorButtonScale:R} " +
            "authority=locally-exported-owned-device-plus-source-derived-code-layer");
    }

    internal void SetCreatorModeState(
        string previewMode,
        bool bodyEnabled,
        bool projectionEnabled = false,
        bool faceEnabled = false)
    {
        if (_creatorModeHitTargets.Count == 0)
            return;
        foreach (var label in _creatorModeHitTargets.Keys)
        {
            var selected = label switch
            {
                "FACE" => previewMode.Equals("FACE", StringComparison.OrdinalIgnoreCase),
                "BODY" => previewMode.Equals("BODY", StringComparison.OrdinalIgnoreCase),
                "PROJECTION" => projectionEnabled,
                _ => label.Equals(previewMode, StringComparison.OrdinalIgnoreCase),
            };
            var glow = _glows[$"creator-{label}"];
            glow.Mesh.SetSurfaceOverrideMaterial(
                glow.Surface,
                selected ? glow.Active : glow.Inactive);
            _creatorModeHitTargets[label].ButtonPressed = selected;
        }
    }

    internal void ActivateCreatorModeControl(string label)
    {
        if (!_creatorModeHitTargets.TryGetValue(label, out var button))
            throw new InvalidOperationException(
                $"Reflectron 2.0 creator control is unavailable: {label}");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    internal void ActivateSourceSectionControl(string label)
    {
        if (!_sourceSectionHitTargets.TryGetValue(label, out var button))
            throw new InvalidOperationException(
                $"Reflectron source section control is unavailable: {label}");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void AddReflectronTwoPointZeroStamp(
        RenderedDeviceFrame frame,
        OwnedBitmapFont sourceFont,
        Material sourceMetal)
    {
        var plaqueCenter = frame.Center + frame.Right * (frame.Radius * 0.255f) +
            frame.Up * (frame.Radius * 0.199f) +
            frame.Normal * (frame.Radius * 0.0025f);
        var recess = EmbossRecessMetal(sourceMetal);
        var shadow = AddEmbossedDeviceText(
            "2.0",
            plaqueCenter - frame.Right * (frame.Radius * 0.0007f) -
                frame.Up * (frame.Radius * 0.0007f),
            frame,
            sourceFont,
            recess,
            pixelSize: frame.Radius * 0.00078f,
            depth: frame.Radius * 0.0009f);
        shadow.Name = "ReflectronTwoPointZeroRecessedStampEdge";
        var stamp = AddEmbossedDeviceText(
            "2.0",
            plaqueCenter + frame.Normal * (frame.Radius * 0.0009f),
            frame,
            sourceFont,
            sourceMetal,
            pixelSize: frame.Radius * 0.00072f,
            depth: frame.Radius * 0.0013f);
        stamp.Name = "ReflectronTwoPointZeroRaisedMetalStamp";
        stamp.SetMeta("layer", "code-generated-not-distributed-retail-asset");
        stamp.SetMeta("stamp", "2.0");
        stamp.SetMeta("material_authority", "owned-reflectron-source-material");
    }

    private MultiMeshInstance3D AddEmbossedDeviceText(
        string text,
        Vector3 center,
        RenderedDeviceFrame frame,
        OwnedBitmapFont sourceFont,
        Material sourceMetal,
        float pixelSize,
        float depth)
    {
        var glyphs = sourceFont.Glyphs.ToDictionary(value => value.Codepoint);
        var atlas = Image.LoadFromFile(sourceFont.Atlas.Path);
        if (atlas is null || atlas.IsEmpty())
            throw new InvalidOperationException(
                "Reflectron embossed source-font atlas could not be decoded.");
        var pixels = new List<Vector2>();
        var cursor = 0.0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!glyphs.TryGetValue(rune.Value, out var glyph))
                throw new InvalidOperationException(
                    $"Reflectron embossed source font has no glyph: U+{rune.Value:X4}.");
            var originX = Mathf.RoundToInt(glyph.UvRect.Position.X);
            var originY = Mathf.RoundToInt(glyph.UvRect.Position.Y);
            var width = Mathf.RoundToInt(glyph.Size.X);
            var height = Mathf.RoundToInt(glyph.Size.Y);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sample = atlas.GetPixel(originX + x, originY + y);
                    if (MathF.Max(sample.R, MathF.Max(sample.G, sample.B)) < 0.28f)
                        continue;
                    pixels.Add(new Vector2(
                        cursor + glyph.HorizontalOffsetPixels + x,
                        glyph.VerticalBearingPixels - y));
                }
            }
            cursor += glyph.AdvancePixels;
        }
        if (pixels.Count == 0)
            throw new InvalidOperationException(
                "Reflectron embossed source-font text has no covered pixels.");
        var minimum = new Vector2(
            pixels.Min(value => value.X),
            pixels.Min(value => value.Y));
        var maximum = new Vector2(
            pixels.Max(value => value.X),
            pixels.Max(value => value.Y));
        var midpoint = (minimum + maximum) * 0.5f;
        var instances = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = pixels.Count,
        };
        var basis = new Basis(
            frame.Right * (pixelSize * 0.92f),
            frame.Up * (pixelSize * 0.92f),
            frame.Normal * depth);
        for (var index = 0; index < pixels.Count; index++)
        {
            var pixel = pixels[index] - midpoint;
            instances.SetInstanceTransform(
                index,
                new Transform3D(
                    basis,
                    center + frame.Right * (pixel.X * pixelSize) +
                        frame.Up * (pixel.Y * pixelSize)));
        }
        var result = new MultiMeshInstance3D
        {
            Name = $"ReflectronTwoPointZeroEmbossed_{text}",
            Multimesh = instances,
            MaterialOverride = sourceMetal,
        };
        DeviceViewport.AddChild(result);
        return result;
    }

    private static Material SourceDerivedEmbossedMetal(Material source)
    {
        if (source is StandardMaterial3D standard)
        {
            return new StandardMaterial3D
            {
                ResourceName = "OpenNV_ReflectronSourceDerivedEmbossedMetal",
                AlbedoColor = standard.AlbedoColor,
                Metallic = standard.Metallic,
                Roughness = standard.Roughness,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }
        return new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronSourceDerivedEmbossedMetalFallback",
            AlbedoColor = new Color(0.62f, 0.64f, 0.57f),
            Metallic = 0.72f,
            Roughness = 0.46f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static Material EmbossRecessMetal(Material source)
    {
        var color = source is StandardMaterial3D standard
            ? standard.AlbedoColor.Darkened(0.72f)
            : new Color(0.14f, 0.15f, 0.13f);
        return new StandardMaterial3D
        {
            ResourceName = "OpenNV_ReflectronSourceDerivedEmbossRecess",
            AlbedoColor = color,
            Metallic = 0.48f,
            Roughness = 0.68f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static Material SourceMaskedGlow(
        Material source,
        string name,
        Color tint,
        float emissionEnergy)
    {
        if (source is not StandardMaterial3D standard || standard.AlbedoTexture is null)
            throw new InvalidOperationException(
                "Reflectron source glow does not expose its owned shape texture.");
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
                uniform sampler2D source_glow : source_color, filter_linear_mipmap, repeat_disable;
                uniform vec4 tint : source_color;
                uniform float emission_energy;
                void fragment() {
                    vec4 sampled = texture(source_glow, UV);
                    float mask = max(sampled.r, max(sampled.g, sampled.b));
                    ALBEDO = tint.rgb;
                    EMISSION = tint.rgb * emission_energy;
                    ALPHA = mask * tint.a;
                }
                """,
        };
        var result = new ShaderMaterial
        {
            ResourceName = name,
            Shader = shader,
        };
        result.SetShaderParameter("source_glow", standard.AlbedoTexture);
        result.SetShaderParameter("tint", tint);
        result.SetShaderParameter("emission_energy", emissionEnergy);
        return result;
    }

    private static Material SourceAlphaMaskedRaisedButton(
        Material source,
        Material shell)
    {
        Texture2D? sourceTexture = source is StandardMaterial3D standard
            ? standard.AlbedoTexture
            : source is ShaderMaterial sourceShaderMaterial &&
                sourceShaderMaterial.GetShaderParameter("base_map").AsGodotObject()
                    is Texture2D baseMap
                ? baseMap
                : null;
        Texture2D? shellTexture = shell is StandardMaterial3D shellStandard
            ? shellStandard.AlbedoTexture
            : shell is ShaderMaterial shellShaderMaterial &&
                shellShaderMaterial.GetShaderParameter("base_map").AsGodotObject()
                    is Texture2D shellBaseMap
                ? shellBaseMap
                : null;
        if (sourceTexture is null || shellTexture is null)
            throw new InvalidOperationException(
                "Reflectron button and shell do not expose their owned blend textures.");
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_prepass_alpha;
                uniform sampler2D source_button : filter_linear_mipmap, repeat_disable;
                uniform sampler2D shell_metal : source_color, filter_linear_mipmap, repeat_disable;
                void fragment() {
                    vec4 sampled = texture(source_button, UV);
                    vec4 metal = texture(shell_metal, UV);
                    float brightness = max(sampled.r, max(sampled.g, sampled.b));
                    float dark_edge_mask = smoothstep(0.10, 0.24, brightness);
                    ALBEDO = metal.rgb;
                    ALPHA = sampled.a * dark_edge_mask;
                }
                """,
        };
        var result = new ShaderMaterial
        {
            ResourceName = "OpenNV_ReflectronRaisedButtonAlphaMask",
            Shader = shader,
        };
        result.SetShaderParameter("source_button", sourceTexture);
        result.SetShaderParameter("shell_metal", shellTexture);
        return result;
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
            Transform = LocalTreeTransform(source).Translated(offset),
        };
        for (var surface = 0; surface < (source.Mesh?.GetSurfaceCount() ?? 0); surface++)
            clone.SetSurfaceOverrideMaterial(
                surface,
                surface == visibleSurface
                    ? source.GetSurfaceOverrideMaterial(surface) ?? transparent
                    : transparent);
        return clone;
    }

    private void AddCreatorModeHitTarget(
        string label,
        Vector3 worldCenter,
        Action action,
        RenderedDeviceFrame frame,
        float visualScale)
    {
        var button = CreateDeviceHitTarget(
            $"ReflectronCreatorModeHitTarget_{label}",
            label,
            worldCenter,
            action,
            frame,
            visualScale);
        _creatorModeHitTargets.Add(label, button);
    }

    private void AddSourceSectionHitTarget(
        string section,
        int surface,
        Action action,
        RenderedDeviceFrame frame)
    {
        var button = CreateDeviceHitTarget(
            $"ReflectronSourceSectionHitTarget_{section}",
            section.ToUpperInvariant(),
            SurfaceCenter(
                _deviceMesh ?? throw new InvalidOperationException(
                    "Reflectron source section has no device mesh."),
                surface),
            () =>
            {
                action();
                SetActiveList(section);
            },
            frame,
            0.72f);
        _sourceSectionHitTargets.Add(section, button);
    }

    private Button CreateDeviceHitTarget(
        string name,
        string tooltip,
        Vector3 worldCenter,
        Action action,
        RenderedDeviceFrame frame,
        float visualScale)
    {
        var camera = _deviceCamera ?? throw new InvalidOperationException(
            "Reflectron device hit testing has no device camera.");
        var center = camera.UnprojectPosition(worldCenter) +
            _source.Framing.Alignment.DeviceTranslationCanvasUnits;
        var edge = camera.UnprojectPosition(
            worldCenter + frame.Right * (frame.Radius * 0.035f * visualScale)) +
            _source.Framing.Alignment.DeviceTranslationCanvasUnits;
        var radius = MathF.Max(18.0f, center.DistanceTo(edge));
        if (!center.IsFinite() || !float.IsFinite(radius) ||
            center.X < radius || center.Y < radius ||
            center.X > _canvasSize.X - radius || center.Y > _canvasSize.Y - radius)
            throw new InvalidOperationException(
                "Reflectron device button is not fully inside its canvas: " +
                $"{tooltip} center={center} radius={radius:R} world={worldCenter} " +
                $"frameCenter={frame.Center} frameRadius={frame.Radius:R} " +
                $"canvas={_canvasSize}.");
        var button = new Button
        {
            Name = name,
            Position = center - Vector2.One * radius,
            Size = Vector2.One * radius * 2.0f,
            Flat = true,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = tooltip,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            button.AddThemeStyleboxOverride(state, empty);
        foreach (var state in new[]
                 {
                     "font_color", "font_hover_color", "font_pressed_color",
                     "font_focus_color", "font_disabled_color", "icon_normal_color",
                     "icon_hover_color", "icon_pressed_color", "icon_focus_color",
                     "icon_disabled_color",
                 })
            button.AddThemeColorOverride(state, Colors.Transparent);
        button.Pressed += action;
        _host.AddChild(button);
        return button;
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
        return LocalTreeTransform(mesh) * bounds.GetCenter();
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
        var screenTransform = LocalTreeTransform(screenMesh);
        var vertices = localVertices
            .Select(value => screenTransform * value)
            .ToArray();
        var center = vertices.Aggregate(Vector3.Zero, (sum, value) => sum + value) /
            vertices.Length;
        var meanUv = uvs.Aggregate(Vector2.Zero, (sum, value) => sum + value) /
            uvs.Length;
        var normal = localNormals
            .Select(value => (screenTransform.Basis * value).Normalized())
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
        var points = NodeTraversal.Descendants<MeshInstance3D>(model)
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
                }.Select(point => LocalTreeTransform(value) * point);
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

    private static Transform3D LocalTreeTransform(Node3D node)
    {
        var result = node.Transform;
        for (var parent = node.GetParent() as Node3D;
             parent is not null;
             parent = parent.GetParent() as Node3D)
            result = parent.Transform * result;
        return result;
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
