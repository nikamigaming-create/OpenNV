using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Content;

// The actual finite audio node belongs to the loaded source graph. Its model
// attachment supplies position while resident, then leaves the last real pose.
// Neither detaching an emitter nor a zero voice count supplies Finished.
internal sealed partial class NativeOwnedFiniteSoundHost : Node3D
{
    private static readonly ConditionalWeakTable<FalloutPluginStack, NativeOwnedFiniteSoundHost> Hosts = new();
    private readonly HashSet<Attachment> _attachments = [];
    private IDisposable? _graphRetirement;
    private bool _retiring;

    internal sealed class Attachment : IDisposable
    {
        private readonly NativeOwnedFiniteSoundHost _host;
        private Node3D? _emitter;
        private readonly Action _cancel;
        private readonly Action _update;
        private bool _disposed;
        internal Node Voice { get; }
        internal ulong EmitterNativeOwner { get; }
        internal string EmitterPath { get; }
        internal Vector3 LastRealPosition { get; private set; }
        internal bool FollowEmitter { get; }
        internal bool EmitterRetired => FollowEmitter && _emitter is null;
        internal bool Alive => !_disposed && !_host._retiring && GodotObject.IsInstanceValid(Voice) && Voice.IsInsideTree();

        internal Attachment(NativeOwnedFiniteSoundHost host, Node voice, Node3D emitter, Action cancel, Action update, bool followEmitter)
        {
            _host = host; Voice = voice; _emitter = followEmitter ? emitter : null; _cancel = cancel; _update = update;
            FollowEmitter = followEmitter;
            EmitterNativeOwner = emitter.GetInstanceId(); EmitterPath = emitter.GetPath().ToString();
            LastRealPosition = emitter.GlobalPosition;
            if (followEmitter) emitter.TreeExiting += RetireEmitter;
        }

        internal void PublishPosition()
        {
            if (_disposed || !GodotObject.IsInstanceValid(Voice)) return;
            if (_emitter is { } emitter)
            {
                if (GodotObject.IsInstanceValid(emitter) && emitter.IsInsideTree()) LastRealPosition = emitter.GlobalPosition;
                else RetireEmitter();
            }
            if (Voice is AudioStreamPlayer3D spatial) spatial.GlobalPosition = LastRealPosition;
            _update();
        }

        private void RetireEmitter()
        {
            if (_emitter is not { } emitter) return;
            if (GodotObject.IsInstanceValid(emitter))
            {
                // TreeExiting runs before the last actual transform is lost.
                if (emitter.IsInsideTree()) LastRealPosition = emitter.GlobalPosition;
                emitter.TreeExiting -= RetireEmitter;
            }
            _emitter = null;
            if (GodotObject.IsInstanceValid(Voice) && Voice is AudioStreamPlayer3D spatial)
                spatial.GlobalPosition = LastRealPosition;
        }

        internal void Cancel() => _cancel();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_emitter is { } emitter && GodotObject.IsInstanceValid(emitter)) emitter.TreeExiting -= RetireEmitter;
            _emitter = null;
            _host._attachments.Remove(this);
        }
    }

    internal static Attachment Attach(FalloutPluginStack records, FalloutAnimationSoundEvents events,
        long generation, Node voice, Node3D emitter, FalloutSoundLoop loop, Action cancel, Action update,
        bool followEmitter = true)
    {
        if (!GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree())
            throw new NotSupportedException("Finite sound requires its actual resident emitter at creation.");
        var entry = events.Events.SingleOrDefault(entry => entry.Generation == generation);
        var stream = voice is AudioStreamPlayer3D spatial ? spatial.Stream : (voice as AudioStreamPlayer)?.Stream;
        if (entry is not { Played: true, End: FalloutAnimationSoundEnd.Active, Error: null } ||
            loop.Mode != FalloutSoundLoopMode.None || NativeOwnedSoundVoice.Reference(records, emitter) != events.Reference ||
            stream is null || !double.IsFinite(stream.GetLength()) || stream.GetLength() <= 0 ||
            !FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256) ||
            !stream.GetMeta("opennv_owned_media_sha256", "").AsString().Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase) ||
            stream is AudioStreamWav { LoopMode: not AudioStreamWav.LoopModeEnum.Disabled })
            throw new NotSupportedException("Finite sound lifetime lacks its matching actual source emitter, media or finite generation.");
        var source = records.GetEffective(entry.Sound);
        if (source.Signature != "SOUN" ||
            !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(entry.SoundSha256, StringComparison.OrdinalIgnoreCase) ||
            FalloutSoundLoop.Read(FalloutSoundRecordReader.Read(source)).Mode != FalloutSoundLoopMode.None)
            throw new InvalidDataException("Finite sound lifetime differs from its winning source sound.");
        var viewport = emitter.GetViewport();
        if (!Hosts.TryGetValue(records, out var host) || !GodotObject.IsInstanceValid(host))
        {
            Node scene = emitter;
            while (scene.GetParent() is { } parent && parent != viewport) scene = parent;
            if (scene.GetParent() != viewport || scene == emitter ||
                scene.HasMeta("opennv_reference_form_key") || scene.HasMeta("opennv_nif_fixed_strings"))
                throw new NotSupportedException("Finite sound has no scene lifetime outside its source model.");
            host = new NativeOwnedFiniteSoundHost { Name = "OwnedFiniteSourceVoices" };
            // This is the persistent scene owner, outside the source model and
            // CELL. Its parent viewport may still be adding that scene in Ready.
            scene.AddChild(host);
            if (!host.IsInsideTree())
            {
                host.Free();
                throw new NotSupportedException("Finite source audio scene owner is not ready.");
            }
            host._graphRetirement = records.SoundVoices.BindRetirement(host.RetireGraph);
            Hosts.Remove(records); Hosts.Add(records, host);
        }
        if (host._retiring || !host.IsInsideTree() || host.GetViewport() != viewport)
            throw new NotSupportedException("Finite sound has no current source-graph audio lifetime.");
        var attachment = new Attachment(host, voice, emitter, cancel, update, followEmitter);
        try
        {
            host.AddChild(voice); host._attachments.Add(attachment); attachment.PublishPosition();
            return attachment;
        }
        catch { attachment.Dispose(); throw; }
    }

    public override void _Process(double delta)
    {
        foreach (var attachment in _attachments.ToArray()) attachment.PublishPosition();
    }

    private void RetireGraph()
    {
        if (_retiring) return;
        _retiring = true;
        foreach (var attachment in _attachments.ToArray()) attachment.Cancel();
        QueueFree();
    }

    public override void _ExitTree()
    {
        _retiring = true;
        foreach (var attachment in _attachments.ToArray()) attachment.Cancel();
        _graphRetirement?.Dispose(); _graphRetirement = null;
    }
}
