using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private int SetReferenceVisibility(
        string referenceFormId,
        bool enabled,
        bool requireLoaded)
    {
        var nodes = _flow.SceneRoles.Values
            .Where(role => role.ReferenceFormId.Equals(
                referenceFormId,
                StringComparison.OrdinalIgnoreCase))
            .Select(role => _roleNodes.GetValueOrDefault(role.Role))
            .Concat(_loaded.Actors
                .Where(actor => actor.ReferenceFormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(actor => actor.Placement))
            .Concat(_loaded.MainContent.PlacedReferences
                .Where(reference => reference.FormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Placement))
            .Concat(_loaded.LinkedCells
                .SelectMany(cell => cell.Content.PlacedReferences)
                .Where(reference => reference.FormId.Equals(
                    referenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Placement))
            .Where(node => node is not null)
            .Cast<Node3D>()
            .Distinct()
            .ToArray();
        if (requireLoaded && nodes.Length == 0)
            throw new InvalidOperationException(
                $"Owned enabled reference is absent from the loaded world: {referenceFormId}");
        foreach (var node in nodes)
            GamebryoReferenceEnableRuntime.Apply(node, enabled);
        return nodes.Length;
    }

    private void ApplyActorIntent(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is not { } reference ||
            command.ReferenceFormId is not { } referenceForm ||
            command.Operation is null ||
            !_flow.SceneRoles.TryGetValue(_flow.GuideActorAi.Role, out var role) ||
            !role.EditorId.Equals(reference, StringComparison.OrdinalIgnoreCase) ||
            !role.ReferenceFormId.Equals(referenceForm, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned opening actor intent target is unsupported.");
        if (command.Operation.Equals("look", StringComparison.OrdinalIgnoreCase))
        {
            _guideLookAtPlayer = true;
            if (!_guideMoving && !_guideFurnitureOccupied && !_guideFurnitureExiting)
                FaceGuideToward(GuidePlayerLookTarget());
        }
        else if (command.Operation.Equals("stoplook", StringComparison.OrdinalIgnoreCase))
        {
            _guideLookAtPlayer = false;
            if (!_guideMoving && !_guideFurnitureOccupied && !_guideFurnitureExiting &&
                _guideDestinationReference is { } destination)
                _guideActor.Placement.Basis = new Basis(destination.RotationGodot);
        }
        else if (command.Operation.Equals("evp", StringComparison.OrdinalIgnoreCase) ||
            command.Operation.Equals("resetai", StringComparison.OrdinalIgnoreCase))
            EvaluateGuidePackage(force: true);
        else
            throw new InvalidOperationException(
                $"Owned opening actor intent operation is unsupported: {command.Operation}");
        GD.Print(
            $"OPENNV_NEW_GAME_ACTOR_INTENT reference={command.ReferenceEditorId} " +
            $"operation={command.Operation} target={command.TargetEditorId}");
    }

    private void ApplyIdle(OpeningFlowCommand command)
    {
        if (command.ReferenceEditorId is null || command.ReferenceFormId is null ||
            command.IdleEditorId is null ||
            command.IdleFormId is null || command.IdleRecordType != "IDLE" ||
            command.AnimationLogicalPath is null)
            throw new InvalidOperationException("Owned opening idle command is incomplete.");
        var actors = _loaded.Actors.Where(value =>
            _flow.SceneRoles.Values.Any(role =>
                role.EditorId.Equals(command.ReferenceEditorId, StringComparison.OrdinalIgnoreCase) &&
                role.ReferenceFormId.Equals(
                    command.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                role.ReferenceFormId.Equals(
                    value.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (actors.Length != 1)
            throw new InvalidOperationException(
                $"Owned opening idle actor is ambiguous: {command.ReferenceEditorId}");
        var actor = actors[0];
        var expected = ActorModelSlice.NormalizeAnimationPath(command.AnimationLogicalPath);
        var animations = actor.Actor.LoadedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (animations.Length != 1)
            throw new InvalidOperationException(
                $"Owned opening idle animation is absent from the actor: {command.AnimationLogicalPath}");
        var animation = animations[0];
        if (actor.ReferenceFormId.Equals(
            _flow.GuideActorAi.ReferenceFormId,
            StringComparison.OrdinalIgnoreCase))
        {
            PlayGuideAnimation(
                command.AnimationLogicalPath,
                expectedSha256: null,
                restart: true,
                idleAnimationFormId: command.IdleFormId);
            _activeGuideIdleAnimation = _activeGuideAnimation;
            _activeGuideAnimation = null;
        }
        else
        {
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Advance(0.0);
        }
        GD.Print(
            $"OPENNV_NEW_GAME_IDLE source={command.ReferenceEditorId} " +
            $"authored={command.IdleEditorId} runtime={animation.RuntimeName}");
    }

    private void ApplyPlayerControls(OpeningFlowCommand command)
    {
        if (command.Operation is null ||
            !OpeningPlayerControlContract.Matches(
                command.ControlArguments,
                command.ControlValues) ||
            command.ControlValues.Any(value =>
                value is not DisabledControlValue and not EnabledControlValue))
            throw new InvalidOperationException("Owned player-control command is invalid.");
        var enabled = command.Operation.Equals("enable", StringComparison.OrdinalIgnoreCase);
        var disabled = command.Operation.Equals("disable", StringComparison.OrdinalIgnoreCase);
        if (!enabled && !disabled)
            throw new InvalidOperationException(
                $"Owned player-control operation is unsupported: {command.Operation}");
        for (var index = 0; index < command.ControlValues.Count; index++)
        {
            if (command.ControlValues[index] == EnabledControlValue)
                _playerControls[index] = enabled;
        }
        ApplyStageControlPolicy();
        GD.Print(
            $"OPENNV_NEW_GAME_CONTROLS operation={command.Operation} " +
            $"movement={_playerControls[MovementControlIndex]} " +
            $"pipBoy={_playerControls[PipBoyControlIndex]} " +
            $"fighting={_playerControls[FightingControlIndex]} " +
            $"pov={_playerControls[PointOfViewControlIndex]} " +
            $"looking={_playerControls[LookingControlIndex]} " +
            $"sneaking={_playerControls[SneakingControlIndex]} " +
            $"rolloverText={_playerControls[RolloverTextControlIndex]}");
    }

    private void ApplyScriptPackage(OpeningFlowCommand command)
    {
        if (command.Kind == "removeScriptPackage")
        {
            _activePlayerPackage = null;
            _activePlayerAnimation = null;
            _packageIdleWaitSeconds = 0.0;
            GD.Print("OPENNV_NEW_GAME_PLAYER_PACKAGE operation=remove");
            return;
        }
        if (command.PackageEditorId is null ||
            !_flow.PlayerAnimation.Packages.TryGetValue(
                command.PackageEditorId,
                out var package))
            throw new InvalidOperationException(
                $"Owned player package is absent: {command.PackageEditorId}");
        var eventName = _activePlayerPackage?.EditorId.Equals(
            package.EditorId,
            StringComparison.OrdinalIgnoreCase) == true
            ? "change"
            : "begin";
        _activePlayerPackage = package;
        _packageIdleCursor = 0;
        _packageIdleSequenceComplete = false;
        _packageIdleWaitSeconds = 0.0;
        if (package.EventAnimationFormIds.TryGetValue(eventName, out var formId) &&
            formId is not null)
        {
            var idleIndex = package.IdleAnimationFormIds
                .Select((value, index) => (value, index))
                .FirstOrDefault(value => value.value.Equals(
                    formId,
                    StringComparison.OrdinalIgnoreCase));
            if (idleIndex.value is not null)
                _packageIdleCursor = idleIndex.index + 1;
            StartPlayerAnimation(formId, true);
        }
        else
            StartNextPackageIdle();
        GD.Print(
            $"OPENNV_NEW_GAME_PLAYER_PACKAGE operation=add " +
            $"package={package.EditorId} event={eventName}");
    }

    private void StartNextPackageIdle()
    {
        var package = _activePlayerPackage;
        if (package is null || package.IdleAnimationFormIds.Count == 0 ||
            _packageIdleSequenceComplete)
            return;
        if (!package.RunInSequence && package.IdleAnimationFormIds.Count > 1)
            throw new InvalidOperationException(
                "Owned opening package random idle selection requires a retail RNG state.");
        if (_packageIdleCursor >= package.IdleAnimationFormIds.Count)
            _packageIdleCursor = 0;
        var formId = package.IdleAnimationFormIds[_packageIdleCursor++];
        StartPlayerAnimation(formId, false);
    }

    private void StartPlayerAnimation(string formId, bool packageEvent)
    {
        if (!_flow.PlayerAnimation.Animations.TryGetValue(formId, out var animation))
            throw new InvalidOperationException(
                $"Owned player animation is absent: {formId}");
        _activePlayerAnimation = animation;
        _activeAnimationIsPackageEvent = packageEvent;
        _playerAnimationElapsedSeconds = 0.0;
        _playerAnimationSampleIndex = 0;
        ApplyPlayerAnimationSample(animation.Track.StartSeconds);
        GD.Print(
            $"OPENNV_NEW_GAME_PLAYER_ANIMATION form={animation.FormId} " +
            $"authored={animation.EditorId} seconds={animation.Track.StopSeconds:F6}");
    }

    private void UpdatePlayerAnimation(double delta)
    {
        if (_activePlayerAnimation is null)
        {
            if (_activePlayerPackage is null || _packageIdleWaitSeconds <= 0.0)
                return;
            _packageIdleWaitSeconds -= delta;
            if (_packageIdleWaitSeconds <= 0.0)
                StartNextPackageIdle();
            return;
        }
        _playerAnimationElapsedSeconds += delta;
        var track = _activePlayerAnimation.Track;
        var time = MathF.Min(
            track.StopSeconds,
            track.StartSeconds + (float)_playerAnimationElapsedSeconds);
        ApplyPlayerAnimationSample(time);
        if (time < track.StopSeconds)
            return;

        _activePlayerAnimation = null;
        var package = _activePlayerPackage;
        if (package is null)
            return;
        if (_activeAnimationIsPackageEvent)
        {
            _activeAnimationIsPackageEvent = false;
            StartNextPackageIdle();
            return;
        }
        if (package.RunInSequence && _packageIdleCursor < package.IdleAnimationFormIds.Count)
        {
            StartNextPackageIdle();
            return;
        }
        if (package.DoOnce)
        {
            _packageIdleSequenceComplete = true;
            return;
        }
        _packageIdleCursor = 0;
        _packageIdleWaitSeconds = package.IdleTimerSeconds;
        if (_packageIdleWaitSeconds <= 0.0)
            StartNextPackageIdle();
    }

    private void ApplyPlayerAnimationSample(float time)
    {
        var animation = _activePlayerAnimation ??
            throw new InvalidOperationException("Owned player animation is not active.");
        var track = animation.Track;
        while (_playerAnimationSampleIndex + 1 < track.Samples.Count &&
            track.Samples[_playerAnimationSampleIndex + 1].TimeSeconds <= time)
            _playerAnimationSampleIndex++;
        var first = track.Samples[_playerAnimationSampleIndex];
        var second = track.Samples[Math.Min(
            _playerAnimationSampleIndex + 1,
            track.Samples.Count - 1)];
        var amount = second.TimeSeconds <= first.TimeSeconds
            ? 0.0f
            : (time - first.TimeSeconds) / (second.TimeSeconds - first.TimeSeconds);
        var translation = first.TranslationGodotGameUnits.Lerp(
            second.TranslationGodotGameUnits,
            amount);
        var rotation = first.Rotation.Slerp(second.Rotation, amount).Normalized();
        var parentTransform = Transform3D.Identity;
        foreach (var parent in track.ParentChain)
        {
            parentTransform *= new Transform3D(
                new Basis(parent.Rotation).Scaled(parent.Scale),
                parent.TranslationGodotGameUnits * _loaded.UnitsToMeters);
        }
        var result = parentTransform * new Transform3D(
            new Basis(rotation),
            translation * _loaded.UnitsToMeters);
        _loaded.Player.ApplyAuthoredCameraTransform(
            new Transform3D(result.Basis.Orthonormalized(), result.Origin));
        _lastAppliedPlayerCameraAnimation = animation;
        _lastAppliedPlayerCameraTime = time;
    }

    private void ApplyImageSpaceModifier(OpeningFlowCommand command)
    {
        if (command.ModifierEditorId is null || command.Operation is null ||
            !_flow.ImageSpaceModifiers.TryGetValue(
                command.ModifierEditorId,
                out var modifier))
            throw new InvalidOperationException(
                $"Owned image-space modifier is absent: {command.ModifierEditorId}");
        if (command.Operation.Equals("remove", StringComparison.OrdinalIgnoreCase))
            _activeImageSpaceModifiers.Remove(modifier.EditorId);
        else if (command.Operation.Equals("apply", StringComparison.OrdinalIgnoreCase))
            _activeImageSpaceModifiers[modifier.EditorId] =
                new ActiveImageSpaceModifier(modifier);
        else
            throw new InvalidOperationException(
                $"Owned image-space operation is unsupported: {command.Operation}");
        UpdateImageSpaceFade();
        GD.Print(
            $"OPENNV_NEW_GAME_IMAGE_SPACE operation={command.Operation} " +
            $"modifier={modifier.EditorId} crossFade={command.CrossFade == true}");
    }

    private void UpdateImageSpaceModifiers(double delta)
    {
        foreach (var active in _activeImageSpaceModifiers.Values)
            active.ElapsedSeconds += delta;
        foreach (var editorId in _activeImageSpaceModifiers
            .Where(value => value.Value.ElapsedSeconds >= value.Value.Modifier.DurationSeconds)
            .Select(value => value.Key)
            .ToArray())
            _activeImageSpaceModifiers.Remove(editorId);
        UpdateImageSpaceFade();
    }

    private void UpdateImageSpaceFade()
    {
        var colorNumerator = Vector3.Zero;
        var colorWeight = 0.0f;
        var strongestAlpha = TransparentAlpha;
        foreach (var active in _activeImageSpaceModifiers.Values)
        {
            var modifier = active.Modifier;
            var normalizedTime = modifier.DurationSeconds <= 0.0f
                ? 1.0f
                : Mathf.Clamp(
                    (float)(active.ElapsedSeconds / modifier.DurationSeconds),
                    0.0f,
                    1.0f);
            var fade = EvaluateFade(modifier.Fade, normalizedTime);
            var weight = MathF.Max(TransparentAlpha, fade.A);
            colorNumerator += new Vector3(fade.R, fade.G, fade.B) * weight;
            colorWeight += weight;
            strongestAlpha = MathF.Max(strongestAlpha, weight);
        }
        _imageSpaceFade.Color = colorWeight <= TransparentAlpha
            ? Colors.Transparent
            : new Color(
                colorNumerator.X / colorWeight,
                colorNumerator.Y / colorWeight,
                colorNumerator.Z / colorWeight,
                strongestAlpha);
    }

    private static Color EvaluateFade(
        IReadOnlyList<OpeningImageSpaceFadeKey> keys,
        float time)
    {
        if (keys.Count == 0)
            return Colors.Transparent;
        if (time <= keys[0].Time)
            return keys[0].Color;
        if (time >= keys[^1].Time)
            return keys[^1].Color;
        foreach (var pair in keys.Zip(keys.Skip(1)))
        {
            if (time < pair.First.Time || time > pair.Second.Time)
                continue;
            var amount = (time - pair.First.Time) / (pair.Second.Time - pair.First.Time);
            return pair.First.Color.Lerp(pair.Second.Color, amount);
        }
        throw new InvalidOperationException("Owned image-space fade interval is absent.");
    }

    private void ApplyInventoryCommand(OpeningFlowCommand command)
    {
        if (command.ItemEditorId is null || command.ItemFormId is null ||
            command.ItemRecordType is null)
            throw new InvalidOperationException("Owned opening inventory command is incomplete.");
        var count = command.Count ?? 1;
        if (count <= 0)
            throw new InvalidOperationException("Owned opening inventory count is invalid.");
        if (command.Kind == "removeitem")
        {
            var remaining = _inventory.GetValueOrDefault(command.ItemFormId)?.Count - count ?? 0;
            if (remaining > 0)
                _inventory[command.ItemFormId] = new OpeningInventoryState(
                    command.ItemFormId,
                    command.ItemEditorId,
                    command.ItemRecordType,
                    remaining);
            else
            {
                _inventory.Remove(command.ItemFormId);
                _equippedItemFormIds.Remove(command.ItemFormId);
            }
            return;
        }
        if (command.Kind == "additem")
        {
            var current = _inventory.GetValueOrDefault(command.ItemFormId)?.Count ?? 0;
            _inventory[command.ItemFormId] = new OpeningInventoryState(
                command.ItemFormId,
                command.ItemEditorId,
                command.ItemRecordType,
                current + count);
            return;
        }
        if (command.Kind == "equipitem")
        {
            if (!_inventory.ContainsKey(command.ItemFormId))
                throw new InvalidOperationException(
                    $"Owned opening equip item is absent from inventory: {command.ItemFormId}");
            _equippedItemFormIds.Add(command.ItemFormId);
            if (command.ItemRecordType == "WEAP")
            {
                if (command.Weapon is null || command.Weapon.Damage <= 0 ||
                    command.Weapon.ClipSize <= 0)
                    throw new InvalidOperationException(
                        "Owned equipped weapon source contract is incomplete.");
                _equippedWeaponState = new OpeningEquippedWeaponState(
                    command.ItemFormId,
                    command.Weapon.AmmoFormId,
                    command.Weapon.Damage,
                    command.Weapon.ClipSize,
                    command.Weapon.ClipSize)
                {
                    AnimationType = command.Weapon.AnimationType,
                };
            }
            return;
        }
        throw new InvalidOperationException(
            $"Owned opening inventory operation is unsupported: {command.Kind}");
    }
}
