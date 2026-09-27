using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

// A reference retains damage independently of model residency. Ordinary weapon
// contacts and explosion overlaps both call Hit; self DPS uses the same stages.
internal sealed partial class RuntimeNativeDestructible : Node
{
    private const string OwnerMeta = "opennv_destruction_owner";
    private Node3D _root = null!;
    private Node3D[] _model = [];
    private FalloutDestructible _source = null!;
    private FalloutReferenceInstance _state = null!;
    private FalloutPluginStack _records = null!;
    private RuntimeLiveContentSource _content = null!;
    private NativeActorCombatContext _context = null!;
    private RuntimeNativeShotEffects _effects = null!;
    private float _units;
    private uint _mask;
    private long _hits, _stageEvents;
    internal Action? ModelChanged { get; set; }
    internal FalloutDestructionState State => _state.Destruction!;
    internal object Observation => new { reference = _state.Reference.ToString(), state = State, hits = _hits, stageEvents = _stageEvents };

    internal static RuntimeNativeDestructible? Find(Node node)
    {
        for (Node? parent = node; parent is not null; parent = parent.GetParent())
            if (parent.HasMeta(OwnerMeta))
            {
                var owner = parent.GetMeta(OwnerMeta).AsGodotObject() as RuntimeNativeDestructible;
                if (!IsInstanceValid(owner) || owner!.GetParent() != parent)
                    throw new InvalidOperationException("Destruction owner left its reference root.");
                return owner;
            }
        return null;
    }

    public override void _EnterTree() => _root.SetMeta(OwnerMeta, this);
    public override void _ExitTree() => _root.RemoveMeta(OwnerMeta);

    internal static RuntimeNativeDestructible? Attach(Node3D root, FalloutReferenceInstance state,
        FalloutPluginStack records, RuntimeLiveContentSource content, float units, uint mask, NativeActorCombatContext context)
    {
        if (FalloutDestructible.Read(records, state.Base) is not { } source) return null;
        state.Destruction ??= FalloutDestructionState.Initial(source);
        state.Destruction.Validate(source);
        var owner = new RuntimeNativeDestructible
        {
            Name = "SourceDestruction",
            _root = root,
            _model = root.GetChildren().OfType<Node3D>().ToArray(),
            _source = source,
            _state = state,
            _records = records,
            _content = content,
            _units = units,
            _mask = mask,
            _context = context
        };
        owner._effects = new(records, content, units, null, mask);
        root.AddChild(owner); owner.AddChild(owner._effects);
        // Cold restoration selects the last replacement without replaying blasts.
        if (source.Stages.Take(state.Destruction.Stage + 1).LastOrDefault(stage => stage.Model is not null)?.Model is { } model)
            owner.ReplaceModel(model);
        owner.SelectModelStage(state.Destruction.ModelStage);
        owner.Publish();
        return owner;
    }

    internal float Hit(float amount, FalloutFormKey? attacker)
    {
        var before = State.Health;
        Apply(amount, attacker, false);
        _hits++;
        return before - State.Health;
    }

    private void Apply(float amount, FalloutFormKey? attacker, bool self)
    {
        var (next, entered) = State.Damage(_source, amount, attacker, self);
        _state.Destruction = next;
        _state.Destroyed |= next.Destroyed;
        if (next.Disabled) _state.Enabled = false;
        try
        {
            foreach (var stage in entered)
            {
                SelectModelStage(stage.ModelStage);
                var point = BlastPoint(stage.ModelStage);
                if (stage.Model is { } model) ReplaceModel(model);
                if (stage.Debris is not null && stage.DebrisCount > 0)
                    throw new NotSupportedException($"Destruction {_source.Form}/{stage.Index} needs its source DEBR spawner.");
                if (stage.Explosion is { } key)
                {
                    var explosion = FalloutExplosion.Read(_records, key);
                    _effects.Explosion(explosion, point);
                    RuntimeNativeExplosionCombat.Detonate(_root, _root, _records, explosion,
                        new(explosion.Damage, 1, 0, 1), point, _mask, _units, State.Attacker ?? _state.Reference,
                        _context.Level(), _context.Globals, _context.Player(), _context.DamagePlayer);
                }
                _stageEvents++;
                GD.Print($"OPENNV_DESTRUCTION_STAGE reference={_state.Reference} stage={stage.Index} modelStage={stage.ModelStage} health={State.Health:R} explosion={stage.Explosion}");
            }
        }
        catch (Exception error)
        {
            _state.Destruction = State with { Error = error.Message };
            GD.PushError($"OPENNV_DESTRUCTION_UNBOUND reference={_state.Reference} {error.Message}");
            throw;
        }
        finally { Publish(); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var remaining = delta;
        try
        {
            while (remaining > 0 && State is { Stage: >= 0, Health: > 0, Disabled: false, Error: null })
            {
                var dps = _source.Stages[State.Stage].SelfDamagePerSecond;
                if (dps == 0) break;
                var threshold = State.Stage + 1 < _source.Stages.Count
                    ? _source.Health * _source.Stages[State.Stage + 1].HealthPercent / 100f : 0;
                var step = Math.Min(remaining, Math.Max(0, (State.Health - threshold) / dps));
                var amount = step < remaining ? Math.Max(0, State.Health - threshold) : (float)(dps * step);
                if (amount == 0) amount = State.Health - MathF.BitDecrement(State.Health);
                var previous = State;
                Apply(amount, State.Attacker, true);
                remaining -= step;
                if (State == previous) break;
            }
        }
        catch (Exception) { SetPhysicsProcess(false); } // Apply retained and reported the reached failure.
    }

    private void SelectModelStage(byte stage)
    {
        foreach (var node in _model.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(root)))
        {
            if (!node.HasMeta("opennv_nif_range")) continue;
            var range = node.GetMeta("opennv_nif_range").AsInt32Array();
            var active = stage >= range[0] && stage <= range[1];
            GamebryoReferenceEnableRuntime.Apply(node, active);
            range[2] = stage; node.SetMeta("opennv_nif_range", range);
        }
    }

    private Vector3 BlastPoint(byte stage)
    {
        var markers = _model.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>())
            .Where(node => node.GetMeta("opennv_nif_range_type", "").AsString() == "BSBlastNode" &&
                node.GetMeta("opennv_nif_range").AsInt32Array() is var range && stage >= range[0] && stage <= range[1]).ToArray();
        if (markers.Length != 0)
            return markers[Math.Min(markers.Length - 1, (int)(_state.SoundRandom.NextUnitFloat() * markers.Length))].GlobalPosition;
        var meshes = _model.SelectMany(root => root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>()).ToArray();
        if (meshes.Length == 0) throw new NotSupportedException("Destructible has neither a blast node nor source geometry bounds.");
        var bounds = meshes[0].GlobalTransform * meshes[0].GetAabb();
        foreach (var mesh in meshes.Skip(1)) bounds = bounds.Merge(mesh.GlobalTransform * mesh.GetAabb());
        return bounds.Position + bounds.Size * new Vector3(_state.SoundRandom.NextUnitFloat(), _state.SoundRandom.NextUnitFloat(), _state.SoundRandom.NextUnitFloat());
    }

    private void ReplaceModel(string path)
    {
        if (!_content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Destruction model is missing.", path);
        var prototype = new RuntimeNativeNifPrototype(bytes, _units);
        Node3D replacement;
        try { replacement = prototype.InstantiatePlaced(Transform3D.Identity); }
        finally { prototype.Scene.Root.Free(); }
        _root.AddChild(replacement);
        foreach (var old in _model) { _root.RemoveChild(old); old.QueueFree(); }
        _model = [replacement];
        _root.SetMeta("opennv_source_model", path);
        SelectModelStage(State.ModelStage);
        ModelChanged?.Invoke();
    }

    private void Publish() => _root.SetMeta("opennv_destruction", System.Text.Json.JsonSerializer.Serialize(Observation));
}
