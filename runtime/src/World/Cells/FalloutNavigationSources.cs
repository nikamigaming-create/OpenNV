using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Only source geometry/topology is shared. Clearance, door state and route
// execution remain queries against each live world's authoritative owners.
internal sealed record FalloutNavigationSourceIndex(CellNavigationGraph Graph) : IFalloutSourceIndex;

internal static class FalloutNavigationSources
{
    internal static bool TryScope(FalloutPluginStack records, FalloutFormKey scope, out CellNavigationGraph? graph)
    {
        var canonical = records.GetEffective(scope).FormKey;
        if (records.TryReadSourceIndex<FalloutNavigationSourceIndex>(out var source, scope: canonical))
        {
            graph = source!.Graph;
            return true;
        }
        graph = null;
        return false;
    }

    internal static CellNavigationGraph Region(FalloutPluginStack records, FalloutFormKey cell,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var definition = FalloutCellSceneReader.ReadDefinition(records, cell);
        return Scope(records, definition.Worldspace ?? definition.FormKey, cancellation);
    }

    internal static CellNavigationGraph Cell(FalloutPluginStack records, FalloutFormKey cell,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var source = records.GetEffective(cell);
        if (source.Signature != "CELL") throw new InvalidDataException("Navigation cell source is not CELL.");
        return Scope(records, source.FormKey, cancellation);
    }

    private static CellNavigationGraph Scope(FalloutPluginStack records, FalloutFormKey scope, CancellationToken cancellation)
        => records.ReadSourceIndex<FalloutNavigationSourceIndex>((owner, token) =>
        {
            var source = owner.GetEffective(scope);
            var cells = new HashSet<FalloutFormKey>();
            if (source.Signature == "CELL") cells.Add(source.FormKey);
            else if (source.Signature == "WRLD")
            {
                foreach (var cell in owner.EffectiveRecords("CELL"))
                {
                    token.ThrowIfCancellationRequested();
                    if (FalloutCellSceneReader.ParentWorldspace(cell) == scope) cells.Add(cell.FormKey);
                }
            }
            else throw new InvalidDataException("Navigation scope is neither a source CELL nor WRLD.");
            return new(CellNavigationGraph.LoadOwned(owner, cells, cancellation: token));
        }, cancellation, scope).Graph;

    internal static void PrepareSavedTravel(FalloutPluginStack records,
        IReadOnlyList<FalloutReferenceSnapshot> references, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        FalloutReferenceSnapshot.Validate(references);
        foreach (var reference in references)
        {
            cancellation.ThrowIfCancellationRequested();
            if (reference.PackageMotion is not { Travel: { NavigationSha256: not null } travel } motion) continue;
            // Restore does not validate the retired route of a replaced package.
            // Preparing it must not turn an unused continuation into a new fault.
            if (reference.PackageAssignment is { } assignment && assignment.Package != motion.Package) continue;
            try
            {
                if (travel.RouteCell is { } routeCell) _ = Region(records, routeCell, cancellation);
                else _ = Cell(records, travel.Cell, cancellation);
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
            {
                throw new InvalidDataException($"Saved Travel source preparation for {reference.Reference}: {error.Message}", error);
            }
        }
        cancellation.ThrowIfCancellationRequested();
    }
}
