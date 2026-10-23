using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerNotesSnapshot(IReadOnlyList<FalloutFormKey> Notes, bool Known = true);

// Collected NOTE identities have their own ordered player state. These methods
// own membership only; command feedback and note playback belong to their callers.
internal sealed class FalloutPlayerNotes
{
    private readonly FalloutPluginStack _records;
    private readonly List<FalloutFormKey> _notes = [];
    internal bool Known { get; private set; } = true;
    internal long Revision { get; private set; }

    internal FalloutPlayerNotes(FalloutPluginStack records) => _records = records;

    internal FalloutPlayerNotes(FalloutPluginStack records, FalloutPlayerNotesSnapshot? snapshot) : this(records)
        => Restore(snapshot);

    internal IReadOnlyList<FalloutFormKey> Notes
    {
        get
        {
            RequireKnown();
            return _notes.ToArray();
        }
    }

    internal bool Contains(FalloutFormKey note)
    {
        var canonical = RequireNote(note);
        RequireKnown();
        return _notes.Contains(canonical);
    }

    internal bool AddMembership(FalloutFormKey note)
    {
        var canonical = RequireNote(note);
        RequireKnown();
        if (_notes.Contains(canonical)) return false;
        _notes.Insert(0, canonical);
        ++Revision;
        return true;
    }

    internal bool RemoveMembership(FalloutFormKey note)
    {
        var canonical = RequireNote(note);
        RequireKnown();
        if (!_notes.Remove(canonical)) return false;
        ++Revision;
        return true;
    }

    internal FalloutPlayerNotesSnapshot Capture() => new(_notes.ToArray(), Known);

    internal void Restore(FalloutPlayerNotesSnapshot? snapshot)
    {
        // A missing legacy snapshot carries no authority for even an absent
        // note. Keep that distinction through subsequent saves and UI queries.
        if (snapshot is null)
        {
            _notes.Clear();
            Known = false;
            ++Revision;
            return;
        }
        if (snapshot.Notes is null || !snapshot.Known && snapshot.Notes.Count != 0)
            throw new InvalidDataException("Saved player note membership is absent or has unknown retained identities.");
        // Validate the entire replacement before publishing it. Source loading
        // preserves list order; it does not run AddNote or replay its feedback.
        var replacement = snapshot.Notes.Select(RequireNote).ToArray();
        _notes.Clear();
        _notes.AddRange(replacement);
        Known = snapshot.Known;
        ++Revision;
    }

    private void RequireKnown()
    {
        if (!Known) throw new NotSupportedException("Saved campaign has no authoritative collected-note membership.");
    }

    private FalloutFormKey RequireNote(FalloutFormKey note)
    {
        if (string.IsNullOrWhiteSpace(note.OwnerPlugin) || note.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Collected player note has an invalid canonical form identity.");
        var record = _records.GetEffective(note);
        if (record.Signature != "NOTE") throw new InvalidDataException("Collected player note identity is not NOTE.");
        // Membership uses identity, independently of text, image or voice
        // decoding. The selected presentation owner validates those resources.
        return record.FormKey;
    }
}
