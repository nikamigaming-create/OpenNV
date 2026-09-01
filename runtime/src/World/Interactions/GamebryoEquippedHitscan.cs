using Godot;

namespace OpenNV.Runtime.World.Interactions;

internal static class GamebryoEquippedHitscan
{
    internal static bool Fire(
        Camera3D camera,
        Rid shooter,
        uint collisionMask,
        float distanceMeters,
        string equippedItemFormId,
        string requiredItemFormId,
        IReadOnlyDictionary<string, Action> sourceHits)
    {
        if (!float.IsFinite(distanceMeters) || distanceMeters <= 0.0f ||
            string.IsNullOrWhiteSpace(equippedItemFormId) ||
            string.IsNullOrWhiteSpace(requiredItemFormId) || sourceHits.Count == 0)
            throw new InvalidOperationException(
                "Gamebryo equipped hitscan contract is invalid.");
        if (!equippedItemFormId.Equals(
                requiredItemFormId, StringComparison.OrdinalIgnoreCase))
            return false;
        var query = PhysicsRayQueryParameters3D.Create(
            camera.GlobalPosition,
            camera.GlobalPosition + -camera.GlobalBasis.Z * distanceMeters,
            collisionMask);
        query.Exclude = [shooter];
        var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var value) ||
            value.AsGodotObject() is not Node node)
            return false;
        for (Node? current = node; current is not null; current = current.GetParent())
        {
            if (!current.HasMeta("opennv_source_form_id"))
                continue;
            if (!sourceHits.TryGetValue(
                    current.GetMeta("opennv_source_form_id").AsString(), out var applied))
                return false;
            applied();
            return true;
        }
        return false;
    }
}
