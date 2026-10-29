using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

internal sealed class NativeActorMuzzle(FalloutPluginStack records, RuntimeLiveContentSource content, Node3D socket, float units)
{
    private Node3D? _root;
    private string? _path;
    private NativeNifEffectPlayback? _playback;
    private OmniLight3D? _light;
    private FalloutFormKey? _lightForm;
    private NativeOwnedAnimationSoundPlayer? _audioOwner;
    internal string? LightError { get; private set; }
    internal bool CaptureReady => _playback?.Active != true;
    internal string? CaptureBlocker => CaptureReady ? null : FormattableString.Invariant(
        $"source-muzzle path={_path ?? "unbound"}; active={_playback?.Active}; remainingSeconds={_playback?.Remaining:R}; activeParticles={_playback?.Particles.Sum(value => (long)value.ActiveCount)}; emittingSystems={_playback?.Particles.Count(value => value.EmissionEnabled)}; lightError={LightError ?? "none"}");
    internal object State => new
    {
        captureReady = CaptureReady,
        captureBlocker = CaptureBlocker,
        path = _path,
        visible = _root?.Visible ?? false,
        seconds = _playback?.Remaining,
        particles = _playback?.Particles.Select(value => new { value.ActiveCount, value.BirthCount, value.EmissionEnabled }).ToArray(),
        light = _light is null ? null : new { form = _lightForm?.ToString(), _light.Visible, _light.OmniRange },
        lightError = LightError,
        addonSounds = _playback?.AddonSoundCount ?? 0,
        addonAudioError = _playback?.AudioError,
        unbound = "shared-master-emitter-pooling,muzzle-light-shadow-selection,flat-world-muzzle-light,retail-billboard-motion-match"
    };

    internal void Prepare(FalloutProjectile projectile, bool encoded, NativeOwnedAnimationSoundPlayer? sounds = null,
        Func<NodePath, Node3D>? soundEmitter = null)
    {
        var path = (projectile.Flags & 8) == 0 ? null : projectile.MuzzleFlash;
        if (_path == path && _lightForm == projectile.MuzzleLight && _audioOwner == sounds) return;
        _light?.Free(); _light = null; _lightForm = null;
        LightError = null;
        RetireSounds();
        _root?.Free(); _root = null; _playback = null; _path = null;
        if (path is null) return;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(bytes), units, contentSource: content);
        try
        {
            NativeNifCollisionBuilder.BindAnimatedAttachment(scene.Root);
            socket.AddChild(scene.Root);
            _playback = new(scene.Root, projectile.MuzzleSeconds, encoded, sounds,
                soundEmitter is null ? null : node => soundEmitter(scene.Root.GetPathTo(node)));
            if (projectile.MuzzleLight is { } form)
            {
                try
                {
                    var source = FalloutCellSceneReader.ReadLight(records.GetEffective(form));
                    FalloutPlacedLightResolver.RequireStaticPoint(source, form);
                    var rgb = FalloutPlacedLightResolver.NormalizeLightColor(source.ColorRgb);
                    _light = new()
                    {
                        Name = "SourceMuzzleLight",
                        Visible = false,
                        LightColor = RetailLighting.GodotLightColor(new(rgb[0], rgb[1], rgb[2])),
                        LightEnergy = source.Intensity,
                        OmniRange = source.RadiusGameUnits * units,
                        OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
                    };
                    socket.AddChild(_light); _lightForm = form;
                }
                catch (NotSupportedException error)
                {
                    _lightForm = form; LightError = error.Message;
                    GD.PushWarning($"OPENNV_MUZZLE_LIGHT_UNBOUND source={form} {error.Message}");
                }
            }
            _root = scene.Root; _path = path; _audioOwner = sounds;
        }
        catch { scene.Root.Free(); _playback = null; throw; }
    }

    internal void Flash()
    {
        _playback?.Start();
        if (_light is not null) _light.Visible = _playback is { Remaining: > 0 };
    }

    internal Node3D AddonSoundEmitter(NodePath path) => _root?.GetNodeOrNull<Node3D>(path) is { } node &&
        node.HasMeta("opennv_addon_sound_plugin") ? node :
        throw new NotSupportedException("Muzzle addon sound has no matching actual world emitter.");

    internal void RetireSounds() => _playback?.RetireSounds();

    internal void Advance(double delta)
    {
        _playback?.Advance(delta);
        if (_light is not null) _light.Visible = _playback is { Remaining: > 0 };
    }
}
