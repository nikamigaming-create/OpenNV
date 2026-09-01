using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private static string RequiredSaveString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value.GetString()!;
    }

    private static int RequiredSaveInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return result;
    }

    private static float RequiredSaveSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return result;
    }

    private static Vector3 RequiredSaveVector3(JsonElement parent, string name)
    {
        var values = RequiredSaveArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static JsonElement RequiredSaveObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value;
    }

    private static JsonElement RequiredSaveArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value;
    }

    private static bool RequiredSaveBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value.GetBoolean();
    }

    private void ValidateRemovedPlayerPackageState(JsonElement source)
    {
        var transition = _profile.Section4Transition;
        if (RequiredSaveString(source, "schema") != "opennv-fo3-player-package-state/v1" ||
            RequiredSaveBoolean(source, "active") ||
            RequiredSaveString(source, "formId") != transition.PackageFormId ||
            RequiredSaveString(source, "editorId") != transition.PackageEditorId ||
            RequiredSaveString(source, "locationReferenceFormId") !=
                transition.LocationReferenceFormId ||
            RequiredSaveString(source, "nextCommand") != transition.NextCommand ||
            RequiredSaveInteger(source, "nextStage") != transition.NextStage)
            throw new InvalidOperationException(
                "Saved Fallout 3 removed player package differs from the profile.");
        var idles = RequiredSaveArray(source, "idleFormIds").EnumerateArray()
            .Select(value => value.GetString() ?? "")
            .ToArray();
        if (idles.Any(string.IsNullOrWhiteSpace) ||
            !transition.IdleFormIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(idles))
            throw new InvalidOperationException(
                "Saved Fallout 3 removed player-package idles differ.");
    }

    private void ValidateBirthRuntimeState(JsonElement source, string expectedCueState)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Saved Fallout 3 birth runtime has no owned presentation contract.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Saved Fallout 3 birth runtime has no constructed presentation.");
        var cg01DadAppearance = coverage.Cg01DadAppearance;
        var cg01DadActor = cg01DadAppearance.Actor;
        var transition = _profile.Section4Transition;
        if (RequiredSaveString(source, "schema") != "opennv-fo3-cg00-birth-runtime/v2" ||
            RequiredSaveString(source, "cellFormId") != contract.CellFormId ||
            RequiredSaveString(source, "entryReferenceFormId") != contract.EntryReferenceFormId ||
            RequiredSaveString(source, "doctorLiReferenceFormId") !=
                contract.DoctorActor.ReferenceFormId ||
            RequiredSaveString(source, "dadReferenceFormId") !=
                contract.DadActor.ReferenceFormId ||
            RequiredSaveString(source, "cg01DadReferenceFormId") !=
                cg01DadActor.ReferenceFormId ||
            !RequiredSaveVector3(source, "cg01DadRawMarkerPositionGodotGameUnits")
                .IsEqualApprox(cg01DadActor.StartMarkerPositionGodotGameUnits) ||
            !RequiredSaveVector3(source, "cg01DadPresentationPositionGodotGameUnits")
                .IsEqualApprox(
                    coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits) ||
            !Mathf.IsEqualApprox(
                RequiredSaveSingle(source, "cg01DadGroundingCorrectionGodotGameUnits"),
                coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits) ||
            RequiredSaveString(source, "cg01DadAppearance") !=
                "source-stage65-match-race-50-percent-facegen-applied" ||
            RequiredSaveString(source, "cg01DadPlayerRaceFormId") !=
                cg01DadAppearance.PlayerRaceFormId ||
            RequiredSaveString(source, "cg01DadPlayerSex") != cg01DadAppearance.PlayerSex ||
            RequiredSaveString(source, "cg01DadSceneSha256") !=
                cg01DadActor.SceneSha256 ||
            RequiredSaveString(source, "cg01DadSymmetricGeometrySha256") !=
                cg01DadAppearance.SymmetricGeometrySha256 ||
            RequiredSaveString(source, "cg01DadAsymmetricGeometrySha256") !=
                cg01DadAppearance.AsymmetricGeometrySha256 ||
            RequiredSaveString(source, "cg01DadSymmetricTextureSha256") !=
                cg01DadAppearance.SymmetricTextureSha256 ||
            RequiredSaveString(source, "beginEventIdleFormId") !=
                transition.BeginEventIdleFormId ||
            RequiredSaveString(source, "changeEventIdleFormId") !=
                transition.ChangeEventIdleFormId ||
            RequiredSaveString(source, "triggerScriptEditorId") !=
                transition.TriggerScriptEditorId ||
            RequiredSaveString(source, "triggerScriptFormId") !=
                transition.TriggerScriptFormId ||
            RequiredSaveString(source, "triggerScriptSourceSha256") !=
                transition.TriggerScriptSourceSha256 ||
            RequiredSaveString(source, "triggerCondition") != transition.TriggerCondition ||
            RequiredSaveString(source, "triggerCommand") != transition.NextCommand ||
            RequiredSaveInteger(source, "triggeredStage") != transition.NextStage ||
            RequiredSaveString(source, "cueState") != expectedCueState)
            throw new InvalidOperationException(
                "Saved Fallout 3 birth runtime differs from its owned source contracts.");
    }

    private Dictionary<string, object?> BirthRuntimeState(string cueState)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 birth runtime has no owned presentation contract.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 birth runtime has no constructed presentation.");
        var cg01DadAppearance = coverage.Cg01DadAppearance;
        var cg01DadActor = cg01DadAppearance.Actor;
        var transition = _profile.Section4Transition;
        return new Dictionary<string, object?>
        {
            ["schema"] = "opennv-fo3-cg00-birth-runtime/v2",
            ["cellFormId"] = contract.CellFormId,
            ["entryReferenceFormId"] = contract.EntryReferenceFormId,
            ["doctorLiReferenceFormId"] = contract.DoctorActor.ReferenceFormId,
            ["dadReferenceFormId"] = contract.DadActor.ReferenceFormId,
            ["cg01DadReferenceFormId"] = cg01DadActor.ReferenceFormId,
            ["cg01DadRawMarkerPositionGodotGameUnits"] = new[]
            {
                cg01DadActor.StartMarkerPositionGodotGameUnits.X,
                cg01DadActor.StartMarkerPositionGodotGameUnits.Y,
                cg01DadActor.StartMarkerPositionGodotGameUnits.Z,
            },
            ["cg01DadPresentationPositionGodotGameUnits"] = new[]
            {
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.X,
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.Y,
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.Z,
            },
            ["cg01DadGroundingCorrectionGodotGameUnits"] =
                coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits,
            ["cg01DadAppearance"] =
                "source-stage65-match-race-50-percent-facegen-applied",
            ["cg01DadPlayerRaceFormId"] = cg01DadAppearance.PlayerRaceFormId,
            ["cg01DadPlayerSex"] = cg01DadAppearance.PlayerSex,
            ["cg01DadSceneSha256"] = cg01DadActor.SceneSha256,
            ["cg01DadSymmetricGeometrySha256"] =
                cg01DadAppearance.SymmetricGeometrySha256,
            ["cg01DadAsymmetricGeometrySha256"] =
                cg01DadAppearance.AsymmetricGeometrySha256,
            ["cg01DadSymmetricTextureSha256"] =
                cg01DadAppearance.SymmetricTextureSha256,
            ["beginEventIdleFormId"] = transition.BeginEventIdleFormId,
            ["endEventIdleFormId"] = transition.EndEventIdleFormId,
            ["changeEventIdleFormId"] = transition.ChangeEventIdleFormId,
            ["triggerScriptEditorId"] = transition.TriggerScriptEditorId,
            ["triggerScriptFormId"] = transition.TriggerScriptFormId,
            ["triggerScriptSourceSha256"] = transition.TriggerScriptSourceSha256,
            ["triggerCondition"] = transition.TriggerCondition,
            ["triggerCommand"] = transition.NextCommand,
            ["triggeredStage"] = transition.NextStage,
            ["cueState"] = cueState,
        };
    }

    private void PersistNamedCharacter(string playerName, Fo3SexChoice sex)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.Stage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            nextCommand = _profile.AppearanceCommand,
            completed = false,
        };
        WriteState(state);
    }

    private object SavedFaceControls(Fo3AppearanceSelection selection) =>
        _profile.Appearance.FaceControls.Select(control => new
        {
            settingEntity = control.SettingEntity,
            axisSha256 = control.AxisSha256,
            value = selection.FaceControlValue(control.SettingEntity),
        }).ToArray();

    private object SavedTextureControls(Fo3AppearanceSelection selection)
    {
        var controls = _profile.Appearance.PreviewFor(
            selection,
            selection.Race.Sex.Single(value => value.Value == selection.Sex).Key)
            .TextureControls;
        return controls.Select(control => new
        {
            settingEntity = control.SettingEntity,
            axisSha256 = control.AxisSha256,
            value = selection.TextureControlValues[control.SettingEntity],
        }).ToArray();
    }

    private Fo3AppearanceSelection LoadSavedFaceControls(
        JsonElement faceGen,
        Fo3AppearanceSelection selection)
    {
        var saved = RequiredSaveArray(faceGen, "geometryControls").EnumerateArray().ToArray();
        if (saved.Length != _profile.Appearance.FaceControls.Count)
            throw new InvalidOperationException(
                "Saved Fallout 3 FaceGen control count differs from the profile.");
        foreach (var control in _profile.Appearance.FaceControls)
        {
            var row = saved.Single(value =>
                RequiredSaveString(value, "settingEntity") == control.SettingEntity);
            if (RequiredSaveString(row, "axisSha256") != control.AxisSha256)
                throw new InvalidOperationException(
                    "Saved Fallout 3 FaceGen control identity differs from the profile.");
            selection = _profile.Appearance.ApplyFaceControl(
                selection,
                control,
                RequiredSaveSingle(row, "value"));
        }
        if (faceGen.TryGetProperty("textureControls", out var textureRows))
        {
            var controls = _profile.Appearance.PreviewFor(
                selection,
                selection.Race.Sex.Single(value => value.Value == selection.Sex).Key)
                .TextureControls;
            var rows = textureRows.EnumerateArray().ToArray();
            if (rows.Length != controls.Count)
                throw new InvalidOperationException(
                    "Saved Fallout 3 FaceGen tone count differs from the profile.");
            var values = selection.TextureControlValues.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (var control in controls)
            {
                var row = rows.Single(value =>
                    RequiredSaveString(value, "settingEntity") == control.SettingEntity);
                if (RequiredSaveString(row, "axisSha256") != control.AxisSha256)
                    throw new InvalidOperationException(
                        "Saved Fallout 3 FaceGen tone identity differs from the profile.");
                values[control.SettingEntity] = RequiredSaveSingle(row, "value");
                if (values[control.SettingEntity] < _profile.Appearance.FaceControl.Minimum ||
                    values[control.SettingEntity] > _profile.Appearance.FaceControl.Maximum)
                    throw new InvalidOperationException(
                        "Saved Fallout 3 FaceGen tone value is outside the profile.");
            }
            var preview = _profile.Appearance.PreviewFor(
                selection,
                selection.Race.Sex.Single(value => value.Value == selection.Sex).Key);
            var textureSha256 = OwnedGamebryoFaceGenTextureRuntime.CoordinateSha256(
                preview.SymmetricTexture,
                controls,
                values,
                _profile.Appearance.FaceControl.MorphWeightScale);
            selection = selection with
            {
                Sex = selection.Sex with
                {
                    FaceGen = selection.Sex.FaceGen with
                    {
                        SymmetricTextureSha256 = textureSha256,
                    },
                },
                TextureControlValues = values,
            };
        }
        return selection;
    }

    private void PersistAppearance(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.AcceptedStage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                    geometryControls = SavedFaceControls(selection),
                    textureControls = SavedTextureControls(selection),
                },
            },
            nextCommand = _profile.Appearance.AcceptedStageCommand,
            completed = false,
        };
        WriteState(state);
    }

    private void PersistSection4Package(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage package)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.AcceptedStage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                    geometryControls = SavedFaceControls(selection),
                    textureControls = SavedTextureControls(selection),
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = true,
                formId = package.FormId,
                editorId = package.EditorId,
                locationReferenceFormId = package.LocationReferenceFormId,
                idleFormIds = package.IdleFormIds,
                nextCommand = package.NextCommand,
                nextStage = package.NextStage,
            },
            nextCommand = package.NextCommand,
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage65Appearance(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage package,
        Fo3Stage65AppearanceState stage65,
        Fo3PlayerPackageRuntimeActivation? birthActivation = null)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = stage65.Stage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                    geometryControls = SavedFaceControls(selection),
                    textureControls = SavedTextureControls(selection),
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = true,
                formId = package.FormId,
                editorId = package.EditorId,
                locationReferenceFormId = package.LocationReferenceFormId,
                idleFormIds = package.IdleFormIds,
                nextCommand = package.NextCommand,
                nextStage = package.NextStage,
            },
            stage65Appearance = new
            {
                schema = Fo3Stage65AppearanceTransition.ExpectedSchema,
                stage = stage65.Stage,
                appliedCommandCount = stage65.AppliedCommandCount,
                playerFaceGen = new
                {
                    symmetricGeometrySha256 = stage65.PlayerSymmetricGeometrySha256,
                    asymmetricGeometrySha256 = stage65.PlayerAsymmetricGeometrySha256,
                    symmetricTextureSha256 = stage65.PlayerSymmetricTextureSha256,
                },
                parents = stage65.Parents.Select(parent => new
                {
                    referenceFormId = parent.ReferenceFormId,
                    referenceEditorId = parent.ReferenceEditorId,
                    baseFormId = parent.BaseFormId,
                    raceFormId = parent.RaceFormId,
                    symmetricGeometrySha256 = parent.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = parent.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = parent.SymmetricTextureSha256,
                }),
                nextBoundary = stage65.NextBoundary,
            },
            birthRuntime = birthActivation is null
                ? null
                : BirthRuntimeState("stage65-source-bound-ready"),
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage80Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State? stage85 = null,
        Fo3Stage90State? stage90 = null,
        Fo3Stage100State? stage100 = null,
        Fo3Cg01Stage0State? cg01 = null,
        Fo3Cg01Stage10State? cg01Stage10 = null,
        Fo3Cg01Stage12State? cg01Stage12 = null,
        Fo3Cg01ToddlerWorldState? cg01ToddlerWorld = null,
        Fo3Cg01Stage14State? cg01Stage14 = null,
        Fo3Cg01Stage20State? cg01Stage20 = null)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = cg01Stage20?.ActiveStage ?? cg01Stage14?.ActiveStage ?? cg01Stage12?.ActiveStage ??
                cg01Stage10?.ActiveStage ?? cg01?.ActiveStage ?? stage100?.Stage ??
                stage90?.Stage ?? stage85?.Stage ?? stage80.Stage,
            activeQuest = cg01 is null
                ? null
                : new
                {
                    formId = cg01.ActiveQuestFormId,
                    editorId = cg01.ActiveQuestEditorId,
                    stage = cg01Stage20?.ActiveStage ?? cg01Stage14?.ActiveStage ?? cg01Stage12?.ActiveStage ??
                        cg01Stage10?.ActiveStage ?? cg01.ActiveStage,
                },
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                    geometryControls = SavedFaceControls(selection),
                    textureControls = SavedTextureControls(selection),
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = stage100 is null,
                formId = section4Package.FormId,
                editorId = section4Package.EditorId,
                locationReferenceFormId = section4Package.LocationReferenceFormId,
                idleFormIds = section4Package.IdleFormIds,
                nextCommand = section4Package.NextCommand,
                nextStage = section4Package.NextStage,
            },
            stage65Appearance = new
            {
                schema = Fo3Stage65AppearanceTransition.ExpectedSchema,
                stage = stage65.Stage,
                appliedCommandCount = stage65.AppliedCommandCount,
                playerFaceGen = new
                {
                    symmetricGeometrySha256 = stage65.PlayerSymmetricGeometrySha256,
                    asymmetricGeometrySha256 = stage65.PlayerAsymmetricGeometrySha256,
                    symmetricTextureSha256 = stage65.PlayerSymmetricTextureSha256,
                },
                parents = stage65.Parents.Select(parent => new
                {
                    referenceFormId = parent.ReferenceFormId,
                    referenceEditorId = parent.ReferenceEditorId,
                    baseFormId = parent.BaseFormId,
                    raceFormId = parent.RaceFormId,
                    symmetricGeometrySha256 = parent.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = parent.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = parent.SymmetricTextureSha256,
                }),
                nextBoundary = stage65.NextBoundary,
            },
            stage80Transition = new
            {
                schema = Fo3Stage80Transition.ExpectedSchema,
                stage = stage80.Stage,
                appliedInfoFormId = stage80.AppliedInfoFormId,
                appliedCommandCount = stage80.AppliedCommandCount,
                addedPlayerPackage = new
                {
                    active = true,
                    formId = stage80.AddedPlayerPackage.FormId,
                    editorId = stage80.AddedPlayerPackage.EditorId,
                    locationReferenceFormId = stage80.AddedPlayerPackage.LocationReferenceFormId,
                    idleFormIds = stage80.AddedPlayerPackage.IdleFormIds,
                },
                scriptVariables = stage80.ScriptVariables.Select(variable => new
                {
                    referenceFormId = variable.ReferenceFormId,
                    referenceEditorId = variable.ReferenceEditorId,
                    variable = variable.Variable,
                    value = variable.Value,
                }),
                evaluatedPackageReferences = stage80.EvaluatedPackageReferences.Select(
                    reference => new
                    {
                        formId = reference.FormId,
                        editorId = reference.EditorId,
                    }),
                enabledReferences = stage80.EnabledReferences.Select(reference => new
                {
                    formId = reference.FormId,
                    editorId = reference.EditorId,
                }),
                nextBoundary = stage80.NextBoundary,
            },
            stage85Transition = stage85 is null
                ? null
                : new
                {
                    schema = Fo3Stage85Transition.ExpectedSchema,
                    stage = stage85.Stage,
                    appliedInfoFormId = stage85.AppliedInfoFormId,
                    appliedCommandCount = stage85.AppliedCommandCount,
                    nextBoundary = stage85.NextBoundary,
                },
            stage90Transition = stage90 is null
                ? null
                : new
                {
                    schema = Fo3Stage90Transition.ExpectedSchema,
                    stage = stage90.Stage,
                    appliedInfoFormId = stage90.AppliedInfoFormId,
                    appliedCommandCount = stage90.AppliedCommandCount,
                    questVariables = stage90.QuestVariables.Select(variable => new
                    {
                        name = variable.Name,
                        type = variable.Type,
                        value = variable.Value,
                    }),
                    imageSpaceModifier = new
                    {
                        formId = stage90.ImageSpaceModifier.FormId,
                        editorId = stage90.ImageSpaceModifier.EditorId,
                        recordSha256 = stage90.ImageSpaceModifier.RecordSha256,
                    },
                    sound = new
                    {
                        formId = stage90.Sound.FormId,
                        editorId = stage90.Sound.EditorId,
                        assetSha256 = stage90.Sound.Asset.Sha256,
                    },
                    imageSpaceFadeApplied = stage90.ImageSpaceFadeApplied,
                    imageSpaceOtherChannelsApplied = stage90.ImageSpaceOtherChannelsApplied,
                    soundStarted = stage90.SoundStarted,
                    timerAdvancing = stage90.TimerAdvancing,
                    nextBoundary = stage90.NextBoundary,
                },
            stage100Transition = stage100 is null
                ? null
                : new
                {
                    schema = Fo3Stage100Transition.ExpectedSchema,
                    stage = stage100.Stage,
                    accountedCommandCount = stage100.AccountedCommandCount,
                    appliedCommandCount = stage100.AppliedCommandCount,
                    timerRemainingSeconds = stage100.TimerRemainingSeconds,
                    timerAdvancing = stage100.TimerAdvancing,
                    playerScriptPackageActive = stage100.PlayerScriptPackageActive,
                    scriptVariables = stage100.ScriptVariables.Select(variable => new
                    {
                        referenceFormId = variable.ReferenceFormId,
                        referenceEditorId = variable.ReferenceEditorId,
                        variable = variable.Variable,
                        value = variable.Value,
                    }),
                    removedImageSpaceModifier = new
                    {
                        formId = stage100.RemovedImageSpaceModifier.FormId,
                        editorId = stage100.RemovedImageSpaceModifier.EditorId,
                        recordSha256 = stage100.RemovedImageSpaceModifier.RecordSha256,
                    },
                    disabledDad = new
                    {
                        formId = stage100.DisabledDad.FormId,
                        editorId = stage100.DisabledDad.EditorId,
                    },
                    cg00Running = stage100.Cg00Running,
                    playerYoung = stage100.PlayerYoung,
                    nextBoundary = new
                    {
                        commandIndex = 7,
                        kind = "setStage",
                        questFormId = stage100.NextBoundary.QuestFormId,
                        questEditorId = stage100.NextBoundary.QuestEditorId,
                        stage = stage100.NextBoundary.Stage,
                        stageResultSourceSha256 =
                            stage100.NextBoundary.StageResultSourceSha256,
                        stageResultCommandCount =
                            stage100.NextBoundary.StageResultCommandCount,
                        transitionContract = new
                        {
                            schema = stage100.NextBoundary.TransitionContract.Schema,
                            sha256 = stage100.NextBoundary.TransitionContract.Sha256,
                        },
                        applied = stage100.NextBoundary.Applied,
                        blocker = stage100.NextBoundary.Blocker,
                    },
                },
            cg01Stage0Transition = cg01 is null
                ? null
                : _profile.Cg01Stage0Transition.SavedState(cg01),
            cg01Stage10Transition = cg01Stage10 is null
                ? null
                : _profile.Cg01Stage10Transition.SavedState(cg01Stage10),
            cg01Stage12Transition = cg01Stage12 is null
                ? null
                : _profile.Cg01Stage12Transition.SavedState(cg01Stage12),
            cg01ToddlerWorld = cg01ToddlerWorld is null
                ? null
                : _profile.Cg01ToddlerWorld.SavedState(cg01ToddlerWorld),
            cg01Stage12DadResponse = cg01Stage14 is null
                ? null
                : _profile.Cg01Stage12DadResponse.SavedState(cg01Stage14),
            cg01PostStage14Transition = cg01Stage20 is null
                ? null
                : _profile.Cg01PostStage14Transition.SavedState(cg01Stage20),
            birthRuntime = BirthRuntimeState(cg01Stage20 is not null
                ? "cg01-stage20-package-dialogue-sequence-applied"
                : cg01Stage14 is not null
                ? "cg01-stage14-dad-response-applied-package-evaluated"
                : cg01Stage12 is not null
                ? "cg01-stage12-physical-trigger-applied-post-stage12-blocked"
                : cg01Stage10 is not null
                ? "cg01-stage10-toddler-world-active"
                : cg01 is not null
                ? "cg01-stage0-stage5-applied-dad-dialogue-pending"
                : stage100 is not null
                ? "stage90-timer-finished-stage100-applied"
                : stage90 is not null
                    ? "stage85-info-finished-stage90-applied"
                : stage85 is not null
                    ? "stage80-info-trigger-stage85-applied"
                    : "stage65-cue-finished-stage80-applied"),
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage85Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85);

    private void PersistStage90Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90);

    private void PersistStage100Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90,
        Fo3Stage100State stage100) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100);

    private void PersistCg01Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90,
        Fo3Stage100State stage100,
        Fo3Cg01Stage0State cg01) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100,
            cg01);

    private void PersistCg01Stage10Transition(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10);

    private void PersistCg01Stage12Transition(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10,
        Fo3Cg01Stage12State cg01Stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10,
            cg01Stage12,
            toddlerWorld);

    private void PersistCg01Stage14Response(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10,
        Fo3Cg01Stage12State cg01Stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State cg01Stage14) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10,
            cg01Stage12,
            toddlerWorld,
            cg01Stage14);

    private void PersistCg01Stage20Transition(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10,
        Fo3Cg01Stage12State cg01Stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State cg01Stage14,
        Fo3Cg01Stage20State cg01Stage20) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10,
            cg01Stage12,
            toddlerWorld,
            cg01Stage14,
            cg01Stage20);

    private void WriteState(object state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_savePath)!);
        var temporary = _savePath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }
}
