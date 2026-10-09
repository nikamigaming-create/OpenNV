using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMenuSoundSelectionCall(string Owner, long Occurrence, int BranchOrdinal,
    FalloutFormKey Sound, long? IndexedVoice = null, int? IndexedIndex = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || Occurrence <= 0 || BranchOrdinal < 0 || Sound.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(Sound.OwnerPlugin) || IndexedVoice <= 0 || (IndexedVoice is null) != (IndexedIndex is null))
            throw new InvalidDataException("Menu selection has no actual source/caller occurrence.");
    }
}
internal enum FalloutMenuSoundSelectionPhase { Entered, Selected, Failed }
internal sealed record FalloutMenuSoundDraw(long Ordinal, uint Bound, uint Value);
internal sealed record FalloutMenuSoundSelectionAttempt(long Ordinal, FalloutMenuSoundSelectionCall Call,
    string Winner, string RecordSha256, string RawPath, long PathRevision, FalloutMenuSoundSelectionPhase Phase,
    long DrawsBefore, IReadOnlyList<FalloutMenuSoundDirectory> Directories, IReadOnlyList<FalloutMenuSoundDraw> Draws,
    string? SelectedPath = null, int? SelectedIndex = null, uint? DirectoryHash = null,
    int? HistorySlot = null, uint? ReplacedHash = null, sbyte? ReplacedIndex = null,
    string? FailureType = null, string? Error = null, bool RandomRequested = false);
internal sealed record FalloutMenuSoundSelectionSnapshot(string Schema, string SourceSha256,
    FalloutSourceRandomSnapshot Random, IReadOnlyList<uint> HistoryHashes, IReadOnlyList<sbyte> HistoryIndices,
    sbyte HistoryCursor, IReadOnlyList<FalloutMenuSoundSelectionAttempt> Attempts, string? Failure,
    FalloutArchiveFileManagerSnapshot? FileManager = null);
internal sealed record FalloutPreparedMenuSound(FalloutSoundRecord Descriptor, long SelectionOrdinal);

// Exact and directory requests share one selected-source lifetime. A committed
// draw/history mutation is never rolled back because a later decoder or native
// player fails. The native voice remains owned by the existing audio ledger.
internal sealed class FalloutMenuSoundSelection
{
    internal const string Schema = "opennv-source-menu-sound-selection/v2";
    internal const int MaximumRepeatDraws = 4096;
    private readonly object _sync = new();
    private readonly FalloutPluginStack _records;
    private readonly Func<string, string, FalloutMenuSoundDirectory> _directory;
    private readonly FalloutSourceRandom _random;
    private readonly Action<FalloutMenuSoundDirectory>? _requireSourceDirectory;
    private readonly Func<FalloutArchiveFileManagerSnapshot>? _captureFileManager;
    private readonly uint[] _hashes = new uint[10];
    private readonly sbyte[] _indices = new sbyte[10];
    private readonly List<FalloutMenuSoundSelectionAttempt> _attempts = [];
    private sbyte _cursor;
    private bool _entered, _retired;
    private string? _failure;
    internal FalloutMenuSoundSelectionSource Source { get; }
    internal FalloutPluginStack Records => _records;
    internal string? Failure => _failure;

    internal FalloutMenuSoundSelection(FalloutPluginStack records, FalloutMenuSoundSelectionSource source,
        Func<string, string, FalloutMenuSoundDirectory> directory, FalloutMenuSoundSelectionSnapshot? restore = null,
        Func<uint>? authoredTickProducer = null, Action<FalloutMenuSoundDirectory>? requireSourceDirectory = null,
        Func<FalloutArchiveFileManagerSnapshot>? captureFileManager = null)
    {
        ArgumentNullException.ThrowIfNull(records); source.Validate(); ArgumentNullException.ThrowIfNull(directory);
        if (records.OwnedSource is not null && (requireSourceDirectory is null || captureFileManager is null))
            throw new NotSupportedException("Owned menu directories require their real cold source/order validator.");
        _records = records; Source = source; _directory = directory; _requireSourceDirectory = requireSourceDirectory;
        _captureFileManager = captureFileManager;
        _random = authoredTickProducer is null ? new(source, restore?.Random) : new(source, authoredTickProducer, restore?.Random);
        if (restore is null) return;
        if ((_captureFileManager is null) != (restore.FileManager is null) ||
            _captureFileManager is not null && JsonSerializer.Serialize(_captureFileManager()) != JsonSerializer.Serialize(restore.FileManager))
            throw new InvalidDataException("Cold menu selector changed its actual file-manager lifetime/prefix.");
        Restore(restore);
    }

    internal object State
    {
        get
        {
            lock (_sync) return new
            {
                source = Source.Identity,
                attempts = _attempts.ToArray(),
                random = _random.Capture(),
                historyHashes = _hashes.ToArray(),
                historyIndices = _indices.ToArray(),
                historyCursor = _cursor,
                failure = _failure,
                fileManager = _captureFileManager?.Invoke(),
                alternateProviderAndMultipleRegistry = "unowned",
                otherSharedRandomConsumers = "unjoined"
            };
        }
    }

    internal FalloutPreparedMenuSound Prepare(FalloutSoundRecord descriptor, FalloutMenuSoundSelectionCall call)
    {
        ArgumentNullException.ThrowIfNull(descriptor); call.Validate();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (_entered || _failure is not null) throw new InvalidOperationException("Sound selector has a reentrant or retained failed source prefix.");
            if (descriptor.FormKey != call.Sound || _attempts.Any(prior => SameCaller(prior.Call, call)))
                throw new InvalidOperationException("Menu sound selection changed or replayed its actual caller occurrence.");
            var record = _records.GetEffective(call.Sound);
            if (record.Signature != "SOUN") throw new InvalidDataException("Menu selection requires an actual winning SOUN.");
            var path = _records.SoundPaths.Read(call.Sound);
            var row = new FalloutMenuSoundSelectionAttempt(checked(_attempts.Count + 1L), call,
                record.Plugin.Name, FalloutRestLocation.SourceIdentity(record), path.File, path.Revision,
                FalloutMenuSoundSelectionPhase.Entered, _random.Draws, [], []);
            _attempts.Add(row); _entered = true;
            try
            {
                _ = FalloutMenuSoundSelectionSource.PathBytes(path.File);
                var expectedDescriptor = FalloutBsaArchive.CanonicalPath("sound\\" + path.File);
                if (descriptor.LogicalPath != expectedDescriptor) throw new InvalidDataException("Menu sound descriptor differs from its current raw source path.");
                var expected = FalloutMenuSoundSelectionSource.LogicalPath(path.File);
                if (!IsDirectory(path.File))
                {
                    row = row with { Phase = FalloutMenuSoundSelectionPhase.Selected, SelectedPath = expected };
                    Set(row); return new(descriptor with { LogicalPath = expected }, row.Ordinal);
                }
                FalloutMenuSoundDirectory? selected = null;
                foreach (var extension in Source.Extensions)
                {
                    var directory = _directory(path.File, extension); RequireDirectory(path.File, extension, directory);
                    row = row with { Directories = row.Directories.Append(directory).ToArray() }; Set(row);
                    if (directory.Paths.Count == 0) continue;
                    selected = directory; break;
                }
                if (selected is null) throw new FileNotFoundException("Original menu directory selector returned no source variant.");
                var count = checked((uint)selected.Paths.Count);
                var index = Draw(ref row, uint.MaxValue) % count;
                if (count > 1 && Source.History == FalloutMenuSoundHistoryPolicy.SignedTenSlotRemainder)
                {
                    var hash = FalloutMenuSoundSelectionSource.DirectoryHash(path.File);
                    var match = Array.FindIndex(_hashes, prior => prior == hash);
                    while (match >= 0 && _indices[match] >= 0 && index == (uint)_indices[match])
                    {
                        if (row.Draws.Count >= MaximumRepeatDraws)
                            throw new NotSupportedException("Original repeat-avoidance loop has not returned within the bounded source draw prefix; no different variant is manufactured.");
                        index = Draw(ref row, count);
                    }
                    row = row with
                    {
                        SelectedIndex = checked((int)index),
                        DirectoryHash = hash,
                        HistorySlot = _cursor,
                        ReplacedHash = _hashes[_cursor],
                        ReplacedIndex = _indices[_cursor]
                    }; Set(row);
                    _hashes[_cursor] = hash; _indices[_cursor] = unchecked((sbyte)index);
                    _cursor = (sbyte)(_cursor % 10);
                }
                var selectedPath = selected.Paths[checked((int)index)];
                row = row with
                {
                    Phase = FalloutMenuSoundSelectionPhase.Selected,
                    SelectedIndex = checked((int)index),
                    SelectedPath = selectedPath
                }; Set(row);
                return new(descriptor with { LogicalPath = selectedPath }, row.Ordinal);
            }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            {
                _failure ??= error.GetType().Name + ": " + error.Message;
                Set(row with { Phase = FalloutMenuSoundSelectionPhase.Failed, FailureType = error.GetType().Name, Error = error.Message });
                throw;
            }
            finally { _entered = false; }
        }
    }

    private uint Draw(ref FalloutMenuSoundSelectionAttempt row, uint bound)
    {
        row = row with { RandomRequested = true }; Set(row);
        var value = _random.Next(bound);
        row = row with { Draws = row.Draws.Append(new FalloutMenuSoundDraw(_random.Draws, bound, value)).ToArray() };
        Set(row); return value;
    }

    internal void RequirePrepared(long selectionOrdinal, FalloutFormKey sound, string path,
        string winner, string recordSha256, long? indexedVoice = null)
    {
        lock (_sync)
        {
            var row = Attempt(selectionOrdinal);
            if (row.Phase != FalloutMenuSoundSelectionPhase.Selected || row.Call.Sound != sound || row.SelectedPath != path ||
                row.Winner != winner || row.RecordSha256 != recordSha256 || indexedVoice is not null && row.Call.IndexedVoice != indexedVoice)
                throw new InvalidDataException("Prepared menu voice differs from its actual immutable selection receipt.");
            RequireWinningSource(row);
        }
    }
    internal FalloutMenuSoundSelectionAttempt Attempt(long ordinal) => ordinal > 0 && ordinal <= _attempts.Count ?
        _attempts[checked((int)ordinal - 1)] : throw new InvalidDataException("Menu selection has no actual attempted ordinal.");
    internal void RequireOwner(FalloutPluginStack records, FalloutMenuSoundSelectionSource source)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (!ReferenceEquals(records, _records) || Source.Identity != source.Identity)
            throw new InvalidDataException("Shared menu selector cannot be rebound to another graph/runtime source.");
    }
    internal FalloutMenuSoundSelectionSnapshot Capture()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (_entered) throw new NotSupportedException("Menu sound selection capture is inside its actual producer call.");
            foreach (var sound in _attempts.Select(row => row.Call.Sound).Distinct())
                if (_records.SoundPaths.Revision(sound) != 0)
                    throw new NotSupportedException("Menu sound capture requires the actual mutable sound-path cold owner.");
            var fileManager = _captureFileManager?.Invoke(); RequireFileManagerPrefix(fileManager);
            return new(Schema, Source.Identity, _random.Capture(), _hashes.ToArray(), _indices.ToArray(), _cursor, _attempts.ToArray(), _failure, fileManager);
        }
    }
    internal void RequireSameSnapshot(FalloutMenuSoundSelectionSnapshot saved)
    {
        if (JsonSerializer.Serialize(Capture()) != JsonSerializer.Serialize(saved))
            throw new InvalidDataException("A living shared sound selector cannot be rewound by another current-state constructor.");
    }
    internal void Retire()
    {
        lock (_sync)
        {
            if (_entered) throw new InvalidOperationException("Shared sound selector retired inside an actual source call.");
            _retired = true;
        }
    }

    private void Restore(FalloutMenuSoundSelectionSnapshot saved)
    {
        if (saved.Schema != Schema || saved.SourceSha256 != Source.Identity || saved.Random is null ||
            saved.HistoryHashes is null || saved.HistoryIndices is null || saved.Attempts is null ||
            saved.HistoryHashes.Count != 10 || saved.HistoryIndices.Count != 10 || saved.HistoryCursor != 0 ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Cold menu sound selector lost its complete current source state.");
        var replay = new FalloutSourceRandom(Source, () => saved.Random.Seed?.TickCount ??
            throw new InvalidDataException("Cold random prefix has no genuine retained seed."));
        uint[] hashes = new uint[10]; sbyte[] indices = new sbyte[10];
        var randomRequested = false;
        var validatedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in saved.Attempts)
        {
            if (row is null || row.Call is null || row.Ordinal != _attempts.Count + 1L ||
                row.Phase == FalloutMenuSoundSelectionPhase.Entered || !Enum.IsDefined(row.Phase) ||
                row.DrawsBefore != replay.Draws || row.PathRevision != 0 || row.Directories is null || row.Draws is null ||
                row.Draws.Count > MaximumRepeatDraws || (row.FailureType is null) != (row.Error is null) ||
                (row.Phase == FalloutMenuSoundSelectionPhase.Failed) != (row.Error is not null) ||
                _attempts.Any(prior => prior.Error is not null || SameCaller(prior.Call, row.Call)))
                throw new InvalidDataException("Cold sound selector changed an entered/failed caller prefix.");
            row.Call.Validate(); RequireWinningSource(row);
            if (_records.SoundPaths.Read(row.Call.Sound) != (row.RawPath, 0L))
                throw new InvalidDataException("Cold sound selector lost its actual original path bytes.");
            for (var index = 0; index < row.Directories.Count; ++index)
            {
                if (index >= Source.Extensions.Count)
                    throw new InvalidDataException("Cold directory selection changed original extension fallback order.");
                var directory = row.Directories[index]; RequireDirectory(row.RawPath, Source.Extensions[index], directory);
                if (validatedDirectories.Add(directory.RawPath + "\0" + directory.Extension + "\0" + directory.OrderSha256 + "\0" + directory.ProducerSha256))
                    _requireSourceDirectory?.Invoke(directory);
                if (index != row.Directories.Count - 1 && directory.Paths.Count != 0)
                    throw new InvalidDataException("Cold directory selection continued after a real nonempty provider.");
            }
            var paths = row.Directories.LastOrDefault()?.Paths;
            uint? selectedIndex = null;
            uint? hash = null;
            var match = -1;
            if (row.RandomRequested)
            {
                randomRequested = true;
                if (!IsDirectory(row.RawPath) || paths is null || paths.Count == 0)
                    throw new InvalidDataException("Cold random getter has no actual nonempty source directory.");
                if (paths.Count > 1 && Source.History == FalloutMenuSoundHistoryPolicy.SignedTenSlotRemainder)
                {
                    hash = FalloutMenuSoundSelectionSource.DirectoryHash(row.RawPath);
                    match = Array.FindIndex(hashes, prior => prior == hash);
                }
                foreach (var draw in row.Draws)
                {
                    var expectedBound = selectedIndex is null ? uint.MaxValue : checked((uint)paths.Count);
                    if (selectedIndex is not null && (hash is null || match < 0 || indices[match] < 0 || selectedIndex != (uint)indices[match]))
                        throw new InvalidDataException("Cold selector invented a repeat-avoidance draw.");
                    if (draw is null || draw.Ordinal != replay.Draws + 1 || draw.Bound != expectedBound || replay.Next(draw.Bound) != draw.Value)
                        throw new InvalidDataException("Cold selector changed its actual committed random prefix.");
                    selectedIndex = draw.Value % checked((uint)paths.Count);
                }
            }
            else if (row.Draws.Count != 0)
                throw new InvalidDataException("Cold draw was not preceded by its actual random getter.");
            if (row.Phase == FalloutMenuSoundSelectionPhase.Selected && IsDirectory(row.RawPath))
            {
                if (paths is null || selectedIndex is not { } index || row.SelectedIndex != index || row.SelectedPath != paths[checked((int)index)] ||
                    hash is not null && match >= 0 && indices[match] >= 0 && index == (uint)indices[match])
                    throw new InvalidDataException("Cold directory selection removed its real selected node or required redraw.");
                if ((hash is not null) != (row.HistorySlot is not null))
                    throw new InvalidDataException("Cold selector removed the original completed history mutation.");
            }
            if (row.HistorySlot is not null)
            {
                if (hash is not { } actualHash || row.HistorySlot != 0 || row.DirectoryHash != actualHash ||
                    row.ReplacedHash != hashes[0] || row.ReplacedIndex != indices[0] ||
                    selectedIndex is not { } index || row.SelectedIndex != index)
                    throw new InvalidDataException("Cold sound history changed its actual mutation prefix.");
                hashes[0] = actualHash; indices[0] = unchecked((sbyte)index);
            }
            else if (row.DirectoryHash is not null || row.ReplacedHash is not null || row.ReplacedIndex is not null)
                throw new InvalidDataException("Cold sound history has a mutation without its source slot.");
            if (!IsDirectory(row.RawPath))
            {
                if (row.RandomRequested || row.Draws.Count != 0 || row.Directories.Count != 0 || row.SelectedIndex is not null || row.HistorySlot is not null)
                    throw new InvalidDataException("Cold exact file fabricated a directory/random consumer.");
                if (row.Phase == FalloutMenuSoundSelectionPhase.Selected && row.SelectedPath != FalloutMenuSoundSelectionSource.LogicalPath(row.RawPath))
                    throw new InvalidDataException("Cold exact file changed its actual prepared path.");
            }
            if (row.Phase == FalloutMenuSoundSelectionPhase.Failed &&
                (row.Ordinal != saved.Attempts.Count || row.SelectedPath is not null || row.SelectedIndex is not null || row.HistorySlot is not null))
                throw new InvalidDataException("Cold failed source selection fabricated a completed suffix or a later call.");
            _attempts.Add(row);
        }
        var replayState = replay.Capture();
        if (!hashes.SequenceEqual(saved.HistoryHashes) || !indices.SequenceEqual(saved.HistoryIndices) ||
            replay.Draws != saved.Random.Draws || !replayState.Words.SequenceEqual(saved.Random.Words) ||
            saved.Random.Constructed != randomRequested ||
            _attempts.LastOrDefault(row => row.Error is not null) is { } failed && saved.Failure != failed.FailureType + ": " + failed.Error ||
            saved.Attempts.Any(row => row.Error is not null) != (saved.Failure is not null))
            throw new InvalidDataException("Cold sound selector omitted its committed history/random/failure suffix.");
        RequireFileManagerPrefix(saved.FileManager);
        Array.Copy(hashes, _hashes, 10); Array.Copy(indices, _indices, 10); _cursor = saved.HistoryCursor; _failure = saved.Failure;
        // Validation computes numbers only. No seed import, native voice,
        // resource load or original source call is replayed on cold admission.
    }

    private void RequireFileManagerPrefix(FalloutArchiveFileManagerSnapshot? manager)
    {
        if (manager is null)
        {
            if (_captureFileManager is not null) throw new InvalidDataException("Sound selector lost its source file-manager prefix.");
            return;
        }
        var returned = _attempts.SelectMany(row => row.Directories).ToArray();
        var actual = manager.Attempts.Where(row => row.Phase == FalloutArchiveDirectoryPhase.Returned).ToArray();
        if (actual.Length != returned.Length)
            throw new InvalidDataException("Shared selector and file manager have different actual directory-call prefixes.");
        for (var index = 0; index < actual.Length; ++index)
            if (actual[index].RawPath != returned[index].RawPath || actual[index].Extension != returned[index].Extension ||
                actual[index].OrderSha256 != returned[index].OrderSha256 || actual[index].ProducerSha256 != returned[index].ProducerSha256)
                throw new InvalidDataException("Selector directory receipt differs from its genuine file-manager call.");
        var failure = manager.Attempts.LastOrDefault(row => row.Phase == FalloutArchiveDirectoryPhase.Failed);
        if (failure is null) return;
        var selected = _attempts.LastOrDefault();
        if (selected is null || selected.Phase != FalloutMenuSoundSelectionPhase.Failed || selected.RandomRequested ||
            selected.RawPath != failure.RawPath || selected.Directories.Count >= Source.Extensions.Count ||
            Source.Extensions[selected.Directories.Count] != failure.Extension || selected.FailureType != failure.FailureType ||
            selected.Error != failure.Error)
            throw new InvalidDataException("File-manager failure was erased or attached to another selector caller/draw prefix.");
    }

    private void RequireWinningSource(FalloutMenuSoundSelectionAttempt row)
    {
        var record = _records.GetEffective(row.Call.Sound);
        if (record.Signature != "SOUN" || record.Plugin.Name != row.Winner || FalloutRestLocation.SourceIdentity(record) != row.RecordSha256)
            throw new InvalidDataException("Menu selection changed its original winning sound bytes.");
    }
    private static bool SameCaller(FalloutMenuSoundSelectionCall first, FalloutMenuSoundSelectionCall second) =>
        first.Owner == second.Owner && first.Occurrence == second.Occurrence && first.BranchOrdinal == second.BranchOrdinal;
    private void Set(FalloutMenuSoundSelectionAttempt row) => _attempts[checked((int)row.Ordinal - 1)] = row;
    private static bool IsDirectory(string raw) => raw.EndsWith('\\') || raw.EndsWith('/');
    internal static string OrderDigest(IReadOnlyList<string> paths) => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(paths));
    private static void RequireDirectory(string raw, string extension, FalloutMenuSoundDirectory row)
    {
        if (row is null || row.RawPath != raw || row.Extension != extension || row.Paths is null ||
            row.OrderSha256 != OrderDigest(row.Paths) || !FalloutAdvancementRuntimeReceipt.Digest(row.ProducerSha256))
            throw new InvalidDataException("Menu selector has no actual ordered directory provider receipt.");
        var prefix = FalloutMenuSoundSelectionSource.LogicalPath(raw).TrimEnd('\\') + "\\";
        foreach (var path in row.Paths)
            if (path is null || path != FalloutBsaArchive.CanonicalPath(path) || !path.StartsWith(prefix, StringComparison.Ordinal) ||
                path[prefix.Length..].Contains('\\'))
                throw new InvalidDataException("Menu directory changed its exact direct-child source names.");
        if (row.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != row.Paths.Count)
            throw new InvalidDataException("Loose menu directory has ambiguous repeated source names.");
    }
}
