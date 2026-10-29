using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutFormListChanges(FalloutFormKey Form, string Winner, string Sha256,
    IReadOnlyList<FalloutFormKey> Added);
internal sealed record FalloutFormListsSnapshot(IReadOnlyList<FalloutFormListChanges> Lists);

// AddFormToFormList owns a persistent prefix before the immutable source entries.
// An existing member anywhere in the list is neither moved nor duplicated.
// Source definitions remain read-only. Contract: owned native duplicate search,
// prepend and saved-prefix behavior; xNVSE GameForms.h describes that prefix.
internal sealed class FalloutFormListState(FalloutPluginStack records) : IFalloutFormLists
{
    private sealed class ListState(FalloutFormList source)
    {
        internal readonly FalloutFormList Source = source;
        internal readonly List<FalloutFormKey> Added = [];
        internal IReadOnlyList<FalloutFormKey?> Current = source.Members;
        internal void Publish() => Current = Added.Select(form => (FalloutFormKey?)form).Concat(Source.Members).ToArray();
    }

    private readonly Dictionary<FalloutFormKey, ListState> _lists = [];
    internal long Revision { get; private set; }
    internal event Action<FalloutFormKey>? Changed;

    private ListState Get(FalloutFormKey list)
    {
        if (!_lists.TryGetValue(list, out var state))
        {
            var source = FalloutFormList.Read(records, list);
            _lists.Add(list, state = new(source));
        }
        return state;
    }

    public IReadOnlyList<FalloutFormKey?> Entries(FalloutFormKey list) => Get(list).Current;

    internal void Add(FalloutFormKey list, FalloutFormKey form)
    {
        RequireMember(form);
        var state = Get(list);
        if (state.Source.Members.Any(member => member is null))
            throw new NotSupportedException("Mutable FLST with serialized null members requires its loader/sentinel contract.");
        if (state.Current.Contains(form)) return;
        state.Added.Insert(0, form);
        Publish(state);
    }

    private void Publish(ListState state)
    {
        state.Publish();
        Revision = checked(Revision + 1);
        Changed?.Invoke(state.Source.Form);
    }

    internal FalloutFormListsSnapshot Capture() => new(_lists.Values.Where(state => state.Added.Count != 0)
        .OrderBy(state => state.Source.Form.OwnerPlugin, StringComparer.OrdinalIgnoreCase).ThenBy(state => state.Source.Form.ObjectId)
        .Select(state => new FalloutFormListChanges(state.Source.Form, state.Source.Winner, state.Source.Sha256,
            state.Added.ToArray())).ToArray());

    internal void Restore(FalloutFormListsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var prepared = new Dictionary<FalloutFormKey, ListState>();
        foreach (var saved in snapshot.Lists)
        {
            var source = FalloutFormList.Read(records, saved.Form);
            if (source.Members.Any(member => member is null))
                throw new NotSupportedException("Saved mutable FLST has unqualified serialized null members.");
            if (!string.Equals(source.Winner, saved.Winner, StringComparison.OrdinalIgnoreCase) || source.Sha256 != saved.Sha256)
                throw new InvalidDataException("Saved form-list source has changed.");
            if (saved.Added.Count != saved.Added.Distinct().Count() || saved.Added.Any(form => source.Members.Contains(form)))
                throw new InvalidDataException("Saved form-list prefix duplicates a source or scripted member.");
            foreach (var form in saved.Added) RequireMember(form);
            var state = new ListState(source);
            state.Added.AddRange(saved.Added);
            state.Publish();
            if (!prepared.TryAdd(source.Form, state)) throw new InvalidDataException("Saved form-list source is duplicated.");
        }
        var changed = _lists.Keys.Union(prepared.Keys).ToArray();
        _lists.Clear();
        foreach (var pair in prepared) _lists.Add(pair.Key, pair.Value);
        if (changed.Length == 0) return;
        Revision = checked(Revision + 1);
        foreach (var list in changed) Changed?.Invoke(list);
    }

    private void RequireMember(FalloutFormKey form)
    {
        FalloutFormList.RequireMember(records, form);
    }
}
