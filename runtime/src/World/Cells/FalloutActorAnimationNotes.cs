using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutAnimationNoteCallbackRole { RagdollArm }
internal sealed record FalloutAnimationNote(uint Type, int Arm, uint Value, int Action);
internal sealed record FalloutAnimationNoteRegistration(Guid Identity, uint Type, FalloutAnimationNoteCallbackRole Role);
internal sealed record FalloutActorAnimationNotesSnapshot(Guid Identity, Guid Process, Guid Constructor,
    FalloutCombatActorIdentity Actor, ushort Capacity, ushort Size, ushort EffectiveSize, ushort GrowBy,
    IReadOnlyList<FalloutAnimationNoteRegistration> Registrations)
{
    internal void Validate()
    {
        Actor.Validate();
        if (Identity == Guid.Empty || Process == Guid.Empty || Constructor == Guid.Empty || GrowBy != 1 ||
            Registrations is null || Capacity != Size || Size != EffectiveSize || Size != Registrations.Count ||
            Registrations.Any(entry => entry.Identity == Guid.Empty || !Enum.IsDefined(entry.Role)) ||
            Registrations.Select(entry => entry.Identity).Distinct().Count() != Registrations.Count ||
            Registrations.Select(entry => (entry.Type, entry.Role)).Distinct().Count() != Registrations.Count)
            throw new InvalidDataException("Actor animation-note receiver lost its actual constructor or callback array.");
    }
}

// The receiver is an independent object retained by ExtraAnimNoteReceiver.
// Each pair holds an actual callable; registration does not dispatch a note.
internal sealed class FalloutActorAnimationNotes
{
    private sealed record Entry(FalloutAnimationNoteRegistration State, Action<FalloutAnimationNote> Callback);
    private readonly Guid _identity = Guid.NewGuid(), _process, _constructor;
    private readonly FalloutCombatActorIdentity _actor;
    private readonly List<Entry> _entries = [];
    internal FalloutActorAnimationNotes(Guid process, Guid constructor, FalloutCombatActorIdentity actor)
    {
        actor.Validate();
        if (process == Guid.Empty || constructor == Guid.Empty) throw new InvalidDataException("Animation receiver lost its actual Actor lifetime.");
        _process = process; _constructor = constructor; _actor = actor;
    }
    internal bool RegisterRagdoll(FalloutActorRagdollControllerField actorField)
    {
        var field = actorField.Capture();
        if (field.Process != _process || field.Constructor != _constructor || field.Actor != _actor)
            throw new InvalidOperationException("Animation callback borrowed another actual Actor field.");
        var last = -1;
        for (var index = 0; index < _entries.Count; ++index)
            if (_entries[index].State is { Type: 1, Role: FalloutAnimationNoteCallbackRole.RagdollArm }) last = index;
        if (last == -1)
        {
            if (_entries.Count == ushort.MaxValue) throw new NotSupportedException("source-animation-note-array-word-capacity-overflow-unowned");
            _entries.Add(new(new(Guid.NewGuid(), 1, FalloutAnimationNoteCallbackRole.RagdollArm), actorField.OnAnimationNote));
        }
        return false;
    }
    internal void Dispatch(FalloutAnimationNote note)
    {
        for (var index = 0; index < _entries.Count; ++index)
            if (_entries[index].State.Type == note.Type) _entries[index].Callback(note);
    }
    internal FalloutActorAnimationNotesSnapshot Capture()
    {
        var size = checked((ushort)_entries.Count);
        var state = new FalloutActorAnimationNotesSnapshot(_identity, _process, _constructor, _actor, size, size, size, 1,
            _entries.Select(entry => entry.State).ToArray());
        state.Validate(); return state;
    }
}
