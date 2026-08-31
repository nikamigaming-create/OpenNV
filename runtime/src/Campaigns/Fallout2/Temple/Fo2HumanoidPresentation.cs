using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.Presentation.Actors;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2HumanoidDonorVariant(
    string Sex,
    string SourceActorFormId,
    string? Campaign,
    string? CharacterId,
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
    string EquipmentSocketNode,
    CharacterBodyProportions? BodyProfile,
    Fo2HumanoidAppearance? DefaultAppearance);

/// <summary>Shared consumer for the FNV full-body player-preview artifact.
/// Its sex-keyed modular body/outfit sources and authored rigid socket are
/// verified before either classic campaign can use the assembled actor.</summary>
internal sealed record Fo2HumanoidDonorContract(
    string ManifestPath,
    string ManifestSha256,
    string BaseManifestPath,
    string BaseManifestSha256,
    string SourceActorFormId,
    IReadOnlyDictionary<string, Fo2HumanoidDonorVariant> Variants,
    IReadOnlyDictionary<string, Fo2HumanoidDonorVariant> CharacterVariants)
{
    private const string PreviewSetSchemaV3 = "opennv-owned-player-facegen-preview-set/v3";
    private const string PreviewSetSchemaV4 = "opennv-owned-player-facegen-preview-set/v4";
    private const string PreviewSetStatusV3 =
        "compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-all-native-geometry-controls-runtime-bound";
    private const string PreviewSetStatusV4 =
        "compiled-default-custom-and-six-classic-premade-full-body-analogs-runtime-bound";
    private const string EquipmentSocketNode = "Bip01 R Hand";
    private static readonly string[] RequiredBodyRoles = ["body", "left-hand", "right-hand"];
    private static readonly string[] AnalogBodyRoles = ["outfit-0", "left-hand", "right-hand"];
    private static readonly string[] RequiredAnalogKeys =
    [
        "fallout1:max-stone",
        "fallout1:natalia",
        "fallout1:albert",
        "fallout2:combat",
        "fallout2:stealth",
        "fallout2:diplomat",
    ];

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
        var schema = Required(root, "schema");
        var expectedStatus = schema switch
        {
            PreviewSetSchemaV3 => PreviewSetStatusV3,
            PreviewSetSchemaV4 => PreviewSetStatusV4,
            _ => "",
        };
        if (string.IsNullOrEmpty(expectedStatus) ||
            Required(root, "status") != expectedStatus ||
            !root.GetProperty("fullBody").GetBoolean() ||
            !root.GetProperty("bodyComponentRoles").EnumerateArray()
                .Select(value => Required(value)).SequenceEqual(RequiredBodyRoles, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Unexpected classic humanoid donor preview set: {path}");
        var outfit = Required(root, "presentationOutfitFormId");
        if (outfit.Length != 8 || !outfit.All(Uri.IsHexDigit))
            throw new InvalidOperationException(
                "Classic humanoid donor has no hash-bound outfit FormID.");
        var sourceActorFormId = RequiredFormId(root, "playerFormId");
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
            VerifyEquipmentSocket(model.RootElement, "classic humanoid donor");
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
                sex, sourceActorFormId, null, null,
                modelPath, sidecarPath, modelSha256, sidecarSha256,
                surfaces.Length, sidecarRoot.GetProperty("textures").GetArrayLength(),
                sidecarRoot.GetProperty("animations").GetArrayLength(), RequiredBodyRoles,
                outfit, socket, EquipmentSocketNode, null, null);
        }).ToDictionary(value => value.Sex, StringComparer.Ordinal);
        if (!variants.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["male", "female"]))
            throw new InvalidOperationException("Classic humanoid donor sex variants are incomplete.");
        var characterVariants = schema == PreviewSetSchemaV4
            ? root.GetProperty("premadeAnalogs").EnumerateArray()
                .Select(LoadAnalogVariant)
                .ToDictionary(
                    value => VariantKey(value.Campaign!, value.CharacterId!),
                    StringComparer.Ordinal)
            : new Dictionary<string, Fo2HumanoidDonorVariant>(StringComparer.Ordinal);
        if (schema == PreviewSetSchemaV4 &&
            !characterVariants.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(RequiredAnalogKeys))
            throw new InvalidOperationException(
                "Classic humanoid donor premade analog bindings are incomplete.");
        var baseManifestPath = path;
        var baseManifestSha256 = Sha256(bytes);
        if (schema == PreviewSetSchemaV4)
        {
            var basePreview = root.GetProperty("basePreviewSet");
            baseManifestPath = Path.GetFullPath(Required(basePreview, "path"));
            baseManifestSha256 = RequiredHash(basePreview, "sha256");
            VerifyFile(
                baseManifestPath,
                baseManifestSha256,
                "classic humanoid base preview set");
        }
        return new Fo2HumanoidDonorContract(
            path,
            Sha256(bytes),
            baseManifestPath,
            baseManifestSha256,
            sourceActorFormId,
            variants,
            characterVariants);
    }

    internal Fo2HumanoidDonorVariant ForSex(string sex) =>
        Variants.TryGetValue(sex.ToLowerInvariant(), out var variant)
            ? variant
            : throw new InvalidOperationException(
                $"Classic humanoid donor has no source-bound variant for {sex}.");

    internal Fo2HumanoidDonorVariant ForIdentity(Fo2HumanoidIdentity identity) =>
        ForClassicCharacter(identity.Campaign, identity.CharacterId, identity.Sex);

    internal Fo2HumanoidDonorVariant ForClassicCharacter(
        string campaign,
        string characterId,
        string sex)
    {
        var key = VariantKey(campaign, characterId);
        if (CharacterVariants.TryGetValue(key, out var variant))
        {
            if (!variant.Sex.Equals(sex, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Classic humanoid analog sex differs from {key}.");
            return variant;
        }
        if (CharacterVariants.Count > 0 &&
            RequiredAnalogKeys.Contains(key, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Classic humanoid donor has no exact premade analog for {key}.");
        return ForSex(sex);
    }

    internal bool HasPremadeAnalogs => CharacterVariants.Count == RequiredAnalogKeys.Length;

    private static Fo2HumanoidDonorVariant LoadAnalogVariant(JsonElement row)
    {
        var campaign = Required(row, "campaign").ToLowerInvariant();
        var characterId = Required(row, "characterId").ToLowerInvariant();
        var sex = Required(row, "sex").ToLowerInvariant();
        if (campaign is not "fallout1" and not "fallout2" ||
            sex is not "male" and not "female")
            throw new InvalidOperationException(
                "Classic humanoid premade analog identity is invalid.");
        var sourceActorFormId = RequiredFormId(row, "sourceActorFormId");
        var outfitFormId = RequiredFormId(row, "outfitFormId");
        var bodyRoles = row.GetProperty("bodyRoles").EnumerateArray()
            .Select(Required).ToArray();
        if (!bodyRoles.SequenceEqual(AnalogBodyRoles, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Classic humanoid premade analog body roles differ for {campaign}:{characterId}.");
        var outputs = row.GetProperty("outputs");
        var modelPath = Path.GetFullPath(Required(outputs, "gltf"));
        var sidecarPath = Path.GetFullPath(Required(outputs, "sidecar"));
        var modelSha256 = RequiredHash(outputs, "gltfSha256");
        var sidecarSha256 = RequiredHash(outputs, "sidecarSha256");
        VerifyFile(modelPath, modelSha256, "classic premade analog model");
        VerifyFile(sidecarPath, sidecarSha256, "classic premade analog sidecar");
        using var sidecar = JsonDocument.Parse(File.ReadAllBytes(sidecarPath));
        using var model = JsonDocument.Parse(File.ReadAllBytes(modelPath));
        var sidecarRoot = sidecar.RootElement;
        var surfaces = sidecarRoot.GetProperty("surfaces").EnumerateArray().ToArray();
        var textures = sidecarRoot.GetProperty("textures").GetArrayLength();
        var animations = sidecarRoot.GetProperty("animations").EnumerateArray().ToArray();
        var coverage = row.GetProperty("coverage");
        if (Required(sidecarRoot, "schema") != "opennv-actor-gltf/v4" ||
            Required(sidecarRoot, "status") != "skinned-animated" ||
            RequiredFormId(sidecarRoot, "actorFormId") != sourceActorFormId ||
            coverage.GetProperty("surfaces").GetInt32() != surfaces.Length ||
            coverage.GetProperty("textures").GetInt32() != textures ||
            coverage.GetProperty("animations").GetInt32() != animations.Length ||
            bodyRoles.Any(role => surfaces.Count(surface =>
                Required(surface, "role") == role) < 1) ||
            !animations.Any(animation => Required(animation, "logicalPath").Contains(
                "idle", StringComparison.OrdinalIgnoreCase)) ||
            !animations.Any(animation => Required(animation, "logicalPath").Contains(
                "forward", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Classic humanoid premade analog coverage differs for {campaign}:{characterId}.");
        VerifyEquipmentSocket(model.RootElement, $"classic premade analog {campaign}:{characterId}");
        var skeleton = sidecarRoot.GetProperty("skeleton");
        var rigidAttachmentNode = Required(row, "rigidAttachmentNode");
        if (rigidAttachmentNode != Required(skeleton, "rigidAttachmentNode") ||
            Required(row, "equipmentSocketNode") != EquipmentSocketNode)
            throw new InvalidOperationException(
                $"Classic humanoid premade analog skeleton differs for {campaign}:{characterId}.");
        var body = row.GetProperty("bodyProfile");
        var bodyProfile = new CharacterBodyProportions(
            Required(body, "id"),
            body.GetProperty("height").GetSingle(),
            body.GetProperty("chest").GetSingle(),
            body.GetProperty("shoulders").GetSingle(),
            body.GetProperty("waist").GetSingle(),
            body.GetProperty("arms").GetSingle(),
            body.GetProperty("thighs").GetSingle(),
            body.GetProperty("calves").GetSingle());
        bodyProfile.Validate($"classic-premade-analog-{campaign}-{characterId}");
        var appearance = row.TryGetProperty("appearance", out var appearanceRow)
            ? new Fo2HumanoidAppearance(
                Required(appearanceRow, "faceShapeId"),
                Required(appearanceRow, "hairStyleId"),
                Required(appearanceRow, "skinToneId"),
                Required(appearanceRow, "hairColorId"),
                Required(appearanceRow, "eyeColorId"),
                Required(appearanceRow, "browStyleId"),
                Required(appearanceRow, "noseStyleId"),
                Required(appearanceRow, "mouthStyleId"))
            : null;
        return new Fo2HumanoidDonorVariant(
            sex,
            sourceActorFormId,
            campaign,
            characterId,
            modelPath,
            sidecarPath,
            modelSha256,
            sidecarSha256,
            surfaces.Length,
            textures,
            animations.Length,
            bodyRoles,
            outfitFormId,
            rigidAttachmentNode,
            EquipmentSocketNode,
            bodyProfile,
            appearance);
    }

    private static void VerifyEquipmentSocket(JsonElement modelRoot, string label)
    {
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
                $"{label} model has no unique skinned {EquipmentSocketNode}.");
    }

    private static string VariantKey(string campaign, string characterId) =>
        $"{campaign.Trim().ToLowerInvariant()}:{characterId.Trim().ToLowerInvariant()}";

    private static string RequiredFormId(JsonElement source, string property)
    {
        var value = Required(source, property).ToLowerInvariant();
        return value.Length == 8 && value.All(Uri.IsHexDigit)
            ? value
            : throw new InvalidOperationException(
                $"Owned humanoid donor FormID is invalid: {property}");
    }

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
    string Campaign,
    string CharacterId,
    string Name,
    string Role,
    string Sex,
    string OwnedIdentitySha256,
    string OwnedPanelSha256,
    string SourceFid,
    string SourceFrmSha256,
    Fo2HumanoidAppearance? Appearance)
{
    internal static Fo2HumanoidIdentity FromPremade(Fo2PremadeCharacter character) => new(
        "fallout2",
        character.Id,
        character.Profile.Name,
        character.Role,
        character.Profile.Sex,
        character.GcdSha256,
        character.Panel.SourceSha256,
        "picker-preview",
        character.Panel.SourceSha256,
        null);

    internal static Fo2HumanoidIdentity FromSelection(
        Fo2CharacterSelection? selection,
        Fo2ArroyoPlayerPresentationSource source)
    {
        if (selection is null)
            return new Fo2HumanoidIdentity(
                "fallout2",
                "source-default",
                "Chosen One",
                "Source default",
                source.Fid == Fo2CharacterStartCatalog.FemaleFid ? "Female" : "Male",
                source.PrototypeSha256,
                "none",
                source.Fid,
                source.SourceSha256,
                null);
        return new Fo2HumanoidIdentity(
            "fallout2",
            selection.Id,
            selection.Profile.Name,
            selection.Role,
            selection.Profile.Sex,
            selection.GcdSha256,
            selection.Source.Panel.SourceSha256,
            source.Fid,
            source.SourceSha256,
            selection.Appearance.CustomFaceEdited
                ? Fo2HumanoidAppearance.FromContract(selection.Appearance)
                : null);
    }
}

internal sealed record Fo2HumanoidAppearance(
    string FaceShapeId,
    string HairStyleId,
    string SkinToneId,
    string HairColorId,
    string EyeColorId,
    string BrowStyleId,
    string NoseStyleId,
    string MouthStyleId)
{
    internal static Fo2HumanoidAppearance FromContract(
        Fo2CharacterAppearanceContract appearance) => new(
        appearance.FaceShapeId,
        appearance.HairStyleId,
        appearance.SkinToneId,
        appearance.HairColorId,
        appearance.EyeColorId,
        appearance.BrowStyleId,
        appearance.NoseStyleId,
        appearance.MouthStyleId);
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
    private Fo2HumanoidAppearance? _appearance;
    private IReadOnlyList<string> _appliedFaceGeometryControls = [];
    private bool _walking;

    internal Fo2HumanoidVisual(
        Fo2HumanoidIdentity identity,
        Fo2HumanoidDonorContract? contract,
        CharacterBodyProportions? proportions = null)
    {
        _identity = identity;
        _appearance = identity.Appearance;
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
    internal Fo2HumanoidAppearance? Appearance => _appearance;
    internal int AppliedFaceGeometryControlCount => _appliedFaceGeometryControls.Count;
    internal Aabb PresentationBounds => _donor is { } donor
        ? ActorModelSlice.PosedWorldBounds(donor)
        : throw new InvalidOperationException(
            "Fallout 2 humanoid presentation bounds are unavailable.");
    internal Vector3 PortraitHeadWorldPosition
    {
        get
        {
            var skeleton = _locomotionSkeleton ?? throw new InvalidOperationException(
                "Fallout 2 humanoid portrait skeleton is unavailable.");
            var head = skeleton.FindBone("Bip01 Head");
            if (head < 0)
                throw new InvalidOperationException(
                    "Fallout 2 humanoid portrait donor has no Bip01 Head bone.");
            var headWorld = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head);
            if (!headWorld.Origin.IsFinite())
                throw new InvalidOperationException(
                    "Fallout 2 humanoid portrait head transform is invalid.");
            return headWorld.Origin;
        }
    }

    internal void SetProportions(CharacterBodyProportions proportions)
    {
        proportions.Validate("fallout2-visible-humanoid");
        _proportions = proportions;
        ApplyBodyProportions();
    }

    internal void SetAppearance(Fo2HumanoidAppearance appearance)
    {
        _appearance = appearance;
        ApplyCharacterAppearance();
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
        SetMeta("campaign", identity.Campaign);
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
            _variant = _contract.ForIdentity(_identity);
            _proportions = _variant.BodyProfile ?? _proportions;
            _proportions.Validate("fallout2-visible-humanoid-donor-binding");
            _appearance = _identity.Appearance ?? _variant.DefaultAppearance;
            var loaded = ActorModelSlice.Load(
                _variant.ModelPath,
                _variant.SidecarPath,
                _donorRoot);
            if (loaded.FormId != _variant.SourceActorFormId ||
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
            ApplyCharacterAppearance();
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
            SetMeta("donor_source_actor_form_id", _variant.SourceActorFormId);
            SetMeta("donor_campaign", _variant.Campaign ?? "custom");
            SetMeta("donor_character_id", _variant.CharacterId ?? "custom");
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
        var headSkinColor = ActorComplexionMath.AverageFaceGenEncodedSkinColor(
            headToneMaterials[0]);
        var neckSkinColor = ActorComplexionMath.AverageFaceGenEncodedNeckColor(
            headToneMaterials[0]);
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
        var joinedRoles = new HashSet<string>(StringComparer.Ordinal);
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
                var sourceSkinColor = ActorComplexionMath.AverageEncodedSkinColor(
                    material,
                    centralTorso: surface.Role == _variant.BodyRoles[0]);
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
                joinedRoles.Add(surface.Role);
            }
        }
        var expectedSkinRoles = donor.Surfaces
            .Where(surface => skinNodes.Contains(surface.RuntimeNodeName) &&
                _variant.BodyRoles.Contains(surface.Role, StringComparer.Ordinal))
            .Select(surface => surface.Role)
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedSkinRoles.Contains("left-hand") ||
            !expectedSkinRoles.Contains("right-hand") ||
            !expectedSkinRoles.IsSubsetOf(joinedRoles))
            throw new InvalidOperationException(
                "Fallout 2 humanoid donor has no complete visible-skin join.");
        SetMeta(
            "skin_join_mode",
            "owned-shaderskin-detail-with-facegen-neck-and-cheek-complexion-v9");
        SetMeta("skin_join_materials", joined);
        SetMeta("skin_join_roles", string.Join(",", joinedRoles.Order(StringComparer.Ordinal)));
        SetMeta("skin_join_head_tone", headTone);
        SetMeta("skin_join_target_color", headSkinColor);
        SetMeta("skin_join_neck_source_color", neckSkinColor);
        SetMeta("skin_join_target_role", "head-paired-cheek-uv-islands");
    }

    private void ApplyCharacterAppearance()
    {
        if (_donor is not { } donor || _appearance is not { } appearance)
            return;
        if (_variant is null)
            throw new InvalidOperationException(
                "Fallout 2 humanoid appearance has no selected donor variant.");
        var catalog = Fo2ProceduralAppearanceCatalog.Load();
        var controls = catalog.NativeFaceGenControls(
            appearance.FaceShapeId,
            appearance.BrowStyleId,
            appearance.NoseStyleId,
            appearance.MouthStyleId);
        foreach (var settingEntity in _appliedFaceGeometryControls)
            ApplyNativeFaceGenControl(donor, settingEntity, 0.0f);
        foreach (var control in controls)
            ApplyNativeFaceGenControl(
                donor,
                control.Key,
                control.Value * catalog.LiveHead.NativeMorphWeightScale);
        _appliedFaceGeometryControls = controls.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();

        var skin = catalog.SkinTone(appearance.SkinToneId).HeadAlbedo;
        var skinTarget = new Vector3(skin.R, skin.G, skin.B);
        if (!skinTarget.IsFinite())
            throw new InvalidOperationException(
                "Fallout 2 custom 3D complexion target is invalid.");
        var headMaterials = donor.Surfaces
            .Where(surface => surface.Role == "head")
            .SelectMany(SurfaceMaterials)
            .Where(material => material.Shader?.Code.Contains(
                "uniform bool use_complexion_target;",
                StringComparison.Ordinal) == true)
            .ToArray();
        if (headMaterials.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 custom 3D appearance requires one FaceGen complexion surface.");
        var headSource = ActorComplexionMath.AverageFaceGenEncodedSkinColor(
            headMaterials[0]);
        headMaterials[0].SetShaderParameter("use_complexion_target", true);
        headMaterials[0].SetShaderParameter("complexion_target", skinTarget);
        headMaterials[0].SetShaderParameter(
            "complexion_source_mean",
            ActorComplexionMath.Mean(headSource));
        headMaterials[0].SetShaderParameter("use_neck_complexion_target", false);

        var joinedSkinMaterials = 0;
        foreach (var surface in donor.Surfaces.Where(surface =>
                     _variant.BodyRoles.Contains(surface.Role, StringComparer.Ordinal)))
        {
            foreach (var material in SurfaceMaterials(surface).Where(material =>
                         material.Shader?.Code.Contains(
                             "uniform bool use_skin_transfer;",
                             StringComparison.Ordinal) == true &&
                         material.GetShaderParameter("use_skin_transfer").AsBool()))
            {
                var source = ActorComplexionMath.AverageEncodedSkinColor(
                    material,
                    centralTorso: surface.Role == _variant.BodyRoles[0]);
                material.SetShaderParameter("skin_complexion_multiplier", Vector3.One);
                material.SetShaderParameter("use_skin_complexion_target", true);
                material.SetShaderParameter("skin_complexion_target", skinTarget);
                material.SetShaderParameter(
                    "skin_complexion_source_mean",
                    ActorComplexionMath.Mean(source));
                joinedSkinMaterials++;
            }
        }
        if (joinedSkinMaterials < 3)
            throw new InvalidOperationException(
                "Fallout 2 custom complexion did not reach the torso and both hands.");

        var hair = catalog.HairColor(appearance.HairColorId).HeadAlbedo;
        var tintedHairMaterials = 0;
        foreach (var material in donor.Surfaces
                     .Where(surface => surface.Role == "hair")
                     .SelectMany(SurfaceMaterials)
                     .Where(material => material.Shader?.Code.Contains(
                         "uniform vec4 base_color_factor;",
                         StringComparison.Ordinal) == true))
        {
            var original = material.GetShaderParameter("base_color_factor").AsColor();
            material.SetShaderParameter(
                "base_color_factor",
                new Color(hair.R, hair.G, hair.B, original.A));
            tintedHairMaterials++;
        }
        if (tintedHairMaterials == 0)
            throw new InvalidOperationException(
                "Fallout 2 custom hair color has no owned hair material target.");

        SetMeta("custom_face_shape_id", appearance.FaceShapeId);
        SetMeta("custom_skin_tone_id", appearance.SkinToneId);
        SetMeta("custom_hair_color_id", appearance.HairColorId);
        SetMeta("custom_facegen_control_count", controls.Count);
        SetMeta(
            "custom_facegen_controls",
            string.Join(",", controls.OrderBy(row => row.Key).Select(row =>
                $"{row.Key}:{row.Value:F1}")));
        SetMeta("custom_skin_target", skinTarget);
        SetMeta(
            "custom_hair_style_disposition",
            $"{appearance.HairStyleId}:source-sex-default-geometry-until-owned-style-set-exists");
        SetMeta(
            "custom_eye_color_disposition",
            $"{appearance.EyeColorId}:source-default-until-iris-only-mask-is-bound");
    }

    private static int ApplyNativeFaceGenControl(
        ActorModelSlice.LoadedActor donor,
        string settingEntity,
        float weight)
    {
        if (!float.IsFinite(weight))
            throw new InvalidOperationException(
                $"Fallout 2 native FaceGen weight is invalid: {settingEntity}.");
        var bindings = 0;
        foreach (var surface in donor.Surfaces)
        {
            for (var index = 0; index < surface.FaceGenMorphTargets.Count; index++)
            {
                if (surface.FaceGenMorphTargets[index] != settingEntity)
                    continue;
                surface.Mesh.SetBlendShapeValue(index, weight);
                bindings++;
            }
        }
        if (bindings == 0)
            throw new InvalidOperationException(
                $"Fallout 2 owned donor has no native FaceGen target {settingEntity}.");
        return bindings;
    }

    private static IEnumerable<ShaderMaterial> SurfaceMaterials(
        ActorModelSlice.LoadedSurface surface) => Enumerable.Range(
            0,
            surface.Mesh.Mesh?.GetSurfaceCount() ?? 0)
        .Select(surface.Mesh.GetSurfaceOverrideMaterial)
        .OfType<ShaderMaterial>();

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
        Fo2HumanoidIdentity identity) => Fo2CharacterBodyProfile.ForSex(identity.Sex);

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
