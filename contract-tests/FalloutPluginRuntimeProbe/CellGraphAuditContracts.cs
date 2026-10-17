using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CellGraphAuditContracts
{
    internal static void Run()
    {
        SourceCampaigns();
        SourceOptions();
        NifTextureSourceIdentity();
        NifGeometryDataLinks();
        NifAnimationSoundDeclarations();
        AddonDependencyDeclarations();
        ConvexProjectionAttribution();
        CompleteDenominator();
        AlternativeQueryMemoization();
        PlacedRootMath();
        FloorProjectionCandidates();
        ModeledLightProjection();
        DisabledNavigationDeclarations();
        ExternalNavigationTargetDeclarations();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-graph-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game");
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(Path.Combine(data, "meshes"));
        Directory.CreateDirectory(Path.Combine(data, "textures"));
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), PluginFixture());
            ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "unused fixture member");
            var archives = Path.Combine(directory, "archives.ini");
            File.WriteAllText(archives, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            File.WriteAllBytes(Path.Combine(data, "meshes", "box.nif"), NifFixture());
            var lightBytes = NifFixture(texture: true);
            File.WriteAllBytes(Path.Combine(data, "meshes", "modeled-light.nif"), lightBytes);
            File.WriteAllBytes(Path.Combine(data, "textures", "probe.dds"), DdsFixture());
            var configuration = Path.Combine(directory, "runtime.json");
            File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
            var checkpoint = Path.Combine(directory, "checkpoint.json");
            var snapshot = Path.Combine(directory, "snapshot.json");
            string compatibility;
            FalloutReferenceSnapshot[] referenceState;
            using (var source = Open())
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            using (var world = new FalloutReferenceWorld(records))
            {
                compatibility = source.SaveCompatibilityId;
                world.Get(Key(0x812)).Enabled = false;
                referenceState = world.Capture().ToArray();
                WriteSnapshot(snapshot, compatibility, records);
            }
            File.WriteAllText(checkpoint, JsonSerializer.Serialize(new
            {
                Schema = FalloutNativeCampaignSave.ExpectedSchema, SaveCompatibilityId = "different-owned-stack",
                ActiveCell = Key(0x800), References = referenceState, Vitals = new { Level = 999 }
            }));
            var output = Path.Combine(directory, "first");
            var arguments = Arguments(game, output, configuration, checkpoint, snapshot);
            Require(!Audit(arguments), "Failed records or a mismatched checkpoint were awarded a passing inventory.");
            using (var component = Read(Path.Combine(output, "component.private.json")))
            {
                var root = component.RootElement;
                Require(root.GetProperty("selection").GetProperty("totalSelectedInteriors").GetInt32() == 4 &&
                    root.GetProperty("selection").GetProperty("xtelWeakComponentFromSeed").GetInt32() == 2 &&
                    root.GetProperty("selection").GetProperty("disconnectedMetadataInteriors").GetArrayLength() == 1 &&
                    root.GetProperty("edges").EnumerateArray().Count(edge => edge.GetProperty("exteriorBoundary").GetBoolean()) == 1,
                    "Explicit metadata selection, directed interior closure, disconnected interiors or exterior boundaries changed.");
                var totals = root.GetProperty("summary").GetProperty("denominator");
                Require(totals.GetProperty("effectiveSourceReferences").GetInt32() == 9 &&
                    totals.GetProperty("readerBoundReferences").GetInt32() == 8 &&
                    totals.GetProperty("unboundSourceReferences").GetInt32() == 1 &&
                    root.GetProperty("summary").GetProperty("unboundSourceReferenceIdentities")[0].GetString() == Key(0x840).ToString(),
                    "A failed source CELL lost its reference denominator or exact unbound identity.");
                Require(root.GetProperty("summary").GetProperty("invariants").EnumerateObject().All(value => value.Value.GetBoolean()),
                    "The source reference/resource union is inconsistent.");
            }
            using (var cell = Read(Path.Combine(output, "cell-FalloutNV-esm-000800.private.json")))
            {
                var root = cell.RootElement;
                Require(!root.GetProperty("invariants").GetProperty("savedWorldRestored").GetBoolean() &&
                    root.GetProperty("checkpoint").GetProperty("schema").ValueKind == JsonValueKind.Null &&
                    root.GetProperty("references").EnumerateArray().All(reference => reference.GetProperty("savedEnabled").ValueKind == JsonValueKind.Null) &&
                    root.GetProperty("failures").EnumerateArray().Any(failure => failure.GetProperty("lane").GetString() == "saved-reference-world"),
                    "A rejected checkpoint leaked reference state or metadata into source evaluation.");
                var light = root.GetProperty("references").EnumerateArray().Single(reference => reference.GetProperty("identity").GetString() == Key(0x813).ToString());
                Require(light.GetProperty("disposition").GetString() == "source-light" &&
                    light.GetProperty("failures").EnumerateArray().Any(failure => failure.GetProperty("lane").GetString() == "light-admission"),
                    "Source inspection concealed the unowned modeled light runtime behavior.");
                var model = root.GetProperty("resources").EnumerateArray().Single(resource => resource.GetProperty("path").GetString() == "meshes/modeled-light.nif");
                Require(model.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(lightBytes)) &&
                    model.GetProperty("decodedBlocks").GetInt32() == 6 &&
                    model.GetProperty("dependencies").EnumerateArray().Single().GetString() == "textures/probe.dds" &&
                    root.GetProperty("resources").EnumerateArray().Any(resource => resource.GetProperty("path").GetString() == "textures/probe.dds"),
                    "The real modeled-LIGH NIF, source hash or DDS dependency was omitted.");
                var review = root.GetProperty("nativeReview");
                Require(review.GetProperty("runtimeBuild").GetGuid() == OldBuild &&
                    review.GetProperty("capturedUtc").GetDateTime() == OldTime &&
                    root.GetProperty("runtimeAuditBuild").GetGuid() != OldBuild,
                    "An old captured native build/time was replaced by the current source audit build.");
                var enabled = root.GetProperty("references").EnumerateArray().Single(reference => reference.GetProperty("identity").GetString() == Key(0x815).ToString());
                var domain = enabled.GetProperty("enableDomain").GetProperty("domain");
                Require(domain[0].GetProperty("subjectEnabled").GetBoolean() && !domain[1].GetProperty("subjectEnabled").GetBoolean(),
                    "Opposite XESP alternatives did not run through the actual reference state owner.");
            }
            Reject(() => CellGraphAudit.Run(arguments), "fresh directory");
            File.WriteAllText(checkpoint, JsonSerializer.Serialize(new
            {
                Schema = FalloutNativeCampaignSave.ExpectedSchema, SaveCompatibilityId = compatibility,
                ActiveCell = Key(0x800), References = referenceState
            }));
            WriteSnapshot(snapshot, "mismatched-native-stack", null);
            var second = Path.Combine(directory, "second");
            Require(!Audit(Arguments(game, second, configuration, checkpoint, snapshot)), "Native source mismatch was ignored.");
            using (var cell = Read(Path.Combine(second, "cell-FalloutNV-esm-000800.private.json")))
            {
                var root = cell.RootElement;
                Require(root.GetProperty("invariants").GetProperty("savedWorldRestored").GetBoolean() &&
                    root.GetProperty("nativeReview").ValueKind == JsonValueKind.Null &&
                    root.GetProperty("denominator").GetProperty("nativeScopeReferences").GetInt32() == 0 &&
                    root.GetProperty("failures").EnumerateArray().Any(failure => failure.GetProperty("lane").GetString() == "native-snapshot-binding"),
                    "A rejected native snapshot partially joined its resident state.");
                var reference = root.GetProperty("references").EnumerateArray().Single(reference => reference.GetProperty("identity").GetString() == Key(0x812).ToString());
                Require(reference.GetProperty("sourceEnabled").GetBoolean() && !reference.GetProperty("savedEnabled").GetBoolean(),
                    "The source and validated reference checkpoint enable lanes were conflated.");
            }
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--seed", Plugin + ":800"]), "--runtime-config");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--seed", Plugin + ":800", "--seed", Plugin + ":801", "--runtime-config", configuration]), "Repeated");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--seed", Plugin + ":1000000", "--runtime-config", configuration]), "plugin:hex-object");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--seed", Plugin + ":800", "--runtime-config", configuration, "--sample-native", "NaN", "0", "0"]), "finite");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--seed", Plugin + ":800", "--runtime-config", configuration, "--fake-success"]), "Unknown");
            Require(CellGraphAudit.RunCommand([game, "invalid-command", "--seed", Plugin + ":800"]) == 2,
                "Command/setup errors were reported as an audited divergence.");
            Require(!Audit([game, Path.Combine(directory, "empty"), "--scope", "component", "--seed", Plugin + ":802", "--runtime-config", configuration]),
                "A source-only empty diagnostic component was awarded complete runtime readiness.");
            using (var empty = Read(Path.Combine(directory, "empty", "cell-FalloutNV-esm-000802.private.json")))
                Require(empty.RootElement.GetProperty("failures").GetArrayLength() == 0 && empty.RootElement.GetProperty("references").GetArrayLength() == 0,
                    "An empty source CELL was assigned invented geometry or reference failures.");
            Console.WriteLine("OPENNV_CELL_GRAPH_AUDIT_CONTRACT_PASS explicitSelection=true interiorClosure=true exteriorBoundary=true disconnected=true empty=true failedCellDenominator=true modeledLightResources=true sourceHashes=true checkpointStackJoin=true snapshotBuildTimePreserved=true placedRootReplaced=true descendantTransforms=true nativePhysicsUnverified=true");

            RuntimeLiveContentSource Open() => RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                activePlugins: [Plugin], archiveIniPath: archives);
            bool Audit(string[] command)
            {
                using var source = Open();
                using var records = FalloutPluginStack.Load(source.PluginSources);
                var options = CellGraphAudit.ParseOptions(command);
                Directory.CreateDirectory(options.Output);
                return CellGraphAudit.RunComponent(source, records, options, .1f,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration))));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void PlacedRootMath()
    {
        var first = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture()), .1f);
        var changedRoot = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture(rootVariant: true)), .1f);
        Require(first.Triangles.Count == 12 && first.Shapes == 1 && first.Failures.Count == 0 &&
            first.Triangles.SequenceEqual(changedRoot.Triangles), "Placed collision composed exported model-root TRS instead of replacing it.");
        Near(first.Triangles[0].A, new(.3f, 1.6f, -3.05f));
        var rotated = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture(rotateChild: true)), .1f);
        Near(rotated.Triangles[0].A, new(-.05f, 1.6f, -1.3f));
        var doubled = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture()), .2f);
        Near(doubled.Triangles[0].A, first.Triangles[0].A * 2);
        var placed = CellGraphAudit.ReferenceTransform([100, 200, 300], [0, 0, 0], 2, .1f);
        Near(placed * first.Triangles[0].A, new(10.6f, 33.2f, -26.1f));
        var multi = CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture(multiRoot: true)), .1f);
        Require(multi.Triangles.Count == 0 && multi.Failures.Count == 1, "Unowned multi-root placement silently contributed support geometry.");
        Reject(() => CellGraphAudit.ReadPlacedCollision(FalloutNifFile.Read(NifFixture()), 0), "positive");
    }

    private static void Near(Vector3 actual, Vector3 expected) => Require(actual.DistanceTo(expected) < 1e-5f,
        $"Source transform/unit fixture differs: {actual}, expected {expected}.");
    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllBytes(path));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when ((error is IOException or InvalidDataException or ArgumentException) &&
            error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidDataException("Cell-graph contract expected rejection: " + message);
    }
}
