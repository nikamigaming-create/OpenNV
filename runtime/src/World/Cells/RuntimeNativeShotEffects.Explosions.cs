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
            var effect = new Effect(root, duration, playback) { Completed = completed, Failed = failed };
            try
            {
                owner.AddChild(root);
                playback?.Start();
                var playing = System.Diagnostics.Stopwatch.GetTimestamp();
                if (source.FirstSound is { } first) Play(first);
                if (source.SecondSound is { } second) Play(second);
                var sounded = System.Diagnostics.Stopwatch.GetTimestamp();
                owner._effects.Add(effect);
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

                void Play(FalloutFormKey sound)
                {
                    var disposition = owner._sounds.DispatchWorldSound(sound, root, admitted: actual =>
                    {
                        if (actual is not ("source-sound-playing" or "source-sound-chance-skipped" or "source-sound-dry-playing-partial"))
                        {
                            effect.Faulted = true;
                            effect.Error = $"Explosion {source.Form} sound {sound} has no complete playback owner: {actual}.";
                            // A synchronous failure is returned to Start's
                            // caller. Later dispatch reports through the live
                            // effect owner after registration.
                            if (owner._effects.Contains(effect)) effect.Failed?.Invoke(effect.Error);
                            GD.PushError("OPENNV_TRANSIENT_EXPLOSION_FAILED " + effect.Error);
                        }
                        if (actual == "source-sound-dry-playing-partial")
                            GD.PushWarning($"OPENNV_EXPLOSION_AUDIO_DIVERGENCE source={source.Form} sound={sound} lanes={string.Join(',', owner._sounds.Unbound)}");
                    });
                    if (disposition != "source-sound-awaiting-pose-publication" && effect.Error is { } error)
                        throw new NotSupportedException(error);
                }
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
            if (source.FirstSound is { } first) _sounds.PrepareSound(first);
            if (source.SecondSound is { } second) _sounds.PrepareSound(second);
            if (source.Model is { } path)
                root.AddChild(RuntimeNativeNifMeshBuilder.Build(ReadModel(path), _units, contentSource: _content).Root);
            var built = System.Diagnostics.Stopwatch.GetTimestamp();
            var controllers = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var duration = controllers.Select(controller => controller.FiniteEffectDuration).DefaultIfEmpty(0).Max();
            var playback = source.Model is null ? null : new NativeNifEffectPlayback(root, duration, encoded: false, _sounds);
            return new(this, source, root, duration, playback, started, built);
        }
        catch { root.Free(); throw; }
    }
}
