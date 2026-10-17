using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutAnimationNoteReceiverPair(Guid Identity, uint Type, Guid Receiver);
internal sealed record FalloutAnimationNoteSourceSnapshot(Guid Identity, ushort Capacity, ushort Size,
    ushort EffectiveSize, ushort GrowBy, IReadOnlyList<FalloutAnimationNoteReceiverPair> Receivers)
{
    internal void Validate()
    {
        if (Identity == Guid.Empty || GrowBy != 1 || Receivers is null || Capacity != Size || Size != EffectiveSize || Size != Receivers.Count ||
            Receivers.Any(pair => pair.Identity == Guid.Empty || pair.Receiver == Guid.Empty) ||
            Receivers.Select(pair => pair.Identity).Distinct().Count() != Receivers.Count ||
            Receivers.Select(pair => (pair.Type, pair.Receiver)).Distinct().Count() != Receivers.Count)
            throw new InvalidDataException("Animation-note source lost its independent actual receiver array.");
    }
}

internal sealed class FalloutAnimationNoteSourceState
{
    private sealed record Entry(FalloutAnimationNoteReceiverPair State, FalloutActorAnimationNotes Receiver);
    private readonly Guid _identity = Guid.NewGuid();
    private readonly List<Entry> _entries = [];
    private bool _retired;

    internal bool Register(uint type, FalloutActorAnimationNotes receiver)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var current = receiver.Capture(); var last = -1;
        for (var index = 0; index < _entries.Count; ++index)
            if (_entries[index].State.Type == type && ReferenceEquals(_entries[index].Receiver, receiver)) last = index;
        if (last == -1)
        {
            if (_entries.Count == ushort.MaxValue) throw new NotSupportedException("source-animation-note-source-word-capacity-overflow-unowned");
            _entries.Add(new(new(Guid.NewGuid(), type, current.Identity), receiver));
        }
        return false;
    }
    internal void Dispatch(FalloutAnimationNote note)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        for (var index = 0; index < _entries.Count; ++index)
            if (_entries[index].State.Type == note.Type) _entries[index].Receiver.Dispatch(note);
    }
    internal FalloutAnimationNoteSourceSnapshot Capture()
    {
        var size = checked((ushort)_entries.Count);
        var state = new FalloutAnimationNoteSourceSnapshot(_identity, size, size, size, 1, _entries.Select(entry => entry.State).ToArray());
        state.Validate(); return state;
    }
    internal void Retire() { _entries.Clear(); _retired = true; }
}
