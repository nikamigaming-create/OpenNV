using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Observed materialization order is independent of the sorted UI projection.
// Removed forms may remain in this history; a later unqualified insertion
// invalidates it rather than reusing an obsolete position.
internal sealed record FalloutInventoryTraversal(FalloutFormKey SourceOwner, string SourceSha256,
    IReadOnlyList<FalloutFormKey> Forms)
{
    internal FalloutInventoryTraversal Copy() => this with { Forms = Forms.ToArray() };

    internal void Validate(FalloutPluginStack records, IReadOnlyList<FalloutCampaignItem> items)
    {
        var source = records.GetEffective(SourceOwner);
        if (source.Signature is not ("NPC_" or "CREA" or "CONT") ||
            !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            Forms is null || Forms.Distinct().Count() != Forms.Count || items.Any(item => !Forms.Contains(item.FormKey)))
            throw new InvalidDataException("Inventory traversal differs from its source owner or retained materialized forms.");
        foreach (var form in Forms) _ = records.GetEffective(form);
    }
}

internal sealed partial class FalloutPlayerInventory
{
    private FalloutInventoryTraversal? _traversal;
    private void InvalidateTraversalForInsertions(IEnumerable<FalloutCampaignItem> replacements)
    {
        if (_traversal is not null && replacements.Any(item => !_items.ContainsKey(item.FormKey))) _traversal = null;
    }
    internal FalloutInventoryTraversal? CaptureTraversal() => _traversal?.Copy();

    internal void RestoreTraversal(FalloutPluginStack records, FalloutInventoryTraversal? traversal)
    {
        traversal?.Validate(records, Items);
        _traversal = traversal?.Copy();
    }

    internal IReadOnlyList<FalloutCampaignItem>? TraversalItems => _traversal is null ? null :
        _traversal.Forms.Select(Item).Where(item => item is not null).Select(item => item!).ToArray();
}
