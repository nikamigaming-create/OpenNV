using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers
{
    private FalloutSkyCloudResource? _sourceCloudResource;
    private SourceCloudResetChild? _sourceCloudChild;
    private FalloutSkyTransferState? _sourceTransfer;
    private readonly Dictionary<int, string> _sourceCloudTextures = [];
    private readonly Dictionary<int, uint> _sourceCloudBlend = [];
    internal void BindSourceReset(FalloutSkyTransferState source)
    {
        if (_sourceCloudChild is not null) throw new InvalidOperationException("Clouds source reset already has an actual native field owner.");
        if (_sourceCloudResource is not { } resource || _clouds.Count != resource.Slots.Count ||
            _clouds.Select(row => row.Layer).Distinct().Count() != _clouds.Count)
            throw new InvalidDataException("Clouds publication does not match the authored NIF child-list denominator.");
        source.BindPresentationThread();
        var child = new SourceCloudResetChild(this, source, resource);
        source.AttachChild(child); _sourceCloudChild = child; _sourceTransfer = source;
        source.MarkExteriorFactoryEntered();
    }
    private void RecordSourceCloudTexture(int layer, string path)
    {
        _sourceCloudTextures[layer] = path;
        // Only the already admitted single-texture branch is published by
        // Sample. Weather blending and the other source texture writer stay
        // visibly unowned; they are not inferred from this registration.
        _sourceCloudBlend[layer] = 0;
    }
    private sealed class SourceCloudResetChild(RuntimeNativeSkyLayers node, FalloutSkyTransferState sky,
        FalloutSkyCloudResource source) : IFalloutSkyResetChild
    {
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private bool _retired;
        public Guid Identity { get; } = Guid.NewGuid();
        public Guid Sky => sky.Identity;
        public string Role => "Clouds";
        private void Require()
        {
            if (_retired || System.Environment.CurrentManagedThreadId != _thread || !GodotObject.IsInstanceValid(node) ||
                !node.IsInsideTree() || node.IsQueuedForDeletion())
                throw new InvalidOperationException("Source Clouds lost its actual native/thread/field lifetime.");
            foreach (var (_, material, _) in node._clouds)
                if (!GodotObject.IsInstanceValid(material) || material.ResourceName != NativeNifSkyMaterial.Identity)
                    throw new InvalidDataException("Clouds source property retired or changed its original material owner.");
        }
        public FalloutSkyChildSnapshot Capture()
        {
            Require();
            var slots = source.Slots.Select(slot =>
            {
                var (_, material, _) = node._clouds.Single(row => row.Layer == slot.Slot);
                var sampler = material.GetShaderParameter("cloud_map");
                var texture = sampler.VariantType == Variant.Type.Nil ? null : sampler.AsGodotObject() as Texture2D ??
                    throw new InvalidDataException("Source cloud sampler has another native resource type.");
                var path = node._sourceCloudTextures.GetValueOrDefault(slot.Slot);
                if ((path is null) != (texture is null) || path is not null &&
                    (!node._textures.TryGetValue(path, out var owned) || texture!.GetInstanceId() != owned.GetInstanceId()))
                    throw new InvalidDataException("Cloud sampler no longer names its actual source-owned texture handle.");
                return new FalloutSkyCloudSlotSnapshot(slot.Slot, slot.Geometry, slot.Property, path, path,
                    node._sourceCloudBlend.GetValueOrDefault(slot.Slot));
            }).ToArray();
            return new(Role, source.SourceSha256, source.Resource, slots);
        }
        public FalloutSkyChildResetReturn Reset(FalloutSkyResetContext context)
        {
            Require();
            if (context.Sky != Sky || context.SourceContract != sky.Source.Contract)
                throw new InvalidDataException("Cloud reset came from another original Sky call.");
            // NIF/shader typing alone does not establish the original runtime
            // dynamic cast. Keep that factory boundary separate from actual
            // Godot sampler cleanup, which is still fully owned below.
            throw new NotSupportedException(source.ResetPropertyUnowned);
        }
        private void ReleaseNativeSamplers()
        {
            Require();
            var completed = new List<string>();
            foreach (var slot in source.Slots)
            {
                Require();
                // The slot and shader are two owning references. Drop the
                // property sampler before releasing our native resource ref.
                node._sourceCloudTextures.Remove(slot.Slot); completed.Add(slot.Slot + "/texture-pointer-null");
                var (_, material, _) = node._clouds.Single(row => row.Layer == slot.Slot);
                material.SetShaderParameter("cloud_map", default(Variant));
                if (material.GetShaderParameter("cloud_map").VariantType != Variant.Type.Nil)
                    throw new InvalidOperationException("Cloud source sampler did not release its actual Godot binding.");
                completed.Add(slot.Slot + "/property-texture-null");
                node._sourceCloudBlend[slot.Slot] = 0; completed.Add(slot.Slot + "/property-Float32-positive-zero");
            }
            // Cache handles belong only to this view. Failed releases stay in
            // the cache; no pre-existing/borrowed texture is force-freed.
            var failures = new List<Exception>();
            foreach (var (path, texture) in node._textures.ToArray())
            {
                try { texture.Dispose(); node._textures.Remove(path); }
                catch (Exception failure) { failures.Add(failure); }
            }
            node._weather = null; node._weatherFields = []; node._cloudRates = new float[4];
            if (failures.Count != 0) throw new AggregateException("Source Clouds sampler release retains its actual resource failures.", failures);
            _ = Capture();
        }
        public void Retire()
        {
            if (_retired) return;
            Require();
            ReleaseNativeSamplers(); _retired = true;
        }
    }
    private void RetireSourceCloudProjection()
    {
        if (_sourceCloudChild is not { } child) return;
        try { child.Retire(); }
        catch (Exception error)
        {
            _sourceTransfer?.RetainNativeChildRetirementFailure(child, error); throw;
        }
        _sourceTransfer?.LoseNativeChild(child, "source-Sky-Clouds-native-root-retired-original-field-reconstruction-pending");
        // A successful view release does not turn the original field into a
        // new constructor null. Reconstruction is an independent source owner.
    }
    public override void _ExitTree() => RetireSourceCloudProjection();
}
