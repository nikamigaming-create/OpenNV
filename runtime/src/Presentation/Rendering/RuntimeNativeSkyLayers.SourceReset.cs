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
    private readonly Dictionary<int, string> _sourceCloudPrimary = [];
    private readonly Dictionary<int, string> _sourceCloudSecondary = [];
    private FalloutSkyNativeChildSource? _sourceNativeChildSource;
    private readonly Dictionary<int, uint> _sourceCloudBlend = [];
    internal void BindSourceReset(FalloutSkyTransferState source, Node parent, FalloutNumericIniSettings settings)
    {
        if (_sourceCloudChild is not null) throw new InvalidOperationException("Clouds source reset already has an actual native field owner.");
        if (_sourceCloudResource is not { } resource || _clouds.Count != resource.Slots.Count ||
            _clouds.Select(row => row.Layer).Distinct().Count() != _clouds.Count)
            throw new InvalidDataException("Clouds publication does not match the authored NIF child-list denominator.");
        source.BindPresentationThread();
        _sourceNativeChildSource = FalloutSkyNativeChildSource.Read(source.Source);
        var child = new SourceCloudResetChild(this, source, resource);
        try
        {
            RestoreSourceCloudFields(source.RetainedCloudFields, resource);
            source.AttachChild(child); _sourceCloudChild = child; _sourceTransfer = source;
        }
        catch (Exception failure)
        {
            try { child.Retire(); }
            catch (Exception retirement)
            {
                var retained = new AggregateException("Cloud construction and actual resource retirement both failed.", failure, retirement);
                _sourceCloudChild = child; _sourceTransfer = source;
                source.RetainFailedNativeFactory(child, retained); throw retained;
            }
            throw;
        }
        source.MarkExteriorFactoryEntered();
        BindSourcePrecipitation(source, parent, settings);
    }
    private void RecordSourceCloudPrimary(int layer, string path)
    {
        _sourceCloudPrimary[layer] = path;
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
        private void Require(bool retirement = false)
        {
            if (_retired || System.Environment.CurrentManagedThreadId != _thread || !GodotObject.IsInstanceValid(node) ||
                !retirement && (!node.IsInsideTree() || node.IsQueuedForDeletion()))
                throw new InvalidOperationException("Source Clouds lost its actual native/thread/field lifetime.");
            (node._sourceNativeChildSource ?? throw new NotSupportedException("Clouds has no selected original reset consumer."))
                .Require(sky.Source);
            foreach (var (_, material, _) in node._clouds)
                if (!GodotObject.IsInstanceValid(material) || material.ResourceName != NativeNifSkyMaterial.Identity)
                    throw new InvalidDataException("Clouds source property retired or changed its actual material owner.");
        }
        private Texture2D? Sampler(ShaderMaterial material, string parameter)
        {
            using var value = material.GetShaderParameter(parameter);
            return value.VariantType == Variant.Type.Nil ? null : value.AsGodotObject() as Texture2D ??
                throw new InvalidDataException("Cloud property sampler has another actual native resource type.");
        }
        private void RequireTexture(Texture2D? texture, string? path)
        {
            if ((path is null) != (texture is null) || path is not null &&
                (!node._textures.TryGetValue(path, out var owned) || !GodotObject.IsInstanceValid(texture) ||
                 texture!.GetInstanceId() != owned.GetInstanceId()))
                throw new InvalidDataException("Cloud sampler no longer names its exact source-owned native texture.");
        }
        public FalloutSkyChildSnapshot Capture()
        {
            Require();
            var slots = source.Slots.Select(slot =>
            {
                var (_, material, _) = node._clouds.Single(row => row.Layer == slot.Slot);
                var primary = node._sourceCloudPrimary.GetValueOrDefault(slot.Slot);
                var secondary = node._sourceCloudSecondary.GetValueOrDefault(slot.Slot);
                RequireTexture(Sampler(material, "cloud_map"), primary);
                RequireTexture(Sampler(material, "cloud_map_blend"), secondary);
                using var scalar = material.GetShaderParameter("source_cloud_blend");
                if (scalar.VariantType != Variant.Type.Float ||
                    BitConverter.SingleToUInt32Bits((float)scalar.AsDouble()) != node._sourceCloudBlend.GetValueOrDefault(slot.Slot))
                    throw new InvalidDataException("Cloud property changed its actual source Float32 blend cell.");
                var staged = node._sourceCloudTextures.GetValueOrDefault(slot.Slot);
                if (staged is not null)
                    throw new NotSupportedException("Cloud staged texture has no admitted cross-weather writer/reference lease.");
                return new FalloutSkyCloudSlotSnapshot(slot.Slot, slot.Geometry, slot.Property, staged, secondary,
                    node._sourceCloudBlend.GetValueOrDefault(slot.Slot), primary);
            }).ToArray();
            return new(Role, source.SourceSha256, source.Resource, slots);
        }
        public FalloutSkyChildResetReturn Reset(FalloutSkyResetContext context)
        {
            Require();
            if (context.Sky != Sky || context.SourceContract != sky.Source.Contract)
                throw new InvalidDataException("Cloud reset came from another original Sky call.");
            _ = Capture(); // Validate current native fields before the first source store.
            var completed = new List<string>();
            foreach (var slot in source.Slots)
            {
                Require();
                // The staged array and secondary shader property are separate
                // fields. The primary weather sampler survives this Reset.
                node._sourceCloudTextures.Remove(slot.Slot);
                completed.Add(slot.Slot + "/texture-pointer-null");
                var (_, material, _) = node._clouds.Single(row => row.Layer == slot.Slot);
                material.SetShaderParameter("cloud_map_blend", default(Variant));
                if (Sampler(material, "cloud_map_blend") is not null)
                    throw new InvalidOperationException("Cloud secondary property did not release its actual native sampler.");
                node._sourceCloudSecondary.Remove(slot.Slot);
                completed.Add(slot.Slot + "/property-texture-null");
                material.SetShaderParameter("source_cloud_blend", 0f);
                node._sourceCloudBlend[slot.Slot] = 0;
                completed.Add(slot.Slot + "/property-Float32-positive-zero");
            }
            _ = Capture();
            return new(context, Identity, Role, completed);
        }
        private void ReleaseNativeSamplers()
        {
            Require(retirement: true);
            var failures = new List<Exception>();
            foreach (var slot in source.Slots)
            {
                try
                {
                    var (_, material, _) = node._clouds.Single(row => row.Layer == slot.Slot);
                    material.SetShaderParameter("cloud_map_blend", default(Variant));
                    if (Sampler(material, "cloud_map_blend") is not null)
                        throw new InvalidOperationException("Cloud retirement retains an actual secondary sampler.");
                    node._sourceCloudSecondary.Remove(slot.Slot);
                    material.SetShaderParameter("cloud_map", default(Variant));
                    if (Sampler(material, "cloud_map") is not null)
                        throw new InvalidOperationException("Cloud retirement retains an actual primary sampler.");
                    node._sourceCloudPrimary.Remove(slot.Slot);
                    node._sourceCloudTextures.Remove(slot.Slot);
                    material.SetShaderParameter("source_cloud_blend", 0f); node._sourceCloudBlend[slot.Slot] = 0;
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            foreach (var (path, texture) in node._textures.ToArray())
            {
                try
                {
                    if (!GodotObject.IsInstanceValid(texture) || texture.GetReferenceCount() != 1)
                        throw new InvalidOperationException("Cloud texture retirement retains an actual foreign/native borrower: " + path);
                    texture.Dispose(); node._textures.Remove(path);
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count != 0)
                throw new AggregateException("Cloud retirement retains its actual sampler/resource failures.", failures);
            node._weather = null; node._weatherFields = []; node._cloudRates = new float[4];
        }
        public void Retire()
        {
            if (_retired) return;
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
    public override void _ExitTree()
    {
        var failures = new List<Exception>();
        try { RetireSourceMoonProjection(); } catch (Exception error) { failures.Add(error); }
        try { RetireSourceCloudProjection(); } catch (Exception error) { failures.Add(error); }
        try { RetireSourcePrecipitationProjection(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Source sky native children retained independent retirement failures.", failures);
    }
}
