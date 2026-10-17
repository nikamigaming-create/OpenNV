using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMusicPlaybackSnapshot(FalloutFormKey Form, string RecordSha256,
    string? Path, string? MediaSha256, double DurationSeconds, double PositionSeconds, bool Playing);

internal sealed record FalloutMusicType(FalloutFormKey Form, string? Path, float GainDb, bool Loop, string RecordSha256)
{
    internal static FalloutMusicType Read(FalloutPluginRecord record)
    {
        if (record.Signature != "MUSC") throw new InvalidDataException("Music command requires MUSC.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature is not ("EDID" or "FNAM" or "ANAM")))
            throw new NotSupportedException("Music type has an unowned field.");
        var files = fields.Where(field => field.Signature == "FNAM").ToArray();
        var levels = fields.Where(field => field.Signature == "ANAM").ToArray();
        if (files.Length > 1 || levels.Length > 1) throw new InvalidDataException("Music type repeats its filename or attenuation.");
        var gain = 0f;
        if (levels.Length == 1)
        {
            var attenuation = levels[0].Data;
            if (attenuation.Length != 4) throw new InvalidDataException("Music attenuation requires one Float32.");
            gain = BinaryPrimitives.ReadSingleLittleEndian(attenuation.Span);
            if (!float.IsFinite(gain)) throw new InvalidDataException("Music attenuation is not finite.");
        }
        string? path = null;
        if (files.Length == 1)
        {
            path = FalloutPlugin.DecodeZeroTerminated(files[0].Data.Span, "music filename").Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') ||
                path.Split('/').Any(part => part is "." or ".."))
                throw new InvalidDataException("Music filename leaves the owned namespace.");
            if (path.EndsWith('/')) throw new NotSupportedException("Music folders require their playlist selection owner.");
            if (!path.StartsWith("music/", StringComparison.OrdinalIgnoreCase)) path = "music/" + path;
            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".mp3" or ".wav" or ".ogg"))
                throw new NotSupportedException("Music file has an unsupported audio format.");
        }
        // MUSC stores loop selection in ANAM's positive sign. Its magnitude is
        // attenuation, not a volume boost. An absent or zero ANAM does not loop.
        return new(record.FormKey, path, -MathF.Abs(gain), gain > 0, Convert.ToHexString(SHA256.HashData(record.ReadData())));
    }

    internal static FalloutMusicType ValidateSnapshot(FalloutPluginStack records, FalloutMusicPlaybackSnapshot snapshot)
    {
        var source = Read(records.GetEffective(snapshot.Form));
        if (!source.RecordSha256.Equals(snapshot.RecordSha256, StringComparison.OrdinalIgnoreCase) || source.Path != snapshot.Path ||
            !double.IsFinite(snapshot.DurationSeconds) || !double.IsFinite(snapshot.PositionSeconds) ||
            snapshot.DurationSeconds < 0 || snapshot.PositionSeconds < 0 || snapshot.PositionSeconds > snapshot.DurationSeconds)
            throw new InvalidDataException("Saved music differs from its source or has an invalid playback cursor.");
        if (source.Path is null)
        {
            if (snapshot.MediaSha256 is not null || snapshot.DurationSeconds != 0 || snapshot.PositionSeconds != 0 || snapshot.Playing)
                throw new InvalidDataException("Silent music type has a saved playback voice.");
        }
        else if (snapshot.MediaSha256 is not { Length: 64 } hash || hash.Any(value => !Uri.IsHexDigit(value)) ||
            snapshot.DurationSeconds <= 0 || snapshot.Playing && snapshot.PositionSeconds >= snapshot.DurationSeconds ||
            !snapshot.Playing && snapshot.PositionSeconds != snapshot.DurationSeconds)
            throw new InvalidDataException("Saved music has no valid media identity or completion state.");
        return source;
    }
}
