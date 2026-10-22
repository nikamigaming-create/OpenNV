using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Explicit user-requested diagnostic stills. Only camera framing and the
    // isolated source clock are adjusted; all entities use the shared builders.
    private async void RunNativePostScreenshots(string manifestPath)
    {
        var rows = new List<Dictionary<string, object?>>();
        string? output = null;
        var noFocusRequired = _options.ContainsKey("post-screenshots-no-focus");
        var window = GetWindow();
        var tree = GetTree();
        var focusViolation = false;
        long focusObservations = 0;
        void RejectFocus()
        {
            focusViolation = true;
            GD.PushError("OPENNV_POST_SCREENSHOT_FOCUS_REFUSED");
            tree.Quit(1);
        }
        void ObserveFocus()
        {
            ++focusObservations;
            if (!window.Unfocusable || window.HasFocus() ||
                !DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.NoFocus) ||
                window.Mode != Window.ModeEnum.Windowed || Input.MouseMode != Input.MouseModeEnum.Visible)
                RejectFocus();
        }
        void RequireNoFocus()
        {
            if (!noFocusRequired) return;
            ObserveFocus();
            if (focusViolation) throw new InvalidOperationException("Native capture requires an unfocused, no-focus, windowed surface and uncaptured mouse.");
        }
        try
        {
            var assemblyOnly = _options.ContainsKey("post-screenshots-assembly-only");
            var headless = DisplayServer.GetName() == "headless";
            if ((assemblyOnly && !headless) || (headless && !assemblyOnly &&
                !_options.ContainsKey("post-screenshot-camera-contracts")))
                throw new InvalidOperationException("Headless post-screenshot requests require explicit --post-screenshots-assembly-only; assembly-only mode requires headless rendering.");
            if (noFocusRequired)
            {
                if (headless || assemblyOnly) throw new InvalidOperationException("No-focus rendered capture requires a native display.");
                RequireNoFocus();
                window.FocusEntered += RejectFocus;
                tree.ProcessFrame += ObserveFocus;
            }
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Screenshots need an owned installation.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (manifest.RootElement.GetProperty("schema").GetString() != "opennv-post-screenshots/v1")
                throw new InvalidDataException("Unknown native screenshot manifest.");
            var shots = manifest.RootElement.GetProperty("shots").EnumerateArray().ToArray();
            if (shots.Length == 0) throw new InvalidDataException("Screenshot manifest has no requested views.");
            var ids = shots.Select(shot => shot.GetProperty("id").GetString() ?? "").ToArray();
            if (ids.Any(id => id.Length == 0 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) ||
                ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
                throw new InvalidDataException("Screenshot identities must be unique filename-safe names.");
            output = Path.GetFullPath(RequireOption(_options, "post-screenshots-output"));
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Screenshot output must be fresh.");
            var install = Path.GetFullPath(Path.GetDirectoryName(content.ContentRoot)!) + Path.DirectorySeparatorChar;
            if ((output + Path.DirectorySeparatorChar).StartsWith(install, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Screenshot output cannot be inside the owned installation.");
            var savePath = Path.GetFullPath(RequireOption(_options, "save-path"));
            if (File.Exists(savePath) || !string.Equals(Path.GetDirectoryName(savePath), output, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Diagnostic screenshots require an unused save path inside their fresh output directory.");
            Directory.CreateDirectory(output);
            GetWindow().Size = new Vector2I(1920, 1080);
            if (_options.ContainsKey("post-screenshot-camera-contracts"))
            {
                await RunPostScreenshotCameraContracts(output);
                return;
            }
            await Task.Run(() => IndexNativeLiveStack(content.PluginSources));
            _nativeGameTime!.InitializeNewGame();
            DismissLoadingScreen();
            var sourceFiles = content.PluginSources.Select(source => new
            {
                source.Name,
                source.RegisteredBytes,
                sha256 = PostScreenshotFileHash(source.AbsolutePath),
            }).ToArray();
            var ordinaryProjection = FalloutCameraProjection.Read(FalloutInstallationSettings.Read(content));
            for (var index = 0; index < shots.Length; ++index)
            {
                var shot = shots[index];
                var row = new Dictionary<string, object?>
                {
                    ["id"] = ids[index],
                    ["label"] = shot.GetProperty("label").GetString(),
                    ["requestedCell"] = shot.GetProperty("cell").GetString(),
                    ["anchorReference"] = shot.GetProperty("anchorReference").GetString(),
                    ["status"] = "pending",
                    ["ordinaryGameplay"] = false,
                    ["parity"] = false,
                    ["performanceEvidence"] = false,
                };
                rows.Add(row);
                Node3D? root = null;
                try
                {
                    var cellKey = ParseGalleryForm(shot.GetProperty("cell").GetString()!);
                    var anchorKey = ParseGalleryForm(shot.GetProperty("anchorReference").GetString()!);
                    var cellRecord = _nativePluginStack!.GetEffective(cellKey);
                    var scene = FalloutCellSceneReader.Read(_nativePluginStack, cellKey);
                    var anchorRecord = _nativePluginStack.GetEffective(anchorKey);
                    if (anchorRecord.IsDeleted || anchorRecord.Signature is not ("REFR" or "ACHR" or "ACRE"))
                        throw new InvalidDataException("Screenshot anchor is not a winning placed source reference.");
                    var anchorScene = scene;
                    var anchor = scene.References.SingleOrDefault(reference => reference.FormKey == anchorKey);
                    if (anchor is null && (scene.Cell.Flags & 1) == 0)
                    {
                        anchorScene = FalloutCellSceneReader.Read(_nativePluginStack,
                            FalloutCellSceneReader.ParentCell(anchorRecord) ?? throw new InvalidDataException("Screenshot anchor has no source CELL."));
                        if (anchorScene.Cell.Worldspace != scene.Cell.Worldspace)
                            throw new InvalidDataException("Persistent screenshot anchor belongs to another source worldspace.");
                        anchor = anchorScene.References.SingleOrDefault(reference => reference.FormKey == anchorKey);
                    }
                    if (anchor is null) throw new InvalidDataException("Screenshot anchor is absent from its source CELL/worldspace.");
                    var position = new Vector3(anchor.Position[0], anchor.Position[1], anchor.Position[2]);
                    var rotation = new Vector3(anchor.RotationRadians[0], anchor.RotationRadians[1], anchor.RotationRadians[2]);
                    if (shot.TryGetProperty("useIncomingTeleport", out var incoming) && incoming.GetBoolean())
                    {
                        var outward = FalloutCellSceneReader.ReadTeleport(anchorRecord) ??
                            throw new InvalidDataException("Screenshot entrance anchor has no source XTEL.");
                        var reciprocal = _nativePluginStack.GetEffective(outward.Door);
                        var entry = FalloutCellSceneReader.ReadTeleport(reciprocal) ??
                            throw new InvalidDataException("Screenshot entrance has no incoming source XTEL.");
                        if (entry.Door != anchorKey || entry.Flags != 0)
                            throw new NotSupportedException("Screenshot entrance has no plain reciprocal incoming source placement.");
                        position = new(entry.Position[0], entry.Position[1], entry.Position[2]);
                        rotation = new(entry.RotationRadians[0], entry.RotationRadians[1], entry.RotationRadians[2]);
                        row["incomingDoor"] = reciprocal.FormKey.ToString();
                        row["incomingDoorSha256"] = FalloutActorRecordHash(reciprocal);
                    }
                    var placement = shot.TryGetProperty("cameraPlacement", out var placementValue) ? placementValue.GetString() : "exact-reference";
                    if (placement is not ("exact-reference" or "grounded-player")) throw new InvalidDataException("Unknown screenshot camera placement.");
                    var grounded = placement == "grounded-player";
                    var offset = shot.TryGetProperty("cameraOffsetGameUnits", out var offsetValue) ? PostScreenshotVector(offsetValue) : Vector3.Zero;
                    var sourcePosition = position + offset;
                    var yaw = shot.GetProperty("yawDegrees").GetSingle();
                    var pitch = shot.GetProperty("pitchDegrees").GetSingle();
                    var hour = shot.GetProperty("gameHour").GetSingle();
                    var fov = shot.TryGetProperty("fov", out var fovValue) ? fovValue.GetSingle() : ordinaryProjection.VerticalFovDegrees;
                    var near = grounded ? ordinaryProjection.NearGameUnits * _configuration.World.GameUnitsToMeters :
                        shot.TryGetProperty("nearMeters", out var nearValue) ? nearValue.GetSingle() : _configuration.Player.CameraNearMeters;
                    if (!sourcePosition.IsFinite() || !rotation.IsFinite() || !float.IsFinite(yaw) || !float.IsFinite(pitch) ||
                        !float.IsFinite(hour) || hour is < 0 or >= 24 || !float.IsFinite(fov) || fov is <= 0 or >= 180 || !float.IsFinite(near) || near <= 0)
                        throw new InvalidDataException("Screenshot camera/time contains invalid finite source values.");
                    var bodyBasis = GamebryoCoordinate.ConvertReferenceEuler(new Vector3(0, 0, rotation.Z), 1) * new Basis(Vector3.Up, Mathf.DegToRad(yaw));
                    var eyeOffset = bodyBasis * (Vector3.Up * _configuration.Player.SpawnCenterHeightMeters +
                        new Vector3(_configuration.Player.DesktopCameraOffsetMeters[0], _configuration.Player.DesktopCameraOffsetMeters[1], _configuration.Player.DesktopCameraOffsetMeters[2]));
                    var intendedFoot = grounded ? GamebryoCoordinate.ConvertVector(PostScreenshotVector(shot.GetProperty("footPositionGameUnits"))) *
                        _configuration.World.GameUnitsToMeters : GamebryoCoordinate.ConvertVector(sourcePosition) * _configuration.World.GameUnitsToMeters - eyeOffset;
                    row["cameraPlacement"] = placement;
                    row["intendedFootSourcePosition"] = PostScreenshotCoordinates(new Vector3(intendedFoot.X, -intendedFoot.Z, intendedFoot.Y) / _configuration.World.GameUnitsToMeters);
                    row["requestedReferenceCameraSourcePosition"] = PostScreenshotCoordinates(sourcePosition);
                    if (grounded)
                    {
                        var intendedEye = intendedFoot + eyeOffset;
                        sourcePosition = new Vector3(intendedEye.X, -intendedEye.Z, intendedEye.Y) / _configuration.World.GameUnitsToMeters;
                    }
                    _nativeGameTime.SetHour(hour); _nativeGameTime.AdvanceSimulation(0);
                    FalloutExteriorGridScene? exterior = null;
                    if ((scene.Cell.Flags & 1) == 0)
                        exterior = ResolveExterior(scene.Cell.Worldspace ?? throw new InvalidDataException("Exterior CELL has no worldspace."),
                            [sourcePosition.X, sourcePosition.Y, sourcePosition.Z]);
                    else if (scene.Cell.Lighting is null)
                        throw new NotSupportedException("Interior CELL has no source lighting declaration.");
                    if (exterior is not null) scene = exterior.Scene;
                    _nativeActorDivergences.Clear(); _nativeReferenceDivergences.Clear();
                    _nativeSkyLighting!.EnterCell(scene.Cell, _nativeGlobals, [sourcePosition.X, sourcePosition.Y, sourcePosition.Z]);
                    root = BuildNativeCellRoot(scene, null, sourceSide: true);
                    if (exterior is not null) AddExteriorLandscape(root, exterior);
                    AddChild(root);
                    if (exterior is null) AddNativeCellEnvironment(root, scene); else AddExteriorEnvironment(root, scene.Cell);
                    // Construction can span many native frames. Settle the requested
                    // isolated clock immediately before binding ordinary source events.
                    _nativeGameTime.SetHour(hour); _nativeGameTime.AdvanceSimulation(0);
                    SetNativeActiveCell(root, scene);
                    var eventDivergences = new List<string>();
                    _nativeReferenceEvents!.ReportDivergence = message =>
                    {
                        eventDivergences.Add(message);
                        GD.PushError(message);
                    };
                    row["referenceEventClockHourAtBinding"] = _nativeGameTime.Hour;
                    row["referenceEventHost"] = "shared-native-reference-events-without-gameplay-player";
                    row["referenceEventDivergences"] = eventDivergences;
                    var camera = new Camera3D
                    {
                        Name = "RequestedDiagnosticCamera",
                        Current = true,
                        Fov = fov,
                        Near = near,
                        Far = _configuration.Player.CameraFarMeters,
                        Transform = new(GamebryoCoordinate.ConvertReferenceEuler(rotation, 1),
                            GamebryoCoordinate.ConvertVector(sourcePosition) * _configuration.World.GameUnitsToMeters),
                    };
                    root.AddChild(camera);
                    camera.RotateObjectLocal(Vector3.Up, Mathf.DegToRad(yaw));
                    camera.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(pitch));
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var cameraCheck = CheckPostScreenshotCamera(root.GetWorld3D().DirectSpaceState,
                        intendedFoot, camera.GlobalPosition, bodyBasis, grounded, camera.Fov, camera.Near,
                        GetViewport().GetVisibleRect().Size.X / GetViewport().GetVisibleRect().Size.Y);
                    row["cameraValidation"] = cameraCheck.Evidence;
                    row["cameraAccepted"] = cameraCheck.Passed;
                    row["requestedCellSha256"] = FalloutActorRecordHash(cellRecord);
                    row["anchorRecordSha256"] = FalloutActorRecordHash(anchorRecord);
                    row["anchorPosition"] = anchor.Position; row["anchorRotationRadians"] = anchor.RotationRadians;
                    row["activeCell"] = scene.Cell.FormKey.ToString();
                    row["activeCellEditorId"] = scene.Cell.EditorId;
                    row["worldspace"] = scene.Cell.Worldspace?.ToString();
                    row["requestedReferenceCameraMeters"] = PostScreenshotCoordinates(camera.GlobalPosition);
                    row["ordinaryProjection"] = new { ordinaryProjection.VerticalFovDegrees, ordinaryProjection.NearGameUnits };
                    if (!cameraCheck.Passed)
                        GD.PushWarning($"OPENNV_POST_SCREENSHOT_CAMERA_REJECTED id={ids[index]} reason={cameraCheck.Reason} accepted=false");
                    if (!cameraCheck.Passed && !(placement == "exact-reference" &&
                        shot.TryGetProperty("retainInvalidCameraDiagnostic", out var retainInvalid) && retainInvalid.GetBoolean()))
                        throw new InvalidDataException("Screenshot camera rejected: " + cameraCheck.Reason);
                    camera.GlobalPosition = cameraCheck.Camera;
                    sourcePosition = new Vector3(cameraCheck.Camera.X, -cameraCheck.Camera.Z, cameraCheck.Camera.Y) / _configuration.World.GameUnitsToMeters;
                    var previousPause = GetTree().Paused;
                    GetTree().Paused = true;
                    try
                    {
                        foreach (var lod in root.GetChildren().OfType<RuntimeNativeExteriorLod>())
                            await lod.PrepareInitialSelection(camera.GlobalPosition);
                    }
                    finally { GetTree().Paused = previousPause; }
                    for (var warm = 0; warm < 30; ++warm)
                        if (assemblyOnly) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        else await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    // Actors and streamed collision can change during warmup.
                    // Recheck the actual unchanged pose at the capture boundary.
                    row["initialCameraValidation"] = cameraCheck.Evidence;
                    var finalCameraCheck = CheckPostScreenshotCamera(root.GetWorld3D().DirectSpaceState,
                        cameraCheck.Foot, camera.GlobalPosition, bodyBasis, false, camera.Fov, camera.Near,
                        GetViewport().GetVisibleRect().Size.X / GetViewport().GetVisibleRect().Size.Y);
                    row["captureBoundaryCameraValidation"] = finalCameraCheck.Evidence;
                    if (cameraCheck.Passed) cameraCheck = finalCameraCheck;
                    row["cameraAccepted"] = cameraCheck.Passed;
                    if (!cameraCheck.Passed && !(placement == "exact-reference" &&
                        shot.TryGetProperty("retainInvalidCameraDiagnostic", out var retainInvalidFinal) && retainInvalidFinal.GetBoolean()))
                        throw new InvalidDataException("Screenshot capture-boundary camera rejected: " + cameraCheck.Reason);
                    row["referenceEvents"] = _nativeReferenceEvents!.State;
                    row["referenceScriptPresentation"] = scene.References.Select(reference => _nativeReferences!.Get(reference.FormKey))
                        .Where(instance => instance.Script is not null || instance.CaptureObjectAnimations is not null)
                        .Select(instance => new
                        {
                            reference = instance.Reference.ToString(),
                            sourceScriptSha256 = instance.Script?.Sha256,
                            enabled = _nativeReferences!.IsEnabled(instance.Reference),
                            error = instance.ScriptError,
                            objectAnimations = instance.CaptureObjectAnimations?.Invoke() ?? instance.ObjectAnimations,
                        }).ToArray();
                    var geometry = root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().Count(node => node.IsVisibleInTree());
                    if (geometry == 0) throw new InvalidDataException("Requested source CELL presents no visible native geometry.");
                    string? png = null;
                    if (assemblyOnly)
                    {
                        if (Engine.GetFramesDrawn() != 0)
                            throw new InvalidOperationException("Assembly-only mode unexpectedly rendered a frame.");
                        row["status"] = cameraCheck.Passed ? "native-assembled-diagnostic" : "native-assembled-invalid-camera-diagnostic";
                        row["image"] = null;
                        row["nativeReferences"] = _nativeReferencePresentation!.Nodes.Select(pair => new
                        {
                            reference = pair.Key.ToString(),
                            insideTree = pair.Value.IsInsideTree(),
                            enabled = _nativeReferences!.IsEnabled(pair.Key),
                            geometryNodes = pair.Value.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().Count(),
                            controllers = pair.Value.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                                .Select(player => new
                                {
                                    player.SourceController,
                                    player.SourceSha256,
                                    player.SequenceNames,
                                    player.ActiveSequence,
                                    player.SourceTimeSeconds
                                }).ToArray(),
                            portals = pair.Value.GetChildren().OfType<RuntimeNativeDoorPortal>().Select(portal => new
                            {
                                reference = portal.Reference.ToString(),
                                destination = portal.Destination.ToString(),
                                cell = portal.DestinationCell.ToString(),
                                worldspace = portal.DestinationWorldspace?.ToString(),
                                portal.ActivationRequests,
                            }).ToArray(),
                        }).ToArray();
                    }
                    else
                    {
                        RequireNoFocus();
                        png = Path.Combine(output, $"{index + 1:D2}-{ids[index]}{(cameraCheck.Passed ? "" : ".invalid-camera")}.png");
                        using var image = GetViewport().GetTexture().GetImage();
                        if (image.IsEmpty() || image.SavePng(png) != Error.Ok) throw new IOException("Native viewport PNG could not be saved.");
                        row["status"] = cameraCheck.Passed ? "native-rendered-diagnostic" : "native-rendered-invalid-camera-diagnostic";
                        row["image"] = Path.GetFileName(png); row["width"] = image.GetWidth(); row["height"] = image.GetHeight();
                        row["pngBytes"] = new FileInfo(png).Length;
                        row["pngSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(png)));
                    }
                    row["assemblyOnly"] = assemblyOnly;
                    row["windowEvidence"] = new
                    {
                        noFocusRequired,
                        focusViolation,
                        focusObservations,
                        noFocus = window.Unfocusable,
                        focused = window.HasFocus(),
                        mode = window.Mode.ToString(),
                        width = window.Size.X,
                        height = window.Size.Y,
                        mouseMode = Input.MouseMode.ToString(),
                    };
                    row["capturedUtc"] = DateTime.UtcNow; row["drawCount"] = Engine.GetFramesDrawn();
                    row["requestedCell"] = cellKey.ToString(); row["requestedCellEditorId"] = GalleryRecordText(cellRecord, "EDID");
                    row["requestedCellSha256"] = FalloutActorRecordHash(cellRecord);
                    row["activeCell"] = scene.Cell.FormKey.ToString(); row["activeCellEditorId"] = scene.Cell.EditorId;
                    row["worldspace"] = scene.Cell.Worldspace?.ToString(); row["sourceReferenceCount"] = scene.References.Count;
                    row["anchorReference"] = anchorKey.ToString(); row["anchorRecordSha256"] = FalloutActorRecordHash(anchorRecord);
                    row["anchorSourceCell"] = anchorScene.Cell.FormKey.ToString();
                    row["anchorPosition"] = anchor.Position; row["anchorRotationRadians"] = anchor.RotationRadians;
                    row["cameraOffsetGameUnits"] = new[] { offset.X, offset.Y, offset.Z };
                    row["cameraSourcePosition"] = new[] { sourcePosition.X, sourcePosition.Y, sourcePosition.Z };
                    row["cameraPositionMeters"] = new[] { camera.GlobalPosition.X, camera.GlobalPosition.Y, camera.GlobalPosition.Z };
                    row["cameraForward"] = new[] { -camera.GlobalBasis.Z.X, -camera.GlobalBasis.Z.Y, -camera.GlobalBasis.Z.Z };
                    row["yawDegrees"] = yaw; row["pitchDegrees"] = pitch; row["fov"] = camera.Fov;
                    row["nearMeters"] = camera.Near; row["farMeters"] = camera.Far;
                    row["requestedGameHour"] = hour; row["actualGameHour"] = _nativeGameTime.Hour;
                    row["skyState"] = _nativeSkyLighting.Capture(); row["visibleGeometryNodes"] = geometry;
                    row["exteriorLod"] = root.GetChildren().OfType<RuntimeNativeExteriorLod>().SingleOrDefault()?.State;
                    row["actorDivergences"] = _nativeActorDivergences.ToArray(); row["referenceDivergences"] = _nativeReferenceDivergences.ToArray();
                    row["missingRuntimeReferences"] = _parityObservations.Snapshot().Missing.ToArray();
                    GD.Print($"OPENNV_POST_SCREENSHOT_{(assemblyOnly ? "ASSEMBLED" : "CAPTURED")} id={ids[index]} cell={scene.Cell.FormKey} hour={_nativeGameTime.Hour} png={png}");
                }
                catch (Exception error)
                {
                    row["status"] = "blocked"; row["error"] = error.Message;
                    GD.PushWarning($"OPENNV_POST_SCREENSHOT_BLOCKED id={ids[index]} error={error.Message}");
                }
                finally
                {
                    if (root is not null && GodotObject.IsInstanceValid(root)) root.Free();
                    if (_nativeActiveCell is { } active && _nativeReferences!.IsCellResident(active.Cell.FormKey))
                        _nativeReferences.UnloadCell(active.Cell.FormKey);
                    _nativeCurrentCellRoot = null; _nativeActiveCell = null;
                    _nativeReferenceEvents = null; _nativeReferencePresentation = null;
                    foreach (var prototype in _nativeNifPrototypes.Values) prototype.Scene.Root.Free();
                    _nativeNifPrototypes.Clear();
                }
                WriteReport(Path.Combine(output, "capture.json"), new
                {
                    schema = "opennv-post-screenshots-result/v1",
                    source = "read-only-owned-files",
                    sourceFiles,
                    sourceCompatibilityId = content.SaveCompatibilityId,
                    sourceStackId = content.StackId,
                    runtimeBuild = typeof(RuntimeCoordinator).Assembly.ManifestModule.ModuleVersionId,
                    runtimeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/OpenNV.dll")))),
                    configurationSha256 = _configuration.Sha256,
                    manifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))),
                    renderingMethod = RenderingServer.GetCurrentRenderingMethod(),
                    graphicsAdapter = RenderingServer.GetVideoAdapterName(),
                    engineVersion = Engine.GetVersionInfo()["string"].AsString(),
                    width = GetViewport().GetVisibleRect().Size.X,
                    height = GetViewport().GetVisibleRect().Size.Y,
                    isolatedSavePath = savePath,
                    sourceSaveWritten = File.Exists(savePath),
                    recording = false,
                    assemblyOnly,
                    noFocusRequired,
                    focusViolation,
                    focusObservations,
                    imagesWritten = rows.Count(row => row.TryGetValue("image", out var image) && image is string),
                    generatedImages = false,
                    retailCaptureUsed = false,
                    boundary = assemblyOnly
                        ? "Native assembly and camera-query diagnostic only; zero rendered frames or images. No ordinary gameplay, activation, transition, complete-scene or parity claim."
                        : "Diagnostic camera/time and fresh source state; no ordinary gameplay, complete-scene, retail-parity or FPS claim.",
                    shots = rows,
                });
            }
            GetTree().Quit(rows.All(row => (string?)row["status"] ==
                (assemblyOnly ? "native-assembled-diagnostic" : "native-rendered-diagnostic")) ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_POST_SCREENSHOTS_FAILED output={output} error={error}");
            GetTree().Quit(1);
        }
        finally
        {
            if (noFocusRequired)
            {
                if (GodotObject.IsInstanceValid(window)) window.FocusEntered -= RejectFocus;
                if (GodotObject.IsInstanceValid(tree)) tree.ProcessFrame -= ObserveFocus;
            }
        }
    }

    private static Vector3 PostScreenshotVector(JsonElement value)
    {
        var values = value.EnumerateArray().Select(component => component.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(component => !float.IsFinite(component)))
            throw new InvalidDataException("Screenshot camera offset requires three finite source coordinates.");
        return new(values[0], values[1], values[2]);
    }

    private static string FalloutActorRecordHash(FalloutPluginRecord record) =>
        Convert.ToHexString(SHA256.HashData(record.ReadData()));

    private static string PostScreenshotFileHash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
