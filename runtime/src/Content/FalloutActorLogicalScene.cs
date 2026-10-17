using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// The model factory owns this mutable source scene. No native node, camera,
// mesh visibility or renderer pose can supply its fields.
internal sealed partial class FalloutActorLogicalScene
{
    internal const string Schema = "opennv-actor-logical-scene/v7";
    internal const uint FixedBound = 0x2000;
    internal const uint UpdateBound = 0x80000;
    internal const uint LoadedActiveFlags = 0x8000e;
    internal const uint ClearedLoadFlag = 0x10;
    internal const string BoundLane = "CSharp-current-source-local-world-and-skin-spheres;canonical-machine-and-animation-blend-parity-unverified";
    internal const string AttachmentLane = "owned-selected-source-model-Prn-or-external-skin-publication;canonical-retail-assembly-graft-parity-unverified";
    private readonly FalloutActorSourceModel _model;
    private readonly Dictionary<FalloutNifSceneKey, FalloutNifSceneObject> _objects = [];
    private readonly Dictionary<FalloutNifSceneKey, FalloutNifSceneProperty> _properties = [];
    private readonly Dictionary<string, FalloutNifSceneObject> _bones = new(StringComparer.Ordinal);
    private readonly List<FalloutNifSceneObject> _order = [];
    private readonly List<FalloutActorLogicalSceneAsset> _assets = [];
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence;
    private bool _busy, _retired;
    private long _reentry;
    private string? _failure;
    private Guid? _restoredLifetime;
    private FalloutNifSceneAlphaCall? _alpha;
    internal FalloutActorLogicalScene(FalloutActorSourceModel model, FalloutActorLogicalSceneSnapshot? saved = null)
    {
        ArgumentNullException.ThrowIfNull(model); _model = model;
        AddAsset(0, model.Appearance.SkeletonPath, model.Skeleton);
        var roots = model.Skeleton.Roots.Where(block => block >= 0).ToArray();
        if (roots.Length != 1 || !_objects[new(0, roots[0])].IsNode)
            throw new NotSupportedException("source-Actor-logical-scene-single-returned-node-root-unowned");
        Root = _objects[new(0, roots[0])];
        foreach (var node in _objects.Values)
            if (node.IsNode && !_bones.TryAdd(node.Name, node))
                throw new InvalidDataException("Logical source skeleton has ambiguous node identity.");
        for (var index = 0; index < model.Parts.Count; index++)
        {
            var part = model.Parts[index]; var asset = index + 1;
            AddAsset(asset, part.Selection.ModelPath ?? throw new InvalidDataException("Logical Actor part has no source resource."), part.Physical);
            foreach (var block in part.Physical.Roots.Where(block => block >= 0))
            {
                var child = _objects[new(asset, block)];
                if (child.Source is not FalloutNifNode declaration)
                    throw new NotSupportedException("source-Actor-part-returned-root-node-cast-unowned");
                var parents = declaration.ExtraData.Where(reference => reference >= 0).Select(part.Physical.Source.ReadObject)
                    .OfType<FalloutNifStringExtraData>().Where(value => value.Name == "Prn").Select(value => value.Value).ToArray();
                if (parents.Length > 1) throw new InvalidDataException("Actor part has ambiguous source parent publication.");
                var parent = parents.Length == 1 ? RequireBone(parents[0]) : Root;
                if (parents.Length == 0 && Descendants(child).Any(node => node.IsGeometry && ((FalloutNifGeometry)node.Source).SkinInstance < 0))
                    throw new NotSupportedException("source-Actor-logical-rigid-part-head-model-space-publication-unowned");
                Attach(parent, child);
            }
        }
        RebuildOrder();
        if (saved is null) UpdateWorldAndBounds(); else Restore(saved);
    }
    internal Guid Factory => _model.Factory;
    internal FalloutActorSourceModel SourceModel => _model;
    internal Guid Lifetime => _lifetime;
    internal FalloutNifSceneObject Root { get; }
    internal IReadOnlyList<FalloutNifSceneObject> Objects => _order;
    internal IReadOnlyCollection<FalloutNifSceneProperty> Properties => _properties.Values;
    internal string SkeletonPath => _model.Appearance.SkeletonPath;
    internal string SkeletonSha256 => _model.Skeleton.Source.Sha256;
    internal string ReadAssetSourceSha256(int asset)
    {
        RequireIdle();
        if (asset < 0 || asset > _model.Parts.Count) throw new InvalidDataException("Logical renderer selected an absent source asset slot.");
        return Asset(asset).Sha256;
    }
    internal string? SaveBlocker => _retired ? "source-Actor-logical-scene-retired" : _failure ??
        (_busy ? "source-Actor-logical-scene-operation-entered" : RagdollTemplatePoseBoundary);
    internal object State => new
    {
        factory = Factory,
        lifetime = _lifetime,
        _sequence,
        _restoredLifetime,
        assets = _assets,
        root = Root.Key,
        rootBound = Root.Bound,
        objects = _objects.Count,
        properties = _properties.Values.Select(property => property.Capture()).ToArray(),
        alpha = _alpha,
        floatExtraData = CaptureFloatExtraData(),
        visibility = CaptureVisibility(),
        morphs = CaptureMorphs(),
        initialModelUpdate = _initialModelUpdate,
        controllerModelPreparation = _controllerModelPreparation,
        ragdollTemplatePose = _ragdollTemplatePose,
        ragdollWorldUpdates = _ragdollWorldUpdates,
        bounds = BoundLane,
        attachments = AttachmentLane,
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };
    private void AddAsset(int asset, string path, FalloutNifPhysicalModel model)
    {
        _assets.Add(new(asset, path, model.Source.Sha256, Array.AsReadOnly(model.Roots.ToArray())));
        foreach (var slot in model.Scene)
        {
            var (local, flags) = slot.Declaration switch
            {
                FalloutNifNode node => (node.Transform, node.Flags),
                FalloutNifGeometry geometry => (geometry.Transform, geometry.Flags),
                FalloutNifAmbientLight light => (light.Transform, light.Flags),
                FalloutNifPointLight light => (light.Light.Transform, light.Light.Flags),
                _ => throw new NotSupportedException("source-logical-scene-current-object-class-unowned:" + slot.Declaration.Block.TypeName),
            };
            var key = new FalloutNifSceneKey(asset, slot.Declaration.Block.Index);
            var current = new FalloutNifSceneObject(key, slot.Declaration, local, (flags & ~ClearedLoadFlag) | LoadedActiveFlags);
            _objects.Add(key, current);
            AddFloatExtraData(current, model.Source);
            AddVisualFields(current, model.Source);
            var references = slot.Declaration switch { FalloutNifNode node => node.Properties, FalloutNifGeometry geometry => geometry.Properties, _ => [] };
            foreach (var index in references.Where(index => index >= 0))
            {
                var propertyKey = new FalloutNifSceneKey(asset, index);
                if (!_properties.TryGetValue(propertyKey, out var property))
                { property = new(propertyKey, model.Source.ReadObject(index)); _properties.Add(propertyKey, property); }
                // The original property-state slot is selected by property class,
                // independently from each shader's serialized specialization.
                if (property.ShaderKind is not null)
                {
                    if (current.Shader is not null) throw new InvalidDataException("Geometry has multiple current slot-three shader properties.");
                    current.Shader = property;
                }
                else if (property.Source.Block.TypeName is "NiShadeProperty")
                    throw new NotSupportedException("source-scene-plain-shade-runtime-extension-unowned");
            }
        }
        foreach (var slot in model.Scene)
        {
            var node = _objects[new(asset, slot.Declaration.Block.Index)];
            node.Parent = slot.Parent is { } parent ? _objects[new(asset, parent)] : null;
            foreach (var block in slot.Children) node.Children.Add(block < 0 ? null : _objects[new(asset, block)]);
        }
    }
    private FalloutNifFile Asset(int slot) => slot == 0 ? _model.Skeleton.Source : _model.Parts[slot - 1].Physical.Source;
    private FalloutNifSceneObject RequireBone(string name) => _bones.GetValueOrDefault(name) ??
        throw new NotSupportedException("source-logical-scene-skin-or-parent-node-unavailable:" + name);
    private static IEnumerable<FalloutNifSceneObject> Descendants(FalloutNifSceneObject root)
    {
        var pending = new Stack<FalloutNifSceneObject>(); pending.Push(root);
        while (pending.TryPop(out var next))
        {
            yield return next;
            for (var index = next.Children.Count - 1; index >= 0; index--) if (next.Children[index] is { } child) pending.Push(child);
        }
    }
    private static void Attach(FalloutNifSceneObject parent, FalloutNifSceneObject child)
    {
        if (!parent.IsNode || child.Parent is not null) throw new InvalidOperationException("Logical scene attachment changed a retained parent.");
        child.Parent = parent; parent.Children.Add(child);
    }
    private void RebuildOrder()
    {
        _order.Clear(); var seen = new HashSet<FalloutNifSceneKey>();
        foreach (var node in Descendants(Root))
        { if (!seen.Add(node.Key)) throw new InvalidDataException("Current logical Actor scene repeats or cycles."); _order.Add(node); }
        if (seen.Count != _objects.Count) throw new InvalidDataException("Selected logical scene omitted source objects from its published tree.");
    }
    internal void PublishBoneSample(string sourceName, FalloutNifAnimationSample sample)
    {
        RequireIdle(); var node = RequireBone(sourceName);
        var local = node.Local;
        node.Local = new(sample.Translation ?? local.Translation,
            sample.Rotation is { } quaternion ? FalloutNifSceneMath.Rotation(quaternion) : (float[])local.RotationRowMajor.Clone(), sample.Scale ?? local.Scale);
        FalloutNifSceneMath.Validate(node.Local); node.PoseStores = checked(node.PoseStores + 1); Next();
    }
    internal void PublishLocalPose(FalloutNifSceneKey key, FalloutNifTransform transform)
    {
        RequireIdle(); FalloutNifSceneMath.Validate(transform); var node = _objects.GetValueOrDefault(key) ??
            throw new InvalidDataException("Source pose publisher selected an absent logical scene object.");
        node.Local = FalloutNifSceneMath.Copy(transform); node.PoseStores = checked(node.PoseStores + 1); Next();
    }
    internal void ResetBonePoses(string identityTarget)
    {
        RequireIdle();
        foreach (var bone in _bones.Values)
        {
            var declared = (FalloutNifNode)bone.Source;
            bone.Local = declared.Name == identityTarget ? FalloutNifSceneMath.Identity : FalloutNifSceneMath.Copy(declared.Transform);
            bone.PoseStores = checked(bone.PoseStores + 1); Next();
        }
    }
    internal void RetainUnownedPose(string boundary)
    { RequireIdle(allowFailure: true); ArgumentException.ThrowIfNullOrWhiteSpace(boundary); _failure ??= boundary; Next(); }
    internal void UpdateWorldAndBounds()
    {
        RequireIdle(); _busy = true;
        try
        {
            var anchor = FalloutNifSceneMath.Identity with { Scale = _model.Appearance.Height };
            foreach (var node in _order)
            {
                node.World = FalloutNifSceneMath.Compose(node.Parent?.World ?? anchor, node.Local); Next();
            }
            UpdateCurrentBounds();
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
        finally { _busy = false; }
    }
    private void UpdateCurrentBounds()
    {
        // Geometry skin palettes may refer to bones elsewhere in the tree;
        // all current world transforms must publish before the bound pass.
        foreach (var node in _order)
            if (node.IsGeometry) UpdateGeometryBound(node);
        for (var index = _order.Count - 1; index >= 0; index--)
            if (_order[index].IsNode) UpdateNodeBound(_order[index]);
    }
    private void UpdateGeometryBound(FalloutNifSceneObject node)
    {
        if (((node.Flags & UpdateBound) == 0 || (node.Flags & FixedBound) != 0) &&
            node.Bound is { } previous && previous.Sphere.Radius != 0) return;
        var geometry = (FalloutNifGeometry)node.Source; var source = Asset(node.Key.Asset);
        FalloutNifSceneSphere next;
        if (geometry.SkinInstance < 0)
        {
            var mesh = source.ReadMeshData(geometry.Data);
            next = FalloutNifSceneMath.Transform(new(mesh.Center, mesh.Radius), node.World);
        }
        else
        {
            var instance = source.ReadObject(geometry.SkinInstance) as FalloutNifSkinInstance ??
                throw new InvalidDataException("Current scene geometry lost its source skin instance.");
            var skin = source.ReadObject(instance.Data) as FalloutNifSkinData ?? throw new InvalidDataException("Current scene skin has no sphere data.");
            if (skin.Bones.Length == 0 || skin.Bones.Length != instance.Bones.Length)
                throw new InvalidDataException("Current skin bound lost its complete ordered bone palette.");
            FalloutNifSceneSphere? combined = null;
            for (var index = 0; index < instance.Bones.Length; index++)
            {
                var bone = RequireBone(source.ReadNode(instance.Bones[index]).Name); var data = skin.Bones[index];
                var sphere = FalloutNifSceneMath.Transform(new(data.BoundingSphereCenter, data.BoundingSphereRadius), bone.World);
                combined = combined is { } current ? FalloutNifSceneMath.Merge(current, sphere) : sphere;
            }
            // The skin producer returns geometry-local bounds through the
            // current skeleton-root inverse and retained skin transform. The
            // geometry updater then applies its own current world transform.
            var rootDeclaration = source.ReadNode(instance.SkeletonRoot);
            var skeletonRoot = source.Roots.Contains(instance.SkeletonRoot)
                ? _objects[new(node.Key.Asset, instance.SkeletonRoot)] : RequireBone(rootDeclaration.Name);
            var localFrame = FalloutNifSceneMath.Compose(skin.SkinTransform, FalloutNifSceneMath.Inverse(skeletonRoot.World));
            next = FalloutNifSceneMath.Transform(FalloutNifSceneMath.Transform(combined!.Value, localFrame), node.World);
        }
        StoreBound(node, next);
    }
    private void UpdateNodeBound(FalloutNifSceneObject node)
    {
        if ((node.Flags & FixedBound) != 0 || (node.Flags & UpdateBound) == 0) return;
        FalloutNifSceneSphere? combined = null;
        foreach (var child in node.Children)
            if (child?.Bound is { } bound && bound.Sphere.Radius != 0)
                combined = combined is { } current ? FalloutNifSceneMath.Merge(current, bound.Sphere) : bound.Sphere;
        // No eligible child leaves the old nullable owner and its values intact.
        if (combined is { } next) StoreBound(node, next);
    }
    private void StoreBound(FalloutNifSceneObject node, FalloutNifSceneSphere sphere)
    {
        node.Bound = FalloutNifSceneBound.From(sphere, node.Bound?.Identity); node.BoundStores = checked(node.BoundStores + 1); Next();
    }
    internal FalloutNifSceneProperty RequireProperty(FalloutNifSceneKey key)
    {
        RequireIdle(); return _properties.GetValueOrDefault(key) ?? throw new InvalidDataException("Logical material consumer selected an absent original property.");
    }
    internal void PublishMaterialAlpha(FalloutNifSceneKey key, uint bits)
    { RequireIdle(); RequireProperty(key).StoreMaterialAlpha(bits); Next(); }
    internal uint? ReadCurrentRadiusBits() { RequireIdle(); return Root.Bound?.Radius; }
    internal void Retire()
    {
        if (_retired) return;
        RequireIdle(allowFailure: true); _retired = true; Next();
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireIdle(bool allowFailure = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Logical source scene changed its actual owning thread.");
        if (_busy) { ++_reentry; throw new InvalidOperationException("Logical source scene cannot reenter a current publication."); }
        if (!allowFailure && _failure is { } failure) throw new NotSupportedException(failure);
        if (!allowFailure && RagdollTemplatePoseBoundary is { } temporary) throw new NotSupportedException(temporary);
    }
}
