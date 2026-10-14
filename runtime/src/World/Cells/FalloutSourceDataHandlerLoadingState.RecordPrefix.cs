using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutDataHandlerIndexedRecordsBinding(Guid RuntimeProcess, Guid Factory,
    Guid ReaderFactory, string SourceHash, string StackIdentity, IReadOnlyList<string> InputOrder,
    int ReaderThread, long BoundSequence);
internal sealed record FalloutDataHandlerRecordPreservation(Guid Admission,
    FalloutDataHandlerLoadingPreservationSource Source, Guid RuntimeProcess, Guid Factory, Guid Prefix,
    Guid ReaderFactory, string RecordsSource, long Changed, int ThreadId, byte Before, byte After,
    bool AvailableBefore, bool AvailableAfter, string? BoundaryAfter)
{
    internal bool OriginalLoaderExecuted => false;
    internal bool WholeOriginalLoaderReturned => false;
}

internal sealed partial class FalloutSourceDataHandlerLoadingState
{
    private readonly List<FalloutDataHandlerRecordPreservation> _recordPreservations = [];
    private readonly List<FalloutDataHandlerIndexedRecordsBinding> _recordBindings = [];

    internal FalloutDataHandlerRecordPreservation PreserveIndexedRecordPrefix(
        FalloutDataHandlerLoadingPreservationSource footprint, FalloutPluginStack records,
        FalloutPluginStackLoadReceipt input, Action requireActualInput)
    {
        footprint.Validate(Source); RequireIndexedInput(records, input);
        BindCurrentIndexedRecords(records, input, requireActualInput);
        var prefix = RetainUnqualifiedRecordLoadPrefix(input.Factory, input.SourceHash, () =>
        { RequireIndexedInput(records, input); requireActualInput(); });
        if (_busy) { ++_fault; throw new InvalidOperationException("DataHandler records preservation reentered its field owner."); }
        _busy = true; var fault = _fault;
        try
        {
            RequireIndexedInput(records, input); requireActualInput();
            if (_fault != fault || prefix.Changed != _sequence || prefix.Factory != Factory ||
                prefix.RuntimeProcess != RuntimeProcess || _available || _boundary != RecordLoadBoundary || _value != prefix.BeforeValue)
                throw new InvalidOperationException("DataHandler records preservation crossed another writer or swallowed a reentry failure.");
            var result = new FalloutDataHandlerRecordPreservation(Guid.NewGuid(), footprint, RuntimeProcess, Factory,
                prefix.Receipt, input.Factory, input.SourceHash, checked(++_sequence), Environment.CurrentManagedThreadId,
                _value, _value, false, prefix.AvailableBefore, prefix.BoundaryBefore);
            _ids.Add(result.Admission); _recordPreservations.Add(result);
            _available = prefix.AvailableBefore; _boundary = prefix.BoundaryBefore;
            return result;
        }
        finally { _busy = false; }
    }

    // A cold current reader is a fresh association with retained field history.
    // It does not repeat an original prefix or emit a field store/admission.
    internal FalloutDataHandlerIndexedRecordsBinding BindCurrentIndexedRecords(FalloutPluginStack records,
        FalloutPluginStackLoadReceipt input, Action requireActualInput)
    {
        ArgumentNullException.ThrowIfNull(requireActualInput); RequireIndexedInput(records, input);
        if (_busy) { ++_fault; throw new InvalidOperationException("DataHandler records binding reentered its field owner."); }
        if (_recordBindings.Any(value => value.Factory == Factory))
            throw new InvalidOperationException("DataHandler records binding replayed its current lifetime.");
        _busy = true; var fault = _fault;
        try
        {
            requireActualInput(); RequireIndexedInput(records, input);
            if (_fault != fault) throw new InvalidOperationException("DataHandler records guard swallowed a real reentry failure.");
            var binding = new FalloutDataHandlerIndexedRecordsBinding(RuntimeProcess, Factory, input.Factory,
                input.SourceHash, input.StackIdentity, Array.AsReadOnly(input.InputOrder.ToArray()), input.ThreadId, _sequence);
            _recordBindings.Add(binding); return binding;
        }
        finally { _busy = false; }
    }

    private void RequireIndexedInput(FalloutPluginStack records, FalloutPluginStackLoadReceipt input)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(input);
        records.RequireLiveLoadReceipt(input);
        if (!input.Returned || input.Factory == Guid.Empty || input.SourceHash is not { Length: 64 } ||
            !input.SourceHash.All(Uri.IsHexDigit) || input.StackIdentity != Stack || input.ThreadId < 1 ||
            input.InputOrder is not { Count: > 0 } ||
            !input.InputOrder.SequenceEqual(records.Plugins.Select(plugin => plugin.Plugin.Name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("DataHandler loading borrowed another actual returned records source/order/lifetime.");
    }

    private static void ValidateRecordBindings(FalloutDataHandlerLoadingSnapshot saved,
        IReadOnlyDictionary<Guid, FalloutDataHandlerLoadingPublication> publications,
        IReadOnlyDictionary<Guid, long> publicationEnds)
    {
        var factories = new HashSet<Guid>();
        foreach (var binding in saved.RecordBindings ?? [])
            if (binding is null || !publications.TryGetValue(binding.Factory, out var publication) ||
                publication.RuntimeProcess != binding.RuntimeProcess || !factories.Add(binding.Factory) ||
                binding.ReaderFactory == Guid.Empty || binding.StackIdentity != saved.Stack ||
                binding.SourceHash is not { Length: 64 } || !binding.SourceHash.All(Uri.IsHexDigit) ||
                binding.ReaderThread < 1 || binding.BoundSequence < 0 || binding.BoundSequence > saved.Sequence ||
                binding.BoundSequence < publication.PublishedSequence || binding.BoundSequence > publicationEnds[binding.Factory] ||
                binding.InputOrder is not { Count: > 0 } || binding.InputOrder.Any(string.IsNullOrWhiteSpace) ||
                binding.InputOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count() != binding.InputOrder.Count)
                throw new InvalidDataException("DataHandler loading replaced its actual records binding/publication.");
    }

    private static void ValidateRecordPreservation(FalloutDataHandlerRecordPreservation preservation,
        FalloutDataHandlerLoadingSnapshot saved, IReadOnlyDictionary<Guid, FalloutDataHandlerLoadingPublication> publications,
        IReadOnlyDictionary<Guid, long> publicationEnds,
        IReadOnlyDictionary<Guid, FalloutDataHandlerRecordLoadPrefix> prefixes,
        IReadOnlyDictionary<Guid, FalloutDataHandlerIndexedRecordsBinding> bindings, long sequence, byte value,
        bool available, string? boundary)
    {
        preservation.Source.Validate(saved.Source);
        bindings.TryGetValue(preservation.Factory, out var binding);
        if (preservation.Admission == Guid.Empty || !publications.TryGetValue(preservation.Factory, out var publication) ||
            publication.RuntimeProcess != preservation.RuntimeProcess || preservation.ThreadId < 1 ||
            preservation.Changed <= publication.PublishedSequence || preservation.Changed > publicationEnds[preservation.Factory] ||
            !prefixes.TryGetValue(preservation.Prefix, out var prefix) || prefix.Factory != preservation.Factory ||
            prefix.RuntimeProcess != preservation.RuntimeProcess || prefix.Changed != sequence || preservation.Changed != sequence + 1 ||
            prefix.Receipt != preservation.ReaderFactory || prefix.RecordsSource != preservation.RecordsSource ||
            binding is null || binding.ReaderFactory != preservation.ReaderFactory || binding.SourceHash != preservation.RecordsSource ||
            binding.BoundSequence >= prefix.Changed || preservation.Before != value || preservation.After != value ||
            preservation.AvailableBefore != available || available || boundary != RecordLoadBoundary ||
            preservation.AvailableAfter != prefix.AvailableBefore || preservation.BoundaryAfter != prefix.BoundaryBefore)
            throw new InvalidDataException("DataHandler preservation fabricated a zero store, erased another writer or replaced its actual indexed input.");
    }
}
