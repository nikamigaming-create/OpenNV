using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private void ResolveSceneRoles()
    {
        foreach (var role in _flow.SceneRoles.Values)
        {
            Node3D? node = role.RecordType switch
            {
                "ACHR" or "ACRE" => _loaded.Actors
                    .FirstOrDefault(value => value.ReferenceFormId.Equals(
                        role.ReferenceFormId,
                        StringComparison.OrdinalIgnoreCase))
                    .Placement,
                _ when _loaded.MainContent.PlacedReferences.FirstOrDefault(value =>
                    value.FormId.Equals(
                        role.ReferenceFormId,
                        StringComparison.OrdinalIgnoreCase)) is { } reference =>
                    reference.Placement,
                _ when _loaded.MainContent.Doors.TryGetValue(
                    role.ReferenceFormId,
                    out var door) => door,
                _ => null,
            };
            if (node is null)
                throw new InvalidOperationException(
                    $"Owned opening scene role is absent from its CELL: {role.Role}");
            _roleNodes.Add(role.Role, node);
        }
    }

    private void ResolveGuideActor()
    {
        var matches = _loaded.Actors.Where(value =>
                value.ReferenceFormId.Equals(
                    _flow.GuideActorAi.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    _flow.GuideActorAi.BaseFormId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1 ||
            !_roleNodes.TryGetValue(_flow.GuideActorAi.Role, out var roleNode) ||
            roleNode != matches[0].Placement)
            throw new InvalidOperationException(
                "Owned opening guide actor is absent or ambiguous in its CELL.");
        _guideActor = matches[0];
        _guideActorResolved = true;
    }

    private void ResolveGuideAnimationObjects()
    {
        var runtimeSurfaces = _guideActor.Actor.Surfaces.Where(surface =>
                surface.Role.StartsWith(
                    "animation-object-",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var animationObject in _flow.GuideActorAi.AnimationObjects)
        {
            var surfaces = runtimeSurfaces.Where(surface =>
                    surface.Role.Equals(
                        animationObject.ComponentRole,
                        StringComparison.OrdinalIgnoreCase) &&
                    surface.SourceFormId?.Equals(
                        animationObject.FormId,
                        StringComparison.OrdinalIgnoreCase) == true &&
                    surface.AttachmentNode?.Equals(
                        animationObject.AttachmentNode,
                        StringComparison.Ordinal) == true)
                .Select(surface => surface.Mesh)
                .Distinct()
                .ToArray();
            if (surfaces.Length == 0)
                throw new InvalidOperationException(
                    "Owned guide animation object is absent from its actor: " +
                    animationObject.EditorId);
            if (surfaces.Any(surface => surface.Visible))
                throw new InvalidOperationException(
                    "Owned guide animation object is not default-hidden: " +
                    animationObject.EditorId);
            _guideAnimationObjectSurfaces.Add(animationObject.FormId, surfaces);
        }
        if (runtimeSurfaces.Length !=
            _guideAnimationObjectSurfaces.Values.Sum(value => value.Count))
            throw new InvalidOperationException(
                "Owned guide actor contains undeclared animation-object surfaces.");
        var cigaretteSource = _flow.GuideActorAi.AnimationObjects.Single();
        var cigaretteSurface = _guideActor.Actor.Surfaces.Single(surface =>
            surface.Role.Equals(
                cigaretteSource.ComponentRole,
                StringComparison.OrdinalIgnoreCase) &&
            surface.SourceFormId?.Equals(
                cigaretteSource.FormId,
                StringComparison.OrdinalIgnoreCase) == true);
        _guideCigaretteSmokePresentation =
            OpeningCigaretteSmokePresentation.Create(
                _loaded.Root,
                cigaretteSurface,
                cigaretteSource);
    }

    private void EvaluateGuidePackage(bool force = false)
    {
        if (!_guideActorResolved)
            return;
        var package = _flow.GuideActorAi.PackagePriority
            .Select(formId => _flow.GuideActorAi.Packages[formId])
            .FirstOrDefault(value => value.Conditions.All(EvaluateGuideCondition))
            ?? throw new InvalidOperationException(
                "Owned opening guide has no eligible AI package.");
        if (!force && _activeGuidePackage?.FormId.Equals(
                package.FormId,
                StringComparison.OrdinalIgnoreCase) == true)
            return;
        _activeGuidePackage = package;
        _guideLookAtPlayer = false;
        BeginGuidePackage(package);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_PACKAGE form={package.FormId} " +
            $"editor={package.EditorId} type={package.PackageTypeName} " +
            $"alwaysRun={package.AlwaysRun}");
    }

    private bool EvaluateGuideCondition(OpeningGuideCondition condition)
    {
        float actual;
        if (condition.FunctionName.Equals("getStage", StringComparison.OrdinalIgnoreCase))
        {
            actual = condition.Parameter1.Equals(
                _flow.GuideActorAi.QuestFormId,
                StringComparison.OrdinalIgnoreCase)
                ? _stage
                : 0.0f;
        }
        else if (condition.FunctionName.Equals(
            "getQuestCompleted",
            StringComparison.OrdinalIgnoreCase))
        {
            actual = condition.Parameter1.Equals(
                    _flow.GuideActorAi.QuestFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                _openingQuestCompleted
                    ? 1.0f
                    : 0.0f;
        }
        else if (condition.FunctionName.Equals(
            "getQuestVariable",
            StringComparison.OrdinalIgnoreCase))
        {
            actual = _questVariables.GetValueOrDefault(
                $"{condition.Parameter1}.{condition.Parameter2}");
        }
        else
        {
            throw new InvalidOperationException(
                $"Owned guide condition function is unsupported: {condition.FunctionName}");
        }
        return CompareCondition(
            condition.OperatorFlags,
            actual,
            condition.ComparisonValue);
    }

    private void BeginGuidePackage(OpeningGuidePackage package)
    {
        _guideArrivalContinuation = null;
        _guideDestinationReference = package.Location?.Reference;
        if (TryPreserveInitialFurnitureOccupancy(package))
        {
            _guidePackageBegan = true;
            _guideMoving = false;
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _activeGuideLocomotion = null;
            PlayGuideFurnitureSeatedLoop();
            return;
        }
        if (_guideFurnitureOccupied)
        {
            BeginGuideFurnitureExit(package);
            return;
        }
        ContinueGuidePackage(package);
    }

    private void ContinueGuidePackage(OpeningGuidePackage package)
    {
        _guidePackageBegan = true;
        if (_guideDestinationReference is not { } destination)
        {
            _guideMoving = false;
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _activeGuideLocomotion = null;
            PlayGuidePackageIdle(package);
            return;
        }
        _guideDestinationCellUnits = GameplayActorGrounding.ApplyGroundOffset(
            _guideActor,
            _loaded.GameToCellUnits(destination.PositionGameUnits));
        _activeGuideLocomotion = package.AlwaysRun
            ? _flow.GuideActorAi.Locomotion.Run
            : _flow.GuideActorAi.Locomotion.Walk;
        if (_guideActor.Placement.Position == _guideDestinationCellUnits)
        {
            _guidePathCellUnits = Array.Empty<Vector3>();
            _guidePathIndex = 0;
            _guideMoving = false;
            FinishGuideTravel();
            return;
        }
        _guidePathCellUnits = _loaded.MainContent.Navigation.FindPath(
                _loaded.CellToGameUnits(_guideActor.Placement.Position),
                destination.PositionGameUnits)
            .Select(_loaded.GameToCellUnits)
            .Select(position => GameplayActorGrounding.ApplyGroundOffset(
                _guideActor,
                position))
            .ToArray();
        if (_guidePathCellUnits.Count == 0)
            throw new InvalidOperationException(
                "Owned opening guide navigation returned no waypoints.");
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_PATH package={package.EditorId} " +
            $"navmeshes={_loaded.MainContent.Navigation.NavMeshes} " +
            $"vertices={_loaded.MainContent.Navigation.Vertices} " +
            $"triangles={_loaded.MainContent.Navigation.Triangles} " +
            $"waypoints={_guidePathCellUnits.Count}");
        _guidePathIndex = 0;
        _guideDestinationCellUnits = _guidePathCellUnits[_guidePathIndex];
        _guideMoving = true;
        PlayGuideAnimation(
            _activeGuideLocomotion.LogicalPath,
            _activeGuideLocomotion.Sha256,
            restart: true);
    }

    private bool TryPreserveInitialFurnitureOccupancy(OpeningGuidePackage package)
    {
        if (_guideFurnitureOccupied)
            return package.Location?.FormId.Equals(
                    _guideFurnitureReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) == true;
        var contract = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guidePackageBegan || package.Conditions.Count != 0 ||
            !package.FormId.Equals(
                contract.InitialPackageFormId,
                StringComparison.OrdinalIgnoreCase) ||
            package.Location is not
            {
                TypeName: "nearReference",
                Reference: { } destination,
            } ||
            !destination.FormId.Equals(
                contract.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase))
            return false;
        var sourceReferences = _loaded.MainContent.SourceReferences.Where(value =>
                value.FormId.Equals(
                    destination.FormId,
                    StringComparison.OrdinalIgnoreCase) &&
                value.BaseRecordType.Equals("FURN", StringComparison.Ordinal))
            .ToArray();
        if (sourceReferences.Length == 0)
            return false;
        if (sourceReferences.Length != 1)
            throw new InvalidOperationException(
                "Owned initial furniture package destination is ambiguous: " +
                destination.FormId);
        var source = sourceReferences[0];
        if (!source.FormId.Equals(
                contract.Furniture.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !source.BaseFormId.Equals(
                contract.Furniture.BaseFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned initial furniture source differs from the marker contract.");
        var furniture = _loaded.MainContent.PlacedReferences.Where(value =>
                value.FormId.Equals(source.FormId, StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    source.BaseFormId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (furniture.Length != 1)
            throw new InvalidOperationException(
                "Owned initial furniture package destination is absent or ambiguous: " +
                destination.FormId);
        var marker = contract.Furniture.Marker;
        var actorRootOffset = marker.OffsetGodotGameUnits -
            marker.ActorPlacementOffset.OffsetGodotGameUnits;
        var markerTransform = furniture[0].Placement.Transform * new Transform3D(
            new Basis(marker.RotationGodot),
            actorRootOffset);
        var actorTransform = markerTransform * new Transform3D(
            new Basis(marker.ActorForwardHeadingDelta.RotationGodot),
            Vector3.Zero);
        if (!markerTransform.Origin.IsFinite() ||
            !markerTransform.Basis.IsFinite() ||
            !actorTransform.Basis.IsFinite())
            throw new InvalidOperationException(
                "Owned initial furniture marker produced a non-finite transform.");
        var actorScale = _guideActor.Placement.Scale;
        _guideActor.Placement.Position = markerTransform.Origin;
        _guideActor.Placement.Basis = actorTransform.Basis
            .Orthonormalized()
            .Scaled(actorScale);
        _loaded.ActorGrounding.RegisterOwnedFurnitureMarkerOccupancy(
            _guideActor,
            furniture[0].Placement,
            markerTransform.Origin);
        _guideFurnitureOccupied = true;
        _guideFurnitureExitRootMotionApplied = false;
        _guideFurnitureReferenceFormId = source.FormId;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_OCCUPIED " +
            $"package={package.EditorId} reference={source.FormId} " +
            $"base={source.BaseFormId} marker={contract.MarkerId} " +
            $"markerDisposition={contract.MarkerDisposition} " +
            $"markerCell={_guideActor.Placement.Position} " +
            $"markerNifOffset={marker.OffsetNifGameUnits} " +
            $"targetGmstOffset={marker.ActorPlacementOffset.OffsetNifGameUnits} " +
            $"actorRootOffset={actorRootOffset} " +
            $"headingDeltaGmst={marker.ActorForwardHeadingDelta.EditorId} " +
            $"headingDeltaRadians={marker.ActorForwardHeadingDelta.ValueRadians:F7} " +
            $"transform=owned-furniture-nif-marker-minus-gmst-target-offset-and-" +
            $"heading-delta");
        return true;
    }

    private void PlayGuideFurnitureSeatedLoop()
    {
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        var animationObject = _flow.GuideActorAi.AnimationObjects.Single(value =>
            value.IdleAnimationFormId.Equals(
                furniture.AnimationObjectIdleFormId,
                StringComparison.OrdinalIgnoreCase));
        var seated = ResolveGuideAnimation(
            furniture.SeatedLoop.LogicalPath,
            furniture.SeatedLoop.Sha256,
            ZeroedAccumulationRootTranslation);
        var smoking = ResolveGuideAnimation(
            animationObject.IdleAnimationLogicalPath,
            animationObject.IdleAnimationSha256,
            ZeroedAccumulationRootTranslation);
        if (smoking.SequenceName != animationObject.IdleAnimationSequenceName ||
            smoking.StartSeconds != animationObject.IdleAnimationStartSeconds ||
            smoking.StopSeconds != animationObject.IdleAnimationStopSeconds ||
            smoking.CycleType != animationObject.IdleAnimationCycleType ||
            !smoking.TransformPrioritiesByNode.OrderBy(value => value.Key)
                .SequenceEqual(
                    animationObject.IdleAnimationTransformPrioritiesByNode
                        .OrderBy(value => value.Key)))
            throw new InvalidOperationException(
                "Owned guide package idle differs from its opening source contract.");
        _guideFurnitureLayeredSeatedAnimation ??=
            OpeningGuidePriorityAnimation.Compose(
                seated,
                smoking,
                animationObject.AttachmentNode);
        var layered = _guideFurnitureLayeredSeatedAnimation;
        layered.Play();
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(furniture.AnimationObjectIdleFormId);
        _activeGuideAnimation = layered.ActiveAnimation;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_SEATED " +
            $"idle={furniture.SeatedLoop.FormId} " +
            $"sequence={furniture.SeatedLoop.SequenceName} " +
            $"packageIdle={animationObject.IdleAnimationFormId} " +
            $"packageSequence={animationObject.IdleAnimationSequenceName} " +
            $"cigaretteIdle={furniture.AnimationObjectIdleFormId} " +
            "composition=owned-controlled-node-priority");
    }

    private void BeginGuideFurnitureExit(OpeningGuidePackage package)
    {
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guideFurnitureExiting || _guideFurnitureExitRootMotionApplied ||
            _stage != furniture.ReleaseStage ||
            !package.FormId.Equals(
                furniture.ReleasePackageFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned guide furniture exit package/stage is unexpected.");
        _guideFurnitureExiting = true;
        _guideFurnitureLayeredSeatedAnimation?.Stop();
        _guideFurnitureExitPackage = package;
        _guideMoving = false;
        _guidePathCellUnits = Array.Empty<Vector3>();
        _guidePathIndex = 0;
        _activeGuideLocomotion = null;
        PlayGuideAnimation(
            furniture.Exit.LogicalPath,
            furniture.Exit.Sha256,
            restart: true,
            idleAnimationFormId: furniture.AnimationObjectIdleFormId,
            loopMode: Animation.LoopModeEnum.None,
            expectedAccumulationRootDisposition:
                RetainedAccumulationRootTranslation);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_EXIT_BEGIN " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"idle={furniture.Exit.FormId} sequence={furniture.Exit.SequenceName} " +
            $"nextPackage={package.EditorId}");
    }

    private void FinishGuideFurnitureExit()
    {
        var package = _guideFurnitureExitPackage
            ?? throw new InvalidOperationException(
                "Owned guide furniture exit has no pending package.");
        var furniture = _flow.GuideActorAi.FurnitureOccupancy;
        if (_guideFurnitureExitRootMotionApplied ||
            furniture.Exit.RootMotion is not { } rootMotion)
            throw new InvalidOperationException(
                "Owned guide furniture exit root motion is absent or already applied.");
        var rootBefore = _guideActor.Placement.Position;
        var displacementCell = _guideActor.Placement.Basis
            .Orthonormalized() * rootMotion.DisplacementGodotGameUnits;
        var rootAfter = rootBefore + displacementCell;
        if (!displacementCell.IsFinite() || !rootAfter.IsFinite())
            throw new InvalidOperationException(
                "Owned guide furniture exit produced a non-finite root transform.");
        _guideActor.Placement.Position = rootAfter;
        _guideFurnitureExitRootMotionApplied = true;
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_EXIT_ROOT " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"sequence={rootMotion.SequenceName} rootBefore={rootBefore} " +
            $"rootAfter={rootAfter} sourceDisplacement=" +
            $"{rootMotion.DisplacementGodotGameUnits} " +
            $"cellDisplacement={displacementCell}");
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_FURNITURE_RELEASED " +
            $"reference={_guideFurnitureReferenceFormId} " +
            $"exit={furniture.Exit.FormId} nextPackage={package.EditorId}");
        _guideFurnitureOccupied = false;
        _guideFurnitureExiting = false;
        _guideFurnitureReferenceFormId = null;
        _guideFurnitureExitPackage = null;
        _activeGuideAnimation = null;
        ContinueGuidePackage(package);
    }

    private void UpdateGuideActor(double delta)
    {
        if (!_guideActorResolved)
            return;
        if (_guideFurnitureExiting)
        {
            if (_activeGuideAnimation is not { } exit)
                throw new InvalidOperationException(
                    "Owned guide furniture exit has no active animation.");
            if (exit.Player.IsPlaying() && exit.Player.CurrentAnimation.ToString().Equals(
                    exit.RuntimeName,
                    StringComparison.Ordinal))
                return;
            FinishGuideFurnitureExit();
            return;
        }
        if (_guideFurnitureOccupied)
        {
            if (_activeGuideAnimation is not { } seatedAnimation ||
                !seatedAnimation.Player.IsPlaying() ||
                !seatedAnimation.Player.CurrentAnimation.ToString().Equals(
                    seatedAnimation.RuntimeName,
                    StringComparison.Ordinal))
                PlayGuideFurnitureSeatedLoop();
            return;
        }
        if (!_guideMoving)
        {
            if (_guideLookAtPlayer)
                FaceGuideToward(_loaded.Player.GlobalPosition);
            return;
        }
        if (_activeGuideLocomotion is not { } locomotion)
            throw new InvalidOperationException(
                "Owned opening guide is moving without locomotion data.");
        if (_activeGuideAnimation is not { } animation || !animation.Player.IsPlaying())
            PlayGuideAnimation(
                locomotion.LogicalPath,
                locomotion.Sha256,
                restart: true);
        var travelRemaining =
            locomotion.RootMotion.SpeedGameUnitsPerSecond * (float)delta;
        while (_guideMoving)
        {
            var current = _guideActor.Placement.Position;
            var offset = _guideDestinationCellUnits - current;
            var distance = offset.Length();
            if (travelRemaining < distance)
            {
                _guideActor.Placement.Position =
                    current + offset / distance * travelRemaining;
                FaceGuideTowardCellPosition(_guideDestinationCellUnits);
                return;
            }
            _guideActor.Placement.Position = _guideDestinationCellUnits;
            travelRemaining -= distance;
            _guidePathIndex++;
            if (_guidePathIndex >= _guidePathCellUnits.Count)
            {
                FinishGuideTravel();
                return;
            }
            _guideDestinationCellUnits = _guidePathCellUnits[_guidePathIndex];
            FaceGuideTowardCellPosition(_guideDestinationCellUnits);
        }
    }

    private void FinishGuideTravel()
    {
        _guideMoving = false;
        _activeGuideLocomotion = null;
        if (_guideDestinationReference is { } destination)
            _guideActor.Placement.Basis = new Basis(destination.RotationGodot);
        if (_guideLookAtPlayer)
            FaceGuideToward(_loaded.Player.GlobalPosition);
        if (_activeGuidePackage is { } package)
            PlayGuidePackageIdle(package);
        GD.Print(
            $"OPENNV_NEW_GAME_GUIDE_ARRIVED package={_activeGuidePackage?.EditorId} " +
            $"position={_guideActor.Placement.Position}");
        if (_guideArrivalContinuation is not { } continuation)
            return;
        var generation = _guideArrivalGeneration;
        _guideArrivalContinuation = null;
        Callable.From(() =>
        {
            if (generation == _generation)
                continuation();
        }).CallDeferred();
    }

    private void PlayGuidePackageIdle(OpeningGuidePackage package)
    {
        var idleFormId = package.IdleAnimationFormIds.FirstOrDefault();
        var path = package.IdleAnimationLogicalPaths.FirstOrDefault()
            ?? _guideActor.IdleAnimationPath;
        PlayGuideAnimation(
            path,
            expectedSha256: null,
            restart: true,
            idleAnimationFormId: idleFormId);
        _activeGuideIdleAnimation = _activeGuideAnimation;
        _activeGuideAnimation = null;
    }

    private void PlayGuideAnimation(
        string logicalPath,
        string? expectedSha256,
        bool restart,
        string? idleAnimationFormId = null,
        Animation.LoopModeEnum? loopMode = null,
        string expectedAccumulationRootDisposition =
            ZeroedAccumulationRootTranslation)
    {
        var animation = ResolveGuideAnimation(
            logicalPath,
            expectedSha256,
            expectedAccumulationRootDisposition);
        if (loopMode is { } requestedLoopMode)
        {
            var resource = animation.Player.GetAnimation(animation.RuntimeName)
                ?? throw new InvalidOperationException(
                    $"Owned guide animation resource is absent: {logicalPath}");
            resource.LoopMode = requestedLoopMode;
        }
        if (restart || !animation.Player.IsPlaying() ||
            !animation.Player.CurrentAnimation.ToString().Equals(
                animation.RuntimeName,
                StringComparison.Ordinal))
        {
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Advance(0.0);
        }
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(idleAnimationFormId);
        _activeGuideAnimation = animation;
    }

    private ActorModelSlice.LoadedAnimation ResolveGuideAnimation(
        string logicalPath,
        string? expectedSha256,
        string expectedAccumulationRootDisposition)
    {
        var expected = ActorModelSlice.NormalizeAnimationPath(logicalPath);
        var matches = _guideActor.Actor.LoadedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase) &&
                (expectedSha256 is null || animation.SourceSha256.Equals(
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase)) &&
                animation.AccumulationRootTranslationDisposition.Equals(
                    expectedAccumulationRootDisposition,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Owned guide animation is absent or ambiguous: {logicalPath}");
        return matches[0];
    }

    private void SetGuideAnimationObjects(string? idleAnimationFormId)
    {
        if (string.Equals(
                _guideAnimationObjectIdleFormId,
                idleAnimationFormId,
                StringComparison.OrdinalIgnoreCase))
            return;
        _guideAnimationObjectIdleFormId = idleAnimationFormId;
        foreach (var animationObject in _flow.GuideActorAi.AnimationObjects)
        {
            var visible = idleAnimationFormId is not null &&
                animationObject.IdleAnimationFormId.Equals(
                    idleAnimationFormId,
                    StringComparison.OrdinalIgnoreCase);
            foreach (var surface in _guideAnimationObjectSurfaces[animationObject.FormId])
                surface.Visible = visible;
            _guideCigaretteSmokePresentation?.SetActive(visible);
            GD.Print(
                $"OPENNV_NEW_GAME_ANIMATION_OBJECT form={animationObject.FormId} " +
                $"idle={idleAnimationFormId ?? "none"} " +
                $"editor={animationObject.EditorId} visible={visible} " +
                $"attachment={animationObject.AttachmentNode}");
        }
    }

    private void UpdateGuideAnimationObjectLifecycle()
    {
        if (_activeGuideIdleAnimation is not { } idle ||
            idle.Player.IsPlaying() && idle.Player.CurrentAnimation.ToString().Equals(
                idle.RuntimeName,
                StringComparison.Ordinal))
            return;
        _activeGuideIdleAnimation = null;
        SetGuideAnimationObjects(null);
    }

    public override void _ExitTree()
    {
        if (_guideActorResolved)
            SetGuideAnimationObjects(null);
        _guideCigaretteSmokePresentation?.Root.QueueFree();
    }

    private void FaceGuideTowardCellPosition(Vector3 target)
    {
        var current = _guideActor.Placement.Position;
        FaceGuideToward(_loaded.Root.ToGlobal(
            new Vector3(target.X, current.Y, target.Z)));
    }

    private void FaceGuideToward(Vector3 globalTarget)
    {
        var origin = _guideActor.Placement.GlobalPosition;
        var levelTarget = new Vector3(globalTarget.X, origin.Y, globalTarget.Z);
        if (levelTarget.IsEqualApprox(origin))
            return;
        _guideActor.Placement.LookAt(levelTarget, Vector3.Up);
    }

    private void RunWhenGuideReady(Action continuation, int generation)
    {
        _guideLookAtPlayer = true;
        if (_guideMoving || _guideFurnitureExiting)
        {
            _guideArrivalContinuation = continuation;
            _guideArrivalGeneration = generation;
            return;
        }
        if (!_guideFurnitureOccupied)
            FaceGuideToward(_loaded.Player.GlobalPosition);
        continuation();
    }

    private bool IsGuideSpeaker(OpeningFlowCommand command) =>
        command.SpeakerEditorId is { } speaker &&
        _flow.SceneRoles.TryGetValue(_flow.GuideActorAi.Role, out var role) &&
        role.EditorId.Equals(speaker, StringComparison.OrdinalIgnoreCase);

    private bool HandleExternalActivation(Node? collider)
    {
        foreach (var role in _flow.SceneRoles.Values)
        {
            if (!_destroyedReferences.Contains(role.ReferenceFormId) ||
                !_roleNodes.TryGetValue(role.Role, out var destroyed) ||
                !MatchesTarget(collider, destroyed))
                continue;
            GD.Print($"OPENNV_NEW_GAME_ACTIVATE_BLOCKED reference={role.ReferenceFormId}");
            return true;
        }
        var interaction = _flow.Interactions.SingleOrDefault(value =>
            value.FromStage == _stage &&
            value.Event.Equals("activate", StringComparison.OrdinalIgnoreCase));
        if (interaction is null || !_roleNodes.TryGetValue(interaction.TargetRole, out var target))
            return false;
        if (!MatchesTarget(collider, target) &&
            _loaded.Player.GlobalPosition.DistanceTo(target.GlobalPosition) >
            _configuration.Player.ActivationDistanceMeters)
            return false;
        if (interaction.Menu?.Role == "special")
        {
            ShowSpecialMenu(() => SetStage(interaction.ToStage));
            return true;
        }
        SetStage(interaction.ToStage);
        return true;
    }

    private bool AuthorizeScriptedActivatorEvent(ScriptedActivatorEvent source)
    {
        var guard = source.Guard;
        return _objectives.TryGetValue(
            ObjectiveKey(guard.QuestFormId, guard.ObjectiveIndex),
            out var objective) &&
            objective.QuestEditorId.Equals(guard.QuestEditorId, StringComparison.OrdinalIgnoreCase) &&
            objective.State == guard.State && objective.Enabled;
    }

    private void ApplyScriptedActivatorEvent(ScriptedActivatorEvent source)
    {
        foreach (var command in source.Commands)
        {
            if (command.Kind == "setStage" && command.Stage is { } stage)
            {
                if (command.QuestFormId.Equals(_flow.QuestFormId, StringComparison.OrdinalIgnoreCase))
                    SetStage(stage);
                else
                    ApplyQuestStage(command.QuestFormId, command.QuestEditorId, stage, true);
                continue;
            }
            if (command.Kind == "objective" && command.Index is { } index &&
                command.State == "completed" && command.Enabled == true &&
                _objectives.TryGetValue(ObjectiveKey(command.QuestFormId, index), out var objective) &&
                objective.QuestEditorId.Equals(command.QuestEditorId, StringComparison.OrdinalIgnoreCase))
            {
                _objectives[ObjectiveKey(command.QuestFormId, index)] = objective with
                {
                    State = command.State,
                    Enabled = true,
                };
                if (_objective.Text == objective.Text)
                    _objective.Visible = false;
                continue;
            }
            throw new InvalidOperationException("Scripted activator command is not admitted by opening state.");
        }
    }

    private static bool MatchesTarget(Node? collider, Node3D target) =>
        collider is not null &&
        (collider == target || target.IsAncestorOf(collider) || collider.IsAncestorOf(target));
}
