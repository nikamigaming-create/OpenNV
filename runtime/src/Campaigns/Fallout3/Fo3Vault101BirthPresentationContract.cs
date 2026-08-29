using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Vault101BirthAsset(
    string Id,
    string LogicalPath,
    string SourceSha256,
    string ModelPath,
    string SidecarPath,
    int Surfaces,
    Vector3 BoundsMinGodotGameUnits,
    Vector3 BoundsMaxGodotGameUnits);

internal sealed record Fo3Vault101BirthReference(
    string FormId,
    string BaseFormId,
    string BaseRecordType,
    string BaseEditorId,
    string AssetId,
    Vector3 PositionGameUnits,
    Vector3 PositionGodotGameUnits,
    Vector3 RotationRadians,
    Quaternion RotationGodotQuaternion,
    float Scale);

internal sealed record Fo3Vault101DoctorActor(
    string ScenePath,
    string SceneSha256,
    string RecipeId,
    string ReferenceFormId,
    string BaseFormId,
    string Name,
    string RaceFormId,
    string HairFormId,
    string EyesFormId,
    IReadOnlyList<string> HeadPartFormIds,
    IReadOnlyList<string> OutfitFormIds,
    Vector3 PositionGameUnits,
    Vector3 PositionGodotGameUnits,
    Quaternion RotationGodotQuaternion,
    float Scale,
    string IdleAnimationPath,
    int Components,
    int Skins,
    int Surfaces,
    int Textures,
    int FaceGenMorphTargets);

internal sealed record Fo3Vault101DadActor(
    string ScenePath,
    string SceneSha256,
    string RecipeId,
    string ReferenceFormId,
    string BaseFormId,
    string Name,
    string RaceFormId,
    string HairFormId,
    string EyesFormId,
    IReadOnlyList<string> HeadPartFormIds,
    IReadOnlyList<string> OutfitFormIds,
    Vector3 AuthoredPositionGameUnits,
    Vector3 AuthoredPositionGodotGameUnits,
    Quaternion AuthoredRotationGodotQuaternion,
    float Scale,
    string StartMarkerReferenceFormId,
    Vector3 StartMarkerPositionGameUnits,
    Vector3 StartMarkerPositionGodotGameUnits,
    Quaternion StartMarkerRotationGodotQuaternion,
    string IdleAnimationPath,
    string BodySurfaceTextureSource,
    int Components,
    int Skins,
    int Surfaces,
    int Textures,
    int FaceGenMorphTargets);

internal sealed record Fo3Vault101BirthPresentationContract(
    string ManifestPath,
    string ManifestSha256,
    string RecipeId,
    string RecipeSha256,
    string CellFormId,
    string CellEditorId,
    float UnitsToMeters,
    string EntryReferenceFormId,
    Vector3 EntryPositionGameUnits,
    Vector3 EntryRotationRadians,
    Quaternion EntryRotationGodotQuaternion,
    string ProofCameraAuthority,
    string ProofCameraSupportReferenceFormId,
    string ProofCameraSupportBaseEditorId,
    string ProofCameraSupportAssetId,
    float ProofCameraSupportSurfaceGodotGameUnits,
    float ProofCameraSurfaceClearanceGameUnits,
    float ProofCameraNearGameUnits,
    Vector3 ProofCameraPositionGameUnits,
    Vector3 ProofCameraPositionGodotGameUnits,
    float VerticalFovDegrees,
    Color ProofAmbientColor,
    float ProofAmbientEnergy,
    Color ProofBackgroundColor,
    float ProofFogNearGameUnits,
    float ProofFogFarGameUnits,
    float ProofFogPower,
    int AuthoredTextureBindingRequests,
    int ResolvedUniqueTextures,
    Fo3Vault101DoctorActor DoctorActor,
    Fo3Vault101DadActor DadActor,
    IReadOnlyDictionary<string, Fo3Vault101BirthAsset> Assets,
    IReadOnlyList<Fo3Vault101BirthReference> References)
{
    internal const string ExpectedSchema = "opennv-fo3-vault101-birth-presentation/v4";
    private const string ExpectedStatus =
        "prepared-owned-materials-doctor-and-cg00-dad-not-yet-rendered";
    private const string ExpectedCellEditorId = "Vault101d";
    private const string ExpectedLightingAuthority =
        "recipe-proof-only-not-retail-CELL-lighting";
    private const string ExpectedMaterialAuthority =
        "owned-NIF-surface-identity-and-owned-DDS-bindings";
    private const string ExpectedProofCameraAuthority =
        "owned-CG00-support-mesh-top-derived-proof-only-not-retail-camera";
    private const string RequiredUnsupportedActors =
        "Mom, player body, and all actors except Doctor Li and CG00 Dad";
    private const string RequiredUnsupportedActorBehavior =
        "automatic CG00 dialogue timing, package state, and animation selection";
    private const string RequiredUnsupportedCommands = "quest and package command execution";
    private const int Sha256HexCharacters = 64;
    private const int FormIdHexCharacters = 8;

    internal static Fo3Vault101BirthPresentationContract Load(
        Fo3BirthSliceContract birthSlice,
        string manifestPath)
    {
        var path = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(path)!;
        var cacheRoot = Path.GetFullPath(Path.Combine(manifestDirectory, "..", ".."));
        var bytes = File.ReadAllBytes(path);
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "status") != ExpectedStatus)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth-presentation identity is unsupported.");

        VerifySourceBinding(birthSlice, RequiredObject(root, "source"));
        var recipe = RequiredObject(root, "recipe");
        var recipeId = RequiredString(recipe, "id");
        var recipeSha256 = RequiredSha256(recipe, "sha256");
        VerifyFile(RequiredString(recipe, "path"), recipeSha256);

        var cell = RequiredObject(root, "cell");
        var cellFormId = RequiredFormId(cell, "formId");
        var cellEditorId = RequiredString(cell, "editorId");
        if (cellFormId != birthSlice.CellFormId ||
            cellEditorId != ExpectedCellEditorId ||
            !RequiredBoolean(cell, "interior"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth-presentation CELL identity differs.");

        var coordinates = RequiredObject(root, "coordinates");
        if (RequiredString(coordinates, "source") !=
                "Gamebryo X-right/Y-forward/Z-up, radians" ||
            RequiredString(coordinates, "target") !=
                "Godot X-right/Y-up/-Z-forward")
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 coordinate contract is unsupported.");
        var unitsToMeters = RequiredPositiveSingle(coordinates, "unitsToMeters");
        var origin = ReadVector3(coordinates, "originGameUnits");

        var entry = RequiredObject(root, "entry");
        var entryReferenceFormId = RequiredFormId(entry, "referenceFormId");
        var entryPosition = ReadVector3(entry, "positionGameUnits");
        var entryLocalPosition = ReadVector3(entry, "positionGodotGameUnits");
        var entryRotation = ReadVector3(entry, "rotationRadians");
        var entryQuaternion = ReadQuaternion(entry, "rotationGodotQuaternion");
        if (RequiredString(entry, "source") != "owned-player-start-marker-transform" ||
            entryReferenceFormId != birthSlice.PlayerSpawnReferenceFormId ||
            !entryPosition.IsEqualApprox(ReadVector3(birthSlice.PlayerSpawnPositionGameUnits)) ||
            !entryRotation.IsEqualApprox(ReadVector3(birthSlice.PlayerSpawnRotationRadians)) ||
            !origin.IsEqualApprox(entryPosition) ||
            !entryLocalPosition.IsZeroApprox())
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 entry differs from the owned player marker.");

        var proofCamera = RequiredObject(root, "proofCamera");
        var proofCameraAuthority = RequiredString(proofCamera, "authority");
        var proofCameraSupportReferenceFormId = RequiredFormId(
            proofCamera,
            "supportReferenceFormId");
        var proofCameraSupportBaseEditorId = RequiredString(
            proofCamera,
            "supportBaseEditorId");
        var proofCameraSupportAssetId = RequiredString(proofCamera, "supportAssetId");
        var proofCameraSupportSurface = RequiredPositiveSingle(
            proofCamera,
            "supportSurfaceGodotGameUnits");
        var proofCameraClearance = RequiredPositiveSingle(
            proofCamera,
            "surfaceClearanceGameUnits");
        var proofCameraNear = RequiredPositiveSingle(proofCamera, "nearGameUnits");
        var proofCameraPosition = ReadVector3(proofCamera, "positionGameUnits");
        var proofCameraLocalPosition = ReadVector3(
            proofCamera,
            "positionGodotGameUnits");
        var proofCameraQuaternion = ReadQuaternion(
            proofCamera,
            "rotationGodotQuaternion");
        if (proofCameraAuthority != ExpectedProofCameraAuthority ||
            RequiredFormId(proofCamera, "entryReferenceFormId") != entryReferenceFormId ||
            proofCameraClearance <= proofCameraNear ||
            !proofCameraQuaternion.IsEqualApprox(entryQuaternion))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof-camera authority is unsupported.");

        var presentation = RequiredObject(root, "presentation");
        if (RequiredString(presentation, "lightingAuthority") != ExpectedLightingAuthority ||
            RequiredString(presentation, "materialAuthority") != ExpectedMaterialAuthority)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof-presentation authority is unsupported.");
        var verticalFovDegrees = RequiredPositiveSingle(presentation, "verticalFovDegrees");
        if (verticalFovDegrees >= 180.0f)
            throw new InvalidOperationException("Fallout 3 Vault 101 proof FOV is invalid.");
        var ambient = ReadColor(presentation, "proofAmbientColor");
        var ambientEnergy = RequiredPositiveSingle(presentation, "proofAmbientEnergy");
        var background = ReadColor(presentation, "proofBackgroundColor");
        var fogNear = RequiredFiniteSingle(presentation, "proofFogNearGameUnits");
        var fogFar = RequiredFiniteSingle(presentation, "proofFogFarGameUnits");
        var fogPower = RequiredPositiveSingle(presentation, "proofFogPower");
        if (fogFar <= fogNear)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof fog range is invalid.");

        var doctorActor = ReadDoctorActor(
            birthSlice,
            RequiredObject(root, "doctorActor"),
            cacheRoot,
            origin);
        var dadActor = ReadDadActor(
            birthSlice,
            RequiredObject(root, "dadActor"),
            cacheRoot,
            origin);

        var assets = RequiredArray(root, "assets")
            .EnumerateArray()
            .Select(value => ReadAsset(value, manifestDirectory))
            .ToDictionary(value => value.Id, StringComparer.Ordinal);
        var references = RequiredArray(root, "references")
            .EnumerateArray()
            .Select(ReadReference)
            .ToArray();
        if (assets.Count == 0 || references.Length == 0 ||
            references.Select(value => value.FormId).Distinct(StringComparer.Ordinal).Count() !=
                references.Length ||
            references.Any(value => !assets.ContainsKey(value.AssetId)))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation reference closure is incomplete.");
        var proofCameraSupports = references
            .Where(value => value.FormId == proofCameraSupportReferenceFormId)
            .ToArray();
        if (proofCameraSupports.Length != 1)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof-camera support reference is absent or ambiguous.");
        var proofCameraSupport = proofCameraSupports[0];
        if (proofCameraSupport.BaseEditorId != proofCameraSupportBaseEditorId ||
            proofCameraSupport.AssetId != proofCameraSupportAssetId ||
            !assets.TryGetValue(proofCameraSupportAssetId, out var proofCameraSupportAsset) ||
            !Mathf.IsZeroApprox(proofCameraSupport.RotationRadians.X) ||
            !Mathf.IsZeroApprox(proofCameraSupport.RotationRadians.Y))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof-camera support identity differs.");
        var expectedSupportSurface = proofCameraSupport.PositionGodotGameUnits.Y +
            proofCameraSupportAsset.BoundsMaxGodotGameUnits.Y * proofCameraSupport.Scale;
        var expectedLocalPosition = new Vector3(
            0.0f,
            expectedSupportSurface + proofCameraClearance,
            0.0f);
        var expectedGamePosition = entryPosition + new Vector3(
            0.0f,
            0.0f,
            expectedLocalPosition.Y);
        if (!Mathf.IsEqualApprox(proofCameraSupportSurface, expectedSupportSurface) ||
            !proofCameraLocalPosition.IsEqualApprox(expectedLocalPosition) ||
            !proofCameraPosition.IsEqualApprox(expectedGamePosition))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof camera differs from its owned support surface.");

        var textureIds = RequiredArray(root, "textures").EnumerateArray()
            .Select(value => VerifyTexture(value, cacheRoot))
            .ToArray();
        if (textureIds.Length == 0 ||
            textureIds.Distinct(StringComparer.Ordinal).Count() != textureIds.Length ||
            RequiredArray(root, "unresolvedTextureBindings").GetArrayLength() != 0)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 owned-texture closure is incomplete.");

        var coverage = RequiredObject(root, "coverage");
        if (RequiredInteger(coverage, "sourceCellReferences") != birthSlice.ReferenceCount ||
            RequiredInteger(coverage, "renderableAssets") != assets.Count ||
            RequiredInteger(coverage, "renderableReferences") != references.Length ||
            RequiredInteger(coverage, "selectedReferences") != references.Length ||
            RequiredInteger(coverage, "selectedUniqueModels") != assets.Count ||
            RequiredInteger(coverage, "nonPresentationAssets") != 0 ||
            RequiredInteger(coverage, "resolvedUniqueTextures") != textureIds.Length ||
            RequiredInteger(coverage, "unresolvedUniqueTextures") != 0 ||
            RequiredPositiveInteger(coverage, "authoredTextureBindingRequests") <
                textureIds.Length)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation coverage differs from its rows.");
        VerifyPromotion(RequiredObject(root, "promotion"));
        var unsupported = RequiredArray(root, "unsupported").EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);
        if (!unsupported.Contains(RequiredUnsupportedActors) ||
            !unsupported.Contains(RequiredUnsupportedActorBehavior) ||
            !unsupported.Contains(RequiredUnsupportedCommands))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 unsupported behavior boundary is incomplete.");

        return new Fo3Vault101BirthPresentationContract(
            path,
            manifestSha256,
            recipeId,
            recipeSha256,
            cellFormId,
            cellEditorId,
            unitsToMeters,
            entryReferenceFormId,
            entryPosition,
            entryRotation,
            entryQuaternion,
            proofCameraAuthority,
            proofCameraSupportReferenceFormId,
            proofCameraSupportBaseEditorId,
            proofCameraSupportAssetId,
            proofCameraSupportSurface,
            proofCameraClearance,
            proofCameraNear,
            proofCameraPosition,
            proofCameraLocalPosition,
            verticalFovDegrees,
            ambient,
            ambientEnergy,
            background,
            fogNear,
            fogFar,
            fogPower,
            RequiredPositiveInteger(coverage, "authoredTextureBindingRequests"),
            textureIds.Length,
            doctorActor,
            dadActor,
            assets,
            references);
    }

    private static Fo3Vault101DoctorActor ReadDoctorActor(
        Fo3BirthSliceContract birthSlice,
        JsonElement source,
        string cacheRoot,
        Vector3 origin)
    {
        const string actorSceneSchema = "opennv-actor-scene/v5";
        const string actorSceneStatus = "skinned-animated";
        const string actorRecipeId = "fo3-vault101-doctor-li-actor-v1";
        const string actorReferenceFormId = "000290a5";
        const string actorBaseFormId = "000290a3";
        const string actorEditorId = "CG00DoctorLi";
        const string actorName = "Doctor Li";
        const string actorRaceFormId = "000038e6";
        const string actorHairFormId = "00039626";
        const string actorEyesFormId = "00004256";
        const string actorIdlePath = "meshes\\characters\\_male\\locomotion\\mtidle.kf";

        if (RequiredString(source, "source") !=
                "transported-owned-ACHR-NPC-template-and-appearance-closure" ||
            RequiredString(source, "poseAuthority") !=
                "owned mtidle compiler input only; CG00 package and scripted idle selection " +
                "are not implemented")
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li ownership or pose authority is unsupported.");

        var scenePath = Path.GetFullPath(RequiredString(source, "scene"));
        VerifyCacheLocalDerivative(cacheRoot, scenePath);
        var sceneSha256 = RequiredSha256(source, "sha256");
        VerifyFile(scenePath, sceneSha256);
        var recipe = RequiredObject(source, "recipe");
        if (RequiredString(recipe, "id") != actorRecipeId)
            throw new InvalidOperationException("Fallout 3 Doctor Li recipe identity differs.");
        VerifyFile(RequiredString(recipe, "path"), RequiredSha256(recipe, "sha256"));
        var transportedModels = RequiredArray(source, "boundTransportedModels")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (transportedModels.Length != 13 || transportedModels.Contains(null) ||
            transportedModels.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                transportedModels.Length)
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li transported model closure differs.");

        using var birthDocument = JsonDocument.Parse(File.ReadAllBytes(birthSlice.Path));
        var ownedDoctor = RequiredObject(birthDocument.RootElement, "doctorActor");
        var ownedReference = RequiredObject(ownedDoctor, "reference");
        var ownedBase = RequiredObject(ownedDoctor, "base");
        var ownedAppearance = RequiredObject(ownedDoctor, "appearance");
        var ownedRace = RequiredObject(ownedAppearance, "race");
        var ownedHair = RequiredObject(ownedAppearance, "hair");
        var ownedEyes = RequiredObject(ownedAppearance, "eyes");
        var recordBindings = RequiredObject(source, "sourceRecordBindings");
        if (RequiredFormId(recordBindings, "referenceFormId") !=
                RequiredFormId(ownedReference, "formId") ||
            RequiredFormId(recordBindings, "baseFormId") !=
                RequiredFormId(ownedBase, "formId") ||
            RequiredSha256(recordBindings, "baseRecordDataSha256") !=
                RequiredSha256(ownedBase, "recordDataSha256") ||
            RequiredSha256(recordBindings, "raceRecordDataSha256") !=
                RequiredSha256(ownedRace, "recordDataSha256") ||
            RequiredSha256(recordBindings, "hairRecordDataSha256") !=
                RequiredSha256(ownedHair, "recordDataSha256") ||
            RequiredSha256(recordBindings, "eyesRecordDataSha256") !=
                RequiredSha256(ownedEyes, "recordDataSha256"))
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li record ownership differs from the birth slice.");

        using var actorDocument = JsonDocument.Parse(File.ReadAllBytes(scenePath));
        var actorRoot = actorDocument.RootElement;
        if (RequiredString(actorRoot, "schema") != actorSceneSchema ||
            RequiredString(actorRoot, "status") != actorSceneStatus ||
            RequiredString(actorRoot, "recipe") != actorRecipeId ||
            RequiredFormId(actorRoot, "cellFormId") != birthSlice.CellFormId)
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li compiled scene identity differs.");

        var reference = RequiredObject(actorRoot, "reference");
        var actor = RequiredObject(actorRoot, "actor");
        var actorPosition = ReadVector3(reference, "positionGameUnits");
        var ownedTransform = RequiredObject(ownedReference, "transform");
        var ownedPosition = ReadVector3(ownedTransform, "positionGameUnits");
        var localPosition = ReadVector3(reference, "positionGodotUnits");
        var expectedLocalPosition = new Vector3(
            ownedPosition.X - origin.X,
            ownedPosition.Z - origin.Z,
            -(ownedPosition.Y - origin.Y));
        var rotation = ReadVector3(reference, "rotationRadians");
        var ownedRotation = ReadVector3(ownedTransform, "rotationRadians");
        var quaternion = ReadQuaternion(reference, "rotationGodotQuaternion");
        var scale = RequiredPositiveSingle(reference, "scale");
        var headPartFormIds = ReadFormIdArray(actor, "headPartFormIds");
        var outfitFormIds = ReadFormIdArray(actor, "outfitFormIds");
        var ownedHeadParts = RequiredArray(ownedAppearance, "headParts")
            .EnumerateArray().Select(value => RequiredFormId(value, "formId")).ToArray();
        var ownedOutfits = RequiredArray(ownedAppearance, "outfits")
            .EnumerateArray().Select(value => RequiredFormId(value, "formId")).ToArray();
        if (RequiredFormId(reference, "formId") != actorReferenceFormId ||
            RequiredFormId(reference, "formId") != birthSlice.DoctorActorReferenceFormId ||
            RequiredFormId(reference, "baseFormId") != actorBaseFormId ||
            RequiredBoolean(reference, "initiallyDisabled") ||
            !actorPosition.IsEqualApprox(ownedPosition) ||
            !localPosition.IsEqualApprox(expectedLocalPosition) ||
            !rotation.IsEqualApprox(ownedRotation) ||
            !Mathf.IsEqualApprox(scale, RequiredPositiveSingle(ownedTransform, "scale")) ||
            RequiredString(actor, "recordType") != "NPC_" ||
            RequiredString(actor, "editorId") != actorEditorId ||
            RequiredString(actor, "name") != actorName ||
            !RequiredBoolean(actor, "female") ||
            RequiredFormId(actor, "raceFormId") != actorRaceFormId ||
            RequiredFormId(actor, "raceFormId") != RequiredFormId(ownedRace, "formId") ||
            RequiredFormId(actor, "hairFormId") != actorHairFormId ||
            RequiredFormId(actor, "hairFormId") != RequiredFormId(ownedHair, "formId") ||
            RequiredFormId(actor, "eyesFormId") != actorEyesFormId ||
            RequiredFormId(actor, "eyesFormId") != RequiredFormId(ownedEyes, "formId") ||
            !headPartFormIds.SequenceEqual(ownedHeadParts) ||
            !outfitFormIds.SequenceEqual(ownedOutfits))
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li actor identity or authored transform differs.");

        var declaredReference = RequiredObject(source, "reference");
        var declaredActor = RequiredObject(source, "actor");
        if (RequiredFormId(declaredReference, "formId") != actorReferenceFormId ||
            RequiredFormId(declaredReference, "baseFormId") != actorBaseFormId ||
            RequiredString(declaredActor, "editorId") != actorEditorId ||
            RequiredString(declaredActor, "name") != actorName)
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li presentation row differs from its actor scene.");

        var coverage = RequiredObject(actorRoot, "coverage");
        var components = RequiredPositiveInteger(coverage, "components");
        var skins = RequiredPositiveInteger(coverage, "skins");
        var surfaces = RequiredPositiveInteger(coverage, "surfaces");
        var textures = RequiredPositiveInteger(coverage, "textures");
        var morphTargets = RequiredPositiveInteger(coverage, "faceGenMorphTargets");
        if (!RequiredBoolean(coverage, "animated") ||
            RequiredInteger(coverage, "omittedSurfaces") != 0 ||
            RequiredString(actorRoot, "idleAnimation") != actorIdlePath)
            throw new InvalidOperationException(
                "Fallout 3 Doctor Li compiled appearance coverage differs.");

        return new Fo3Vault101DoctorActor(
            scenePath,
            sceneSha256,
            actorRecipeId,
            actorReferenceFormId,
            actorBaseFormId,
            actorName,
            actorRaceFormId,
            actorHairFormId,
            actorEyesFormId,
            headPartFormIds,
            outfitFormIds,
            actorPosition,
            localPosition,
            quaternion,
            scale,
            actorIdlePath,
            components,
            skins,
            surfaces,
            textures,
            morphTargets);
    }

    private static Fo3Vault101DadActor ReadDadActor(
        Fo3BirthSliceContract birthSlice,
        JsonElement source,
        string cacheRoot,
        Vector3 origin)
    {
        const string actorSceneSchema = "opennv-actor-scene/v5";
        const string actorSceneStatus = "skinned-animated";
        const string actorRecipeId = "fo3-vault101-dad-actor-v1";
        const string actorReferenceFormId = "000290a7";
        const string actorBaseFormId = "000290a6";
        const string actorEditorId = "CG00Dad";
        const string actorName = "Dad";
        const string actorRaceFormId = "000042c3";
        const string actorHairFormId = "0002ddee";
        const string actorEyesFormId = "00004255";
        const string actorIdlePath = "meshes\\characters\\_male\\locomotion\\mtidle.kf";
        const string bodyModPolicy =
            "owned-race-base-diffuse-when-precomputed-absent";
        const string bodySurfaceTextureSource =
            "owned-race-base-diffuse-no-body-mod";

        if (RequiredString(source, "source") !=
                "direct-owned-CG00Dad-ACHR-NPC-race-and-FaceGen" ||
            RequiredString(source, "poseAuthority") !=
                "owned mtidle compiler input and exact stage-0 MoveTo marker only; " +
                "CG00 package idle selection is not implemented" ||
            RequiredString(source, "bodyModPolicy") != bodyModPolicy ||
            RequiredString(source, "bodySurfaceTextureSource") != bodySurfaceTextureSource)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad ownership or presentation authority is unsupported.");

        var scenePath = Path.GetFullPath(RequiredString(source, "scene"));
        VerifyCacheLocalDerivative(cacheRoot, scenePath);
        var sceneSha256 = RequiredSha256(source, "sha256");
        VerifyFile(scenePath, sceneSha256);
        var recipe = RequiredObject(source, "recipe");
        if (RequiredString(recipe, "id") != actorRecipeId)
            throw new InvalidOperationException("Fallout 3 CG00 Dad recipe identity differs.");
        VerifyFile(RequiredString(recipe, "path"), RequiredSha256(recipe, "sha256"));

        using var birthDocument = JsonDocument.Parse(File.ReadAllBytes(birthSlice.Path));
        var startGraph = RequiredObject(birthDocument.RootElement, "startGraph");
        var fatherRows = RequiredArray(startGraph, "actors").EnumerateArray()
            .Where(value => RequiredString(value, "role") == "father")
            .ToArray();
        if (fatherRows.Length != 1)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad birth actor identity is absent or ambiguous.");
        var father = fatherRows[0];
        var ownedReference = RequiredObject(father, "reference");
        var ownedMarker = RequiredObject(father, "startMarker");
        var bases = RequiredArray(
                RequiredObject(birthDocument.RootElement, "cellGraph"),
                "bases")
            .EnumerateArray()
            .Where(value => RequiredFormId(value, "formId") == actorBaseFormId)
            .ToArray();
        if (bases.Length != 1)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad base ownership is absent or ambiguous.");
        var ownedBase = bases[0];
        var recordBindings = RequiredObject(source, "sourceRecordBindings");
        if (RequiredFormId(recordBindings, "referenceFormId") !=
                RequiredFormId(ownedReference, "formId") ||
            RequiredFormId(recordBindings, "baseFormId") !=
                RequiredFormId(ownedBase, "formId") ||
            RequiredSha256(recordBindings, "baseRecordDataSha256") !=
                RequiredSha256(ownedBase, "recordDataSha256"))
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad record ownership differs from the birth slice.");

        using var actorDocument = JsonDocument.Parse(File.ReadAllBytes(scenePath));
        var actorRoot = actorDocument.RootElement;
        if (RequiredString(actorRoot, "schema") != actorSceneSchema ||
            RequiredString(actorRoot, "status") != actorSceneStatus ||
            RequiredString(actorRoot, "recipe") != actorRecipeId ||
            RequiredFormId(actorRoot, "cellFormId") != birthSlice.CellFormId ||
            RequiredString(actorRoot, "bodyModPolicy") != bodyModPolicy ||
            RequiredString(actorRoot, "bodySurfaceTextureSource") !=
                bodySurfaceTextureSource ||
            !actorRoot.TryGetProperty("bodyModLogicalPath", out var bodyModPath) ||
            bodyModPath.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad compiled scene identity differs.");

        var reference = RequiredObject(actorRoot, "reference");
        var actor = RequiredObject(actorRoot, "actor");
        var ownedTransform = RequiredObject(ownedReference, "transform");
        var actorPosition = ReadVector3(reference, "positionGameUnits");
        var ownedPosition = ReadVector3(ownedTransform, "positionGameUnits");
        var actorLocalPosition = ReadVector3(reference, "positionGodotUnits");
        var expectedLocalPosition = new Vector3(
            ownedPosition.X - origin.X,
            ownedPosition.Z - origin.Z,
            -(ownedPosition.Y - origin.Y));
        var actorRotation = ReadVector3(reference, "rotationRadians");
        var ownedRotation = ReadVector3(ownedTransform, "rotationRadians");
        var actorQuaternion = ReadQuaternion(reference, "rotationGodotQuaternion");
        var scale = RequiredPositiveSingle(reference, "scale");
        if (RequiredFormId(reference, "formId") != actorReferenceFormId ||
            RequiredFormId(reference, "baseFormId") != actorBaseFormId ||
            RequiredBoolean(reference, "initiallyDisabled") ||
            !actorPosition.IsEqualApprox(ownedPosition) ||
            !actorLocalPosition.IsEqualApprox(expectedLocalPosition) ||
            !actorRotation.IsEqualApprox(ownedRotation) ||
            !Mathf.IsEqualApprox(scale, RequiredPositiveSingle(ownedTransform, "scale")) ||
            RequiredString(actor, "recordType") != "NPC_" ||
            RequiredString(actor, "editorId") != actorEditorId ||
            RequiredString(actor, "name") != actorName ||
            RequiredBoolean(actor, "female") ||
            RequiredFormId(actor, "raceFormId") != actorRaceFormId ||
            RequiredFormId(actor, "hairFormId") != actorHairFormId ||
            RequiredFormId(actor, "eyesFormId") != actorEyesFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad actor identity or authored transform differs.");

        var marker = RequiredObject(source, "startMarker");
        var ownedMarkerTransform = RequiredObject(ownedMarker, "transform");
        var markerPosition = ReadVector3(marker, "positionGameUnits");
        var ownedMarkerPosition = ReadVector3(
            ownedMarkerTransform,
            "positionGameUnits");
        var markerLocalPosition = ReadVector3(marker, "positionGodotGameUnits");
        var expectedMarkerLocalPosition = new Vector3(
            ownedMarkerPosition.X - origin.X,
            ownedMarkerPosition.Z - origin.Z,
            -(ownedMarkerPosition.Y - origin.Y));
        var markerRotation = ReadVector3(marker, "rotationRadians");
        if (RequiredFormId(marker, "referenceFormId") !=
                RequiredFormId(ownedMarker, "formId") ||
            !markerPosition.IsEqualApprox(ownedMarkerPosition) ||
            !markerLocalPosition.IsEqualApprox(expectedMarkerLocalPosition) ||
            !markerRotation.IsEqualApprox(
                ReadVector3(ownedMarkerTransform, "rotationRadians")))
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad stage-0 start marker differs from the owned slice.");

        var coverage = RequiredObject(actorRoot, "coverage");
        var components = RequiredPositiveInteger(coverage, "components");
        var skins = RequiredPositiveInteger(coverage, "skins");
        var surfaces = RequiredPositiveInteger(coverage, "surfaces");
        var textures = RequiredPositiveInteger(coverage, "textures");
        var morphTargets = RequiredPositiveInteger(coverage, "faceGenMorphTargets");
        if (!RequiredBoolean(coverage, "animated") ||
            RequiredInteger(coverage, "omittedSurfaces") != 0 ||
            RequiredString(actorRoot, "idleAnimation") != actorIdlePath)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Dad compiled appearance coverage differs.");

        return new Fo3Vault101DadActor(
            scenePath,
            sceneSha256,
            actorRecipeId,
            actorReferenceFormId,
            actorBaseFormId,
            actorName,
            actorRaceFormId,
            actorHairFormId,
            actorEyesFormId,
            ReadFormIdArray(actor, "headPartFormIds"),
            ReadFormIdArray(actor, "outfitFormIds"),
            actorPosition,
            actorLocalPosition,
            actorQuaternion,
            scale,
            RequiredFormId(marker, "referenceFormId"),
            markerPosition,
            markerLocalPosition,
            ReadQuaternion(marker, "rotationGodotQuaternion"),
            actorIdlePath,
            bodySurfaceTextureSource,
            components,
            skins,
            surfaces,
            textures,
            morphTargets);
    }

    private static Fo3Vault101BirthAsset ReadAsset(
        JsonElement source,
        string manifestDirectory)
    {
        var modelPath = Path.GetFullPath(RequiredString(source, "model"));
        var sidecarPath = Path.GetFullPath(RequiredString(source, "sidecar"));
        VerifyCacheLocalDerivative(manifestDirectory, modelPath);
        VerifyCacheLocalDerivative(manifestDirectory, sidecarPath);
        var surfaces = RequiredPositiveInteger(source, "surfaces");
        var bounds = RequiredObject(source, "boundsGodotGameUnits");
        var boundsMinimum = ReadVector3(bounds, "min");
        var boundsMaximum = ReadVector3(bounds, "max");
        if (boundsMaximum.X < boundsMinimum.X ||
            boundsMaximum.Y < boundsMinimum.Y ||
            boundsMaximum.Z < boundsMinimum.Z)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 asset bounds are invalid.");
        var materials = RequiredArray(source, "materials").EnumerateArray().ToArray();
        if (materials.Length != surfaces || materials.Any(material =>
                RequiredArray(material, "unresolvedTextureRoles").GetArrayLength() != 0))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 material binding closure is incomplete.");
        return new Fo3Vault101BirthAsset(
            RequiredString(source, "id"),
            RequiredString(source, "logicalPath"),
            RequiredSha256(source, "sourceSha256"),
            modelPath,
            sidecarPath,
            surfaces,
            boundsMinimum,
            boundsMaximum);
    }

    private static string VerifyTexture(JsonElement source, string cacheRoot)
    {
        var id = RequiredString(source, "id");
        VerifyCacheLocalDerivative(cacheRoot, Path.GetFullPath(RequiredString(source, "dds")));
        VerifyCacheLocalDerivative(cacheRoot, Path.GetFullPath(RequiredString(source, "png")));
        _ = RequiredSha256(source, "ddsSha256");
        _ = RequiredSha256(source, "pngSha256");
        return id;
    }

    private static Fo3Vault101BirthReference ReadReference(JsonElement source)
    {
        if (RequiredBoolean(source, "initiallyDisabled"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation admitted a disabled reference.");
        var recordType = RequiredString(source, "baseRecordType");
        if (recordType is "NPC_" or "CREA")
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 geometry presentation admitted an actor base.");
        return new Fo3Vault101BirthReference(
            RequiredFormId(source, "formId"),
            RequiredFormId(source, "baseFormId"),
            recordType,
            RequiredString(source, "baseEditorId"),
            RequiredString(source, "assetId"),
            ReadVector3(source, "positionGameUnits"),
            ReadVector3(source, "positionGodotGameUnits"),
            ReadVector3(source, "rotationRadians"),
            ReadQuaternion(source, "rotationGodotQuaternion"),
            RequiredPositiveSingle(source, "scale"));
    }

    private static void VerifySourceBinding(
        Fo3BirthSliceContract birthSlice,
        JsonElement source)
    {
        if (!Path.GetFullPath(RequiredString(source, "birthSlice"))
                .Equals(Path.GetFullPath(birthSlice.Path), StringComparison.OrdinalIgnoreCase) ||
            !RequiredSha256(source, "birthSliceSha256")
                .Equals(birthSlice.Sha256, StringComparison.OrdinalIgnoreCase) ||
            RequiredString(source, "birthSliceRecipeId") != birthSlice.RecipeId)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation differs from its owned birth slice.");
        using var birthDocument = JsonDocument.Parse(File.ReadAllBytes(birthSlice.Path));
        var birthSource = RequiredObject(birthDocument.RootElement, "source");
        VerifyArchiveBinding(
            RequiredObject(source, "texturesArchive"),
            RequiredObject(birthSource, "texturesArchive"));
    }

    private static void VerifyArchiveBinding(JsonElement actual, JsonElement expected)
    {
        if (!RequiredString(actual, "file").Equals(
                RequiredString(expected, "file"),
                StringComparison.OrdinalIgnoreCase) ||
            RequiredLong(actual, "bytes") != RequiredLong(expected, "bytes") ||
            !RequiredSha256(actual, "sha256").Equals(
                RequiredSha256(expected, "sha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 owned archive binding differs.");
    }

    private static void VerifyPromotion(JsonElement promotion)
    {
        if (!RequiredBoolean(promotion, "transported") ||
            !RequiredBoolean(promotion, "texturesPrepared") ||
            !RequiredBoolean(promotion, "doctorActorPrepared") ||
            !RequiredBoolean(promotion, "dadActorPrepared") ||
            RequiredBoolean(promotion, "runtimeManifestValidated") ||
            RequiredBoolean(promotion, "runtimeSceneConstructed") ||
            RequiredBoolean(promotion, "rendered") ||
            RequiredBoolean(promotion, "interactive") ||
            RequiredBoolean(promotion, "actorsRendered") ||
            RequiredBoolean(promotion, "questCommandsExecuted") ||
            RequiredBoolean(promotion, "parityReviewed") ||
            RequiredBoolean(promotion, "headsetAccepted"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 prepared-presentation promotion state is unsupported.");
    }

    private static void VerifyCacheLocalDerivative(string manifestDirectory, string path)
    {
        var relative = Path.GetRelativePath(manifestDirectory, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 derivative escapes its local cache: {path}");
    }

    private static void VerifyFile(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(Path.GetFullPath(path));
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Fallout 3 file hash differs: {path}");
    }

    private static Vector3 ReadVector3(JsonElement parent, string name) =>
        ReadVector3(RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray());

    private static IReadOnlyList<string> ReadFormIdArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray()
            .Select(value =>
            {
                var formId = value.GetString();
                if (string.IsNullOrWhiteSpace(formId) || formId.Length != FormIdHexCharacters ||
                    formId.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidOperationException(
                        $"Fallout 3 Vault 101 FormID array {name} is invalid.");
                return formId;
            })
            .ToArray();

    private static Vector3 ReadVector3(IReadOnlyList<float> values)
    {
        if (values.Count != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 3 Vault 101 vector is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement parent, string name)
    {
        var values = RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 quaternion {name} is invalid.");
        var result = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!Mathf.IsEqualApprox(result.LengthSquared(), 1.0f))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 quaternion {name} is not normalized.");
        return result;
    }

    private static Color ReadColor(JsonElement parent, string name)
    {
        var values = RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => value is < 0.0f or > 1.0f))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 color {name} is invalid.");
        return new Color(values[0], values[1], values[2]);
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value.GetString()!;
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != FormIdHexCharacters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 Vault 101 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Sha256HexCharacters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 Vault 101 SHA-256 {name} is invalid.");
        return value;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) ||
            result < 0)
            throw new InvalidOperationException($"Fallout 3 Vault 101 integer {name} is invalid.");
        return result;
    }

    private static int RequiredPositiveInteger(JsonElement parent, string name)
    {
        var result = RequiredInteger(parent, name);
        if (result <= 0)
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 positive integer {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) ||
            result <= 0)
            throw new InvalidOperationException($"Fallout 3 Vault 101 integer {name} is invalid.");
        return result;
    }

    private static float RequiredFiniteSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 Vault 101 number {name} is invalid.");
        return result;
    }

    private static float RequiredPositiveSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) || result <= 0.0f)
            throw new InvalidOperationException($"Fallout 3 Vault 101 number {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"Fallout 3 Vault 101 boolean {name} is invalid.");
        return value.GetBoolean();
    }
}
