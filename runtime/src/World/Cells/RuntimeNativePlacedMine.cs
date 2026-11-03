using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

// A real PGRE and its owned NIF remain the spatial/physical authority. The
// reference owns the timer across residency and save/load, not this adapter.
internal sealed partial class RuntimeNativePlacedMine : Node, IGamebryoReferenceEnableLifecycle
{
    private const string OwnerMeta = "opennv_mine_owner";
    private readonly Node3D _root;
    private readonly Node3D _anchor;
    private readonly FalloutMineSource _source;
    private readonly FalloutReferenceWorld _world;
    private readonly IFalloutMineReference _state;
    private readonly FalloutPluginStack _records;
    private readonly NativeActorCombatContext _context;
    private readonly NativeOwnedAnimationSoundPlayer _sounds;
    private readonly RuntimeNativeShotEffects _effects;
    private readonly Func<float> _playerExplosives, _playerTriggerChance;
    private readonly Action<string> _failure;
    private readonly bool _exterior;
    private readonly float _units;
    private readonly uint _mask;
    private readonly Node3D? _lightOn, _lightOff;
    internal FalloutMineState State => _state.Mine;
    internal FalloutSpatialReferenceId Identity => _state.Identity;
    internal FalloutMineSource Source => _source;
    internal bool CanInteract => State.Error is null && State.Phase != FalloutMinePhase.Detonated &&
        (_source.Projectile.CanBeDisabled && State.Phase != FalloutMinePhase.Disabled || _source.Projectile.CanBePickedUp);
    internal string ActionSetting => _source.Projectile.CanBeDisabled && State.Phase != FalloutMinePhase.Disabled
        ? "sDisarmMine" : "sTargetTypeTake";

    internal static RuntimeNativePlacedMine? Find(Node node)
    {
        for (Node? parent = node; parent is not null; parent = parent.GetParent())
            if (parent.HasMeta(OwnerMeta))
                return parent.GetMeta(OwnerMeta).AsGodotObject() as RuntimeNativePlacedMine ??
                    throw new InvalidOperationException("Mine lost its resident owner.");
        return null;
    }

    internal static void Attach(Node3D root, FalloutReferenceWorld world, FalloutFormKey reference,
        FalloutPluginStack records, RuntimeLiveContentSource content, float units, uint mask,
        NativeActorCombatContext context, bool exterior, Func<float> playerExplosives,
        Func<float> playerTriggerChance, Action<string> failure)
    {
        if (records.GetEffective(reference).Signature != "PGRE" ||
            FalloutMineSource.Read(records, world.Get(reference).Base) is not { } source) return;
        RequireSource(source.Projectile);
        if (source.Projectile.CanBePickedUp) _ = source.PickupWeapon(records);
        root.AddChild(new RuntimeNativePlacedMine(root, world, reference, source, records, content,
            units, mask, context, exterior, playerExplosives, playerTriggerChance, failure));
    }

    internal static void RequireSource(FalloutProjectile projectile)
    {
        if (!(projectile.IsProximityMine || projectile.IsRemoteMine) || projectile.Type != 2 || projectile.Hitscan || projectile.HasExplicitRotation ||
            projectile.ProjectileLight is not null || projectile.FlightSound is not null)
            throw new NotSupportedException("Proximity projectile needs its declared motion, light or flight-audio owner.");
        projectile.ExplosionSource!.RequireRuntimeDamageOwner();
    }

    private RuntimeNativePlacedMine(Node3D root, FalloutReferenceWorld world, FalloutFormKey reference,
        FalloutMineSource source, FalloutPluginStack records, RuntimeLiveContentSource content, float units,
        uint mask, NativeActorCombatContext context, bool exterior, Func<float> playerExplosives,
        Func<float> playerTriggerChance, Action<string> failure)
        : this(root, world, new FalloutPlacedMineReference(world, world.Get(reference), source), source, records, content,
            units, mask, context, exterior, playerExplosives, playerTriggerChance, failure)
    { }

    internal RuntimeNativePlacedMine(Node3D root, FalloutReferenceWorld world, IFalloutMineReference state,
        FalloutMineSource source, FalloutPluginStack records, RuntimeLiveContentSource content, float units,
        uint mask, NativeActorCombatContext context, bool exterior, Func<float> playerExplosives,
        Func<float> playerTriggerChance, Action<string> failure)
    {
        Name = "SourceProximityMine";
        _root = root; _world = world; _state = state; _source = source; _records = records;
        _units = units; _mask = mask; _context = context; _exterior = exterior;
        _playerExplosives = playerExplosives; _playerTriggerChance = playerTriggerChance; _failure = failure;
        var bodies = root.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().ToArray();
        if (bodies.Length > 1) throw new NotSupportedException("Mine has multiple source dynamic bodies without a unique projectile spatial owner.");
        _anchor = bodies.Length == 1 ? bodies[0] : root;
        _lightOn = Light("##LightOn"); _lightOff = Light("##LightOff");
        _sounds = new(records, content, root, units, _state.SoundRandom, _state.AnimationSoundEvents);
        AddChild(_sounds);
        if (source.Projectile.CountdownSound is { } countdown) _sounds.PrepareSound(countdown);
        if (source.Projectile.DisableSound is { } disable) _sounds.PrepareSound(disable);
        _effects = new(records, content, units, null, mask, _sounds);
        AddChild(_effects);
        Node3D? Light(string name) => root.FindChildren("*", "", true, false).OfType<Node3D>()
            .SingleOrDefault(node => node.GetMeta("opennv_nif_source_name", "").AsString() == name);
    }

    public override void _EnterTree() => _root.SetMeta(OwnerMeta, this);
    public override void _ExitTree() => _root.RemoveMeta(OwnerMeta);
    public override void _Ready() => PublishLight(State.LightOn);

    public void BeforeReferenceDisabled()
    {
        if (_state.Live && !_state.Destroyed) return;
        _sounds.StopEmitterSounds(_anchor);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State.Error is not null || !_state.Live) return;
        try
        {
            _sounds.RequirePcmRestored();
            if (State.Phase == FalloutMinePhase.Armed && !_source.Projectile.IsRemoteMine) DetectActor();
            if (State.Phase != FalloutMinePhase.Countdown) return;
            _state.Mine = State.Advance(delta);
            if (State.RemainingSeconds == 0) { Detonate(); return; }
            var blink = State.BlinkSeconds - delta;
            if (blink <= 0)
            {
                var max = _records.NumericSettings.Float("fMinesBlinkMax");
                var slow = _records.NumericSettings.Float("fMinesBlinkSlow");
                var fast = _records.NumericSettings.Float("fMinesBlinkFast");
                if (max <= 0 || slow < 0 || fast < 0) throw new InvalidDataException("Mine blink settings are invalid.");
                blink = State.RemainingSeconds > max ? slow : fast + (slow - fast) * State.RemainingSeconds / max;
                var light = !(State.LightOn ?? _lightOn?.Visible ?? false);
                _state.Mine = State with { BlinkSeconds = blink, LightOn = light };
                PublishLight(light);
                // Native blink publication restarts a finished countdown
                // voice, but never overlaps an already-playing generation.
                if (_source.Projectile.CountdownSound is { } sound &&
                    !_state.AnimationSoundEvents.ActiveEvents.Any(entry => entry.Sound == sound)) RequireSound(sound);
            }
            else _state.Mine = State with { BlinkSeconds = blink };
        }
        catch (Exception error) { Fail(error.Message); }
    }

    private void DetectActor()
    {
        var radius = _source.Radius(_records, _exterior) * _units;
        foreach (var node in GetTree().GetNodesInGroup("OpenNVNativeCombatActors"))
        {
            if (node is not RuntimeNativeActorCombat { CanReceiveExplosionDamage: true } actor ||
                actor.GetParent() is not Node3D body || body.GlobalPosition.DistanceSquaredTo(_anchor.GlobalPosition) > radius * radius ||
                !_state.ActorEligible(actor.TouchEffectTarget)) continue;
            var target = _world.Get(actor.TouchEffectTarget);
            var creature = _records.GetEffective(target.Base).Signature == "CREA";
            _state.Mine = State.Trigger(_source, _records, target.Reference, creature,
                creature ? 0 : _world.ActorValue(target.Reference, "explosives"));
            return;
        }
        if (State.IgnoresPlayer || _context.Player() is not { } player || _context.Vitals().HitPoints <= 0 ||
            player.GlobalPosition.DistanceSquaredTo(_anchor.GlobalPosition) > radius * radius) return;
        var playerKey = _records.RuntimeFormKey(0x14);
        if (!_state.ActorEligible(playerKey)) return;
        var chance = _playerTriggerChance();
        if (!float.IsFinite(chance)) throw new InvalidDataException("Mine trigger perk chance is not finite.");
        if ((int)(_state.SoundRandom.NextUnitFloat() * 100) >= Math.Truncate(chance))
        {
            _state.Mine = State with { IgnoresPlayer = true };
            return;
        }
        _state.Mine = State.Trigger(_source, _records, playerKey, false, _playerExplosives());
    }

    internal void Activate(FalloutPlayerInventory inventory, Action<double> experience)
    {
        if (!CanInteract || !_state.Live) throw new InvalidOperationException("Mine cannot be activated.");
        if (_source.Projectile.CanBeDisabled && State.Phase != FalloutMinePhase.Disabled)
        {
            // An untouched mine awards XP only if its activating actor could
            // trigger it. A countdown already committed to an actor awards XP
            // independently. In particular, own untriggered mines do not farm XP.
            var reward = State.Phase == FalloutMinePhase.Countdown || _state.ActorEligible(_records.RuntimeFormKey(0x14))
                ? _records.NumericSettings.IntegerBits("iMineDisarmExperience") : 0;
            var next = State.Disable(_source);
            _sounds.StopEmitterSounds(_anchor);
            if (_source.Projectile.DisableSound is { } sound) RequireSound(sound);
            _state.Mine = next;
            PublishLight(false);
            if (reward > 0) experience(reward);
        }
        else _state.Take(inventory, _context.Level(), _context.Globals);
    }

    private void Detonate()
    {
        var explosion = _source.Projectile.ExplosionSource!;
        var point = _anchor.GlobalPosition;
        using var prepared = _effects.PrepareExplosion(explosion, new(Basis.Identity, point));
        // Commit before effects/damage callbacks: a failing blast cannot be
        // silently retried and apply already-delivered health damage twice.
        _state.Mine = State with { Phase = FalloutMinePhase.Detonated, RemainingSeconds = 0, BlinkSeconds = 0 };
        _sounds.StopEmitterSounds(_anchor);
        foreach (var geometry in _root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()) geometry.Hide();
        foreach (var body in _root.FindChildren("*", "", true, false).OfType<CollisionObject3D>()) body.CollisionLayer = body.CollisionMask = 0;
        prepared.Start(() => _state.Taken = true, Fail);
        _ = RuntimeNativeExplosionCombat.Detonate(_root, _root, _records, explosion,
            _state.Damage ?? new(explosion.Damage, 1, 0, 1), point, _mask, _units, _state.DamageAttacker,
            _context.Level(), _context.Globals, _context.Player(), _context.DamagePlayer);
    }

    private void RequireSound(FalloutFormKey sound)
    {
        var disposition = _sounds.DispatchSound(sound, _anchor);
        if (disposition is not ("source-sound-playing" or "source-sound-chance-skipped" or "source-sound-dry-playing-partial"))
            throw new NotSupportedException($"Mine source sound {sound} returned {disposition}; " +
                $"lanes={string.Join(',', _sounds.Unbound)}; event={System.Text.Json.JsonSerializer.Serialize(_sounds.LastEvent)}");
        if (disposition == "source-sound-dry-playing-partial")
        {
            var divergence = $"Mine source audio {sound}: {string.Join(',', _sounds.Unbound)}";
            _failure(divergence);
            GD.PushWarning(divergence);
        }
    }

    private void PublishLight(bool? on)
    {
        if (on is not { } visible) return;
        if (_lightOn is not null) _lightOn.Visible = visible;
        if (_lightOff is not null) _lightOff.Visible = !visible;
    }

    private void Fail(string error)
    {
        _state.Mine = State with { Error = error };
        _failure(error);
        GD.PushError($"OPENNV_NATIVE_MINE_UNBOUND reference={_state.Identity}: {error}");
    }
}
