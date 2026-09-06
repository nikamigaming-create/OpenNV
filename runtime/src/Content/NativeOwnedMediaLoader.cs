using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal static class NativeOwnedMediaLoader
{
    private const string WavExtension = ".wav";
    private const string OggExtension = ".ogg";
    private const int AsciiDigitThree = 0x33;
    private static readonly string Mp3Extension = string.Concat(
        ".mp", char.ConvertFromUtf32(AsciiDigitThree));

    internal static ImageTexture LoadTexture(
        string logicalPath,
        string? preferredArchive = null)
    {
        var payload = Read(logicalPath, preferredArchive, out var source);
        NativeOwnedMediaFormat.ValidateDds(payload);
        var image = new Image();
        var result = image.LoadDdsFromBuffer(payload);
        if (result != Error.Ok || image.IsEmpty())
            throw new InvalidDataException(
                $"Godot rejected owned DDS data from {source}: {result}");
        return NativeDdsTexture.Create(image) ??
            throw new InvalidDataException(
                $"Godot could not create a texture from owned DDS data: {source}");
    }

    internal static AudioStream LoadAudio(
        string logicalPath,
        string? preferredArchive = null)
    {
        var payload = Read(logicalPath, preferredArchive, out var source);
        var extension = Path.GetExtension(logicalPath).ToLowerInvariant();
        AudioStream? stream;
        if (extension == WavExtension)
            stream = LoadWav(payload);
        else if (extension == Mp3Extension)
            stream = LoadMp3(payload);
        else if (extension == OggExtension)
            stream = LoadOgg(payload);
        else
        {
            throw new InvalidDataException(
                $"Unsupported owned audio extension: {logicalPath}");
        }
        if (stream is null) throw new InvalidDataException($"Godot rejected owned audio data from {source}");
        stream.SetMeta("opennv_owned_media_source", source);
        stream.SetMeta("opennv_owned_media_path", logicalPath);
        stream.SetMeta("opennv_owned_media_sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)));
        return stream;
    }

    private static AudioStreamWav? LoadWav(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateWav(payload);
        return AudioStreamWav.LoadFromBuffer(
            payload, new Godot.Collections.Dictionary());
    }

    private static AudioStreamMP3? LoadMp3(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateMp3(payload);
        return AudioStreamMP3.LoadFromBuffer(payload);
    }

    private static AudioStreamOggVorbis? LoadOgg(byte[] payload)
    {
        NativeOwnedMediaFormat.ValidateOgg(payload);
        return AudioStreamOggVorbis.LoadFromBuffer(payload);
    }

    private static byte[] Read(
        string logicalPath,
        string? preferredArchive,
        out string source)
    {
        var owned = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException(
                "Live media loading requires a selected retail Data folder.");
        if (!owned.TryRead(logicalPath, preferredArchive, out var payload, out source))
            throw new FileNotFoundException(
                $"Owned media resource is missing: {logicalPath}", logicalPath);
        return payload;
    }
}
