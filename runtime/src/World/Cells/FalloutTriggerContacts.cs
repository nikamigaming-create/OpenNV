using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Contact discovery belongs to physics; script admission belongs to the world.
// Admit one enter OR one leave per frame, with enters taking precedence. Keep
// deferred contacts so simultaneous overlaps cannot silently lose an event.
internal sealed class FalloutTriggerContacts
{
    private readonly List<FalloutFormKey> _admitted = [];
    private readonly HashSet<FalloutFormKey> _members = [];

    internal IReadOnlyList<FalloutReferenceScriptEvent> Advance(IReadOnlyList<FalloutFormKey> overlapping)
    {
        var physical = overlapping.ToHashSet();
        if (physical.Count != overlapping.Count)
            throw new InvalidDataException("Trigger contacts contain duplicate reference identities.");
        var events = new List<FalloutReferenceScriptEvent>();
        var entered = overlapping.Where(reference => !_members.Contains(reference)).Select(reference => (FalloutFormKey?)reference).FirstOrDefault();
        if (entered is { } added)
        {
            _admitted.Add(added);
            _members.Add(added);
            events.Add(new("OnTriggerEnter", added));
        }
        else
        {
            var departed = _admitted.Where(reference => !physical.Contains(reference)).Select(reference => (FalloutFormKey?)reference).FirstOrDefault();
            if (departed is { } removed)
            {
                _admitted.Remove(removed);
                _members.Remove(removed);
                events.Add(new("OnTriggerLeave", removed));
            }
        }
        if (_admitted.Count != 0) events.Add(new("OnTrigger", TriggerReferences: _admitted.ToHashSet()));
        return events;
    }
}
