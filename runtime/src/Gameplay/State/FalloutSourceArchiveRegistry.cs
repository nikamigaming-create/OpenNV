using System.Runtime.ExceptionServices;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutArchiveRegistrationPhase { Registered, Failed }
internal sealed record FalloutArchiveRegistration(long Ordinal, string RequestedName,
    FalloutArchiveRegistrationPhase Phase, int? Category = null, int? InsertionIndex = null,
    FalloutBsaDirectorySource? Source = null, string? FailureType = null, string? Error = null);
internal sealed record FalloutSourceArchiveRegistrySnapshot(string Schema, string SourceSha256,
    FalloutArchiveStartupInput Input, IReadOnlyList<FalloutArchiveRegistration> Registrations,
    IReadOnlyList<long> RegistryOrdinals, string? Failure);
internal sealed record FalloutArchiveSoundWinner(long RegistrationOrdinal, FalloutBsaDirectorySource Archive,
    FalloutBsaSourceMember Member);

internal sealed class FalloutSourceArchiveRegistry
{
    internal const string Schema = "opennv-source-sound-archive-registry/v1";
    private readonly FalloutArchiveRegistrySource _source;
    private readonly FalloutArchiveStartupInput _input;
    private readonly List<FalloutArchiveRegistration> _registrations = [];
    private readonly List<(FalloutArchiveRegistration Row, FalloutBsaArchive Reader)> _registry = [];
    private Exception? _failure;
    private bool _retired;

    internal FalloutSourceArchiveRegistry(FalloutArchiveRegistrySource source, FalloutArchiveStartupInput input,
        Func<string, FalloutBsaArchive> read, FalloutSourceArchiveRegistrySnapshot? restore = null)
    {
        source.Validate(); input.Validate(); ArgumentNullException.ThrowIfNull(read);
        _source = source; _input = input;
        foreach (var name in input.Names)
        {
            var ordinal = checked(_registrations.Count + 1L);
            try
            {
                var reader = read(name); var identity = reader.DirectorySource;
                if (!Path.GetFileName(identity.Archive).Equals(name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Registered archive reader differs from the actual requested source filename.");
                var index = source.InsertionIndex(name, _registry.Select(item => item.Row.RequestedName).ToArray());
                var row = new FalloutArchiveRegistration(ordinal, name, FalloutArchiveRegistrationPhase.Registered,
                    source.Order == FalloutArchiveRegistrationOrder.Append ? null : source.Category(name), index, identity);
                _registrations.Add(row); _registry.Insert(index, (row, reader));
            }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            {
                _registrations.Add(new(ordinal, name, FalloutArchiveRegistrationPhase.Failed,
                    FailureType: error.GetType().FullName, Error: error.Message));
                _failure = _failure is null ? error : new AggregateException("Archive startup retained independent registration failures.", _failure, error);
                // Original startup retains its false result and continues the
                // remaining tokens. Earlier registrations are not erased.
            }
        }
        if (restore is not null && JsonSerializer.Serialize(Capture()) != JsonSerializer.Serialize(restore))
            throw new InvalidDataException("Cold archive registration changed actual settings, auxiliary bytes, table identities, order or failure prefix.");
    }

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(Capture()));
    internal IReadOnlyList<FalloutBsaDirectorySource> Sources => Array.AsReadOnly(_registry.Select(item => item.Reader.DirectorySource).ToArray());

    internal void RequireFileManagerOwner(FalloutArchiveFileManagerSource source, FalloutArchiveStartupModes modes)
    {
        ObjectDisposedException.ThrowIf(_retired, this); source.Validate(); modes.Validate();
        if (source.EngineSha256 != _source.EngineSha256 || source.RuntimeSha256 != _source.RuntimeSha256 ||
            modes.Identity != _input.ModesSha256 || modes.UseArchives != 1)
            throw new InvalidDataException("Source archive registry differs from its actual file-manager startup owner.");
    }

    internal IReadOnlyList<string> ReadDirectory(string folder, string extension)
    {
        RequireAvailable();
        var paths = new LinkedList<string>();
        foreach (var item in _registry)
            foreach (var row in item.Reader.SourceSoundDirectory(folder, extension))
                // The original exclusion set was captured before traversal.
                // Both source modes are enabled here, so no loose nodes exist;
                // a later archive's matching path does not remove earlier nodes.
                paths.AddFirst(row.LogicalPath);
        return Array.AsReadOnly(paths.ToArray());
    }

    internal FalloutArchiveSoundWinner? Resolve(string logicalPath)
    {
        RequireAvailable();
        foreach (var item in _registry)
            if (item.Reader.SourceSoundMember(logicalPath) is { } member)
                return new(item.Row.Ordinal, item.Reader.DirectorySource, member);
        return null;
    }

    internal FalloutSourceArchiveRegistrySnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        return new(Schema, _source.Identity, _input, Array.AsReadOnly(_registrations.ToArray()),
            Array.AsReadOnly(_registry.Select(item => item.Row.Ordinal).ToArray()), _failure?.Message);
    }

    internal void RequireSameSnapshot(FalloutSourceArchiveRegistrySnapshot saved)
    {
        if (saved is null || JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(Capture()))
            throw new InvalidDataException("Actual source archive registry lost its current/cold identity.");
    }

    internal void Retire()
    {
        // These real readers remain owned by RuntimeLiveContentSource. Repeated
        // source registrations may borrow the same retained handle; this owner
        // never closes it, removes an original row or replays a cue selection.
        _retired = true;
    }

    private void RequireAvailable()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
    }
}
