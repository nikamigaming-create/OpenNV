using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

using OpenNV.Runtime.Presentation.CharacterCreation;

var controls = new[]
{
    new OwnedGamebryoFaceGenMorphControl("shape-a", new string('a', 64), [2.0f, 0.0f]),
    new OwnedGamebryoFaceGenMorphControl("shape-b", new string('b', 64), [0.0f, -4.0f]),
};
var values = new Dictionary<string, float>(StringComparer.Ordinal)
{
    ["shape-a"] = 2.5f,
    ["shape-b"] = -1.0f,
};
var evaluated = OwnedGamebryoFaceGenMorphRuntime.Evaluate(
    [1.0f, 2.0f], controls, values, -5.0f, 5.0f, 0.1f, 0.0f);
Require(evaluated.SymmetricGeometry.SequenceEqual([1.5f, 2.4f]),
    "FaceGen evaluation did not apply exact UI-to-EGM scaling.");
Require(evaluated.SymmetricGeometrySha256 == Hash([1.5f, 2.4f]),
    "FaceGen evaluation hash differs from its exact float payload.");

var initial = OwnedGamebryoFaceGenMorphRuntime.Evaluate(
    [1.0f, 2.0f],
    controls,
    new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["shape-a"] = 0.0f,
        ["shape-b"] = 0.0f,
    },
    -5.0f, 5.0f, 0.1f, 0.0f);
var first = OwnedGamebryoFaceGenMorphRuntime.Advance(
    initial.SymmetricGeometry, controls, initial.ControlValues,
    "shape-a", 2.5f, -5.0f, 5.0f, 0.1f);
var second = OwnedGamebryoFaceGenMorphRuntime.Advance(
    first.SymmetricGeometry, controls, first.ControlValues,
    "shape-b", -1.0f, -5.0f, 5.0f, 0.1f);
Require(second.SymmetricGeometry.SequenceEqual(evaluated.SymmetricGeometry),
    "Incremental FaceGen state differs from deterministic reconstruction.");
var revised = OwnedGamebryoFaceGenMorphRuntime.Advance(
    second.SymmetricGeometry, controls, second.ControlValues,
    "shape-a", 1.0f, -5.0f, 5.0f, 0.1f);
Require(revised.SymmetricGeometry.SequenceEqual([1.2f, 2.4f]),
    "FaceGen absolute UI coordinate was applied as an accumulated offset.");

RequireThrows(() => OwnedGamebryoFaceGenMorphRuntime.Advance(
    revised.SymmetricGeometry, controls, revised.ControlValues,
    "unsupported", 1.0f, -5.0f, 5.0f, 0.1f));
RequireThrows(() => OwnedGamebryoFaceGenMorphRuntime.Advance(
    revised.SymmetricGeometry, controls, revised.ControlValues,
    "shape-a", 6.0f, -5.0f, 5.0f, 0.1f));

if (args.Length == 2 && args[0] == "--owned-opening")
    RunOwnedOpening(args[1]);
else if (args.Length != 0)
    throw new InvalidOperationException("Unsupported FaceGen probe arguments.");

Console.WriteLine("OPENNV_GAMEBRYO_FACEGEN_MORPH_PROBE_OK");

static void RunOwnedOpening(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var root = document.RootElement;
    var isNewVegas = root.TryGetProperty("newGameFlow", out var newGameFlow);
    var appearance = isNewVegas
        ? newGameFlow.GetProperty("character").GetProperty("appearance")
        : root.GetProperty("opening").GetProperty("characterSelection")
            .GetProperty("appearance");
    var faceGen = appearance.GetProperty("player").GetProperty("faceGen");
    var controlSpace = faceGen.GetProperty("controlSpace");
    var exposure = controlSpace.GetProperty("nativeGeometryExposure")
        .GetProperty("controls").EnumerateArray().ToArray();
    var axes = controlSpace.GetProperty("format").GetProperty("controls")
        .GetProperty("symmetricGeometry").EnumerateArray()
        .ToDictionary(value => value.GetProperty("index").GetInt32());
    var ownedControls = exposure.Select(value =>
    {
        var axis = axes[value.GetProperty("controlIndex").GetInt32()];
        var axisSha256 = value.GetProperty("axisSha256").GetString()!;
        Require(axis.GetProperty("axisSha256").GetString() == axisSha256,
            "Owned FaceGen control and EGM axis identities differ.");
        return new OwnedGamebryoFaceGenMorphControl(
            value.GetProperty("settingEntity").GetString()!,
            axisSha256,
            axis.GetProperty("axis").EnumerateArray()
                .Select(coordinate => coordinate.GetSingle()).ToArray());
    }).ToArray();
    var previewPolicy = controlSpace.GetProperty("runtimePreviewControl");
    var reset = previewPolicy.GetProperty("resetValue").GetSingle();
    var acceptance = previewPolicy.GetProperty("acceptanceValue").GetSingle();
    var first = ownedControls[0];
    var ownedValues = ownedControls.ToDictionary(
        value => value.SettingEntity,
        _ => reset,
        StringComparer.Ordinal);
    ownedValues[first.SettingEntity] = acceptance;
    var baselineSource = isNewVegas
        ? faceGen.GetProperty("symmetricGeometry")
        : appearance.GetProperty("races")[0].GetProperty("sex")
            .GetProperty("male").GetProperty("faceGenDefaults")
            .GetProperty("symmetricGeometry");
    var baseline = baselineSource.GetProperty("values")
        .EnumerateArray().Select(value => value.GetSingle()).ToArray();
    var result = OwnedGamebryoFaceGenMorphRuntime.Evaluate(
        baseline,
        ownedControls,
        ownedValues,
        previewPolicy.GetProperty("minimum").GetSingle(),
        previewPolicy.GetProperty("maximum").GetSingle(),
        previewPolicy.GetProperty("morphWeightScale").GetSingle(),
        reset);
    var baselineSha256 = baselineSource.GetProperty("sha256").GetString();
    Require(result.SymmetricGeometrySha256 != baselineSha256,
        "Owned nonzero FaceGen coordinate did not change symmetric geometry.");

    var preview = faceGen.GetProperty("previewHead").GetProperty("previews")[0];
    using var sidecar = JsonDocument.Parse(File.ReadAllText(
        preview.GetProperty("outputs").GetProperty("sidecar").GetString()!));
    var boundSurfaces = sidecar.RootElement.GetProperty("surfaces").EnumerateArray()
        .Count(surface => surface.GetProperty("faceGenMorphs")
            .GetProperty("geometryControls").GetProperty("targetNames")
            .EnumerateArray().Any(value => value.GetString() == first.SettingEntity));
    Require(boundSurfaces > 0,
        "Owned preview artifact does not bind the selected FaceGen control.");
    Console.WriteLine(
        $"OPENNV_GAMEBRYO_FACEGEN_OWNED_MORPH_OK control={first.SettingEntity} " +
        $"value={acceptance:R} geometrySha256={result.SymmetricGeometrySha256} " +
        $"boundSurfaces={boundSurfaces}");
}

static string Hash(IReadOnlyList<float> values)
{
    var payload = new byte[values.Count * sizeof(float)];
    for (var index = 0; index < values.Count; index++)
        BinaryPrimitives.WriteSingleLittleEndian(
            payload.AsSpan(index * sizeof(float), sizeof(float)), values[index]);
    return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RequireThrows(Action action)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Expected fail-closed FaceGen rejection.");
}
