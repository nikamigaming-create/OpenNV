using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNV.AnimationCompare;

internal sealed record ExternalRigConfiguration(string Engine, string Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, int MaximumRequestBytes = 1024 * 1024,
    int MaximumReceiptBytes = 4 * 1024 * 1024, int MaximumDiagnosticBytes = 1024 * 1024);

// Public transport only. The explicitly supplied private helper owns native
// control; this tool contains no addresses, injection or process discovery.
internal sealed class ExternalRigEngineControl : IParityRigEngineControl, IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };
    private readonly ExternalRigConfiguration _configuration;
    private readonly SemaphoreSlim _serial = new(1);
    private readonly HashSet<Guid> _requests = [];
    private bool _helperUnsettled;
    public string Engine => _configuration.Engine;

    internal ExternalRigEngineControl(ExternalRigConfiguration configuration)
    {
        if (configuration.Engine is not ("retail" or "opennv") || !Path.IsPathFullyQualified(configuration.Executable) ||
            !File.Exists(configuration.Executable) || configuration.Arguments is null || configuration.Arguments.Any(argument => argument is null || argument.Contains('\0')) ||
            configuration.MaximumRequestBytes <= 0 || configuration.MaximumReceiptBytes <= 0 || configuration.MaximumDiagnosticBytes <= 0)
            throw new ArgumentException("External rig control requires an explicit existing executable, argument array and positive byte bounds.");
        if (configuration.WorkingDirectory is { } directory && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
            throw new ArgumentException("External rig working directory must be an existing absolute directory.");
        _configuration = configuration with { Arguments = configuration.Arguments.ToArray() };
    }

    public Task<ParityRigReceipt> ExecuteAsync(ParityRigRequest request, CancellationToken cancellationToken) => Exchange(request, cancellationToken);

    public Task<ParityRigReceipt> QuiesceAsync(ParityRigRequest request, CancellationToken cancellationToken) =>
        request.Operation == "rig.quiesce" ? Exchange(request, cancellationToken) :
            throw new ArgumentException("Quiescence requires the explicit rig.quiesce operation.");

    private async Task<ParityRigReceipt> Exchange(ParityRigRequest request, CancellationToken cancellationToken)
    {
        if (request.Run == Guid.Empty || request.Request == Guid.Empty || string.IsNullOrWhiteSpace(request.Operation) ||
            string.IsNullOrWhiteSpace(request.SourceStackHash) || request.SourceStackHash.Length != 64 || !request.SourceStackHash.All(char.IsAsciiHexDigit))
            throw new ArgumentException("External rig request lacks correlated identity/source provenance.");
        var input = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        if (input.Length > _configuration.MaximumRequestBytes) throw new InvalidDataException("External rig request exceeds its explicit byte bound.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(8));
        await _serial.WaitAsync(lifetime.Token);
        try
        {
            if (_helperUnsettled) throw new IOException("Previous helper termination was not confirmed; new control requests are refused.");
            if (!_requests.Add(request.Request)) throw new InvalidOperationException("External rig requests cannot be replayed/retried.");
            var start = new ProcessStartInfo(_configuration.Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8,
                WorkingDirectory = _configuration.WorkingDirectory ?? Path.GetDirectoryName(_configuration.Executable)!,
            };
            foreach (var argument in _configuration.Arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            try { if (!process.Start()) throw new IOException("Explicit external rig helper did not start."); }
            catch (System.ComponentModel.Win32Exception error) { throw new IOException("Explicit external rig helper could not start.", error); }
            // Kill only this helper. The separately running game is never a
            // child-tree cleanup target. Quiesce must settle its entered work.
            using var kill = lifetime.Token.Register(() => StopHelper(process));
            var output = ReadBounded(process.StandardOutput.BaseStream, _configuration.MaximumReceiptBytes, lifetime);
            var diagnostics = ReadBounded(process.StandardError.BaseStream, _configuration.MaximumDiagnosticBytes, lifetime);
            var exchange = WriteAndWait(process, input, lifetime.Token);
            try
            {
                await Task.WhenAll(output, diagnostics, exchange).WaitAsync(lifetime.Token);
                var text = Utf8.GetString(await output);
                var diagnosticText = Utf8.GetString(await diagnostics);
                ParityRigReceipt receipt;
                try
                {
                    using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
                    RequireUniqueProperties(document.RootElement);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Receipt is not a JSON object.");
                    foreach (var field in new[] { "run", "request", "engine", "operation", "sourceStackHash", "disposition", "error", "evidence" })
                        if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("Receipt is missing " + field + ".");
                    receipt = document.RootElement.Deserialize<ParityRigReceipt>(Json) ?? throw new InvalidDataException("Receipt is null.");
                }
                catch (Exception error) when (error is JsonException or InvalidDataException)
                {
                    throw new IOException($"External rig helper exited {process.ExitCode} without a strict receipt. {Short(diagnosticText)}", error);
                }
                if (receipt.Run != request.Run || receipt.Request != request.Request || receipt.Engine != Engine ||
                    receipt.SourceStackHash != request.SourceStackHash || receipt.Operation != request.Operation ||
                    string.IsNullOrWhiteSpace(receipt.Disposition) || receipt.Evidence is null)
                    throw new InvalidDataException("External rig helper returned a stale or mismatched receipt.");
                if (receipt.Disposition == "completed" && (process.ExitCode != 0 || receipt.Error is not null))
                    throw new InvalidDataException("External rig helper claimed completion despite process/error failure.");
                if (receipt.Disposition != "completed" && string.IsNullOrWhiteSpace(receipt.Error))
                    throw new InvalidDataException("External rig helper failed without an explicit reason.");
                foreach (var evidence in receipt.Evidence)
                {
                    if (evidence is null || string.IsNullOrWhiteSpace(evidence.Lane) || string.IsNullOrWhiteSpace(evidence.Schema) ||
                        string.IsNullOrWhiteSpace(evidence.Path) || !Path.IsPathFullyQualified(evidence.Path) || !File.Exists(evidence.Path) ||
                        string.IsNullOrWhiteSpace(evidence.Sha256) || evidence.Sha256.Length != 64 || !evidence.Sha256.All(char.IsAsciiHexDigit))
                        throw new InvalidDataException("External rig evidence lacks existing absolute-file provenance.");
                    await using var stream = File.OpenRead(evidence.Path);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, lifetime.Token));
                    if (!hash.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("External rig evidence hash changed: " + evidence.Path);
                }
                return receipt;
            }
            catch (Exception original)
            {
                lifetime.Cancel(); StopHelper(process);
                process.StandardInput.Dispose(); process.StandardOutput.Dispose(); process.StandardError.Dispose();
                try { await Task.WhenAll(output, diagnostics, exchange).WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or InvalidOperationException) { }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException)
                {
                    _helperUnsettled = true;
                    throw new IOException("External helper termination was not confirmed; further control is refused.", original);
                }
                throw;
            }
        }
        finally { _serial.Release(); }
    }

    private static async Task WriteAndWait(Process process, byte[] input, CancellationToken cancellationToken)
    {
        await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadBounded(Stream stream, int limit, CancellationTokenSource lifetime)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, lifetime.Token);
                if (read == 0) return bytes.ToArray();
                if (bytes.Length + read > limit) throw new InvalidDataException("External rig pipe exceeded its explicit byte bound; output was not truncated into a receipt.");
                bytes.Write(buffer, 0, read);
            }
        }
        catch { lifetime.Cancel(); throw; }
    }

    private static void StopHelper(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Receipt repeats JSON property " + property.Name + ".");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }

    private static string Short(string value) => value.Length <= 4096 ? value : value[..4096] + " [diagnostic excerpt]";
    public void Dispose() => _serial.Dispose();
}
