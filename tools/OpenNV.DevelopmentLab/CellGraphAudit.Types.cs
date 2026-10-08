using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    private sealed class ReferenceRow
    {
        public string Identity { get; init; } = "";
        public string Base { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Winner { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public string? BaseWinner { get; init; }
        public string? BaseSha256 { get; init; }
        public uint Flags { get; init; }
        public float[] Position { get; init; } = [];
        public float[] Rotation { get; init; } = [];
        public float Scale { get; init; }
        public bool InitiallyDisabled { get; init; }
        public bool? SourceEnabled { get; set; }
        public bool? SavedEnabled { get; set; }
        public object? SavedPlacement { get; set; }
        public string? EnableParent { get; init; }
        public bool Opposite { get; init; }
        public object? EnableDomain { get; set; }
        public string Disposition { get; set; } = "unbound";
        public string? Model { get; init; }
        public List<object> Failures { get; } = [];
        public object? Actor { get; set; }
        public object? Packages { get; set; }
        public object? NativeActorAi { get; set; }
        public object? Door { get; set; }
        public object? Primitive { get; set; }
        public object? Script { get; set; }
        public object? Native { get; set; }
    }

    internal sealed class ResourceRow
    {
        public string Path { get; init; } = "";
        public string Kind { get; set; } = "";
        public HashSet<string> InspectionRoles { get; } = new(StringComparer.Ordinal);
        public string? Source { get; set; }
        public string? Sha256 { get; set; }
        public int Bytes { get; set; }
        public uint? Stream { get; set; }
        public int Blocks { get; set; }
        public int DecodedBlocks { get; set; }
        public string[] BlockTypes { get; set; } = [];
        public int Roots { get; set; }
        public int Geometry { get; set; }
        public int CollisionAttachments { get; set; }
        public int CollisionBodies { get; set; }
        public int CollisionShapes { get; set; }
        public int CollisionTriangles { get; set; }
        public List<string> Dependencies { get; } = [];
        public List<ResourceDependency> DependencyEdges { get; } = [];
        public List<object> DependencyDeclarations { get; } = [];
        public List<AddonDependencyDeclaration> AddonLinks { get; } = [];
        public List<GeometryDataLinkRow> GeometryDataLinks { get; } = [];
        public List<object> Failures { get; } = [];
        public object? Native { get; set; }

        internal void AddDependency(string path, string kind)
        {
            path = Canonical(path);
            if (!Dependencies.Contains(path)) Dependencies.Add(path);
            var edge = new ResourceDependency(path, kind);
            if (!DependencyEdges.Contains(edge)) DependencyEdges.Add(edge);
        }
    }

    internal sealed record ResourceDependency(string Path, string Kind);

    internal sealed class GeometryDataLinkRow
    {
        public int Block { get; init; }
        public string Type { get; init; } = "";
        public int Offset { get; init; }
        public int Bytes { get; init; }
        public string Field { get; } = "Data";
        public int TargetBlock { get; init; }
        public string? TargetType { get; init; }
        public int? TargetOffset { get; init; }
        public int? TargetBytes { get; init; }
        public string Reader { get; } = "FalloutNifFile.ReadMeshData";
        public string ReaderDisposition { get; set; } = "uninspected";
        public int? Vertices { get; set; }
        public int? Triangles { get; set; }
        public string NativeAdmission { get; } = "unverified";
        public string? Error { get; set; }
    }

    internal readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, int Shape, string Reference);
    internal sealed record CollisionMath(IReadOnlyList<Triangle> Triangles, int Shapes, int PackedTriangles, IReadOnlyList<object> Failures);
    internal sealed record Model(FalloutNifFile? File, ResourceRow Report, IReadOnlyList<Triangle> Collision);
    private sealed record FloorHit(string Reference, int Shape, float Height, float NormalY);
    private sealed class ReadObservation : IDisposable
    {
        private readonly RuntimeLiveContentSource _source;
        private readonly Action<string, string, ReadOnlyMemory<byte>>? _previous;
        internal readonly Dictionary<string, object> Resources = new(StringComparer.OrdinalIgnoreCase);
        internal ReadObservation(RuntimeLiveContentSource source)
        {
            _source = source; _previous = source.ResourceReadObserver;
            source.ResourceReadObserver = (logical, identity, bytes) =>
            {
                Resources[Canonical(logical)] = new { path = Canonical(logical), source = identity, sha256 = Hash(bytes.Span), bytes = bytes.Length };
                _previous?.Invoke(logical, identity, bytes);
            };
        }
        public void Dispose() => _source.ResourceReadObserver = _previous;
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
