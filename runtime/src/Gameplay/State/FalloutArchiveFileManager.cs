using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutArchiveModeSetter { UseArchives, InvalidateOlderFiles, InvalidationFile, ArchiveRegistration }
internal enum FalloutArchiveDirectoryPhase { Entered, Returned, Failed }
internal sealed record FalloutArchiveInvalidationInput(string ScopeSha256, bool Present, long Bytes, string? Sha256, string? UnownedReason = null)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(ScopeSha256) || Bytes < 0 ||
            Present != (Sha256 is not null) || !Present && Bytes != 0 ||
            Sha256 is not null && !FalloutAdvancementRuntimeReceipt.Digest(Sha256) ||
            UnownedReason is not null && (string.IsNullOrWhiteSpace(UnownedReason) || Present || Bytes != 0 || Sha256 is not null))
            throw new InvalidDataException("Archive invalidation observation has no actual file/source extent.");
    }
}
internal sealed record FalloutArchiveModeFailure(FalloutArchiveModeSetter Setter, string Caller,
    string Requested, string FailureType, string Error);
internal sealed record FalloutArchiveDirectoryAttempt(long Ordinal, string RawPath, string Extension,
    FalloutArchiveDirectoryPhase Phase, string? OrderSha256 = null, string? ProducerSha256 = null,
    string? FailureType = null, string? Error = null);
internal sealed record FalloutArchiveFileManagerSnapshot(string Schema, string SourceSha256,
    FalloutArchiveStartupModes Modes, FalloutArchiveInvalidationInput? Invalidation,
    IReadOnlyList<FalloutBsaDirectorySource> Archives, IReadOnlyList<FalloutArchiveDirectoryAttempt> Attempts,
    FalloutArchiveModeFailure? SetterFailure, string? Failure);
internal sealed record FalloutLooseSoundDirectory(string Root, IReadOnlyList<string> Paths, string ProducerSha256);

// One selected source lifetime; it borrows archive readers from that lifetime.
// Actual directory calls keep their returned/refused prefix independently of
// later RNG, audio decoding, native allocation and native voice completion.
internal sealed class FalloutArchiveFileManager
{
    internal const string Schema = "opennv-source-archive-file-manager/v1";
    private readonly object _sync = new();
    private readonly FalloutArchiveFileManagerSource _source;
    private readonly FalloutArchiveStartupModes _modes;
    private readonly FalloutArchiveInvalidationInput? _invalidation;
    private readonly IReadOnlyList<FalloutBsaArchive> _archives;
    private readonly IReadOnlyList<FalloutBsaDirectorySource> _archiveSources;
    private readonly Func<string, string, FalloutLooseSoundDirectory> _loose;
    private readonly Func<string, string?> _resolve;
    private readonly List<FalloutArchiveDirectoryAttempt> _attempts = [];
    private FalloutArchiveModeFailure? _setterFailure;
    private string? _failure;
    private bool _entered, _retired;

    internal FalloutArchiveFileManager(FalloutArchiveFileManagerSource source, FalloutArchiveStartupModes modes,
        IReadOnlyList<FalloutBsaArchive> archives, FalloutArchiveInvalidationInput? invalidation,
        Func<string, string, FalloutLooseSoundDirectory> loose, Func<string, string?> resolve,
        FalloutArchiveFileManagerSnapshot? restore = null)
    {
        source.Validate(); modes.Validate(); invalidation?.Validate();
        ArgumentNullException.ThrowIfNull(archives); ArgumentNullException.ThrowIfNull(loose); ArgumentNullException.ThrowIfNull(resolve);
        if (modes.UseArchives == 0 && (archives.Count != 0 || invalidation is not null) ||
            modes.UseArchives == 1 && invalidation is null)
            throw new InvalidDataException("Actual archive startup did not preserve its independent no-registry branch/invalidation input.");
        _source = source; _modes = modes; _archives = Array.AsReadOnly(archives.ToArray()); _invalidation = invalidation;
        _archiveSources = Array.AsReadOnly(archives.Select(archive => archive.DirectorySource).ToArray());
        if (_archiveSources.Select(row => row.Archive).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archives.Count)
            throw new InvalidDataException("Archive input inventory repeats a physical source reader.");
        _loose = loose; _resolve = resolve;
        if (restore is not null) Restore(restore);
    }

    internal FalloutMenuSoundDirectory ReadDirectory(string raw, string extension)
    {
        lock (_sync)
        {
            RequireAvailable();
            var row = new FalloutArchiveDirectoryAttempt(checked(_attempts.Count + 1L), raw, extension, FalloutArchiveDirectoryPhase.Entered);
            _attempts.Add(row); _entered = true;
            try
            {
                var current = ReadInputs(raw, extension);
                _attempts[^1] = row with { Phase = FalloutArchiveDirectoryPhase.Returned,
                    OrderSha256 = current.OrderSha256, ProducerSha256 = current.ProducerSha256 };
                return current;
            }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            {
                _failure = error.Message;
                _attempts[^1] = row with { Phase = FalloutArchiveDirectoryPhase.Failed,
                    FailureType = error.GetType().Name, Error = error.Message };
                throw;
            }
            finally { _entered = false; }
        }
    }

    internal FalloutMenuSoundDirectory ObserveSourceDirectory(string raw, string extension)
    {
        lock (_sync)
        {
            RequireAvailable();
            return ReadInputs(raw, extension);
        }
    }

    internal void RequireColdDirectory(FalloutMenuSoundDirectory saved)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (_entered) throw new InvalidOperationException("Cold source check entered a living file-manager call.");
            // Pure source input verification does not enter a directory caller,
            // change its prefix, draw RNG, decode media or start a voice.
            var current = ReadInputs(saved.RawPath, saved.Extension);
            if (current.OrderSha256 != saved.OrderSha256 || current.ProducerSha256 != saved.ProducerSha256 ||
                !current.Paths.SequenceEqual(saved.Paths, StringComparer.Ordinal))
                throw new InvalidDataException("Cold source directory changed its mode, visibility, table order or resource winner.");
        }
    }

    private FalloutMenuSoundDirectory ReadInputs(string raw, string extension)
    {
        _ = FalloutMenuSoundSelectionSource.PathBytes(raw);
        if (!raw.EndsWith('\\') && !raw.EndsWith('/')) throw new InvalidDataException("Directory call lost its source separator.");
        if (_modes.InvalidateOlderFiles == 0)
            throw new NotSupportedException("Audio selected the independent direct archive provider; wildcard directory lookup cannot replace it.");
        var folder = FalloutMenuSoundSelectionSource.LogicalPath(raw).TrimEnd('\\');
        _ = FalloutArchiveNameHash.Wildcard(extension);
        IReadOnlyList<string> paths; string producer;
        if (_modes.UseArchives == 0)
        {
            var loose = _loose(raw, extension);
            if (loose.Paths is null || !FalloutAdvancementRuntimeReceipt.Digest(loose.ProducerSha256))
                throw new InvalidDataException("Native loose enumeration returned no genuine ordered source owner.");
            paths = loose.Paths;
            foreach (var path in paths)
            {
                var expected = Path.GetFullPath(Path.Combine(loose.Root, path.Replace('\\', Path.DirectorySeparatorChar)));
                if (!SameSource(_resolve(path), expected))
                    throw new NotSupportedException("Loose directory and the shared selected resource have different winning provenance.");
            }
            producer = FalloutAdvancementRuntimeReceipt.Hash(_source.Identity + "\0" + _modes.Identity +
                "\0loose-only\0" + loose.ProducerSha256);
        }
        else
        {
            if (_invalidation?.UnownedReason is { } unowned)
                throw new NotSupportedException("Archive visibility has no genuine startup search-root/invalidation observation: " + unowned);
            if (_invalidation is { Present: true, Bytes: > 0 })
                throw new NotSupportedException("Nonempty original invalidation lists require their actual hash/parser/visibility mutation owner.");
            var configured = FalloutArchiveFileManagerSource.ConfiguredArchiveNames(_modes);
            foreach (var name in configured)
                if (_archiveSources.Count(input => Path.GetFileName(input.Archive).Equals(name, StringComparison.OrdinalIgnoreCase)) != 1)
                    throw new NotSupportedException("Actual configured startup archive is missing or ambiguous in the retained readers; directory absence cannot replace its source registration consumer.");
            var candidates = _archives.Where(archive => archive.HasSourceSoundFolder(folder))
                .Select(archive => (Archive: archive, Rows: archive.SourceSoundDirectory(folder, extension)))
                .Where(candidate => candidate.Rows.Count != 0).ToArray();
            if (candidates.Length > 1)
                throw new NotSupportedException("Multiple contributing archives require actual registration/category traversal and shadow-retirement order.");
            if (candidates.Length == 0) paths = [];
            else
            {
                var selected = candidates[0]; var archive = selected.Archive; var input = archive.DirectorySource;
                if (!configured.Contains(Path.GetFileName(input.Archive), StringComparer.OrdinalIgnoreCase))
                    throw new NotSupportedException("Contributing archive has no actual configured startup registration; plugin filename matching is insufficient.");
                // The original callback prepends each visible matching file.
                // A sole contributor makes registry order immaterial; no sort
                // or arbitrary concatenated virtual list is admitted.
                paths = Array.AsReadOnly(selected.Rows.Reverse().Select(row => row.LogicalPath).ToArray());
                foreach (var path in paths)
                    if (!SameSource(_resolve(path), input.Archive + "::" + path))
                        throw new NotSupportedException("Archive directory and the shared selected resource have different winning provenance.");
            }
            producer = FalloutAdvancementRuntimeReceipt.Hash(_source.Identity + "\0" + _modes.Identity +
                "\0archive-only\0" + JsonSerializer.Serialize(_invalidation) + "\0" + JsonSerializer.Serialize(_archiveSources));
        }
        var prefix = folder + "\\";
        if (paths.Any(path => path is null || path != FalloutBsaArchive.CanonicalPath(path) ||
            !path.StartsWith(prefix, StringComparison.Ordinal) || path[prefix.Length..].Contains('\\')) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
            throw new InvalidDataException("Actual source directory lost its distinct canonical direct-child names.");
        return new(raw, extension, paths, FalloutMenuSoundSelection.OrderDigest(paths), producer);
    }

    internal FalloutArchiveFileManagerSnapshot Capture()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (_entered) throw new NotSupportedException("File-manager capture intersects an entered original source directory caller.");
            return new(Schema, _source.Identity, _modes, _invalidation, _archiveSources, _attempts.ToArray(), _setterFailure, _failure);
        }
    }

    internal void RefuseRuntimeSetter(FalloutArchiveModeSetter setter, string requested, string caller)
    {
        lock (_sync)
        {
            RequireAvailable();
            if (!Enum.IsDefined(setter) || string.IsNullOrWhiteSpace(caller) || requested is null)
                throw new InvalidDataException("Runtime file-manager setter has no actual source caller/value.");
            var error = new NotSupportedException("Actual runtime file-manager mode/registry setter and its visibility effects are unowned.");
            _setterFailure = new(setter, caller, requested, error.GetType().FullName!, error.Message); _failure = error.Message;
            throw error;
        }
    }

    internal void Retire()
    {
        lock (_sync)
        {
            if (_entered) throw new InvalidOperationException("Source file manager retired inside its actual directory call.");
            _retired = true;
            // Archive readers remain owned by RuntimeLiveContentSource. This
            // owner never closes a borrowed reader or refunds a selection.
        }
    }

    private void Restore(FalloutArchiveFileManagerSnapshot saved)
    {
        if (saved.Schema != Schema || saved.SourceSha256 != _source.Identity || saved.Modes is null || saved.Archives is null ||
            saved.Attempts is null || JsonSerializer.Serialize(saved.Modes) != JsonSerializer.Serialize(_modes) ||
            JsonSerializer.Serialize(saved.Invalidation) != JsonSerializer.Serialize(_invalidation) ||
            JsonSerializer.Serialize(saved.Archives) != JsonSerializer.Serialize(_archiveSources))
            throw new InvalidDataException("Cold file manager lost its actual startup modes/archive/input identities.");
        foreach (var row in saved.Attempts)
        {
            if (row is null || row.Ordinal != _attempts.Count + 1L || !Enum.IsDefined(row.Phase) ||
                row.Phase == FalloutArchiveDirectoryPhase.Entered || _attempts.Any(prior => prior.Phase == FalloutArchiveDirectoryPhase.Failed) ||
                row.Phase == FalloutArchiveDirectoryPhase.Returned &&
                    (!FalloutAdvancementRuntimeReceipt.Digest(row.OrderSha256) || !FalloutAdvancementRuntimeReceipt.Digest(row.ProducerSha256) ||
                    row.FailureType is not null || row.Error is not null) ||
                row.Phase == FalloutArchiveDirectoryPhase.Failed &&
                    (row.OrderSha256 is not null || row.ProducerSha256 is not null ||
                    string.IsNullOrWhiteSpace(row.FailureType) || string.IsNullOrWhiteSpace(row.Error)))
                throw new InvalidDataException("Cold file-manager prefix has invented/incomplete/replayed directory calls.");
            _ = FalloutMenuSoundSelectionSource.PathBytes(row.RawPath); _ = FalloutArchiveNameHash.Wildcard(row.Extension);
            if (row.Phase == FalloutArchiveDirectoryPhase.Returned)
            {
                var current = ReadInputs(row.RawPath, row.Extension);
                if (current.OrderSha256 != row.OrderSha256 || current.ProducerSha256 != row.ProducerSha256)
                    throw new InvalidDataException("Cold file-manager returned prefix changed its genuine source inputs.");
            }
            else if (string.IsNullOrWhiteSpace(row.Error) || string.IsNullOrWhiteSpace(row.FailureType))
                throw new InvalidDataException("Cold file-manager failure lost its actual retained refusal.");
            _attempts.Add(row);
        }
        if (saved.SetterFailure is { } setter && (!Enum.IsDefined(setter.Setter) || string.IsNullOrWhiteSpace(setter.Caller) ||
            setter.Requested is null || string.IsNullOrWhiteSpace(setter.Error) || string.IsNullOrWhiteSpace(setter.FailureType)))
            throw new InvalidDataException("Cold file-manager setter lost its retained unowned operation.");
        var failure = saved.SetterFailure?.Error ?? _attempts.LastOrDefault(row => row.Phase == FalloutArchiveDirectoryPhase.Failed)?.Error;
        if (saved.Failure != failure || saved.SetterFailure is not null && _attempts.Any(row => row.Phase == FalloutArchiveDirectoryPhase.Failed))
            throw new InvalidDataException("Cold file manager erased or invented a failed committed prefix.");
        _setterFailure = saved.SetterFailure; _failure = saved.Failure;
    }

    private void RequireAvailable()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_entered || _failure is not null) throw new InvalidOperationException("File manager has an entered/reentrant or retained failed source prefix.");
    }
    private static bool SameSource(string? actual, string expected) => actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
