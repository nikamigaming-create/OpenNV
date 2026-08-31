using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed partial class ClassicCustomAppearanceProofHost : Node
{
    private static readonly AppearanceCase[] Cases =
    [
        new(
            "male",
            "Male",
            new Fo1CustomAppearanceSelection(
                "angular", "cropped", "medium", "auburn", "blue")),
        new(
            "female",
            "Female",
            new Fo1CustomAppearanceSelection(
                "round", "long", "deep", "black", "green")),
    ];

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        try
        {
            var options = ClassicProofOptions.Parse(
                OS.GetCmdlineUserArgs(),
                "classic custom appearance proof");
            var output = Path.GetFullPath(options.Required(
                "classic-custom-appearance-proof"));
            var donor = Fo2HumanoidDonorContract.Load(options.Required(
                "classic-humanoid-donor-preview-set"));
            var donors = PlayerDonors(donor);
            var reflectron = OpeningManifest.Load(
                options.Required("character-reflectron-opening-manifest"),
                RuntimeConfiguration.Load());
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite classic custom appearance proof: {output}");
            Directory.CreateDirectory(output);
            var captures = new List<object>();
            foreach (var row in Cases)
            {
                var editor = new Fo1CustomAppearanceEditor(
                    row.Sex,
                    donors,
                    reflectron,
                    row.Selection)
                {
                    Name = $"FO1_CUSTOM_{row.Id.ToUpperInvariant()}_APPEARANCE_PROOF",
                };
                AddChild(editor);
                for (var frame = 0; frame < 16; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!editor.GreenPortraitReady)
                    throw new InvalidOperationException(
                        $"Fallout 1 custom {row.Id} green portrait donor did not load.");
                RenderingServer.ForceSync();
                var image = GetViewport().GetTexture().GetImage();
                if (image.IsEmpty() || image.GetWidth() != 640 || image.GetHeight() != 480)
                    throw new InvalidOperationException(
                        $"Fallout 1 custom {row.Id} appearance capture is empty.");
                var fileName = $"fallout1-custom-{row.Id}-appearance-settings.png";
                var filePath = Path.Combine(output, fileName);
                var error = image.SavePng(filePath);
                if (error != Error.Ok)
                    throw new InvalidOperationException(
                        $"Fallout 1 custom {row.Id} appearance capture failed: {error}");
                var portraitFileName = $"fallout1-custom-{row.Id}-green-portrait.png";
                var portraitPath = Path.Combine(output, portraitFileName);
                var portraitError = editor.CapturePortrait().SavePng(portraitPath);
                if (portraitError != Error.Ok)
                    throw new InvalidOperationException(
                        $"Fallout 1 custom {row.Id} green portrait capture failed: {portraitError}");
                captures.Add(new
                {
                    row.Id,
                    row.Sex,
                    file = fileName,
                    sha256 = Sha256(File.ReadAllBytes(filePath)),
                    row.Selection.FaceShapeId,
                    row.Selection.HairStyleId,
                    row.Selection.SkinToneId,
                    row.Selection.HairColorId,
                    row.Selection.EyeColorId,
                    portraitFile = portraitFileName,
                    portraitSha256 = Sha256(File.ReadAllBytes(portraitPath)),
                    sourceActorFormId = editor.PortraitSourceActorFormId,
                    generatedLocally = true,
                    source3DUnified = true,
                    displayedAsCloseGreenWireframeProjection = true,
                });
                RemoveChild(editor);
                editor.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var reportPath = Path.Combine(output, "classic-custom-appearance-proof.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema = "opennv-classic-custom-appearance-runtime-proof/v1",
                        status = "pass-fo1-male-female-owned-donor-green-wireframe-portrait-settings",
                        captures,
                        boundary =
                            "Fallout 1 custom portraits are close green wireframe projections of owned-data donors; original premade portraits remain unchanged",
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            GD.Print(
                $"OPENNV_CLASSIC_CUSTOM_APPEARANCE_PROOF_PASS captures={captures.Count} report={reportPath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_CLASSIC_CUSTOM_APPEARANCE_PROOF_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyDictionary<string, Fo1HexSceneLoader.PlayerPresentationSource>
        PlayerDonors(Fo2HumanoidDonorContract contract)
    {
        var unitsToMeters = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        return new[] { "Male", "Female" }.ToDictionary(
            sex => sex,
            sex =>
            {
                var variant = contract.ForSex(sex);
                return new Fo1HexSceneLoader.PlayerPresentationSource(
                    sex,
                    "classic-fnv-custom-full-body-donor",
                    $"FNV custom full-body {sex} donor",
                    variant.ModelPath,
                    variant.SidecarPath,
                    variant.ModelSha256,
                    variant.SidecarSha256,
                    variant.SourceActorFormId,
                    variant.Sex.Equals("female", StringComparison.Ordinal),
                    unitsToMeters,
                    variant.Surfaces,
                    variant.Textures,
                    variant.Animations,
                    variant.BodyProfile);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct AppearanceCase(
        string Id,
        string Sex,
        Fo1CustomAppearanceSelection Selection);
}
