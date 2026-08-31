using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    internal string? VisualProofReportPath { get; private set; }

    private sealed class OpeningVisualCaptureSession
    {
        private const int SeatedCaptureMinimumStage = 8;
        internal const int DocSpatialAcceptanceDeadlineStage = 30;
        private const int NameEntryStage = 10;
        private const int AppearanceCreatorStage = 36;
        private const int StableRenderedFrames = 4;
        private const float SourceTransformTolerance = 0.00001f;
        private const float PixelChannelDeltaMinimum = 0.0039215686f;
        private const float PreviewOpaqueAlphaMinimum = 0.05f;
        private const float PreviewNonBlackChannelMinimum = 0.03f;
        private const float FullyOpaqueAlpha = 1.0f;
        private const string ExpectedCheckpointMode = "checkpoint";
        private const string ExpectedCreatorMode = "creator";
        private const string ExpectedResumeMode = "resume";
        private static readonly string[] CheckpointFrames =
        [
            "owned-imad-dialogue-transition",
            "first-doc-reveal",
            "doc-seated-smoking",
            "name-entry",
            "creator-default",
            "creator-edited",
            "creator-female-default",
            "creator-confirm-ready",
            "controls-released",
        ];
        private static readonly string[] ResumeFrames =
        [
            "first-action-ready",
            "first-action",
        ];
        private static readonly string[] CreatorFrames =
        [
            "first-doc-reveal",
            "doc-seated-smoking",
            "name-entry",
            "creator-default",
            "creator-edited",
            "creator-female-default",
            "creator-confirm-ready",
            "creator-accepted-doc-return",
        ];

        private readonly string _root;
        private readonly string _mode;
        private readonly string _playerName;
        private readonly Dictionary<string, int> _stableFrames =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _captured = new(StringComparer.Ordinal);
        private readonly List<OpeningVisualFrame> _frames = [];
        private Image? _defaultPreviewImage;

        private OpeningVisualCaptureSession(
            string root,
            string mode,
            string playerName)
        {
            _root = root;
            _mode = mode;
            _playerName = playerName;
        }

        internal static OpeningVisualCaptureSession? Create(
            OpeningQuestRuntime host,
            string? requestedRoot,
            string mode,
            string playerName)
        {
            if (string.IsNullOrWhiteSpace(requestedRoot))
                return null;
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Opening visual proof requires the native rendering display driver.");
            if (mode is not ExpectedCheckpointMode and
                not ExpectedCreatorMode and
                not ExpectedResumeMode)
                throw new InvalidOperationException(
                    "Opening visual proof mode is unsupported.");
            var root = Path.GetFullPath(requestedRoot);
            if (Directory.Exists(root) || File.Exists(root))
                throw new InvalidOperationException(
                    $"Opening visual proof output already exists: {root}");
            Directory.CreateDirectory(root);
            var expected = host._configuration.Capture;
            var viewport = host.GetViewport().GetVisibleRect().Size;
            if (!Mathf.IsEqualApprox(viewport.X, expected.ExpectedWidthPixels) ||
                !Mathf.IsEqualApprox(viewport.Y, expected.ExpectedHeightPixels))
                throw new InvalidOperationException(
                    "Opening visual proof viewport differs from the configured native capture.");
            return new OpeningVisualCaptureSession(root, mode, playerName);
        }

        internal async Task<bool> ObserveCheckpointState(OpeningQuestRuntime host)
        {
            if (_mode is not ExpectedCheckpointMode and not ExpectedCreatorMode)
                return false;
            if (!_captured.Contains("owned-imad-dialogue-transition") &&
                OwnedImageSpaceDialogueTransitionActive(host))
            {
                await Capture(
                    host,
                    "owned-imad-dialogue-transition",
                    "01-owned-imad-dialogue-transition.png",
                    null,
                    null);
                return true;
            }
            if (_captured.Contains("owned-imad-dialogue-transition") &&
                !_captured.Contains("first-doc-reveal") &&
                FirstDocRevealActive(host))
            {
                await Capture(
                    host,
                    "first-doc-reveal",
                    "02-first-doc-reveal.png",
                    null,
                    null);
                return true;
            }
            if (await CaptureWhenStable(
                    host,
                    "doc-seated-smoking",
                    "03-doc-seated-smoking.png",
                    host._stage >= SeatedCaptureMinimumStage &&
                    host._stage < DocSpatialAcceptanceDeadlineStage &&
                    host._guideFurnitureOccupied &&
                    host._guideAnimationObjectIdleFormId?.Equals(
                        host._flow.GuideActorAi.FurnitureOccupancy
                            .AnimationObjectIdleFormId,
                        StringComparison.OrdinalIgnoreCase) == true &&
                    DocPresentationInFrame(host)))
                return true;

            var lineEdit = host._activeModal is null
                ? null
                : NodeTraversal.Descendants<LineEdit>(host._activeModal).SingleOrDefault();
            if (lineEdit is not null && !_captured.Contains("name-entry"))
                lineEdit.Text = _playerName;
            if (await CaptureWhenStable(
                    host,
                    "name-entry",
                    "04-name-entry.png",
                    host._stage == NameEntryStage &&
                    lineEdit is not null &&
                    lineEdit.IsVisibleInTree() &&
                    lineEdit.Text == _playerName))
                return true;

            var appearanceVisible = host._activeModal is not null &&
                host._raceSexMenuHost is { ActiveEntryCount: > 0 } raceSexMenu &&
                raceSexMenu.ActiveList is "faceGeometry" or "sex" &&
                host._appearancePreviewHost is not null &&
                NodeTraversal.Descendants<OptionButton>(host._activeModal).Count() == 0 &&
                NodeTraversal.Descendants<HSlider>(host._activeModal).Count() == 0;
            if (await CaptureWhenStable(
                    host,
                    "creator-default",
                    "05-stage36-creator-default.png",
                    host._stage == AppearanceCreatorStage &&
                    appearanceVisible &&
                    host._acceptanceAppearancePhase == AcceptanceAppearancePhase.InitialSex))
                return true;
            if (await CaptureWhenStable(
                    host,
                    "creator-edited",
                    "06-stage36-creator-edited.png",
                    host._stage == AppearanceCreatorStage &&
                    appearanceVisible &&
                    host._acceptanceAppearancePhase == AcceptanceAppearancePhase.ResetGeometry))
                return true;
            if (await CaptureWhenStable(
                    host,
                    "creator-female-default",
                    "07-stage36-creator-female-default.png",
                    host._stage == AppearanceCreatorStage &&
                    appearanceVisible &&
                    host._sexIndex == 1 &&
                    host._appearancePreviewHost is not null &&
                    host._acceptanceAppearancePhase == AcceptanceAppearancePhase.SelectParts))
                return true;
            return await CaptureWhenStable(
                host,
                "creator-confirm-ready",
                "08-stage36-creator-confirm-ready.png",
                host._stage == AppearanceCreatorStage &&
                appearanceVisible &&
                host._acceptanceAppearancePhase == AcceptanceAppearancePhase.Complete);
        }

        internal async Task CaptureCheckpointRelease(OpeningQuestRuntime host)
        {
            if (_mode != ExpectedCheckpointMode ||
                host._stage != host.AuthoredCheckpointStage() ||
                host._activeModal is not null ||
                !host._playerControls[MovementControlIndex] ||
                host._activePlayerPackage is not null)
                throw new InvalidOperationException(
                    "Opening visual proof did not reach the clean checkpoint control release.");
            await CaptureAfterRenderedFrames(
                host,
                "controls-released",
                "09-stage55-controls-released.png");
        }

        internal async Task CaptureCreatorAccepted(OpeningQuestRuntime host)
        {
            if (_mode != ExpectedCreatorMode ||
                host._stage != host.AuthoredAppearanceReturnStage() ||
                host._activeModal is not null ||
                host._acceptanceAppearancePhase != AcceptanceAppearancePhase.Complete)
                throw new InvalidOperationException(
                    "Opening visual proof did not persist the real RaceSexMenu stage36-to-40 accept path.");
            await CaptureAfterRenderedFrames(
                host,
                "creator-accepted-doc-return",
                "09-stage40-creator-accepted-doc-return.png");
        }

        internal async Task CaptureFirstActionReady(OpeningQuestRuntime host)
        {
            if (_mode != ExpectedResumeMode ||
                host._activeModal is not null ||
                !host._playerControls[MovementControlIndex] ||
                host._activePlayerPackage is not null)
                throw new InvalidOperationException(
                    "Opening visual proof first action is not cleanly released.");
            await CaptureAfterRenderedFrames(
                host,
                "first-action-ready",
                "01-stage55-first-action-ready.png");
        }

        internal async Task CaptureFirstAction(
            OpeningQuestRuntime host,
            float progressMeters,
            float minimumMeters)
        {
            if (_mode != ExpectedResumeMode || progressMeters < minimumMeters)
                throw new InvalidOperationException(
                    "Opening visual proof first action did not reach its configured threshold.");
            await CaptureAfterRenderedFrames(
                host,
                "first-action",
                "02-stage55-first-action.png",
                progressMeters,
                minimumMeters);
        }

        internal string Complete(
            OpeningQuestRuntime host,
            OpeningCampaignState finalState)
        {
            var required = _mode switch
            {
                ExpectedCheckpointMode => CheckpointFrames,
                ExpectedCreatorMode => CreatorFrames,
                _ => ResumeFrames,
            };
            var missing = required.Where(value => !_captured.Contains(value)).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException(
                    "Opening visual proof is missing required frames: " +
                    string.Join(", ", missing));
            var openingPath = Path.GetFullPath(host._opening.Path);
            var reportPath = Path.Combine(_root, "opening-visual-proof.json");
            var sourceClosure = SourceClosure(host, required);
            var report = new
            {
                schema = "opennv-fnv-opening-visual-proof/v1",
                status = "pass",
                mode = _mode,
                claimBoundary = "native source-bound presentation proof; not retail parity",
                renderer = RenderingServer.GetCurrentRenderingMethod().ToString(),
                displayDriver = DisplayServer.GetName(),
                configuration = new
                {
                    schema = RuntimeConfiguration.ExpectedSchema,
                    sha256 = host._configuration.Sha256,
                },
                openingManifest = new
                {
                    path = openingPath,
                    sha256 = HashFile(openingPath),
                },
                sourceClosure,
                frames = _frames,
                final = new
                {
                    stage = finalState.Stage,
                    completed = finalState.Completed,
                    playerName = finalState.PlayerName,
                    sexIndex = finalState.SexIndex,
                    raceFormId = finalState.Appearance.RaceFormId,
                    hairFormId = finalState.Appearance.HairFormId,
                    eyesFormId = finalState.Appearance.EyesFormId,
                    faceGeometryControls =
                        finalState.Appearance.FaceGeometryControlValues,
                },
            };
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }) + System.Environment.NewLine);
            return reportPath;
        }

        private static OpeningSourceClosureAcceptance SourceClosure(
            OpeningQuestRuntime host,
            IReadOnlyList<string> requiredFrames)
        {
            var dialogueInfos = host._flow.TopicsByFormId.Values
                .SelectMany(topic => topic.Infos)
                .Append(host._flow.PsychologyRootInfo)
                .DistinctBy(info => info.FormId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var dialogueResponses = dialogueInfos.Sum(info => info.Responses.Count);
            var animationObjectSurfaces = host._guideAnimationObjectSurfaces.Values
                .Sum(surfaces => surfaces.Count);
            var unaccounted = new List<string>();
            if (host._roleNodes.Count != host._flow.SceneRoles.Count)
                unaccounted.Add("scene-role-runtime-node-count");
            if (!host._flow.CommandContract.AllEmittedKindsRuntimeBlocking ||
                !host._flow.CommandContract.AllDeclaredRecordReferencesResolved)
                unaccounted.Add("opening-command-runtime-or-record-identity");
            if (dialogueInfos.Length != host._flow.DialogueVoice.InfoCount ||
                dialogueResponses != host._flow.DialogueVoice.ResponseCount)
                unaccounted.Add("dialogue-info-response-voice-closure");
            if (animationObjectSurfaces !=
                host._guideActor.Actor.Surfaces.Count(surface =>
                    surface.Role.Equals(
                        host._flow.GuideActorAi.AnimationObjects.Single().ComponentRole,
                        StringComparison.OrdinalIgnoreCase)))
                unaccounted.Add("guide-animation-object-surface-closure");
            var omitted = host._guideActor.Actor.OmittedSurfaces.Select(surface =>
                $"guide-actor:{surface.Role}:{surface.Shape}:{surface.Disposition}")
                .ToArray();
            if (unaccounted.Count != 0)
                throw new InvalidOperationException(
                    "Opening source closure has unaccounted contracts: " +
                    string.Join(", ", unaccounted));
            var nativeCreatorControls = host._flow.Character.Appearance.FaceGen
                .ControlSpace.NativeGeometryControls.Count;
            var previewCreatorControls = host._flow.Character.Appearance.FaceGen
                .PreviewHead.GeometryControlCount;
            var savedCreatorControls = host._faceGeometryControlValues.Count;
            if (nativeCreatorControls == 0 ||
                previewCreatorControls != nativeCreatorControls ||
                savedCreatorControls != nativeCreatorControls)
                throw new InvalidOperationException(
                    "Opening source closure has incomplete creator control bindings.");
            var unsupported = new[]
            {
                "non-default-race-hair-eye-live-3d-face-preview",
            };
            return new OpeningSourceClosureAcceptance(
                "opennv-fnv-first-slice-source-closure/v1",
                "source-accounted-playable-claim-blocked-by-explicit-capability-gap",
                omitted.Length == 0 && unsupported.Length == 0 && unaccounted.Count == 0,
                host._opening.IntroVideoPath,
                HashFile(host._opening.IntroVideoPath),
                requiredFrames,
                host._flow.SceneRoles.Count,
                host._roleNodes.Count,
                host._loaded.MainContent.PlacedReferences.Count,
                host._flow.CommandContract.CommandCount,
                host._flow.CommandContract.CommandCount,
                dialogueInfos.Length,
                host._flow.DialogueVoice.InfoCount,
                dialogueResponses,
                host._flow.DialogueVoice.ResponseCount,
                host._flow.Menus.Count,
                host._flow.GuideActorAi.Packages.Count,
                host._flow.GuideActorAi.AnimationObjects.Count,
                host._guideActor.Actor.Surfaces.Count,
                animationObjectSurfaces,
                host._flow.ImageSpaceModifiers.Count,
                OpeningCigaretteSmokePresentation.Authority,
                nativeCreatorControls,
                previewCreatorControls,
                savedCreatorControls,
                omitted,
                unsupported,
                unaccounted);
        }

        private async Task<bool> CaptureWhenStable(
            OpeningQuestRuntime host,
            string role,
            string filename,
            bool ready)
        {
            if (_captured.Contains(role))
                return false;
            if (!ready)
            {
                _stableFrames[role] = 0;
                return false;
            }
            var stable = _stableFrames.GetValueOrDefault(role) + 1;
            _stableFrames[role] = stable;
            if (stable < StableRenderedFrames)
                return true;
            await Capture(host, role, filename, null, null);
            return true;
        }

        private async Task CaptureAfterRenderedFrames(
            OpeningQuestRuntime host,
            string role,
            string filename,
            float? progressMeters = null,
            float? minimumMeters = null)
        {
            if (_captured.Contains(role))
                return;
            for (var frame = 0; frame < StableRenderedFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(host, role, filename, progressMeters, minimumMeters);
        }

        private async Task Capture(
            OpeningQuestRuntime host,
            string role,
            string filename,
            float? progressMeters,
            float? minimumMeters)
        {
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            var path = Path.Combine(_root, filename);
            if (File.Exists(path))
                throw new InvalidOperationException(
                    $"Opening visual proof frame already exists: {path}");
            var image = host.GetViewport().GetTexture().GetImage();
            if (image.IsEmpty())
                throw new InvalidOperationException(
                    $"Opening visual proof could not read frame: {path}");
            var expected = host._configuration.Capture;
            if (image.GetWidth() != expected.ExpectedWidthPixels ||
                image.GetHeight() != expected.ExpectedHeightPixels)
                throw new InvalidOperationException(
                    $"Opening visual proof frame dimensions differ: {path}");
            if (image.SavePng(path) != Error.Ok)
                throw new InvalidOperationException(
                    $"Opening visual proof could not save frame: {path}");
            if (role.StartsWith("creator-", StringComparison.Ordinal) &&
                role != "creator-accepted-doc-return" &&
                host._raceSexRenderedDeviceHost is { } renderedDevice)
            {
                SaveRenderedDeviceLayer(
                    renderedDevice.ScreenViewport,
                    Path.Combine(_root, $"{role}-screen-source.png"),
                    role,
                    "screen-source");
                SaveRenderedDeviceLayer(
                    renderedDevice.DeviceViewport,
                    Path.Combine(_root, $"{role}-device-composite.png"),
                    role,
                    "device-composite");
            }
            var animationObject = host._flow.GuideActorAi.AnimationObjects.Single();
            var declaredSurfaces = host._guideActor.Actor.Surfaces.Where(surface =>
                    surface.Role.Equals(
                        animationObject.ComponentRole,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var runtimeSurfaces = host._guideAnimationObjectSurfaces[animationObject.FormId];
            var docAcceptance = role == "doc-seated-smoking"
                ? await MeasureDocPresentation(host, image)
                : null;
            var creatorAcceptance = role.StartsWith(
                    "creator-",
                    StringComparison.Ordinal) &&
                role != "creator-accepted-doc-return"
                ? MeasureCreatorPresentation(host, role)
                : null;
            _captured.Add(role);
            _frames.Add(new OpeningVisualFrame(
                role,
                path,
                HashFile(path),
                new FileInfo(path).Length,
                image.GetWidth(),
                image.GetHeight(),
                host._stage,
                host._activeModal is not null,
                host._guideFurnitureOccupied,
                host._guideFurnitureExiting,
                animationObject.FormId,
                animationObject.Sha256,
                animationObject.AttachmentNode,
                runtimeSurfaces.Count,
                runtimeSurfaces.All(surface => surface.Visible),
                declaredSurfaces.All(surface => surface.RigidShapeTransformBaked),
                docAcceptance,
                creatorAcceptance,
                host._acceptanceAppearancePhase.ToString(),
                host._faceGeometryControlValues.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal),
                host._loaded.Player.GlobalPosition,
                host._playerControls
                    .Select(value => value ? EnabledControlValue : DisabledControlValue)
                    .ToArray(),
                Transition(host),
                progressMeters,
                minimumMeters));
            GD.Print(
                $"OPENNV_OPENING_VISUAL_FRAME role={role} stage={host._stage} " +
                $"path={path} sha256={_frames[^1].Sha256} " +
                $"cigaretteVisible={runtimeSurfaces.All(surface => surface.Visible)} " +
                $"cigarettePixels={docAcceptance?.Cigarette.DifferentialPixels ?? 0} " +
                $"smokePixels={docAcceptance?.Smoke.DifferentialPixels ?? 0} " +
                $"previewNonBlack={creatorAcceptance?.NonBlackPixels ?? 0} " +
                $"rigidShapeTransformBaked=" +
                $"{declaredSurfaces.All(surface => surface.RigidShapeTransformBaked)}");
        }

        private static void SaveRenderedDeviceLayer(
            SubViewport viewport,
            string path,
            string role,
            string layer)
        {
            if (File.Exists(path))
                throw new InvalidOperationException(
                    $"Opening rendered-device layer already exists: {path}");
            var image = viewport.GetTexture().GetImage();
            if (image.IsEmpty() || image.SavePng(path) != Error.Ok)
                throw new InvalidOperationException(
                    $"Opening rendered-device layer could not be saved: {path}");
            GD.Print(
                $"OPENNV_NEW_GAME_RACESEX_LAYER role={role} layer={layer} " +
                $"path={path} sha256={HashFile(path)}");
        }

        private static bool OwnedImageSpaceDialogueTransitionActive(
            OpeningQuestRuntime host)
        {
            var fade = host._imageSpaceFade.Color;
            var firstStage = host._flow.Stages.Keys.Min();
            var sourceModifierEditorIds = host._flow.Stages[firstStage].Commands
                .Where(command =>
                    command.Kind == "imageSpaceModifier" &&
                    command.Operation?.Equals(
                        "apply",
                        StringComparison.OrdinalIgnoreCase) == true)
                .Select(command => command.ModifierEditorId)
                .Where(editorId => editorId is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return host._activeDialogueInfoFormId is not null &&
                host._dialogueVoice.Playing &&
                sourceModifierEditorIds.Length == 1 &&
                host._activeImageSpaceModifiers.Values.Any(active =>
                    active.Modifier.EditorId.Equals(
                        sourceModifierEditorIds[0],
                        StringComparison.OrdinalIgnoreCase)) &&
                fade.A > 0.0f &&
                fade.A <= FullyOpaqueAlpha;
        }

        private static bool FirstDocRevealActive(OpeningQuestRuntime host)
        {
            if (!host._guideActorResolved ||
                Mathf.IsEqualApprox(host._imageSpaceFade.Color.A, FullyOpaqueAlpha))
                return false;
            return ProjectBounds(
                host._loaded.Player.Camera,
                ActorModelSlice.PosedWorldBounds(host._guideActor.Actor),
                host.GetViewport().GetVisibleRect().Size).InFrame;
        }

        private static OpeningTransitionVisualAcceptance Transition(
            OpeningQuestRuntime host)
        {
            OpeningDialogueVisualAcceptance? dialogue = null;
            if (host._activeDialogueInfoFormId is { } infoFormId)
            {
                var infos = host._flow.TopicsByFormId.Values
                    .SelectMany(topic => topic.Infos)
                    .Append(host._flow.PsychologyRootInfo)
                    .Where(info => info.FormId.Equals(
                        infoFormId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (infos.Length != 1)
                    throw new InvalidOperationException(
                        "Opening visual proof active INFO is absent or ambiguous.");
                var response = infos[0].Responses.Single(value =>
                    value.Index == host._activeDialogueResponseIndex);
                dialogue = new OpeningDialogueVisualAcceptance(
                    infoFormId,
                    response.Index,
                    response.Text,
                    response.Voice.LogicalPath,
                    response.Voice.Sha256,
                    (float)host._dialogueVoice.GetPlaybackPosition());
            }
            return new OpeningTransitionVisualAcceptance(
                new OpeningVisualColor(
                    host._imageSpaceFade.Color.R,
                    host._imageSpaceFade.Color.G,
                    host._imageSpaceFade.Color.B,
                    host._imageSpaceFade.Color.A),
                host._activeImageSpaceModifiers.Values
                    .OrderBy(value => value.Modifier.FormId, StringComparer.Ordinal)
                    .Select(value => new OpeningImageSpaceVisualAcceptance(
                        value.Modifier.FormId,
                        value.Modifier.EditorId,
                        value.Modifier.RecordSha256,
                        value.Modifier.DurationSeconds,
                        (float)value.ElapsedSeconds))
                    .ToArray(),
                dialogue,
                host._lastAppliedPlayerCameraAnimation?.FormId,
                host._lastAppliedPlayerCameraAnimation?.Sha256,
                host._lastAppliedPlayerCameraTime);
        }

        private static async Task<OpeningDocVisualAcceptance> MeasureDocPresentation(
            OpeningQuestRuntime host,
            Image visibleImage)
        {
            var spatial = MeasureDocSpatial(host);
            var smoke = host._guideCigaretteSmokePresentation
                ?? throw new InvalidOperationException(
                    "Opening visual proof has no cigarette smoke presentation.");
            var cigaretteBounds = ActorModelSlice.PosedWorldBounds(
                host._guideActor.Actor,
                smoke.Cigarette);
            var cigaretteProjection = ProjectBounds(
                host._loaded.Player.Camera,
                cigaretteBounds,
                host.GetViewport().GetVisibleRect().Size);
            var smokeBounds = smoke.SmokeWorldBounds();
            var smokeProjection = ProjectBounds(
                host._loaded.Player.Camera,
                smokeBounds,
                host.GetViewport().GetVisibleRect().Size);
            if (!cigaretteProjection.InFrame || !smokeProjection.InFrame)
                throw new InvalidOperationException(
                    "Opening visual proof cigarette or smoke is outside the camera frame.");

            var cigarettePixels = await MeasureVisibilityDifferential(
                host,
                visibleImage,
                cigaretteProjection.Rect,
                smoke.Cigarette.Mesh,
                visible: false);
            var smokePixels = await MeasureVisibilityDifferential(
                host,
                visibleImage,
                smokeProjection.Rect,
                smoke.Root,
                visible: false);
            if (cigarettePixels == 0 || smokePixels == 0)
                throw new InvalidOperationException(
                    "Opening visual proof cigarette or smoke has no measurable rendered pixels.");
            return new OpeningDocVisualAcceptance(
                spatial,
                new OpeningCigaretteVisualAcceptance(
                    smoke.Cigarette.SourceFormId!,
                    smoke.Cigarette.AttachmentNode!,
                    smoke.Cigarette.RigidShapeTransformBaked,
                    Vector(smoke.TipLocal),
                    Vector(smoke.TipWorld),
                    Bounds(cigaretteBounds),
                    cigaretteProjection,
                    cigarettePixels),
                new OpeningSmokeVisualAcceptance(
                    OpeningCigaretteSmokePresentation.Authority,
                    smoke.ActivePuffCount,
                    smoke.LifetimeSeconds,
                    Vector(smoke.TipWorld),
                    Bounds(smokeBounds),
                    smokeProjection,
                    smokePixels));
        }

        internal static bool TryValidateDocSpatial(
            OpeningQuestRuntime host,
            out string telemetry)
        {
            telemetry = "";
            if (host._stage < SeatedCaptureMinimumStage ||
                host._stage >= DocSpatialAcceptanceDeadlineStage ||
                !host._guideFurnitureOccupied ||
                host._guideAnimationObjectIdleFormId?.Equals(
                    host._flow.GuideActorAi.FurnitureOccupancy.AnimationObjectIdleFormId,
                    StringComparison.OrdinalIgnoreCase) != true)
                return false;
            var spatial = MeasureDocSpatial(host, requireClearBedOccupancy: false);
            var seatedAnimationSeconds =
                host._guideFurnitureLayeredSeatedAnimation
                    ?.FurniturePositionSeconds ?? 0.0;
            var packageAnimationSeconds =
                host._guideFurnitureLayeredSeatedAnimation
                    ?.PackagePositionSeconds ?? 0.0;
            var smoke = host._guideCigaretteSmokePresentation
                ?? throw new InvalidOperationException(
                    "Opening spatial proof has no cigarette/smoke presentation.");
            var layeredTelemetry =
                $"furniture={spatial.FurnitureReferenceFormId} " +
                $"stage={host._stage} " +
                $"seatedAnimationSeconds={seatedAnimationSeconds:R} " +
                $"packageAnimationSeconds={packageAnimationSeconds:R} " +
                $"markerPositionErrorGu={spatial.MarkerPositionErrorGameUnits:R} " +
                $"markerBasisError={spatial.MarkerBasisError:R} " +
                $"actorForwardToPlayerDot={spatial.ActorForwardToPlayerDot:R} " +
                $"patientBed={spatial.PatientBedReferenceFormId} " +
                $"actorAabbIntersectsBed={spatial.ActorAabbIntersectsPatientBed} " +
                $"actorTrianglesCrossingBed=" +
                $"{spatial.ActorTrianglesIntersectingPatientBed}/" +
                $"{spatial.ActorTriangleCount} " +
                $"penetratingSurfaces=[{string.Join(" | ", spatial.PenetratingSurfaces)}] " +
                $"bedCollisionTriangles={spatial.PatientBedCollisionTriangles} " +
                $"bedVisualBounds={spatial.PatientBedWorldBounds} " +
                $"bedCollisionBounds={spatial.PatientBedOccupiedWorldBounds} " +
                $"camera={spatial.Camera.FormId} " +
                $"cameraPositionErrorMeters={spatial.Camera.PositionErrorMeters:R} " +
                $"cameraBasisError={spatial.Camera.BasisError:R} " +
                $"cigaretteForm={smoke.Cigarette.SourceFormId} " +
                $"cigaretteAttachment={smoke.Cigarette.AttachmentNode} " +
                $"cigaretteVisible={smoke.Cigarette.Mesh.Visible} " +
                $"cigaretteTip={spatial.CigaretteTipWorld} " +
                $"smokeActive={smoke.Active} smokePuffs={smoke.ActivePuffCount} " +
                $"smokeLifetimeSeconds={smoke.LifetimeSeconds:R} " +
                $"smokeAuthority={OpeningCigaretteSmokePresentation.Authority}";
            if (!spatial.ActorIntersectsPatientBed)
            {
                telemetry = layeredTelemetry;
                return true;
            }
            var layered = host._guideFurnitureLayeredSeatedAnimation
                ?? throw new InvalidOperationException(
                    "Opening spatial proof has no source-priority layered playback.");
            layered.PoseFurnitureOnlyAtCurrentPhase();
            var furnitureOnly = MeasureDocSpatial(
                host,
                requireClearBedOccupancy: false);
            telemetry = layeredTelemetry +
                $" furnitureOnlyTrianglesCrossingBed=" +
                $"{furnitureOnly.ActorTrianglesIntersectingPatientBed}/" +
                $"{furnitureOnly.ActorTriangleCount} " +
                $"furnitureOnlyPenetratingSurfaces=[" +
                $"{string.Join(" | ", furnitureOnly.PenetratingSurfaces)}]";
            return false;
        }

        private static OpeningDocSpatialAcceptance MeasureDocSpatial(
            OpeningQuestRuntime host,
            bool requireClearBedOccupancy = true)
        {
            var furniture = host._flow.GuideActorAi.FurnitureOccupancy.Furniture;
            var placedFurniture = host._loaded.MainContent.PlacedReferences.Single(value =>
                value.FormId.Equals(
                    furniture.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) &&
                value.BaseFormId.Equals(
                    furniture.BaseFormId,
                    StringComparison.OrdinalIgnoreCase));
            var marker = furniture.Marker;
            var actorRootOffset = marker.OffsetGodotGameUnits -
                marker.ActorPlacementOffset.OffsetGodotGameUnits;
            var expectedMarker = placedFurniture.Placement.Transform * new Transform3D(
                new Basis(marker.RotationGodot),
                actorRootOffset);
            var expectedActor = expectedMarker * new Transform3D(
                new Basis(marker.ActorForwardHeadingDelta.RotationGodot),
                Vector3.Zero);
            var markerPositionError = host._guideActor.Placement.Position.DistanceTo(
                expectedMarker.Origin);
            var markerBasisError = BasisError(
                host._guideActor.Placement.Basis.Orthonormalized(),
                expectedActor.Basis.Orthonormalized());
            if (markerPositionError > SourceTransformTolerance ||
                markerBasisError > SourceTransformTolerance)
                throw new InvalidOperationException(
                    "Opening visual proof guide transform differs from the exact owned " +
                    "furniture marker/GMST contract.");

            var actorBounds = ActorModelSlice.PosedWorldBounds(host._guideActor.Actor);
            var actorOrigin = host._guideActor.Placement.GlobalPosition;
            var playerOffset = host._loaded.Player.GlobalPosition - actorOrigin;
            playerOffset.Y = 0.0f;
            var actorForward = -host._guideActor.Placement.GlobalBasis.Z;
            actorForward.Y = 0.0f;
            if (playerOffset.IsZeroApprox() || actorForward.IsZeroApprox())
                throw new InvalidOperationException(
                    "Opening visual proof guide/player facing vector is degenerate.");
            var actorForwardToPlayerDot = actorForward.Normalized().Dot(
                playerOffset.Normalized());
            if (!float.IsFinite(actorForwardToPlayerDot) ||
                actorForwardToPlayerDot <= 0.0f)
                throw new InvalidOperationException(
                    "Opening visual proof guide faces away from the player at the exact " +
                    "owned furniture marker/GMST root.");
            var patientBedSource =
                host._flow.GuideActorAi.FurnitureOccupancy.PatientBed;
            var patientBedMatches = host._loaded.MainContent.PlacedReferences
                .Where(value =>
                    value.FormId.Equals(
                        patientBedSource.ReferenceFormId,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.BaseFormId.Equals(
                        patientBedSource.BaseFormId,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.BaseEditorId.Equals(
                        patientBedSource.EditorId,
                        StringComparison.Ordinal))
                .ToArray();
            if (patientBedMatches.Length != 1)
                throw new InvalidOperationException(
                    "Opening visual proof did not resolve exactly one hash-bound owned " +
                    $"patient bed {patientBedSource.ReferenceFormId}/" +
                    $"{patientBedSource.BaseFormId}/{patientBedSource.EditorId}: " +
                    $"matches={patientBedMatches.Length}.");
            var patientBedReference = patientBedMatches[0];
            var patientBed = new
            {
                Reference = patientBedReference,
                Bounds = ActorModelSlice.WorldBounds(patientBedReference.Visual),
            };
            var collisionRoot = patientBed.Reference.Placement.GetNodeOrNull<Node3D>(
                    $"AUTHORED_COLLISION_{patientBed.Reference.AssetId}")
                ?? throw new InvalidOperationException(
                    "Opening visual proof patient bed has no verified owned collision root.");
            var occupiedBedBounds = ActorModelSlice.WorldBounds(collisionRoot);
            var actorAabbIntersectsBed = actorBounds.Intersects(occupiedBedBounds);
            var intersectingActorTriangles = 0;
            var actorTriangleCount = 0;
            var bedCollisionTriangles = 0;
            var penetratingSurfaces = Array.Empty<string>();
            if (actorAabbIntersectsBed)
            {
                var intersectionMinimum = actorBounds.Position.Max(
                    occupiedBedBounds.Position);
                var intersectionMaximum = actorBounds.End.Min(occupiedBedBounds.End);
                var intersectingSurfaces = host._guideActor.Actor.Surfaces
                    .Where(surface => surface.Mesh.Visible)
                    .Select(surface => new
                    {
                        surface.Role,
                        surface.Shape,
                        Bounds = ActorModelSlice.PosedWorldBounds(
                            host._guideActor.Actor,
                            surface),
                    })
                    .Where(surface => surface.Bounds.Intersects(occupiedBedBounds))
                    .Select(surface =>
                        $"{surface.Role}/{surface.Shape}:{surface.Bounds}")
                    .ToArray();
                var bedFaces = NodeTraversal.Descendants<MeshInstance3D>(collisionRoot)
                    .Where(mesh => mesh.Mesh is not null)
                    .SelectMany(mesh => mesh.Mesh!.GetFaces()
                        .Select(vertex => mesh.ToGlobal(vertex)))
                    .ToArray();
                if (bedFaces.Length == 0 || bedFaces.Length % 3 != 0)
                    throw new InvalidOperationException(
                        "Opening visual proof patient bed has no triangular occupied mesh.");
                bedCollisionTriangles = bedFaces.Length / 3;
                var bedTriangles = Enumerable.Range(0, bedFaces.Length / 3)
                    .Select(index => new ActorModelSlice.PosedTriangle(
                        bedFaces[index * 3],
                        bedFaces[index * 3 + 1],
                        bedFaces[index * 3 + 2]))
                    .ToArray();
                var surfacePenetration = host._guideActor.Actor.Surfaces
                    .Where(surface => surface.Mesh.Visible)
                    .Select(surface =>
                    {
                        var triangles = ActorModelSlice.PosedWorldTriangles(
                            host._guideActor.Actor,
                            surface);
                        var crossing = triangles.Count(actorTriangle =>
                            BoundsOverlapInclusive(
                                TriangleBounds(actorTriangle),
                                occupiedBedBounds) &&
                            bedTriangles.Any(bedTriangle =>
                                BoundsOverlapInclusive(
                                    TriangleBounds(actorTriangle),
                                    TriangleBounds(bedTriangle)) &&
                                TrianglesIntersect(actorTriangle, bedTriangle)));
                        return new
                        {
                            Surface = surface,
                            Triangles = triangles.Count,
                            Crossing = crossing,
                        };
                    })
                    .ToArray();
                actorTriangleCount = surfacePenetration.Sum(value => value.Triangles);
                intersectingActorTriangles = surfacePenetration.Sum(
                    value => value.Crossing);
                penetratingSurfaces = surfacePenetration
                    .Where(value => value.Crossing != 0)
                    .Select(value =>
                        $"{value.Surface.Role}/{value.Surface.Shape}/" +
                        $"{value.Surface.RuntimeNodeName}/" +
                        $"{value.Surface.AttachmentNode ?? "skinned"}=" +
                        $"{value.Crossing}/{value.Triangles}")
                    .ToArray();
                if (requireClearBedOccupancy && intersectingActorTriangles != 0)
                    throw new InvalidOperationException(
                    "Opening visual proof guide posed triangles cross the owned patient bed: " +
                    $"bed={patientBed.Reference.FormId} actor={actorBounds} " +
                    $"bedVisualBounds={patientBed.Bounds} " +
                    $"bedCollisionBounds={occupiedBedBounds} " +
                    $"intersectionPosition={intersectionMinimum} " +
                    $"intersectionSize={intersectionMaximum - intersectionMinimum} " +
                    $"crossingActorTriangles={intersectingActorTriangles}/" +
                    $"{actorTriangleCount} " +
                    $"visibleIntersectingSurfaces=" +
                    string.Join(" | ", intersectingSurfaces) + ".");
            }
            var actorIntersectsBed = intersectingActorTriangles != 0;

            var camera = MeasureOwnedCamera(host);
            if (camera.PositionErrorMeters > SourceTransformTolerance ||
                camera.BasisError > SourceTransformTolerance)
                throw new InvalidOperationException(
                    "Opening visual proof camera differs from its owned animation track.");

            var smoke = host._guideCigaretteSmokePresentation
                ?? throw new InvalidOperationException(
                    "Opening visual proof has no cigarette smoke presentation.");
            if (!smoke.Active || smoke.ActivePuffCount == 0 ||
                smoke.LifetimeSeconds <= 0.0f ||
                !smoke.Cigarette.Mesh.Visible)
                throw new InvalidOperationException(
                    "Opening visual proof cigarette/smoke lifecycle is inactive.");
            return new OpeningDocSpatialAcceptance(
                furniture.ReferenceFormId,
                Vector(expectedMarker.Origin),
                Vector(host._guideActor.Placement.Position),
                markerPositionError,
                markerBasisError,
                actorForwardToPlayerDot,
                Bounds(actorBounds),
                patientBed.Reference.FormId,
                Bounds(patientBed.Bounds),
                Bounds(occupiedBedBounds),
                actorIntersectsBed,
                actorAabbIntersectsBed,
                intersectingActorTriangles,
                actorTriangleCount,
                bedCollisionTriangles,
                penetratingSurfaces,
                camera,
                Vector(smoke.TipWorld));
        }

        private static bool TrianglesIntersect(
            ActorModelSlice.PosedTriangle first,
            ActorModelSlice.PosedTriangle second)
        {
            return SegmentCrosses(first.A, first.B, second) ||
                SegmentCrosses(first.B, first.C, second) ||
                SegmentCrosses(first.C, first.A, second) ||
                SegmentCrosses(second.A, second.B, first) ||
                SegmentCrosses(second.B, second.C, first) ||
                SegmentCrosses(second.C, second.A, first);
        }

        private static bool SegmentCrosses(
            Vector3 from,
            Vector3 to,
            ActorModelSlice.PosedTriangle triangle) =>
            Geometry3D.SegmentIntersectsTriangle(
                from,
                to,
                triangle.A,
                triangle.B,
                triangle.C).VariantType != Variant.Type.Nil;

        private static Aabb TriangleBounds(ActorModelSlice.PosedTriangle triangle)
        {
            var minimum = triangle.A.Min(triangle.B).Min(triangle.C);
            var maximum = triangle.A.Max(triangle.B).Max(triangle.C);
            return new Aabb(minimum, maximum - minimum);
        }

        private static bool BoundsOverlapInclusive(Aabb first, Aabb second)
        {
            var tolerance = new Vector3(
                SourceTransformTolerance,
                SourceTransformTolerance,
                SourceTransformTolerance);
            var firstMinimum = first.Position - tolerance;
            var firstMaximum = first.End + tolerance;
            var secondMinimum = second.Position - tolerance;
            var secondMaximum = second.End + tolerance;
            return firstMinimum.X <= secondMaximum.X &&
                firstMaximum.X >= secondMinimum.X &&
                firstMinimum.Y <= secondMaximum.Y &&
                firstMaximum.Y >= secondMinimum.Y &&
                firstMinimum.Z <= secondMaximum.Z &&
                firstMaximum.Z >= secondMinimum.Z;
        }

        private static bool DocPresentationInFrame(OpeningQuestRuntime host)
        {
            var smoke = host._guideCigaretteSmokePresentation;
            if (smoke is not { Active: true } || !smoke.Cigarette.Mesh.Visible)
                return false;
            var viewport = host.GetViewport().GetVisibleRect().Size;
            return ProjectBounds(
                    host._loaded.Player.Camera,
                    ActorModelSlice.PosedWorldBounds(
                        host._guideActor.Actor,
                        smoke.Cigarette),
                    viewport).InFrame &&
                ProjectBounds(
                    host._loaded.Player.Camera,
                    smoke.SmokeWorldBounds(),
                    viewport).InFrame;
        }

        private OpeningCreatorVisualAcceptance MeasureCreatorPresentation(
            OpeningQuestRuntime host,
            string role)
        {
            var preview = host._appearancePreviewHost
                ?? throw new InvalidOperationException(
                    "Opening visual proof creator has no exact owned 3D selection preview.");
            var image = preview.CaptureRenderedImage();
            if (image.IsEmpty())
                throw new InvalidOperationException(
                    "Opening visual proof creator preview did not render.");
            var nonTransparent = 0;
            var nonBlack = 0;
            var luminance = 0.0;
            for (var y = 0; y < image.GetHeight(); y++)
            {
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.A <= PreviewOpaqueAlphaMinimum)
                        continue;
                    nonTransparent++;
                    var channel = MathF.Max(pixel.R, MathF.Max(pixel.G, pixel.B));
                    if (channel <= PreviewNonBlackChannelMinimum)
                        continue;
                    nonBlack++;
                    luminance += channel;
                }
            }
            var meanLuminance = nonBlack == 0 ? 0.0f : (float)(luminance / nonBlack);
            if (nonTransparent == 0 || nonBlack == 0 ||
                meanLuminance <= PreviewNonBlackChannelMinimum)
                throw new InvalidOperationException(
                    "Opening visual proof creator selection is transparent or black.");
            var vertexDelta = preview.MaximumAppliedVertexDeltaMeters();
            var nativeControlCount = host._flow.Character.Appearance.FaceGen
                .ControlSpace.NativeGeometryControls.Count;
            var raceSexMenu = host._raceSexMenuHost
                ?? throw new InvalidOperationException(
                    "Opening visual proof creator has no owned RaceSexMenu state.");
            var expectedActiveList = role is "creator-default" or "creator-female-default"
                ? "sex"
                : "faceGeometry";
            var expectedActiveEntryCount = expectedActiveList == "sex"
                ? host._flow.Character.SexChoices.Count +
                    (string.IsNullOrWhiteSpace(host._flow.Character.SexTitle) ? 0 : 1)
                : nativeControlCount;
            if (nativeControlCount == 0 ||
                preview.BoundControlCount != nativeControlCount ||
                raceSexMenu.ActiveList != expectedActiveList ||
                raceSexMenu.ActiveEntryCount != expectedActiveEntryCount ||
                raceSexMenu.VisibleEntryCount <= 0 ||
                (role == "creator-female-default" && host._sexIndex != 1))
                throw new InvalidOperationException(
                    "Opening visual proof creator control surface is incomplete.");
            var referenceCanvasSize = host._flow.ReferenceCanvasSize;
            var viewportSize = host._viewport.Size;
            var expectedCanvasScale = Mathf.Min(
                viewportSize.X / referenceCanvasSize.X,
                viewportSize.Y / referenceCanvasSize.Y);
            var expectedCanvasPosition =
                (viewportSize - referenceCanvasSize * expectedCanvasScale) *
                OwnedUiTheme.CenteringFactor;
            var sourceFaceGrab = host._flow.Menus["appearance"].RaceSexMenuTiles!
                .FaceGrab.Rect;
            var renderedDevice = host._raceSexRenderedDeviceHost
                ?? throw new InvalidOperationException(
                    "Opening visual proof creator has no rendered-device host.");
            var previewGlobal = preview.Control.GetGlobalRect();
            if (!host._canvas.Scale.IsEqualApprox(
                    Vector2.One * expectedCanvasScale) ||
                !host._canvas.Position.IsEqualApprox(expectedCanvasPosition) ||
                !renderedDevice.ScreenRoot.Size.IsEqualApprox(referenceCanvasSize) ||
                !previewGlobal.Position.IsEqualApprox(
                    renderedDevice.FacePresentationRect.Position) ||
                !previewGlobal.Size.IsEqualApprox(
                    renderedDevice.FacePresentationRect.Size))
                throw new InvalidOperationException(
                    "Opening visual proof RaceSex source-canvas or FaceGrab mapping differs.");
            int editedPixels;
            if (role == "creator-default")
            {
                if (!Mathf.IsZeroApprox(vertexDelta))
                    throw new InvalidOperationException(
                        "Opening visual proof default creator selection is already edited.");
                _defaultPreviewImage = image;
                editedPixels = 0;
            }
            else
            {
                if (_defaultPreviewImage is null || vertexDelta <= 0.0f)
                    throw new InvalidOperationException(
                        "Opening visual proof creator edit has no source vertex delta.");
                editedPixels = CountDifferentialPixels(
                    _defaultPreviewImage,
                    image,
                    new Rect2(Vector2.Zero, new Vector2(
                        image.GetWidth(),
                        image.GetHeight())));
                if (editedPixels == 0)
                    throw new InvalidOperationException(
                        "Opening visual proof creator edit has no rendered image delta.");
            }
            return new OpeningCreatorVisualAcceptance(
                image.GetWidth(),
                image.GetHeight(),
                nonTransparent,
                nonBlack,
                meanLuminance,
                vertexDelta,
                editedPixels,
                nativeControlCount,
                preview.BoundControlCount,
                raceSexMenu.ActiveList,
                raceSexMenu.ActiveEntryCount,
                raceSexMenu.VisibleEntryCount,
                expectedActiveEntryCount,
                new OpeningVisualVector2(
                    host._canvas.Scale.X,
                    host._canvas.Scale.Y),
                new OpeningVisualVector2(
                    host._canvas.Position.X,
                    host._canvas.Position.Y),
                VisualRect(sourceFaceGrab),
                VisualRect(previewGlobal),
                preview.FramingDisposition,
                preview.LightingDisposition);
        }

        private static OpeningCameraVisualAcceptance MeasureOwnedCamera(
            OpeningQuestRuntime host)
        {
            var animation = host._lastAppliedPlayerCameraAnimation
                ?? throw new InvalidOperationException(
                    "Opening visual proof camera has no owned animation source.");
            var track = animation.Track;
            var time = host._lastAppliedPlayerCameraTime;
            var sampleIndex = 0;
            while (sampleIndex + 1 < track.Samples.Count &&
                track.Samples[sampleIndex + 1].TimeSeconds <= time)
                sampleIndex++;
            var first = track.Samples[sampleIndex];
            var second = track.Samples[Math.Min(sampleIndex + 1, track.Samples.Count - 1)];
            var amount = second.TimeSeconds <= first.TimeSeconds
                ? 0.0f
                : (time - first.TimeSeconds) /
                    (second.TimeSeconds - first.TimeSeconds);
            var translation = first.TranslationGodotGameUnits.Lerp(
                second.TranslationGodotGameUnits,
                amount);
            var rotation = first.Rotation.Slerp(second.Rotation, amount).Normalized();
            var parentTransform = Transform3D.Identity;
            foreach (var parent in track.ParentChain)
            {
                parentTransform *= new Transform3D(
                    new Basis(parent.Rotation).Scaled(parent.Scale),
                    parent.TranslationGodotGameUnits * host._loaded.UnitsToMeters);
            }
            var expected = parentTransform * new Transform3D(
                new Basis(rotation),
                translation * host._loaded.UnitsToMeters);
            expected = new Transform3D(expected.Basis.Orthonormalized(), expected.Origin);
            expected.Origin -= Vector3.Up *
                host._configuration.Player.SpawnCenterHeightMeters;
            var actual = host._loaded.Player.Camera.Transform;
            return new OpeningCameraVisualAcceptance(
                animation.FormId,
                animation.EditorId,
                animation.LogicalPath,
                animation.Sha256,
                time,
                Vector(expected.Origin),
                Vector(actual.Origin),
                expected.Origin.DistanceTo(actual.Origin),
                BasisError(expected.Basis, actual.Basis));
        }

        private static async Task<int> MeasureVisibilityDifferential(
            OpeningQuestRuntime host,
            Image visibleImage,
            Rect2 rect,
            Node3D node,
            bool visible)
        {
            var before = node.Visible;
            try
            {
                node.Visible = visible;
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
                var hiddenImage = host.GetViewport().GetTexture().GetImage();
                return CountDifferentialPixels(visibleImage, hiddenImage, rect);
            }
            finally
            {
                node.Visible = before;
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
            }
        }

        private static int CountDifferentialPixels(
            Image first,
            Image second,
            Rect2 rect)
        {
            if (first.GetWidth() != second.GetWidth() ||
                first.GetHeight() != second.GetHeight())
                throw new InvalidOperationException(
                    "Opening visual differential image dimensions differ.");
            var minimumX = Math.Clamp(Mathf.FloorToInt(rect.Position.X), 0, first.GetWidth());
            var minimumY = Math.Clamp(Mathf.FloorToInt(rect.Position.Y), 0, first.GetHeight());
            var maximumX = Math.Clamp(Mathf.CeilToInt(rect.End.X), 0, first.GetWidth());
            var maximumY = Math.Clamp(Mathf.CeilToInt(rect.End.Y), 0, first.GetHeight());
            var count = 0;
            for (var y = minimumY; y < maximumY; y++)
            {
                for (var x = minimumX; x < maximumX; x++)
                {
                    var a = first.GetPixel(x, y);
                    var b = second.GetPixel(x, y);
                    if (MathF.Max(
                            MathF.Abs(a.R - b.R),
                            MathF.Max(
                                MathF.Abs(a.G - b.G),
                                MathF.Max(
                                    MathF.Abs(a.B - b.B),
                                    MathF.Abs(a.A - b.A)))) > PixelChannelDeltaMinimum)
                        count++;
                }
            }
            return count;
        }

        private static OpeningScreenProjection ProjectBounds(
            Camera3D camera,
            Aabb bounds,
            Vector2 viewportSize)
        {
            var corners = new[]
            {
                new Vector3(bounds.Position.X, bounds.Position.Y, bounds.Position.Z),
                new Vector3(bounds.End.X, bounds.Position.Y, bounds.Position.Z),
                new Vector3(bounds.Position.X, bounds.End.Y, bounds.Position.Z),
                new Vector3(bounds.End.X, bounds.End.Y, bounds.Position.Z),
                new Vector3(bounds.Position.X, bounds.Position.Y, bounds.End.Z),
                new Vector3(bounds.End.X, bounds.Position.Y, bounds.End.Z),
                new Vector3(bounds.Position.X, bounds.End.Y, bounds.End.Z),
                new Vector3(bounds.End.X, bounds.End.Y, bounds.End.Z),
            };
            var projected = corners.Where(value => !camera.IsPositionBehind(value))
                .Select(camera.UnprojectPosition)
                .ToArray();
            if (projected.Length == 0)
                return new OpeningScreenProjection(false, new Rect2());
            var minimum = projected.Aggregate((a, b) => a.Min(b));
            var maximum = projected.Aggregate((a, b) => a.Max(b));
            var clippedMinimum = minimum.Max(Vector2.Zero);
            var clippedMaximum = maximum.Min(viewportSize);
            var size = clippedMaximum - clippedMinimum;
            var rect = new Rect2(clippedMinimum, size.Max(Vector2.Zero));
            return new OpeningScreenProjection(
                rect.Size.X > 0.0f && rect.Size.Y > 0.0f,
                rect);
        }

        private static float BasisError(Basis first, Basis second) => MathF.Max(
            first.X.DistanceTo(second.X),
            MathF.Max(
                first.Y.DistanceTo(second.Y),
                first.Z.DistanceTo(second.Z)));

        private static OpeningVisualVector Vector(Vector3 value) =>
            new(value.X, value.Y, value.Z);

        private static OpeningVisualBounds Bounds(Aabb value) =>
            new(Vector(value.Position), Vector(value.Size));

        private static OpeningVisualRect VisualRect(Rect2 value) => new(
            new OpeningVisualVector2(value.Position.X, value.Position.Y),
            new OpeningVisualVector2(value.Size.X, value.Size.Y));

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    private sealed record OpeningVisualFrame(
        string Role,
        string Path,
        string Sha256,
        long Bytes,
        int Width,
        int Height,
        int Stage,
        bool ModalVisible,
        bool GuideFurnitureOccupied,
        bool GuideFurnitureExiting,
        string AnimationObjectFormId,
        string AnimationObjectModelSha256,
        string AnimationObjectAttachmentNode,
        int AnimationObjectSurfaces,
        bool AnimationObjectVisible,
        bool RigidShapeTransformBaked,
        OpeningDocVisualAcceptance? DocAcceptance,
        OpeningCreatorVisualAcceptance? CreatorAcceptance,
        string AppearancePhase,
        IReadOnlyDictionary<string, float> FaceGeometryControls,
        Vector3 PlayerPosition,
        IReadOnlyList<int> PlayerControls,
        OpeningTransitionVisualAcceptance Transition,
        float? ProgressMeters,
        float? MinimumMeters);

    private sealed record OpeningTransitionVisualAcceptance(
        OpeningVisualColor ImageSpaceFade,
        IReadOnlyList<OpeningImageSpaceVisualAcceptance> ActiveImageSpaceModifiers,
        OpeningDialogueVisualAcceptance? Dialogue,
        string? CameraAnimationFormId,
        string? CameraAnimationSha256,
        float CameraAnimationTimeSeconds);

    private sealed record OpeningImageSpaceVisualAcceptance(
        string FormId,
        string EditorId,
        string RecordSha256,
        float DurationSeconds,
        float ElapsedSeconds);

    private sealed record OpeningDialogueVisualAcceptance(
        string InfoFormId,
        int ResponseIndex,
        string Text,
        string VoiceLogicalPath,
        string VoiceSha256,
        float PlaybackSeconds);

    private sealed record OpeningDocVisualAcceptance(
        OpeningDocSpatialAcceptance Spatial,
        OpeningCigaretteVisualAcceptance Cigarette,
        OpeningSmokeVisualAcceptance Smoke);

    private sealed record OpeningDocSpatialAcceptance(
        string FurnitureReferenceFormId,
        OpeningVisualVector ExpectedMarkerCell,
        OpeningVisualVector ActualMarkerCell,
        float MarkerPositionErrorGameUnits,
        float MarkerBasisError,
        float ActorForwardToPlayerDot,
        OpeningVisualBounds ActorWorldBounds,
        string PatientBedReferenceFormId,
        OpeningVisualBounds PatientBedWorldBounds,
        OpeningVisualBounds PatientBedOccupiedWorldBounds,
        bool ActorIntersectsPatientBed,
        bool ActorAabbIntersectsPatientBed,
        int ActorTrianglesIntersectingPatientBed,
        int ActorTriangleCount,
        int PatientBedCollisionTriangles,
        IReadOnlyList<string> PenetratingSurfaces,
        OpeningCameraVisualAcceptance Camera,
        OpeningVisualVector CigaretteTipWorld);

    private sealed record OpeningCameraVisualAcceptance(
        string FormId,
        string EditorId,
        string LogicalPath,
        string Sha256,
        float SampleTimeSeconds,
        OpeningVisualVector ExpectedLocalPosition,
        OpeningVisualVector ActualLocalPosition,
        float PositionErrorMeters,
        float BasisError);

    private sealed record OpeningCigaretteVisualAcceptance(
        string FormId,
        string AttachmentNode,
        bool RigidShapeTransformBaked,
        OpeningVisualVector TipLocal,
        OpeningVisualVector TipWorld,
        OpeningVisualBounds WorldBounds,
        OpeningScreenProjection Projection,
        int DifferentialPixels);

    private sealed record OpeningSmokeVisualAcceptance(
        string Authority,
        int ActivePuffCount,
        float LifetimeSeconds,
        OpeningVisualVector EmitterWorld,
        OpeningVisualBounds WorldBounds,
        OpeningScreenProjection Projection,
        int DifferentialPixels);

    private sealed record OpeningCreatorVisualAcceptance(
        int Width,
        int Height,
        int NonTransparentPixels,
        int NonBlackPixels,
        float MeanNonBlackLuminance,
        float MaximumAppliedVertexDeltaMeters,
        int EditedDifferentialPixels,
        int NativeControlCount,
        int BoundPreviewControlCount,
        string ActiveList,
        int ActiveEntryCount,
        int VisibleEntryCount,
        int ExpectedActiveEntryCount,
        OpeningVisualVector2 ReferenceCanvasScale,
        OpeningVisualVector2 ReferenceCanvasPosition,
        OpeningVisualRect SourceFaceGrabRect,
        OpeningVisualRect RuntimeFaceGrabRect,
        string FramingDisposition,
        string LightingDisposition);

    private sealed record OpeningSourceClosureAcceptance(
        string Schema,
        string Status,
        bool PlayableClaimReady,
        string OwnedEntryVideoPath,
        string OwnedEntryVideoSha256,
        IReadOnlyList<string> AdmittedBeatOrder,
        int SceneParticipants,
        int AccountedSceneParticipants,
        int LoadedCellReferences,
        int SourceCommands,
        int AccountedCommands,
        int DialogueInfos,
        int AccountedDialogueInfos,
        int DialogueResponses,
        int AccountedDialogueResponses,
        int UiMenus,
        int GuidePackages,
        int GuideAnimationObjects,
        int GuideActorSurfaces,
        int AnimationObjectSurfaces,
        int ImageSpaceModifiers,
        string SmokePresentationAuthority,
        int NativeCreatorControls,
        int BoundPreviewCreatorControls,
        int SavedCreatorControls,
        IReadOnlyList<string> Omitted,
        IReadOnlyList<string> Unsupported,
        IReadOnlyList<string> Unaccounted);

    private sealed record OpeningScreenProjection(bool InFrame, Rect2 Rect);
    private sealed record OpeningVisualBounds(
        OpeningVisualVector Position,
        OpeningVisualVector Size);
    private sealed record OpeningVisualVector(float X, float Y, float Z);
    private sealed record OpeningVisualVector2(float X, float Y);
    private sealed record OpeningVisualRect(
        OpeningVisualVector2 Position,
        OpeningVisualVector2 Size);
    private sealed record OpeningVisualColor(float R, float G, float B, float A);
}
