using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed partial class Fo2PremadeHumanoidPreview : SubViewportContainer
{
    private const float AmbientEnergy = 0.74f;
    private const float KeyPitchDegrees = -26.0f;
    private const float KeyYawDegrees = -30.0f;
    private const float KeyEnergy = 1.15f;
    private const float CameraSize = 2.25f;
    private const float CameraZ = 4.0f;
    private const float FramingMargin = 1.16f;
    internal const float PickerDetailsCompositionRightOffset = 0.58f;
    internal const float EditorColumnCompositionRightOffset = 0.0f;
    private const float OrbitRadiansPerPixel = 0.012f;
    private const float MinimumZoom = 0.58f;
    private const float MaximumZoom = 1.65f;
    private const float ZoomStep = 0.88f;
    private readonly Fo2CharacterStartCatalog _catalog;
    private readonly Fo2HumanoidDonorContract _humanoidDonor;
    private readonly SubViewport _viewport;
    private readonly Node3D _previewRoot;
    private readonly Camera3D _camera;
    private Fo2HumanoidVisual? _donor;
    private Fo2PremadeCharacter _character;
    private Fo2HumanoidAppearance? _appearance;
    private float _compositionRightOffset = PickerDetailsCompositionRightOffset;
    private float _zoom = 1.0f;
    private bool _orbitDragging;
    private bool _classicPortraitProjection;

    internal Fo2PremadeHumanoidPreview(
        Fo2PremadeCharacter character,
        Fo2CharacterStartCatalog catalog,
        Fo2HumanoidDonorContract humanoidDonor)
    {
        _character = character;
        _catalog = catalog;
        _humanoidDonor = humanoidDonor;
        Name = "FO2_PREMADE_TRUE_3D_HUMANOID_PREVIEW";
        Stretch = true;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = "Drag to rotate • Mouse wheel to zoom • Middle click to reset";
        Visible = false;
        _viewport = new SubViewport
        {
            Name = "FO2_PREMADE_TRUE_3D_HUMANOID_VIEWPORT",
            Size = new Vector2I(592, 260),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("07100b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = AmbientEnergy,
            },
        });
        _viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(KeyPitchDegrees, KeyYawDegrees, 0.0f),
            LightEnergy = KeyEnergy,
            ShadowEnabled = false,
        });
        _previewRoot = new Node3D { Name = "FO2_HASH_BOUND_DONOR_PREVIEW_ROOT" };
        _viewport.AddChild(_previewRoot);
        _camera = new Camera3D
        {
            Name = "FO2_PREMADE_HASH_BOUND_DONOR_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSize,
            Position = new Vector3(0.0f, 0.0f, CameraZ),
            Current = true,
        };
        _viewport.AddChild(_camera);
        SetCharacter(character);
    }

    internal string CharacterId => _character.Id;
    internal string SourcePanelSha256 => _character.Panel.SourceSha256;
    internal string LocalPanelPngSha256 => _character.Panel.PngSha256;
    internal int SurfaceCount => _donor?.AuthoredSurfaces ?? 0;
    internal string PresentationMode => _donor?.PresentationMode ??
        Fo2HumanoidVisual.UnavailableMode;
    internal string PresentationLabel => _donor?.PresentationLabel ??
        "LIVE 3D UNAVAILABLE: OWNED DONOR IS REQUIRED";
    internal bool UsesOwnedDonor => _donor?.UsesOwnedDonor == true;
    internal string? DonorFailure => _donor?.DonorFailure;
    internal Fo2HumanoidDonorContract DonorContract => _humanoidDonor;
    internal float Zoom => _zoom;
    internal float OrbitYawRadians => _previewRoot.Rotation.Y;
    internal float CompositionRightOffset => _compositionRightOffset;
    internal bool ClassicPortraitProjection => _classicPortraitProjection;

    internal void SetClassicPortraitProjection(bool enabled)
    {
        _classicPortraitProjection = enabled;
        Material = enabled ? ClassicPortraitMaterial() : null;
        _viewport.RenderTargetUpdateMode = enabled
            ? SubViewport.UpdateMode.Once
            : SubViewport.UpdateMode.Always;
        if (enabled)
        {
            ResetView();
            if (_donor?.UsesOwnedDonor == true)
                FrameDonor(_donor);
        }
        SetMeta(
            "classic_portrait_projection",
            enabled
                ? "frozen-stylized-render-of-current-data-bound-3d-character"
                : "live-source-material-3d-character");
    }

    internal void SetProportions(
        OpenNV.Runtime.Presentation.CharacterCreation.CharacterBodyProportions proportions)
    {
        var donor = _donor ?? throw new InvalidOperationException(
            "Fallout 2 live 3D preview has no loaded humanoid.");
        donor.SetProportions(proportions);
        if (IsInsideTree() && donor.UsesOwnedDonor)
            FrameDonor(donor);
    }

    internal void SetAppearance(Fo2HumanoidAppearance appearance)
    {
        _appearance = appearance;
        var donor = _donor ?? throw new InvalidOperationException(
            "Fallout 2 live 3D preview has no loaded humanoid.");
        donor.SetAppearance(appearance);
        if (IsInsideTree() && donor.UsesOwnedDonor)
            FrameDonor(donor);
    }

    internal void SetCompositionRightOffset(float value)
    {
        if (!float.IsFinite(value) || value is < 0.0f or > 0.75f)
            throw new ArgumentOutOfRangeException(nameof(value));
        _compositionRightOffset = value;
        if (IsInsideTree() && _donor?.UsesOwnedDonor == true)
            FrameDonor(_donor);
    }

    internal void SetSex(string sex)
    {
        if (sex is not "Male" and not "Female")
            throw new ArgumentOutOfRangeException(nameof(sex));
        SetCharacter(_character with
        {
            Profile = _character.Profile with { Sex = sex },
        });
    }

    internal void SetCharacter(Fo2PremadeCharacter character)
    {
        _character = character;
        ResetView();
        if (_donor is not null)
        {
            _previewRoot.RemoveChild(_donor);
            _donor.QueueFree();
        }
        _donor = new Fo2HumanoidVisual(
            Fo2HumanoidIdentity.FromPremade(character),
            _humanoidDonor,
            Fo2CharacterBodyProfile.ForSex(character.Profile.Sex))
        {
            Name = $"FO2_PREMADE_{character.Id}_HASH_BOUND_DONOR",
        };
        _previewRoot.AddChild(_donor);
        if (_appearance is not null)
            _donor.SetAppearance(_appearance);
        if (IsInsideTree())
            PrepareDonor(_donor);
        SetMeta("source_character_id", character.Id);
        SetMeta("source_panel_logical_path", character.Panel.LogicalPath);
        SetMeta("source_panel_sha256", character.Panel.SourceSha256);
        SetMeta("local_panel_png_sha256", character.Panel.PngSha256);
        SetMeta("donor_manifest_sha256", _humanoidDonor.ManifestSha256);
        SetMeta("donor_outfit_form_id", _humanoidDonor.ForSex(character.Profile.Sex).OutfitFormId);
        SetMeta("presentation_boundary",
            "owned-fnv-body-is-presentation-only-not-fallout2-character-geometry");
    }

    public override void _Ready()
    {
        PrepareDonor(_donor ?? throw new InvalidOperationException(
            "Fallout 2 live 3D preview entered the tree without its humanoid."));
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                _orbitDragging = left.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.WheelUp,
            }:
                SetZoom(_zoom * ZoomStep);
                AcceptEvent();
                break;
            case InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.WheelDown,
            }:
                SetZoom(_zoom / ZoomStep);
                AcceptEvent();
                break;
            case InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Middle,
            }:
                ResetView();
                if (_donor?.UsesOwnedDonor == true)
                    FrameDonor(_donor);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _orbitDragging:
                _previewRoot.RotateY(-motion.Relative.X * OrbitRadiansPerPixel);
                if (_donor?.UsesOwnedDonor == true)
                    FrameDonor(_donor);
                SetMeta("presentation_orbit_y_radians", _previewRoot.Rotation.Y);
                AcceptEvent();
                break;
        }
    }

    private void SetZoom(float value)
    {
        _zoom = Math.Clamp(value, MinimumZoom, MaximumZoom);
        if (_donor?.UsesOwnedDonor == true)
            FrameDonor(_donor);
        SetMeta("presentation_zoom", _zoom);
    }

    private void ResetView()
    {
        _orbitDragging = false;
        _zoom = 1.0f;
        _previewRoot.Rotation = Vector3.Zero;
        SetMeta("presentation_zoom", _zoom);
        SetMeta("presentation_orbit_y_radians", 0.0f);
    }

    private void PrepareDonor(Fo2HumanoidVisual donor)
    {
        if (!donor.UsesOwnedDonor)
            throw new InvalidOperationException(
                "Fallout 2 live 3D preview donor did not load its owned assembly.");
        donor.SetDirection(_character.Profile.Sex == "Female" ? 2 : 3);
        var configuration = RuntimeConfiguration.Load();
        var litMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
            donor,
            new Color(0.46f, 0.42f, 0.36f, 1.0f),
            new Color(0.015f, 0.012f, 0.008f, 1.0f),
            0.0f,
            100000.0f,
            1.0f,
            configuration.World.GameUnitsToMeters);
        if (litMaterials <= 0)
            throw new InvalidOperationException(
                "Fallout 2 live 3D preview has no source-lit materials.");
        FrameDonor(donor);
    }

    private static ShaderMaterial ClassicPortraitMaterial() => new()
    {
        ResourceName = "OpenNV_FalloutClassicCustomPortraitProjection",
        Shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode unshaded;

                void fragment() {
                    vec4 source = texture(TEXTURE, UV);
                    vec3 left = texture(TEXTURE, UV - vec2(TEXTURE_PIXEL_SIZE.x, 0.0)).rgb;
                    vec3 right = texture(TEXTURE, UV + vec2(TEXTURE_PIXEL_SIZE.x, 0.0)).rgb;
                    vec3 up = texture(TEXTURE, UV - vec2(0.0, TEXTURE_PIXEL_SIZE.y)).rgb;
                    vec3 down = texture(TEXTURE, UV + vec2(0.0, TEXTURE_PIXEL_SIZE.y)).rgb;
                    float edge = length(right - left) + length(down - up);
                    vec3 graded = pow(max(source.rgb, vec3(0.0)), vec3(0.86));
                    float luma = dot(graded, vec3(0.299, 0.587, 0.114));
                    graded = mix(vec3(luma), graded, 1.28);
                    graded *= vec3(1.06, 0.98, 0.86);
                    graded = floor(graded * 6.0 + 0.5) / 6.0;
                    float ink = smoothstep(0.19, 0.48, edge);
                    float paper = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453);
                    graded *= mix(0.965, 1.035, paper);
                    graded = mix(graded, vec3(0.018, 0.014, 0.010), ink * 0.86);
                    COLOR = vec4(clamp(graded, vec3(0.0), vec3(1.0)), source.a);
                }
                """,
        },
    };

    private void FrameDonor(Fo2HumanoidVisual donor)
    {
        var bounds = donor.PresentationBounds;
        var aspect = Size.X / MathF.Max(Size.Y, 1.0f);
        var size = MathF.Max(bounds.Size.Y, bounds.Size.X / MathF.Max(aspect, 0.01f)) *
            FramingMargin * _zoom;
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() ||
            !float.IsFinite(size) || size <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 live 3D preview framing bounds are invalid.");
        var target = bounds.GetCenter();
        target += Vector3.Right * size * _compositionRightOffset;
        _camera.Size = size;
        _camera.Position = target + Vector3.Back * MathF.Max(4.0f, bounds.Size.Z * 2.0f);
        _camera.LookAt(target, Vector3.Up);
        SetMeta("presentation_bounds", bounds);
        SetMeta("presentation_camera_size", size);
        SetMeta("presentation_framing_margin", FramingMargin);
        SetMeta("presentation_composition_right_offset", _compositionRightOffset);
    }
}
