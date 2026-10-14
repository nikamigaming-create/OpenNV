using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Capture/cold validation only. Live getters still use the original shared
// byte owner and do not build history indexes on a frame or input path.
internal sealed class FalloutPlayerInhibitionReadHistory
{
    private const int CompleteConstructorRevision = 6;
    private readonly string _source, _stack;
    private readonly long _sequence;
    private readonly HashSet<long> _inFlightSetterRevisions = [];
    private readonly Dictionary<(Guid Process, Guid Factory), PublicationInterval> _publications = [];
    private readonly List<FalloutPlayerInhibitionEntry>[] _writers =
        Enum.GetValues<FalloutPlayerInhibitionLane>().Select(_ => new List<FalloutPlayerInhibitionEntry>()).ToArray();
    private sealed record PublicationInterval(Guid Constructor, long Start, long End);

    internal FalloutPlayerInhibitionReadHistory(FalloutPlayerInhibitionSnapshot saved,
        FalloutPlayerFollowingContinuationSource source, string stack)
    {
        FalloutPlayerInhibitionState.Validate(saved, source, stack);
        _source = source.Identity; _stack = stack; _sequence = saved.Sequence;
        var publications = saved.Publications!;
        var order = publications.Select((row, index) => (row.Process, row.Factory, index))
            .ToDictionary(row => (row.Process, row.Factory), row => row.index);
        var first = new long[publications.Count]; var last = new long[publications.Count];
        var starts = new long[publications.Count]; var priorOrder = 0;
        foreach (var entry in saved.History)
        {
            var ordinal = order[(entry.Process, entry.Factory)];
            if (ordinal < priorOrder)
                throw new InvalidDataException("Inhibition writer history returned to an already retired process publication.");
            priorOrder = ordinal;
            if (first[ordinal] == 0) first[ordinal] = entry.Changed;
            last[ordinal] = entry.Changed;
            if (entry.Operation != FalloutPlayerInhibitionOperation.SelectedVoidCallback)
                _writers[(int)entry.Lane].Add(entry);
            if (entry.Operation == FalloutPlayerInhibitionOperation.ZeroSetter)
                _inFlightSetterRevisions.Add(entry.Changed);
        }
        long preceding = 0;
        for (var index = 0; index < publications.Count; index++)
        { starts[index] = preceding; preceding = Math.Max(preceding, last[index]); }
        var end = _sequence;
        for (var index = publications.Count - 1; index >= 0; index--)
        {
            var row = publications[index];
            _publications.Add((row.Process, row.Factory), new(row.Constructor, starts[index], end));
            if (first[index] != 0) end = first[index] - 1;
        }
    }

    internal void Require(FalloutPlayerInhibitionRead read, FalloutPlayerInhibitionConsumer consumer,
        FalloutPlayerSourceCaller caller, Guid process, FalloutPlayerInhibitionLane lane, Guid? constructor = null)
    {
        if (read is null || caller is null || caller.Main == Guid.Empty || caller.Ordinal < 1 || caller.Invocation == Guid.Empty ||
            !Enum.IsDefined(consumer) || !Enum.IsDefined(lane) || process == Guid.Empty || read.Source != _source || read.Stack != _stack ||
            read.Process != process || read.Consumer != consumer || read.Caller != caller || read.Lane != lane ||
            read.Revision < CompleteConstructorRevision || read.Revision > _sequence || _inFlightSetterRevisions.Contains(read.Revision) ||
            !_publications.TryGetValue((read.Process, read.Factory), out var publication) ||
            constructor is { } expected && publication.Constructor != expected ||
            read.Revision < publication.Start || read.Revision > publication.End)
            throw new InvalidDataException("Inhibition read changed its actual source, caller, consumer, lane or historical publication interval.");
        var entries = _writers[(int)lane]; var low = 0; var high = entries.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (entries[middle].Changed <= read.Revision) low = middle + 1; else high = middle;
        }
        if (low == 0 || entries[low - 1].Value != read.Value || entries[low - 1].Operation == FalloutPlayerInhibitionOperation.UnownedWriter)
            throw new InvalidDataException("Inhibition read differs from its actual available historical byte writer.");
    }
}
