using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;


using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed partial class ClassicPremadeAnalogProofHost : Node
{
    private static readonly AnalogIdentity[] Roster =
    [
        new("fallout1", "max-stone", "Max Stone", "Male"),
        new("fallout1", "natalia", "Natalia", "Female"),
        new("fallout1", "albert", "Albert Cole", "Male"),
        new("fallout2", "combat", "Narg", "Male"),
        new("fallout2", "stealth", "Mingan", "Male"),
        new("fallout2", "diplomat", "Chitsa", "Female"),
    ];

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        try
        {
            var options = ClassicProofOptions.Parse(
                OS.GetCmdlineUserArgs(),
                "classic premade analog proof");
            var output = Path.GetFullPath(options.Required(
                "classic-premade-analog-proof"));
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite classic premade analog proof: {output}");
            var contract = Fo2HumanoidDonorContract.Load(options.Required(
                "classic-humanoid-donor-preview-set"));
            if (!contract.HasPremadeAnalogs)
                throw new InvalidOperationException(
                    "Classic premade analog proof requires all six exact character bindings.");
            Directory.CreateDirectory(output);
            var configuration = RuntimeConfiguration.Load();
            var captures = new List<object>();
            foreach (var row in Roster)
            {
                var identity = new Fo2HumanoidIdentity(
                    row.Campaign,
                    row.CharacterId,
                    row.Name,
                    "classic-premade-analog-proof",
                    row.Sex,
                    new string('0', 64),
                    new string('0', 64),
                    "proof",
                    new string('0', 64),
                    null);
                var variant = contract.ForIdentity(identity);
                var viewport = BuildViewport();
                AddChild(viewport);
                var previewRoot = new Node3D { Name = "ClassicPremadeAnalogRoot" };
                viewport.AddChild(previewRoot);
                var visual = new Fo2HumanoidVisual(identity, contract)
                {
                    Name = $"ClassicPremadeAnalog_{row.Campaign}_{row.CharacterId}",
                };
                previewRoot.AddChild(visual);
                for (var frame = 0; frame < 4; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!visual.UsesOwnedDonor)
                    throw new InvalidOperationException(
                        $"Classic premade analog failed to load: {row.Campaign}:{row.CharacterId}");
                visual.SetDirection(3);
                var litMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
                    visual,
                    new Color(0.46f, 0.42f, 0.36f, 1.0f),
                    new Color(0.015f, 0.012f, 0.008f, 1.0f),
                    0.0f,
                    100000.0f,
                    1.0f,
                    configuration.World.GameUnitsToMeters);
                if (litMaterials <= 0)
                    throw new InvalidOperationException(
                        $"Classic premade analog has no lit materials: {row.Campaign}:{row.CharacterId}");
                var camera = Frame(viewport, visual.PresentationBounds);
                for (var frame = 0; frame < 10; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                RenderingServer.ForceSync();
                var image = viewport.GetTexture().GetImage();
                if (image.IsEmpty() || image.GetWidth() != 720 || image.GetHeight() != 960)
                    throw new InvalidOperationException(
                        $"Classic premade analog capture is empty: {row.Campaign}:{row.CharacterId}");
                var meanLuminance = MeanLuminance(image);
                if (meanLuminance < 0.025f)
                    throw new InvalidOperationException(
                        $"Classic premade analog capture is too dark: {row.Campaign}:{row.CharacterId}");
                var fileName = $"{row.Campaign}-{row.CharacterId}-3d.png";
                var filePath = Path.Combine(output, fileName);
                var saveError = image.SavePng(filePath);
                if (saveError != Error.Ok)
                    throw new InvalidOperationException(
                        $"Classic premade analog frame failed to save: {saveError}");
                var bounds = visual.PresentationBounds;
                captures.Add(new
                {
                    row.Campaign,
                    row.CharacterId,
                    row.Name,
                    row.Sex,
                    file = fileName,
                    sha256 = Sha256(File.ReadAllBytes(filePath)),
                    meanLuminance,
                    sourceActorFormId = variant.SourceActorFormId,
                    variant.OutfitFormId,
                    bodyProfile = variant.BodyProfile?.Id,
                    appearance = variant.DefaultAppearance,
                    visual.Proportions,
                    bounds = new
                    {
                        position = Vector(bounds.Position),
                        size = Vector(bounds.Size),
                    },
                    camera = new
                    {
                        projection = "orthogonal",
                        size = camera.Size,
                        target = Vector(bounds.GetCenter()),
                        centered = true,
                        framingMargin = 1.12f,
                    },
                    visual.EquipmentSocketResolved,
                    visual.ActiveAnimationLogicalPath,
                    visual.AppliedFaceGeometryControlCount,
                    visualParity = false,
                });
                RemoveChild(viewport);
                viewport.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var reportPath = Path.Combine(output, "classic-premade-analog-proof.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema = "opennv-classic-premade-analog-runtime-proof/v1",
                        status = "pass-six-exact-owned-analog-bindings-centered",
                        donorManifest = contract.ManifestPath,
                        donorManifestSha256 = contract.ManifestSha256,
                        captures,
                        centered = true,
                        exactCharacterBindings = true,
                        exactOutfitBindings = true,
                        sourcePortraitsModified = false,
                        visualParity = false,
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            GD.Print(
                $"OPENNV_CLASSIC_PREMADE_ANALOG_PROOF_PASS captures={captures.Count} report={reportPath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_CLASSIC_PREMADE_ANALOG_PROOF_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private static SubViewport BuildViewport()
    {
        var viewport = new SubViewport
        {
            Name = "ClassicPremadeAnalogViewport",
            Size = new Vector2I(720, 960),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("07100b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.74f,
            },
        });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-26.0f, -30.0f, 0.0f),
            LightEnergy = 1.15f,
            ShadowEnabled = false,
        });
        return viewport;
    }

    private static Camera3D Frame(SubViewport viewport, Aabb bounds)
    {
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || bounds.Size.Y <= 0.0f)
            throw new InvalidOperationException("Classic premade analog framing bounds are invalid.");
        var target = bounds.GetCenter();
        var aspect = (float)viewport.Size.X / viewport.Size.Y;
        var size = MathF.Max(bounds.Size.Y, bounds.Size.X / aspect) * 1.12f;
        var camera = new Camera3D
        {
            Name = "ClassicPremadeAnalogCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = size,
            Near = 0.02f,
            Far = 50.0f,
            Current = true,
        };
        viewport.AddChild(camera);
        camera.Position = target + Vector3.Back * MathF.Max(4.0f, bounds.Size.Z * 2.5f);
        camera.LookAt(target, Vector3.Up);
        return camera;
    }

    private static float MeanLuminance(Image image)
    {
        double total = 0.0;
        var samples = 0;
        for (var y = 0; y < image.GetHeight(); y += 4)
            for (var x = 0; x < image.GetWidth(); x += 4)
            {
                var color = image.GetPixel(x, y);
                total += color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
                samples++;
            }
        return (float)(total / Math.Max(samples, 1));
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private readonly record struct AnalogIdentity(
        string Campaign,
        string CharacterId,
        string Name,
        string Sex);
}
