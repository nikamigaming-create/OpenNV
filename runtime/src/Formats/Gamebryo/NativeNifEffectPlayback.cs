using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// The effect, its controllers and its emitters consume one clock. In particular,
// a host hitch cannot close an emission window before its particles advance.
internal sealed class NativeNifEffectPlayback
{
    private readonly Node3D _root;
    private readonly RuntimeNifControllerPlayer[] _controllers;
    internal RuntimeNifParticleSystem[] Particles { get; }
    private readonly (Node3D Node, bool Visible)[] _surfaces;
    private readonly double _duration;
    private readonly NativeOwnedAnimationSoundPlayer? _sounds;
    private readonly (Node3D Source, FalloutFormKey Sound)[] _addonSounds;
    private readonly Func<Node3D, Node3D>? _soundEmitter;
    private readonly HashSet<Node3D> _resolvedSoundEmitters = [];
    private double _elapsed;
    internal bool Active { get; private set; }
    internal int AddonSoundCount => _addonSounds.Length;
    internal string? AudioError { get; private set; }
    internal double Remaining => Math.Max(0, _duration - _elapsed);

    internal NativeNifEffectPlayback(Node3D root, double duration, bool encoded,
        NativeOwnedAnimationSoundPlayer? sounds = null, Func<Node3D, Node3D>? soundEmitter = null)
    {
        if (!double.IsFinite(duration) || duration < 0) throw new InvalidDataException("Effect duration is invalid.");
        _root = root; _duration = duration;
        _sounds = sounds; _soundEmitter = soundEmitter;
        var nodes = root.FindChildren("*", "", true, false);
        _controllers = nodes.OfType<RuntimeNifControllerPlayer>().ToArray();
        Particles = nodes.OfType<RuntimeNifParticleSystem>().ToArray();
        foreach (var controller in _controllers)
        {
            if (controller.ActiveSequence is null && controller.SequenceNames.Count != 1)
                throw new NotSupportedException("Transient effect has ambiguous source sequence selection.");
            controller.SetProcess(false);
        }
        foreach (var particle in Particles) { particle.EmissionEnabled = false; particle.SetProcess(false); particle.SetOutputEncoding(encoded); }
        // Addon smoke continues after the muzzle mesh's source duration.
        var addons = nodes.Where(node => node.HasMeta("opennv_addon_form")).ToArray();
        _addonSounds = addons.OfType<Node3D>().Where(node => node.HasMeta("opennv_addon_sound_plugin"))
            .Select(node => (node, new FalloutFormKey(node.GetMeta("opennv_addon_sound_plugin").AsString(),
                node.GetMeta("opennv_addon_sound_object").AsUInt32()))).ToArray();
        if (sounds is not null)
            foreach (var (node, sound) in _addonSounds)
            {
                try
                {
                    sounds.PrepareSound(sound);
                    node.RemoveMeta("opennv_addon_audio_unbound");
                    node.SetMeta("opennv_addon_audio_owner", "source-effect-activation");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                { AudioError = error.Message; }
            }
        _surfaces = nodes.OfType<MeshInstance3D>().Where(mesh => !addons.Any(addon => addon.IsAncestorOf(mesh)))
            .Select(mesh => ((Node3D)mesh, mesh.Visible)).ToArray();
        foreach (var mesh in nodes.OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is ShaderMaterial { ResourceName: NativeNifEffectMaterial.ResourceIdentity } shader)
                    shader.SetShaderParameter("source_store_encoded", encoded);
        root.Visible = false;
    }

    internal void Start()
    {
        AudioError = null;
        RetireSounds();
        _resolvedSoundEmitters.Clear();
        foreach (var (source, sound) in _addonSounds)
        {
            try
            {
                var sounds = _sounds ?? throw new NotSupportedException("ADDN sound has no gameplay effect audio owner.");
                var emitter = _soundEmitter?.Invoke(source) ?? source;
                if (!GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree())
                    throw new NotSupportedException("ADDN sound emitter is no longer resident.");
                _resolvedSoundEmitters.Add(emitter);
                sounds.StopEmitterLoops(emitter);
                if (sounds.DispatchSound(sound, emitter) == "unbound-source-sound")
                    throw new NotSupportedException($"ADDN sound {sound} could not start its source voice.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
            {
                AudioError = error.Message;
                GD.PushError($"OPENNV_ADDON_AUDIO_FAILED sound={sound}: {error.Message}");
            }
        }
        foreach (var controller in _controllers)
        {
            controller.PlaySourceSequence(controller.ActiveSequence ?? controller.SequenceNames.Single());
            controller.SetProcess(false);
        }
        foreach (var (node, visible) in _surfaces) node.Visible = visible;
        foreach (var particle in Particles) { particle.EmissionEnabled = _duration > 0; particle.ResetParentMotion(); }
        _elapsed = 0; Active = true; _root.Visible = true;
    }

    internal void RestartCompleted()
    {
        if (Active) throw new InvalidOperationException("An active effect cannot be recycled.");
        foreach (var particle in Particles) particle.ResetCompleted();
        Start();
    }

    internal void RetireSounds()
    {
        // Resolution belongs to activation. A replacement view or muzzle can
        // already exist during retirement; never resolve an old path into it.
        // Own authored nodes also retire voices emitted through another view.
        foreach (var emitter in _resolvedSoundEmitters.Concat(_addonSounds.Select(sound => sound.Source)).Distinct())
        {
            if (GodotObject.IsInstanceValid(emitter)) _sounds?.StopEmitterLoops(emitter);
        }
    }

    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!Active) return;
        foreach (var particle in Particles) particle.BeginParentMotion(delta);
        var remaining = delta;
        while (remaining > 1e-9)
        {
            var emitting = _elapsed < _duration;
            var step = Math.Min(remaining, 1.0 / 120);
            if (emitting) step = Math.Min(step, _duration - _elapsed);
            // Authored bursts can be only ten microseconds long. Split at
            // controller edges so a render frame cannot skip their emission.
            foreach (var controller in _controllers)
                if (controller.SecondsToBoundary is > 1e-10 and var boundary)
                    step = Math.Min(step, boundary);
            foreach (var controller in _controllers) controller._Process(step * .5);
            foreach (var particle in Particles) { particle.EmissionEnabled = emitting; particle.Advance((float)step); }
            foreach (var controller in _controllers) controller._Process(step * .5);
            _elapsed += step; remaining -= step;
        }
        foreach (var particle in Particles) { particle.EmissionEnabled = _elapsed < _duration; particle.EndParentMotion(); particle.Publish(); }
        if (_elapsed < _duration) return;
        foreach (var (node, _) in _surfaces) node.Visible = false;
        if (Particles.Any(particle => particle.ActiveCount != 0)) return;
        RetireSounds();
        Active = false; _root.Visible = false;
    }
}
