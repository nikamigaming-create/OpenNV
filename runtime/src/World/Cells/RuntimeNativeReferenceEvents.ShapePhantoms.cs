using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    // A fresh native broadphase cannot reconstruct the original event-membership
    // prefix. Keep active contacts refused until that independent save producer
    // is owned; rebuilding a volume is not a synthetic cold Enter receipt.
    internal string? ContactSaveBlocker => _bindings.Values.FirstOrDefault(binding =>
        binding.Contacts.HasAdmittedContacts) is { } active
        ? $"source-trigger-contact-membership:{active.Reference.FormKey}" : null;

    internal string? ObserveContactContinuation(FalloutReferenceWorld world)
    {
        if (!ReferenceEquals(_world, world) || _scripts is null || !GodotObject.IsInstanceValid(this) ||
            !IsInsideTree() || IsQueuedForDeletion())
            throw new NotSupportedException("Contact continuation lost its genuine current world/native event lifetime.");
        foreach (var binding in _bindings.Values) RequireShapeContacts(binding);
        return ContactSaveBlocker;
    }

    private FalloutReferencePrimitive? ReadContactPrimitive(FalloutFormKey reference,
        FalloutReferenceInstance instance, string signature) => instance.Script is not null || signature == "ACTI"
        ? FalloutReferencePrimitive.Read(_records.GetEffective(reference)) : null;

    private IReadOnlyList<Area3D> BindShapeContacts(FalloutPlacedReference reference,
        FalloutReferenceInstance instance, Node3D? node, FalloutReferencePrimitive? primitive)
    {
        if (node is null) return [];
        var descendants = node.FindChildren("*", "", true, false);
        using var descendantsOwner = (Godot.Collections.Array)descendants;
        var areas = descendants.OfType<Area3D>()
            .Where(area => area.HasMeta("opennv_nif_shape_phantom")).ToArray();
        if (areas.Length == 0) return [];
        if (instance.Reference != reference.FormKey || instance.Base != reference.Base)
            throw new InvalidDataException("Shaped-phantom contact target differs from its source instance.");
        foreach (var area in areas) NativeNifCollisionBuilder.RequirePhantom(area, node);
        if (primitive is not null)
        {
            // The selected source ACTI loader replaces the model root's native
            // collision with XPRM. Two simultaneously active volumes would
            // create events for geometry the original consumer already retired.
            var source = FalloutPrimitiveContactSource.Read(_world.CampaignPlayerRuntimeSource.Receipt.EngineSha256);
            var children = node.GetChildren();
            using var childrenOwner = (Godot.Collections.Array)children;
            var originalRoot = children.OfType<Node3D>().SingleOrDefault(child =>
                child.GetMeta("opennv_nif_root_transform_owner", "").AsString() == "placed-reference");
            var rootAreas = areas.Count(area => area.GetParent() == originalRoot &&
                originalRoot is not null && area.GetMeta("opennv_nif_phantom_target").AsInt32() ==
                originalRoot.GetMeta("opennv_nif_block", -1).AsInt32());
            source.RequireReplacement(_records.GetEffective(reference.Base).Signature, primitive, rootAreas, areas.Length);
            var original = areas.Single();
            var identity = original.GetMeta("opennv_nif_phantom_source").AsString();
            original.Monitoring = false;
            GamebryoReferenceEnableRuntime.SetCollisionFilter(original, 0, 0);
            original.Free();
            node.SetMeta("opennv_source_primitive_retired_phantom", source.ContractSha256 + ":" + identity);
            return [];
        }
        // Native filtering inherits the actual reference's enable metadata.
        // Do not install a second Enabled field on the child volume: it would
        // retain a false value after the real model was re-enabled.
        return NativeNifCollisionBuilder.PlacedPhantoms(node, _collisionMask);
    }

    private void RebindShapeContacts(Binding binding, Node3D? node)
    {
        if (node == binding.Node)
        {
            RequireShapeContacts(binding);
            return;
        }
        // Native replacement must not silently erase the already committed
        // source membership or invent matching cold/re-entry callbacks.
        if (binding.Contacts.HasAdmittedContacts)
            throw new NotSupportedException("Contact-volume replacement requires its actual source membership retirement.");
        var primitive = ReadContactPrimitive(binding.Reference.FormKey, binding.Instance, binding.Signature);
        binding.ShapePhantoms = BindShapeContacts(binding.Reference, binding.Instance, node, primitive);
    }

    private void RequireShapeContacts(Binding binding)
    {
        if (binding.Instance.ScriptError is { } error && error.StartsWith("Contact binding:", StringComparison.Ordinal))
            throw new NotSupportedException(error);
        if (binding.ShapePhantoms.Count == 0) return;
        var node = binding.Node ?? throw new NotSupportedException("Current shaped phantom lost its actual source model.");
        foreach (var area in binding.ShapePhantoms)
        {
            NativeNifCollisionBuilder.RequirePhantom(area, node);
            if (!area.IsInsideTree() || !node.IsInsideTree() || !area.Monitoring)
                throw new NotSupportedException("Current shaped-phantom broadphase owner is unpublished or retired.");
        }
    }

    private IReadOnlyList<FalloutFormKey> SampleShapeContacts(Binding binding)
    {
        RequireShapeContacts(binding);
        var result = new List<FalloutFormKey>(); var observed = new HashSet<FalloutFormKey>();
        IEnumerable<Area3D> volumes = binding.Trigger is { } trigger
            ? new[] { trigger }.Concat(binding.ShapePhantoms) : binding.ShapePhantoms;
        foreach (var volume in volumes)
        {
            if (!GodotObject.IsInstanceValid(volume) || volume.IsQueuedForDeletion() || !volume.IsInsideTree())
                throw new NotSupportedException("Source contact sampling lost its living query volume.");
            var bodies = volume.GetOverlappingBodies();
            var areas = volume.GetOverlappingAreas();
            using var bodyOwner = (Godot.Collections.Array)bodies;
            using var areaOwner = (Godot.Collections.Array)areas;
            foreach (var collider in bodies.Cast<Node>().Concat(areas))
                if (Contact(collider) is { } reference && observed.Add(reference)) result.Add(reference);
        }
        // Preserve actual discovery order through the one shared enter/leave
        // admission owner. No sorted FormIDs or forged player contacts.
        return result;
    }
}
