using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutOcclusionRegistration(FalloutDynamicPrimitive Primitive, FalloutRoomMembership Membership);
internal sealed record FalloutOcclusionView(IReadOnlyList<FalloutFormKey> Candidates,
    IReadOnlyList<(FalloutFormKey? Room, FalloutOcclusionExpression Expression)> Expressions);

internal sealed class FalloutOcclusionGraph(FalloutOcclusionCullingSource source, FalloutFormKey cell)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<FalloutOcclusionRegistration> _registrations = [];
    private readonly Dictionary<FalloutFormKey, FalloutOcclusionRegistration> _planes = new(FalloutFormKeyComparer.Instance);
    private IReadOnlyList<FalloutOcclusionGeometry> _geometry = [];
    private FalloutOcclusionSnapshot? _cold;
    private bool _sealed, _entered, _retired;
    internal string? Failure { get; private set; }
    internal string RegistrationSha256 { get; private set; } = string.Empty;
    internal long Generation { get; private set; }
    internal IReadOnlyList<FalloutOcclusionView> Views { get; private set; } = [];
    internal IReadOnlyList<FalloutOcclusionRegistration> Registrations => _registrations.AsReadOnly();

    internal void Register(FalloutDynamicPrimitive primitive, FalloutRoomMembership membership)
    {
        Require(); source.RequireCurrent();
        if (_sealed || primitive.Role != FalloutDynamicPrimitiveRole.OcclusionPlane || primitive.Cell != cell ||
            primitive.Source.EngineSha256 != source.EngineSha256 || membership.Reference != primitive.Reference ||
            membership.SourceSha256 != primitive.SourceSha256 || !_planes.TryAdd(primitive.Reference, new(primitive, membership)))
            throw new InvalidDataException("Occlusion registration is foreign, duplicate or no longer constructing.");
        _registrations.Add(_planes[primitive.Reference]);
    }
    internal void Seal(Func<FalloutFormKey, bool> hasActualRoom)
    {
        Require(); source.RequireCurrent();
        if (_sealed) throw new InvalidOperationException("Source occlusion graph was sealed twice.");
        try
        {
            foreach (var row in _registrations)
            {
                if (row.Membership.Room is { } room && !hasActualRoom(room))
                    throw new NotSupportedException("Source occlusion membership has no actual resident room owner.");
                foreach (var target in row.Primitive.OrderedOcclusionPlanes)
                    if (target is { } key && !_planes.ContainsKey(key))
                        throw new NotSupportedException("Source ordered plane link has no returning native rectangle owner.");
            }
            var text = new StringBuilder(source.ContractSha256).Append('|').Append(cell);
            foreach (var row in _registrations)
            {
                text.Append('|').Append(row.Primitive.Reference).Append(':').Append(row.Primitive.SourceSha256)
                    .Append(':').Append(row.Membership.Room?.ToString() ?? "cell")
                    .Append(':').Append(row.Membership.SourceSha256);
                foreach (var target in row.Primitive.OrderedOcclusionPlanes) text.Append(':').Append(target?.ToString() ?? "null");
            }
            RegistrationSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
            _sealed = true;
        }
        catch (Exception error) { Failure = error.Message; throw; }
    }
    internal void Observe(IReadOnlyList<FalloutRoomCamera> cameras, IReadOnlyList<FalloutOcclusionGeometry> geometry,
        float units, long actualFrame)
    {
        Require(); source.RequireCurrent();
        if (!_sealed || _entered || cameras.Count == 0 || !float.IsFinite(units) || units <= 0 || actualFrame <= Generation)
            throw new InvalidOperationException("Occlusion consumer has no fresh entered native camera/frame ownership.");
        _entered = true;
        try
        {
            var current = geometry.ToDictionary(row => row.Reference, FalloutFormKeyComparer.Instance);
            if (current.Count != _registrations.Count || _registrations.Any(row => !current.ContainsKey(row.Primitive.Reference)))
                throw new InvalidDataException("Occlusion frame differs from its returning native factory stream.");
            foreach (var row in geometry)
                if (row.Mode != FalloutOcclusionCullMode.Automatic)
                    throw new NotSupportedException("Occlusion mode mutation has no source command/current-state producer.");
            if (_cold is { } cold)
            {
                foreach (var saved in cold.Planes)
                    if (current[saved.Reference].Enabled != saved.Enabled || current[saved.Reference].Mode != saved.Mode)
                        throw new InvalidDataException("Cold occlusion state differs from the restored actual reference enable/mode owner.");
                _cold = null;
            }
            var views = new List<FalloutOcclusionView>();
            foreach (var camera in cameras)
            {
                var cones = new Dictionary<FalloutFormKey, FalloutOcclusionCone>(FalloutFormKeyComparer.Instance);
                foreach (var row in _registrations)
                    if (FalloutOcclusionVisibilityMath.Observe(current[row.Primitive.Reference], camera,
                        source.CoplanarGameUnits * units) is { } cone) cones.Add(row.Primitive.Reference, cone);
                var expressions = new List<(FalloutFormKey?, FalloutOcclusionExpression)>();
                foreach (var row in _registrations)
                    if (cones.TryGetValue(row.Primitive.Reference, out var cone) &&
                        FalloutOcclusionVisibilityMath.Link(row.Primitive, cone, cones, current) is { } expression)
                        expressions.Add((row.Membership.Room, expression));
                views.Add(new(Array.AsReadOnly(_registrations.Where(row => cones.ContainsKey(row.Primitive.Reference))
                    .Select(row => row.Primitive.Reference).ToArray()), expressions.AsReadOnly()));
            }
            _geometry = geometry.ToArray(); Views = views.AsReadOnly(); Generation = actualFrame;
        }
        catch (Exception error) { Failure = error.Message; throw; }
        finally { _entered = false; }
    }
    internal bool Sees(int view, FalloutFormKey? actualRoom, FalloutVisibilitySphere bound)
    {
        Require(); bound.Validate();
        if (!_sealed || Views.Count == 0 || view < 0 || view >= Views.Count)
            throw new NotSupportedException("Occlusion visibility has no actual current native view.");
        return Views[view].Expressions.Where(row => row.Room is null || row.Room == actualRoom)
            .All(row => row.Expression.Sees(bound));
    }
    internal FalloutOcclusionSnapshot Capture()
    {
        Require();
        if (!_sealed || _entered || Views.Count == 0 || Generation == 0 || _cold is not null)
            throw new NotSupportedException("Occlusion capture has no returned current native frame.");
        var current = _geometry.ToDictionary(row => row.Reference, FalloutFormKeyComparer.Instance);
        return new(source.EngineSha256, source.ContractSha256, cell, RegistrationSha256, Generation,
            Array.AsReadOnly(_registrations.Select(row => new FalloutOcclusionPlaneSnapshot(row.Primitive.Reference,
                row.Primitive.SourceSha256, row.Membership.SourceSha256, row.Membership.Room,
                current[row.Primitive.Reference].Enabled, current[row.Primitive.Reference].Mode)).ToArray()), Failure);
    }
    internal void Restore(FalloutOcclusionSnapshot saved)
    {
        Require(); saved.Validate();
        if (!_sealed || Generation != 0 || Views.Count != 0 || saved.EngineSha256 != source.EngineSha256 ||
            saved.ContractSha256 != source.ContractSha256 || saved.Cell != cell || saved.RegistrationSha256 != RegistrationSha256 ||
            saved.Planes.Count != _registrations.Count || !saved.Planes.Select(row => row.Reference)
                .SequenceEqual(_registrations.Select(row => row.Primitive.Reference)))
            throw new InvalidDataException("Cold occlusion graph differs from its winning source/factory order.");
        foreach (var savedRow in saved.Planes)
        {
            var row = _planes[savedRow.Reference];
            if (savedRow.SourceSha256 != row.Primitive.SourceSha256 || savedRow.MembershipSha256 != row.Membership.SourceSha256 ||
                savedRow.Room != row.Membership.Room)
                throw new InvalidDataException("Cold occlusion plane lost its exact source/membership identity.");
        }
        if (saved.Failure is not null) throw new NotSupportedException("Cold source occlusion retains a failed consumer: " + saved.Failure);
        Generation = saved.LastNativeGeneration; _cold = saved;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_entered || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Source occlusion retirement crossed its entered frame/product caller.");
        _retired = true; Views = []; _geometry = []; _cold = null;
    }
    private void Require()
    {
        if (_retired || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Source occlusion graph lost its product-thread lifetime.");
        if (Failure is not null) throw new NotSupportedException("Source occlusion consumer retains its failure: " + Failure);
    }
}
