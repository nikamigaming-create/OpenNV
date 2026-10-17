using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeShotEffects
{
    private object? _lastExplosionEffect;
    internal void Explosion(FalloutExplosion source, Vector3 point)
    {
        using var prepared = PrepareExplosion(source, new(Basis.Identity, point));
        prepared.Start();
    }

    internal sealed class PreparedExplosion(RuntimeNativeShotEffects owner, FalloutExplosion source,
        Node3D root, double duration, NativeNifEffectPlayback? playback, long started, long built) : IDisposable
    {
        private bool _started;
        internal Node3D Start(Action? completed = null, Action<string>? failed = null)
        {
            if (_started) throw new InvalidOperationException("Prepared explosion cannot be started twice.");
            _started = true;
            try
            {
                owner.AddChild(root);
                playback?.Start();
                var playing = System.Diagnostics.Stopwatch.GetTimestamp();
                if (source.FirstSound is { } first) owner._sounds.DispatchSound(first, root);
                if (source.SecondSound is { } second) owner._sounds.DispatchSound(second, root);
                var sounded = System.Diagnostics.Stopwatch.GetTimestamp();
                owner._effects.Add(new(root, duration, playback) { Completed = completed, Failed = failed });
                owner._lastExplosionEffect = new
                {
                    source = source.Form.ToString(),
                    duration,
                    tracked = completed is not null,
                    buildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, built).TotalMilliseconds,
                    playbackMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(built, playing).TotalMilliseconds,
                    soundMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(playing, sounded).TotalMilliseconds,
                };
                GD.Print($"OPENNV_EXPLOSION_EFFECT source={source.Form} model={source.Model} duration={duration:R} imagespace={source.ImageSpace} imagespaceOwner=player-exposure");
                return root;
            }
            catch { root.Free(); throw; }
        }
        public void Dispose() { if (!_started) root.Free(); }
    }

    internal PreparedExplosion PrepareExplosion(FalloutExplosion source, Transform3D transform)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!transform.IsFinite()) throw new InvalidDataException("Explosion transform is not finite.");
        var root = new Node3D
        {
            Name = "SourceExplosion",
            Transform = source.UsesWorldOrientation ? new(Basis.Identity, transform.Origin) : transform
        };
        try
        {
            if (source.Model is { } path)
                root.AddChild(RuntimeNativeNifMeshBuilder.Build(ReadModel(path), _units, contentSource: _content).Root);
            var built = System.Diagnostics.Stopwatch.GetTimestamp();
            var controllers = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var duration = controllers.Select(controller => controller.FiniteEffectDuration).DefaultIfEmpty(0).Max();
            var playback = source.Model is null ? null : new NativeNifEffectPlayback(root, duration, encoded: false);
            return new(this, source, root, duration, playback, started, built);
        }
        catch { root.Free(); throw; }
    }
}
