using Godot;

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
    private double _elapsed;
    internal bool Active { get; private set; }
    internal double Remaining => Math.Max(0, _duration - _elapsed);

    internal NativeNifEffectPlayback(Node3D root, double duration, bool encoded)
    {
        if (!double.IsFinite(duration) || duration < 0) throw new InvalidDataException("Effect duration is invalid.");
        _root = root; _duration = duration;
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
        foreach (var controller in _controllers)
        {
            controller.PlaySourceSequence(controller.ActiveSequence ?? controller.SequenceNames.Single());
            controller.SetProcess(false);
        }
        foreach (var (node, visible) in _surfaces) node.Visible = visible;
        foreach (var particle in Particles) particle.EmissionEnabled = _duration > 0;
        _elapsed = 0; Active = true; _root.Visible = true;
    }

    internal void RestartCompleted()
    {
        if (Active) throw new InvalidOperationException("An active effect cannot be recycled.");
        foreach (var particle in Particles) particle.ResetCompleted();
        Start();
    }

    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!Active) return;
        var remaining = delta;
        while (remaining > 1e-9)
        {
            var emitting = _elapsed < _duration;
            var step = Math.Min(remaining, 1.0 / 120);
            if (emitting) step = Math.Min(step, _duration - _elapsed);
            foreach (var controller in _controllers) controller._Process(step * .5);
            foreach (var particle in Particles) { particle.EmissionEnabled = emitting; particle.Advance((float)step); }
            foreach (var controller in _controllers) controller._Process(step * .5);
            _elapsed += step; remaining -= step;
        }
        foreach (var particle in Particles) { particle.EmissionEnabled = _elapsed < _duration; particle.Publish(); }
        if (_elapsed < _duration) return;
        foreach (var (node, _) in _surfaces) node.Visible = false;
        if (Particles.Any(particle => particle.ActiveCount != 0)) return;
        Active = false; _root.Visible = false;
    }
}
