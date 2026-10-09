using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record RecordedInputPlugin(string Name, string Sha256);
internal sealed record RecordedInputBinding(string CheckpointSha256, string StateKey,
    IReadOnlyList<RecordedInputPlugin> Plugins);
internal sealed record RecordedInputHeader(string Schema, string Engine, RecordedInputBinding? Binding,
    string? RetailCheckpointSha256 = null, string Timing = "producer-monotonic",
    string? RetailExecutableSha256 = null);
internal sealed record RecordedInputStep(long Ordinal, long Microseconds, string StateKey, JsonElement Input);
internal sealed record RecordedInputFooter(long Inputs, long Microseconds, string Sha256, bool Complete, string? Error);

// A private input journal, not a save or a source of gameplay outcomes. Its
// checkpoint must already have been restored by the ordinary campaign owner.
internal sealed record RecordedInputTape(RecordedInputHeader Header, IReadOnlyList<RecordedInputStep> Inputs,
    RecordedInputFooter Footer)
{
    internal const string Schema = "opennv-recorded-input/v1";
    internal const string UnjoinedSchema = "opennv-unjoined-recorded-input/v1";
    internal bool Unjoined => Header.Schema == UnjoinedSchema;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static void ValidateBinding(RecordedInputBinding binding)
    {
        if (binding is null || !Hash(binding.CheckpointSha256) || string.IsNullOrWhiteSpace(binding.StateKey) ||
            binding.Plugins is null || binding.Plugins.Count == 0 || binding.Plugins.Any(plugin => plugin is null || string.IsNullOrWhiteSpace(plugin.Name) || !Hash(plugin.Sha256)) ||
            binding.Plugins.Select(plugin => plugin.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != binding.Plugins.Count)
            throw new InvalidDataException("Recorded input has no valid checkpoint, scene or ordered source identity.");
    }

    internal static void RequireBinding(RecordedInputBinding expected, RecordedInputBinding actual)
    {
        ValidateBinding(expected); ValidateBinding(actual);
        if (!expected.CheckpointSha256.Equals(actual.CheckpointSha256, StringComparison.OrdinalIgnoreCase) ||
            expected.StateKey != actual.StateKey || expected.Plugins.Count != actual.Plugins.Count ||
            expected.Plugins.Where((plugin, index) =>
                !plugin.Name.Equals(actual.Plugins[index].Name, StringComparison.OrdinalIgnoreCase) ||
                !plugin.Sha256.Equals(actual.Plugins[index].Sha256, StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException("Input replay differs from its loaded checkpoint, scene or ordered source stack.");
    }

    internal static void ValidateHeader(RecordedInputHeader header)
    {
        if (header is null) throw new InvalidDataException("Recorded input header is absent.");
        if (header.Schema == UnjoinedSchema)
        {
            if (header.Engine != "retail" || header.Binding is not null || header.Timing != "receipt-observed" ||
                !Hash(header.RetailExecutableSha256) || header.RetailCheckpointSha256 is not null && !Hash(header.RetailCheckpointSha256))
                throw new InvalidDataException("An unjoined diagnostic must retain its actual retail source and cannot invent an OpenNV binding.");
            return;
        }
        ValidateBinding(header.Binding ?? throw new InvalidDataException("A bound input segment has no actual OpenNV checkpoint binding."));
        if (header.Schema != Schema || header.Engine is not ("retail" or "opennv") ||
            header.Timing is not ("producer-monotonic" or "receipt-observed") ||
            header.RetailExecutableSha256 is not null && !Hash(header.RetailExecutableSha256) ||
            header.Engine == "retail" && !Hash(header.RetailCheckpointSha256))
            throw new InvalidDataException("Recorded input header or retail checkpoint identity is invalid.");
    }

    internal static void ValidateInput(JsonElement input)
    {
        try { ValidateInputFields(input); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Recorded input has an absent or invalid field.", error); }
    }

    private static void ValidateInputFields(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Recorded input must be an object.");
        var operation = input.GetProperty("op").GetString();
        string[] allowed;
        switch (operation)
        {
            case "key":
                allowed = ["op", "key", "pressed", "leaseMilliseconds"];
                var key = input.GetProperty("key").GetString();
                if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || !key.All(char.IsAsciiLetterOrDigit))
                    throw new InvalidDataException("Recorded physical key is invalid.");
                _ = input.GetProperty("pressed").GetBoolean();
                if (input.GetProperty("leaseMilliseconds").GetInt32() is < 20 or > 1000)
                    throw new InvalidDataException("Recorded key lease is outside its native input contract.");
                break;
            case "mouse":
                allowed = ["op", "button", "pressed", "leaseMilliseconds"];
                if (input.GetProperty("button").GetString() is not ("Left" or "Right" or "Middle" or "Xbutton1" or "Xbutton2"))
                    throw new InvalidDataException("Recorded mouse button is invalid.");
                _ = input.GetProperty("pressed").GetBoolean();
                if (input.GetProperty("leaseMilliseconds").GetInt32() is < 20 or > 1000)
                    throw new InvalidDataException("Recorded mouse lease is outside its native input contract.");
                break;
            case "look":
                allowed = ["op", "dx", "dy"];
                foreach (var name in new[] { "dx", "dy" })
                    if (!float.IsFinite(input.GetProperty(name).GetSingle()))
                        throw new InvalidDataException("Recorded mouse displacement is not finite.");
                break;
            case "button":
                allowed = ["op", "path", "text"];
                if (string.IsNullOrWhiteSpace(input.GetProperty("path").GetString()) ||
                    string.IsNullOrWhiteSpace(input.GetProperty("text").GetString()))
                    throw new InvalidDataException("Recorded button needs its observed path and caption.");
                break;
            case "pointer":
                allowed = ["op", "x", "y", "button", "pressed"];
                foreach (var name in new[] { "x", "y" })
                    if (!float.IsFinite(input.GetProperty(name).GetSingle()))
                        throw new InvalidDataException("Recorded pointer coordinate is not finite.");
                if (input.TryGetProperty("button", out var button) &&
                    (button.GetString() is not ("Left" or "Right" or "Middle" or "WheelUp" or "WheelDown") ||
                        !input.TryGetProperty("pressed", out var pressed) || pressed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                    throw new InvalidDataException("Recorded pointer button is invalid.");
                if (!input.TryGetProperty("button", out _) && input.TryGetProperty("pressed", out _))
                    throw new InvalidDataException("Recorded pointer press has no button.");
                break;
            case "text":
                allowed = ["op", "text"];
                var text = input.GetProperty("text").GetString() ?? throw new InvalidDataException("Recorded text is absent.");
                if (text.EnumerateRunes().Any(Rune.IsControl)) throw new InvalidDataException("Recorded text contains a control character.");
                break;
            default:
                throw new InvalidDataException("Input tapes admit ordinary input only; state edits and console commands are not replayable.");
        }
        var names = input.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(name => !allowed.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("Recorded input contains duplicate or unknown fields.");
    }

    internal static RecordedInputTape Read(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var first = reader.ReadLine() ?? throw new InvalidDataException("Recorded input is empty.");
        using (var value = JsonDocument.Parse(first)) RequireUniqueFields(value.RootElement);
        var header = JsonSerializer.Deserialize<RecordedInputHeader>(first, Json) ?? throw new InvalidDataException("Recorded header is absent.");
        ValidateHeader(header); AppendHash(digest, first);
        var inputs = new List<RecordedInputStep>();
        RecordedInputFooter? footer = null;
        while (reader.ReadLine() is { } line)
        {
            if (footer is not null) throw new InvalidDataException("Recorded input has bytes after its footer.");
            using var value = JsonDocument.Parse(line);
            RequireUniqueFields(value.RootElement);
            if (value.RootElement.TryGetProperty("complete", out _))
                footer = JsonSerializer.Deserialize<RecordedInputFooter>(line, Json);
            else
            {
                var step = JsonSerializer.Deserialize<RecordedInputStep>(line, Json) ?? throw new InvalidDataException("Recorded step is absent.");
                if (step.Ordinal != inputs.Count + 1 || step.Microseconds < (inputs.LastOrDefault()?.Microseconds ?? 0) ||
                    string.IsNullOrWhiteSpace(step.StateKey)) throw new InvalidDataException("Recorded input lost its ordinal, clock or scene.");
                ValidateInput(step.Input); inputs.Add(step); AppendHash(digest, line);
            }
        }
        if (footer is null || !footer.Complete || footer.Error is not null || footer.Inputs != inputs.Count ||
            footer.Microseconds < (inputs.LastOrDefault()?.Microseconds ?? 0) ||
            !Convert.ToHexString(digest.GetHashAndReset()).Equals(footer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recorded input is incomplete, failed, truncated or corrupt.");
        return new(header, inputs.AsReadOnly(), footer);
    }

    internal static void RequireUniqueFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Input journal contains duplicate JSON fields.");
                RequireUniqueFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RequireUniqueFields(item);
    }

    internal static void AppendHash(IncrementalHash digest, string line) => digest.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

internal sealed class RecordedInputWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _inputs, _lastMicroseconds;
    private bool _finished;
    internal RecordedInputWriter(string path, RecordedInputHeader header)
    {
        try
        {
            RecordedInputTape.ValidateHeader(header);
            _writer = new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            { AutoFlush = true };
            try { WriteHashed(header); }
            catch { _writer.Dispose(); throw; }
        }
        catch { _digest.Dispose(); throw; }
    }
    private void WriteHashed<T>(T value)
    {
        var line = JsonSerializer.Serialize(value, RecordedInputTape.Json);
        _writer.WriteLine(line); RecordedInputTape.AppendHash(_digest, line);
    }
    internal void Append(long microseconds, string stateKey, JsonElement input)
    {
        if (_finished || microseconds < _lastMicroseconds || string.IsNullOrWhiteSpace(stateKey))
            throw new InvalidDataException("Input recording clock, scene or lifecycle is invalid.");
        RecordedInputTape.ValidateInput(input);
        WriteHashed(new RecordedInputStep(++_inputs, microseconds, stateKey, input));
        _lastMicroseconds = microseconds;
    }
    internal void Finish(long microseconds, string? error = null)
    {
        if (_finished) return;
        if (microseconds < _lastMicroseconds) throw new InvalidDataException("Input recording footer precedes delivered input.");
        _writer.WriteLine(JsonSerializer.Serialize(new RecordedInputFooter(_inputs, microseconds,
            Convert.ToHexString(_digest.GetHashAndReset()), error is null, error), RecordedInputTape.Json));
        _finished = true; _writer.Dispose(); _digest.Dispose();
    }
    public void Dispose()
    {
        if (!_finished) Finish(_lastMicroseconds, "Recording ended without an explicit completed segment.");
    }
}
