using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorSourcePart(FalloutNpcAppearancePart Selection, FalloutNifPhysicalModel Physical);

// One selected assembly factory, constructed from original source declarations
// before native presentation. Parts retain separate source graphs: their skin,
// equipment and runtime physical attachment writers cannot be inferred by name.
internal sealed class FalloutActorSourceModel
{
    internal FalloutActorSourceModel(FalloutNpcAppearance appearance, FalloutNifPhysicalModel skeleton,
        IEnumerable<FalloutActorSourcePart> parts)
    {
        Appearance = appearance; Skeleton = skeleton;
        Parts = Array.AsReadOnly(parts.ToArray());
        if (!appearance.Models.SequenceEqual(Parts.Select(part => part.Selection)))
            throw new InvalidDataException("Actor source assembly changed its selected part order or identity.");
    }

    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutNpcAppearance Appearance { get; }
    internal FalloutNifPhysicalModel Skeleton { get; }
    internal IReadOnlyList<FalloutActorSourcePart> Parts { get; }

    internal FalloutActorSourceModel Instantiate() => new(Appearance, Skeleton, Parts);
}
