using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Non-spatial script SFX consume owned PCM; the C# owner retains admission/lifetime.</summary>
internal sealed partial class ClassicScriptSoundPlayback : Node
{
    private sealed record Voice(AudioStreamPlayer Player, AudioStreamWav Stream);
    private readonly Dictionary<long, Voice> _voices = [];
    private ClassicScriptSounds _owner = null!;
    internal event Action<string>? Failed;
    internal int PlayingCount => _voices.Count;
    internal void Configure(ClassicScriptSounds owner) => _owner = owner;

    public override void _Process(double delta)
    {
        if (_owner is null) return;
        foreach (var request in _owner.TakePending())
        {
            AudioStreamWav? stream = null; AudioStreamPlayer? player = null;
            try
            {
                stream = new AudioStreamWav
                { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Stereo = false, Data = request.Pcm.Bytes };
                player = new AudioStreamPlayer { Stream = stream, VolumeDb = request.Decibels };
                AddChild(player);
                _voices.Add(request.Id, new(player, stream));
                player.Finished += () => Retire(request.Id);
                player.Play();
                if (!player.Playing) throw new InvalidOperationException("Godot did not start the classic source sound.");
            }
            catch (Exception error)
            {
                if (_voices.ContainsKey(request.Id)) Retire(request.Id);
                else { player?.Free(); stream?.Dispose(); _owner.Retire(request.Id); }
                Failed?.Invoke($"Script sound {request.Pcm.Path}: {error.Message}");
            }
        }
    }

    private void Retire(long id)
    {
        if (_voices.Remove(id, out var voice))
        { voice.Player.Stop(); voice.Player.Stream = null; voice.Player.QueueFree(); voice.Stream.Dispose(); }
        _owner.Retire(id);
    }
    public override void _ExitTree()
    {
        foreach (var id in _voices.Keys.ToArray()) Retire(id);
        _owner?.Clear();
    }
}
