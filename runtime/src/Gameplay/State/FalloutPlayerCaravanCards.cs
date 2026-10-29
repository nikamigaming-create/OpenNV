using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerCaravanCardsSnapshot(IReadOnlyList<FalloutFormKey> First,
    IReadOnlyList<FalloutFormKey> Second, bool Known = true);

// Collection identity is independent from inventory instances and match cards.
// The selected executable's add command searches both source lists and inserts
// an absent CCRD at the head of the first. List roles, UI transfers and cold
// campaign integration need separate evidence and owners.
internal sealed class FalloutPlayerCaravanCards(FalloutPluginStack records)
{
    private readonly List<FalloutFormKey> _first = [];
    private readonly List<FalloutFormKey> _second = [];
    internal bool Known { get; private set; } = true;
    internal long Revision { get; private set; }

    internal IReadOnlyList<FalloutFormKey> First { get { RequireKnown(); return _first.ToArray(); } }
    internal IReadOnlyList<FalloutFormKey> Second { get { RequireKnown(); return _second.ToArray(); } }

    // The selected executable counts both collection lists before opening the
    // match interface. Message, opponent and menu ownership remain separate.
    internal bool MeetsMatchMinimum()
    {
        RequireKnown();
        return (long)_first.Count + _second.Count >= 30;
    }

    internal bool Add(FalloutFormKey card)
    {
        RequireKnown(); var key = Canonical(card);
        if (_first.Contains(key) || _second.Contains(key)) return false;
        _first.Insert(0, key); ++Revision; return true;
    }

    internal FalloutPlayerCaravanCardsSnapshot Capture() => new(_first.ToArray(), _second.ToArray(), Known);

    // Prepare replacement without publishing; rollback restores the precise
    // collection revision as well as membership.
    internal FalloutInventoryChangeLease PrepareReplacement(FalloutPlayerCaravanCardsSnapshot replacement)
    {
        var staged = new FalloutPlayerCaravanCards(records);
        staged.Restore(replacement);
        var before = Capture(); var after = staged.Capture(); var revision = Revision;
        var changed = before.Known != after.Known || !before.First.SequenceEqual(after.First) ||
            !before.Second.SequenceEqual(after.Second);
        if (changed) _ = checked(revision + 1);
        void Apply(FalloutPlayerCaravanCardsSnapshot value, long version)
        {
            _first.Clear(); _first.AddRange(value.First); _second.Clear(); _second.AddRange(value.Second);
            Known = value.Known; Revision = version;
        }
        var applied = false;
        return new(() =>
        {
            if (Revision != revision) throw new InvalidOperationException("Caravan collection changed during prepared pickup.");
            if (changed) { Apply(after, revision + 1); applied = true; }
        }, () => { if (applied) Apply(before, revision); });
    }

    internal void Restore(FalloutPlayerCaravanCardsSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            _first.Clear(); _second.Clear(); Known = false; ++Revision; return;
        }
        if (snapshot.First is null || snapshot.Second is null ||
            !snapshot.Known && (snapshot.First.Count != 0 || snapshot.Second.Count != 0))
            throw new InvalidDataException("Caravan collection snapshot is absent or has unknown retained identities.");
        // Preserve source order and any retained duplicates; the add command's
        // deduplication does not prove that all loaded histories are unique.
        var first = snapshot.First.Select(Canonical).ToArray();
        var second = snapshot.Second.Select(Canonical).ToArray();
        _first.Clear(); _first.AddRange(first); _second.Clear(); _second.AddRange(second);
        Known = snapshot.Known; ++Revision;
    }

    private FalloutFormKey Canonical(FalloutFormKey card)
    {
        var source = records.GetEffective(card);
        if (source.Signature != "CCRD") throw new InvalidDataException("Caravan collection item is not a card record.");
        return source.FormKey;
    }
    private void RequireKnown()
    {
        if (!Known) throw new NotSupportedException("Caravan collection history is unknown.");
    }
}
