using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// Mutable form paths live with the selected graph. Already prepared voices keep
// their own immutable descriptor and media; this owner never stops or rebinds them.
internal sealed class FalloutSoundPaths(FalloutPluginStack records)
{
    private sealed record PathState(string Source, string Sha256, string Current, long Revision);
    private readonly object _sync = new();
    private readonly Dictionary<FalloutFormKey, PathState> _paths = [];

    internal (string File, long Revision) Read(FalloutFormKey sound)
    {
        lock (_sync)
        {
            var (_, state) = Resolve(sound);
            return (state.Current, state.Revision);
        }
    }

    internal long Revision(FalloutFormKey sound) => Read(sound).Revision;

    internal void Set(FalloutFormKey sound, string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Contains('\0')) throw new InvalidDataException("Sound source paths cannot contain zero bytes.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { _ = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetByteCount(file); } // AllowSystemFallback: strict owned text encoding.
        catch (EncoderFallbackException error) // AllowSystemFallback: strict encoding failure.
        {
            throw new NotSupportedException("Sound source path cannot be represented by the owned text encoding.", error);
        }
        lock (_sync)
        {
            var (key, state) = Resolve(sound);
            if (state.Current == file) return;
            _paths[key] = state with { Current = file, Revision = checked(state.Revision + 1) };
        }
    }

    internal object State
    {
        get
        {
            lock (_sync) return new
            {
                overrides = _paths.Where(pair => pair.Value.Current != pair.Value.Source).Select(pair => new
                {
                    sound = pair.Key.ToString(),
                    source = pair.Value.Source,
                    sha256 = pair.Value.Sha256,
                    file = pair.Value.Current,
                    revision = pair.Value.Revision,
                }).ToArray(),
                persistence = "selected-stack-session-only;retail-cold-continuity-unmeasured",
                activeVoices = "retain-prepared-source-and-media",
            };
        }
    }

    private (FalloutFormKey Key, PathState State) Resolve(FalloutFormKey sound)
    {
        var source = records.GetEffective(sound);
        if (source.Signature != "SOUN") throw new InvalidDataException("Sound path commands require a SOUN form.");
        var key = source.FormKey;
        if (!_paths.TryGetValue(key, out var state))
        {
            var rows = source.ReadSubrecords().Where(row => row.Signature == "FNAM").ToArray();
            if (rows.Length != 1 || rows[0].Data.Length == 0 ||
                rows[0].Data.Span.IndexOf((byte)0) != rows[0].Data.Length - 1)
                throw new InvalidDataException("Sound FNAM requires one zero-terminated source path.");
            var file = FalloutPlugin.DecodeZeroTerminated(rows[0].Data.Span, "SOUN FNAM");
            _paths.Add(key, state = new(file, Convert.ToHexString(SHA256.HashData(source.ReadData())), file, 0));
        }
        return (key, state);
    }
}
