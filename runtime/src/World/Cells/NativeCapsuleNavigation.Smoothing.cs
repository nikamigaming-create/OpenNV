using Godot;

namespace OpenNV.Runtime.World.Cells;

internal static partial class NativeCapsuleNavigation
{
    private delegate IEnumerable<Vector3?> SupportedFlatEdge(Vector3 from, Vector3 desired);

    private static IEnumerable<IReadOnlyList<Vector3>?> SmoothRoute(IReadOnlyList<Vector3> route,
        Vector3 start, float spacing, float heightTolerance, SupportedFlatEdge edge)
    {
        var points = route[0] == start ? route.ToArray() : new[] { start }.Concat(route).ToArray();
        var result = new List<Vector3>();
        for (var anchor = 0; anchor < points.Length - 1;)
        {
            var selected = anchor + 1;
            for (var candidate = points.Length - 1; candidate > anchor + 1; candidate--)
            {
                // Preserve every height transition. A flat shortcut must not
                // turn stairs, a lower floor or a bridge into a direct segment.
                var origin = points[anchor];
                var heightTransition = false;
                for (var index = anchor + 1; index <= candidate; index++)
                    if (Math.Abs(points[index].Y - origin.Y) > heightTolerance) { heightTransition = true; break; }
                if (heightTransition)
                {
                    yield return null;
                    continue;
                }
                var destination = points[candidate];
                var count = Math.Max(1, (int)Math.Ceiling(origin.DistanceTo(destination) / spacing));
                var from = origin;
                var clear = true;
                for (var sample = 1; sample <= count; sample++)
                {
                    var desired = origin.Lerp(destination, (float)sample / count);
                    Vector3? endpoint = null;
                    foreach (var edgeSample in edge(from, desired))
                    {
                        if (edgeSample is { } landing) endpoint = landing;
                        else yield return null;
                    }
                    if (endpoint is not { } accepted || Math.Abs(accepted.Y - origin.Y) > heightTolerance)
                        clear = false;
                    else from = accepted;
                    // Smoothing shares the search's physics-thread work budget.
                    yield return null;
                    if (!clear) break;
                }
                if (!clear) continue;
                selected = candidate;
                break;
            }
            result.Add(points[selected]);
            anchor = selected;
        }
        yield return result;
    }
}
