using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

// Dynamic references share the inventory transaction and native NIF body owners.
// Their GUID is a runtime identity, separate from every owned-file FormID.
internal partial class RuntimeNativeDroppedItems(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutPlayerInventory inventory, float units, Func<FalloutDroppedReferenceSnapshot, bool> resident,
    Func<int> playerLevel, FalloutGlobalState? globals, Action<Node> releaseGrab) : Node3D
{
    private sealed record Physical(FalloutDroppedReferenceSnapshot Item, Node3D Root,
        RuntimeNifRigidBody[] Bodies, Func<FalloutDroppedReferenceSnapshot> Capture);
    private readonly Dictionary<Guid, Physical> _physical = [];
    private readonly HashSet<Guid> _failed = [];
    private readonly Dictionary<Guid, string> _errors = [];
    private bool _retired;

    internal void Drop(FalloutFormKey form, int variant, int count, long revision, FalloutReferencePlacement placement, DropOrigin origin)
    {
        var item = world.PrepareDroppedItem(inventory, form, variant, count, placement);
        var physical = Prepare(item, origin);
        using var publication = world.PrepareDroppedPublication(physical.Item);
        var attached = false;
        using var effects = new FalloutInventoryChangeLease(() =>
        {
            publication.Commit();
            Attach(physical); attached = true;
        }, () =>
        {
            if (attached) Retire(item.Identity);
            publication.Dispose();
        }, publication.Complete);
        try { inventory.DropInstance(records, form, variant, count, revision, effects); }
        finally
        {
            if (!attached && GodotObject.IsInstanceValid(physical.Root)) physical.Root.Free();
        }
    }

    internal (FalloutDroppedReferenceSnapshot Item, Node3D Root)? Aimed(Node collider)
    {
        for (Node? node = collider; node is not null && node != this; node = node.GetParent())
            if (node.HasMeta("opennv_dynamic_reference") &&
                Guid.TryParse(node.GetMeta("opennv_dynamic_reference").AsString(), out var id) &&
                _physical.TryGetValue(id, out var physical)) return (physical.Item, physical.Root);
        return null;
    }

    internal bool TryActivate(Node collider)
    {
        if (Aimed(collider) is not { } picked) return false;
        var physical = _physical[picked.Item.Identity];
        releaseGrab(physical.Root);
        using var presentation = new FalloutInventoryChangeLease(
            () => { physical.Root.Visible = false; physical.Root.ProcessMode = ProcessModeEnum.Disabled; },
            () => { physical.Root.Visible = true; physical.Root.ProcessMode = ProcessModeEnum.Inherit; },
            () => Retire(picked.Item.Identity));
        world.TakeDroppedItem(picked.Item.Identity, inventory, playerLevel(), globals, presentation);
        return true;
    }

    internal Action<bool>? BindGrab(Node collider)
    {
        if (Aimed(collider) is not { } picked) return null;
        world.RequireDroppedItemEventOwner(picked.Item.Base, "OnGrab");
        world.RequireDroppedItemEventOwner(picked.Item.Base, "OnRelease");
        return _ => { };
    }

    public override void _Process(double delta)
    {
        if (_retired) return;
        var items = world.DroppedItems.ToDictionary(item => item.Identity);
        foreach (var (id, physical) in _physical.ToArray())
        {
            var position = physical.Bodies[0].GlobalPosition;
            var sourcePosition = new Vector3(position.X, -position.Z, position.Y) / units;
            var current = physical.Item with { Placement = physical.Item.Placement with { Position = [sourcePosition.X, sourcePosition.Y, sourcePosition.Z] } };
            if (!items.ContainsKey(id) || !resident(current)) Retire(id);
        }
        foreach (var item in world.DroppedItems)
        {
            if (_physical.ContainsKey(item.Identity) || _failed.Contains(item.Identity) || !resident(item)) continue;
            try { Attach(Prepare(item)); }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                _failed.Add(item.Identity);
                _errors[item.Identity] = error.Message;
                GD.PushError($"OPENNV_NATIVE_DROPPED_REFERENCE_DIVERGENCE identity={item.Identity} base={item.Base}: {error.Message}");
            }
        }
    }

    private Physical Prepare(FalloutDroppedReferenceSnapshot item, DropOrigin? origin = null)
    {
        var source = records.GetEffective(item.Base);
        var modelField = source.Signature switch
        {
            "ARMO" => "MOD2",
            "WEAP" when item.Item.WeaponMods != 0 => "MWD" + item.Item.WeaponMods,
            _ => "MODL"
        };
        var model = source.ReadSubrecords().SingleOrDefault(field => field.Signature == modelField).Data;
        if (model.IsEmpty && source.Signature == "WEAP" && modelField != "MODL")
            model = source.ReadSubrecords().SingleOrDefault(field => field.Signature == "MODL").Data;
        if (model.IsEmpty) throw new NotSupportedException("Dropped item has no winning world model.");
        var path = FalloutCellSceneReader.NormalizeModelPath(FalloutDialogueTopic.Text(model.Span), source.Signature);
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Dropped item source is absent.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Dropped item world model is absent.", path);
        var root = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(bytes), units, contentSource: content).Root;
        try
        {
            root.Name = "Dropped_" + item.Identity.ToString("N");
            root.SetMeta("opennv_dynamic_reference", item.Identity.ToString());
            root.SetMeta("opennv_dynamic_base", item.Base.ToString());
            root.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(item.Placement.RotationRadians[0],
                item.Placement.RotationRadians[1], item.Placement.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(item.Placement.Position[0], item.Placement.Position[1], item.Placement.Position[2])) * units);
            var bodies = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(root).Where(body => !body.Freeze).ToArray();
            if (bodies.Length == 0) throw new NotSupportedException("Dropped model has no authored dynamic collision body.");
            foreach (var body in bodies) body.ContinuousCd = true;
            if (bodies.GroupBy(body => (body.SourceSha256, body.SourceBody)).Any(group => group.Count() != 1))
                throw new NotSupportedException("Dropped model has ambiguous dynamic source bodies.");
            if (origin is { } start)
            {
                PlaceClearOfPlayer(root, start);
                var point = root.Position / units;
                item = item with { Placement = item.Placement with { Position = [point.X, -point.Z, point.Y] } };
            }
            if (item.Physics is { } saved)
            {
                if (saved.Count != bodies.Length) throw new InvalidDataException("Dropped item saved body count differs from its model.");
                foreach (var state in saved)
                {
                    var body = bodies.SingleOrDefault(value => value.SourceBody == state.Body && value.SourceSha256.Equals(state.Sha256, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("Dropped item saved body identity differs from its model.");
                    body.RestorePhysics(state);
                }
            }
            FalloutDroppedReferenceSnapshot Capture()
            {
                var physics = bodies.Select(body => body.CapturePhysics()).ToArray();
                var first = physics[0].Transform;
                var position = new Vector3(first[9], -first[11], first[10]) / units;
                return item with { Placement = item.Placement with { Position = [position.X, position.Y, position.Z] }, Physics = physics };
            }
            return new(item, root, bodies, Capture);
        }
        catch { root.Free(); throw; }
    }

    private void Attach(Physical physical)
    {
        var added = false;
        try
        {
            AddChild(physical.Root);
            _ = physical.Capture();
            _physical.Add(physical.Item.Identity, physical); added = true;
            world.BindDroppedCapture(physical.Item.Identity, physical.Capture);
        }
        catch
        {
            if (added) _physical.Remove(physical.Item.Identity);
            physical.Root.Free();
            throw;
        }
    }

    private void Retire(Guid id)
    {
        if (!_physical.TryGetValue(id, out var physical)) return;
        releaseGrab(physical.Root);
        world.RetireDroppedCapture(id, physical.Capture);
        _physical.Remove(id);
        physical.Root.Free();
    }

    internal void RetireAll()
    {
        if (_retired) return;
        foreach (var id in _physical.Keys.ToArray()) Retire(id);
        _retired = true;
    }

    public override void _ExitTree() => RetireAll();
}
