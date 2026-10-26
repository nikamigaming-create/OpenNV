using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
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
    private readonly Dictionary<FalloutFormKey, (FalloutPlacedReference Source, Node3D Node)> _warmNodes = [];
    private readonly Dictionary<FalloutFormKey, FalloutPlacedReference> _referencesByKey = [];
    private readonly HashSet<FalloutFormKey> _unresolvable = [];
    private readonly Dictionary<FalloutFormKey, bool> _enabled;
    private long _publishedStateRevision = -1, _publishedAppearanceRevision = -1;
    private readonly Dictionary<FalloutFormKey, GeometryInstance3D[]> _fadeGeometry = [];
    private readonly Dictionary<FalloutFormKey, float> _publishedOpacity = [];
    private readonly FalloutReferenceFadeSettings _fadeSettings;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    internal event Action<FalloutFormKey, Node3D>? Materialized;
    private Action<OpenNV.Runtime.World.Actors.RuntimeNativeNpc>? _synchronizeActorAppearance;
    internal Action<OpenNV.Runtime.World.Actors.RuntimeNativeNpc>? SynchronizeActorAppearance
    {
        get => _synchronizeActorAppearance;
        set { _synchronizeActorAppearance = value; _publishedAppearanceRevision = -1; }
    }
    internal string? Error { get; private set; }
    internal IEnumerable<OpenNV.Runtime.World.Actors.RuntimeNativeNpc> Actors => _nodes.Values.OfType<OpenNV.Runtime.World.Actors.RuntimeNativeNpc>();
    internal IReadOnlyDictionary<FalloutFormKey, Node3D> Nodes => _nodes;
    internal int WarmNodeCount => _warmNodes.Count;
    internal bool HasPrepared(FalloutFormKey key) => _nodes.ContainsKey(key) || _warmNodes.ContainsKey(key) || _unresolvable.Contains(key);

    internal async Task PrepareActorAudioTransferAsync(IReadOnlyList<Node> sourceProducers)
    {
        var sourceReferences = _nodes.Keys.Concat(_warmNodes.Keys).Distinct().Where(key =>
            (_nodes.GetValueOrDefault(key) ?? _warmNodes.GetValueOrDefault(key).Node) is
                OpenNV.Runtime.World.Actors.RuntimeNativeNpc or OpenNV.Runtime.World.Actors.RuntimeNativeCreature).ToHashSet();
        if (sourceReferences.Any(key => !_world.Get(key).AnimationSoundEvents.CanCapture))
        {
            var tree = GetTree(); var previousPause = tree.Paused;
            try
            {
                using var sounds = _world.PrepareSourceAudioTransfer(sourceReferences);
                using var producers = new RuntimeNativeSaveProducerPause(sourceProducers);
                tree.Paused = true;
                sounds.Activate();
                var started = Time.GetTicksMsec();
                while (sounds.ObservePending().Count != 0)
                {
                    if (Time.GetTicksMsec() - started > RuntimeManualSavePreparation.MaximumWaitMilliseconds)
                        throw new TimeoutException("Actor handoff timed out awaiting its original finite native sound completion.");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (!IsInsideTree() || IsQueuedForDeletion())
                        throw new InvalidOperationException("Actor handoff lost its resident presentation.");
                    producers.Validate();
                }
            }
            finally { tree.Paused = previousPause; }
        }
        foreach (var key in sourceReferences)
            if (!_world.Get(key).AnimationSoundEvents.CanCapture) _ = _world.Get(key).AnimationSoundEvents.Capture();
    }

    internal void RetireOverlappingActors(IReadOnlyList<FalloutPlacedReference> destination,
        out IReadOnlyList<(FalloutFormKey Reference, Transform3D Pose, Vector3 Velocity)> previous)
    {
        previous = [];
        var actors = destination.Select(reference => reference.FormKey).Distinct().Select(key =>
            (Key: key, Node: _nodes.GetValueOrDefault(key) ?? _warmNodes.GetValueOrDefault(key).Node))
            .Where(value => value.Node is OpenNV.Runtime.World.Actors.RuntimeNativeNpc or OpenNV.Runtime.World.Actors.RuntimeNativeCreature)
            .Select(value => (value.Key, Node: (CharacterBody3D)value.Node!, Resident: _nodes.ContainsKey(value.Key))).ToArray();
        // Warm actors also retain bindings, despite being outside this grid.
        foreach (var (key, node, _) in actors)
        {
            if (!node.IsInsideTree() || node.IsQueuedForDeletion())
                throw new InvalidOperationException($"Overlapping actor {key} has no live presentation to retire.");
            _ = _world.Get(key).Capture();
        }
        previous = actors.Where(value => value.Resident)
            .Select(value => (value.Key, value.Node.GlobalTransform, value.Node.Velocity)).ToArray();
        foreach (var (key, node, _) in actors)
        {
            _nodes.Remove(key); _warmNodes.Remove(key);
            UnbindObjectAnimation(key);
            _fadeGeometry.Remove(key); _publishedOpacity.Remove(key);
            // Free synchronously: QueueFree leaves source callbacks alive
            // until the next frame, after the replacement factory has run.
            RuntimeCoordinator.RetireNativeActorBindings(node);
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(node);
            node.Free();
        }
    }

    internal void RestoreRetiredActors(IReadOnlyList<(FalloutFormKey Reference, Transform3D Pose, Vector3 Velocity)> previous)
    {
        foreach (var (key, pose, velocity) in previous)
        {
            if (Resolve(key) is not CharacterBody3D actor)
                throw new InvalidOperationException($"Previous resident actor {key} could not restore its retained source state.");
            actor.GlobalTransform = pose;
            actor.Velocity = velocity;
        }
    }

    internal void RetireTransferredActor(FalloutFormKey key, OpenNV.Runtime.World.Actors.INativeActorPortal native)
    {
        var actor = native.PortalBody;
        if (!_nodes.TryGetValue(key, out var current) || current != actor || !native.PortalTransferred)
            throw new InvalidOperationException("NPC portal retirement does not own the committed actor.");
        // Retain the completed transition before any replacement binds the same
        // reference. The next residency pass materializes its actual new pose.
        native.PortalCombat.RetireSourceBindings();
        native.RetirePortalBindings();
        foreach (var sound in NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(actor)) sound.RetirePlayback();
        GamebryoReferenceEnableRuntime.Apply(actor, false);
        UnbindObjectAnimation(key);
        _nodes.Remove(key); _fadeGeometry.Remove(key); _publishedOpacity.Remove(key);
        actor.GetParent().RemoveChild(actor);
        actor.QueueFree();
    }

    internal void SetResidency(IReadOnlyList<FalloutPlacedReference> references, Func<FalloutPlacedReference, Node3D?> materialize,
        Func<FalloutPlacedReference, bool>? retainWarm = null)
    {
        var retained = references.Select(reference => reference.FormKey).ToHashSet();
        var previous = _references.ToDictionary(reference => reference.FormKey);
        foreach (var key in _enabled.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            if (_nodes.Remove(key, out var node))
            {
                UnbindObjectAnimation(key);
                // A warm model is only a construction cache. It has left the
                // authoritative resident set, so attached loops unload exactly
                // as they do when its model is freed. Finite voices stay hosted.
                NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(node);
                GamebryoReferenceEnableRuntime.Apply(node, false);
                if (retainWarm?.Invoke(previous[key]) == true) _warmNodes.Add(key, (previous[key], node));
                else
                {
                    RuntimeCoordinator.RetireNativeActorBindings(node);
                    node.QueueFree();
                }
            }
            _enabled.Remove(key); _fadeGeometry.Remove(key); _publishedOpacity.Remove(key);
        }
        foreach (var (key, warm) in _warmNodes.ToArray())
        {
            if (retained.Contains(key))
            {
                _warmNodes.Remove(key);
                Register(key, warm.Node);
            }
            else if (retainWarm?.Invoke(warm.Source) != true)
            {
                _warmNodes.Remove(key);
                RuntimeCoordinator.RetireNativeActorBindings(warm.Node);
                NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(warm.Node);
                warm.Node.QueueFree();
            }
        }
        foreach (var reference in references)
            if (!_enabled.ContainsKey(reference.FormKey))
                _enabled.Add(reference.FormKey, _world.IsEnabled(reference.FormKey));
        _references = references; _materialize = materialize;
        _referencesByKey.Clear();
        foreach (var reference in references) _referencesByKey[reference.FormKey] = reference;
        _unresolvable.Clear();
        _publishedStateRevision = -1; _publishedAppearanceRevision = -1;
    }

    internal RuntimeNativeReferencePresentation(FalloutReferenceWorld world, IReadOnlyList<FalloutPlacedReference> references,
        Func<FalloutPlacedReference, Node3D?> materialize)
    {
        Name = "NativeReferencePresentation";
        _world = world; _references = references; _materialize = materialize;
        foreach (var reference in references) _referencesByKey[reference.FormKey] = reference;
        _enabled = references.ToDictionary(reference => reference.FormKey, reference => world.IsEnabled(reference.FormKey));
        _fadeSettings = FalloutReferenceFadeSettings.Read(FalloutInstallationSettings.Read(
            RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Reference fades need owned installation settings.")));
    }

    internal void Register(FalloutFormKey key, Node3D node)
    {
        _publishedAppearanceRevision = -1;
        if (_nodes.ContainsKey(key)) throw new InvalidOperationException($"Reference {key} is already registered.");
        RefreshGeometry(key, node);
        _nodes.Add(key, node);
        if (node is OpenNV.Runtime.World.Actors.RuntimeNativeNpc actor)
            actor.AppearanceChanged = () => RefreshGeometry(key, node);
        if (node.GetChildren().OfType<RuntimeNativeDestructible>().SingleOrDefault() is { } destruction)
            destruction.ModelChanged = () => RefreshGeometry(key, node);
        GamebryoReferenceEnableRuntime.Apply(node, _world.IsEnabled(key));
        PublishOpacity(key);
    }

    private void RefreshGeometry(FalloutFormKey key, Node3D node)
    {
        UnbindObjectAnimation(key);
        BindObjectAnimation(key, node);
        _fadeGeometry[key] = RuntimeNativeReferenceOpacity.Collect(node);
        _publishedOpacity.Remove(key);
        PublishOpacity(key);
    }

    internal Node3D? TryResolve(FalloutFormKey key) => _unresolvable.Contains(key) ? null : (_enabled.ContainsKey(key) ? Resolve(key) : null);

    internal Node3D? Resolve(FalloutFormKey key)
    {
        if (_nodes.TryGetValue(key, out var node)) return node;
        if (_unresolvable.Contains(key)) return null;
        if (!_referencesByKey.TryGetValue(key, out var source)) return null;
        node = _materialize(source);
        if (node is null)
        {
            _unresolvable.Add(key);
            return null;
        }
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
        if (effect.Kind == FalloutReferenceEffectKind.DoorOpenState)
        {
            var node = Resolve(key) ?? throw new NotSupportedException($"Door state target {key} has no loaded source model.");
            var motion = node.GetChildren().OfType<RuntimeNativeDoorMotion>().SingleOrDefault() ??
                throw new NotSupportedException($"Reference {key} has no source Open/Close motion owner.");
            motion.SetOpen(effect.Enable);
            return;
        }
        if (effect.Kind != FalloutReferenceEffectKind.ReferenceEnable) throw new NotSupportedException("Reference presentation effect is unbound.");
        // This is a command notification. The world update applies queued
        // enable state before the native projection changes visibility.
    }

    internal void Advance(double seconds)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.ReferencePresentation);
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
        foreach (var node in _doorNodes.Values)
        {
            var children = node.GetChildren();
            using var childrenOwner = (Godot.Collections.Array)children;
            foreach (var motion in children.OfType<RuntimeNativeDoorMotion>()) motion.Synchronize();
        }
        var appearanceRevision = _world.AppearanceRevision;
        if (_publishedAppearanceRevision != appearanceRevision)
        {
            foreach (var actor in Actors) SynchronizeActorAppearance?.Invoke(actor);
            _publishedAppearanceRevision = appearanceRevision;
        }
        var stateRevision = _world.PresentationRevision;
        if (_publishedStateRevision == stateRevision) return;
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
        _publishedStateRevision = stateRevision;
    }

    private void PublishOpacity(FalloutFormKey key)
    {
        if (!_fadeGeometry.TryGetValue(key, out var geometry)) return;
        var opacity = _world.Get(key).Opacity;
        if (_publishedOpacity.TryGetValue(key, out var previous) && previous == opacity) return;
        RuntimeNativeReferenceOpacity.Publish(geometry, opacity);
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
            texture = NativeDdsTexture.Load(payload, source);
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
