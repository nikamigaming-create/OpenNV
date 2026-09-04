namespace Godot
{
    internal enum Error { Ok }
    internal abstract class AudioStream { }
    internal sealed class AudioStreamWav : AudioStream
    {
        internal static AudioStreamWav? LoadFromBuffer(
            byte[] payload,
            Collections.Dictionary options) => new();
    }
    internal sealed class AudioStreamMP3 : AudioStream
    {
        internal static AudioStreamMP3? LoadFromBuffer(byte[] payload) => new();
    }
    internal sealed class AudioStreamOggVorbis : AudioStream
    {
        internal static AudioStreamOggVorbis? LoadFromBuffer(byte[] payload) => new();
    }
    internal sealed class Image
    {
        internal Error LoadDdsFromBuffer(byte[] payload) => Error.Ok;
        internal bool IsEmpty() => false;
    }
    internal sealed class ImageTexture
    {
        internal static ImageTexture? CreateFromImage(Image image) => new();
    }
}

namespace Godot.Collections
{
    internal sealed class Dictionary
    {
        public void Add(object key, object? value) { }
    }
}

namespace OpenNV.Runtime.Content
{
    internal sealed class RuntimeOwnedContentSource
    {
        internal static RuntimeOwnedContentSource? Current => null;
        internal bool TryRead(string logicalPath, string? preferredArchive, out byte[] data, out string source)
        {
            data = [];
            source = string.Empty;
            return false;
        }
    }
}
