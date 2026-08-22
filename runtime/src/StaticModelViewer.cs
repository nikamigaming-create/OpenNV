using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

public partial class StaticModelViewer : Node3D
{
    private const string SidecarSchema = "opennv-static-nif-gltf/v1";

    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private CanvasLayer? _onboardingLayer;
    private Label? _statusLabel;
    private Button? _selectButton;

    public override void _Ready()
    {
        Callable.From(LoadConfiguredModel).CallDeferred();
    }

    private void LoadConfiguredModel()
    {
        try
        {
            _options = ParseOptions(OS.GetCmdlineUserArgs());
            var hasDataRoot = _options.TryGetValue("data-root", out var dataRoot);
            var hasModel = _options.ContainsKey("model");
            if (hasDataRoot && hasModel)
                throw new ArgumentException("Use either --data-root or --model/--sidecar, not both.");
            if (!hasModel && _options.ContainsKey("sidecar"))
                throw new ArgumentException("--sidecar requires --model.");

            if (hasDataRoot)
            {
                var prepared = LegalAssetPreparer.Prepare(dataRoot!, _options);
                LoadModel(prepared.ModelPath, prepared.SidecarPath, _options);
                return;
            }

            if (hasModel)
            {
                LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
                return;
            }

            if (_options.TryGetValue("report", out var startupReportPath))
                WriteStartupReport(startupReportPath);
            GD.Print("OPENNV_GODOT_EXPERIMENTAL_READY playable=0");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            else
                ShowExperimentalStatus();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_STATIC_MODEL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void LoadModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var provenance = ValidateProvenance(modelPath, sidecarPath);
        var model = LoadGltfScene(modelPath);
        model.Name = "RetailStaticModel";
        AddChild(model);

        var meshes = Descendants<MeshInstance3D>(model).ToArray();
        if (meshes.Length == 0)
            throw new InvalidOperationException("Imported glTF contains no MeshInstance3D nodes.");
        var surfaces = meshes.Sum(mesh => mesh.Mesh?.GetSurfaceCount() ?? 0);
        var vertices = meshes.Sum(mesh =>
            mesh.Mesh is not ArrayMesh arrayMesh
                ? 0
                : Enumerable.Range(0, arrayMesh.GetSurfaceCount()).Sum(arrayMesh.SurfaceGetArrayLen));
        if (surfaces == 0 || vertices == 0)
            throw new InvalidOperationException("Imported glTF contains no renderable surfaces or vertices.");

        BuildReferenceView(meshes[0]);
        var report = new
        {
            schema = "opennv-godot-static-model/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            sourceSha256 = provenance.SourceSha256,
            meshes = meshes.Length,
            surfaces,
            vertices,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_MODEL_PASS source={provenance.SourceSha256} " +
            $"meshes={meshes.Length} surfaces={surfaces} vertices={vertices}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void BuildReferenceView(MeshInstance3D referenceMesh)
    {
        var bounds = referenceMesh.Mesh!.GetAabb();
        var center = bounds.GetCenter();
        var extent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), MathF.Max(bounds.Size.Z, 1.0f));

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.035f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.38f, 0.42f),
            AmbientLightEnergy = 0.65f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50.0f, -30.0f, 0.0f),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
        });
        var camera = new Camera3D
        {
            Position = center + new Vector3(extent * 1.2f, extent * 0.65f, extent * 1.8f),
            Near = MathF.Max(0.01f, extent / 10_000.0f),
            Far = MathF.Max(100.0f, extent * 20.0f),
            Current = true,
        };
        AddChild(camera);
        camera.LookAt(center, Vector3.Up);
    }

    private void ShowExperimentalStatus()
    {
        var layer = new CanvasLayer { Name = "LegalAssetSetup" };
        _onboardingLayer = layer;
        AddChild(layer);
        var background = new ColorRect
        {
            Color = new Color(0.025f, 0.045f, 0.07f),
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(background);
        var text = new VBoxContainer
        {
            Position = new Vector2(64.0f, 64.0f),
            Size = new Vector2(760.0f, 420.0f),
        };
        layer.AddChild(text);
        var title = new Label { Text = "OPEN NEVADA  /  EXPERIMENTAL GODOT RUNTIME" };
        title.AddThemeFontSizeOverride("font_size", 28);
        text.AddChild(title);
        text.AddChild(new HSeparator());
        var body = new Label
        {
            Text = "The direct legal-asset pipeline and Forward+ geometry slice are working.\n\n" +
                   "Select your legal Fallout: New Vegas Data folder to prepare and load the first\n" +
                   "retail geometry slice. Python and external engine runtimes are not required.\n\n" +
                   "No game assets are included in this build, and your installation is never modified.",
        };
        body.AddThemeFontSizeOverride("font_size", 18);
        text.AddChild(body);

        var button = new Button
        {
            Text = "Select Fallout: New Vegas Data folder",
            CustomMinimumSize = new Vector2(0.0f, 48.0f),
        };
        _selectButton = button;
        text.AddChild(button);

        var status = new Label { Text = "Waiting for a legal Data folder." };
        status.AddThemeColorOverride("font_color", new Color(0.70f, 0.80f, 0.90f));
        status.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel = status;
        text.AddChild(status);

        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            UseNativeDialog = true,
            ModeOverridesTitle = false,
            Title = "Select Fallout: New Vegas Data folder",
        };
        dialog.DirSelected += OnDataRootSelected;
        layer.AddChild(dialog);
        button.Pressed += () => dialog.PopupCenteredRatio(0.8f);
    }

    private void OnDataRootSelected(string dataRoot)
    {
        _selectButton!.Disabled = true;
        _statusLabel!.Text = "Validating the installation and preparing the local cache...";
        Callable.From(() => PrepareSelectedData(dataRoot)).CallDeferred();
    }

    private void PrepareSelectedData(string dataRoot)
    {
        try
        {
            var prepared = LegalAssetPreparer.Prepare(dataRoot, _options);
            LoadModel(prepared.ModelPath, prepared.SidecarPath, _options);
            _onboardingLayer?.QueueFree();
            _onboardingLayer = null;
        }
        catch (Exception exception)
        {
            _statusLabel!.Text = "That folder could not be prepared. Choose the Fallout: New Vegas Data folder and try again.";
            _selectButton!.Disabled = false;
            GD.PushError($"OPENNV_LEGAL_ASSET_SETUP_FAIL {exception.Message}");
        }
    }

    private static void WriteStartupReport(string reportPath)
    {
        WriteReport(reportPath, new
        {
            schema = "opennv-godot-startup/v1",
            status = "experimental",
            playable = false,
            engine = "Godot 4.7.1 Forward+",
        });
    }

    private static Provenance ValidateProvenance(string modelPath, string sidecarPath)
    {
        var sidecarFile = ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarFile));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SidecarSchema)
            throw new InvalidOperationException($"Unexpected sidecar schema: {sidecarPath}");
        if (root.GetProperty("status").GetString() != "geometry-only")
            throw new InvalidOperationException($"Static slice requires geometry-only status: {sidecarPath}");

        var modelFile = ResolvePath(modelPath);
        var gltf = root.GetProperty("outputs").GetProperty("gltf");
        VerifyHash(modelFile, gltf.GetProperty("sha256").GetString()!);
        var buffer = root.GetProperty("outputs").GetProperty("buffer");
        var bufferFile = Path.Combine(Path.GetDirectoryName(modelFile)!, buffer.GetProperty("file").GetString()!);
        VerifyHash(bufferFile, buffer.GetProperty("sha256").GetString()!);
        return new Provenance(root.GetProperty("source").GetProperty("sha256").GetString()!);
    }

    private static Node3D LoadGltfScene(string modelPath)
    {
        var modelFile = ResolvePath(modelPath);
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(modelFile, state, 0, Path.GetDirectoryName(modelFile)!);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot glTF import failed ({error}): {modelFile}");
        return document.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no Node3D scene from glTF: {modelFile}");
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private static void VerifyHash(string path, string expected)
    {
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provenance hash mismatch: {path}");
    }

    private static void WriteReport(string reportPath, object report)
    {
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        File.WriteAllText(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
    }

    private static Dictionary<string, string> ParseOptions(string[] arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected runtime argument: {argument}");
            var name = argument[2..];
            var value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? arguments[++index]
                : "true";
            result.Add(name, value);
        }
        return result;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required --{name} option.");

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

    private readonly record struct Provenance(string SourceSha256);
}
