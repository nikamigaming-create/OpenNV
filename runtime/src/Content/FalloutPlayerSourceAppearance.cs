namespace OpenNV.Runtime.Content;

internal static class FalloutPlayerSourceAppearance
{
    private const string FirstPersonDirectory = "meshes/characters/_1stperson";
    private const uint HandBipedSlots = 0x18;

    internal static FalloutNpcAppearance Select(FalloutNpcAppearance appearance,
        RuntimeLiveContentSource content, bool firstPerson)
    {
        ArgumentNullException.ThrowIfNull(appearance); ArgumentNullException.ThrowIfNull(content);
        if (!firstPerson) return appearance;
        var models = appearance.Models.Where(part => part.Role is "body" or "hand-left" or "hand-right" or "armor" or "armor-addon")
            .Select(SelectHandVariant).ToArray();
        return appearance with { Models = models, SkeletonPath = FirstPersonDirectory + "/skeleton.nif" };

        FalloutNpcAppearancePart SelectHandVariant(FalloutNpcAppearancePart part)
        {
            if ((part.BipedSlots & HandBipedSlots) == 0 || part.ModelPath is not { } path || !path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                return part;
            // The existing source selection tests the owned first-person hand
            // variant. Its inverse binds belong to the first-person hierarchy.
            var candidate = path[..^4] + "1st.nif";
            return content.TryRead(candidate, null, out _, out _) ? part with { ModelPath = candidate } : part;
        }
    }
}
