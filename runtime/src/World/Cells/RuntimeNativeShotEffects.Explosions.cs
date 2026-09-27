using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeShotEffects
{
    private object? _lastExplosionEffect;
    internal void Explosion(FalloutExplosion source, Vector3 point)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var root = new Node3D { Name = "SourceExplosion", Position = point };
        try
        {
            if (source.Model is { } path)
                root.AddChild(RuntimeNativeNifMeshBuilder.Build(ReadModel(path), _units, contentSource: _content).Root);
            var built = System.Diagnostics.Stopwatch.GetTimestamp();
            AddChild(root);
            var controllers = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var duration = controllers.Select(controller => controller.FiniteEffectDuration).DefaultIfEmpty(0).Max();
            var playback = source.Model is null ? null : new NativeNifEffectPlayback(root, duration, encoded: false);
            playback?.Start();
            var playing = System.Diagnostics.Stopwatch.GetTimestamp();
            if (source.FirstSound is { } first) _sounds.DispatchSound(first, root);
            if (source.SecondSound is { } second) _sounds.DispatchSound(second, root);
            var sounded = System.Diagnostics.Stopwatch.GetTimestamp();
            _effects.Add(new(root, duration, playback));
            _lastExplosionEffect = new
            {
                source = source.Form.ToString(),
                buildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, built).TotalMilliseconds,
                playbackMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(built, playing).TotalMilliseconds,
                soundMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(playing, sounded).TotalMilliseconds,
            };
            GD.Print($"OPENNV_EXPLOSION_EFFECT source={source.Form} model={source.Model} duration={duration:R} imagespace={source.ImageSpace} imagespaceOwner=player-exposure");
        }
        catch { root.Free(); throw; }
    }
}
