using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed class OwnedGamebryoFaceGenPreviewHost
{
    private const float PreviewMinimumDimensionPixels = 160.0f;
    private const float LegacyAspectFitFrameMargin = 1.15f;
    private const float LegacyPreviewNearPlaneMeters = 0.01f;
    private const float LegacyPreviewFarPlaneMeters = 100.0f;
    private const float PreviewEnvironmentAmbientEnergy = 0.0f;
    private const float PreviewHalfExtentFactor = 0.5f;
    private const float PreviewMaximumFieldOfViewDegrees = 180.0f;
    private const float PreviewFaceFrameMargin = 1.035f;
    private const float PreviewBodyFrameMargin = 1.18f;
    private const string PreviewHeadRole = "head";
    private const string PreviewLeftEyeRole = "eye-left";
    private const string PreviewRightEyeRole = "eye-right";
    private const string PreviewHairRole = "hair";
    private const string PreviewHeadFramingDisposition =
        "owned-posed-head-eyes-hair-bounds-and-eye-pair-center-aspect-fit";
    private const string PreviewFullActorRaceSexFramingDisposition =
        "provisional-sibling-gamebryo-racesex-camera-public-fnv-contract-blocked";
    private const string PreviewLightingDisposition =
        "owned-interface-menu-player-diffuse-and-ambient-actor-lighting";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<MorphBinding>> _bindings;
    private readonly SubViewport _viewport;
    private readonly Node3D _actorRoot;
    private readonly Skeleton3D _bodySkeleton;
    private readonly Camera3D _camera;
    private readonly Aabb _faceBounds;
    private readonly Aabb _fullActorBounds;
    private readonly IReadOnlyList<MeshInstance3D> _actorMeshes;
    private readonly int _bodySurfaceCount;
    private readonly float _morphWeightScale;
    private readonly float _unitsToMeters;

    private OwnedGamebryoFaceGenPreviewHost(
        SubViewportContainer control,
        SubViewport viewport,
        Node3D actorRoot,
        Skeleton3D bodySkeleton,
        Camera3D camera,
        Aabb faceBounds,
        Aabb fullActorBounds,
        IReadOnlyList<MeshInstance3D> actorMeshes,
        IReadOnlyDictionary<string, IReadOnlyList<MorphBinding>> bindings,
        int bodySurfaceCount,
        string framingDisposition,
        float morphWeightScale,
        float unitsToMeters)
    {
        Control = control;
        _viewport = viewport;
        _actorRoot = actorRoot;
        _bodySkeleton = bodySkeleton;
        _camera = camera;
        _faceBounds = faceBounds;
        _fullActorBounds = fullActorBounds;
        _actorMeshes = actorMeshes;
        _bindings = bindings;
        _bodySurfaceCount = bodySurfaceCount;
        _morphWeightScale = morphWeightScale;
        _unitsToMeters = unitsToMeters;
        FramingDisposition = framingDisposition;
    }

    internal Control Control { get; }
    internal int BoundControlCount => _bindings.Count;
    internal int BoundSurfaceCount => _bindings.Values
        .Select(value => value.Count)
        .Distinct()
        .Single();
    internal int BodySurfaceCount => _bodySurfaceCount;
    internal string FramingDisposition { get; }
    internal string LightingDisposition => PreviewLightingDisposition;

    internal static OwnedGamebryoFaceGenPreviewHost Load(
        OpeningPlayerFaceGenPreview source,
        IReadOnlyList<OpeningNativeFaceGenGeometryControl> controls,
        OpeningFaceGenPreviewControl policy,
        Control parent,
        RuntimeConfiguration configuration,
        CellContentLoader.LightingContract lighting,
        float unitsToMeters,
        Vector2 availableSize,
        OwnedGamebryoFaceGenDeviceContract? renderedDevice = null)
    {
        VerifyHash(source.GltfPath, source.GltfSha256);
        VerifyHash(source.SidecarPath, source.SidecarSha256);
        if (controls.Count == 0 ||
            controls.Select(value => value.SettingEntity).Distinct(StringComparer.Ordinal).Count() !=
                controls.Count)
            throw new InvalidOperationException(
                "Player FaceGen preview control selection is invalid.");
        if (!float.IsFinite(policy.MorphWeightScale) || policy.MorphWeightScale <= 0.0f ||
            !float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException(
                "Player FaceGen preview scale contract is invalid.");
        if (!parent.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview owner is outside the SceneTree.");
        var width = Mathf.Max(PreviewMinimumDimensionPixels, availableSize.X);
        var height = Mathf.Max(PreviewMinimumDimensionPixels, availableSize.Y);
        var viewportContainer = new SubViewportContainer
        {
            Name = "OwnedPlayerFaceGenPreview",
            CustomMinimumSize = new Vector2(width, height),
            Stretch = true,
        };
        parent.AddChild(viewportContainer);
        viewportContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (!viewportContainer.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview viewport container did not enter the SceneTree.");
        var viewport = new SubViewport
        {
            Name = "OwnedPlayerFaceGenPreviewViewport",
            Size = new Vector2I(Mathf.RoundToInt(width), Mathf.RoundToInt(height)),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewportContainer.AddChild(viewport);
        var scene = new Node3D { Name = "OwnedPlayerFaceGenPreviewScene" };
        viewport.AddChild(scene);
        if (!scene.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview scene did not enter the SceneTree.");
        var actor = ActorModelSlice.Load(
            source.GltfPath,
            source.SidecarPath,
            scene,
            configuration,
            scaleToMeters: true,
            source.FullBody
                ? ActorModelSlice.BoundsContract.Humanoid
                : ActorModelSlice.BoundsContract.AnyActor);
        var menuDiffuse = renderedDevice is null
            ? lighting.DirectionalColor
            : renderedDevice.PlayerDiffuse;
        var menuAmbient = renderedDevice is null
            ? lighting.AmbientColor
            : renderedDevice.PlayerAmbient;
        var litMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
            actor.Root,
            menuAmbient,
            lighting.FogColor,
            lighting.FogNearGameUnits,
            lighting.FogFarGameUnits,
            lighting.FogPower,
            unitsToMeters);
        if (litMaterials == 0)
            throw new InvalidOperationException(
                "Player FaceGen preview has no source-lit actor materials.");
        var head = UniqueSurface(actor, PreviewHeadRole);
        var leftEye = UniqueSurface(actor, PreviewLeftEyeRole);
        var rightEye = UniqueSurface(actor, PreviewRightEyeRole);
        var hair = UniqueSurface(actor, PreviewHairRole);
        var headBounds = ActorModelSlice.PosedWorldBounds(actor, head);
        var leftEyeBounds = ActorModelSlice.PosedWorldBounds(actor, leftEye);
        var rightEyeBounds = ActorModelSlice.PosedWorldBounds(actor, rightEye);
        var hairBounds = ActorModelSlice.PosedWorldBounds(actor, hair);
        var headTarget = (leftEyeBounds.GetCenter() + rightEyeBounds.GetCenter()) / 2.0f;
        var headFramedBounds = headBounds.Merge(leftEyeBounds).Merge(rightEyeBounds)
            .Merge(hairBounds);
        var headOverlapsLeftEye = BoundsOverlap(headBounds, leftEyeBounds);
        var headOverlapsRightEye = BoundsOverlap(headBounds, rightEyeBounds);
        var headOverlapsHair = BoundsOverlap(headBounds, hairBounds);
        var targetInsideHeadRegion = headFramedBounds.HasPoint(headTarget);
        var headRegionSmallerThanActor = headFramedBounds.Size.Y < actor.Bounds.Size.Y;
        if (!headOverlapsLeftEye ||
            !headOverlapsRightEye ||
            !headOverlapsHair ||
            !targetInsideHeadRegion ||
            !headRegionSmallerThanActor)
            throw new InvalidOperationException(
                "Player FaceGen semantic head surfaces do not occupy one source-bound " +
                $"head region inside the assembled actor: head={headBounds} " +
                $"leftEye={leftEyeBounds} rightEye={rightEyeBounds} hair={hairBounds} " +
                $"actor={actor.Bounds} headOverlapsLeftEye={headOverlapsLeftEye} " +
                $"headOverlapsRightEye={headOverlapsRightEye} " +
                $"headOverlapsHair={headOverlapsHair} " +
                $"targetInsideHeadRegion={targetInsideHeadRegion} " +
                $"headRegionSmallerThanActor={headRegionSmallerThanActor}.");
        // RSM_Face_Grab is the owned RaceSex face viewport. FullBody governs
        // actor assembly/coverage; it does not turn that source rect into an
        // all-surfaces character-gallery camera.
        var framedBounds = headFramedBounds;
        var target = headTarget;
        var framingDisposition = source.FullBody
            ? PreviewFullActorRaceSexFramingDisposition
            : PreviewHeadFramingDisposition;
        var aspect = width / height;
        if (!target.IsFinite() || !framedBounds.Position.IsFinite() ||
            !framedBounds.Size.IsFinite() || !float.IsFinite(aspect) || aspect <= 0.0f)
            throw new InvalidOperationException(
                "Player FaceGen preview semantic source bounds are invalid.");
        var presentation = policy.Presentation;
        var camera = new Camera3D
        {
            Name = "OwnedPlayerFaceGenPreviewCamera",
            Current = true,
            KeepAspect = Camera3D.KeepAspectEnum.Width,
        };
        var cameraTarget = target;
        var distanceGameUnits = float.NaN;
        var verticalOffsetGameUnits = float.NaN;
        var yawRadians = float.NaN;
        if (renderedDevice is not null)
        {
            if (renderedDevice.CameraContractReady || renderedDevice.ParityReady)
                throw new InvalidOperationException(
                    "Ready FNV RaceSex preview-camera data requires an implemented exact contract join.");
            var zoom = presentation.StartingZoomFraction;
            verticalOffsetGameUnits = Mathf.Lerp(
                presentation.FullOutVerticalOffsetGameUnits,
                presentation.FullInVerticalOffsetGameUnits,
                zoom);
            distanceGameUnits = Mathf.Lerp(
                presentation.FullOutDistanceGameUnits,
                presentation.FullInDistanceGameUnits,
                zoom);
            yawRadians = Mathf.Lerp(
                presentation.FullOutYawRadians,
                presentation.FullInYawRadians,
                zoom);
            var nearMeters = renderedDevice.NearDistanceGameUnits * unitsToMeters;
            var farMeters = renderedDevice.FarDistanceGameUnits * unitsToMeters;
            var fovHalfTangent = renderedDevice.FovHalfTangent;
            var fovDegrees = Mathf.RadToDeg(2.0f * Mathf.Atan(fovHalfTangent));
            if (!float.IsFinite(verticalOffsetGameUnits) ||
                !float.IsFinite(distanceGameUnits) || distanceGameUnits <= 0.0f ||
                !float.IsFinite(yawRadians) ||
                !float.IsFinite(nearMeters) || nearMeters <= 0.0f ||
                !float.IsFinite(farMeters) || farMeters <= nearMeters ||
                !float.IsFinite(fovHalfTangent) || fovHalfTangent <= 0.0f ||
                !float.IsFinite(fovDegrees) || fovDegrees <= 0.0f ||
                fovDegrees >= PreviewMaximumFieldOfViewDegrees)
                throw new InvalidOperationException(
                    "Observed RaceSex camera or owned display frustum is invalid.");
            camera.Fov = fovDegrees;
            camera.Near = nearMeters;
            camera.Far = farMeters;
            cameraTarget += Vector3.Up * verticalOffsetGameUnits * unitsToMeters;
            camera.Position = cameraTarget +
                Vector3.Forward.Rotated(Vector3.Up, yawRadians) *
                distanceGameUnits * unitsToMeters;
        }
        else
        {
            var framedMinimum = framedBounds.Position;
            var framedMaximum = framedBounds.End;
            var horizontalHalfExtent = MathF.Max(
                MathF.Abs(target.X - framedMinimum.X),
                MathF.Abs(framedMaximum.X - target.X));
            var verticalHalfExtent = MathF.Max(
                MathF.Abs(target.Y - framedMinimum.Y),
                MathF.Abs(framedMaximum.Y - target.Y));
            var verticalHalfRadians = Mathf.DegToRad(camera.Fov) *
                presentation.VerticalFovHalfAngleFactor;
            var verticalTangent = MathF.Tan(verticalHalfRadians);
            var distanceMeters = MathF.Max(
                verticalHalfExtent / verticalTangent,
                horizontalHalfExtent / (verticalTangent * aspect)) *
                LegacyAspectFitFrameMargin;
            camera.Near = LegacyPreviewNearPlaneMeters;
            camera.Far = LegacyPreviewFarPlaneMeters;
            camera.Position = target + Vector3.Forward *
                (distanceMeters + framedBounds.Size.Z *
                    presentation.DepthExtentFraction);
            distanceGameUnits = distanceMeters / unitsToMeters;
            verticalOffsetGameUnits = 0.0f;
            yawRadians = 0.0f;
        }
        scene.AddChild(camera);
        camera.LookAt(cameraTarget, Vector3.Up);
        var environment = new WorldEnvironment
        {
            Name = "OwnedPlayerFaceGenPreviewEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(
                    lighting.FogColor.R,
                    lighting.FogColor.G,
                    lighting.FogColor.B,
                    1.0f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = menuAmbient,
                AmbientLightEnergy = PreviewEnvironmentAmbientEnergy,
            },
        };
        scene.AddChild(environment);
        var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
            lighting.DirectionalRotationDegrees.X,
            lighting.DirectionalRotationDegrees.Y);
        scene.AddChild(new DirectionalLight3D
        {
            Name = "OwnedPlayerFaceGenPreviewDirectional",
            Transform = new Transform3D(
                RetailLighting.DirectionalLightBasis(surfaceToLight),
                Vector3.Zero),
            LightColor = menuDiffuse,
            LightEnergy = configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = configuration.ActorReview.DirectionalShadows,
        });
        GD.Print(
            "OPENNV_NEW_GAME_FACEGEN_PRESENTATION " +
            $"framing={framingDisposition} target={target} " +
            $"headBounds={headBounds} leftEyeBounds={leftEyeBounds} " +
            $"rightEyeBounds={rightEyeBounds} hairBounds={hairBounds} " +
            $"bounds={framedBounds} actorBounds={actor.Bounds} aspect={aspect:R} " +
            $"cameraDistanceGameUnits={distanceGameUnits:R} " +
            $"cameraVerticalOffsetGameUnits={verticalOffsetGameUnits:R} " +
            $"cameraYawRadians={yawRadians:R} cameraFovDegrees={camera.Fov:R} " +
            $"cameraContractStatus={renderedDevice?.CameraContractStatus ?? "not-applicable"} " +
            $"cameraContractReady={renderedDevice?.CameraContractReady ?? false} " +
            $"parityReady={renderedDevice?.ParityReady ?? false} " +
            $"lighting={PreviewLightingDisposition} menuDiffuse={menuDiffuse} " +
            $"menuAmbient={menuAmbient}");

        var bodySources = source.BodyComponentSourcesBySex is not null &&
            source.BodyComponentSourcesBySex.TryGetValue(source.Sex, out var selectedBodySources)
                ? selectedBodySources
                : Array.Empty<OpeningPlayerBodyComponentSource>();
        var bodyRoles = bodySources.Select(value => value.Role)
            .ToHashSet(StringComparer.Ordinal);
        if (source.FullBody &&
            (bodyRoles.Count == 0 ||
             source.BodyComponentRoles is null ||
             !bodySources.Select(value => value.Role).SequenceEqual(
                 source.BodyComponentRoles,
                 StringComparer.Ordinal) ||
             bodySources.Any(value =>
                 actor.Surfaces.Count(surface => surface.Role == value.Role) !=
                    value.RetainedSurfaceCount)))
            throw new InvalidOperationException(
                "Player FaceGen full-body preview differs from its sex-keyed body contract.");
        if (!source.FullBody &&
            ((source.BodyComponentRoles?.Count ?? 0) != 0 || bodyRoles.Count != 0))
            throw new InvalidOperationException(
                "Head-only FaceGen preview unexpectedly declares body roles.");
        var morphSurfaces = actor.Surfaces.Where(surface =>
        {
            var targetCounts = controls.Select(control =>
                surface.FaceGenMorphTargets.Count(name =>
                    name == control.SettingEntity)).ToArray();
            var hasAny = targetCounts.Any(count => count != 0);
            if (hasAny && targetCounts.Any(count => count != 1))
                throw new InvalidOperationException(
                    "Player FaceGen preview surface has incomplete CTL/EGM targets: " +
                    $"{surface.Role}/{surface.Shape}.");
            if (bodyRoles.Contains(surface.Role) && hasAny)
                throw new InvalidOperationException(
                    "Player body surface unexpectedly owns FaceGen head targets: " +
                    $"{surface.Role}/{surface.Shape}.");
            if (!bodyRoles.Contains(surface.Role) && !hasAny)
                throw new InvalidOperationException(
                    "Player FaceGen surface has no native geometry targets: " +
                    $"{surface.Role}/{surface.Shape}.");
            return hasAny;
        }).ToArray();
        if (morphSurfaces.Length == 0)
            throw new InvalidOperationException(
                "Player FaceGen preview has no morph-controlled surfaces.");
        var bindings = controls.ToDictionary(
            control => control.SettingEntity,
            control => (IReadOnlyList<MorphBinding>)morphSurfaces.Select(surface =>
            {
                var matches = surface.FaceGenMorphTargets
                    .Select((name, index) => (name, index))
                    .Where(value => value.name == control.SettingEntity)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        "Owned player preview surface has no unique CTL/EGM target: " +
                        $"{surface.Role}/{surface.Shape}/{control.SettingEntity}.");
                return new MorphBinding(surface.Mesh, matches[0].index);
            }).ToArray(),
            StringComparer.Ordinal);
        if (bindings.Values.Any(value => value.Count != morphSurfaces.Length))
            throw new InvalidOperationException(
                "Owned player preview CTL/EGM binding coverage is incomplete.");
        var bodySurfaceCount = actor.Surfaces.Count(surface => bodyRoles.Contains(surface.Role));
        GD.Print(
            "OPENNV_NEW_GAME_FACEGEN_ACTOR_COVERAGE " +
            $"fullBody={source.FullBody} bodyRoles={bodyRoles.Count} " +
            $"bodySurfaces={bodySurfaceCount} morphSurfaces={morphSurfaces.Length} " +
            $"controls={bindings.Count} actorBounds={actor.Bounds}");
        return new OwnedGamebryoFaceGenPreviewHost(
            viewportContainer,
            viewport,
            actor.Root,
            CharacterBodyRig.ResolveSkeleton(actor.Root, "reflectron-live-preview"),
            camera,
            headFramedBounds,
            actor.Bounds,
            NodeTraversal.Descendants<MeshInstance3D>(actor.Root).ToArray(),
            bindings,
            bodySurfaceCount,
            framingDisposition,
            policy.MorphWeightScale,
            unitsToMeters);
    }

    private static bool BoundsOverlap(Aabb left, Aabb right) =>
        left.Position.X <= right.End.X && left.End.X >= right.Position.X &&
        left.Position.Y <= right.End.Y && left.End.Y >= right.Position.Y &&
        left.Position.Z <= right.End.Z && left.End.Z >= right.Position.Z;

    internal void Apply(string settingEntity, float uiValue)
    {
        var morphWeight = uiValue * _morphWeightScale;
        if (!float.IsFinite(uiValue) || !float.IsFinite(morphWeight) ||
            !_bindings.TryGetValue(settingEntity, out var bindings))
            throw new ArgumentOutOfRangeException(nameof(uiValue));
        foreach (var binding in bindings)
            binding.Mesh.SetBlendShapeValue(binding.Index, morphWeight);
    }

    internal void ApplyBodyProportions(CharacterBodyProportions proportions)
    {
        CharacterBodyRig.Apply(
            _actorRoot,
            _bodySkeleton,
            proportions,
            _actorRoot,
            "reflectron-live-preview");
    }

    internal void ShowThreeDimensional(CharacterBodyProportions proportions)
    {
        SetPreviewState(proportions, faceFraming: true, greenProjection: false);
    }

    internal void ShowTwoDimensional(CharacterBodyProportions proportions)
    {
        SetPreviewState(proportions, faceFraming: true, greenProjection: true);
    }

    internal void SetPreviewState(
        CharacterBodyProportions proportions,
        bool faceFraming,
        bool greenProjection)
    {
        ApplyBodyProportions(proportions);
        foreach (var mesh in _actorMeshes)
            mesh.MaterialOverlay = null;
        Control.Material = greenProjection
            ? ClassicGreenWireframeShader.Create(
                "OpenNV_ReflectronCharacterGreenVatsEdgeProjection")
            : null;
        FrameBounds(
            faceFraming ? _faceBounds : _fullActorBounds,
            proportions.Height,
            faceFraming ? PreviewFaceFrameMargin : PreviewBodyFrameMargin,
            faceFraming ? "frontal classic portrait" : "frontal full-body");
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        var mode = (greenProjection, faceFraming) switch
        {
            (true, true) => "green-face-wireframe-closeup",
            (true, false) => "green-body-wireframe",
            (false, true) => "normal-face",
            _ => "normal-body",
        };
        _actorRoot.SetMeta("reflectron_preview_mode", mode);
        _actorRoot.SetMeta("reflectron_camera_alignment", "front-centered");
        _actorRoot.SetMeta(
            "reflectron_projection_shader_role",
            greenProjection ? ClassicGreenWireframeShader.ProjectionRole : "source-normal");
    }

    private void FrameBounds(
        Aabb bounds,
        float heightScale,
        float margin,
        string label)
    {
        var target = bounds.GetCenter();
        target.Y *= heightScale;
        var size = bounds.Size;
        size.Y *= heightScale;
        var aspect = _viewport.Size.X / (float)_viewport.Size.Y;
        var halfFov = Mathf.DegToRad(_camera.Fov) * PreviewHalfExtentFactor;
        var tangent = MathF.Tan(halfFov);
        var distance = MathF.Max(
            size.Y * PreviewHalfExtentFactor / tangent,
            size.X * PreviewHalfExtentFactor / (tangent * aspect)) * margin;
        if (!target.IsFinite() || !size.IsFinite() ||
            !float.IsFinite(distance) || distance <= 0.0f)
            throw new InvalidOperationException(
                $"Reflectron {label} preview framing is invalid.");
        // Imported Gamebryo actors face the preview camera on Godot's forward axis.
        // The device's decorative yaw must never turn a character portrait.
        _camera.Position = target + Vector3.Forward *
            (distance + size.Z * PreviewHalfExtentFactor);
        _camera.LookAt(target, Vector3.Up);
    }

    internal Image CaptureRenderedImage() => _viewport.GetTexture().GetImage();

    internal PreviewDisposalAcceptance DisposeOwnedTree()
    {
        if (!GodotObject.IsInstanceValid(Control) ||
            !GodotObject.IsInstanceValid(_viewport) ||
            !GodotObject.IsInstanceValid(_actorRoot) ||
            _viewport.GetParent() != Control ||
            !_viewport.OwnWorld3D ||
            !_viewport.IsAncestorOf(_actorRoot))
            throw new InvalidOperationException(
                "Player FaceGen preview ownership is not isolated under its private SubViewport.");
        var controlId = Control.GetInstanceId();
        var viewportId = _viewport.GetInstanceId();
        var actorId = _actorRoot.GetInstanceId();
        Control.Free();
        if (GodotObject.IsInstanceValid(Control) ||
            GodotObject.IsInstanceValid(_viewport) ||
            GodotObject.IsInstanceValid(_actorRoot))
            throw new InvalidOperationException(
                "Player FaceGen preview tree survived its owning modal close.");
        return new PreviewDisposalAcceptance(
            controlId,
            viewportId,
            actorId,
            "private-own-world3d-control-free-invalidates-control-viewport-actor");
    }

    internal float MaximumAppliedVertexDeltaMeters()
    {
        var maximum = 0.0f;
        foreach (var bindings in _bindings.Values)
        {
            foreach (var binding in bindings)
            {
                var weight = binding.Mesh.GetBlendShapeValue(binding.Index);
                if (Mathf.IsZeroApprox(weight))
                    continue;
                var mesh = binding.Mesh.Mesh ?? throw new InvalidOperationException(
                    "Player FaceGen preview morph binding has no mesh.");
                for (var surfaceIndex = 0;
                     surfaceIndex < mesh.GetSurfaceCount();
                     surfaceIndex++)
                {
                    var targets = mesh.SurfaceGetBlendShapeArrays(surfaceIndex);
                    if (binding.Index >= targets.Count)
                        throw new InvalidOperationException(
                            "Player FaceGen preview morph binding exceeds target count.");
                    var positions = targets[binding.Index]
                        [(int)Mesh.ArrayType.Vertex]
                        .AsVector3Array();
                    foreach (var position in positions)
                        maximum = MathF.Max(
                            maximum,
                            position.Length() * MathF.Abs(weight) * _unitsToMeters);
                }
            }
        }
        return maximum;
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Player FaceGen preview artifact hash differs: {path}");
    }

    private static ActorModelSlice.LoadedSurface UniqueSurface(
        ActorModelSlice.LoadedActor actor,
        string role)
    {
        var matches = actor.Surfaces.Where(surface =>
            surface.Role.Equals(role, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Player FaceGen preview has {matches.Length} surfaces for role {role}.");
        return matches[0];
    }

    private readonly record struct MorphBinding(MeshInstance3D Mesh, int Index);

    internal readonly record struct PreviewDisposalAcceptance(
        ulong ControlInstanceId,
        ulong ViewportInstanceId,
        ulong ActorInstanceId,
        string Disposition);
}
