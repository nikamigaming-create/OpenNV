using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1PremadePlayerPreviewContracts
{
    internal const int ViewportWidth = 424;
    internal const int ViewportHeight = 374;
    internal const float CameraMargin = 1.12f;
    internal const float CameraDepthMeters = 4.0f;
    internal const float CameraNearMeters = 0.05f;
    internal const float CameraFarMeters = 20.0f;
    internal const float AmbientEnergy = 0.72f;
    internal const float KeyEnergy = 1.1f;
    internal const float KeyPitchDegrees = -24.0f;
    internal const float KeyYawDegrees = -28.0f;
}

/// <summary>
/// Shows a verified owned skinned donor only when its source sex agrees with
/// the selected Fallout 1 identity. GCD and portrait FRM remain authoritative;
/// an unsupported identity has no substitute humanoid geometry.
/// </summary>
internal sealed partial class Fo1PremadePlayerPreview : SubViewportContainer
{
    private const string OwnedDonorMode = "owned-fnv-full-body-presentation-donor";
    private const string UnavailableMode = "owned-humanoid-donor-unavailable-fail-closed";

    private readonly IReadOnlyDictionary<string, Fo1HexSceneLoader.PlayerPresentationSource> _sources;
    private readonly Node3D _donorRoot;
    private readonly Camera3D _camera;
    private readonly Dictionary<string, PreviewEvidence> _evidence =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActorModelSlice.LoadedActor> _donors =
        new(StringComparer.OrdinalIgnoreCase);
    private Fo1HexSceneLoader.PlayerPresentationSource? _activeSource;
    private Fo1PremadeCharacter? _pendingCharacter;
    private string? _donorFailure;
    private bool _ready;

    internal Fo1PremadePlayerPreview(
        IReadOnlyDictionary<string, Fo1HexSceneLoader.PlayerPresentationSource> sources)
    {
        _sources = sources;
        if (!_sources.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(["Male", "Female"]))
            throw new InvalidOperationException(
                "Fallout 1 premade player preview requires male and female owned donors.");
        Name = "FO1_PREMADE_TRUE_3D_PLAYER_PREVIEW";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        var viewport = new SubViewport
        {
            Name = "FO1_PREMADE_TRUE_3D_PLAYER_VIEWPORT",
            Size = new Vector2I(
                Fo1PremadePlayerPreviewContracts.ViewportWidth,
                Fo1PremadePlayerPreviewContracts.ViewportHeight),
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
                AmbientLightEnergy = Fo1PremadePlayerPreviewContracts.AmbientEnergy,
            },
        });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(
                Fo1PremadePlayerPreviewContracts.KeyPitchDegrees,
                Fo1PremadePlayerPreviewContracts.KeyYawDegrees,
                0.0f),
            LightEnergy = Fo1PremadePlayerPreviewContracts.KeyEnergy,
            ShadowEnabled = false,
        });
        _donorRoot = new Node3D { Name = "VerifiedOwnedFNVDonor" };
        viewport.AddChild(_donorRoot);
        _camera = new Camera3D
        {
            Name = "FO1_PREMADE_TRUE_3D_PLAYER_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = Fo1PremadePlayerPreviewContracts.CameraNearMeters,
            Far = Fo1PremadePlayerPreviewContracts.CameraFarMeters,
            Current = true,
        };
        viewport.AddChild(_camera);
    }

    internal string CharacterId => GetMeta("fo1_character_id").AsString();
    internal string PresentationMode => GetMeta("presentation_mode").AsString();
    internal string PresentationLabel => PresentationMode == OwnedDonorMode
        ? "LIVE 3D: OWNED FNV VAULT-SUIT DONOR"
        : "LIVE 3D UNAVAILABLE: OWNED DONOR DOES NOT MATCH SELECTION";
    internal bool UsesOwnedDonor => PresentationMode == OwnedDonorMode;
    internal int DonorSurfaces => _activeSource is { } source &&
        _donors.TryGetValue(source.SourceActorFemale ? "Female" : "Male", out var donor)
            ? donor.AuthoredSurfaces : 0;
    internal string DonorModelSha256 => _activeSource?.ModelSha256 ?? "";
    internal string DonorSourceActorFormId => _activeSource?.SourceActorBaseFormId ?? "";
    internal string? DonorFailure => _donorFailure;

    internal object Report() => new
    {
        schema = "opennv-fo1-premade-player-preview/v1",
        source = new
        {
            donors = _sources.OrderBy(row => row.Key).Select(row => new {
                sex = row.Key, row.Value.ModelSha256, row.Value.SidecarSha256,
                row.Value.SourceActorBaseFormId, row.Value.Surfaces,
                row.Value.Textures, row.Value.Animations }).ToArray(),
        },
        characters = _evidence.Values
            .OrderBy(row => row.Index)
            .Select(row => new
            {
                row.CharacterId,
                row.Sex,
                row.PresentationMode,
                row.OwnedPortraitFrmSha256,
            })
            .ToArray(),
        donorFailure = _donorFailure,
        boundary =
            "owned FNV model is presentation-only; unsupported identities render no substitute humanoid",
    };

    public override void _Ready()
    {
        try
        {
            foreach (var (sex, source) in _sources)
            {
                var donor = ActorModelSlice.Load(source.Model, source.Sidecar, _donorRoot);
                if (donor.FormId != source.SourceActorBaseFormId ||
                    donor.AuthoredSurfaces != source.Surfaces ||
                    donor.AuthoredTextures != source.Textures ||
                    donor.Animations != source.Animations)
                    throw new InvalidOperationException(
                        "Fallout 1 premade preview donor coverage differs from the scene contract.");
                donor.Root.Position += Vector3.Up * -donor.Bounds.Position.Y;
                donor.Root.Visible = false;
                _donors.Add(sex, donor with { Bounds = new Aabb(
                    donor.Bounds.Position + Vector3.Up * -donor.Bounds.Position.Y,
                    donor.Bounds.Size) });
            }
        }
        catch (Exception exception)
        {
            _donorFailure = exception.Message;
            _donorRoot.Visible = false;
        }
        _ready = true;
        if (_pendingCharacter is not null)
            ApplyCharacter(_pendingCharacter);
    }

    internal void SetCharacter(Fo1PremadeCharacter character)
    {
        _pendingCharacter = character;
        if (_ready)
            ApplyCharacter(character);
    }

    private void ApplyCharacter(Fo1PremadeCharacter character)
    {
        var useOwnedDonor = _sources.TryGetValue(character.Profile.Sex, out var source) &&
            _donors.TryGetValue(character.Profile.Sex, out var donor);
        _activeSource = useOwnedDonor ? source : null;
        foreach (var (sex, row) in _donors)
            row.Root.Visible = useOwnedDonor && sex.Equals(character.Profile.Sex,
                StringComparison.OrdinalIgnoreCase);
        SetMeta("fo1_character_id", character.Id);
        SetMeta("fo1_character_sex", character.Profile.Sex);
        SetMeta("presentation_mode", useOwnedDonor ? OwnedDonorMode : UnavailableMode);
        SetMeta("owned_fallout_portrait_sha256", character.Portrait.SourceFrmSha256);
        SetMeta("owned_fnv_donor_model_sha256", _activeSource?.ModelSha256 ?? "none");
        SetMeta("owned_fnv_donor_sidecar_sha256", _activeSource?.SidecarSha256 ?? "none");
        SetMeta("owned_fnv_donor_source_actor_form_id", _activeSource?.SourceActorBaseFormId ?? "none");
        SetMeta(
            "boundary",
            useOwnedDonor
                ? "presentation-donor-not-fallout1-authored-character-geometry"
                : "no-substitute-humanoid-rendered-donor-selection-mismatch");
        _evidence[character.Id] = new PreviewEvidence(
            Array.IndexOf(new[] { "max-stone", "natalia", "albert" }, character.Id),
            character.Id,
            character.Profile.Sex,
            useOwnedDonor ? OwnedDonorMode : UnavailableMode,
            character.Portrait.SourceFrmSha256);
        if (useOwnedDonor && _donors.TryGetValue(character.Profile.Sex, out var framedDonor))
            Frame(framedDonor.Bounds);
    }

    private void Frame(Aabb bounds)
    {
        var center = bounds.GetCenter();
        var aspect = (float)Fo1PremadePlayerPreviewContracts.ViewportWidth /
            Fo1PremadePlayerPreviewContracts.ViewportHeight;
        _camera.Size = MathF.Max(bounds.Size.Y, bounds.Size.X / aspect) *
            Fo1PremadePlayerPreviewContracts.CameraMargin;
        _camera.Position = new Vector3(
            center.X,
            center.Y,
            center.Z - Fo1PremadePlayerPreviewContracts.CameraDepthMeters);
        _camera.LookAt(center, Vector3.Up);
    }

    private readonly record struct PreviewEvidence(
        int Index,
        string CharacterId,
        string Sex,
        string PresentationMode,
        string OwnedPortraitFrmSha256);
}
