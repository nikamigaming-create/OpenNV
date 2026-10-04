using System.Text.Json;
using OpenNV.Runtime.Diagnostics.Parity;

internal static class RecordedInputProbe
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "opennv-input-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var binding = new RecordedInputBinding(new string('a', 64), "cell:source",
            [new("Source.esm", new string('b', 64)), new("Patch.esp", new string('c', 64))]);
        JsonElement Key(bool pressed) => JsonSerializer.SerializeToElement(new { op = "key", key = "W", pressed, leaseMilliseconds = 200 });
        try
        {
            using (var writer = new RecordedInputWriter(path, new(RecordedInputTape.Schema, "retail", binding, new string('d', 64))))
            {
                writer.Append(0, binding.StateKey, Key(true));
                writer.Append(100_000, binding.StateKey, Key(false));
                writer.Finish(150_000);
            }
            var tape = RecordedInputTape.Read(path);
            var delivered = new List<bool>(); var releases = 0;
            var playback = new RecordedInputPlayback(tape, binding, 10_000);
            void Advance(long time, string scene = "cell:source") => playback.Advance(time, () => scene,
                input => delivered.Add(input.GetProperty("pressed").GetBoolean()), () => ++releases);
            Advance(0); Advance(99_999);
            Require(delivered.SequenceEqual([true]) && playback.Cursor == 1 && playback.Active, "An input ran before its recorded deadline.");
            Advance(100_000); Advance(150_000); Advance(200_000);
            Require(delivered.SequenceEqual([true, false]) && playback.Complete && releases == 1,
                "Playback lost a key edge, repeated a completed segment or failed to release input.");

            foreach (var changed in new[] { binding with { CheckpointSha256 = new string('e', 64) },
                binding with { StateKey = "cell:wrong" }, binding with { Plugins = binding.Plugins.Reverse().ToArray() } })
                Reject(() => new RecordedInputPlayback(tape, changed, 10_000));
            playback = new(tape, binding, 10_000); delivered.Clear(); releases = 0;
            Advance(0, "cell:wrong");
            Require(playback.Error?.Contains("expected scene", StringComparison.Ordinal) == true && delivered.Count == 0 && releases == 1,
                "Scene divergence delivered an input or concealed its failure.");
            playback = new(tape, binding, 10_000); releases = 0;
            Advance(10_001);
            Require(playback.Error?.Contains("late", StringComparison.Ordinal) == true && releases == 1,
                "A delayed runner silently compressed its input history.");
            playback = new(tape, binding, 10_000); releases = 0;
            playback.Advance(0, () => binding.StateKey, _ => throw new InvalidOperationException("Observed UI no longer exists."), () => ++releases);
            Require(playback.Error == "Observed UI no longer exists." && playback.Cursor == 0 && releases == 1,
                "Rejected delivery consumed an input or left a held control.");

            foreach (var operation in new[] { "console", "physics.sever", "checkpoint.load", "bot", "stage" })
                Reject(() => RecordedInputTape.ValidateInput(JsonSerializer.SerializeToElement(new { op = operation })));
            Reject(() => RecordedInputTape.ValidateInput(JsonDocument.Parse("{\"op\":\"look\",\"dx\":0,\"dy\":0,\"quest\":50}").RootElement));
            Reject(() => RecordedInputTape.ValidateInput(JsonDocument.Parse("{\"op\":\"look\",\"dx\":0,\"dx\":1,\"dy\":0}").RootElement));
            var original = File.ReadAllText(path);
            File.WriteAllText(path, original.Replace("\"complete\":true", "\"complete\":false,\"complete\":true", StringComparison.Ordinal));
            Reject(() => RecordedInputTape.Read(path));
            File.WriteAllText(path, original.Replace("\"binding\":{", "\"binding\":null,\"binding\":{", StringComparison.Ordinal));
            Reject(() => RecordedInputTape.Read(path));
            Reject(() => RecordedInputTape.ValidateBinding(binding with { Plugins = null! }));
            Reject(() => RecordedInputTape.ValidateInput(JsonSerializer.SerializeToElement(new { op = "pointer", x = 0, y = 0, pressed = true })));
            File.WriteAllText(path, original.Replace("\"key\":\"W\"", "\"key\":\"S\"", StringComparison.Ordinal));
            Reject(() => RecordedInputTape.Read(path));
            File.WriteAllText(path, original[..original.LastIndexOf('{')]); Reject(() => RecordedInputTape.Read(path));
            File.Delete(path);
            using (var abandoned = new RecordedInputWriter(path, new(RecordedInputTape.Schema, "opennv", binding)))
                abandoned.Append(0, binding.StateKey, Key(true));
            Reject(() => RecordedInputTape.Read(path));
            Console.WriteLine("OPENNV_RECORDED_INPUT_CONTRACT_OK checkpointBound=true orderedSourceBound=true clockAndEdges=true " +
                "rejectionStops=true release=true stateEditsRefused=true truncatedAndCorruptRefused=true abandonedRefused=true framesRecorded=false");
        }
        finally { File.Delete(path); }
        Tolerances();
    }

    private static void Tolerances()
    {
        var left = new ParityTelemetryFrame(ParityEngine.Retail, 1, 0, 0, 1, "matched",
            [ParityTelemetryField.Float64(ParityCategory.Camera, 1, 1), ParityTelemetryField.UInt64(ParityCategory.Quest, 2, 70)]);
        var right = left with { Engine = ParityEngine.OpenNv, Fields =
            [ParityTelemetryField.Float64(ParityCategory.Camera, 1, 1.001), ParityTelemetryField.UInt64(ParityCategory.Quest, 2, 70)] };
        var comparison = ParityFrameComparator.Compare(left, right);
        var policy = new ParityTolerancePolicy([new(ParityCategory.Camera, 1, .002)]);
        var assessed = policy.Assess(comparison);
        Require(assessed.WithinDeclaredTolerances && !assessed.ExactBytesEqual && assessed.Deltas.Count == 1 &&
            assessed.Deltas[0].Delta.RetailHex != assessed.Deltas[0].Delta.OpenNvHex, "A declared tolerance erased exact source deltas.");
        Require(!new ParityTolerancePolicy([]).Assess(comparison).WithinDeclaredTolerances, "Undeclared numeric drift passed.");
        right = right with { Fields = [right.Fields[0], ParityTelemetryField.UInt64(ParityCategory.Quest, 2, 71)] };
        Require(!new ParityTolerancePolicy([new(ParityCategory.Camera, 1, 10), new(ParityCategory.Quest, 2, 100)])
            .Assess(ParityFrameComparator.Compare(left, right)).WithinDeclaredTolerances, "A numeric tolerance hid an integer quest outcome.");
        Require(!policy.Assess(ParityFrameComparator.Compare(left, right with { EventOrdinal = 0 })).Comparable,
            "Unobserved event alignment became comparable.");
        Require(!policy.Assess(ParityFrameComparator.Compare(left, right with { Fields = [] })).WithinDeclaredTolerances,
            "Missing runtime fields passed a tolerance.");
        Reject(() => new ParityTolerancePolicy([new(ParityCategory.Camera, 1, double.NaN)]));
        Reject(() => new ParityTolerancePolicy([new(ParityCategory.Camera, 1, 1), new(ParityCategory.Camera, 1, 2)]));
        Console.WriteLine("OPENNV_PARITY_TOLERANCE_CONTRACT_OK exactBytesRetained=true explicitFloatRules=true " +
            "integerOutcomesExact=true missingFieldsFail=true unknownEventsFail=true");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or JsonException) { return; }
        throw new InvalidDataException("Invalid input replay or tolerance was accepted.");
    }
}
