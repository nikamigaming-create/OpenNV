using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorLogicalScene
{
    // Cold resource construction already checks every actual asset/root/class
    // association. This checks that rebinding changes only lifetime identities,
    // never poses, flags, bounds, materials, alpha history or source child slots.
    internal static void RequireColdRestoration(FalloutActorLogicalSceneSnapshot before,
        FalloutActorLogicalSceneSnapshot after)
    {
        RequireComplete(before); RequireComplete(after);
        if (after.Factory == before.Factory || after.Lifetime == before.Lifetime ||
            after.RestoredLifetime != before.Lifetime || after.Sequence != checked(before.Sequence + 1) ||
            after.Objects.Count != before.Objects.Count)
            throw new InvalidDataException("Cold logical model reused a prior lifetime or replayed source state.");
        var objects = new FalloutNifSceneObjectSnapshot[after.Objects.Count];
        for (var index = 0; index < objects.Length; index++)
        {
            var old = before.Objects[index]; var current = after.Objects[index];
            if (old.Key != current.Key || (old.Bound is null) != (current.Bound is null) ||
                old.Bound is { } bound && current.Bound!.Identity == bound.Identity)
                throw new InvalidDataException("Cold logical model lost source object order or reused a native bound lifetime.");
            objects[index] = current with { Bound = current.Bound is { } value ? value with { Identity = old.Bound!.Identity } : null };
        }
        var rebound = after with
        {
            Factory = before.Factory,
            Lifetime = before.Lifetime,
            Sequence = before.Sequence,
            RestoredLifetime = before.RestoredLifetime,
            Objects = objects
        };
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(rebound))
            throw new InvalidDataException("Cold logical model changed retained source resources or mutable graph state.");
    }
}
