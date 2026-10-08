using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    // One immutable source-stack audit owns these small summaries and original
    // refusals. No target mesh becomes a current-state navigation member.
    internal sealed class SourceNavigationTargets(FalloutPluginStack records)
    {
        private sealed record Target(string? Winner, string? Signature, string? Sha256, bool Deleted,
            bool InitiallyDisabled, int? Triangles, int[] ExcludedTriangles, string Status, Exception? Error);
        private readonly Dictionary<FalloutFormKey, Lazy<Target>> _targets = new(FalloutFormKeyComparer.Instance);
        internal int TargetQueries { get; private set; }
        internal int TargetEntries => _targets.Count;

        internal sealed record Declaration(int SourceEntry, uint Type, string Target, int Triangle, string? Winner,
            string? Signature, string? Sha256, bool Deleted, bool InitiallyDisabled, int? TargetTriangles,
            bool? TargetTriangleExcludedByFlags, string Bounds, string? Error,
            string NativeAdmission = "unverified; source bounds do not activate a mesh or certify a directed path",
            string TypeSemantics = "uninspected; the declared NVEX Type bits are preserved independently of target bounds");

        internal Declaration[] Inspect(FalloutPluginRecord source, FalloutNavigationMesh mesh, List<object> failures)
        {
            if (!records.TryGetEffective(source.FormKey, out var winner) || !ReferenceEquals(winner, source) ||
                source.Signature != "NAVM" || mesh.Form != source.FormKey)
                throw new ArgumentException("External NAVM inspection belongs to another source-stack record owner.");
            return mesh.Edges.Select((edge, index) =>
            {
                if (!_targets.TryGetValue(edge.Mesh, out var retained))
                    _targets.Add(edge.Mesh, retained = new(() => ReadTarget(edge.Mesh)));
                var target = retained.Value;
                var bounds = target.Status;
                bool? excluded = null;
                var error = target.Error?.Message;
                if (target.Triangles is { } count)
                {
                    bounds = edge.Triangle < count ? "in-bounds" : "out-of-bounds";
                    if (bounds == "in-bounds") excluded = Array.BinarySearch(target.ExcludedTriangles, (int)edge.Triangle) >= 0;
                    else error = $"External NAVM triangle {edge.Triangle} exceeds winning target {edge.Mesh}'s {count} triangles.";
                }
                if (bounds != "in-bounds")
                    failures.Add(new { lane = target.Triangles is not null ? "navm-external-target-bounds" :
                        target.Status == "target-layout-refused" ? "navm-external-target-layout" : "navm-external-owner",
                        sourceEntry = index, target = edge.Mesh.ToString(), edge.Triangle, error });
                return new Declaration(index, edge.Type, edge.Mesh.ToString(), edge.Triangle, target.Winner,
                    target.Signature, target.Sha256, target.Deleted, target.InitiallyDisabled, target.Triangles,
                    excluded, bounds, error);
            }).ToArray();
        }

        private Target ReadTarget(FalloutFormKey key)
        {
            TargetQueries++;
            if (!records.TryGetWinner(key, out var winner))
                return new(null, null, null, false, false, null, [], "target-missing",
                    new KeyNotFoundException("External NAVM target has no winning source record: " + key));
            var off = (winner.Flags & 0x800) != 0;
            if (winner.IsDeleted || winner.Signature != "NAVM")
                return new(winner.Plugin.Name, winner.Signature, null, winner.IsDeleted, off, null, [],
                    winner.IsDeleted ? "target-deleted" : "target-wrong-signature",
                    new InvalidDataException("External NAVM target is deleted or not NAVM: " + key));
            string? sha256 = null;
            try
            {
                sha256 = Hash(winner.ReadData());
                var mesh = FalloutNavigationMesh.Read(winner);
                return new(winner.Plugin.Name, winner.Signature, sha256, false, off, mesh.Triangles.Length,
                    Enumerable.Range(0, mesh.Triangles.Length).Where(index => (mesh.Triangles[index].Flags & 8) != 0).ToArray(),
                    "decoded", null);
            }
            catch (Exception error)
            {
                return new(winner.Plugin.Name, winner.Signature, sha256, false, off, null, [], "target-layout-refused", error);
            }
        }
    }
}
