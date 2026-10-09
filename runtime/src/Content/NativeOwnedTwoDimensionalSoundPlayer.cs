using Godot;

namespace OpenNV.Runtime.Content;

// LoadAudio returns a fresh decoded resource for this one voice. This owner
// releases that resource's managed reference after stopping/detaching it; it
// never decrements a sibling resource or reconstructs a wrapper from an ID.
internal sealed partial class NativeOwnedTwoDimensionalSoundPlayer : AudioStreamPlayer
{
    private AudioStream? _ownedStream;
    internal ulong OwnedStreamId { get; private set; }
    internal bool OwnedStreamReleased { get; private set; }
    internal bool OwnsDecodedStream => _ownedStream is not null;
    internal event Action? SourceRegistryStopped;

    internal void StopFromSourceRegistry()
    {
        Stop();
        if (Playing) throw new InvalidOperationException("Original source SOUN registry did not stop its actual 2D voice.");
        SourceRegistryStopped?.Invoke();
    }

    internal void AdoptDecodedStream(AudioStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (_ownedStream is not null || OwnedStreamId != 0 || Stream is not null)
            throw new InvalidOperationException("Native 2D voice already owns a decoded source stream.");
        _ownedStream = stream; OwnedStreamId = stream.GetInstanceId();
        // Publish ownership before a native setter can fail.
        Stream = stream;
    }

    internal void ReleaseDecodedStream()
    {
        if (OwnedStreamReleased) return;
        var stream = _ownedStream;
        if (stream is null)
        {
            if (OwnedStreamId != 0) throw new InvalidOperationException("Native 2D voice lost its original stream wrapper.");
            return;
        }
        Stop();
        if (Playing) throw new InvalidOperationException("Native 2D voice still plays while releasing its owned source stream.");
        Stream = null;
        stream.Dispose();
        _ownedStream = null; OwnedStreamReleased = true;
    }

    public override void _ExitTree() => ReleaseDecodedStream();
    public override void _Notification(int what)
    {
        // Prepared players can fail before tree attachment. Explicit factory/
        // host cleanup uses the same owner; predelete is the detached fallback.
        if (what == NotificationPredelete) ReleaseDecodedStream();
    }
}
