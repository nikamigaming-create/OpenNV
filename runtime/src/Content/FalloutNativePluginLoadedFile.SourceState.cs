using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFile
{
    internal FalloutNativeLoadedSourceState CaptureSourceState()
    {
        RequireIdle();
        var parser = _parser.Capture();
        return new(new(parser.Plugin, parser.Sha256, parser.Revision, parser.RecordHeaderOffset,
                parser.RawFormId, parser.RecordHeader is { } header ? Convert.ToHexString(SHA256.HashData(header)) : null,
                parser.BodySha256, parser.DataOffset, parser.ChunkType, parser.ChunkBytes, parser.BytesRead, parser.AtEnd),
            _binary.CaptureSourceState(), _metadata.Capture(), _revision);
    }

    internal static string SourceStateSha256(FalloutNativeLoadedSourceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
    }

    internal NativeNvseSourceFileCurrentStamp SourceCurrentStamp()
        => new("opennv-actual-loaded-source/v1", SourceSha256, SourceStateSha256(CaptureSourceState()));

    internal FalloutNativeLoadedFileSnapshot RecreateSourceState(FalloutNativeLoadedSourceState saved)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Parser is null || saved.Binary is null || saved.Metadata is null || saved.Revision < 0)
            throw new InvalidDataException("Source continuation lacks its actual parser/binary/metadata state.");
        var cursor = saved.Parser; byte[]? header = null;
        if (cursor.RecordHeaderOffset is { } at)
        {
            if (at < 0 || at > _context.Bytes - FalloutPlugin.RecordHeaderSize ||
                cursor.RecordHeaderSha256 is null || cursor.RecordHeaderSha256.Length != 64 ||
                !cursor.RecordHeaderSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Source continuation record has no complete original header coordinate/digest.");
            header = _context.Plugin.ReadAt(at, FalloutPlugin.RecordHeaderSize, "source continuation original header");
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(header)), cursor.RecordHeaderSha256))
                throw new InvalidDataException("Source continuation original record header changed.");
        }
        else if (cursor.RecordHeaderSha256 is not null)
            throw new InvalidDataException("A source continuation without a current record cannot retain its header digest.");
        var parser = new FalloutNativeSourceFileSnapshot(cursor.Plugin, cursor.SourceSha256, cursor.Revision,
            cursor.RecordHeaderOffset, cursor.RawFormId, header, cursor.BodySha256,
            cursor.DataOffset, cursor.ChunkType, cursor.ChunkBytes, cursor.BytesRead, cursor.AtEnd);
        return new(parser, _binary.RecreateSourceState(saved.Binary), saved.Metadata, saved.Revision);
    }

    internal void RestoreSourceState(FalloutNativeLoadedSourceState saved) => Restore(RecreateSourceState(saved));
}
