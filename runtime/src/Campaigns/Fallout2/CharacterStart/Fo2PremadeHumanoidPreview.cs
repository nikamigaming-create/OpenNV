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
    private readonly Fo2CharacterStartCatalog _catalog;
    private readonly Fo2HumanoidDonorContract _humanoidDonor;
    private readonly Node3D _previewRoot;
    private readonly Camera3D _camera;
    private Fo2HumanoidVisual? _donor;
    private Fo2PremadeCharacter _character;

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
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var viewport = new SubViewport
        {
            Name = "FO2_PREMADE_TRUE_3D_HUMANOID_VIEWPORT",
            Size = new Vector2I(592, 260),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
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
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(KeyPitchDegrees, KeyYawDegrees, 0.0f),
            LightEnergy = KeyEnergy,
            ShadowEnabled = false,
        });
        _previewRoot = new Node3D { Name = "FO2_HASH_BOUND_DONOR_PREVIEW_ROOT" };
        viewport.AddChild(_previewRoot);
        _camera = new Camera3D
        {
            Name = "FO2_PREMADE_HASH_BOUND_DONOR_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSize,
            Position = new Vector3(0.0f, 0.0f, CameraZ),
            Current = true,
        };
        viewport.AddChild(_camera);
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

    internal void SetCharacter(Fo2PremadeCharacter character)
    {
        _character = character;
        if (_donor is not null)
        {
            _previewRoot.RemoveChild(_donor);
            _donor.QueueFree();
        }
        _donor = new Fo2HumanoidVisual(
            Fo2HumanoidIdentity.FromPremade(character),
            _humanoidDonor)
        {
            Name = $"FO2_PREMADE_{character.Id}_HASH_BOUND_DONOR",
        };
        _previewRoot.AddChild(_donor);
        SetMeta("source_character_id", character.Id);
        SetMeta("source_panel_logical_path", character.Panel.LogicalPath);
        SetMeta("source_panel_sha256", character.Panel.SourceSha256);
        SetMeta("local_panel_png_sha256", character.Panel.PngSha256);
        SetMeta("donor_manifest_sha256", _humanoidDonor.ManifestSha256);
        SetMeta("donor_outfit_form_id", _humanoidDonor.ForSex(character.Profile.Sex).OutfitFormId);
        SetMeta("presentation_boundary",
            "owned-fnv-body-is-presentation-only-not-fallout2-character-geometry");
    }
}
