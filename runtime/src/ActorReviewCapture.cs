using Godot;

namespace OpenNV.Runtime;

internal static class ActorReviewCapture
{
    private const int FrameNumberDigits = 6;
    private const float FrustumCenterDivisor = 2.0f;
    private const int SkinLinearM11Index = 0;
    private const int SkinLinearM12Index = 1;
    private const int SkinLinearM13Index = 2;
    private const int SkinTranslationXIndex = 3;
    private const int SkinLinearM21Index = 4;
    private const int SkinLinearM22Index = 5;
    private const int SkinLinearM23Index = 6;
    private const int SkinTranslationYIndex = 7;
    private const int SkinLinearM31Index = 8;
    private const int SkinLinearM32Index = 9;
    private const int SkinLinearM33Index = 10;
    private const int SkinTranslationZIndex = 11;

    internal static async Task Run(
        Node3D host,
        ActorModelSlice.LoadedActor actor,
        RuntimeConfiguration configuration,
        string contractPath,
        string captureRoot,
        string? reportPath)
    {
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite actor review output: {output}");
            if (reportPath is not null && (File.Exists(reportPath) || Directory.Exists(reportPath)))
                throw new InvalidOperationException($"Refusing to overwrite actor review report: {reportPath}");

            var contract = ActorReviewContract.Load(contractPath, actor.FormId, configuration);
            var samples = contract.Shots.SelectMany(shot => shot.Samples).ToArray();
            var sourceSize = new Vector2I(
                samples[0].SourceFrame.Width,
                samples[0].SourceFrame.Height);
            host.GetWindow().Size = sourceSize;
            Directory.CreateDirectory(output);

            actor.AnimationPlayer.Stop();
            actor.AnimationPlayer.Active = false;
            var skeleton = Descendants<Skeleton3D>(actor.Root).Single();
            var mappings = BuildNodeMappings(
                skeleton,
                samples[0],
                configuration.World.GameUnitsToMeters);
            var skinMappings = BuildSkinMappings(actor, skeleton, samples[0]);
            BuildReviewView(host, contract, configuration);
            var camera = new Camera3D
            {
                Current = true,
                KeepAspect = Camera3D.KeepAspectEnum.Height,
            };
            host.AddChild(camera);
            await EnvironmentCapture.WaitForRenderedFrames(
                host,
                configuration.Capture.RenderedFramesBeforeCapture);

            var files = new List<object>();
            var appliedSamples = new List<object>();
            var allVisualGatesPassed = true;
            var allSkinPaletteGatesPassed = true;
            foreach (var shot in contract.Shots)
            {
                foreach (var sample in shot.Samples)
                {
                    var paletteApplication = ApplyPose(
                        actor,
                        skeleton,
                        mappings,
                        skinMappings,
                        sample,
                        configuration);
                    ApplyCamera(camera, sample, configuration.World.GameUnitsToMeters);
                    await EnvironmentCapture.WaitForRenderedFrames(
                        host,
                        configuration.Capture.ActorRenderedFramesBeforeCapture);
                    var pose = MeasurePose(
                        skeleton,
                        camera,
                        mappings,
                        sample,
                        configuration.World.GameUnitsToMeters,
                        configuration.ActorParity.MaximumReportedWorstBones);
                    var posePassed = pose.MaximumLocalTranslationErrorMeters <=
                            configuration.ActorParity.PoseTranslationToleranceMeters &&
                        pose.MaximumLocalRotationErrorRadians <=
                            configuration.ActorParity.PoseRotationToleranceRadians &&
                        pose.MaximumWorldTranslationErrorMeters <=
                            configuration.ActorParity.PoseTranslationToleranceMeters &&
                        pose.MaximumWorldRotationErrorRadians <=
                            configuration.ActorParity.PoseRotationToleranceRadians &&
                        pose.MaximumProjectedErrorPixels <=
                            configuration.ActorReview.ProjectedBoneTolerancePixels;
                    var skinPalette = MeasureSkinPalette(
                        skeleton,
                        skinMappings,
                        sample,
                        configuration.World.GameUnitsToMeters,
                        configuration.ActorParity.MaximumReportedWorstBones);
                    var skinPalettePassed =
                        paletteApplication.MaximumCandidateLinearError <=
                            configuration.ActorReview.SkinPaletteLinearTolerance &&
                        paletteApplication.MaximumCandidateTranslationErrorGameUnits <=
                            configuration.ActorReview.SkinPaletteTranslationToleranceGameUnits &&
                        skinPalette.MaximumLinearError <=
                            configuration.ActorReview.SkinPaletteLinearTolerance &&
                        skinPalette.MaximumTranslationErrorGameUnits <=
                            configuration.ActorReview.SkinPaletteTranslationToleranceGameUnits;
                    allSkinPaletteGatesPassed &= skinPalettePassed;
                    var fileName = $"godot-{SafeToken(shot.Kind)}-frame-" +
                        sample.Frame.ToString($"D{FrameNumberDigits}") + ".png";
                    var capture = EnvironmentCapture.SaveViewportPng(
                        host,
                        output,
                        fileName,
                        configuration.Capture.ActorMinimumMeanLuminance,
                        configuration.Capture,
                        sourceSize);
                    files.Add(capture.Evidence);
                    allVisualGatesPassed &= capture.Passed;
                    appliedSamples.Add(new
                    {
                        shotKind = shot.Kind,
                        sample.Frame,
                        retailSourceFrame = sample.SourceFrame,
                        godotFrame = Path.Combine(output, fileName),
                        sourceSize = new[] { sourceSize.X, sourceSize.Y },
                        cameraPositionMeters = Vector(camera.GlobalPosition),
                        cameraBasis = BasisValues(camera.GlobalBasis),
                        verticalFovDegrees = Mathf.RadToDeg(sample.Camera.FovYRadians),
                        retailFrustum = new
                        {
                            leftSlope = sample.Camera.Frustum.Left,
                            rightSlope = sample.Camera.Frustum.Right,
                            topSlope = sample.Camera.Frustum.Top,
                            bottomSlope = sample.Camera.Frustum.Bottom,
                            near = sample.Camera.Frustum.Near * configuration.World.GameUnitsToMeters,
                            far = sample.Camera.Frustum.Far * configuration.World.GameUnitsToMeters,
                        },
                        sample.Camera.EventSha256,
                        projectionExact = sample.ProjectionExact,
                        projectionStatus = sample.ProjectionStatus,
                        finalSceneColorSurface = new
                        {
                            sample.Camera.Surface.EventSha256,
                            sample.Camera.Surface.RenderFrame,
                            sample.Camera.Surface.MatchedTexturePath,
                            sample.Camera.Surface.MatchedTextureHash,
                            sample.Camera.Surface.VertexShaderFnv1a32,
                            sample.Camera.Surface.IsDirectBackBuffer,
                            sceneDimensions = new[]
                            {
                                sample.Camera.Surface.SceneDimensions.X,
                                sample.Camera.Surface.SceneDimensions.Y,
                            },
                            sample.Camera.Surface.SceneColorFormat,
                            sample.Camera.Surface.BackBufferFormat,
                        },
                        cullingObservation = new
                        {
                            sample.Camera.Culling.EventSha256,
                            verticalFovDegrees =
                                Mathf.RadToDeg(sample.Camera.Culling.FovYRadians),
                            frustum = sample.Camera.Culling.Frustum,
                        },
                        actorRootScale = Vector(sample.ActorRoot.Basis.Scale),
                        mappedSkeletonNodes = mappings.Count,
                        runtimeNamedNodes = sample.Nodes.Count,
                        sample.AnimationLayers,
                        pose.MaximumLocalTranslationErrorMeters,
                        pose.MaximumLocalRotationErrorRadians,
                        pose.MaximumWorldTranslationErrorMeters,
                        pose.MaximumWorldRotationErrorRadians,
                        pose.MaximumProjectedErrorPixels,
                        pose.WorstBones,
                        posePassed,
                        skinPalette = new
                        {
                            sample.SkinPalette.Summary,
                            mappedSkins = skinMappings.Count,
                            mappedBones = skinMappings.Sum(mapping => mapping.Bones.Count),
                            paletteApplication.MaximumCandidateLinearError,
                            paletteApplication.MaximumCandidateTranslationErrorGameUnits,
                            skinPalette.MaximumLinearError,
                            skinPalette.MaximumTranslationErrorGameUnits,
                            skinPalette.WorstBones,
                            passed = skinPalettePassed,
                        },
                        sample.VisualSnapshotEventSha256,
                    });
                }
            }

            var captureSucceeded = allVisualGatesPassed && allSkinPaletteGatesPassed;
            var captureReport = new
            {
                schema = "opennv-godot-actor-review-capture/v1",
                status = captureSucceeded
                    ? "captured-provisional-light-direction"
                    : "capture-gate-failed",
                parityPassed = false,
                renderer = "forward_plus",
                configuration = new
                {
                    schema = RuntimeConfiguration.ExpectedSchema,
                    sha256 = configuration.Sha256,
                },
                actor = new
                {
                    actor.FormId,
                    actor.Name,
                    contract.RecordType,
                    actor.Meshes,
                    actor.Skeletons,
                    actor.Animations,
                    actor.AuthoredSurfaces,
                    actor.AuthoredTextures,
                },
                retailContract = new
                {
                    contract.Path,
                    contract.Sha256,
                    contract.ExactProjectionResolved,
                    shots = contract.Shots.Count,
                    samples = samples.Length,
                    appearance = contract.Appearance,
                    environment = contract.Environment,
                },
                pose = new
                {
                    exactRetailRenderCacheResolvedIntoImportedParentGraph = true,
                    retailNamedNodeWorldAndScreenCoordinatesRetainedAsDiagnostics = true,
                    namedNodeSnapshotIsNotTheRenderedSurfaceAuthority = true,
                    allSkinPaletteGatesPassed,
                    mappings = mappings.Select(mapping => new
                    {
                        mapping.BoneIndex,
                        mapping.BoneName,
                        mapping.ParentBoneName,
                        mapping.NodePath,
                        mapping.CandidateCount,
                    }),
                    skins = skinMappings.Select(mapping => new
                    {
                        mapping.MeshName,
                        mapping.GeometryName,
                        bones = mapping.Bones.Count,
                    }),
                },
                presentation = new
                {
                    retailEnvironmentColorsApplied = true,
                    retailDirectionalVectorResolved = false,
                    projectionResolved = contract.ExactProjectionResolved,
                    exactRetailCameraAppliedPerSourceFrame = true,
                    exactRetailFinalSceneColorProjectionAppliedPerSourceFrame = true,
                    retailNiCameraCullingProjectionRetainedSeparately = true,
                    provisionalDirectionalRotationDegrees =
                        configuration.ActorReview.DirectionalRotationDegrees,
                },
                evidencePolicy = new
                {
                    captureIsNotParityPass = true,
                    exactRetailProjectionRequired = true,
                    exactRetailSkinPaletteRequired = true,
                    unresolvedLightDirectionCannotPass = true,
                    matchedComparisonStatus = "pending",
                },
                allVisualGatesPassed,
                samples = appliedSamples,
                files,
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var localReport = Path.Combine(output, "actor-review-capture-report.json");
            RuntimeCoordinator.WriteReport(localReport, captureReport);
            if (reportPath is not null)
                RuntimeCoordinator.WriteReport(reportPath, captureReport);
            if (captureSucceeded)
                GD.Print(
                    $"OPENNV_GODOT_ACTOR_REVIEW_CAPTURED review={contract.ReviewKey} " +
                    $"frames={samples.Length} parity=0 output={output}");
            else
                GD.PushError(
                    $"OPENNV_GODOT_ACTOR_REVIEW_CAPTURE_GATE_FAIL review={contract.ReviewKey} " +
                    $"output={output}");
            host.GetTree().Quit(captureSucceeded ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_ACTOR_REVIEW_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void BuildReviewView(
        Node3D host,
        ActorReviewContract.Contract contract,
        RuntimeConfiguration configuration)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = contract.Environment.FogColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = contract.Environment.AmbientColor,
            AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(configuration.Renderer.ToneMapper),
        };
        host.AddChild(new WorldEnvironment { Environment = environment });
        host.AddChild(new DirectionalLight3D
        {
            RotationDegrees = configuration.ActorReview.DirectionalRotationDegrees.Vector3(),
            LightColor = contract.Environment.DirectionalColor,
            LightEnergy = configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = configuration.ActorReview.DirectionalShadows,
        });
    }

    private static IReadOnlyList<NodeMapping> BuildNodeMappings(
        Skeleton3D skeleton,
        ActorReviewContract.Sample sample,
        float unitsToMeters)
    {
        var mappings = new List<NodeMapping>();
        for (var boneIndex = 0; boneIndex < skeleton.GetBoneCount(); boneIndex++)
        {
            var boneName = skeleton.GetBoneName(boneIndex).ToString();
            var parentIndex = skeleton.GetBoneParent(boneIndex);
            var parentName = parentIndex < 0
                ? ""
                : skeleton.GetBoneName(parentIndex).ToString();
            var namedCandidates = sample.Nodes
                .Where(node => node.Name.Equals(boneName, StringComparison.Ordinal))
                .ToArray();
            if (namedCandidates.Length == 0)
                throw new InvalidOperationException(
                    $"Retail actor frame {sample.Frame} is missing authored skeleton node: {boneName}");
            var parentCandidates = namedCandidates
                .Where(node => node.ParentName.Equals(parentName, StringComparison.Ordinal))
                .ToArray();
            var candidates = parentCandidates.Length > 0 ? parentCandidates : namedCandidates;
            var rest = skeleton.GetBoneRest(boneIndex);
            var selected = candidates
                .OrderBy(node => node.Local.Translation.DistanceTo(rest.Origin) * unitsToMeters)
                .ThenBy(node => node.Local.Basis.Orthonormalized().GetRotationQuaternion()
                    .AngleTo(rest.Basis.Orthonormalized().GetRotationQuaternion()))
                .ThenBy(node => node.Local.Basis.Scale.DistanceTo(rest.Basis.Scale))
                .ThenBy(node => node.NodePath, StringComparer.Ordinal)
                .First();
            mappings.Add(new NodeMapping(
                boneIndex,
                boneName,
                parentName,
                selected.NodePath,
                candidates.Length));
        }
        return mappings;
    }

    private static IReadOnlyList<SkinMapping> BuildSkinMappings(
        ActorModelSlice.LoadedActor actor,
        Skeleton3D skeleton,
        ActorReviewContract.Sample sample)
    {
        var captured = sample.SkinPalette.Instances
            .Where(instance => instance.Bones.Count > 0)
            .ToArray();
        var surfaces = actor.Surfaces
            .Where(surface => surface.Skinned)
            .ToArray();
        var used = new HashSet<MeshInstance3D>();
        var mappings = new List<SkinMapping>();
        foreach (var instance in captured)
        {
            var candidates = new List<SkinMapping>();
            foreach (var surface in surfaces.Where(surface => !used.Contains(surface.Mesh)))
            {
                if (!surface.Shape.Equals(instance.GeometryName, StringComparison.Ordinal))
                    continue;
                var mesh = surface.Mesh;
                var skin = mesh.Skin!;
                if (skin.GetBindCount() != instance.Bones.Count)
                    continue;
                var bindings = new List<SkinBinding>();
                var matches = true;
                for (var bindIndex = 0; bindIndex < skin.GetBindCount(); bindIndex++)
                {
                    var expected = instance.Bones[bindIndex];
                    var bindName = skin.GetBindName(bindIndex).ToString();
                    var boneIndex = string.IsNullOrEmpty(bindName)
                        ? skin.GetBindBone(bindIndex)
                        : skeleton.FindBone(bindName);
                    if (boneIndex < 0 ||
                        !skeleton.GetBoneName(boneIndex).ToString()
                            .Equals(expected.Name, StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                    bindings.Add(new SkinBinding(
                        bindIndex,
                        boneIndex,
                        expected.Name,
                        skin.GetBindPose(bindIndex)));
                }
                if (matches)
                    candidates.Add(new SkinMapping(
                        instance.NodePath,
                        instance.GeometryName,
                        surface.RuntimeNodeName,
                        mesh,
                        skin,
                        bindings));
            }
            if (candidates.Count != 1)
                throw new InvalidOperationException(
                    $"Retail skin {instance.NodePath} {instance.GeometryName} maps to " +
                    $"{candidates.Count} imported meshes.");
            used.Add(candidates[0].Mesh);
            mappings.Add(candidates[0]);
        }
        if (mappings.Count != captured.Length || used.Count != surfaces.Length)
            throw new InvalidOperationException(
                $"Imported skinned meshes differ from active retail palettes: " +
                $"retail={captured.Length} imported={surfaces.Length}.");
        return mappings;
    }

    private static PaletteApplication ApplyPose(
        ActorModelSlice.LoadedActor actor,
        Skeleton3D skeleton,
        IReadOnlyList<NodeMapping> mappings,
        IReadOnlyList<SkinMapping> skinMappings,
        ActorReviewContract.Sample sample,
        RuntimeConfiguration configuration)
    {
        actor.Root.Transform = new Transform3D(
            sample.ActorRoot.Basis.Scaled(
                Vector3.One * configuration.World.GameUnitsToMeters),
            Vector3.Zero);
        skeleton.ResetBonePoses();
        var nodes = sample.Nodes.ToDictionary(node => node.NodePath, StringComparer.Ordinal);
        var desiredSkeletonGlobals = new Dictionary<int, Transform3D>();
        foreach (var mapping in mappings)
        {
            var node = nodes[mapping.NodePath];
            desiredSkeletonGlobals.Add(
                mapping.BoneIndex,
                skeleton.GlobalTransform.AffineInverse() *
                    ExpectedWorldTransform(
                        node,
                        sample,
                        configuration.World.GameUnitsToMeters));
        }

        var paletteCandidates = new Dictionary<int, List<Transform3D>>();
        foreach (var skinMapping in skinMappings)
        {
            var palette = sample.SkinPalette.Instances.Single(instance =>
                instance.NodePath.Equals(skinMapping.NodePath, StringComparison.Ordinal) &&
                instance.GeometryName.Equals(skinMapping.GeometryName, StringComparison.Ordinal));
            foreach (var binding in skinMapping.Bones)
            {
                var retailBone = palette.Bones[binding.BindIndex];
                var expectedPaletteWorld = ExpectedPaletteWorldTransform(
                    retailBone,
                    sample,
                    configuration.World.GameUnitsToMeters);
                var targetBoneGlobal = skeleton.GlobalTransform.AffineInverse() *
                    expectedPaletteWorld * binding.InverseBindPose.AffineInverse();
                if (!paletteCandidates.TryGetValue(binding.BoneIndex, out var candidates))
                {
                    candidates = new List<Transform3D>();
                    paletteCandidates.Add(binding.BoneIndex, candidates);
                }
                candidates.Add(targetBoneGlobal);
            }
        }

        var maximumCandidateLinearError = 0.0f;
        var maximumCandidateTranslationError = 0.0f;
        foreach (var (boneIndex, candidates) in paletteCandidates)
        {
            var selected = candidates[0];
            foreach (var candidate in candidates.Skip(1))
            {
                maximumCandidateLinearError = MathF.Max(
                    maximumCandidateLinearError,
                    BasisComponentError(selected.Basis, candidate.Basis));
                maximumCandidateTranslationError = MathF.Max(
                    maximumCandidateTranslationError,
                    selected.Origin.DistanceTo(candidate.Origin));
            }
            desiredSkeletonGlobals[boneIndex] = selected;
        }

        foreach (var mapping in mappings)
        {
            var desiredSkeletonGlobal = desiredSkeletonGlobals[mapping.BoneIndex];
            var parentIndex = skeleton.GetBoneParent(mapping.BoneIndex);
            var desiredLocal = parentIndex < 0
                ? desiredSkeletonGlobal
                : desiredSkeletonGlobals[parentIndex].AffineInverse() * desiredSkeletonGlobal;
            skeleton.SetBonePose(mapping.BoneIndex, desiredLocal);
        }
        return new PaletteApplication(
            maximumCandidateLinearError,
            maximumCandidateTranslationError);
    }

    private static SkinPaletteMeasurement MeasureSkinPalette(
        Skeleton3D skeleton,
        IReadOnlyList<SkinMapping> skinMappings,
        ActorReviewContract.Sample sample,
        float unitsToMeters,
        int maximumReportedWorstBones)
    {
        var measurements = new List<SkinPaletteBoneMeasurement>();
        foreach (var skinMapping in skinMappings)
        {
            var palette = sample.SkinPalette.Instances.Single(instance =>
                instance.NodePath.Equals(skinMapping.NodePath, StringComparison.Ordinal) &&
                instance.GeometryName.Equals(skinMapping.GeometryName, StringComparison.Ordinal));
            foreach (var binding in skinMapping.Bones)
            {
                var expected = ExpectedPaletteWorldTransform(
                    palette.Bones[binding.BindIndex], sample, unitsToMeters);
                var actual = skeleton.GlobalTransform *
                    skeleton.GetBoneGlobalPose(binding.BoneIndex) * binding.InverseBindPose;
                measurements.Add(new SkinPaletteBoneMeasurement(
                    skinMapping.GeometryName,
                    binding.BoneName,
                    binding.BindIndex,
                    BasisComponentError(expected.Basis, actual.Basis) / unitsToMeters,
                    expected.Origin.DistanceTo(actual.Origin) / unitsToMeters));
            }
        }
        return new SkinPaletteMeasurement(
            measurements.Max(row => row.LinearError),
            measurements.Max(row => row.TranslationErrorGameUnits),
            measurements
                .OrderByDescending(row => row.TranslationErrorGameUnits)
                .ThenByDescending(row => row.LinearError)
                .ThenBy(row => row.GeometryName, StringComparer.Ordinal)
                .ThenBy(row => row.BindIndex)
                .Take(maximumReportedWorstBones)
                .ToArray());
    }

    private static void ApplyCamera(
        Camera3D camera,
        ActorReviewContract.Sample sample,
        float unitsToMeters)
    {
        var source = sample.Camera;
        camera.GlobalTransform = new Transform3D(
            source.GodotBasis,
            GamebryoCoordinate.ConvertVector(source.OffsetGameUnits) * unitsToMeters);
        var near = source.Frustum.Near * unitsToMeters;
        var far = source.Frustum.Far * unitsToMeters;
        var size = (source.Frustum.Top - source.Frustum.Bottom) * near;
        var offset = new Vector2(
            (source.Frustum.Left + source.Frustum.Right) * near / FrustumCenterDivisor,
            (source.Frustum.Top + source.Frustum.Bottom) * near / FrustumCenterDivisor);
        camera.SetFrustum(size, offset, near, far);
    }

    private static PoseMeasurement MeasurePose(
        Skeleton3D skeleton,
        Camera3D camera,
        IReadOnlyList<NodeMapping> mappings,
        ActorReviewContract.Sample sample,
        float unitsToMeters,
        int maximumReportedWorstBones)
    {
        var nodes = sample.Nodes.ToDictionary(node => node.NodePath, StringComparer.Ordinal);
        var measurements = new List<BoneMeasurement>();
        var desiredSkeletonGlobals = new Dictionary<int, Transform3D>();
        foreach (var mapping in mappings)
        {
            var node = nodes[mapping.NodePath];
            var expectedWorld = ExpectedWorldTransform(node, sample, unitsToMeters);
            var desiredSkeletonGlobal = skeleton.GlobalTransform.AffineInverse() * expectedWorld;
            var parentIndex = skeleton.GetBoneParent(mapping.BoneIndex);
            var expectedLocal = parentIndex < 0
                ? desiredSkeletonGlobal
                : desiredSkeletonGlobals[parentIndex].AffineInverse() * desiredSkeletonGlobal;
            desiredSkeletonGlobals.Add(mapping.BoneIndex, desiredSkeletonGlobal);
            var actualLocal = skeleton.GetBonePose(mapping.BoneIndex);
            var actualWorld = skeleton.GlobalTransform *
                skeleton.GetBoneGlobalPose(mapping.BoneIndex);
            var projected = camera.UnprojectPosition(actualWorld.Origin);
            measurements.Add(new BoneMeasurement(
                mapping.BoneName,
                mapping.NodePath,
                actualLocal.Origin.DistanceTo(expectedLocal.Origin) * unitsToMeters,
                actualLocal.Basis.Orthonormalized().GetRotationQuaternion().AngleTo(
                    expectedLocal.Basis.Orthonormalized().GetRotationQuaternion()),
                actualWorld.Origin.DistanceTo(expectedWorld.Origin),
                actualWorld.Basis.Orthonormalized().GetRotationQuaternion().AngleTo(
                    expectedWorld.Basis.Orthonormalized().GetRotationQuaternion()),
                projected.DistanceTo(node.Screen.Pixels),
                Vector(actualWorld.Origin),
                new[] { projected.X, projected.Y },
                new[] { node.Screen.Pixels.X, node.Screen.Pixels.Y }));
        }
        return new PoseMeasurement(
            measurements.Max(row => row.LocalTranslationErrorMeters),
            measurements.Max(row => row.LocalRotationErrorRadians),
            measurements.Max(row => row.WorldTranslationErrorMeters),
            measurements.Max(row => row.WorldRotationErrorRadians),
            measurements.Max(row => row.ProjectedErrorPixels),
            measurements
                .OrderByDescending(row => row.ProjectedErrorPixels)
                .ThenByDescending(row => row.WorldTranslationErrorMeters)
                .ThenBy(row => row.BoneName, StringComparer.Ordinal)
                .Take(maximumReportedWorstBones)
                .ToArray());
    }

    private static Transform3D ExpectedWorldTransform(
        ActorReviewContract.RetailNode node,
        ActorReviewContract.Sample sample,
        float unitsToMeters)
    {
        return new Transform3D(
            node.World.Basis.Scaled(Vector3.One * unitsToMeters),
            GamebryoCoordinate.ConvertVector(
                node.World.Translation - sample.ActorRoot.Translation) * unitsToMeters);
    }

    private static Transform3D ExpectedPaletteWorldTransform(
        ActorReviewContract.SkinPaletteBone bone,
        ActorReviewContract.Sample sample,
        float unitsToMeters)
    {
        var matrix = bone.MatrixRowMajor3x4;
        var linear = new[]
        {
            matrix[SkinLinearM11Index], matrix[SkinLinearM12Index],
            matrix[SkinLinearM13Index],
            matrix[SkinLinearM21Index], matrix[SkinLinearM22Index],
            matrix[SkinLinearM23Index],
            matrix[SkinLinearM31Index], matrix[SkinLinearM32Index],
            matrix[SkinLinearM33Index],
        };
        var cameraRelativeTranslation = new Vector3(
            matrix[SkinTranslationXIndex],
            matrix[SkinTranslationYIndex],
            matrix[SkinTranslationZIndex]);
        var actorRelativeTranslation = cameraRelativeTranslation +
            sample.Camera.WorldTranslationGameUnits - sample.ActorRoot.Translation;
        return new Transform3D(
            GamebryoCoordinate.ConvertBasis(
                linear,
                1.0f,
                $"frame {sample.Frame} skin {bone.Name}")
                .Scaled(Vector3.One * unitsToMeters),
            GamebryoCoordinate.ConvertVector(actorRelativeTranslation) * unitsToMeters);
    }

    private static float BasisComponentError(Basis one, Basis two)
    {
        var left = BasisValues(one);
        var right = BasisValues(two);
        return left.Select((value, index) => MathF.Abs(value - right[index])).Max();
    }

    private static string SafeToken(string value)
    {
        var token = new string(value.Select(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray());
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Actor review shot kind cannot form a file name.");
        return token;
    }

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static float[] BasisValues(Basis value) => new[]
    {
        value.X.X, value.Y.X, value.Z.X,
        value.X.Y, value.Y.Y, value.Z.Y,
        value.X.Z, value.Y.Z, value.Z.Z,
    };

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

    private readonly record struct NodeMapping(
        int BoneIndex,
        string BoneName,
        string ParentBoneName,
        string NodePath,
        int CandidateCount);

    private readonly record struct SkinMapping(
        string NodePath,
        string GeometryName,
        string MeshName,
        MeshInstance3D Mesh,
        Skin Skin,
        IReadOnlyList<SkinBinding> Bones);

    private readonly record struct SkinBinding(
        int BindIndex,
        int BoneIndex,
        string BoneName,
        Transform3D InverseBindPose);

    private readonly record struct PaletteApplication(
        float MaximumCandidateLinearError,
        float MaximumCandidateTranslationErrorGameUnits);

    private readonly record struct SkinPaletteMeasurement(
        float MaximumLinearError,
        float MaximumTranslationErrorGameUnits,
        IReadOnlyList<SkinPaletteBoneMeasurement> WorstBones);

    private readonly record struct SkinPaletteBoneMeasurement(
        string GeometryName,
        string BoneName,
        int BindIndex,
        float LinearError,
        float TranslationErrorGameUnits);

    private readonly record struct PoseMeasurement(
        float MaximumLocalTranslationErrorMeters,
        float MaximumLocalRotationErrorRadians,
        float MaximumWorldTranslationErrorMeters,
        float MaximumWorldRotationErrorRadians,
        float MaximumProjectedErrorPixels,
        IReadOnlyList<BoneMeasurement> WorstBones);

    private readonly record struct BoneMeasurement(
        string BoneName,
        string NodePath,
        float LocalTranslationErrorMeters,
        float LocalRotationErrorRadians,
        float WorldTranslationErrorMeters,
        float WorldRotationErrorRadians,
        float ProjectedErrorPixels,
        float[] ActualWorldPositionMeters,
        float[] ActualScreenPixels,
        float[] RetailScreenPixels);
}
