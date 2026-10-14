using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    private FalloutArchiveFileManager? _soundFileManager;
    private FalloutSoundSearchNamespace? _soundSearchNamespace;
    private readonly List<FileStream> _soundInvalidationLeases = [];

    internal FalloutMenuSoundDirectory ObserveSourceMenuDirectory(FalloutPluginStack records,
        FalloutAdvancementRuntimeSource runtime, string raw, string extension)
    {
        using var readLifetime = EnterResourceRead();
        lock (_menuSoundGate)
        {
            _ = OpenMenuSoundSelection(records, runtime);
            return (_soundFileManager ?? throw new InvalidOperationException("Observed directory lost its selected file-manager owner."))
                .ObserveSourceDirectory(raw, extension);
        }
    }

    private FalloutArchiveFileManager OpenSoundFileManager(FalloutMenuSoundSelectionSource source,
        FalloutArchiveFileManagerSnapshot? restore)
    {
        var declaration = FalloutArchiveFileManagerSource.Read(source);
        var modes = declaration.ReadStartup(this);
        IReadOnlyList<FalloutBsaArchive> archives = [];
        FalloutArchiveInvalidationInput? invalidation = null;
        FalloutSourceArchiveRegistry? registry = null;
        if (modes.UseArchives != 0)
        {
            ArchiveWarmup.GetAwaiter().GetResult();
            archives = Array.AsReadOnly(_archivePaths.Select(GetArchive).ToArray());
            invalidation = ReadSourceInvalidation(modes, declaration);
            registry = OpenSourceSoundRegistry(source, modes, restore?.Registry);
        }
        return new(declaration, modes, archives, invalidation,
            (raw, extension) => ReadActualLooseSoundDirectory(modes, raw, extension),
            path => TryResolve(path, null, out var winner) ? winner : null, restore, registry);
    }

    private FalloutArchiveInvalidationInput ReadSourceInvalidation(FalloutArchiveStartupModes modes,
        FalloutArchiveFileManagerSource declaration)
    {
        var scope = FalloutAdvancementRuntimeReceipt.Hash(declaration.Identity + "\0" + modes.Identity + "\0" +
            StackId + "\0" + JsonSerializer.Serialize(_layers.Roots));
        try
        {
            var search = RequireStartupSoundNamespace(modes);
            var name = modes.InvalidationFile.Value;
            FalloutArchiveNameHash.RequireAscii(name);
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name.IndexOfAny(['\\', '/', ':']) >= 0)
                throw new NotSupportedException("Invalidation path requires its original absolute/special-prefix search consumer.");
            // The pre-registration source lookup tries its direct installation
            // namespace first, then the actual startup DATA prefix. It does
            // not merge files or reuse a later archive resource winner.
            if (search.FindStartupFile(name) is { } found)
            {
                var file = new FileStream(found, FileMode.Open, FileAccess.Read, FileShare.Read);
                _soundInvalidationLeases.Add(file);
                if (file.Length > 1024 * 1024)
                    throw new NotSupportedException("Source invalidation input exceeds the bounded inspected list extent.");
                var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
                return new(FalloutAdvancementRuntimeReceipt.Hash(scope + "\0" + found), true, file.Length, hash);
            }
            return new(scope, false, 0, null);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            // Exact cues and an unreached directory remain constructible. This
            // is explicitly Unowned, never an absent/empty visibility receipt.
            return new(scope, false, 0, null, error.GetType().FullName + ": " + error.Message);
        }
    }

    private FalloutSoundSearchNamespace RequireStartupSoundNamespace(FalloutArchiveStartupModes modes)
    {
        var installation = Path.GetDirectoryName(FalloutExecutablePath) ?? throw new InvalidDataException("Selected image lost its installation root.");
        var search = _soundSearchNamespace ??= new(installation, ContentRoot, _layers);
        search.RequireLocalMasterPath(modes.LocalMasterPath.Value);
        return search;
    }

    private void RetireSoundFileManager()
    {
        List<Exception>? failures = null;
        try { _soundFileManager?.Retire(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { (failures ??= []).Add(error); }
        try { RetireSourceSoundRegistry(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { (failures ??= []).Add(error); }
        foreach (var file in _soundInvalidationLeases.ToArray())
            try { file.Dispose(); _soundInvalidationLeases.Remove(file); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { (failures ??= []).Add(error); }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException("Sound file-manager retirement retained independent source failures.", failures);
    }
}
