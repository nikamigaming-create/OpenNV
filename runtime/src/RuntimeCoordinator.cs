using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator : Node3D
{
    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private LegalAssetSetupView? _setupView;

    public override void _Ready()
    {
        Callable.From(StartRuntime).CallDeferred();
    }

    private void StartRuntime()
    {
        try
        {
            _options = ParseOptions(OS.GetCmdlineUserArgs());
            var hasDataRoot = _options.TryGetValue("data-root", out var dataRoot);
            var hasModel = _options.ContainsKey("model");
            var hasCellScene = _options.ContainsKey("cell-scene");
            if ((hasDataRoot ? 1 : 0) + (hasModel ? 1 : 0) + (hasCellScene ? 1 : 0) > 1)
                throw new ArgumentException("Use only one of --data-root, --model/--sidecar, or --cell-scene.");
            if (!hasModel && _options.ContainsKey("sidecar"))
                throw new ArgumentException("--sidecar requires --model.");

            if (hasDataRoot)
            {
                var prepared = LegalAssetPreparer.Prepare(dataRoot!, _options);
                LoadPrepared(prepared, _options);
                return;
            }

            if (hasModel)
            {
                LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
                return;
            }

            if (hasCellScene)
            {
                LoadCellScene(RequireOption(_options, "cell-scene"), _options);
                return;
            }

            if (_options.ContainsKey("reuse-cache"))
            {
                if (!LegalAssetPreparer.TryRestore(_options, out var restored, out var restoreError))
                    throw new InvalidOperationException(restoreError ?? "No prepared legal-asset cache exists.");
                LoadPrepared(restored, _options);
                return;
            }

            if (_options.TryGetValue("report", out var startupReportPath))
                WriteStartupReport(startupReportPath);
            GD.Print("OPENNV_GODOT_EXPERIMENTAL_READY playable=0");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            else if (LegalAssetPreparer.TryRestore(_options, out var restored, out var restoreError))
                LoadPrepared(restored, _options);
            else
                ShowExperimentalStatus(restoreError);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_RUNTIME_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void LoadPrepared(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        if (prepared.CellScenePath is not null)
            LoadCellScene(prepared.CellScenePath, options);
        else
            LoadModel(prepared.ModelPath, prepared.SidecarPath, options);
    }

    private void LoadCellScene(string scenePath, IReadOnlyDictionary<string, string> options)
    {
        var runTraversalProof = options.ContainsKey("portal-proof");
        var loaded = CellSceneLoader.Load(
            scenePath,
            this,
            !runTraversalProof && options.ContainsKey("open-proof-door"),
            options.TryGetValue("proof-door", out var proofDoor) ? proofDoor : null);
        if (runTraversalProof)
        {
            _ = RunDoorTraversalProof(loaded, scenePath, options);
            return;
        }
        CompleteCellLoad(loaded, scenePath, options, null);
    }

    private async Task RunDoorTraversalProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var floor = CellSceneLoader.CastSpawnFloor(GetWorld3D().DirectSpaceState);
            if (!floor.Hit || MathF.Abs(floor.Y) > 0.20f)
                throw new InvalidOperationException(
                    $"XTEL floor contract failed: hit={floor.Hit} y={floor.Y} collider={floor.ColliderPath}");
            var ray = CellSceneLoader.BuildProofRay(loaded.ProofDoor);
            var closed = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            loaded.ProofDoor.SetOpen(true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var opened = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            if (!closed.Hit || !closed.HitProofDoor || opened.Hit)
                throw new InvalidOperationException(
                    $"Door traversal contract failed: closedHit={closed.Hit} " +
                    $"closedHitDoor={closed.HitProofDoor} closedCollider={closed.ColliderPath} " +
                    $"openHit={opened.Hit} openCollider={opened.ColliderPath} " +
                    $"localSize={ray.LocalSize} localNormal={ray.LocalNormal} from={ray.From} to={ray.To}");
            CompleteCellLoad(
                loaded with { ProofDoorOpen = true },
                scenePath,
                options,
                new DoorTraversalProof(
                    floor.Hit,
                    floor.Y,
                    floor.ColliderPath,
                    closed.Hit,
                    closed.HitProofDoor,
                    opened.Hit));
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_DOOR_TRAVERSAL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteCellLoad(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        DoorTraversalProof? traversalProof)
    {
        var report = new
        {
            schema = "opennv-godot-cell/v1",
            status = "pass",
            renderer = "forward_plus",
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            assets = loaded.Assets,
            references = loaded.References,
            doors = loaded.Doors,
            collisionMeshes = loaded.CollisionMeshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            spawnSource = "XTEL",
            spawnAtFloorOrigin = true,
            proofDoorFormId = loaded.ProofDoorFormId,
            proofDoorOpen = loaded.ProofDoorOpen,
            wholeCellVisible = true,
            doorTraversal = traversalProof is null
                ? null
                : new
                {
                    status = "pass",
                    floorHit = traversalProof.Value.FloorHit,
                    floorY = traversalProof.Value.FloorY,
                    floorCollider = traversalProof.Value.FloorCollider,
                    closedHit = traversalProof.Value.ClosedHit,
                    closedHitDoor = traversalProof.Value.ClosedHitDoor,
                    openHit = traversalProof.Value.OpenHit,
                },
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_CELL_PASS cell={loaded.FormId} assets={loaded.Assets} " +
            $"references={loaded.References} doors={loaded.Doors} collision={loaded.CollisionMeshes} " +
            $"surfaces={loaded.Surfaces} vertices={loaded.Vertices} proofDoorOpen={loaded.ProofDoorOpen} " +
            $"doorTraversal={(traversalProof is null ? "not-requested" : "pass")}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = StaticModelSlice.Load(modelPath, sidecarPath, this);
        var report = new
        {
            schema = "opennv-godot-static-model/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            sourceSha256 = loaded.SourceSha256,
            meshes = loaded.Meshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_MODEL_PASS source={loaded.SourceSha256} " +
            $"meshes={loaded.Meshes} surfaces={loaded.Surfaces} vertices={loaded.Vertices}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void ShowExperimentalStatus(string? restoreError)
    {
        _setupView = new LegalAssetSetupView();
        _setupView.Configure(restoreError, OnDataRootSelected);
        AddChild(_setupView);
    }

    private async void OnDataRootSelected(string dataRoot)
    {
        _setupView!.SetPreparing();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PrepareSelectedData(dataRoot);
    }

    private void PrepareSelectedData(string dataRoot)
    {
        try
        {
            var prepared = LegalAssetPreparer.Prepare(dataRoot, _options);
            LoadPrepared(prepared, _options);
            _setupView?.QueueFree();
            _setupView = null;
        }
        catch (Exception exception)
        {
            _setupView!.ShowError();
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

    private readonly record struct DoorTraversalProof(
        bool FloorHit,
        float FloorY,
        string FloorCollider,
        bool ClosedHit,
        bool ClosedHitDoor,
        bool OpenHit);
}
