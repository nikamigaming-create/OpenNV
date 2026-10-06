namespace OpenNV.Runtime.Content;

// A live source/native/media receipt, never an active-audio save continuation.
internal sealed record FalloutFiniteSoundVoice(ulong NativeOwner, FalloutFormKey Reference,
    long Generation, FalloutFormKey Sound, string SoundSha256, string Path, string MediaSha256)
{
    internal void Validate()
    {
        static bool Key(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (NativeOwner == 0 || Generation <= 0 || !Key(Reference) || !Key(Sound) ||
            !Hash(SoundSha256) || !Hash(MediaSha256) ||
            string.IsNullOrWhiteSpace(Path) || FalloutBsaArchive.CanonicalPath(Path) != Path ||
            !Path.StartsWith("sound\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Finite sound lacks its actual source, native voice or media binding.");
    }
}
