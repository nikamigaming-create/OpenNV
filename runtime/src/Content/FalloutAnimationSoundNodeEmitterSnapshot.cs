namespace OpenNV.Runtime.Content;

internal sealed record FalloutAnimationSoundNodeEmitterSnapshot(FalloutFormKey Reference,
    string ModelPath, string ModelSha256, int? Block, string? Name)
{
    internal void Validate(FalloutFormKey reference)
    {
        if (Reference != reference || string.IsNullOrWhiteSpace(Reference.OwnerPlugin) ||
            Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            !FalloutAnimationSoundEventsSnapshot.Hash(ModelSha256) ||
            string.IsNullOrWhiteSpace(ModelPath) || FalloutBsaArchive.CanonicalPath(ModelPath) != ModelPath ||
            !ModelPath.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase) ||
            !ModelPath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
            Block is < 0 || (Block is null ? Name is not null : Name is null))
            throw new InvalidDataException("Saved sound node has an invalid reference/model/block identity.");
    }
}
