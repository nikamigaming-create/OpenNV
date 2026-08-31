using System.Security.Cryptography;
using System.Text.Json;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed partial record OpeningNewGameFlow
{
    private static void Validate(OpeningNewGameFlow flow)
    {
        if (string.IsNullOrWhiteSpace(flow.QuestFormId) ||
            string.IsNullOrWhiteSpace(flow.QuestEditorId) ||
            flow.ReferenceCanvasSize.X <= 0.0f ||
            flow.ReferenceCanvasSize.Y <= 0.0f ||
            !flow.Stages.ContainsKey(flow.CompletionStage) ||
            !flow.Stages.ContainsKey(flow.PsychologyStartStage) ||
            !flow.Stages.ContainsKey(flow.OutroStartStage) ||
            !flow.TopicsByFormId.ContainsKey(flow.OutroTopicFormId) ||
            flow.Menus.Count == 0 ||
            flow.Strings.Count == 0 ||
            flow.SceneRoles.Count == 0 ||
            flow.Interactions.Count == 0 ||
            !flow.SceneRoles.TryGetValue(
                flow.DialogueVoice.SpeakerRole,
                out var dialogueSpeaker) ||
            !dialogueSpeaker.ReferenceFormId.Equals(
                flow.DialogueVoice.SpeakerReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !dialogueSpeaker.BaseFormId.Equals(
                flow.DialogueVoice.SpeakerBaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !flow.SceneRoles.TryGetValue(flow.GuideActorAi.Role, out var guideRole) ||
            !guideRole.ReferenceFormId.Equals(
                flow.GuideActorAi.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !guideRole.BaseFormId.Equals(
                flow.GuideActorAi.BaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !flow.GuideActorAi.QuestFormId.Equals(
                flow.QuestFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned New Game flow is incomplete.");
        foreach (var ordinary in flow.OrdinaryQuests.Values)
        {
            if (string.IsNullOrWhiteSpace(ordinary.FormId) ||
                string.IsNullOrWhiteSpace(ordinary.EditorId) ||
                string.IsNullOrWhiteSpace(ordinary.ScriptFormId) ||
                string.IsNullOrWhiteSpace(ordinary.ScriptEditorId) ||
                ordinary.Stages.Count == 0 ||
                !ordinary.Stages.ContainsKey(ordinary.EntryStage) ||
                ordinary.Stages.Values.SelectMany(stage => stage.Commands)
                    .Where(command => command.Kind == "objective")
                    .Any(command => command.Index is null ||
                        !ordinary.Objectives.ContainsKey(command.Index.Value)))
                throw new InvalidOperationException(
                    "Owned ordinary quest handoff is incomplete.");
            ValidateCommandContract(
                ordinary.CommandContract,
                ordinary.Stages.Values.SelectMany(stage => stage.Commands).ToArray());
        }
        foreach (var actor in flow.OrdinaryActors)
        {
            var actorCommands = actor.Topics.Values.SelectMany(topic =>
                topic.Infos.SelectMany(info => info.Commands)).ToArray();
            if (!flow.SceneRoles.TryGetValue(actor.Role, out var role) ||
                !role.ReferenceFormId.Equals(
                    actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase) ||
                !role.BaseFormId.Equals(actor.BaseFormId, StringComparison.OrdinalIgnoreCase) ||
                actor.PackagePriority.Count == 0 ||
                actor.PackagePriority.Any(formId => !actor.Packages.ContainsKey(formId)) ||
                !actor.Topics.ContainsKey(actor.ActivationTopicFormId) ||
                !actor.Voice.SpeakerRole.Equals(actor.Role, StringComparison.OrdinalIgnoreCase) ||
                actor.ArrivalTransitions.Any(value =>
                    !actor.Packages.ContainsKey(value.PackageFormId) ||
                    !value.ActorReferenceFormId.Equals(
                        actor.ReferenceFormId, StringComparison.OrdinalIgnoreCase) ||
                    !flow.OrdinaryQuests.TryGetValue(
                        value.QuestFormId, out var arrivalQuest) ||
                    !arrivalQuest.Stages.ContainsKey(value.FromStage) ||
                    !arrivalQuest.Stages.ContainsKey(value.ToStage) ||
                    string.IsNullOrWhiteSpace(value.ScriptEditorId)) ||
                actor.AutomaticDialogueTriggers.Any(value =>
                    string.IsNullOrWhiteSpace(value.ScriptFormId) ||
                    string.IsNullOrWhiteSpace(value.ScriptEditorId) ||
                    string.IsNullOrWhiteSpace(value.TriggerReferenceFormId) ||
                    value.BoundsGameUnits.X <= 0 || value.BoundsGameUnits.Y <= 0 ||
                    value.BoundsGameUnits.Z <= 0 ||
                    !actor.Topics.ContainsKey(value.TopicFormId) ||
                    !flow.OrdinaryQuests.TryGetValue(value.QuestFormId, out var triggerQuest) ||
                    !triggerQuest.Objectives.ContainsKey(value.ObjectiveIndex)))
                throw new InvalidOperationException(
                    "Owned ordinary actor dialogue handoff is incomplete.");
            ValidateCommandContract(actor.CommandContract, actorCommands);
        }
        if (flow.Stages.Values
            .SelectMany(value => value.Commands)
            .Where(value => value.Kind == "objective" &&
                value.QuestEditorId?.Equals(
                    flow.QuestEditorId,
                    StringComparison.OrdinalIgnoreCase) == true)
            .Any(value => value.Index is null || !flow.Objectives.ContainsKey(value.Index.Value)))
            throw new InvalidOperationException("Owned New Game objective text is incomplete.");
        if (flow.TimerTransitions.Values.Any(value =>
                !flow.Stages.ContainsKey(value.FromStage) ||
                !flow.Stages.ContainsKey(value.ToStage)) ||
            flow.MenuCloseTransitions.Any(value =>
                !flow.Stages.ContainsKey(value.Key) ||
                !flow.Stages.ContainsKey(value.Value)) ||
            flow.Interactions.Any(value =>
                !flow.SceneRoles.ContainsKey(value.TargetRole) ||
                !flow.Stages.ContainsKey(value.FromStage) ||
                !flow.Stages.ContainsKey(value.ToStage)))
            throw new InvalidOperationException("Owned New Game transitions do not join authored stages.");
        var commands = flow.Stages.Values
            .SelectMany(value => value.Commands)
            .Concat(flow.TopicsByFormId.Values.SelectMany(topic =>
                topic.Infos.SelectMany(info => info.Commands)))
            .Concat(flow.PsychologyRootInfo.Commands)
            .ToArray();
        ValidateCommandContract(flow.CommandContract, commands);
        var dialogueInfos = flow.TopicsByFormId.Values
            .SelectMany(topic => topic.Infos)
            .Append(flow.PsychologyRootInfo)
            .ToArray();
        var uniqueDialogueInfos = dialogueInfos
            .GroupBy(info => info.FormId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (string.IsNullOrWhiteSpace(flow.DialogueVoice.VoiceTypeFormId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.VoiceTypeEditorId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.MemberNamespace) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveSchema) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveRecipeId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveRecipeSha256) ||
            flow.DialogueVoice.ArchiveCount == 0 ||
            flow.DialogueVoice.InfoCount != uniqueDialogueInfos.Length ||
            flow.DialogueVoice.ResponseCount !=
                uniqueDialogueInfos.Sum(info => info.Responses.Count) ||
            dialogueInfos.Any(info =>
                info.Responses.Count == 0 ||
                info.Responses.Where((response, index) => response.Index != index + 1).Any() ||
                info.Responses.Any(response =>
                    string.IsNullOrWhiteSpace(response.Text) ||
                    !ValidDialogueAsset(response.Voice) ||
                    !ValidDialogueAsset(response.Lip))))
            throw new InvalidOperationException(
                "Owned dialogue response, voice, or lip graph is incomplete.");
        var guide = flow.GuideActorAi;
        var furniture = guide.FurnitureOccupancy;
        var guideIdleAnimations = guide.Packages.Values
            .SelectMany(package => package.IdleAnimationFormIds.Zip(
                package.IdleAnimationLogicalPaths))
            .ToHashSet();
        if (guide.PackagePriority.Count == 0 ||
            guide.PackagePriority.Count != guide.Packages.Count ||
            guide.PackagePriority.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                guide.PackagePriority.Count ||
            guide.PackagePriority.Any(form => !guide.Packages.ContainsKey(form)) ||
            guide.Packages.Values.Any(package =>
                string.IsNullOrWhiteSpace(package.FormId) ||
                string.IsNullOrWhiteSpace(package.EditorId) ||
                string.IsNullOrWhiteSpace(package.RecordSha256) ||
                string.IsNullOrWhiteSpace(package.PackageTypeName) ||
                package.Conditions.Any(condition =>
                    string.IsNullOrWhiteSpace(condition.FunctionName) ||
                    !float.IsFinite(condition.ComparisonValue)) ||
                package.Location is { TypeName: "nearReference", Reference: null } ||
                package.Location?.Reference is { } destination &&
                    (!destination.PositionGameUnits.IsFinite() ||
                        !destination.RotationGodot.IsNormalized()) ||
                package.IdleAnimationFormIds.Count !=
                    package.IdleAnimationLogicalPaths.Count) ||
            furniture.MarkerId != DocInitialChairMarkerId ||
            !furniture.MarkerDisposition.Equals(
                "compose-owned-furniture-reference-nif-marker-minus-gmst-target-" +
                    "offset-and-heading-delta",
                StringComparison.Ordinal) ||
            !furniture.Furniture.ReferenceFormId.Equals(
                furniture.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !furniture.Furniture.RecordType.Equals("FURN", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.ReferenceRecordSha256) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.BaseFormId) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.EditorId) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.RecordSha256) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.ModelLogicalPath) ||
            furniture.Furniture.ModelBytes <= 0 ||
            string.IsNullOrWhiteSpace(furniture.Furniture.ModelSha256) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.SourceArchive) ||
            string.IsNullOrWhiteSpace(furniture.Furniture.SourceArchiveSha256) ||
            !ValidGuideFurnitureIdentity(furniture.PatientBed) ||
            furniture.Furniture.Marker.ExtraDataName != "FRN" ||
            furniture.Furniture.Marker.Index != 2 ||
            furniture.Furniture.Marker.PositionRef1 != furniture.MarkerId ||
            furniture.Furniture.Marker.PositionRef2 != furniture.MarkerId ||
            furniture.Furniture.Marker.Orientation !=
                DocInitialChairMarkerOrientation ||
            !Mathf.IsEqualApprox(
                furniture.Furniture.Marker.OrientationRadians,
                furniture.Furniture.Marker.Orientation /
                FurnitureMarkerOrientationUnitsPerRadian) ||
            furniture.Furniture.Marker.AnimationType != 1 ||
            !furniture.Furniture.Marker.OffsetNifGameUnits.IsFinite() ||
            !furniture.Furniture.Marker.OffsetGodotGameUnits.IsFinite() ||
            !furniture.Furniture.Marker.OffsetGodotGameUnits.IsEqualApprox(
                new Vector3(
                    furniture.Furniture.Marker.OffsetNifGameUnits.X,
                    furniture.Furniture.Marker.OffsetNifGameUnits.Z,
                    -furniture.Furniture.Marker.OffsetNifGameUnits.Y)) ||
            !furniture.Furniture.Marker.RotationGodot.IsNormalized() ||
            furniture.Furniture.Marker.ActorPlacementOffset.Semantics !=
                ExpectedGuideFurniturePlacementSemantics ||
            !FurniturePlacementGameSettingIsValid(
                furniture.Furniture.Marker.ActorPlacementOffset.X,
                ExpectedGuideFurniturePlacementXEditorId) ||
            !FurniturePlacementGameSettingIsValid(
                furniture.Furniture.Marker.ActorPlacementOffset.Y,
                ExpectedGuideFurniturePlacementYEditorId) ||
            !FurniturePlacementGameSettingIsValid(
                furniture.Furniture.Marker.ActorPlacementOffset.Z,
                ExpectedGuideFurniturePlacementZEditorId) ||
            !furniture.Furniture.Marker.ActorPlacementOffset.OffsetNifGameUnits
                .IsEqualApprox(new Vector3(
                    furniture.Furniture.Marker.ActorPlacementOffset.X.ValueGameUnits,
                    furniture.Furniture.Marker.ActorPlacementOffset.Y.ValueGameUnits,
                    furniture.Furniture.Marker.ActorPlacementOffset.Z.ValueGameUnits)) ||
            !furniture.Furniture.Marker.ActorPlacementOffset.OffsetGodotGameUnits
                .IsEqualApprox(new Vector3(
                    furniture.Furniture.Marker.ActorPlacementOffset.X.ValueGameUnits,
                    furniture.Furniture.Marker.ActorPlacementOffset.Z.ValueGameUnits,
                    -furniture.Furniture.Marker.ActorPlacementOffset.Y.ValueGameUnits)) ||
            string.IsNullOrWhiteSpace(
                furniture.Furniture.Marker.ActorForwardHeadingDelta.FormId) ||
            furniture.Furniture.Marker.ActorForwardHeadingDelta.EditorId !=
                ExpectedGuideFurnitureHeadingDeltaEditorId ||
            string.IsNullOrWhiteSpace(
                furniture.Furniture.Marker.ActorForwardHeadingDelta.RecordSha256) ||
            furniture.Furniture.Marker.ActorForwardHeadingDelta.SourceKind !=
                ExpectedOwnedGameSettingSourceKind ||
            !float.IsFinite(
                furniture.Furniture.Marker.ActorForwardHeadingDelta.ValueRadians) ||
            !furniture.Furniture.Marker.ActorForwardHeadingDelta.RotationGodot
                .IsNormalized() ||
            !new Basis(
                furniture.Furniture.Marker.ActorForwardHeadingDelta.RotationGodot)
                .IsEqualApprox(new Basis(new Quaternion(
                    Vector3.Up,
                    -furniture.Furniture.Marker.ActorForwardHeadingDelta.ValueRadians))) ||
            !flow.Stages.ContainsKey(furniture.ReleaseStage) ||
            !guide.Packages.TryGetValue(
                furniture.InitialPackageFormId,
                out var initialFurniturePackage) ||
            initialFurniturePackage.Location?.FormId.Equals(
                furniture.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) != true ||
            !initialFurniturePackage.IdleAnimationFormIds.Contains(
                furniture.AnimationObjectIdleFormId,
                StringComparer.OrdinalIgnoreCase) ||
            !guide.Packages.TryGetValue(
                furniture.ReleasePackageFormId,
                out var releaseFurniturePackage) ||
            !releaseFurniturePackage.Conditions.Any(condition =>
                condition.FunctionName.Equals(
                    "getStage",
                    StringComparison.OrdinalIgnoreCase) &&
                condition.Parameter1.Equals(
                    flow.QuestFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                condition.OperatorFlags == GuideConditionGreaterOrEqual &&
                condition.ComparisonValue == furniture.ReleaseStage) ||
            !ValidGuideFurnitureAnimation(
                furniture.SeatedLoop,
                "seatedLoop",
                0,
                requireRootMotion: false) ||
            !ValidGuideFurnitureAnimation(
                furniture.Exit,
                "exit",
                2,
                requireRootMotion: true) ||
            guide.AnimationObjects.Select(value => value.FormId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                guide.AnimationObjects.Count ||
            guide.AnimationObjects.Any(value =>
                !value.RecordType.Equals("ANIO", StringComparison.Ordinal) ||
                !value.ComponentRole.Equals(
                    $"animation-object-{value.FormId}",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(value.EditorId) ||
                string.IsNullOrWhiteSpace(value.RecordSha256) ||
                string.IsNullOrWhiteSpace(value.IdleAnimationEditorId) ||
                string.IsNullOrWhiteSpace(value.IdleAnimationSha256) ||
                string.IsNullOrWhiteSpace(value.IdleAnimationSequenceName) ||
                value.IdleAnimationStartSeconds != 0.0f ||
                value.IdleAnimationStopSeconds <= value.IdleAnimationStartSeconds ||
                value.IdleAnimationTransformPrioritiesByNode.Count == 0 ||
                value.IdleAnimationTransformPrioritiesByNode.Any(priority =>
                    priority.Value < 0) ||
                string.IsNullOrWhiteSpace(value.ModelLogicalPath) ||
                value.Bytes <= 0 ||
                string.IsNullOrWhiteSpace(value.Sha256) ||
                string.IsNullOrWhiteSpace(value.SourceArchive) ||
                string.IsNullOrWhiteSpace(value.SourceArchiveSha256) ||
                string.IsNullOrWhiteSpace(value.AttachmentNode) ||
                !guideIdleAnimations.Contains((
                    value.IdleAnimationFormId,
                    value.IdleAnimationLogicalPath))) ||
            !ValidGuideLocomotionClip(guide.Locomotion.Walk) ||
            !ValidGuideLocomotionClip(guide.Locomotion.Run))
            throw new InvalidOperationException("Owned guide-actor AI graph is incomplete.");
        if (flow.PlayerAnimation.Packages.Count == 0 ||
            flow.PlayerAnimation.Animations.Count == 0 ||
            flow.PlayerAnimation.Packages.Values.Any(package =>
                package.IdleTimerSeconds < 0.0f ||
                package.IdleAnimationFormIds.Any(form =>
                    !flow.PlayerAnimation.Animations.ContainsKey(form)) ||
                package.EventAnimationFormIds.Values.Any(form =>
                    form is not null && !flow.PlayerAnimation.Animations.ContainsKey(form))) ||
            flow.PlayerAnimation.Animations.Values.Any(animation =>
                animation.Track.TargetNode != flow.PlayerAnimation.CameraNode ||
                animation.Track.StopSeconds <= animation.Track.StartSeconds ||
                animation.Track.ParentChain.Count == 0 ||
                animation.Track.Samples.Count < 2 ||
                animation.Track.Samples[0].TimeSeconds != animation.Track.StartSeconds ||
                animation.Track.Samples[^1].TimeSeconds != animation.Track.StopSeconds ||
                animation.Track.Samples.Zip(
                    animation.Track.Samples.Skip(1),
                    (first, second) => second.TimeSeconds > first.TimeSeconds)
                    .Any(increasing => !increasing)) ||
            commands.Any(command =>
                command.Kind == "addScriptPackage" &&
                (command.PackageEditorId is null ||
                    !flow.PlayerAnimation.Packages.ContainsKey(command.PackageEditorId))) ||
            commands.Any(command =>
                command.Kind == "imageSpaceModifier" &&
                (command.ModifierEditorId is null ||
                    !flow.ImageSpaceModifiers.ContainsKey(command.ModifierEditorId))))
            throw new InvalidOperationException(
                "Owned player animation or image-space command graph is incomplete.");
        var character = flow.Character;
        if (character.SexChoices.Count == 0 ||
            !ValidPlayerAppearance(character) ||
            character.SpecialValues.Count == 0 ||
            character.SkillValues.Count == 0 ||
            character.TraitValues.Count == 0 ||
            character.SpecialMinimum > character.SpecialInitial ||
            character.SpecialInitial > character.SpecialMaximum ||
            character.SpecialTotalPoints <
                character.SpecialInitial * character.SpecialValues.Count ||
            character.DocReaction.Values.Count != character.SpecialValues.Count ||
            character.TagSkillMaximumSelected <= 0 ||
            character.TagSkillMaximumSelected > character.SkillValues.Count ||
            character.TraitMaximumSelected <= 0 ||
            character.TraitMaximumSelected > character.TraitValues.Count)
            throw new InvalidOperationException("Owned character-creation contract is invalid.");
    }

    private static bool ValidPlayerAppearance(OpeningCharacterCreation character)
    {
        var appearance = character.Appearance;
        var sexValues = appearance.SexEngineValues.ToHashSet(StringComparer.Ordinal);
        if (appearance.Schema != ExpectedPlayerAppearanceSchema ||
            appearance.Status != ExpectedPlayerAppearanceStatus ||
            appearance.SexEngineValues.Count != character.SexChoices.Count ||
            !sexValues.SetEquals(["male", "female"]) ||
            appearance.Races.Count == 0 ||
            appearance.Races.Select(value => value.FormId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                appearance.Races.Count ||
            !appearance.Races.Any(value => value.FormId.Equals(
                appearance.DefaultRaceFormId,
                StringComparison.OrdinalIgnoreCase)) ||
            appearance.FaceGen.SymmetricGeometryCount != FaceGenSymmetricGeometryCount ||
            appearance.FaceGen.AsymmetricGeometryCount != FaceGenAsymmetricGeometryCount ||
            appearance.FaceGen.SymmetricTextureCount != FaceGenSymmetricTextureCount ||
            appearance.FaceGen.SymmetricGeometryValues.Count != FaceGenSymmetricGeometryCount ||
            appearance.FaceGen.AsymmetricGeometryValues.Count != FaceGenAsymmetricGeometryCount ||
            appearance.FaceGen.SymmetricTextureValues.Count != FaceGenSymmetricTextureCount ||
            string.IsNullOrWhiteSpace(appearance.FaceGen.SymmetricGeometrySha256) ||
            string.IsNullOrWhiteSpace(appearance.FaceGen.AsymmetricGeometrySha256) ||
            string.IsNullOrWhiteSpace(appearance.FaceGen.SymmetricTextureSha256) ||
            !ValidFaceGenControlSpace(appearance.FaceGen.ControlSpace) ||
            !ValidPlayerFaceGenPreview(appearance))
            return false;
        return appearance.Races.All(race =>
            ValidIdentity(race.EditorId, race.FormId, "RACE", "RACE") &&
            !string.IsNullOrWhiteSpace(race.Label) &&
            !string.IsNullOrWhiteSpace(race.RecordSha256) &&
            race.Sex.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(sexValues) &&
            race.Sex.Values.All(sex =>
                sex.HairOptions.Count > 0 &&
                sex.EyeOptions.Count > 0 &&
                sex.HairOptions.Any(value => value.FormId.Equals(
                    sex.DefaultHairFormId,
                    StringComparison.OrdinalIgnoreCase)) &&
                sex.EyeOptions.Any(value => value.FormId.Equals(
                    sex.DefaultEyesFormId,
                    StringComparison.OrdinalIgnoreCase)) &&
                sex.HairOptions.All(value => ValidAppearanceOption(value, "HAIR")) &&
                sex.EyeOptions.All(value => ValidAppearanceOption(value, "EYES"))));
    }

    private static bool ValidPlayerFaceGenPreview(OpeningPlayerAppearance appearance)
    {
        var previewSet = appearance.FaceGen.PreviewHead;
        var controls = appearance.FaceGen.ControlSpace.NativeGeometryControls;
        var race = appearance.Races.SingleOrDefault(value => value.FormId.Equals(
            appearance.DefaultRaceFormId,
            StringComparison.OrdinalIgnoreCase));
        if (race is null ||
            previewSet.Schema != ExpectedPlayerFaceGenPreviewSchema ||
            previewSet.Status != ExpectedPlayerFaceGenPreviewStatus ||
            previewSet.RuntimeDisposition !=
                ExpectedPlayerFaceGenPreviewRuntimeDisposition ||
            previewSet.SelectionScope != ExpectedPlayerFaceGenPreviewSelectionScope ||
            previewSet.UnsupportedSelectionScope !=
                ExpectedPlayerFaceGenUnsupportedSelectionScope ||
            !previewSet.PlayerFormId.Equals(
                appearance.PlayerFormId,
                StringComparison.OrdinalIgnoreCase) ||
            previewSet.GeometryControlCount != FaceGenNativeGeometryControlCount ||
            !previewSet.GeometryControlNames.SequenceEqual(
                controls.Select(value => value.SettingEntity),
                StringComparer.Ordinal) ||
            previewSet.TextureControlCount <= 0 ||
            previewSet.TextureControlCount != previewSet.TextureControlNames.Count ||
            !previewSet.FullBody ||
            previewSet.BodyComponentRoles is null ||
            !previewSet.BodyComponentRoles.SequenceEqual(
                ExpectedPlayerFaceGenBodyComponentRoles,
                StringComparer.Ordinal) ||
            !ValidPlayerBodySourcesBySex(previewSet.BodyComponentSourcesBySex) ||
            !OwnedGamebryoFaceGenSelectionInventory.IsComplete(
                previewSet,
                appearance.Races.SelectMany(value => value.Sex.Select(pair =>
                    new OwnedGamebryoFaceGenSelectionDomain(
                        pair.Key,
                        value.FormId,
                        pair.Value.HairOptions.Select(option => option.FormId).ToArray(),
                        pair.Value.EyeOptions.Select(option => option.FormId).ToArray())))))
            return false;

        return previewSet.Previews.All(preview =>
            appearance.Races.SingleOrDefault(value => value.FormId.Equals(
                preview.RaceFormId,
                StringComparison.OrdinalIgnoreCase)) is { } previewRace &&
            previewRace.Sex.TryGetValue(preview.Sex, out var sex) &&
            preview.Schema == previewSet.Schema &&
            preview.Status == previewSet.Status &&
            preview.RuntimeDisposition == previewSet.RuntimeDisposition &&
            preview.PlayerFormId.Equals(
                previewSet.PlayerFormId,
                StringComparison.OrdinalIgnoreCase) &&
            sex.HairOptions.Any(value => value.FormId.Equals(
                preview.HairFormId,
                StringComparison.OrdinalIgnoreCase)) &&
            sex.EyeOptions.Any(value => value.FormId.Equals(
                preview.EyesFormId,
                StringComparison.OrdinalIgnoreCase)) &&
            preview.GeometryControlCount == previewSet.GeometryControlCount &&
            preview.GeometryControlNames.SequenceEqual(
                previewSet.GeometryControlNames,
                StringComparer.Ordinal) &&
            preview.TextureControlCount == previewSet.TextureControlCount &&
            preview.TextureControlNames.SequenceEqual(
                previewSet.TextureControlNames,
                StringComparer.Ordinal) &&
            preview.TextureControls.Select(value => value.SettingEntity).SequenceEqual(
                previewSet.TextureControlNames,
                StringComparer.Ordinal) &&
            preview.TextureControls.All(value =>
                value.Axis.Count == preview.SymmetricTexture.Count) &&
            preview.AgeControl is { } previewAge &&
            previewAge.SettingEntity ==
                appearance.FaceGen.ControlSpace.NativeAgeControl.SettingEntity &&
            previewAge.GeometryAxis.SequenceEqual(
                appearance.FaceGen.ControlSpace.NativeAgeControl.GeometryAxis) &&
            previewAge.TextureAxis.SequenceEqual(
                appearance.FaceGen.ControlSpace.NativeAgeControl.TextureAxis) &&
            preview.FullBody == previewSet.FullBody &&
            preview.BodyComponentRoles is not null &&
            preview.BodyComponentRoles.SequenceEqual(
                previewSet.BodyComponentRoles,
                StringComparer.Ordinal) &&
            ReferenceEquals(
                preview.BodyComponentSourcesBySex,
                previewSet.BodyComponentSourcesBySex) &&
            !string.IsNullOrWhiteSpace(preview.GltfPath) &&
            !string.IsNullOrWhiteSpace(preview.GltfSha256) &&
            !string.IsNullOrWhiteSpace(preview.SidecarPath) &&
            !string.IsNullOrWhiteSpace(preview.SidecarSha256) &&
            !string.IsNullOrWhiteSpace(preview.BufferSha256) &&
            !string.IsNullOrWhiteSpace(preview.EgtPath) &&
            !string.IsNullOrWhiteSpace(preview.EgtSha256));
    }

    private static bool ValidPlayerBodySourcesBySex(
        IReadOnlyDictionary<string, IReadOnlyList<OpeningPlayerBodyComponentSource>>?
            sources)
    {
        if (sources is null ||
            !sources.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "male", "female" }))
            return false;
        foreach (var sex in new[] { "male", "female" })
        {
            var rows = sources[sex];
            if (!rows.Select(value => value.Role).SequenceEqual(
                    ExpectedPlayerFaceGenBodyComponentRoles,
                    StringComparer.Ordinal) ||
                rows.Any(value =>
                    string.IsNullOrWhiteSpace(value.ModelLogicalPath) ||
                    string.IsNullOrWhiteSpace(value.ModelSha256) ||
                    value.SourceSurfaceCount < 1 ||
                    value.RetainedSurfaceCount < 1 ||
                    value.RetainedSurfaceNames.Count != value.RetainedSurfaceCount ||
                    value.OmittedDismemberCapSurfaceCount < 0 ||
                    value.SourceSurfaceCount != value.RetainedSurfaceCount +
                        value.OmittedDismemberCapSurfaceCount ||
                    string.IsNullOrWhiteSpace(value.DiffuseLogicalPath) ||
                    string.IsNullOrWhiteSpace(value.DiffuseSha256) ||
                    string.IsNullOrWhiteSpace(value.NormalLogicalPath) ||
                    string.IsNullOrWhiteSpace(value.NormalSha256) ||
                    string.IsNullOrWhiteSpace(value.ShapeTransformDisposition)))
                return false;
        }
        return true;
    }

    private static bool ValidFaceGenControlSpace(
        OpeningFaceGenControlSpace source)
    {
        if (source.Schema != ExpectedFaceGenControlSpaceSchema ||
            source.Status != ExpectedFaceGenControlSpaceStatus ||
            source.FormatSignature != "FRCTL001" ||
            source.EngineBuild != ExpectedFaceGenEngineBuild ||
            source.RuntimeDisposition != ExpectedFaceGenControlRuntimeDisposition ||
            source.SourceBytes <= 0 ||
            string.IsNullOrWhiteSpace(source.SourceArchive) ||
            string.IsNullOrWhiteSpace(source.SourceArchiveSha256) ||
            string.IsNullOrWhiteSpace(source.SourceLogicalPath) ||
            string.IsNullOrWhiteSpace(source.SourceSha256) ||
            string.IsNullOrWhiteSpace(source.SourceExecutableSha256) ||
            source.SymmetricGeometryBasisCount != FaceGenSymmetricGeometryCount ||
            source.AsymmetricGeometryBasisCount != FaceGenAsymmetricGeometryCount ||
            source.SymmetricTextureBasisCount != FaceGenSymmetricTextureCount ||
            source.AsymmetricTextureBasisCount != FaceGenAsymmetricTextureCount ||
            source.SymmetricGeometryControlCount != FaceGenSymmetricGeometryControlCount ||
            source.AsymmetricGeometryControlCount != FaceGenAsymmetricGeometryControlCount ||
            source.SymmetricTextureControlCount != FaceGenSymmetricTextureControlCount ||
            source.AsymmetricTextureControlCount != FaceGenAsymmetricTextureControlCount ||
            source.SymmetricGeometryControls.Count != FaceGenSymmetricGeometryControlCount ||
            source.NativeGeometryControls.Count != FaceGenNativeGeometryControlCount)
            return false;

        var age = source.NativeAgeControl;
        if (string.IsNullOrWhiteSpace(age.SettingEntity) ||
            string.IsNullOrWhiteSpace(age.SourceLabel) ||
            string.IsNullOrWhiteSpace(age.Semantics) ||
            age.GeometryAxis.Count != FaceGenSymmetricGeometryCount ||
            age.TextureAxis.Count != FaceGenSymmetricTextureCount ||
            age.GeometryAxis.Any(value => !float.IsFinite(value)) ||
            age.TextureAxis.Any(value => !float.IsFinite(value)) ||
            age.RawMinimum >= age.RawMaximum || age.RawStep <= 0.0f ||
            age.MappedMinimumYears >= age.MappedMaximumYears ||
            age.MappedMultiplier <= 0.0f)
            return false;

        var controls = source.SymmetricGeometryControls;
        if (controls.Select(value => value.Index).Distinct().Count() != controls.Count ||
            controls.Any(value =>
                value.Index < 0 ||
                value.Index >= FaceGenSymmetricGeometryControlCount ||
                string.IsNullOrWhiteSpace(value.SourceLabel) ||
                string.IsNullOrWhiteSpace(value.AxisSha256) ||
                value.Axis.Count != FaceGenSymmetricGeometryCount ||
                value.Axis.Any(axis => !float.IsFinite(axis))))
            return false;

        var byIndex = controls.ToDictionary(value => value.Index);
        var nativeValid = source.NativeGeometryControls
            .Select(value => value.ControlIndex).Distinct().Count() ==
                source.NativeGeometryControls.Count &&
            source.NativeGeometryControls.All(value =>
                byIndex.TryGetValue(value.ControlIndex, out var control) &&
                value.SettingEntity == $"sRSMShapeOption{value.ControlIndex + 1:00}" &&
                value.SourceLabel == control.SourceLabel &&
                value.AxisSha256 == control.AxisSha256);
        var preview = source.PreviewControl;
        return nativeValid &&
            preview.Semantics == ExpectedFaceGenPreviewControlSemantics &&
            float.IsFinite(preview.Minimum) &&
            float.IsFinite(preview.Maximum) &&
            float.IsFinite(preview.Step) &&
            float.IsFinite(preview.Jump) &&
            float.IsFinite(preview.MorphWeightScale) &&
            float.IsFinite(preview.ResetValue) &&
            float.IsFinite(preview.AcceptanceValue) &&
            Mathf.IsEqualApprox(preview.Minimum, ExpectedFaceGenSliderUiMinimum) &&
            Mathf.IsEqualApprox(preview.Maximum, ExpectedFaceGenSliderUiMaximum) &&
            Mathf.IsEqualApprox(preview.Step, ExpectedFaceGenSliderOrdinaryIncrement) &&
            Mathf.IsEqualApprox(preview.Jump, ExpectedFaceGenSliderJump) &&
            Mathf.IsEqualApprox(
                preview.MorphWeightScale,
                ExpectedFaceGenSliderMorphWeightScale) &&
            preview.ResetValue >= preview.Minimum &&
            preview.ResetValue <= preview.Maximum &&
            preview.AcceptanceValue >= preview.Minimum &&
            preview.AcceptanceValue <= preview.Maximum &&
            preview.AcceptanceValue != preview.ResetValue &&
            ValidFaceGenSliderSemanticsEvidence(preview.SliderSemanticsEvidence) &&
            ValidFaceGenPreviewPresentation(preview.Presentation) &&
            source.NativeGeometryControls.SingleOrDefault(value =>
                value.ControlIndex == preview.ControlIndex) is { } native &&
            preview.SettingEntity == native.SettingEntity &&
            preview.SourceLabel == native.SourceLabel &&
            preview.AxisSha256 == native.AxisSha256;
    }

    private static bool ValidFaceGenSliderSemanticsEvidence(
        OpeningFaceGenSliderSemanticsEvidence source) =>
        source.Classification == ExpectedFaceGenSliderEvidenceClassification &&
        source.EngineBuild == ExpectedFaceGenSliderEvidenceEngineBuild &&
        source.SourceExecutableSha256 ==
            ExpectedFaceGenSliderEvidenceExecutableSha256Prefix +
            ExpectedFaceGenSliderEvidenceExecutableSha256Suffix &&
        Mathf.IsEqualApprox(source.SourceMinimum, ExpectedFaceGenSliderSourceMinimum) &&
        Mathf.IsEqualApprox(source.SourceMaximum, ExpectedFaceGenSliderSourceMaximum) &&
        Mathf.IsEqualApprox(source.UiScale, ExpectedFaceGenSliderUiScale) &&
        Mathf.IsEqualApprox(source.UiMinimum, ExpectedFaceGenSliderUiMinimum) &&
        Mathf.IsEqualApprox(source.UiMaximum, ExpectedFaceGenSliderUiMaximum) &&
        Mathf.IsEqualApprox(
            source.OrdinaryIncrement,
            ExpectedFaceGenSliderOrdinaryIncrement) &&
        Mathf.IsEqualApprox(source.Jump, ExpectedFaceGenSliderJump) &&
        Mathf.IsEqualApprox(
            source.MorphWeightScale,
            ExpectedFaceGenSliderMorphWeightScale) &&
        source.LowGlobalAddress == ExpectedFaceGenSliderLowGlobalAddress &&
        source.HighGlobalAddress == ExpectedFaceGenSliderHighGlobalAddress &&
        source.IncrementTrait == ExpectedFaceGenSliderIncrementTrait &&
        Mathf.IsEqualApprox(
            source.IncrementDefaultThreshold,
            ExpectedFaceGenSliderOrdinaryIncrement);

    private static bool ValidFaceGenPreviewPresentation(
        OpeningFaceGenPreviewPresentation source)
    {
        var aspectFit =
            float.IsFinite(source.ViewportWidthFraction) &&
            source.ViewportWidthFraction > 0.0f &&
            source.ViewportWidthFraction <= 1.0f &&
            float.IsFinite(source.ViewportHeightFraction) &&
            source.ViewportHeightFraction > 0.0f &&
            source.ViewportHeightFraction <= 1.0f &&
            float.IsFinite(source.VerticalFovHalfAngleFactor) &&
            source.VerticalFovHalfAngleFactor > 0.0f &&
            source.VerticalFovHalfAngleFactor <= 1.0f &&
            float.IsFinite(source.DepthExtentFraction) &&
            source.DepthExtentFraction > 0.0f &&
            source.DepthExtentFraction <= 1.0f;
        var observedRaceSex =
            float.IsFinite(source.FullInVerticalOffsetGameUnits) &&
            float.IsFinite(source.FullInDistanceGameUnits) &&
            source.FullInDistanceGameUnits > 0.0f &&
            float.IsFinite(source.FullInYawRadians) &&
            float.IsFinite(source.FullOutVerticalOffsetGameUnits) &&
            float.IsFinite(source.FullOutDistanceGameUnits) &&
            source.FullOutDistanceGameUnits > source.FullInDistanceGameUnits &&
            float.IsFinite(source.FullOutYawRadians) &&
            float.IsFinite(source.StartingZoomFraction) &&
            source.StartingZoomFraction is >= 0.0f and <= 1.0f;
        return aspectFit || observedRaceSex;
    }

    private static bool ValidAppearanceOption(
        OpeningAppearanceOption option,
        string expectedRecordType) =>
        ValidIdentity(
            option.EditorId,
            option.FormId,
            option.RecordType,
            expectedRecordType) &&
        !string.IsNullOrWhiteSpace(option.Label) &&
        !string.IsNullOrWhiteSpace(option.RecordSha256) &&
        (expectedRecordType != "HAIR" ||
            !string.IsNullOrWhiteSpace(option.ModelLogicalPath)) &&
        !string.IsNullOrWhiteSpace(option.Texture.Path);

    private static void ValidateCommandContract(
        OpeningCommandContract contract,
        IReadOnlyList<OpeningFlowCommand> commands)
    {
        var kindCounts = commands
            .GroupBy(command => command.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var identityCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["itemEditorId"] = commands.Count(command => command.ItemEditorId is not null),
            ["questEditorId"] = commands.Count(command => command.QuestEditorId is not null),
            ["globalEditorId"] = commands.Count(command => command.GlobalEditorId is not null),
            ["ownerEditorId"] = commands.Count(command => command.OwnerEditorId is not null),
            ["referenceEditorId"] = commands.Count(command => command.ReferenceEditorId is not null),
        };
        foreach (var empty in identityCounts.Where(value => value.Value == 0).ToArray())
            identityCounts.Remove(empty.Key);
        if (contract.Schema != ExpectedCommandContractSchema ||
            !contract.AllEmittedKindsRuntimeBlocking ||
            !contract.AllDeclaredRecordReferencesResolved ||
            contract.CommandCount != commands.Count ||
            !DictionaryMatches(contract.KindCounts, kindCounts) ||
            !DictionaryMatches(contract.RecordIdentityCounts, identityCounts) ||
            commands.Any(command => !RuntimeCommandKinds.Contains(command.Kind)) ||
            commands.Any(command =>
                !ValidIdentity(command.ItemEditorId, command.ItemFormId, command.ItemRecordType) ||
                !ValidIdentity(
                    command.QuestEditorId,
                    command.QuestFormId,
                    command.QuestRecordType,
                    "QUST") ||
                !ValidIdentity(
                    command.GlobalEditorId,
                    command.GlobalFormId,
                    command.GlobalRecordType,
                    "GLOB") ||
                !ValidIdentity(
                    command.OwnerEditorId,
                    command.OwnerFormId,
                    command.OwnerRecordType,
                    "QUST") ||
                command.Kind == "playIdle" && !ValidIdentity(
                    command.IdleEditorId,
                    command.IdleFormId,
                    command.IdleRecordType,
                    "IDLE") ||
                !ValidReferenceIdentity(command)))
            throw new InvalidOperationException(
                "Owned opening command execution contract is incomplete.");
    }

    private static bool DictionaryMatches(
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual) =>
        expected.Count == actual.Count &&
        expected.All(value => actual.GetValueOrDefault(value.Key) == value.Value);

    private static bool ValidIdentity(
        string? editorId,
        string? formId,
        string? recordType,
        string? expectedRecordType = null)
    {
        if (editorId is null)
            return formId is null && recordType is null;
        if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(recordType) ||
            expectedRecordType is not null && recordType != expectedRecordType)
            return false;
        try
        {
            return FalloutFormId.Normalize(formId) == formId;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidReferenceIdentity(OpeningFlowCommand command) =>
        ValidIdentity(
            command.ReferenceEditorId,
            command.ReferenceFormId,
            command.ReferenceRecordType) &&
        (command.ReferenceRecordType is null or "REFR" or "ACHR" or "ACRE");

    private static bool ValidGuideLocomotionClip(OpeningGuideLocomotionClip clip) =>
        !string.IsNullOrWhiteSpace(clip.LogicalPath) &&
        !string.IsNullOrWhiteSpace(clip.Sha256) &&
        ValidGuideRootMotion(clip.RootMotion);

    private static bool ValidGuideRootMotion(OpeningGuideRootMotion rootMotion) =>
        !string.IsNullOrWhiteSpace(rootMotion.SequenceName) &&
        !string.IsNullOrWhiteSpace(rootMotion.TargetNode) &&
        float.IsFinite(rootMotion.StartSeconds) &&
        float.IsFinite(rootMotion.StopSeconds) &&
        float.IsFinite(rootMotion.SpeedGameUnitsPerSecond) &&
        rootMotion.StopSeconds > rootMotion.StartSeconds &&
        rootMotion.SpeedGameUnitsPerSecond > 0.0f &&
        rootMotion.DisplacementGodotGameUnits.IsFinite();

    private static bool FurniturePlacementGameSettingIsValid(
        OpeningGuideFurniturePlacementGameSetting setting,
        string expectedEditorId) =>
        !string.IsNullOrWhiteSpace(setting.FormId) &&
        setting.EditorId == expectedEditorId &&
        !string.IsNullOrWhiteSpace(setting.RecordSha256) &&
        setting.SourceKind == ExpectedOwnedGameSettingSourceKind &&
        float.IsFinite(setting.ValueGameUnits);

    private static bool ValidGuideFurnitureIdentity(
        OpeningGuideFurnitureIdentity source) =>
        ValidFormId(source.ReferenceFormId) &&
        ValidFormId(source.BaseFormId) &&
        source.RecordType.Equals("FURN", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(source.EditorId) &&
        ValidSha256(source.ReferenceRecordSha256) &&
        ValidSha256(source.RecordSha256) &&
        !string.IsNullOrWhiteSpace(source.ModelLogicalPath) &&
        source.ModelBytes > 0 &&
        ValidSha256(source.ModelSha256) &&
        !string.IsNullOrWhiteSpace(source.SourceArchive) &&
        ValidSha256(source.SourceArchiveSha256);

    private static bool ValidSha256(string value) =>
        value.Length == SHA256.HashSizeInBits / 4 && value.All(Uri.IsHexDigit);

    private static bool ValidFormId(string formId)
    {
        try
        {
            return FalloutFormId.Normalize(formId) == formId;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidGuideFurnitureAnimation(
        OpeningGuideFurnitureAnimation animation,
        string role,
        int cycleType,
        bool requireRootMotion) =>
        animation.Role.Equals(role, StringComparison.Ordinal) &&
        animation.RecordType.Equals("IDLE", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(animation.FormId) &&
        !string.IsNullOrWhiteSpace(animation.EditorId) &&
        !string.IsNullOrWhiteSpace(animation.RecordSha256) &&
        !string.IsNullOrWhiteSpace(animation.LogicalPath) &&
        animation.Bytes > 0 &&
        !string.IsNullOrWhiteSpace(animation.Sha256) &&
        !string.IsNullOrWhiteSpace(animation.SourceArchive) &&
        !string.IsNullOrWhiteSpace(animation.SourceArchiveSha256) &&
        !string.IsNullOrWhiteSpace(animation.SequenceName) &&
        animation.StartSeconds == 0.0f &&
        animation.StopSeconds > animation.StartSeconds &&
        animation.CycleType == cycleType &&
        animation.ControlledBlocks > 0 &&
        (requireRootMotion
            ? animation.RootMotion is { } rootMotion &&
                ValidGuideRootMotion(rootMotion) &&
                rootMotion.SequenceName.Equals(
                    animation.SequenceName,
                    StringComparison.Ordinal) &&
                rootMotion.StartSeconds == animation.StartSeconds &&
                rootMotion.StopSeconds == animation.StopSeconds &&
                rootMotion.CycleType == animation.CycleType
            : animation.RootMotion is null);

    private static bool ValidDialogueAsset(OpeningDialogueAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.LogicalPath) &&
        !string.IsNullOrWhiteSpace(asset.SourcePath) &&
        !string.IsNullOrWhiteSpace(asset.Sha256) &&
        !string.IsNullOrWhiteSpace(asset.SourceArchive) &&
        !string.IsNullOrWhiteSpace(asset.SourceArchiveSha256);
}
