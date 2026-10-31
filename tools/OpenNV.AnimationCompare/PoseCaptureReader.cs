using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace OpenNV.AnimationCompare;

internal sealed record ObservedLayer(int Slot, string Resource, double Seconds, bool Elapsed,
    double? Begin, double? End, double Frequency, int State, double Weight, double? Ease);
internal sealed record ObservedTransform(Vector3 Position, Quaternion Rotation, Vector3 Scale);
internal sealed record ObservedResource(string Resource, string Owner, int? State);
internal sealed record ObservedPose(double Seconds, long Frame, ObservedTransform Body,
    IReadOnlyDictionary<string, ObservedTransform> Bones, IReadOnlyList<ObservedLayer> Layers,
    bool LayerCoverageQualified, IReadOnlyDictionary<string, ObservedTransform>? WorldBones = null,
    double? SimulationSeconds = null, string RateTimeBasis = "wall-clock", bool SimulationRateQualified = false,
    IReadOnlyList<ObservedResource>? AdditionalResources = null, bool? RootBehindCamera = null);
internal sealed class ObservedActor(string reference)
{
    internal string Reference { get; } = reference;
    internal string? Base { get; set; }
    internal bool HierarchyQualified { get; set; }
    internal string? PosePublicationUnqualifiedReason { get; set; }
    internal List<ObservedPose> Samples { get; } = [];
}
internal sealed record ObservedCapture(string Engine, string Input, string Stage,
    IReadOnlyDictionary<string, ObservedActor> Actors, IReadOnlyList<string> Problems);

// These adapters consume bounded observation files. They neither select an
// animation nor substitute reference transforms into the product runtime.
internal static class PoseCaptureReader
{
    private const long MaximumInputBytes = 128 * 1024 * 1024;
    internal static ObservedCapture ReadNative(string path, string[] plugins, double units)
    {
        RequireFile(path);
        if (plugins.Length == 0 || plugins.Any(string.IsNullOrWhiteSpace) || !double.IsFinite(units) || units <= 0)
            throw new ArgumentException("Explicit native load order and a positive source-unit conversion are required.");
        var actors = new Dictionary<string, ObservedActor>(StringComparer.OrdinalIgnoreCase);
        var problems = new HashSet<string>(StringComparer.Ordinal);
        long? process = null, generation = null, firstTicks = null, previousFrame = null;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            ++lineNumber;
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            var currentProcess = Long(row, "processId");
            var currentGeneration = Long(row, "generation");
            process ??= currentProcess; generation ??= currentGeneration;
            if (process != currentProcess || generation != currentGeneration)
                throw new InvalidDataException("Native file mixes processes or request generations.");
            if (Long(row, "readFailures") != 0 || !Boolean(row, "withinTraversalBounds"))
                problems.Add($"native-read-or-traversal-failure:row-{lineNumber}");
            var ticks = Long(row, "ticks");
            firstTicks ??= ticks;
            var frequency = Number(row, "clockFrequency");
            if (frequency <= 0) throw new InvalidDataException("Native monotonic frequency is invalid.");
            var seconds = (ticks - firstTicks.Value) / frequency;
            var frame = Long(row, "frame");
            if (previousFrame is { } previous && frame != previous + 1) problems.Add("native-callback-gap");
            previousFrame = frame;
            foreach (var subject in row.GetProperty("actors").EnumerateArray())
            {
                var reference = ResolveForm(subject.GetProperty("form").GetUInt32(), plugins);
                if (!actors.TryGetValue(reference, out var actor)) actors.Add(reference, actor = new(reference));
                actor.Base = ResolveForm(subject.GetProperty("base").GetUInt32(), plugins);
                if (subject.GetProperty("root").ValueKind != JsonValueKind.Object ||
                    subject.GetProperty("animations").ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"native-pose-owner-unobserved:{reference}");
                    continue;
                }
                actor.HierarchyQualified = true;
                var nodes = subject.GetProperty("nodes").EnumerateArray().ToArray();
                var byAddress = nodes.ToDictionary(node => Long(node, "address"));
                var transforms = new Dictionary<string, ObservedTransform>(StringComparer.Ordinal);
                var worlds = new Dictionary<string, ObservedTransform>(StringComparer.Ordinal);
                var ambiguous = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in nodes)
                {
                    var names = new List<string>();
                    var visited = new HashSet<long>();
                    var current = node;
                    while (true)
                    {
                        var address = Long(current, "address");
                        if (!visited.Add(address)) throw new InvalidDataException("Native node parent cycle.");
                        names.Add(Text(current, "name") ?? "<unnamed>");
                        if (!byAddress.TryGetValue(Long(current, "parent"), out current)) break;
                    }
                    names.Reverse();
                    var key = string.Join('\u001f', names);
                    if (ambiguous.Contains(key)) continue;
                    if (!transforms.TryAdd(key, NativeTransform(node.GetProperty("local"), units)))
                    {
                        transforms.Remove(key);
                        worlds.Remove(key);
                        ambiguous.Add(key);
                        problems.Add($"ambiguous-native-node:{reference}:{key}");
                    }
                    else worlds.Add(key, NativeTransform(node.GetProperty("world"), units));
                }
                var layers = new List<ObservedLayer>();
                var animations = subject.GetProperty("animations");
                var slots = animations.GetProperty("slots").EnumerateArray().ToArray();
                var selected = new HashSet<long>();
                for (var slot = 0; slot < slots.Length; slot++)
                    if (slots[slot].ValueKind == JsonValueKind.Object)
                    {
                        selected.Add(Long(slots[slot], "address"));
                        layers.Add(NativeLayer(slots[slot], slot));
                    }
                var completeLayers = true;
                if (animations.TryGetProperty("managerActiveSequences", out var manager))
                    foreach (var sequence in manager.EnumerateArray())
                        if (!selected.Contains(Long(sequence, "address")))
                        {
                            layers.Add(NativeLayer(sequence, -1));
                            completeLayers = false; // Retired visual layer has no qualified slot join yet.
                        }
                actor.Samples.Add(new(seconds, frame,
                    NativeTransform(subject.GetProperty("root").GetProperty("world"), units), transforms,
                    layers, completeLayers, worlds, RootBehindCamera: NativeRootBehindCamera(row, subject)));
            }
        }
        if (actors.Count == 0) throw new InvalidDataException("No native actor observations were present.");
        foreach (var actor in actors.Values)
        {
            FlagUnqualifiedPublication(actor);
            if (actor.PosePublicationUnqualifiedReason is { } reason) problems.Add($"{reason}:{actor.Reference}");
        }
        problems.Add("native-callback-is-before-world-update; compare source phases, not frame numbers");
        return new("retail", Path.GetFullPath(path), "native-callback", actors, problems.Order().ToArray());
    }

    internal static ObservedCapture ReadOpenNv(string path, string stage)
    {
        RequireFile(path);
        if (stage is not ("post-physics" or "post-draw")) throw new ArgumentException("Unknown observation phase.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var problems = new HashSet<string>(StringComparer.Ordinal);
        if (Text(root, "reason") != "completed" || Text(root, "error") is not null)
            problems.Add("opennv-capture-incomplete:" + Text(root, "reason"));
        var actor = new ObservedActor(Text(root, "reference") ?? throw new InvalidDataException("Missing actor identity."))
        { Base = Text(root, "baseSource") };
        var names = root.GetProperty("boneNames").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var parents = root.TryGetProperty("boneParents", out var parentValues)
            ? parentValues.EnumerateArray().Select(item => item.GetInt32()).ToArray() : null;
        if (parents is not null && parents.Length != names.Length) throw new InvalidDataException("Bone parent count differs.");
        actor.HierarchyQualified = parents is not null;
        if (parents is null) problems.Add("legacy-capture-has-no-parent-identity; unique-name joins only");
        var keys = new string[names.Length];
        for (var bone = 0; bone < names.Length; bone++)
        {
            var pathNames = new List<string>();
            var visited = new HashSet<int>();
            for (var current = bone; current >= 0; current = parents?[current] ?? -1)
            {
                if (current >= names.Length || !visited.Add(current)) throw new InvalidDataException("Invalid skeleton hierarchy.");
                pathNames.Add(names[current]);
            }
            pathNames.Reverse(); keys[bone] = string.Join('\u001f', pathNames);
        }
        foreach (var sample in root.GetProperty("samples").EnumerateArray())
        {
            if (Text(sample, "step") != stage) continue;
            if (Boolean(sample, "sceneTreePaused")) { problems.Add("paused-samples-excluded"); continue; }
            var values = Floats(sample.GetProperty("bones"));
            if (values.Length != names.Length * 12) throw new InvalidDataException("Bone matrix count differs.");
            var bones = new Dictionary<string, ObservedTransform>(StringComparer.Ordinal);
            for (var bone = 0; bone < names.Length; bone++)
                bones.Add(keys[bone], GodotTransform(values.AsSpan(bone * 12, 12)));
            Dictionary<string, ObservedTransform>? worlds = null;
            if (sample.TryGetProperty("globalBones", out var globals) && sample.TryGetProperty("skeletonWorld", out var skeletonWorld))
            {
                var globalValues = Floats(globals);
                if (globalValues.Length != values.Length) throw new InvalidDataException("Global bone count differs.");
                var worldMatrix = GodotMatrix(Floats(skeletonWorld));
                worlds = [];
                for (var bone = 0; bone < names.Length; bone++)
                    worlds.Add(keys[bone], Decompose(GodotMatrix(globalValues.AsSpan(bone * 12, 12)) * worldMatrix));
            }
            var layers = ReadOpenNvLayers(sample.GetProperty("owners"), out var qualified);
            var frame = Long(sample, stage == "post-draw" ? "drawFrame" : "physicsFrame");
            var physicsRate = OptionalNumber(root, "physicsTicksPerSecond");
            var usePhysicsClock = stage == "post-physics" && physicsRate is > 0 && double.IsFinite(physicsRate.Value);
            actor.Samples.Add(new(Number(sample, "milliseconds") / 1000,
                frame, GodotTransform(Floats(sample.GetProperty("body"))), bones, layers, qualified, worlds,
                usePhysicsClock ? frame / physicsRate!.Value : null,
                usePhysicsClock ? "fixed-physics-nominal; Engine.TimeScale unobserved" :
                    stage == "post-physics" ? "fixed-physics-unqualified" : "wall-clock", false,
                ReadOpenNvResources(sample.GetProperty("owners"))));
        }
        if (actor.Samples.Count < 2) throw new InvalidDataException("At least two unpaused samples are required.");
        return new("opennv", Path.GetFullPath(path), stage,
            new Dictionary<string, ObservedActor>(StringComparer.OrdinalIgnoreCase) { [actor.Reference] = actor }, problems.Order().ToArray());
    }

    private static IReadOnlyList<ObservedResource> ReadOpenNvResources(JsonElement owners)
    {
        // A nonpackage idle owner already reports actual resources but has no
        // qualified native slot/contribution join. Keep that evidence without
        // manufacturing slot 0/7 or making phase matching eligible.
        var result = new List<ObservedResource>();
        if (Text(owners, "owner") is not ("base" or "base+idle")) return result;
        if (!Boolean(owners, "baseDormant") && Text(owners, "baseResource") is { Length: > 0 } resource)
            result.Add(new(NormalizeResource(resource), "base", null));
        if (Text(owners, "owner") == "base+idle" && OptionalNumber(owners, "idleSequenceState") is { } state &&
            state != 0 && Text(owners, "idleResource") is { Length: > 0 } idle)
            result.Add(new(NormalizeResource(idle), "idle:" + (Text(owners, "idleOwner") ?? "unqualified"), checked((int)state)));
        return result;
    }

    private static bool? NativeRootBehindCamera(JsonElement row, JsonElement subject)
    {
        if (!row.TryGetProperty("cameraRawWorldToCam9C", out var matrix) || matrix.ValueKind != JsonValueKind.Array ||
            !row.TryGetProperty("cameraOrthographic", out var orthographic) || orthographic.ValueKind != JsonValueKind.False)
            return null;
        var values = Floats(matrix);
        var root = Floats(subject.GetProperty("root").GetProperty("world"));
        if (values.Length != 16 || root.Length != 13) return null;
        return (double)values[12] * root[9] + (double)values[13] * root[10] +
            (double)values[14] * root[11] + values[15] <= 0;
    }

    private static void FlagUnqualifiedPublication(ObservedActor actor)
    {
        var samples = actor.Samples;
        if (samples.Count < 2 || samples.Any(sample => sample.RootBehindCamera != true)) return;
        // Observed source clocks can advance while off-camera local bones stay
        // frozen. Root/NonAccum transport may still move. This is evidence of
        // an unjoined publication clock, not proof of the engine's culling rule.
        var first = samples[0];
        var keys = first.Bones.Keys.Where(key => key.Split('\u001f')[^1] is not ("Scene Root" or "Bip01 NonAccum")).ToArray();
        if (keys.Length == 0 || samples.Any(sample => sample.Bones.Count != first.Bones.Count ||
            keys.Any(key => !sample.Bones.TryGetValue(key, out var value) || value != first.Bones[key]))) return;
        var advancing = first.Layers.Any(layer => samples.Skip(1).Any(sample => sample.Layers.Any(other =>
            other.Slot == layer.Slot && other.Resource == layer.Resource && other.Seconds != layer.Seconds)));
        if (advancing) actor.PosePublicationUnqualifiedReason =
            "native-pose-publication-unqualified; frozen-local-pose-with-advancing-clock; root-behind-camera; culling-inferred";
    }

    private static IReadOnlyList<ObservedLayer> ReadOpenNvLayers(JsonElement owners, out bool qualified)
    {
        var result = new List<ObservedLayer>();
        qualified = false;
        if (owners.TryGetProperty("activeAnimationLayers", out var active) && active.ValueKind == JsonValueKind.Array)
        {
            qualified = true;
            foreach (var layer in active.EnumerateArray())
                result.Add(new((int)Long(layer, "slot"), NormalizeResource(Text(layer, "resource") ?? ""),
                    Number(layer, "sourceSeconds"), false, OptionalNumber(layer, "begin"), OptionalNumber(layer, "end"),
                    OptionalNumber(layer, "frequency") ?? 1, (int)Long(layer, "state"),
                    OptionalNumber(layer, "weight") ?? 1, OptionalNumber(layer, "ease")));
            return result;
        }
        if (Text(owners, "packageAnimation") is { Length: > 0 } package && Boolean(owners, "packageOwnsPose"))
        {
            // The legacy trace has one actual selected package clip. Its
            // missing idle/movement layering is a reported difference, not
            // fabricated into a matching reference stack by this adapter.
            var file = Path.GetFileNameWithoutExtension(package.Replace('\\', '/'));
            var slot = file.EndsWith("idle", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            result.Add(new(slot, NormalizeResource(package), Number(owners, "packageElapsedSeconds"), true,
                null, null, 1, 1, 1, 1));
            qualified = Text(owners, "owner") == "package";
        }
        if (!Boolean(owners, "baseDormant") && Text(owners, "baseResource") is { Length: > 0 } resource)
        {
            result.Add(new(0, NormalizeResource(resource), Number(owners, "baseSourceSeconds"), false,
                null, null, 1, 1, 1, 1));
            qualified = Text(owners, "owner") == "base";
        }
        return result;
    }

    private static ObservedLayer NativeLayer(JsonElement sequence, int slot) => new(slot,
        NormalizeResource(Text(sequence, "file") ?? ""), Number(sequence, "lastScaled"), false,
        Number(sequence, "begin"), Number(sequence, "end"), Number(sequence, "frequency"),
        (int)Long(sequence, "state"), Number(sequence, "weight"), Long(sequence, "state") == 1 ? 1 : null);

    internal static string NormalizeResource(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return normalized.StartsWith("meshes/", StringComparison.Ordinal) ? normalized[7..] : normalized;
    }

    private static string ResolveForm(uint form, string[] plugins)
    {
        var index = form >> 24;
        if (index == 255) return $"dynamic:{form:x8}";
        if (index >= plugins.Length) throw new InvalidDataException($"Native form {form:x8} has no declared load-order owner.");
        return $"{plugins[index]}:{form & 0xffffff:x6}";
    }

    private static ObservedTransform NativeTransform(JsonElement element, double units)
    {
        var a = Floats(element);
        if (a.Length != 13) throw new InvalidDataException("Native transform needs rotation9, position3, scale1.");
        Vector3 Convert(Vector3 value) => new(value.X, value.Z, -value.Y);
        var x = Convert(new(a[0], a[3], a[6])) * a[12];
        var y = Convert(new(a[2], a[5], a[8])) * a[12];
        var z = -Convert(new(a[1], a[4], a[7])) * a[12];
        var p = Convert(new(a[9], a[10], a[11])) * (float)units;
        return Decompose(new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1));
    }

    private static ObservedTransform GodotTransform(ReadOnlySpan<float> a) => Decompose(GodotMatrix(a));
    private static Matrix4x4 GodotMatrix(ReadOnlySpan<float> a)
    {
        if (a.Length != 12) throw new InvalidDataException("Godot transform needs basis columns9 and position3.");
        return new(a[0], a[1], a[2], 0, a[3], a[4], a[5], 0,
            a[6], a[7], a[8], 0, a[9], a[10], a[11], 1);
    }

    private static ObservedTransform Decompose(Matrix4x4 value)
    {
        if (!Matrix4x4.Decompose(value, out var scale, out var rotation, out var position) || rotation.LengthSquared() <= 0)
            throw new InvalidDataException("Observed transform cannot be decomposed.");
        return new(position, Quaternion.Normalize(rotation), scale);
    }

    private static void RequireFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumInputBytes)
            throw new InvalidDataException("Capture is absent, empty, or exceeds the 128 MiB input bound.");
    }
    private static float[] Floats(JsonElement value)
    {
        var result = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        if (result.Any(number => !float.IsFinite(number))) throw new InvalidDataException("Nonfinite observation value.");
        return result;
    }
    internal static string? Text(JsonElement value, string property) => value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    internal static double? OptionalNumber(JsonElement value, string property) => value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.Number ? field.GetDouble() : null;
    internal static double Number(JsonElement value, string property) => OptionalNumber(value, property)
        ?? throw new InvalidDataException("Missing numeric observation: " + property);
    internal static long Long(JsonElement value, string property) => value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.Number ? field.GetInt64() : throw new InvalidDataException("Missing integer observation: " + property);
    internal static bool Boolean(JsonElement value, string property) => value.TryGetProperty(property, out var field)
        && field.ValueKind == JsonValueKind.True;
}
