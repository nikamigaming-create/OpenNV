using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorEatProgress(FalloutFormKey Item, string ItemSha256,
    FalloutFormKey? WorldReference, FalloutFormKey? Idle = null, string? IdleSha256 = null,
    bool Consumed = false, IReadOnlyList<FalloutActorIdleObjectBinding>? Objects = null, bool NoIdle = false)
{
    internal void Validate()
    {
        static bool Form(FalloutFormKey form) => !string.IsNullOrWhiteSpace(form.OwnerPlugin) && form.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        static bool Hash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
        if (Objects is not null) foreach (var binding in Objects) binding.Validate();
        if (Idle is null && Objects is { Count: > 0 }) throw new InvalidDataException("Eat ANIO has no owning idle.");
        if (!Form(Item) || !Hash(ItemSha256) || WorldReference is { } reference && !Form(reference) ||
            (Idle is null) != (IdleSha256 is null) || Idle is { } idle && (!Form(idle) || !Hash(IdleSha256)) ||
            NoIdle && (Idle is not null || !Consumed) || Consumed && Idle is null && !NoIdle)
            throw new InvalidDataException("Actor Eat continuation is invalid.");
    }
}
