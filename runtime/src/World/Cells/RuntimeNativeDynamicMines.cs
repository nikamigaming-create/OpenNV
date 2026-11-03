using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

// Persistent PROJ instances use the owned NIF body; they are not dropped WEAP
// models or swept-ray flights. The world retains identities and state off-cell.
internal sealed partial class RuntimeNativeDynamicMines(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutPlayerInventory inventory, RuntimeLiveContentSource content, float units, uint mask,
    NativeActorCombatContext context, Func<FalloutReferencePlacement, bool> resident,
    Func<FalloutFormKey> activeCell, Func<float> explosives, Func<float> triggerChance,
    Action<string> failure) : Node3D
{
    private sealed record Physical(FalloutDynamicMineReference State, Node3D Root, RuntimeNifRigidBody Body,
        Func<FalloutDynamicMineSnapshot> Capture, PhysicsBody3D? CreatorBody);
    private readonly Dictionary<uint, Physical> _physical = [];
    private readonly Dictionary<uint, string> _errors = [];
    private bool _retired;

    internal void Prepare(FalloutWeaponShot shot)
    {
        _ = RequireSource(shot);
        var root = Build(shot.Projectile);
        root.Free();
    }

    private FalloutMineSource RequireSource(FalloutWeaponShot shot)
    {
        if (shot.WeaponAnimationType is not (11 or 12) || shot.Ammunition is not null || shot.Projectiles != 1 || shot.AmmoEffects.Count != 0 ||
            FalloutMineSource.Read(records, shot.Projectile.Form) is not { } source)
            throw new NotSupportedException("Mine deployment requires one consuming source proximity projectile.");
        RuntimeNativePlacedMine.RequireSource(shot.Projectile);
        if (records.NumericSettings.Float("fMineAgeMax") < 0) throw new InvalidDataException("Mine maximum age is invalid.");
        FalloutReferenceWorld.RequirePlayerItemAcquisition(records, records.GetEffective(shot.Weapon), records.RuntimeFormKey(0x14));
        if (FalloutScriptLocals.AttachedScript(records, records.GetEffective(shot.Weapon)) is not null)
            throw new NotSupportedException("Mine source item script requires its release and retained-instance event owner.");
        return source with { Projectile = shot.Projectile };
    }

    internal uint Deploy(FalloutWeaponShot shot, FalloutWeaponDamage damage, Transform3D release, float speed)
    {
        if (_retired || !IsInsideTree() || !release.IsFinite() || !float.IsFinite(speed) || speed <= 0)
            throw new InvalidOperationException("Mine has no live source release owner.");
        var source = RequireSource(shot);
        var item = inventory.SelectedWeapon(shot.Weapon).Extra;
        if (item.ScriptState is not null || item.Magazine is not null)
            throw new NotSupportedException("Mine instance has unowned item-script or magazine continuation.");
        var revision = inventory.Revision;
        var root = Build(shot.Projectile);
        var body = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(root).Single(value => !value.Freeze);
        var (identity, allocation) = world.RuntimeReferences.Prepare(FalloutRuntimeReferenceKind.Projectile, shot.Projectile.Form);
        using var allocationScope = allocation;
        var point = release.Origin / units;
        var seed = new FalloutDynamicMineSnapshot(identity.RuntimeId!.Value, shot.Projectile.Form, shot.Weapon,
            records.RuntimeFormKey(0x14), item with { Count = 1, Hotkey = null },
            new(activeCell(), [point.X, -point.Z, point.Y], [0, 0, 0]), FalloutMineState.Initial(source),
            damage, 0, BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))), new(identity, 0, []), []);
        var state = new FalloutDynamicMineReference(world, records, seed);
        var published = false;
        var attached = false;
        using var effects = new FalloutInventoryChangeLease(() =>
        {
            allocation.Commit();
            world.PublishDynamicMine(state); published = true;
            root.Transform = release;
            var physical = Attach(state, root, body, context.Player()); attached = true;
            body.LinearVelocity = -release.Basis.Z.Normalized() * speed * units;
            body.Sleeping = false;
            _ = physical.State.Capture();
        }, () =>
        {
            if (attached) Retire(state.Saved.RuntimeId, capture: false);
            if (published) world.RollbackDynamicMine(state.Saved.RuntimeId);
            allocation.Dispose();
        }, allocation.Complete);
        try
        {
            inventory.ConsumeDeployedWeapon(shot.Weapon, item, revision, effects);
            return state.Saved.RuntimeId;
        }
        finally { if (!attached && GodotObject.IsInstanceValid(root)) root.Free(); }
    }

    private Node3D Build(FalloutProjectile projectile)
    {
        var path = projectile.Model ?? throw new NotSupportedException("Mine has no source world model.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Mine source model is absent.", path);
        var root = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(bytes), units, contentSource: content).Root;
        try
        {
            var bodies = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(root).Where(body => !body.Freeze).ToArray();
            if (bodies.Length != 1) throw new NotSupportedException("Mine requires one real authored dynamic projectile body.");
            bodies[0].ContinuousCd = true;
            return root;
        }
        catch { root.Free(); throw; }
    }

    private Physical Attach(FalloutDynamicMineReference state, Node3D root, RuntimeNifRigidBody body, PhysicsBody3D? creator)
    {
        var id = state.Saved.RuntimeId;
        root.Name = "Projectile_" + id.ToString("x8");
        root.SetMeta("opennv_runtime_reference", id);
        var source = state.Saved.Source(records);
        RuntimeNativePlacedMine.RequireSource(source.Projectile);
        var cell = FalloutCellSceneReader.ReadDefinition(records, state.Saved.Placement.Cell);
        root.AddChild(new RuntimeNativePlacedMine(root, world, state, source, records, content, units, mask,
            context, cell.Worldspace is not null, explosives, triggerChance, failure));
        FalloutDynamicMineSnapshot Capture()
        {
            var physics = body.CapturePhysics();
            var point = body.GlobalPosition / units;
            return state.Saved with { Physics = [physics], Placement = state.Saved.Placement with { Position = [point.X, -point.Z, point.Y] } };
        }
        var physical = new Physical(state, root, body, Capture, creator);
        try
        {
            state.Resident = true;
            AddChild(root);
            state.CapturePhysical = Capture;
            _physical.Add(id, physical);
            if (creator is not null && state.AgeSeconds <= 1) body.AddCollisionExceptionWith(creator);
            return physical;
        }
        catch
        {
            state.Resident = false; state.CapturePhysical = null;
            _physical.Remove(id);
            if (GodotObject.IsInstanceValid(root)) root.Free();
            throw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_retired) return;
        foreach (var physical in _physical.Values.ToArray())
        {
            try
            {
                var state = physical.State;
                if (state.Mine.Error is not null) continue;
                if (state.Taken) { Retire(state.Saved.RuntimeId); continue; }
                state.AgeSeconds += delta;
                if (state.AgeSeconds > 1 && physical.CreatorBody is { } creator && GodotObject.IsInstanceValid(creator))
                    physical.Body.RemoveCollisionExceptionWith(creator);
                var maxAge = records.NumericSettings.Float("fMineAgeMax");
                if (maxAge > 0 && state.AgeSeconds > maxAge)
                {
                    state.Taken = true; Retire(state.Saved.RuntimeId); continue;
                }
                if (!resident(physical.Capture().Placement)) Retire(state.Saved.RuntimeId);
            }
            catch (Exception error)
            {
                physical.State.Mine = physical.State.Mine with { Error = error.Message };
                _errors[physical.State.Saved.RuntimeId] = error.Message;
                failure(error.Message);
                GD.PushError("OPENNV_DYNAMIC_MINE_UNBOUND " + error.Message);
            }
        }
        foreach (var state in world.DynamicMines)
        {
            if (state.Taken || _physical.ContainsKey(state.Saved.RuntimeId) || _errors.ContainsKey(state.Saved.RuntimeId) || !resident(state.Saved.Placement)) continue;
            Node3D? root = null;
            try
            {
                root = Build(state.Saved.Source(records).Projectile);
                root.Position = GamebryoCoordinate.ConvertVector(new(state.Saved.Placement.Position[0], state.Saved.Placement.Position[1], state.Saved.Placement.Position[2])) * units;
                var body = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(root).Single(value => !value.Freeze);
                body.RestorePhysics(state.Saved.Physics.Single());
                _ = Attach(state, root, body, context.Player());
            }
            catch (Exception error)
            {
                if (root is not null && GodotObject.IsInstanceValid(root)) root.Free();
                _errors[state.Saved.RuntimeId] = error.Message;
                state.Mine = state.Mine with { Error = error.Message };
                failure(error.Message);
                GD.PushError("OPENNV_DYNAMIC_MINE_UNBOUND " + error.Message);
            }
        }
    }

    private void Retire(uint id, bool capture = true)
    {
        if (!_physical.TryGetValue(id, out var physical)) return;
        if (capture) _ = physical.State.Capture();
        _physical.Remove(id);
        physical.State.CapturePhysical = null; physical.State.Resident = false;
        physical.Root.Free();
    }

    internal object State => new
    {
        items = world.DynamicMines.Select(mine => new
        {
            identity = mine.Identity.ToString(),
            projectile = mine.Saved.Projectile.ToString(),
            weapon = mine.Saved.Weapon.ToString(),
            creator = mine.Saved.Creator.ToString(),
            mine.Mine,
            mine.AgeSeconds,
            mine.Taken,
            resident = _physical.ContainsKey(mine.Saved.RuntimeId),
            cell = mine.Saved.Placement.Cell.ToString(),
            error = _errors.GetValueOrDefault(mine.Saved.RuntimeId),
            position = _physical.TryGetValue(mine.Saved.RuntimeId, out var physical)
                ? new[] { physical.Body.GlobalPosition.X, physical.Body.GlobalPosition.Y, physical.Body.GlobalPosition.Z } : null
        }).ToArray()
    };

    internal void RetireAll()
    {
        if (_retired) return;
        foreach (var id in _physical.Keys.ToArray()) Retire(id);
        _retired = true;
    }
    public override void _ExitTree() => RetireAll();
}
