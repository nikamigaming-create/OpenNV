using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct RuntimeNativeNifSceneMaterialTarget(int Asset, RuntimeNativeNifGeometryMaterial Binding);

// Presentation owns this borrower and its native resources. Logical state is
// complete without it, including under a renderer failure or no graphics API.
internal sealed partial class RuntimeNativeNifLogicalSceneMaterials : Node
{
    private sealed record Target(NativeNifSceneAlphaMaterial Material, FalloutNifSceneProperty? MaterialProperty);
    private sealed record Group(FalloutNifSceneProperty Property, List<Target> Targets);
    private readonly FalloutActorLogicalScene _scene;
    private readonly Func<bool> _living;
    private readonly Dictionary<FalloutNifSceneKey, Group> _groups = [];
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private bool _retired;
    private FalloutNifSceneKey? _current;
    internal RuntimeNativeNifLogicalSceneMaterials(FalloutActorLogicalScene scene,
        IEnumerable<RuntimeNativeNifSceneMaterialTarget> targets, Func<bool> living)
    {
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(targets); ArgumentNullException.ThrowIfNull(living);
        _scene = scene; _living = living; Name = "LogicalSceneMaterialConsumer";
        try
        {
            var geometry = scene.Objects.Where(node => node.IsGeometry).ToDictionary(node => node.Key);
            var resources = new Dictionary<Material, (FalloutNifSceneKey Shader, FalloutNifSceneKey? Material)>(ReferenceEqualityComparer.Instance);
            var presented = new HashSet<FalloutNifSceneKey>();
            foreach (var target in targets)
            {
                var key = new FalloutNifSceneKey(target.Asset, target.Binding.GeometryBlock);
                if (!geometry.TryGetValue(key, out var node) || node.Source.Block.Index != target.Binding.GeometryBlock ||
                    target.Binding.SourceSha256 != scene.ReadAssetSourceSha256(target.Asset))
                    throw new InvalidDataException("Logical material target changed its actual source geometry identity.");
                if (node.Shader is not { ShaderKind: not null and not -1 } property) continue;
                presented.Add(key);
                _current = property.Key;
                var materialProperties = ((FalloutNifGeometry)node.Source).Properties.Where(index => index >= 0)
                    .Select(index => scene.RequireProperty(new(target.Asset, index))).Where(value => value.MaterialAlphaBits is not null).Take(2).ToArray();
                if (materialProperties.Length > 1) throw new NotSupportedException("source-geometry-current-material-property-ambiguous");
                var materialProperty = materialProperties.FirstOrDefault();
                if (resources.TryGetValue(target.Binding.Material, out var previous))
                {
                    if (previous != (property.Key, materialProperty?.Key))
                        throw new InvalidDataException("A native material aliases independent logical shader/material properties.");
                    continue;
                }
                resources.Add(target.Binding.Material, (property.Key, materialProperty?.Key));
                if (!_groups.TryGetValue(property.Key, out var group)) _groups.Add(property.Key, group = new(property, []));
                group.Targets.Add(new(new(target.Binding.Material), materialProperty));
            }
            var expected = geometry.Values.Where(node => node.Shader is { ShaderKind: not null and not -1 }).Select(node => node.Key).ToHashSet();
            if (!expected.SetEquals(presented)) throw new NotSupportedException("source-logical-scene-alpha-presented-geometry-target-missing");
            BorrowCurrent();
        }
        catch (Exception error) { Failure = error.Message; }
    }
    internal string? Failure { get; private set; }
    internal object State => new
    {
        factory = _scene.Factory,
        lifetime = _scene.Lifetime,
        retired = _retired,
        failure = Failure,
        current = _current,
        properties = _groups.Count,
        materials = _groups.Values.Sum(group => group.Targets.Count),
        stores = _groups.Values.Sum(group => group.Targets.Sum(target => target.Material.Stores)),
        parity = NativeNifSceneAlphaMaterial.Parity,
    };
    internal void BorrowCurrent()
    {
        if (_retired) throw new ObjectDisposedException(nameof(RuntimeNativeNifLogicalSceneMaterials));
        if (Failure is not null) throw new NotSupportedException(Failure);
        if (_thread != System.Environment.CurrentManagedThreadId || !_living())
            throw new InvalidOperationException("Logical material borrower changed its actual native lifetime/thread.");
        foreach (var (key, group) in _groups)
        {
            _current = key;
            var actual = _scene.RequireProperty(key);
            if (!ReferenceEquals(actual, group.Property) || actual.ShaderAlphaBits is not { } bits)
                throw new InvalidDataException("Logical material borrower lost its retained property object.");
            foreach (var target in group.Targets)
            {
                if (target.MaterialProperty is { } material)
                {
                    if (!ReferenceEquals(_scene.RequireProperty(material.Key), material) || material.MaterialAlphaBits is not { } materialBits)
                        throw new InvalidDataException("Logical material borrower lost its independent current material cell.");
                    target.Material.BorrowMaterialAlpha(materialBits);
                }
                target.Material.Borrow(bits);
            }
        }
    }
    public override void _Process(double delta)
    {
        if (_retired || Failure is not null) return;
        try { BorrowCurrent(); }
        catch (Exception error) { Failure = error.Message; GD.PushError("OPENNV_LOGICAL_SCENE_MATERIAL_DIVERGENCE " + error.Message); }
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Logical material retirement changed its actual owner thread.");
        try
        {
            NativeNifSceneRetirement.Drain("Logical scene material targets",
                _groups.Values.SelectMany(group => group.Targets).Select(target => (Action)target.Material.Retire));
        }
        finally { _retired = true; }
    }
    public override void _ExitTree() => Retire();
}
