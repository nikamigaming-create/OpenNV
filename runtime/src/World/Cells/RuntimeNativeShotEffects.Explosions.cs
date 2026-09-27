using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativeShotEffects
{
    internal void Explosion(FalloutExplosion source, Vector3 point)
    {
        var root = new Node3D { Name = "SourceExplosion", Position = point };
        try
        {
            if (source.Model is { } path)
                root.AddChild(RuntimeNativeNifMeshBuilder.Build(ReadModel(path), _units, contentSource: _content).Root);
            AddChild(root);
            var controllers = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var duration = controllers.Select(controller => controller.FiniteEffectDuration).DefaultIfEmpty(0).Max();
            var playback = source.Model is null ? null : new NativeNifEffectPlayback(root, duration, encoded: false);
            playback?.Start();
            if (source.FirstSound is { } first) _sounds.DispatchSound(first, root);
            if (source.SecondSound is { } second) _sounds.DispatchSound(second, root);
            _effects.Add(new(root, duration, playback));
            GD.Print($"OPENNV_EXPLOSION_EFFECT source={source.Form} model={source.Model} duration={duration:R} imagespace={source.ImageSpace} imagespaceOwner=player-exposure");
        }
        catch { root.Free(); throw; }
    }
}
