using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    // Selected owned references only: proves dispatch and native controller
    // ownership, not a complete CELL, rendered pixels or retail parity.
    private void ExerciseOwnedClockLightReference(string dataRoot, string receiptPath)
    {
        var output = Path.GetFullPath(receiptPath);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Clock/light audit receipt must be fresh.");
        var installation = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.StartsWith(installation, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Clock/light audit cannot write inside the owned installation.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var rows = new List<object>();
        var sourceRows = new List<object>();
        var prototypes = new Dictionary<FalloutFormKey, RuntimeNativeNifPrototype>();
        var sourceHashes = new Dictionary<FalloutFormKey, string>();
        var modelHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? failure = null;
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        FalloutFormKey Form(uint id) => new("FalloutNV.esm", id);
        var animated = Form(0x13ba74); var alwaysOn = Form(0x171b33);
        var selected = new[] { animated, alwaysOn };
        var sourceCells = selected.Select(key => FalloutCellSceneReader.ParentCell(records.GetEffective(key)) ??
            throw new InvalidDataException($"Clock/light reference {key} has no authored parent CELL."))
            .Distinct().Select(key => FalloutCellSceneReader.Read(records, key)).ToArray();
        var references = selected.Select(key => sourceCells.SelectMany(cell => cell.References)
            .Single(reference => reference.FormKey == key)).ToArray();
        var bases = sourceCells.SelectMany(cell => cell.BaseObjects).DistinctBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        const float units = .0142875f;
        static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
        static Transform3D Pose(FalloutPlacedReference reference) => new(
            GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
            GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
        static object NativeControllers(Node3D node) => NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node)
            .Select(controller => new
            {
                controller.SourceController,
                controller.SourceSha256,
                groups = controller.SequenceNames.ToArray(),
                controller.ActiveSequence,
                controller.ActiveSourceSequence,
                controller.SourceTimeSeconds,
                controller.Playing,
                state = controller.CaptureObjectState()
            }).ToArray();
        try
        {
            foreach (var reference in references)
            {
                var record = records.GetEffective(reference.FormKey);
                var basis = records.GetEffective(reference.Base);
                var script = FalloutScriptLocals.AttachedScript(records, record);
                Require(record.Signature == "REFR" && !record.IsDeleted && record.Flags == reference.Flags,
                    "Clock/light reference source signature/deletion/flags disagree with the parsed placement.");
                Require(reference.FormKey == animated
                    ? reference.Base == Form(0x152c88) && basis.Signature == "ACTI" && script?.FormKey == Form(0x11b039)
                    : reference.Base == Form(0x171b32) && basis.Signature == "STAT" && script is null,
                    "Selected clock/light winning reference/base/script graph changed.");
                sourceHashes[record.FormKey] = Hash(record);
                sourceHashes[basis.FormKey] = Hash(basis);
                if (script is not null) sourceHashes[script.FormKey] = Hash(script);
                var model = bases[reference.Base].ModelPath ?? throw new InvalidDataException("Clock/light base has no source model.");
                Require(content.TryRead(model, null, out var bytes, out _), "Clock/light source NIF is absent.");
                var nif = FalloutNifFile.Read(bytes);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                Require(nif.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase), "Decoded source NIF identity disagrees with its actual bytes.");
                modelHashes[model] = hash;
                var managers = nif.Blocks.Where(block => block.TypeName == "NiControllerManager")
                    .Select(block => (FalloutNifControllerManager)nif.ReadObject(block.Index)).ToArray();
                var sequences = nif.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                    .Select(block => nif.ReadControllerSequence(block.Index)).ToArray();
                if (reference.FormKey == animated)
                    Require(sequences.Any(sequence => sequence.Name.Equals("Left", StringComparison.OrdinalIgnoreCase)) &&
                        sequences.Any(sequence => sequence.Name.Equals("Right", StringComparison.OrdinalIgnoreCase)),
                        "Authored clock/light NIF does not declare both requested groups.");
                sourceRows.Add(new
                {
                    reference = reference.FormKey.ToString(),
                    reference.Flags,
                    referenceSha256 = Hash(record),
                    basis = reference.Base.ToString(),
                    baseFlags = basis.Flags,
                    baseSha256 = Hash(basis),
                    script = script?.FormKey.ToString(),
                    scriptSha256 = script is null ? null : Hash(script),
                    model,
                    nifSha256 = hash,
                    managers = managers.Select(manager => new { block = manager.Block.Index, manager.Time.Flags, manager.Sequences }).ToArray(),
                    sequences = sequences.Select(sequence => new
                    {
                        block = sequence.Block.Index,
                        sequence.Name,
                        sequence.Manager,
                        sequence.CycleType,
                        sequence.Frequency,
                        sequence.StartTime,
                        sequence.StopTime
                    }).ToArray(),
                });
                prototypes.Add(reference.FormKey, new(nif, units));
            }
            foreach (var hour in new float[] { 12, 22, 6, 20 })
            {
                using var world = new FalloutReferenceWorld(records);
                foreach (var cell in sourceCells) world.LoadCell(cell);
                Require(world.Get(animated).Script?.Record.FormKey == Form(0x11b039) && world.Get(alwaysOn).Script is null &&
                    selected.All(world.IsEnabled), "Source script/enabled admission changed before the clock frame.");
                var globals = FalloutGlobalState.Read(records);
                var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                    FalloutCalendar.Read(Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe")));
                clock.InitializeNewGame(); clock.SetHour(hour); clock.AdvanceSimulation(0);
                var root = new Node3D(); AddChild(root);
                try
                {
                    var presentation = new RuntimeNativeReferencePresentation(world, references, reference =>
                    {
                        var node = prototypes[reference.FormKey].InstantiatePlaced(Pose(reference));
                        node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); root.AddChild(node);
                        return node;
                    });
                    root.AddChild(presentation);
                    foreach (var key in selected) _ = presentation.Resolve(key);
                    var controllers = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(presentation.Nodes[animated]).ToArray();
                    foreach (var controller in NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(root)) controller.SetProcess(false);
                    Require(new[] { "Left", "Right" }.All(group => controllers.Count(controller => controller.SourceController >= 0 && controller.HasSequence(group)) == 1),
                        "Actual native animation has no unique managed owner for both source groups.");
                    Require(controllers.Where(controller => controller.SourceController >= 0).All(controller =>
                        controller.SourceSha256.Equals(modelHashes[bases[references.Single(reference => reference.FormKey == animated).Base].ModelPath!], StringComparison.OrdinalIgnoreCase)),
                        "Native controller identity differs from the owned NIF.");
                    var beforeAlwaysOn = JsonSerializer.Serialize(new
                    {
                        state = world.Get(alwaysOn).Capture(),
                        native = NativeControllers(presentation.Nodes[alwaysOn])
                    });
                    var before = controllers.Select(controller => controller.CaptureObjectState()).ToArray();
                    var reports = new List<string>();
                    var host = RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) =>
                        throw new NotSupportedException("Selected clock/light audit has no furniture owner."),
                        _ => throw new NotSupportedException("Selected clock/light audit has no gameplay/player effect owner."),
                        Globals: globals, PlayGroup: presentation.PlayGroup, IsAnimPlaying: presentation.IsAnimPlaying, GameTime: clock), null);
                    var events = new RuntimeNativeReferenceEvents { ReportDivergence = reports.Add };
                    events.Configure(records, world, new(records), sourceCells[0] with { References = references, BaseObjects = bases },
                        root, host, Pose, units, 1);
                    root.AddChild(events);
                    events.SetProcess(true); events._Process(0); events.SetProcess(false);
                    var after = controllers.Select(controller => controller.CaptureObjectState()).ToArray();
                    var selectedState = after.Where(state => state?.ScriptSelected == true).ToArray();
                    var expected = hour == 12 ? "Right" : hour == 22 ? "Left" : null;
                    var unchangedAlwaysOn = beforeAlwaysOn == JsonSerializer.Serialize(new
                    {
                        state = world.Get(alwaysOn).Capture(),
                        native = NativeControllers(presentation.Nodes[alwaysOn])
                    });
                    rows.Add(new
                    {
                        hour = clock.Hour,
                        expected,
                        before,
                        after,
                        reports,
                        referenceEvents = events.State,
                        scriptError = world.Get(animated).ScriptError,
                        scriptlessAlwaysOnUnchanged = unchangedAlwaysOn,
                        scriptlessAlwaysOnControllers = NativeControllers(presentation.Nodes[alwaysOn]),
                        controllers = controllers.Select(controller => new
                        {
                            controller.SourceController,
                            controller.SourceSha256,
                            groups = controller.SequenceNames.ToArray(),
                            controller.ActiveSequence,
                            controller.Playing
                        }).ToArray()
                    });
                    Require(reports.Count == 0 && world.Get(animated).ScriptError is null,
                        "Actual clock/light dispatch refused: " + string.Join("; ", reports));
                    Require(unchangedAlwaysOn, "Source clock dispatch changed the scriptless AlwaysOn reference.");
                    if (expected is null)
                        Require(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after), "Boundary hour invented a source animation selection.");
                    else
                        Require(selectedState.Length == 1 && (selectedState[0]!.Sequence.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                            selectedState[0]!.PendingSequence?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true),
                            "Actual source dispatch did not select its expected owned animation group.");
                    var stable = JsonSerializer.Serialize(after);
                    events.SetProcess(true); events._Process(0); events.SetProcess(false);
                    Require(stable == JsonSerializer.Serialize(controllers.Select(controller => controller.CaptureObjectState()).ToArray()),
                        "An unchanged source clock frame replayed its animation selection.");
                }
                finally { root.Free(); }
            }
            foreach (var (key, expected) in sourceHashes)
                Require(Hash(records.GetEffective(key)) == expected, "Clock/light audit changed owned record bytes.");
            foreach (var (model, expected) in modelHashes)
            {
                Require(content.TryRead(model, null, out var bytes, out _), "Source NIF disappeared during the clock/light proof.");
                Require(Convert.ToHexString(SHA256.HashData(bytes)) == expected, "Clock/light audit changed owned NIF bytes.");
            }
            GD.Print("OPENNV_NATIVE_OWNED_CLOCK_LIGHT_REFERENCE_PASS sourceGraph=true dayRight=true nightLeft=true boundaries=true " +
                "realManagedController=true scriptlessAlwaysOnUnchanged=true repeatedFrameStable=true sourceUnchanged=true " +
                "playerAbsent=true nativePixels=unverified retailParity=unverified");
        }
        catch (Exception error) { failure = error.Message; throw; }
        finally
        {
            foreach (var prototype in prototypes.Values) prototype.Scene.Root.Free();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-clock-light-reference/v1",
                status = failure is null ? "passed" : "blocked",
                error = failure,
                runtimeBuild = typeof(RuntimeCoordinator).Assembly.ManifestModule.ModuleVersionId,
                source = "read-only-owned-winning-records-and-native-models",
                playerCreated = false,
                intensityAdjusted = false,
                recording = false,
                nativePixels = "unverified",
                retailParity = "unverified",
                sources = sourceRows,
                frames = rows,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
