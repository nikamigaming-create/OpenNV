using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

// Private evidence only. A transport acknowledgement never establishes a
// gameplay effect, matched frame or exact native input-consumption timestamp.
internal sealed class RecordedInputDeliveryJournal : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly IncrementalHash _hash;
    private long _ordinal;
    private long _lastMicroseconds;
    private bool _closed;
    internal string Path { get; }
    internal RecordedInputDeliveryJournal(string path, object header)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("Input receipts require an absolute private journal path.");
        Path = path;
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            _writer = new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            try { Write(new { schema = "opennv-input-delivery-journal/v1", header, gameplayParity = "unverified" }); }
            catch { _writer.Dispose(); throw; }
        }
        catch { _hash.Dispose(); throw; }
    }

    internal void Append(string phase, long microseconds, object value)
    {
        if (_closed || microseconds < _lastMicroseconds) throw new InvalidOperationException("Receipt journal is closed or its clock regressed.");
        Write(new { ordinal = ++_ordinal, phase, microseconds, value });
        _lastMicroseconds = microseconds;
    }

    private void Write(object value)
    {
        var line = JsonSerializer.Serialize(value, RecordedInputTape.Json);
        _writer.WriteLine(line); RecordedInputTape.AppendHash(_hash, line);
    }

    internal void Finish(long microseconds, string? error, object? state = null)
    {
        if (_closed) return;
        if (microseconds < _lastMicroseconds) throw new InvalidDataException("Receipt journal footer precedes its actual delivery prefix.");
        _closed = true;
        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(new { complete = error is null, error, microseconds,
                entries = _ordinal, sha256 = Convert.ToHexString(_hash.GetHashAndReset()), state, gameplayParity = "unverified" }, RecordedInputTape.Json));
        }
        finally { _writer.Dispose(); _hash.Dispose(); }
    }
    public void Dispose() => Finish(_lastMicroseconds, "Receipt journal retired without an explicit completed segment.");
}
