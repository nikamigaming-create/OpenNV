using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2HumanoidDonorVariant(
    string Sex,
    string ModelPath,
    string SidecarPath,
    string ModelSha256,
    string SidecarSha256,
    int Surfaces,
    int Textures,
    int Animations,
    IReadOnlyList<string> BodyRoles,
    string OutfitFormId,
    string RigidAttachmentNode,
    string EquipmentSocketNode);

/// <summary>Shared consumer for the FNV full-body player-preview artifact.
/// Its sex-keyed modular body/outfit sources and authored rigid socket are
/// verified before either classic campaign can use the assembled actor.</summary>
internal sealed record Fo2HumanoidDonorContract(
    string ManifestPath,
    string ManifestSha256,
    string SourceActorFormId,
    IReadOnlyDictionary<string, Fo2HumanoidDonorVariant> Variants)
{
    private const string PreviewSetSchema = "opennv-owned-player-facegen-preview-set/v3";
    private const string PreviewSetStatus =
        "compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-all-native-geometry-controls-runtime-bound";
    private const string EquipmentSocketNode = "Bip01 R Hand";
    private static readonly string[] RequiredBodyRoles = ["body", "left-hand", "right-hand"];

    internal static Fo2HumanoidDonorContract? FromOptions(
        IReadOnlyDictionary<string, string> options) =>
        options.TryGetValue("classic-humanoid-donor-preview-set", out var path)
            ? Load(path)
            : null;

    internal static Fo2HumanoidDonorContract RequireFromOptions(
        IReadOnlyDictionary<string, string> options) =>
        FromOptions(options) ?? throw new InvalidOperationException(
            "Fallout 2 3D player requires --classic-humanoid-donor-preview-set; " +
            "no substitute player body is admitted.");

    internal static Fo2HumanoidDonorContract Load(string configuredPath)
    {
        var path = Path.GetFullPath(configuredPath);
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Required(root, "schema") != PreviewSetSchema ||
            Required(root, "status") != PreviewSetStatus ||
            !root.GetProperty("fullBody").GetBoolean() ||
            !root.GetProperty("bodyComponentRoles").EnumerateArray()
                .Select(value => Required(value)).SequenceEqual(RequiredBodyRoles, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Unexpected classic humanoid donor preview set: {path}");
        var outfit = Required(root, "presentationOutfitFormId");
        if (outfit.Length != 8 || !outfit.All(Uri.IsHexDigit))
            throw new InvalidOperationException(
                "Classic humanoid donor has no hash-bound outfit FormID.");
        var bodySources = root.GetProperty("bodyComponentSourcesBySex")
            .EnumerateObject().ToDictionary(
                value => value.Name,
                value => value.Value.EnumerateArray().ToArray(),
                StringComparer.Ordinal);
        var variants = root.GetProperty("previews").EnumerateArray().Select(row =>
        {
            var sex = Required(row, "sex");
            if (!bodySources.TryGetValue(sex, out var modules) ||
                !modules.Select(module => Required(module, "role"))
                    .SequenceEqual(RequiredBodyRoles, StringComparer.Ordinal) ||
                modules.Any(module => !ValidHash(module, "modelSha256") ||
                    !ValidHash(module, "diffuseSha256") || !ValidHash(module, "normalSha256")))
                throw new InvalidOperationException(
                    $"Classic humanoid donor module join is incomplete for {sex}.");
            var outputs = row.GetProperty("outputs");
            var modelPath = Path.GetFullPath(Required(outputs, "gltf"));
            var sidecarPath = Path.GetFullPath(Required(outputs, "sidecar"));
            var modelSha256 = RequiredHash(outputs, "gltfSha256");
            var sidecarSha256 = RequiredHash(outputs, "sidecarSha256");
            VerifyFile(modelPath, modelSha256, "classic humanoid donor model");
            VerifyFile(sidecarPath, sidecarSha256, "classic humanoid donor sidecar");
            using var sidecar = JsonDocument.Parse(File.ReadAllBytes(sidecarPath));
            using var model = JsonDocument.Parse(File.ReadAllBytes(modelPath));
            var sidecarRoot = sidecar.RootElement;
            var socket = Required(sidecarRoot.GetProperty("skeleton"), "rigidAttachmentNode");
            var modelRoot = model.RootElement;
            var nodes = modelRoot.GetProperty("nodes").EnumerateArray().ToArray();
            var equipmentNodeIndices = nodes
                .Select((node, index) => (Node: node, Index: index))
                .Where(value => value.Node.TryGetProperty("name", out var name) &&
                    name.GetString() == EquipmentSocketNode)
                .Select(value => value.Index)
                .ToArray();
            if (equipmentNodeIndices.Length != 1 ||
                !modelRoot.GetProperty("skins").EnumerateArray().Any(skin =>
                    skin.GetProperty("joints").EnumerateArray().Any(joint =>
                        joint.GetInt32() == equipmentNodeIndices[0])))
                throw new InvalidOperationException(
                    $"Classic humanoid donor model has no unique skinned {EquipmentSocketNode}.");
            var surfaces = sidecarRoot.GetProperty("surfaces").EnumerateArray().ToArray();
            var expectedSurfaces = modules.Sum(module => module.GetProperty("retainedSurfaceCount").GetInt32());
            if (sidecarRoot.GetProperty("schema").GetString() != "opennv-actor-gltf/v4" ||
                sidecarRoot.GetProperty("status").GetString() != "skinned-animated" ||
                surfaces.Length < expectedSurfaces ||
                modules.Any(module => surfaces.Count(surface =>
                    Required(surface, "role") == Required(module, "role")) !=
                    module.GetProperty("retainedSurfaceCount").GetInt32()))
                throw new InvalidOperationException(
                    $"Classic humanoid donor sidecar differs from its modular contract for {sex}.");
            return new Fo2HumanoidDonorVariant(
                sex, modelPath, sidecarPath, modelSha256, sidecarSha256,
                surfaces.Length, sidecarRoot.GetProperty("textures").GetArrayLength(),
                sidecarRoot.GetProperty("animations").GetArrayLength(), RequiredBodyRoles,
                outfit, socket, EquipmentSocketNode);
        }).ToDictionary(value => value.Sex, StringComparer.Ordinal);
        if (!variants.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["male", "female"]))
            throw new InvalidOperationException("Classic humanoid donor sex variants are incomplete.");
        return new Fo2HumanoidDonorContract(
            path,
            Sha256(bytes),
            Required(root, "playerFormId"),
            variants);
    }

    internal Fo2HumanoidDonorVariant ForSex(string sex) =>
        Variants.TryGetValue(sex.ToLowerInvariant(), out var variant)
            ? variant
            : throw new InvalidOperationException(
                $"Classic humanoid donor has no source-bound variant for {sex}.");

    private static string Required(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Owned humanoid donor field is empty: {property}")
            : value;
    }

    private static string Required(JsonElement source)
    {
        var value = source.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("Classic humanoid donor contains an empty string.")
            : value;
    }

    private static bool ValidHash(JsonElement source, string property)
    {
        try { _ = RequiredHash(source, property); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static string RequiredHash(JsonElement source, string property)
    {
        var value = Required(source, property).ToLowerInvariant();
        return value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value
            : throw new InvalidOperationException(
                $"Owned humanoid donor hash is invalid: {property}");
    }

    private static void VerifyFile(string path, string expected, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing {label}.", path);
        var actual = Sha256(File.ReadAllBytes(path));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{label} hash mismatch: expected {expected}, got {actual}.");
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record Fo2HumanoidIdentity(
    string CharacterId,
    string Name,
    string Role,
    string Sex,
    string OwnedIdentitySha256,
    string OwnedPanelSha256,
    string SourceFid,
    string SourceFrmSha256)
{
    internal static Fo2HumanoidIdentity FromPremade(Fo2PremadeCharacter character) => new(
        character.Id,
        character.Profile.Name,
        character.Role,
        character.Profile.Sex,
        character.GcdSha256,
        character.Panel.SourceSha256,
        "picker-preview",
        character.Panel.SourceSha256);

    internal static Fo2HumanoidIdentity FromSelection(
        Fo2CharacterSelection? selection,
        Fo2ArroyoPlayerPresentationSource source)
    {
        if (selection is null)
            return new Fo2HumanoidIdentity(
                "source-default",
                "Chosen One",
                "Source default",
                source.Fid == Fo2CharacterStartCatalog.FemaleFid ? "Female" : "Male",
                source.PrototypeSha256,
                "none",
                source.Fid,
                source.SourceSha256);
        return new Fo2HumanoidIdentity(
            selection.Id,
            selection.Profile.Name,
            selection.Role,
            selection.Profile.Sex,
            selection.GcdSha256,
            selection.Source.Panel.SourceSha256,
            source.Fid,
            source.SourceSha256);
    }
}

/// <summary>
/// Presentation-only full-body geometry. Fallout 2 GCD/FRM data remains the
/// identity and animation-state authority; a verified owned FNV actor supplies
/// the current full-body presentation. Missing or incompatible donors leave no
/// substitute body and fail acceptance closed.
/// </summary>
internal sealed partial class Fo2HumanoidVisual : Node3D
{
    private const float PresentationFogPower = 1.0f;
    private const float CaveAmbientResponse = 0.28f;
    private const float DonorForwardAxisOffsetRadians = MathF.PI;
    private static readonly string[] LegBoneNames =
    [
        "Bip01 L Thigh",
        "Bip01 L Calf",
        "Bip01 L Foot",
        "Bip01 R Thigh",
        "Bip01 R Calf",
        "Bip01 R Foot",
    ];
    internal const string OwnedDonorMode =
        "owned-fnv-full-body-presentation-donor-non-parity";
    internal const string UnavailableMode =
        "owned-humanoid-donor-unavailable-fail-closed";

    private readonly Fo2HumanoidDonorContract _contract;
    private readonly Node3D _donorRoot = new() { Name = "VerifiedOwnedHumanoidDonor" };
    private CharacterBodyProportions _proportions;
    private ActorModelSlice.LoadedActor? _donor;
    private Fo2HumanoidIdentity _identity;
    private Fo2HumanoidDonorVariant? _variant;
    private BoneAttachment3D? _equipmentSocket;
    private Skeleton3D? _locomotionSkeleton;
    private ActorModelSlice.LoadedAnimation? _activeAnimation;
    private bool _walking;

    internal Fo2HumanoidVisual(
        Fo2HumanoidIdentity identity,
        Fo2HumanoidDonorContract? contract,
        CharacterBodyProportions? proportions = null)
    {
        _identity = identity;
        _proportions = proportions ?? ProportionsForIdentity(identity);
        _proportions.Validate("fallout2-visible-humanoid");
        _contract = contract ?? throw new InvalidOperationException(
            "Fallout 2 3D humanoid presentation requires a verified owned donor; " +
            "procedural fallback bodies are not permitted.");
        Name = "FO2_TRUE_3D_HUMANOID_PRESENTATION";
        AddChild(_donorRoot);
        ApplyIdentity(identity);
    }

    internal string PresentationMode => GetMeta("presentation_mode").AsString();
    internal string PresentationLabel => UsesOwnedDonor
        ? "LIVE 3D: OWNED FNV BODY DONOR (NON-PARITY)"
        : "LIVE 3D UNAVAILABLE: OWNED DONOR DOES NOT MATCH SELECTION";
    internal bool UsesOwnedDonor => PresentationMode == OwnedDonorMode;
    internal string CharacterId => _identity.CharacterId;
    internal string OwnedPanelSha256 => _identity.OwnedPanelSha256;
    internal string OwnedIdentitySha256 => _identity.OwnedIdentitySha256;
    internal int MeshInstances => UsesOwnedDonor ? _donor?.Meshes ?? 0 : 0;
    internal int AuthoredSurfaces => UsesOwnedDonor ? _donor?.AuthoredSurfaces ?? 0 : 0;
    internal bool EquipmentSocketResolved => _equipmentSocket is not null;
    internal string EquipmentSocketName => _variant?.EquipmentSocketNode ?? "";
    internal int LitMaterials { get; private set; }
    internal string ActiveAnimationLogicalPath { get; private set; } = "";
    internal double ActiveAnimationPositionSeconds =>
        _activeAnimation?.Player.CurrentAnimationPosition ?? 0.0;
    internal string? DonorFailure { get; private set; }
    internal CharacterBodyProportions Proportions => _proportions;

    internal void SetProportions(CharacterBodyProportions proportions)
    {
        proportions.Validate("fallout2-visible-humanoid");
        _proportions = proportions;
        ApplyBodyProportions();
    }

    internal float[] CaptureLegPose()
    {
        var skeleton = _locomotionSkeleton ?? throw new InvalidOperationException(
            "Fallout 2 humanoid locomotion skeleton is unavailable.");
        var sample = new float[LegBoneNames.Length * 12];
        var offset = 0;
        foreach (var boneName in LegBoneNames)
        {
            var bone = skeleton.FindBone(boneName);
            if (bone < 0)
                throw new InvalidOperationException(
                    $"Fallout 2 humanoid locomotion bone is absent: {boneName}");
            var pose = skeleton.GetBoneGlobalPose(bone);
            foreach (var value in new[]
                     {
                         pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z,
                         pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z,
                         pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z,
                         pose.Origin.X, pose.Origin.Y, pose.Origin.Z,
                     })
                sample[offset++] = value;
        }
        return sample;
    }

    internal static float LegPoseDistance(
        IReadOnlyList<float> first,
        IReadOnlyList<float> second)
    {
        if (first.Count != second.Count || first.Count == 0)
            throw new InvalidOperationException(
                "Fallout 2 humanoid leg-pose samples are incompatible.");
        var squared = 0.0f;
        for (var index = 0; index < first.Count; index++)
        {
            var delta = first[index] - second[index];
            squared += delta * delta;
        }
        return MathF.Sqrt(squared / first.Count);
    }

    internal void ApplyIdentity(Fo2HumanoidIdentity identity)
    {
        _identity = identity;
        var useDonor = _donor is not null && _variant?.Sex.Equals(
            identity.Sex, StringComparison.OrdinalIgnoreCase) == true;
        _donorRoot.Visible = useDonor;
        SetMeta("presentation_mode", useDonor ? OwnedDonorMode : UnavailableMode);
        SetMeta("character_id", identity.CharacterId);
        SetMeta("character_name", identity.Name);
        SetMeta("character_sex", identity.Sex);
        SetMeta("owned_identity_sha256", identity.OwnedIdentitySha256);
        SetMeta("owned_panel_sha256", identity.OwnedPanelSha256);
        SetMeta("source_fid", identity.SourceFid);
        SetMeta("source_frm_sha256", identity.SourceFrmSha256);
        SetMeta("visible_sprite3d_cards", 0);
        SetMeta(
            "boundary",
            useDonor
                ? "owned-fnv-body-is-presentation-only-not-fallout2-character-geometry"
                : "no-substitute-body-rendered-donor-selection-mismatch");
    }

    internal void SetDirection(int direction)
    {
        if (direction is < 0 or >= Fo1HexMath.DirectionCount)
            throw new ArgumentOutOfRangeException(nameof(direction));
        var anchor = Fo1HexMath.Tile(new Vector2I(100, 100));
        var offset = Fo1HexMath.Center(Fo1HexMath.TileInDirection(anchor, direction)) -
            Fo1HexMath.Center(anchor);
        Rotation = new Vector3(
            0.0f,
            MathF.Atan2(offset.X, offset.Z) + DonorForwardAxisOffsetRadians,
            0.0f);
        SetMeta("source_direction", direction);
    }

    internal void SetWalking(bool walking)
    {
        if (_walking == walking)
            return;
        _walking = walking;
        if (_donor is not null && UsesOwnedDonor)
            PlayDonorAnimation(walking);
    }

    internal void SetEquipmentState(
        bool equipped,
        string sourceFid,
        string sourcePid,
        int weaponAnimationCode,
        string geometryDisposition)
    {
        if (sourceFid != Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid ||
            sourcePid != Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid ||
            weaponAnimationCode !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode ||
            geometryDisposition !=
                Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition)
            throw new InvalidOperationException(
                "Fallout 2 equipped-player source identity drifted.");
        if (equipped && _donor is not null && _equipmentSocket is null)
            throw new InvalidOperationException(
                "Fallout 2 equipped player has no verified donor right-hand socket.");
        SetMeta("equipment_state", equipped ? "spear-equipped" : "unarmed");
        SetMeta("equipment_source_fid", sourceFid);
        SetMeta("equipment_source_pid", sourcePid);
        SetMeta("equipment_weapon_animation_code", weaponAnimationCode);
        SetMeta("equipment_socket", EquipmentSocketName);
        SetMeta("equipment_socket_resolved", EquipmentSocketResolved);
        SetMeta("equipment_geometry_disposition", geometryDisposition);
        SetMeta("equipment_geometry_visible", false);
    }

    internal void ApplyPresentationLighting(
        Fo2ArroyoCaves3DProfile profile,
        float cameraFarMeters)
    {
        var configuration = RuntimeConfiguration.Load();
        var unitsToMeters = configuration.World.GameUnitsToMeters;
        var atmosphere = profile.Atmosphere;
        var ambient = new Color(
            atmosphere.AmbientColor.R * atmosphere.AmbientEnergy * CaveAmbientResponse,
            atmosphere.AmbientColor.G * atmosphere.AmbientEnergy * CaveAmbientResponse,
            atmosphere.AmbientColor.B * atmosphere.AmbientEnergy * CaveAmbientResponse,
            atmosphere.AmbientColor.A);
        if (!float.IsFinite(cameraFarMeters) || cameraFarMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 humanoid presentation camera range is invalid.");
        LitMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
            this,
            ambient,
            atmosphere.FogColor,
            0.0f,
            cameraFarMeters / unitsToMeters,
            PresentationFogPower,
            unitsToMeters);
        if (LitMaterials <= 0)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor has no compatible source-lit materials.");
        SetMeta("presentation_lit_materials", LitMaterials);
        SetMeta("presentation_lighting_profile", profile.ResourcePath);
        SetMeta("presentation_lighting_profile_sha256", profile.Sha256);
        SetMeta("presentation_lighting_parity", false);
    }

    public override void _Ready()
    {
        TryLoadDonor();
        ApplyIdentity(_identity);
        if (_walking && UsesOwnedDonor)
            PlayDonorAnimation(true);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        ApplyBodyProportions();
    }

    private void TryLoadDonor()
    {
        try
        {
            _variant = _contract.ForSex(_identity.Sex);
            var loaded = ActorModelSlice.Load(
                _variant.ModelPath,
                _variant.SidecarPath,
                _donorRoot);
            if (loaded.FormId != _contract.SourceActorFormId ||
                loaded.AuthoredSurfaces != _variant.Surfaces ||
                loaded.AuthoredTextures != _variant.Textures ||
                loaded.Animations != _variant.Animations ||
                _variant.BodyRoles.Any(role =>
                    loaded.Surfaces.Count(surface => surface.Role == role) < 1))
                throw new InvalidOperationException(
                    "Owned humanoid donor runtime coverage differs from its manifest.");
            loaded.Root.Position += Vector3.Up * -loaded.Bounds.Position.Y;
            _donor = loaded;
            ApplySkinOnlyToneTransfer(loaded);
            ResolveEquipmentSocket(loaded);
            ApplyBodyProportions();
            if (!loaded.LoadedAnimations.Any(row =>
                    row.LogicalPath.Contains("idle", StringComparison.OrdinalIgnoreCase)) ||
                !loaded.LoadedAnimations.Any(row =>
                    row.LogicalPath.Contains("forward", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Fallout 2 owned humanoid donor requires distinct idle and forward clips.");
            PlayDonorAnimation(false);
            SetMeta("donor_manifest_sha256", _contract.ManifestSha256);
            SetMeta("donor_model_sha256", _variant.ModelSha256);
            SetMeta("donor_sidecar_sha256", _variant.SidecarSha256);
            SetMeta("donor_source_actor_form_id", _contract.SourceActorFormId);
            SetMeta("donor_outfit_form_id", _variant.OutfitFormId);
            SetMeta("donor_rigid_attachment_node", _variant.RigidAttachmentNode);
            SetMeta("donor_equipment_socket_node", _variant.EquipmentSocketNode);
        }
        catch (Exception exception)
        {
            DonorFailure = exception.Message;
            _donorRoot.Visible = false;
            SetMeta("donor_failure", exception.Message);
            throw new InvalidOperationException(
                "Fallout 2 owned humanoid donor failed closed.",
                exception);
        }
    }

    private void PlayDonorAnimation(bool walking)
    {
        if (_donor is not { } donor)
            return;
        var token = walking ? "forward" : "idle";
        var animation = donor.LoadedAnimations.FirstOrDefault(row =>
            row.LogicalPath.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(animation.RuntimeName))
            throw new InvalidOperationException(
                $"Fallout 2 owned humanoid donor has no {token} animation.");
        var resource = animation.Player.GetAnimation(animation.RuntimeName)
            ?? throw new InvalidOperationException(
                $"Fallout 2 owned humanoid donor has no runtime {token} animation resource.");
        resource.LoopMode = Animation.LoopModeEnum.Linear;
        animation.Player.Play(animation.RuntimeName);
        animation.Player.Advance(0.0);
        _activeAnimation = animation;
        ActiveAnimationLogicalPath = animation.LogicalPath;
        SetMeta("active_animation_logical_path", animation.LogicalPath);
        SetMeta("active_animation_runtime_name", animation.RuntimeName);
    }

    private void ApplySkinOnlyToneTransfer(ActorModelSlice.LoadedActor donor)
    {
        if (_variant is null)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor variant is unavailable for skin joining.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(_variant.SidecarPath));
        var headToneMaterials = donor.Surfaces
            .Where(surface => surface.Role == "head")
            .SelectMany(surface => Enumerable.Range(
                0,
                surface.Mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(index => surface.Mesh.GetSurfaceOverrideMaterial(index)))
            .OfType<ShaderMaterial>()
            .Where(material => material.Shader?.Code.Contains(
                "uniform vec3 tone_multiplier;",
                StringComparison.Ordinal) == true)
            .ToArray();
        if (headToneMaterials.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor requires one live FaceGen head tone source.");
        var headTone = headToneMaterials[0]
            .GetShaderParameter("tone_multiplier")
            .AsVector3();
        if (!headTone.IsFinite())
            throw new InvalidOperationException(
                "Fallout 2 humanoid live FaceGen head tone is invalid.");
        var headSkinColor = AverageFaceGenEncodedSkinColor(headToneMaterials[0]);
        var neckSkinColor = AverageFaceGenEncodedNeckColor(headToneMaterials[0]);
        headToneMaterials[0].SetShaderParameter("use_neck_complexion_target", true);
        headToneMaterials[0].SetShaderParameter(
            "neck_complexion_target",
            headSkinColor);
        headToneMaterials[0].SetShaderParameter(
            "neck_complexion_source_mean",
            (neckSkinColor.X + neckSkinColor.Y + neckSkinColor.Z) / 3.0f);
        var skinNodes = document.RootElement.GetProperty("surfaces")
            .EnumerateArray()
            .Where(row =>
            {
                var material = row.GetProperty("material");
                return material.TryGetProperty("skin", out var skin) &&
                    skin.ValueKind == JsonValueKind.Object &&
                    material.GetProperty("faceGen").ValueKind == JsonValueKind.Null &&
                    skin.GetProperty("schema").GetString() ==
                        "opennv-retail-actor-skin-material/v1" &&
                    skin.GetProperty("source").GetString() ==
                        "owned-nif-bs-shader-type-shaderskin";
            })
            .Select(row => row.GetProperty("runtimeNodeName").GetString() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var joined = 0;
        foreach (var surface in donor.Surfaces.Where(surface =>
                     skinNodes.Contains(surface.RuntimeNodeName)))
        {
            for (var index = 0;
                 index < (surface.Mesh.Mesh?.GetSurfaceCount() ?? 0);
                 index++)
            {
                if (surface.Mesh.GetSurfaceOverrideMaterial(index) is not ShaderMaterial material)
                    continue;
                if (material.ResourceName !=
                        RuntimeMaterialLoader.RetailActorMaterialResourceName ||
                    material.Shader?.Code.Contains(
                        "uniform bool use_skin_transfer;",
                        StringComparison.Ordinal) != true)
                    throw new InvalidOperationException(
                        "Fallout 2 humanoid source-declared skin surface lacks its runtime transfer.");
                material.SetShaderParameter("use_skin_transfer", true);
                var sourceSkinColor = AverageEncodedSkinColor(
                    material,
                    centralTorso: surface.Role == "body");
                var skinMatch = new Vector3(
                    Math.Clamp(headSkinColor.X / MathF.Max(sourceSkinColor.X, 0.0001f), 0.15f, 4.0f),
                    Math.Clamp(headSkinColor.Y / MathF.Max(sourceSkinColor.Y, 0.0001f), 0.15f, 4.0f),
                    Math.Clamp(headSkinColor.Z / MathF.Max(sourceSkinColor.Z, 0.0001f), 0.15f, 4.0f));
                SetMeta(
                    $"skin_join_match_{surface.Role.Replace('-', '_')}",
                    skinMatch);
                material.SetShaderParameter("skin_complexion_multiplier", Vector3.One);
                material.SetShaderParameter("use_skin_complexion_target", true);
                material.SetShaderParameter("skin_complexion_target", headSkinColor);
                material.SetShaderParameter(
                    "skin_complexion_source_mean",
                    (sourceSkinColor.X + sourceSkinColor.Y + sourceSkinColor.Z) / 3.0f);
                joined++;
            }
        }
        if (joined < 3)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor has no complete torso-and-hand skin join.");
        SetMeta(
            "skin_join_mode",
            "owned-shaderskin-detail-with-facegen-neck-and-cheek-complexion-v9");
        SetMeta("skin_join_materials", joined);
        SetMeta("skin_join_head_tone", headTone);
        SetMeta("skin_join_target_color", headSkinColor);
        SetMeta("skin_join_neck_source_color", neckSkinColor);
        SetMeta("skin_join_target_role", "head-paired-cheek-uv-islands");
    }

    private static Vector3 AverageFaceGenEncodedSkinColor(ShaderMaterial material)
    {
        // The canonical humanoid head UV keeps the two exposed cheeks away
        // from eyes, lips, hair, and the lower-neck seam.  Average both owned
        // islands so body, hands, and the neck inherit the visible complexion.
        var leftCheek = AverageFaceGenEncodedColor(
            material,
            0.12f,
            0.42f,
            0.40f,
            0.68f);
        var rightCheek = AverageFaceGenEncodedColor(
            material,
            0.58f,
            0.88f,
            0.40f,
            0.68f);
        return (leftCheek + rightCheek) * 0.5f;
    }

    private static Vector3 AverageFaceGenEncodedNeckColor(ShaderMaterial material)
    {
        return AverageFaceGenEncodedColor(
            material,
            0.18f,
            0.82f,
            0.78f,
            0.98f);
    }

    private static Vector3 AverageFaceGenEncodedColor(
        ShaderMaterial material,
        float minimumU,
        float maximumU,
        float minimumV,
        float maximumV)
    {
        var baseImage = RequiredTextureImage(material, "base_map");
        var detailImage = RequiredTextureImage(material, "facegen_map0");
        var neutral = material.GetShaderParameter("signed_detail_neutral").AsSingle();
        var detailScale = material.GetShaderParameter("signed_detail_scale").AsSingle();
        var tone = material.GetShaderParameter("tone_multiplier").AsVector3();
        Vector3 Sample(int x, int y, Color baseColor)
        {
            var detailX = Math.Min(
                detailImage.GetWidth() - 1,
                x * detailImage.GetWidth() / baseImage.GetWidth());
            var detailY = Math.Min(
                detailImage.GetHeight() - 1,
                y * detailImage.GetHeight() / baseImage.GetHeight());
            var detail = detailImage.GetPixel(detailX, detailY);
            return new Vector3(
                Math.Clamp(
                    (baseColor.R + detailScale * (detail.R - neutral)) * tone.X,
                    0.0f,
                    1.0f),
                Math.Clamp(
                    (baseColor.G + detailScale * (detail.G - neutral)) * tone.Y,
                    0.0f,
                    1.0f),
                Math.Clamp(
                    (baseColor.B + detailScale * (detail.B - neutral)) * tone.Z,
                    0.0f,
                    1.0f));
        }
        return AverageTextureColor(
            baseImage,
            Sample,
            minimumU,
            maximumU,
            minimumV,
            maximumV);
    }

    private static Vector3 AverageEncodedSkinColor(
        ShaderMaterial material,
        bool centralTorso)
    {
        var image = RequiredTextureImage(material, "base_map");
        return AverageTextureColor(image, (_, _, color) => new Vector3(
            color.R,
            color.G,
            color.B),
            centralTorso ? 0.25f : 0.0f,
            centralTorso ? 0.75f : 1.0f,
            centralTorso ? 0.25f : 0.0f,
            centralTorso ? 0.72f : 1.0f);
    }

    private static Vector3 AverageTextureColor(
        Image image,
        Func<int, int, Color, Vector3> convert,
        float minimumU,
        float maximumU,
        float minimumV,
        float maximumV)
    {
        var stepX = Math.Max(1, image.GetWidth() / 96);
        var stepY = Math.Max(1, image.GetHeight() / 96);
        var startX = Math.Clamp((int)(image.GetWidth() * minimumU), 0, image.GetWidth() - 1);
        var endX = Math.Clamp((int)(image.GetWidth() * maximumU), startX + 1, image.GetWidth());
        var startY = Math.Clamp((int)(image.GetHeight() * minimumV), 0, image.GetHeight() - 1);
        var endY = Math.Clamp((int)(image.GetHeight() * maximumV), startY + 1, image.GetHeight());
        var total = Vector3.Zero;
        var samples = 0;
        for (var y = startY; y < endY; y += stepY)
        {
            for (var x = startX; x < endX; x += stepX)
            {
                var color = image.GetPixel(x, y);
                if (color.A < 0.5f)
                    continue;
                total += convert(x, y, color);
                samples++;
            }
        }
        if (samples == 0)
            throw new InvalidOperationException(
                "Fallout 2 humanoid skin texture has no opaque samples.");
        return total / samples;
    }

    private static Image RequiredTextureImage(ShaderMaterial material, string parameter)
    {
        if (material.GetShaderParameter(parameter).AsGodotObject() is not Texture2D texture)
            throw new InvalidOperationException(
                $"Fallout 2 humanoid skin material has no {parameter} texture.");
        var image = texture.GetImage();
        if (image.IsEmpty())
            throw new InvalidOperationException(
                $"Fallout 2 humanoid skin material {parameter} texture is empty.");
        return image;
    }

    private void ApplyBodyProportions()
    {
        if (_locomotionSkeleton is not { } skeleton)
            return;
        CharacterBodyRig.Apply(
            _donorRoot,
            skeleton,
            _proportions,
            this,
            "fallout2-visible-humanoid");
    }

    private static CharacterBodyProportions ProportionsForIdentity(
        Fo2HumanoidIdentity identity) =>
        identity.Sex.Equals("Male", StringComparison.OrdinalIgnoreCase)
            ? new CharacterBodyProportions(
                "fo2-chosen-one-broad-upper-lean-lower-v1",
                1.01f,
                1.12f,
                1.10f,
                0.96f,
                1.03f,
                0.94f,
                0.92f)
            : CharacterBodyProportions.Neutral(
                "fo2-chosen-one-female-neutral-v1");

    private void ResolveEquipmentSocket(ActorModelSlice.LoadedActor donor)
    {
        if (_variant is null)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor variant is unavailable for socket resolution.");
        var matching = Descendants<Skeleton3D>(donor.Root)
            .Where(skeleton => skeleton.FindBone(_variant.EquipmentSocketNode) >= 0)
            .ToArray();
        if (matching.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor requires exactly one authored rigid attachment " +
                $"bone named {_variant.EquipmentSocketNode}; found {matching.Length}.");
        if (LegBoneNames.Any(name => matching[0].FindBone(name) < 0))
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor is missing its authored locomotion leg chain.");
        _locomotionSkeleton = matching[0];
        _equipmentSocket = new BoneAttachment3D
        {
            Name = "FO2_EQUIPMENT_AUTHORED_RIGHT_HAND_SOCKET",
            BoneName = _variant.EquipmentSocketNode,
        };
        matching[0].AddChild(_equipmentSocket);
        SetMeta("equipment_socket", _variant.EquipmentSocketNode);
        SetMeta("equipment_socket_resolved", true);
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

}
