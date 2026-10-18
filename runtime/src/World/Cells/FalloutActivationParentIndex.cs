using System.Collections.ObjectModel;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Immutable winning declarations only. Relay clocks and deliveries belong to
// each reference world, including a fresh world created by Continue.
internal sealed record FalloutActivationParentIndex(
    IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<(FalloutFormKey Child, float Delay)>> Children,
    IReadOnlyList<FalloutActivationParentIssue> Issues) : IFalloutSourceIndex
{
    internal static FalloutActivationParentIndex Read(FalloutPluginStack records, CancellationToken cancellation)
    {
        var children = new Dictionary<FalloutFormKey, List<(FalloutFormKey, float)>>();
        var issues = new List<FalloutActivationParentIssue>();
        foreach (var reference in new[] { "REFR", "ACHR", "ACRE", "PGRE", "PMIS" }.SelectMany(records.EffectiveRecords))
        {
            cancellation.ThrowIfCancellationRequested();
            if (reference.IsDeleted) continue;
            foreach (var declaration in FalloutActivationParents.Read(reference))
            {
                var error = !records.TryGetWinner(declaration.Parent, out var parent) ? "missing parent" :
                    parent.IsDeleted ? "deleted winning parent" :
                    parent.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS")
                        ? $"parent has non-reference type {parent.Signature}" : null;
                if (error is not null) issues.Add(new(reference.FormKey, declaration.Parent, error));
                if (!children.TryGetValue(declaration.Parent, out var values)) children.Add(declaration.Parent, values = []);
                values.Add((reference.FormKey, declaration.DelaySeconds));
            }
        }
        var result = new FalloutActivationParentIndex(
            new ReadOnlyDictionary<FalloutFormKey, IReadOnlyList<(FalloutFormKey Child, float Delay)>>(
                children.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyList<(FalloutFormKey Child, float Delay)>)Array.AsReadOnly(pair.Value.ToArray()))),
            Array.AsReadOnly(issues.ToArray()));
        cancellation.ThrowIfCancellationRequested();
        return result;
    }
}
