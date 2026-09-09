using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

// Resident presentation has a shorter lifetime than reference state. Disabled
// models are constructed on demand through the same cell factory as initial
// loading. Texture changes are per-instance and expire when this cell unloads.
internal partial class RuntimeNativeReferencePresentation : Node
{
    private readonly FalloutReferenceWorld _world;
    private IReadOnlyList<FalloutPlacedReference> _references;
    private Func<FalloutPlacedReference, Node3D?> _materialize;
    private readonly Dictionary<FalloutFormKey, Node3D> _nodes = [];
    private readonly Dictionary<FalloutFormKey, bool> _enabled;
    private readonly Dictionary<FalloutFormKey, GeometryInstance3D[]> _fadeGeometry = [];
    private readonly Dictionary<FalloutFormKey, float> _publishedOpacity = [];
    private readonly FalloutReferenceFadeSettings _fadeSettings;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    internal event Action<FalloutFormKey, Node3D>? Materialized;
    internal string? Error { get; private set; }
    internal IEnumerable<OpenNV.Runtime.World.Actors.RuntimeNativeNpc> Actors => _nodes.Values.OfType<OpenNV.Runtime.World.Actors.RuntimeNativeNpc>();
    internal IReadOnlyDictionary<FalloutFormKey, Node3D> Nodes => _nodes;

    internal void SetResidency(IReadOnlyList<FalloutPlacedReference> references, Func<FalloutPlacedReference, Node3D?> materialize)
    {
        var retained = references.Select(reference => reference.FormKey).ToHashSet();
        foreach (var key in _enabled.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            if (_nodes.Remove(key, out var node))
            {
                GamebryoReferenceEnableRuntime.Apply(node, false);
                node.QueueFree();
            }
            _enabled.Remove(key); _fadeGeometry.Remove(key); _publishedOpacity.Remove(key);
        }
        foreach (var reference in references) _enabled.TryAdd(reference.FormKey, _world.IsEnabled(reference.FormKey));
        _references = references; _materialize = materialize;
    }

    internal RuntimeNativeReferencePresentation(FalloutReferenceWorld world, IReadOnlyList<FalloutPlacedReference> references,
        Func<FalloutPlacedReference, Node3D?> materialize)
    {
        Name = "NativeReferencePresentation";
        _world = world; _references = references; _materialize = materialize;
        _enabled = references.ToDictionary(reference => reference.FormKey, reference => world.IsEnabled(reference.FormKey));
        _fadeSettings = FalloutReferenceFadeSettings.Read(FalloutInstallationSettings.Read(
            RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Reference fades need owned installation settings.")));
    }

    internal void Register(FalloutFormKey key, Node3D node)
    {
        _nodes.Add(key, node);
        _fadeGeometry.Add(key, NodeTraversal.SelfAndDescendants<Node3D>(node)
            .Where(child => child.GetMeta("opennv_nif_fade_node", false).AsBool())
            .SelectMany(NodeTraversal.SelfAndDescendants<GeometryInstance3D>).Distinct().ToArray());
        GamebryoReferenceEnableRuntime.Apply(node, _world.IsEnabled(key));
        PublishOpacity(key);
    }

    internal Node3D? Resolve(FalloutFormKey key)
    {
        if (_nodes.TryGetValue(key, out var node)) return node;
        var source = _references.SingleOrDefault(reference => reference.FormKey == key) ??
            throw new NotSupportedException($"Reference {key} is outside this resident presentation.");
        node = _materialize(source);
        if (node is null) return null;
        Register(key, node);
        Materialized?.Invoke(key, node);
        return node;
    }

    internal void Apply(FalloutReferenceScriptEffect effect)
    {
        var key = effect.Target ?? throw new InvalidDataException("Reference presentation effect has no target.");
        if (effect.Kind == FalloutReferenceEffectKind.Texture)
        {
            var node = Resolve(key) ?? throw new NotSupportedException($"Texture target {key} has no loaded model.");
            SwapTexture(node, effect.NodeName ?? throw new InvalidDataException("Texture node is absent."),
                effect.TexturePath ?? throw new InvalidDataException("Texture path is absent."));
            return;
        }
        if (effect.Kind != FalloutReferenceEffectKind.ReferenceEnable) throw new NotSupportedException("Reference presentation effect is unbound.");
        // This is a command notification. The world update applies queued
        // enable state before the native projection changes visibility.
    }

    internal void Advance(double seconds)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        try
        {
            _world.AdvanceEnableChanges(seconds, _fadeSettings, key =>
            {
                if (!_enabled.ContainsKey(key)) return false; // No loaded 3D in this cell.
                if (!_nodes.ContainsKey(key) && _world.IsEnabled(key)) _ = Resolve(key);
                return _fadeGeometry.TryGetValue(key, out var geometry) && geometry.Length != 0;
            });
            Synchronize();
        }
        catch (Exception error) { Error = error.Message; throw; }
    }

    private void Synchronize()
    {
        foreach (var reference in _references)
        {
            var enabled = _world.IsEnabled(reference.FormKey);
            if (_enabled[reference.FormKey] == enabled) { PublishOpacity(reference.FormKey); continue; }
            if (!_nodes.TryGetValue(reference.FormKey, out var node))
            {
                if (!enabled) { _enabled[reference.FormKey] = false; continue; }
                node = Resolve(reference.FormKey);
            }
            if (node is not null) GamebryoReferenceEnableRuntime.Apply(node, enabled);
            _enabled[reference.FormKey] = enabled;
            PublishOpacity(reference.FormKey);
        }
    }

    private void PublishOpacity(FalloutFormKey key)
    {
        if (!_fadeGeometry.TryGetValue(key, out var geometry)) return;
        var opacity = _world.Get(key).Opacity;
        if (_publishedOpacity.TryGetValue(key, out var previous) && previous == opacity) return;
        foreach (var instance in geometry) instance.Transparency = 1 - opacity;
        _publishedOpacity[key] = opacity;
    }

    private void SwapTexture(Node3D reference, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Texture replacement has an empty source node/path.");
        var meshes = reference.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.GetMeta("opennv_nif_source_name", "").AsString() == name).ToArray();
        if (meshes.Length == 0) throw new NotSupportedException($"Texture node {name} has no native geometry binding.");
        if (reference.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Any())
            throw new NotSupportedException("Texture replacement on controller-owned materials requires shared mutation binding.");
        var logical = "textures/" + path.Replace('\\', '/') + ".dds";
        if (!_textures.TryGetValue(logical, out var texture))
        {
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned resources are absent.");
            if (!content.TryRead(logical, null, out var payload, out var source)) throw new FileNotFoundException($"Texture replacement {logical} is absent.");
            using var decoded = new Image();
            if (decoded.LoadDdsFromBuffer(payload) != Godot.Error.Ok || decoded.IsEmpty()) throw new InvalidDataException($"Texture replacement {logical} is invalid.");
            texture = NativeDdsTexture.Create(decoded);
            texture.SetMeta("opennv_source_texture", source); texture.SetMeta("opennv_logical_texture", logical);
            _textures.Add(logical, texture);
        }
        var changes = new List<(MeshInstance3D Mesh, int Surface, Material Material)>();
        foreach (var mesh in meshes)
        {
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var original = mesh.GetActiveMaterial(surface) ?? throw new InvalidDataException("Texture node has no source material.");
                var material = (Material)original.Duplicate();
                switch (material)
                {
                    case StandardMaterial3D standard: standard.AlbedoTexture = texture; break;
                    case ShaderMaterial shader when shader.ResourceName == NativeNifLightingMaterial.ResourceIdentity:
                        shader.SetShaderParameter("base_map", texture); shader.SetShaderParameter("use_base_map", true); break;
                    case ShaderMaterial shader when shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity:
                        shader.SetShaderParameter("source_texture", texture); shader.SetShaderParameter("source_has_texture", true); break;
                    default: throw new NotSupportedException($"Texture material {material.ResourceName} has no diffuse owner.");
                }
                changes.Add((mesh, surface, material));
            }
        }
        foreach (var change in changes) change.Mesh.SetSurfaceOverrideMaterial(change.Surface, change.Material);
    }
}
