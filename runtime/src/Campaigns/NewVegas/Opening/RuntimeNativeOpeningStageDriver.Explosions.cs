using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private void ConfigureScriptExplosions()
    {
        var world = _scripts.References!;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Script explosions have no owned content source.");
        var owner = new RuntimeNativeScriptExplosions(_pluginStack, world, content, _player,
            () => _activeCell, () => PlayerLevel, _globals ?? throw new InvalidOperationException("Script explosions have no globals."), DamagePlayer);
        AddChild(owner);
    }
}

// This is the presentation of a real transient EXPL reference. A caller that
// has no mesh (for example a source marker) retains its authoritative placement.
internal sealed partial class RuntimeNativeScriptExplosions : Node3D
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutReferenceWorld _world;
    private readonly RuntimeNativePlayer _player;
    private readonly Func<FalloutFormKey> _cell;
    private readonly Func<int> _level;
    private readonly FalloutGlobalState _globals;
    private readonly Action<FalloutWeaponDamage, byte> _damagePlayer;
    private readonly RuntimeNativeShotEffects _effects;
    private readonly Func<FalloutTransientExplosionRequest, FalloutPreparedTransientExplosion> _prepare;

    internal RuntimeNativeScriptExplosions(FalloutPluginStack records, FalloutReferenceWorld world,
        RuntimeLiveContentSource content, RuntimeNativePlayer player, Func<FalloutFormKey> cell,
        Func<int> level, FalloutGlobalState globals, Action<FalloutWeaponDamage, byte> damagePlayer)
    {
        Name = "OwnedScriptExplosions";
        _records = records; _world = world; _player = player; _cell = cell; _level = level;
        _globals = globals; _damagePlayer = damagePlayer;
        _effects = new(records, content, player.UnitsToMeters, null, player.CollisionMask | player.CollisionLayer);
        AddChild(_effects);
        _prepare = Prepare;
    }

    public override void _Ready() => _world.TransientExplosions.Prepare = _prepare;

    public override void _ExitTree()
    {
        if (_world.TransientExplosions.Prepare == _prepare) _world.TransientExplosions.RetirePresentation();
    }

    private FalloutPreparedTransientExplosion Prepare(FalloutTransientExplosionRequest request)
    {
        if (!IsInsideTree() || !IsInstanceValid(_player) || !_player.IsInsideTree())
            throw new InvalidOperationException("Script explosion presentation is not resident.");
        if (_records.RuntimeFormId(request.Caller) != 0x14 && !_world.IsResident(request.Caller))
            throw new NotSupportedException("Placed explosion caller is not resident.");
        var units = _player.UnitsToMeters;
        var position = _player.GlobalPosition / units;
        var rotation = GamebryoCoordinate.ReferenceEuler(_player.GlobalBasis);
        var player = new FalloutReferencePlacement(_cell(), [position.X, -position.Z, position.Y], [rotation.X, rotation.Y, rotation.Z]);
        var placement = _world.SpawnPlacement(request.Caller, player, units);
        placement.Validate();
        var callerCell = FalloutCellSceneReader.ReadDefinition(_records, placement.Cell);
        var activeCell = FalloutCellSceneReader.ReadDefinition(_records, player.Cell);
        if (callerCell.Worldspace != activeCell.Worldspace || callerCell.Worldspace is null && callerCell.FormKey != activeCell.FormKey)
            throw new NotSupportedException("Placed explosion is outside the resident physics world.");
        var mask = _player.CollisionMask | _player.CollisionLayer;
        if (mask == 0 || !float.IsFinite(units) || units <= 0)
            throw new InvalidDataException("Placed explosion has invalid world physics inputs.");
        var point = GamebryoCoordinate.ConvertVector(new Vector3(placement.Position[0], placement.Position[1], placement.Position[2])) * units;
        if (_player.CanOccupyPosition?.Invoke(point) != true)
            throw new NotSupportedException("Placed explosion has no resident collision scene at its source position.");
        var basis = GamebryoCoordinate.ConvertReferenceEuler(new Vector3(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1);
        var prepared = _effects.PrepareExplosion(request.Explosion, new(basis, point));
        return new(placement, (id, completed, failed) =>
        {
            var root = prepared.Start(completed, failed);
            root.SetMeta("opennv_transient_reference", id);
            _ = RuntimeNativeExplosionCombat.Detonate(root, root, _records, request.Explosion,
                new(0, 1, 0, 1), point, mask, units, request.Caller, _level(), _globals, _player, _damagePlayer);
            GD.Print($"OPENNV_SCRIPT_EXPLOSION reference={id:x8} source={request.Explosion.Form} caller={request.Caller}");
        }, prepared.Dispose);
    }
}
