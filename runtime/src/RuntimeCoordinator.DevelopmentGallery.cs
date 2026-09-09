using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Explicit lab entry point. The shared cell/entity/AI/material owners below
    // also serve ordinary loading. No source actor transform or enable flag is
    // changed to manufacture a successful image.
    private async void RunNativeDevelopmentGallery(string manifestPath)
    {
        string? output = null;
        var rows = new List<Dictionary<string, object?>>();
        var originalMaxFps = Engine.MaxFps;
        try
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Gallery needs a live owned installation.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (manifest.RootElement.GetProperty("schema").GetString() != "opennv-development-gallery/v1")
                throw new InvalidDataException("Unknown development gallery manifest.");
            var subjects = manifest.RootElement.GetProperty("subjects").EnumerateArray().ToArray();
            if (subjects.Length == 0) throw new InvalidDataException("Gallery has no subjects.");
            var requested = subjects.Select(subject => ParseGalleryForm(subject.GetProperty("reference").GetString()!)).ToArray();
            if (requested.Distinct().Count() != requested.Length) throw new InvalidDataException("Gallery repeats a reference.");
            output = Path.GetFullPath(RequireOption(_options, "gallery-output"));
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Gallery output must be fresh.");
            var install = Path.GetDirectoryName(Path.GetFullPath(source.ContentRoot))! + Path.DirectorySeparatorChar;
            if (output.StartsWith(install, StringComparison.OrdinalIgnoreCase)) throw new IOException("Gallery output cannot be inside the owned installation.");
            var encoder = Path.GetFullPath(RequireOption(_options, "gallery-ffmpeg"));
            if (!File.Exists(encoder)) throw new FileNotFoundException("Gallery encoder is missing.", encoder);
            Directory.CreateDirectory(output);
            GetWindow().Size = new Vector2I(1920, 1080);
            Engine.MaxFps = 30;
            await Task.Run(() => IndexNativeLiveStack(source.PluginSources));
            _nativeGameTime?.InitializeNewGame();
            DismissLoadingScreen();
            for (var index = 0; index < requested.Length; index++)
            {
                var row = new Dictionary<string, object?>
                {
                    ["ordinal"] = index + 1,
                    ["reference"] = requested[index].ToString(),
                    ["requestedLabel"] = subjects[index].GetProperty("label").GetString(),
                    ["status"] = "pending",
                    ["ordinaryGameplay"] = false,
                    ["parity"] = false,
                    ["weaponDraw"] = "unbound",
                    ["equippedWeaponPresentation"] = "unbound",
                };
                rows.Add(row);
                Node3D? root = null;
                try
                {
                    var record = _nativePluginStack!.GetEffective(requested[index]);
                    if (record.Signature != "ACHR" || record.IsDeleted)
                        throw new InvalidDataException("Subject is not a winning, undeleted ACHR.");
                    var npc = _nativePluginStack.GetEffective(FalloutDialogueTopic.RequiredForm(record, "NAME"));
                    row["name"] = GalleryRecordText(npc, "FULL");
                    row["npc"] = npc.FormKey.ToString();
                    row["referenceSha256"] = Convert.ToHexString(SHA256.HashData(record.ReadData()));
                    var cellKey = FalloutCellSceneReader.ParentCell(record) ?? throw new InvalidDataException("Actor has no source CELL.");
                    row["cell"] = cellKey.ToString();
                    var cell = FalloutCellSceneReader.Read(_nativePluginStack, cellKey);
                    row["cellEditorId"] = cell.Cell.EditorId;
                    row["sourceReferenceCount"] = cell.References.Count;
                    var reference = cell.References.Single(value => value.FormKey == requested[index]);
                    row["sourcePosition"] = reference.Position;
                    row["sourceRotationRadians"] = reference.RotationRadians;
                    row["initiallyDisabled"] = FalloutCellSceneReader.IsInitiallyDisabled(reference);
                    try
                    {
                        var appearance = FalloutNpcAppearanceResolver.Resolve(_nativePluginStack, npc.FormKey, reference.FormKey);
                        row["appearanceBlockers"] = appearance.Blockers;
                        row["sourceInventory"] = appearance.Inventory.Select(item => new
                        {
                            form = item.Item.ToString(),
                            item.Signature,
                            item.Count,
                            name = GalleryRecordText(_nativePluginStack.GetEffective(item.Item), "FULL"),
                        }).ToArray();
                    }
                    catch (Exception error) when (error is IOException or NotSupportedException or InvalidOperationException)
                    {
                        row["appearanceError"] = error.Message;
                    }
                    if ((cell.Cell.Flags & 1) == 0 || cell.Cell.Lighting is null)
                        throw new NotSupportedException("Current shared exterior streaming/lighting cannot assemble this source placement. No substitute environment was used.");
                    root = BuildNativeCellRoot(cell, null, sourceSide: true);
                    AddChild(root);
                    AddNativeCellEnvironment(root, cell);
                    SetNativeActiveCell(root, cell);
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var actor = root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                        .SingleOrDefault(value => value.Appearance.Reference == reference.FormKey);
                    row["actorPresent"] = actor is not null;
                    row["actorDivergences"] = _nativeActorDivergences.ToArray();
                    row["referenceDivergences"] = _nativeReferenceDivergences.ToArray();
                    row["missingRuntimeReferences"] = _parityObservations.Snapshot().Missing.ToArray();
                    var camera = new Camera3D { Name = "DevelopmentGalleryCamera", Fov = 55, Near = 0.04f, Far = 300, Current = true };
                    root.AddChild(camera);
                    var placement = ReferenceTransform(reference);
                    var target = placement.Origin + Vector3.Up;
                    var radius = 1.2f;
                    if (actor is not null)
                    {
                        var bounds = actor.CurrentWorldBound(source);
                        target = new Vector3(bounds.Center.X, bounds.Center.Y, bounds.Center.Z);
                        radius = bounds.Radius;
                    }
                    var distance = radius / Mathf.Sin(Mathf.DegToRad(camera.Fov / 2)) * 1.4f;
                    var forward = actor?.HeadFacingDirection ?? -placement.Basis.Z;
                    forward.Y = 0;
                    forward = forward.Normalized();
                    if (forward.LengthSquared() < 0.5f) throw new InvalidDataException("Actor has no horizontal camera facing basis.");
                    var bestBlocked = int.MaxValue;
                    var bestPosition = target + forward * distance;
                    var selectedAngle = 0f;
                    // Move only the diagnostic camera. Occluders, actors and
                    // source geometry are never hidden or repositioned.
                    foreach (var degrees in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 180f })
                    {
                        var candidate = target + forward.Rotated(Vector3.Up, Mathf.DegToRad(degrees)) * distance;
                        var blocked = 0;
                        foreach (var aim in new[] { target, target + Vector3.Up * radius * 0.6f, target - Vector3.Up * radius * 0.6f })
                        {
                            var query = PhysicsRayQueryParameters3D.Create(aim, candidate);
                            if (root.GetWorld3D().DirectSpaceState.IntersectRay(query).Count != 0) blocked++;
                        }
                        if (blocked >= bestBlocked) continue;
                        bestBlocked = blocked; bestPosition = candidate; selectedAngle = degrees;
                        if (blocked == 0) break;
                    }
                    camera.GlobalPosition = bestPosition;
                    camera.LookAt(target, Vector3.Up);
                    row["camera"] = new
                    {
                        position = new[] { bestPosition.X, bestPosition.Y, bestPosition.Z },
                        target = new[] { target.X, target.Y, target.Z },
                        camera.Fov,
                        blockedRays = bestBlocked,
                        selectedAngle,
                        owner = actor is null ? "source-reference-empty-location" : "current-posed-actor-bounds"
                    };
                    row["animationBefore"] = actor?.AnimationState;
                    // Shader warmup is outside recording. Each viewport sample
                    // goes directly into one encoder; no raw frame archive.
                    for (var warm = 0; warm < 30; warm++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var stem = (index + 1).ToString("D2", CultureInfo.InvariantCulture);
                    var imagePath = Path.Combine(output, stem + ".png");
                    using (var image = GetViewport().GetTexture().GetImage())
                    {
                        if (image.SavePng(imagePath) != Error.Ok) throw new IOException("Could not save gallery preview.");
                        row["image"] = Path.GetFileName(imagePath);
                    }
                    var moviePath = Path.Combine(output, stem + ".mp4");
                    row["capture"] = await EncodeNativeGalleryClip(encoder, moviePath);
                    row["movie"] = Path.GetFileName(moviePath);
                    row["animationAfter"] = actor?.AnimationState;
                    row["status"] = actor is null ? "cell-rendered-actor-absent" : "rendered-with-unverified-presentation";
                }
                catch (Exception error)
                {
                    row["status"] = "blocked";
                    row["error"] = error.Message;
                    GD.PushWarning($"OPENNV_NATIVE_GALLERY_BLOCKED subject={requested[index]} error={error.Message}");
                }
                finally
                {
                    if (root is not null && GodotObject.IsInstanceValid(root)) root.Free();
                    if (_nativeActiveCell is { } active) _nativeReferences?.UnloadCell(active.Cell.FormKey);
                    _nativeCurrentCellRoot = null; _nativeActiveCell = null;
                    foreach (var prototype in _nativeNifPrototypes.Values) prototype.Scene.Root.Free();
                    _nativeNifPrototypes.Clear();
                }
                WriteGalleryReport();
                GD.Print($"OPENNV_NATIVE_GALLERY_PROGRESS completed={rows.Count} total={requested.Length} status={row["status"]}");
            }
            GetTree().Quit(0);

            void WriteGalleryReport() => WriteReport(Path.Combine(output, "gallery-report.json"), new
            {
                schema = "opennv-development-gallery-report/v1",
                status = rows.Count == requested.Length ? "complete-with-visible-limitations" : "running",
                requestedCount = requested.Length,
                completedCount = rows.Count,
                manifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))),
                runtimeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                    ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/OpenNV.dll")))),
                configurationSha256 = _configuration.Sha256,
                source = "live-owned-files",
                retailCaptureUsed = false,
                parity = false,
                testBoundary = "Fresh disposable source state; source enable flags retained; no campaign playthrough; camera framing is diagnostic; weapon equip/draw is unbound.",
                subjects = rows,
            });
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_NATIVE_GALLERY_FAILED {error}");
            GetTree().Quit(1);
        }
        finally { Engine.MaxFps = originalMaxFps; }
    }

    private async Task<object> EncodeNativeGalleryClip(string encoder, string path)
    {
        var info = new ProcessStartInfo(encoder)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-n", "-f", "rawvideo", "-pixel_format", "rgb24",
            "-video_size", "1920x1080", "-framerate", "30", "-i", "pipe:0", "-an", "-c:v", "libx264", "-preset", "veryfast",
            "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", path }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Gallery encoder did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        var complete = false;
        var watch = Stopwatch.StartNew();
        try
        {
            for (var frame = 0; frame < 120; frame++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() != 1920 || image.GetHeight() != 1080) throw new InvalidDataException("Gallery viewport changed dimensions.");
                image.Convert(Image.Format.Rgb8);
                process.StandardInput.BaseStream.Write(image.GetData());
            }
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new IOException("Gallery encoder failed: " + await stderr);
            complete = true;
            return new
            {
                frames = 120,
                frameRate = 30,
                encodedSeconds = 4,
                sampleWallSeconds = watch.Elapsed.TotalSeconds,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                limitation = "Consecutive rendered frames; simulation-to-encoded-clock equality is unverified; silent."
            };
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (!complete && File.Exists(path)) File.Delete(path);
        }
    }

    private static FalloutFormKey ParseGalleryForm(string value)
    {
        var split = value.LastIndexOf(':');
        if (split <= 0 || !uint.TryParse(value.AsSpan(split + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
            id > FalloutFormKey.ObjectIdMask) throw new InvalidDataException("Gallery needs plugin:local-hex reference identities.");
        return new(value[..split], id);
    }

    private static string GalleryRecordText(FalloutPluginRecord record, string signature)
    {
        var data = record.ReadSubrecords().SingleOrDefault(field => field.Signature == signature).Data;
        return data.IsEmpty ? "" : FalloutDialogueTopic.Text(data.Span);
    }
}
