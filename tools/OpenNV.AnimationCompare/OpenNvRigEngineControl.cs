using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.AnimationCompare;

internal sealed record OpenNvRigPlugin(string Name, string Sha256);
internal sealed record OpenNvRigBinding(string Reference, string SourceForm, string BindingHash, string PoseOperation,
    string? EntryReference = null, string? IdleForm = null, float? ExpectedVerticalFov = null,
    string? TargetAnchorReference = null, bool? Restrained = null);
internal sealed record OpenNvRigConfiguration(string HarnessDirectory, int ProcessId, string OutputDirectory,
    IReadOnlyList<OpenNvRigPlugin> Plugins, IReadOnlyDictionary<string, OpenNvRigBinding> Bindings);

// No command executes until RunAsync/ExecuteAsync is explicitly invoked by the
// operator. This adapter never starts/restarts games or makes source fixtures.
internal sealed class OpenNvRigEngineControl : IParityRigEngineControl, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OpenNvRigConfiguration _configuration;
    private readonly RigFileCommandChannel _channel;
    private readonly SemaphoreSlim _serial = new(1);
    private readonly Dictionary<string, string> _saved = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _requests = [];
    private readonly string _sourceHash;
    private bool _sourceVerified;
    private JsonElement _sourceObservation;
    public string Engine => "opennv";

    internal OpenNvRigEngineControl(OpenNvRigConfiguration configuration)
    {
        if (!Path.IsPathFullyQualified(configuration.OutputDirectory)) throw new ArgumentException("Rig output must be an absolute private path.");
        _sourceHash = SourceStackHash(configuration.Plugins);
        _configuration = configuration;
        Directory.CreateDirectory(configuration.OutputDirectory);
        _channel = new(configuration.HarnessDirectory, configuration.ProcessId);
    }

    internal static string SourceStackHash(IReadOnlyList<OpenNvRigPlugin> plugins)
    {
        if (plugins.Count == 0 || plugins.Any(plugin => string.IsNullOrWhiteSpace(plugin.Name) || plugin.Name.Contains('\n') ||
            plugin.Sha256.Length != 64 || !plugin.Sha256.All(char.IsAsciiHexDigit)) ||
            plugins.Select(plugin => plugin.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plugins.Count)
            throw new ArgumentException("Rig source requires an ordered unique plugin/SHA-256 manifest.");
        var canonical = "opennv-animation-rig-source-stack/v1\n" + string.Concat(plugins.Select((plugin, index) =>
            $"{index}\n{plugin.Name.ToLowerInvariant()}\n{plugin.Sha256.ToLowerInvariant()}\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public async Task<ParityRigReceipt> ExecuteAsync(ParityRigRequest request, CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            RequireRequest(request);
            var evidence = new List<ParityRigEvidence>();
            ParityRigReceipt result;
            try
            {
                await VerifySource(request, evidence, cancellationToken);
                switch (request.Operation)
                {
                    case "checkpoint.save":
                    case "checkpoint.load": await Checkpoint(request, evidence, cancellationToken); break;
                    case "rig.prepare": await Prepare(request, evidence, cancellationToken); break;
                    case "rig.action": await Action(request, evidence, cancellationToken); break;
                    default: throw new NotSupportedException($"No OpenNV rig adapter operation {request.Operation}.");
                }
                result = Receipt(request, evidence);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException or
                NotSupportedException or OperationCanceledException or JsonException)
            {
                result = new(request.Run, request.Request, Engine, request.Operation, _sourceHash,
                    exception is OperationCanceledException ? "cancelled" : "failed", exception.Message, evidence);
            }
            // A durable terminal journal keeps failed artifacts observable even
            // when the caller stops before creating its aggregate report.
            Write(request, "receipt", result);
            return result;
        }
        finally { _serial.Release(); }
    }

    public async Task<ParityRigReceipt> QuiesceAsync(ParityRigRequest request, CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            RequireRequest(request);
            // Resolve a timed-out published prefix before issuing anything else.
            // A genuine rejected prefix is terminal and safe to release; a
            // still-pending prefix is never retried or silently discarded.
            try { await _channel.SettleAsync(cancellationToken); }
            catch (NotSupportedException) { }
            while (true)
            {
                var (_, state) = await Control(new { op = "rig.quiesce" }, cancellationToken);
                if (state.GetProperty("disposition").GetString() == "completed")
                    return Receipt(request, [Write(request, "quiescence", state)]);
                await Task.Delay(25, cancellationToken);
            }
        }
        finally { _serial.Release(); }
    }

    private void RequireRequest(ParityRigRequest request)
    {
        if (request.SourceStackHash != _sourceHash || request.Run == Guid.Empty || request.Request == Guid.Empty || !_requests.Add(request.Request))
            throw new InvalidDataException("Rig source/request identity is mismatched or reused.");
    }

    private async Task VerifySource(ParityRigRequest request, List<ParityRigEvidence> evidence, CancellationToken token, string lane = "source")
    {
        if (_sourceVerified) { evidence.Add(Write(request, lane, _sourceObservation)); return; }
        var (_, source) = await Control(new { op = "rig.source" }, token);
        var actual = source.GetProperty("plugins").EnumerateArray().Select((plugin, index) =>
        {
            if (plugin.GetProperty("loadOrderIndex").GetInt32() != index) throw new InvalidDataException("Rig plugin order is not canonical.");
            return new OpenNvRigPlugin(plugin.GetProperty("name").GetString()!, plugin.GetProperty("sha256").GetString()!);
        }).ToArray();
        if (SourceStackHash(actual) != request.SourceStackHash)
            throw new InvalidDataException("Actual OpenNV ordered source stack differs from the rig catalog.");
        _sourceObservation = source;
        evidence.Add(Write(request, lane, source)); _sourceVerified = true;
    }

    private async Task Checkpoint(ParityRigRequest request, List<ParityRigEvidence> evidence, CancellationToken token)
    {
        if (!Guid.TryParseExact(request.Checkpoint, "N", out _)) throw new ArgumentException("OpenNV checkpoint requires a GUID identity.");
        if (request.Operation == "checkpoint.save" && _saved.ContainsKey(request.Checkpoint!))
            throw new InvalidDataException("Rig will not overwrite an existing checkpoint identity.");
        if (request.Operation == "checkpoint.load" && !_saved.ContainsKey(request.Checkpoint!))
            throw new InvalidDataException("Rig restore must use this control's actually confirmed fresh checkpoint.");
        var (entered, receipt) = await _channel.SendAsync(new { op = request.Operation, id = request.Checkpoint, pauseAfterLoad = false }, token);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var state = _channel.ReadState();
            if (state.TryGetProperty("checkpoint", out var checkpoint) && checkpoint.ValueKind == JsonValueKind.Object &&
                checkpoint.TryGetProperty("request", out var checkpointRequest) && checkpointRequest.GetInt64() == entered)
            {
                if (checkpoint.GetProperty("operation").GetString() != request.Operation)
                    throw new InvalidDataException("Checkpoint receipt belongs to another operation.");
                if (checkpoint.TryGetProperty("disposition", out var disposition))
                {
                    if (disposition.GetString() == "pending") { await Task.Delay(25, token); continue; }
                    if (disposition.GetString() != "completed")
                    {
                        evidence.Add(Write(request, "checkpoint-failed", new { entered, receipt, checkpoint }));
                        throw new InvalidDataException("Shared checkpoint preparation failed: " + checkpoint.GetProperty("error").GetString());
                    }
                }
                if (state.GetProperty("checkpointTransitioning").GetBoolean()) { await Task.Delay(25, token); continue; }
                if (checkpoint.GetProperty("slot").GetProperty("id").GetString() != request.Checkpoint)
                    throw new InvalidDataException("Checkpoint committed another slot identity.");
                if (request.Operation == "checkpoint.load" &&
                    (!state.TryGetProperty("checkpointRestored", out var restored) || restored.ValueKind != JsonValueKind.Object ||
                        restored.GetProperty("id").GetString() != request.Checkpoint))
                { await Task.Delay(25, token); continue; }
                var path = checkpoint.GetProperty("slot").GetProperty("path").GetString()!;
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new IOException("Confirmed rig checkpoint has no actual save file.");
                if (request.Operation == "checkpoint.save") _saved.Add(request.Checkpoint!, path);
                else if (_saved[request.Checkpoint!] != path) throw new InvalidDataException("Rig restored another checkpoint path.");
                evidence.Add(Write(request, "checkpoint", new
                {
                    entered,
                    receipt,
                    checkpoint,
                    restored = state.GetProperty("checkpointRestored"),
                    path,
                    sha256 = await HashFile(path, token),
                    confirmed = true
                }));
                if (request.Operation == "checkpoint.load")
                {
                    _sourceVerified = false;
                    await VerifySource(request, evidence, token, "source-restored");
                }
                return;
            }
            await Task.Delay(25, token);
        }
    }

    private OpenNvRigBinding Binding(ParityRigRequest request)
    {
        var item = request.Case ?? throw new ArgumentException("Rig action has no case.");
        if (!_configuration.Bindings.TryGetValue(item.Id, out var binding) || binding.Reference != item.Reference ||
            binding.SourceForm != item.SourceForm || binding.BindingHash != item.BindingHash || binding.EntryReference != item.EntryReference ||
            binding.TargetAnchorReference != item.TargetAnchorReference || binding.Restrained != item.Restrained ||
            binding.PoseOperation is not ("actor.pose" or "object.pose"))
            throw new NotSupportedException("Rig case has no exact operator-admitted source/reference/pose binding.");
        return binding;
    }

    private async Task Prepare(ParityRigRequest request, List<ParityRigEvidence> evidence, CancellationToken token)
    {
        var binding = Binding(request);
        await Control(new
        {
            op = "rig.prepare",
            caseId = request.Case!.Id,
            reference = binding.Reference,
            sourceForm = binding.SourceForm,
            entryReference = binding.EntryReference,
            targetAnchorReference = binding.TargetAnchorReference,
            restrained = binding.Restrained
        }, token);
        while (true)
        {
            var (_, state) = await Control(new { op = "rig.status", caseId = request.Case!.Id }, token);
            if (state.GetProperty("disposition").GetString() == "prepared")
            {
                ValidatePrepared(request, binding, state);
                evidence.Add(Write(request, "camera", state)); return;
            }
            await Task.Delay(25, token);
        }
    }

    private static void ValidatePrepared(ParityRigRequest request, OpenNvRigBinding binding, JsonElement state)
    {
        if (state.GetProperty("reference").GetString() != binding.Reference || state.GetProperty("baseSource").GetString() != binding.SourceForm)
            throw new InvalidDataException("Actual prepared actor/object differs from the source rig case.");
        if (state.GetProperty("targetAnchorReference").GetString() != binding.TargetAnchorReference ||
            binding.Restrained is { } restrained && state.GetProperty("restrained").GetBoolean() != restrained)
            throw new InvalidDataException("Actual rig target anchor/restraint differs from its explicit preparation binding.");
        var viewport = state.GetProperty("viewport");
        if (viewport.GetProperty("width").GetDouble() != request.Width || viewport.GetProperty("height").GetDouble() != request.Height)
            throw new InvalidDataException($"Actual rig viewport differs from requested {request.Width}x{request.Height}.");
        if (binding.ExpectedVerticalFov is { } fov && state.GetProperty("fov").GetSingle() != fov)
            throw new InvalidDataException("Actual rig camera FOV differs from its explicit reference setting.");
        foreach (var property in new[] { "cameraWorld", "targetWorld" })
            if (state.GetProperty(property).GetArrayLength() != 12 || state.GetProperty(property).EnumerateArray().Any(value => !double.IsFinite(value.GetDouble())))
                throw new InvalidDataException("Rig camera/target matrix is missing or nonfinite.");
    }

    private async Task Action(ParityRigRequest request, List<ParityRigEvidence> evidence, CancellationToken token)
    {
        var binding = Binding(request); var action = request.Action ?? throw new ArgumentException("Rig has no action.");
        if (action.Operation is not ("observe" or "object.playgroup" or "actor.playidle"))
            throw new NotSupportedException("This adapter cannot yet prove the requested source activation/action completion.");
        if (action.Operation == "actor.playidle" && binding.IdleForm is null)
            throw new NotSupportedException("Actor IDLE requires its exact catalog-resolved source form.");
        var (_, before) = await Control(new { op = "rig.status", caseId = request.Case!.Id }, token);
        ValidatePrepared(request, binding, before);
        var (capture, _) = await _channel.SendAsync(new
        {
            op = binding.PoseOperation,
            reference = binding.Reference,
            seconds = action.ObservationDuration.TotalSeconds
        }, token);
        var (entered, admitted) = await Control(new
        {
            op = "rig.action",
            caseId = request.Case!.Id,
            action = action.Operation,
            argument = action.Argument
        }, token);
        if (admitted.GetProperty("disposition").GetString() != "admitted") throw new InvalidDataException("Rig action was not admitted by its actual owner.");
        var suffix = binding.PoseOperation == "actor.pose" ? ".pose.json" : ".object-pose.json";
        var path = Path.Combine(_channel.DirectoryPath, $"{capture:D10}{suffix}");
        while (!File.Exists(path)) { token.ThrowIfCancellationRequested(); _ = _channel.ReadState(); await Task.Delay(25, token); }
        var pose = RigFileCommandChannel.ReadJson(path, 64 * 1024 * 1024);
        evidence.Add(await Retain(request, "pose", path, pose.GetProperty("schema").GetString()!, token));
        var completedReason = binding.PoseOperation == "actor.pose" ? "completed" : "duration-complete";
        if (pose.GetProperty("reference").GetString() != binding.Reference || pose.GetProperty("reason").GetString() != completedReason ||
            pose.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null ||
            pose.GetProperty("physicsSamples").GetInt32() == 0 || pose.GetProperty("drawSamples").GetInt32() < 2)
            throw new InvalidDataException("Actual rig pose capture failed, lost membership, or lacks temporal samples.");
        if (!ObservedAction(action, binding, pose)) throw new InvalidDataException("Requested source action never appeared in actual pose-owner observations.");
        foreach (var sample in pose.GetProperty("samples").EnumerateArray())
            if (sample.TryGetProperty("owners", out var owners))
                foreach (var name in new[] { "animationError", "sourceError", "packageError", "combatError" })
                    if (owners.TryGetProperty(name, out var ownerError) && ownerError.ValueKind != JsonValueKind.Null)
                        throw new InvalidDataException($"Actual rig pose owner failed: {name}={ownerError}.");
        var (_, after) = await Control(new { op = "rig.status", caseId = request.Case!.Id }, token);
        ValidatePrepared(request, binding, after);
        if (before.GetProperty("nodeInstance").GetUInt64() != after.GetProperty("nodeInstance").GetUInt64())
            throw new InvalidDataException("Rig target instance changed during the action.");
        evidence.Add(Write(request, "action", new { entered, capture, admitted, operation = action.Operation, observed = true }));
        evidence.Add(Write(request, "camera", new { before, after, pairedCameraTransformJoin = "unverified; comparison reader owns the native join" }));
        var samples = pose.GetProperty("samples").EnumerateArray().Where(sample =>
            (sample.TryGetProperty("phase", out var phase) ? phase : sample.GetProperty("step")).GetString() == "post-draw").ToArray();
        var first = samples[0]; var last = samples[^1]; var frequency = pose.GetProperty("monotonicFrequency").GetDouble();
        var seconds = (last.GetProperty("ticks").GetInt64() - first.GetProperty("ticks").GetInt64()) / frequency;
        var fps = seconds > 0 ? (last.GetProperty("drawFrame").GetUInt64() - first.GetProperty("drawFrame").GetUInt64()) / seconds : 0;
        evidence.Add(Write(request, "performance", new
        {
            actualAverageDrawFramesPerSecond = fps,
            targetFramesPerSecond = request.TargetFramesPerSecond,
            meetsAverageTarget = fps >= request.TargetFramesPerSecond,
            boundary = "Average draw counter rate under bounded pose capture; no percentile or final-pixel cadence pass."
        }));
    }

    private static bool ObservedAction(ParityRigAction action, OpenNvRigBinding binding, JsonElement pose)
    {
        if (action.Operation == "observe") return true;
        foreach (var sample in pose.GetProperty("samples").EnumerateArray())
        {
            if (action.Operation == "object.playgroup" && sample.TryGetProperty("controllers", out var controllers) &&
                controllers.EnumerateArray().Any(controller => controller.GetProperty("owner").GetProperty("active").GetString() == action.Argument)) return true;
            if (action.Operation == "actor.playidle" && sample.TryGetProperty("owners", out var owners))
                foreach (var name in new[] { "idle", "scriptIdle" })
                    if (owners.TryGetProperty(name, out var idle) && Form(idle) == binding.IdleForm) return true;
        }
        return false;
    }

    private static string? Form(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() :
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("ownerPlugin", out var plugin) && value.TryGetProperty("objectId", out var id)
            ? $"{plugin.GetString()}:{id.GetUInt32():x6}" : null;

    private async Task<(long Request, JsonElement State)> Control(object command, CancellationToken token)
    {
        var (request, _) = await _channel.SendAsync(command, token);
        var path = Path.Combine(_channel.DirectoryPath, $"{request:D10}.rig.json");
        while (!File.Exists(path)) { token.ThrowIfCancellationRequested(); _ = _channel.ReadState(); await Task.Delay(20, token); }
        var artifact = RigFileCommandChannel.ReadJson(path, 4 * 1024 * 1024);
        if (artifact.GetProperty("request").GetInt64() != request) throw new InvalidDataException("Rig control artifact belongs to another request.");
        return (request, artifact.GetProperty("state").Clone());
    }

    private ParityRigReceipt Receipt(ParityRigRequest request, IReadOnlyList<ParityRigEvidence> evidence) =>
        new(request.Run, request.Request, Engine, request.Operation, _sourceHash, "completed", null, evidence);

    private ParityRigEvidence Write(ParityRigRequest request, string lane, object value)
    {
        var path = Path.Combine(_configuration.OutputDirectory, $"{request.Run:N}-{request.Request:N}-{lane}.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = "opennv-controlled-rig-observation/v1", request, lane, value }, Json);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { stream.Write(bytes); stream.Flush(true); }
        return new(lane, path, "opennv-controlled-rig-observation/v1", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private async Task<ParityRigEvidence> Retain(ParityRigRequest request, string lane, string source, string schema, CancellationToken token)
    {
        var path = Path.Combine(_configuration.OutputDirectory, $"{request.Run:N}-{request.Request:N}-{lane}.json");
        var complete = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { await input.CopyToAsync(output, token); output.Flush(true); }
            var hash = await HashFile(path, token);
            if (hash != await HashFile(source, token)) throw new IOException("Retained rig pose differs from its actual capture.");
            complete = true;
            File.Delete(source); // Keep one durable requested artifact, not duplicate captures.
            return new(lane, path, schema, hash);
        }
        finally { if (!complete && File.Exists(path)) File.Delete(path); }
    }

    private static async Task<string> HashFile(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    public void Dispose() { _channel.Dispose(); _serial.Dispose(); }
}
