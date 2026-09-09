using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

// Godot supplies contacts and aimed input. This adapter invokes the same C#
// event owner as the lab; no quest IDs or stage transitions belong here.
internal partial class RuntimeNativeReferenceEvents : Node
{
    private sealed record Binding(FalloutPlacedReference Reference, FalloutReferenceInstance Instance, string Signature,
        Node3D? Presentation, Area3D? Trigger, FalloutTriggerContacts Contacts)
    {
        internal bool Loaded;
        internal bool ReportedError;
        internal FalloutFormKey? PendingActivation;
        internal Node3D? Node = Presentation;
        internal bool ActorScriptStarted;
    }

    private FalloutPluginStack _records = null!;
    private FalloutQuestState _quests = null!;
    private FalloutReferenceWorld _world = null!;
    private RuntimeNativeReferencePresentation? _presentation;
    internal RuntimeNativePlayer? Player { get; set; }
    internal Action<FalloutPlacedReference, Node3D?, string>? Interact { get; set; }
    internal Action<string> ReportDivergence { get; set; } = message => GD.PushError(message);
    private FalloutReferenceScripts _scripts = null!;
    private FalloutReferenceScriptHost _host = null!;
    private readonly Dictionary<FalloutFormKey, Binding> _bindings = [];
    private readonly Dictionary<ulong, FalloutFormKey> _nodeReferences = [];
    private long _frames, _executedBlocks;
    private Func<FalloutPlacedReference, Transform3D> _transform = null!;
    private float _unitsToMeters;
    private uint _collisionMask;
    internal IReadOnlyList<FalloutPlacedReference> BoundTriggers => _bindings.Values
        .Where(binding => binding.Trigger is { } trigger && GodotObject.IsInstanceValid(trigger) && trigger.IsInsideTree())
        .Select(binding => binding.Reference).ToArray();
    internal object State => new
    {
        frames = _frames,
        executedBlocks = _executedBlocks,
        references = _bindings.Count,
        triggers = _bindings.Values.Count(value => value.Trigger is not null),
        errors = _bindings.Values.Where(value => value.Instance.ScriptError is not null)
            .Select(value => new { reference = value.Reference.FormKey.ToString(), error = value.Instance.ScriptError }).ToArray(),
        boundary = "source-events-from-native-contacts; full-collision-filter-order-and-retail-timing-unverified",
    };

    internal void Configure(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutCellScene cell, Node3D root, FalloutReferenceScriptHost host,
        Func<FalloutPlacedReference, Transform3D> transform, float unitsToMeters, uint collisionMask)
    {
        if (_scripts is not null) throw new InvalidOperationException("Reference events were already configured.");
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0 || collisionMask == 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMeters));
        Name = "NativeReferenceEvents";
        _records = records;
        _world = world;
        _quests = quests;
        _scripts = new(records, world, quests, host);
        _host = host;
        _transform = transform; _unitsToMeters = unitsToMeters; _collisionMask = collisionMask;
        SetResidency(cell, root);
    }

    internal void SetResidency(FalloutCellScene cell, Node3D root)
    {
        var records = _records; var world = _world;
        var retained = cell.References.Select(reference => reference.FormKey).ToHashSet();
        foreach (var key in _bindings.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            var old = _bindings[key];
            if (old.Trigger is { } trigger) { GamebryoReferenceEnableRuntime.Apply(trigger, false); trigger.QueueFree(); }
            _bindings.Remove(key);
        }
        _nodeReferences.Clear();
        var identities = cell.References.ToDictionary(reference => reference.FormKey.ToString(), reference => reference.FormKey);
        var nodes = new Dictionary<FalloutFormKey, Node3D>();
        foreach (var node in root.GetChildren().OfType<Node3D>())
        {
            if (node.IsQueuedForDeletion()) continue;
            var key = node is RuntimeNativeNpc actor ? actor.Appearance.Reference :
                node.HasMeta("opennv_reference_form_key") && identities.TryGetValue(node.GetMeta("opennv_reference_form_key").AsString(), out var identity)
                    ? identity : (FalloutFormKey?)null;
            if (key is not { } found) continue;
            nodes.Add(found, node);
            _nodeReferences.Add(node.GetInstanceId(), found);
        }
        foreach (var reference in cell.References)
        {
            if (_bindings.TryGetValue(reference.FormKey, out var existing))
            {
                existing.Node = nodes.GetValueOrDefault(reference.FormKey);
                continue; // Keep OnLoad, contact membership and source clocks.
            }
            var instance = world.Get(reference.FormKey);
            Area3D? trigger = null;
            try
            {
                if (instance.Script is not null && FalloutReferencePrimitive.Read(records.GetEffective(reference.FormKey)) is { } primitive)
                {
                    trigger = CreateTrigger(primitive, _unitsToMeters, _collisionMask);
                    trigger.Name = $"NativeTrigger_{reference.FormKey}";
                    trigger.Transform = _transform(reference);
                    root.AddChild(trigger);
                    GamebryoReferenceEnableRuntime.Apply(trigger, world.IsEnabled(reference.FormKey));
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            {
                instance.ScriptError ??= "Contact binding: " + error.Message;
            }
            _bindings.Add(reference.FormKey, new(reference, instance, cell.BaseObjects[reference.Base].Signature,
                nodes.GetValueOrDefault(reference.FormKey), trigger, new()));
        }
        if (_presentation is not null) _presentation.Materialized -= BindMaterialized;
        _presentation = root.GetChildren().OfType<RuntimeNativeReferencePresentation>().SingleOrDefault();
        if (_presentation is not null) _presentation.Materialized += BindMaterialized;
    }

    private void BindMaterialized(FalloutFormKey reference, Node3D node)
    {
        _bindings[reference].Node = node;
        _nodeReferences[node.GetInstanceId()] = reference;
    }

    public override void _ExitTree()
    {
        if (_presentation is not null) _presentation.Materialized -= BindMaterialized;
    }

    internal static Area3D CreateTrigger(FalloutReferencePrimitive primitive, float unitsToMeters, uint collisionMask)
    {
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0 || collisionMask == 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMeters));
        var size = new Vector3(primitive.X, primitive.Z, primitive.Y) * (2 * unitsToMeters);
        if (!size.IsFinite() || size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new InvalidDataException("Primitive dimensions cannot be represented by the physics owner.");
        if (primitive.CollisionLayer is not (null or 12))
            throw new NotSupportedException($"Script primitive collision layer {primitive.CollisionLayer} has no trigger filter owner.");
        Shape3D shape = primitive.Type switch
        {
            1 => new BoxShape3D { Size = size },
            2 when primitive.X == primitive.Y && primitive.X == primitive.Z => new SphereShape3D { Radius = primitive.X * unitsToMeters },
            _ => throw new NotSupportedException($"Script primitive shape {primitive.Type}/{primitive.X:R}/{primitive.Y:R}/{primitive.Z:R} has no contact owner."),
        };
        var area = new Area3D { CollisionLayer = 0, CollisionMask = collisionMask, Monitoring = true, Monitorable = false };
        area.AddChild(new CollisionShape3D { Shape = shape });
        return area;
    }

    internal bool TryActivate(Node collider)
    {
        for (Node? node = collider; node is not null; node = node.GetParent())
        {
            if (!_nodeReferences.TryGetValue(node.GetInstanceId(), out var reference)) continue;
            if (!_bindings.TryGetValue(reference, out var binding)) return false;
            if (!_world.CanActivate(reference)) return false;
            if (binding.Instance.Script is null && binding.Signature is "STAT" or "SCOL" or "TREE" or "GRAS" or "XPRM" or "LIGH" or "SOUN" or "ASPC" or "IDLM")
                return false;
            if (binding.Instance.ScriptError is { } error && !error.StartsWith("OnActivate:", StringComparison.OrdinalIgnoreCase) ||
                binding.PendingActivation is not null) return false;
            binding.PendingActivation = _records.RuntimeFormKey(0x14);
            GD.Print($"OPENNV_NATIVE_REFERENCE_ACTIVATE reference={reference} queued=true");
            return true;
        }
        return false;
    }

    internal FalloutPlacedReference? AimedReference(Node collider)
    {
        for (Node? node = collider; node is not null; node = node.GetParent())
            if (_nodeReferences.TryGetValue(node.GetInstanceId(), out var reference) && _bindings.TryGetValue(reference, out var binding) &&
                _world.CanActivate(reference) && (binding.Instance.Script is not null || binding.Signature is "NPC_" or "CREA" or "DOOR" or "CONT" or "FURN" or "ACTI" || FalloutReferenceWorld.IsInventoryItem(binding.Signature)))
                return binding.Reference;
        return null;
    }

    internal Node3D? AimedPresentation(Node collider)
    {
        for (Node? node = collider; node is not null; node = node.GetParent())
            if (_nodeReferences.TryGetValue(node.GetInstanceId(), out var reference) && _bindings.TryGetValue(reference, out var binding))
                return binding.Node;
        return null;
    }

    internal void DefaultActivate(FalloutFormKey reference)
    {
        var binding = _bindings[reference];
        if (binding.Instance.Destroyed || binding.Instance.DeletePending) return;
        if (binding.Signature is "DOOR" or "CONT" || FalloutReferenceWorld.IsInventoryItem(binding.Signature) ||
            binding.Signature is "NPC_" or "CREA" && _world.IsDead(reference))
        {
            (Interact ?? throw new InvalidOperationException("Reference interaction owner is absent."))(binding.Reference, binding.Node, binding.Signature);
            return;
        }
        var type = binding.Signature;
        if (type is "NPC_" or "CREA" && FalloutDialogueSpeaker.AllowsPlayerDialogue(_records, binding.Reference.Base))
        {
            _host.Apply(new(FalloutReferenceEffectKind.Conversation, reference, reference, _records.RuntimeFormKey(0x14)));
            return;
        }
        if (type == "FURN")
        {
            if (Player is null || binding.Node is null) throw new NotSupportedException("Furniture activation has no resident player/presentation.");
            if (_bindings.Values.Any(value => value.Node is RuntimeNativeNpc npc && npc.CurrentFurniture == reference))
                return;
            Player.ActivateFurniture(_records, _quests, binding.Reference, binding.Node.GlobalTransform, binding.Instance.Cell);
            return;
        }
        if (type == "ACTI") return; // Activators have no engine default action.
        throw new NotSupportedException($"Default activation for {type} {reference} has no runtime interaction owner.");
    }

    internal void ScriptActivate(FalloutFormKey reference, FalloutFormKey actor, bool runOnActivate)
    {
        if (!_bindings.TryGetValue(reference, out var binding)) throw new NotSupportedException("Script activation target has no resident event owner.");
        if (binding.Instance.DeletePending) return;
        if (!runOnActivate) { DefaultActivate(reference); return; }
        if (binding.PendingActivation is not null) throw new InvalidOperationException("Reference already has an admitted activation.");
        binding.PendingActivation = actor;
    }

    internal bool IsCurrentFurniture(FalloutFormKey actor, FalloutFormKey furniture)
    {
        if (_records.RuntimeFormId(actor) == 0x14) return Player?.CurrentFurniture == furniture;
        if (_bindings.GetValueOrDefault(actor)?.Node is RuntimeNativeNpc npc)
            return npc.CurrentFurniture == furniture;
        throw new NotSupportedException($"Furniture query actor {actor} has no resident runtime owner.");
    }

    private FalloutFormKey? Contact(Node body)
    {
        for (Node? node = body; node is not null; node = node.GetParent())
        {
            if (node is RuntimeNativePlayer) return _records.RuntimeFormKey(0x14);
            if (node is RuntimeNativeNpc actor) return actor.Appearance.Reference;
            if (body is RigidBody3D && _nodeReferences.TryGetValue(node.GetInstanceId(), out var reference)) return reference;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        ++_frames;
        var tree = GetTree();
        foreach (var binding in _bindings.Values)
        {
            if (binding.Instance.Script is null && binding.PendingActivation is null && binding.Trigger is null) continue;
            if (!IsProcessing() || tree.Paused) break; // An effect can unload this cell or open a modal menu.
            var enabled = _world.IsEnabled(binding.Reference.FormKey);
            binding.ActorScriptStarted |= enabled;
            if (binding.Signature is "NPC_" or "CREA" && !binding.ActorScriptStarted) continue;
            if (binding.Trigger is { } volume && volume.Visible != enabled) GamebryoReferenceEnableRuntime.Apply(volume, enabled);
            if (binding.Instance.Script is null && binding.PendingActivation is null) continue;
            // Keep admitting real contact transitions after a script fault. The
            // C# owner decides whether a fresh event can retry its source block;
            // suppressing contact sampling here permanently poisoned saved triggers.
            var events = new List<FalloutReferenceScriptEvent>();
            if (binding.PendingActivation is { } actor)
            {
                events.Add(new("OnActivate", actor));
                binding.PendingActivation = null;
            }
            if (!binding.Loaded && enabled && (binding.Node is not null || binding.Trigger is not null)) { events.Add(new("OnLoad")); binding.Loaded = true; }
            if (enabled && binding.Trigger is { } trigger)
            {
                var bodies = trigger.GetOverlappingBodies();
                var areas = trigger.GetOverlappingAreas();
                using var bodiesOwner = (Godot.Collections.Array)bodies;
                using var areasOwner = (Godot.Collections.Array)areas;
                var contacts = bodies.Cast<Node>().Concat(areas).Select(Contact).Where(key => key is not null)
                    .Select(key => key!.Value).Distinct().ToArray();
                events.AddRange(binding.Contacts.Advance(contacts));
            }
            events.Add(new("GameMode"));
            Report(binding, _scripts.DispatchFrame(binding.Reference.FormKey, events, delta));
        }
    }

    private void Report(Binding binding, IReadOnlyList<FalloutReferenceScriptEventResult> results)
    {
        _executedBlocks += results.Sum(result => result.Blocks);
        var error = binding.Instance.ScriptError ?? results.FirstOrDefault(result => result.Error is not null)?.Error;
        if (error is null || binding.ReportedError) return;
        binding.ReportedError = binding.Instance.ScriptError is not null;
        ReportDivergence($"OPENNV_NATIVE_REFERENCE_EVENT_DIVERGENCE reference={binding.Reference.FormKey}: {error}");
    }
}
