using Godot;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

// Real Godot object lifetime supplies the native side of the C# CELL protocol.
// A completed model callback, resource registration or detached root cannot
// certify native tree publication. No complete-scene/parity claim lives here.
internal sealed class RuntimeNativeCellProcessAttachment
{
    private readonly FalloutReferenceWorld _world;
    private readonly Node3D _root;
    private readonly ulong _rootIdentity;
    private RuntimeNativeReferenceEvents? _sourceEvents;
    internal Guid Identity { get; }
    internal RuntimeNativeCellProcessAttachment(FalloutReferenceWorld world, Node3D root,
        FalloutCellScene scene, IReadOnlyList<FalloutFormKey> cells, Guid? coldAttachment = null)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(root);
        if (!GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion())
            throw new InvalidDataException("CELL attachment root is already retired.");
        _world = world; _root = root; _rootIdentity = root.GetInstanceId();
        if (coldAttachment is { } previous)
        {
            world.CellProcesses.BeginColdAttachment(previous, scene, cells, _rootIdentity); Identity = previous;
        }
        else Identity = world.CellProcesses.BeginAttachment(scene, cells, _rootIdentity);
    }
    internal void ReferenceReturned(FalloutPlacedReference reference, FalloutBaseObjectDefinition basis,
        IReadOnlyList<Node> actualNewRootChildren)
    {
        RequireRoot(); var child = Child(reference.FormKey);
        if (child.Source.Base != basis.FormKey || basis.FormKey != reference.Base)
            throw new InvalidDataException("CELL child/native base does not belong to its original source work.");
        if (JsonSerializer.Serialize(_world.CellProcesses.ReadChildBase(child.Source)) != JsonSerializer.Serialize(basis))
            throw new InvalidDataException("CELL child changed its actual source model/light declaration after attachment entry.");
        var nodes = actualNewRootChildren.Select(node =>
        {
            if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || node.GetParent() != _root)
                throw new InvalidDataException("CELL child has no living direct native root ownership.");
            RequireNativeReference(node, reference.FormKey);
            return node.GetInstanceId();
        }).ToArray();
        if (nodes.Length > 0)
        {
            _world.CellProcesses.CompleteChild(Identity, child.Source, FalloutCellProcessChildPhase.Published, nodes,
                "actual-source-reference-native-factory-returned");
            return;
        }
        if (!_world.IsEnabled(reference.FormKey))
        {
            _world.CellProcesses.CompleteChild(Identity, child.Source, FalloutCellProcessChildPhase.SourceDisabled, [],
                "actual-authoritative-reference-enable-owner-suppressed-materialization");
            return;
        }
        if (basis.Signature is "NPC_" or "CREA" && _world.Get(reference.FormKey).Templates?.Absent == true)
        {
            _world.CellProcesses.CompleteChild(Identity, child.Source, FalloutCellProcessChildPhase.SourceNoDraw, [],
                "actual-source-leveled-template-selection-absent");
            return;
        }
        // The actual reference-event factory owns a scripted XPRM's contact
        // object later. Keep this child pending until that real owner returns.
        if (basis.Signature == "XPRM" && _world.Get(reference.FormKey).Script is not null) return;
        if (basis.ModelPath is null && basis.Signature is not ("NPC_" or "CREA" or "XPRM") && basis.Light is null)
        {
            _world.CellProcesses.CompleteChild(Identity, child.Source, FalloutCellProcessChildPhase.SourceNoDraw, [],
                "actual-source-no-model-reference-consumer-returned");
            return;
        }
        var failure = new NotSupportedException("Actual enabled source child returned without a native owner or an admitted no-draw consumer.");
        _world.CellProcesses.FailChild(Identity, child.Source, failure, "actual-native-source-reference-factory"); throw failure;
    }
    internal void ReferenceFailed(FalloutFormKey reference, Exception error, IReadOnlyList<Node> stillOwnedChildren)
    {
        var child = Child(reference);
        var retained = stillOwnedChildren.Where(GodotObject.IsInstanceValid).Select(node => node.GetInstanceId()).ToArray();
        _world.CellProcesses.FailChild(Identity, child.Source, error, "actual-native-source-reference-factory-failed", retained);
    }
    internal void Publish(RuntimeNativeReferenceEvents sourceEvents)
    {
        ArgumentNullException.ThrowIfNull(sourceEvents);
        RequireRoot();
        if (!_root.IsInsideTree()) throw new InvalidOperationException("CELL root is detached; actual native publication has not occurred.");
        foreach (var child in _world.CellProcesses.ReadAttachment(Identity).Children)
        {
            try
            {
                var nodes = sourceEvents.SourceCellNativeConsumers(child.Source.Reference);
                if (nodes.Any(node => !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() ||
                    !node.IsInsideTree() || node.GetParent() != _root))
                    throw new InvalidDataException("CELL reference-event consumer belongs to a foreign/unpublished native root.");
                _world.CellProcesses.JoinNativeChildConsumers(Identity, child.Source, nodes.Select(node => node.GetInstanceId()).ToArray(),
                    "actual-native-source-reference-event-consumers-returned");
            }
            catch (Exception error)
            {
                _world.CellProcesses.FailChild(Identity, child.Source, error, "actual-native-source-reference-event-consumer-failed"); throw;
            }
        }
        RequireLiveChildNodes();
        _world.CellProcesses.PublishRoot(Identity, _rootIdentity, "actual-source-CELL-root-and-children-published-in-native-tree");
        _sourceEvents = sourceEvents;
    }
    internal void ObserveForCapture()
    {
        RequireRoot(); var attachment = _world.CellProcesses.ReadAttachment(Identity);
        if (!attachment.RootPublished || attachment.Retired || attachment.NativeRoot != _rootIdentity || !_root.IsInsideTree())
            throw new NotSupportedException("CELL capture still requires genuine current native publication.");
        RequireLiveChildNodes();
        foreach (var child in attachment.Children)
        {
            var events = _sourceEvents ?? throw new NotSupportedException("Current CELL capture lost its actual reference-event consumer.");
            var current = events.SourceCellNativeConsumers(child.Source.Reference);
            if (current.Any(node => !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree() ||
                node.GetParent() != _root || !child.NativeObjects.Contains(node.GetInstanceId())))
            {
                var failure = new NotSupportedException("Actual reference rematerialization changed its current native CELL child ownership without a joined receipt.");
                _world.CellProcesses.FailChild(Identity, child.Source, failure, "actual-current-native-reference-consumer-unmatched",
                    child.NativeObjects.Concat(current.Where(GodotObject.IsInstanceValid).Select(node => node.GetInstanceId())).Distinct().ToArray());
                throw failure;
            }
            if (child.Phase == FalloutCellProcessChildPhase.SourceDisabled && _world.IsEnabled(child.Source.Reference))
            {
                var failure = new NotSupportedException("Actual reference-enable rematerialization has no joined CELL child receipt.");
                _world.CellProcesses.FailChild(Identity, child.Source, failure, "actual-current-reference-enable-lifecycle-unmatched");
                throw failure;
            }
        }
    }
    private void RequireLiveChildNodes()
    {
        foreach (var child in _world.CellProcesses.ReadAttachment(Identity).Children)
            foreach (var identity in child.NativeObjects)
                if (GodotObject.InstanceFromId(identity) is not Node node || !GodotObject.IsInstanceValid(node) ||
                    node.IsQueuedForDeletion() || !node.IsInsideTree() || node.GetParent() != _root)
                {
                    var failure = new InvalidDataException("CELL publication lost an actual living source child/native lease.");
                    _world.CellProcesses.FailChild(Identity, child.Source, failure, "actual-native-source-child-lifetime-lost",
                        child.NativeObjects.Where(id => GodotObject.IsInstanceValid(GodotObject.InstanceFromId(id))).ToArray());
                    throw failure;
                }
    }
    internal bool BeginDetach()
    {
        RequireRoot();
        return _world.CellProcesses.BeginDetach(Identity, _rootIdentity, "actual-native-CELL-retirement-entered");
    }
    internal void ObserveRetiredChild(FalloutFormKey reference)
    {
        var child = Child(reference);
        if (child.NativeObjects.Any(identity => GodotObject.IsInstanceValid(GodotObject.InstanceFromId(identity))))
            throw new InvalidOperationException("CELL child retirement still owns a native object; QueueFree/_ExitTree is not destruction.");
        _world.CellProcesses.RetireChild(Identity, child.Source, "actual-native-CELL-child-objects-destroyed");
    }
    internal void ObserveRetiredRoot()
    {
        if (GodotObject.IsInstanceValid(GodotObject.InstanceFromId(_rootIdentity)))
            throw new InvalidOperationException("CELL root retirement still owns its native object.");
        _world.CellProcesses.CompleteDetach(Identity, _rootIdentity, "actual-native-CELL-root-and-source-child-consumers-retired");
    }
    private FalloutCellProcessChild Child(FalloutFormKey reference) => _world.CellProcesses.ReadAttachment(Identity).Children
        .SingleOrDefault(child => child.Source.Reference == reference) ?? throw new InvalidDataException("Native CELL reference belongs to another original attachment.");
    internal static void RequireNativeReference(Node node, FalloutFormKey reference)
    {
        if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion())
            throw new InvalidDataException("Native source reference consumer is already retired.");
        var actual = node switch
        {
            RuntimeNativeNpc npc => npc.Appearance.Reference,
            RuntimeNativeCreature creature => creature.Appearance.Reference,
            _ => (FalloutFormKey?)null,
        };
        if (actual is { } actor)
        {
            if (actor != reference) throw new InvalidDataException("Native actor consumer belongs to another original reference.");
        }
        else if (!node.HasMeta("opennv_reference_form_key") ||
            !string.Equals(node.GetMeta("opennv_reference_form_key").AsString(), reference.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native model consumer has no matching original reference factory identity.");
    }
    private void RequireRoot()
    {
        if (!GodotObject.IsInstanceValid(_root) || _root.GetInstanceId() != _rootIdentity || _root.IsQueuedForDeletion())
            throw new InvalidOperationException("Actual CELL native root lease is retired.");
    }
}
