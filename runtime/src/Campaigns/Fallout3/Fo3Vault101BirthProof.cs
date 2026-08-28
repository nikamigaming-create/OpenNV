using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3Vault101BirthProof : Node3D
{
    private const int WarmupFrames = 8;
    private const double MinimumLuminanceDeviation = 0.005;
    private const int MinimumNonBackgroundPixels = 1000;
    private const float BackgroundPixelDeltaSquared = 16.0f;

    public override async void _Ready()
    {
        try
        {
            var profilePath = RequiredOption("--fo3-profile");
            var presentationPath = RequiredOption("--fo3-birth-presentation");
            var output = Path.GetFullPath(RequiredOption("--fo3-birth-capture"));
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 3 Vault 101 proof: {output}");
            Directory.CreateDirectory(output);
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 Vault 101 render proof requires a rendering display driver.");

            var profile = Fo3OwnedProfile.Load(profilePath);
            var contract = Fo3Vault101BirthPresentationContract.Load(
                profile.BirthSlice,
                presentationPath);
            var coverage = Fo3Vault101BirthScene.Build(this, contract);
            for (var frame = 0; frame < WarmupFrames; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var framePath = Path.Combine(output, "vault101-birth-entry.png");
            var image = GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var metrics = Analyze(image, contract.ProofBackgroundColor);
            var saveError = image.SavePng(framePath);
            if (saveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 3 Vault 101 render frame: {saveError}");
            using var frameStream = File.OpenRead(framePath);
            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameStream)).ToLowerInvariant();
            var failure = metrics.LuminanceDeviation < MinimumLuminanceDeviation
                ? "luminance-deviation"
                : metrics.NonBackgroundPixels < MinimumNonBackgroundPixels
                    ? "owned-geometry-not-visible"
                    : null;
            var report = new
            {
                schema = "opennv-fo3-vault101-birth-native-render-proof/v1",
                status = failure is null
                    ? "pass-rendered-owned-textured-birth-room-no-actors-scripts-or-gameplay"
                    : "fail-rendered-owned-birth-room",
                campaign = "Fallout3",
                slice = "Vault101BirthRoom",
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                displayDriver = DisplayServer.GetName(),
                source = new
                {
                    profileId = profile.ProfileId,
                    profileSha256 = profile.Sha256,
                    birthSlice = profile.BirthSlice.Path,
                    birthSliceSha256 = profile.BirthSlice.Sha256,
                    presentationManifest = contract.ManifestPath,
                    presentationManifestSha256 = contract.ManifestSha256,
                    recipeId = contract.RecipeId,
                    recipeSha256 = contract.RecipeSha256,
                },
                cell = new
                {
                    formId = contract.CellFormId,
                    editorId = contract.CellEditorId,
                    sourceReferences = profile.BirthSlice.ReferenceCount,
                    loadedStaticReferences = coverage.PlacedReferences,
                    loadedUniqueModels = coverage.LoadedAssets,
                },
                entry = new
                {
                    authority = "exact owned CG00PlayerStartMarker transform",
                    referenceFormId = contract.EntryReferenceFormId,
                    positionGameUnits = Vector(contract.EntryPositionGameUnits),
                    rotationRadians = Vector(contract.EntryRotationRadians),
                    positionGodotMeters = Vector(coverage.Camera.GlobalPosition),
                    cameraProjection = "recipe-proof-only-not-retail-parity",
                    verticalFovDegrees = coverage.Camera.Fov,
                },
                geometry = new
                {
                    meshInstances = coverage.MeshInstances,
                    surfaces = coverage.Surfaces,
                    vertices = coverage.Vertices,
                    triangles = coverage.Triangles,
                    materialAuthority =
                        "owned NIF surface identities plus exact owned DDS bindings",
                    collisionConsumed = false,
                },
                materials = new
                {
                    authoredTextureBindingRequests =
                        contract.AuthoredTextureBindingRequests,
                    resolvedUniqueTextures = coverage.LoadedTextures,
                    materialBindings = coverage.MaterialBindings,
                    proofLitRetailMaterials = coverage.ProofLitRetailMaterials,
                    authoredDdsTextures = coverage.AuthoredDdsTextures,
                    authoredDdsMipChainTextures = coverage.AuthoredDdsMipChainTextures,
                    decodedAuthoredBc1AlphaMipChainTextures =
                        coverage.DecodedAuthoredBc1AlphaMipChainTextures,
                    runtimeGeneratedMipTextures = coverage.RuntimeGeneratedMipTextures,
                    unresolvedUniqueTextures = 0,
                    texturesBound = coverage.LoadedTextures == contract.ResolvedUniqueTextures &&
                        coverage.MaterialBindings > 0,
                    lightingAuthority = "recipe proof only; retail CELL lighting remains absent",
                },
                frame = new
                {
                    path = framePath,
                    bytes = frameStream.Length,
                    sha256 = frameSha256,
                    width = metrics.Width,
                    height = metrics.Height,
                    meanLuminance = metrics.MeanLuminance,
                    luminanceDeviation = metrics.LuminanceDeviation,
                    nonBackgroundPixels = metrics.NonBackgroundPixels,
                    visualGatePassed = failure is null,
                    visualGateFailure = failure,
                },
                promotion = new
                {
                    transported = true,
                    runtimeManifestValidated = true,
                    runtimeSceneConstructed = true,
                    rendered = failure is null,
                    interactive = false,
                    actorsRendered = false,
                    questCommandsExecuted = false,
                    characterSelectionJoinedToScene = false,
                    collisionConsumed = false,
                    texturesBound = failure is null &&
                        coverage.LoadedTextures == contract.ResolvedUniqueTextures &&
                        coverage.MaterialBindings > 0,
                    retailParityReviewed = false,
                    headsetAccepted = false,
                    launcherPlayable = false,
                },
                unsupported = new[]
                {
                    "Dad, Doctor Li, Mom, player body, and all other actors",
                    "CG00 dialogue, packages, animation, quest triggers, and stage progression",
                    "CELL lighting, image-space effects, collision, interaction, audio, save, and OpenXR",
                    "retail camera, material, lighting, animation, and pixel parity",
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                Path.Combine(output, "vault101-birth-native-render-proof.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            if (failure is null)
                GD.Print(
                    $"OPENNV_FO3_VAULT101_RENDER_PASS cell={contract.CellFormId} " +
                    $"entry={contract.EntryReferenceFormId} references={coverage.PlacedReferences} " +
                    $"models={coverage.LoadedAssets} surfaces={coverage.Surfaces} " +
                    $"textures={coverage.LoadedTextures} materials={coverage.MaterialBindings} " +
                    $"actors=0 interactive=0 output={output}");
            else
                GD.PushError(
                    $"OPENNV_FO3_VAULT101_RENDER_VISUAL_FAIL failure={failure} output={output}");
            GetTree().Quit(failure is null ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_VAULT101_RENDER_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static FrameMetrics Analyze(Image image, Color backgroundColor)
    {
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        if (pixels <= 0 || data.Length != pixels * 4)
            throw new InvalidOperationException("Fallout 3 Vault 101 viewport is empty.");
        var background = new Vector3(
            backgroundColor.R * byte.MaxValue,
            backgroundColor.G * byte.MaxValue,
            backgroundColor.B * byte.MaxValue);
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var nonBackgroundPixels = 0;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var value = (0.2126 * data[offset] + 0.7152 * data[offset + 1] +
                0.0722 * data[offset + 2]) / byte.MaxValue;
            luminance += value;
            luminanceSquared += value * value;
            var delta = new Vector3(data[offset], data[offset + 1], data[offset + 2]) -
                background;
            if (delta.LengthSquared() > BackgroundPixelDeltaSquared)
                nonBackgroundPixels++;
        }
        var mean = luminance / pixels;
        return new FrameMetrics(
            image.GetWidth(),
            image.GetHeight(),
            mean,
            Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean)),
            nonBackgroundPixels);
    }

    private static string RequiredOption(string option)
    {
        var arguments = OS.GetCmdlineUserArgs();
        var matches = Enumerable.Range(0, arguments.Length - 1)
            .Where(index => arguments[index] == option)
            .Select(index => arguments[index + 1])
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0]))
            throw new InvalidOperationException($"Required Fallout 3 option is absent: {option}");
        return matches[0];
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private readonly record struct FrameMetrics(
        int Width,
        int Height,
        double MeanLuminance,
        double LuminanceDeviation,
        int NonBackgroundPixels);
}
