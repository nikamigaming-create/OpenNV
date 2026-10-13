using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    private FalloutSourceArchiveRegistry? _soundArchiveRegistry;
    private readonly List<FileStream> _soundRegistryInputLeases = [];

    private FalloutSourceArchiveRegistry OpenSourceSoundRegistry(FalloutMenuSoundSelectionSource selection,
        FalloutArchiveStartupModes modes, FalloutSourceArchiveRegistrySnapshot? restore)
    {
        var source = FalloutArchiveRegistrySource.Read(selection, FalloutExecutablePath);
        var search = RequireStartupSoundNamespace(modes);
        var roots = search.PhysicalRoots;
        var auxiliaryDeclaration = FalloutExecutableStringTable.ReadArchiveAuxiliaryDeclaration(FalloutExecutablePath, selection.EngineSha256);
        var auxiliaryName = auxiliaryDeclaration.Filename;
        var auxiliaryScope = FalloutAdvancementRuntimeReceipt.Hash(source.Identity + "\0" + modes.Identity + "\0" +
            StackId + "\0" + roots[0] + "\0" + auxiliaryName);
        var matches = search.FindAuxiliaryFile(auxiliaryName);
        FalloutArchiveAuxiliaryInput auxiliary; IReadOnlyList<string> additional = [];
        if (matches is null) auxiliary = new(auxiliaryScope, auxiliaryName, false, 0, null);
        else
        {
            var file = new FileStream(matches, FileMode.Open, FileAccess.Read, FileShare.Read);
            _soundRegistryInputLeases.Add(file);
            if (file.Length > 1024 * 1024)
                throw new NotSupportedException("Original startup auxiliary input exceeds its bounded inspected file extent.");
            var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
            auxiliary = new(auxiliaryScope, auxiliaryName, true, bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var available = new List<string>();
            foreach (var name in FalloutArchiveStartupInput.ReadAuxiliaryArchiveNames(bytes))
                // Source startup appends only candidates whose real DATA file
                // exists. An unavailable namespace is never observed as absent.
                if (search.FindDataFile(name) is not null) available.Add(name);
            additional = Array.AsReadOnly(available.ToArray());
        }
        var startupList = modes.ArchiveList.Value;
        foreach (var name in additional)
        {
            if (startupList.Length < 2)
                throw new NotSupportedException("Auxiliary startup reaches the original list look-behind outside an admitted string extent.");
            if (startupList[^2] == '\n') startupList = startupList[..^2];
            var next = startupList + auxiliaryDeclaration.AppendSeparator + name;
            if (System.Text.Encoding.ASCII.GetByteCount(next) >= auxiliaryDeclaration.AppendCapacity)
                throw new NotSupportedException("Auxiliary startup append reaches the original bounded list buffer; its invalid-parameter consumer is unowned.");
            startupList = next;
        }
        var names = FalloutArchiveStartupInput.ReadConfiguredNames(startupList).ToArray();
        var input = new FalloutArchiveStartupInput(modes.Identity, auxiliary, Array.AsReadOnly(names));
        return _soundArchiveRegistry = new(source, input, name =>
        {
            var file = search.FindStartupFile(name) ??
                throw new FileNotFoundException("Actual configured archive registration has no source file.", name);
            return GetArchive(file);
        }, restore);
    }

    internal bool TryReadSourceMenuAudio(string logicalPath, long selectionOrdinal, out byte[] data, out string source)
    {
        lock (_menuSoundGate)
        {
            ObjectDisposedException.ThrowIf(_menuSoundRetiring, this);
            var manager = _soundFileManager ?? throw new NotSupportedException("Menu media read has no actual selected file-manager lifetime.");
            var selection = _menuSoundSelection ?? throw new NotSupportedException("Menu media read has no actual selected sound caller.");
            var raw = selection.RequirePreparedMediaPath(selectionOrdinal, logicalPath);
            var winner = manager.ResolveArchiveSound(logicalPath);
            if (winner is not null)
            {
                var archive = GetArchive(winner.Archive.Archive);
                source = winner.Archive.Archive + "::" + winner.Member.LogicalPath;
                _archiveResources.TryAdd(source, winner.Member.LogicalPath);
                var payload = _payloads.GetOrAddWithEncoding(source, _ =>
                {
                    var read = archive.ReadWithEncoding(winner.Member.LogicalPath);
                    return new(read.Data, read.Encoding);
                });
                data = payload.Data; ResourceReadObserver?.Invoke(logicalPath, source, data); return true;
            }
            // The source's ordinary read checks its registry before physical
            // search. If no registered member exists, the selected DATA
            // namespace supplies the physical fallback; legacy archive order cannot.
            var search = RequireStartupSoundNamespace(manager.RequirePhysicalSoundModes());
            var loose = search.FindPhysicalFile(raw);
            if (loose is not null)
            {
                var info = new FileInfo(loose);
                var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                var payload = _loosePayloads.AddOrUpdate(loose,
                    _ => new LoosePayload(info.Length, mtime, $"{loose}|{info.Length}|{mtime}"),
                    (_, previous) => previous.Bytes == info.Length && previous.MtimeMilliseconds == mtime
                        ? previous : new LoosePayload(info.Length, mtime, $"{loose}|{info.Length}|{mtime}"));
                data = _payloads.GetOrAdd(payload.CacheKey, _ => File.ReadAllBytes(loose)); source = loose;
                ResourceReadObserver?.Invoke(logicalPath, source, data); return true;
            }
            data = []; source = string.Empty; return false;
        }
    }

    private void RetireSourceSoundRegistry()
    {
        List<Exception>? errors = null;
        try { _soundArchiveRegistry?.Retire(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { (errors ??= []).Add(error); }
        foreach (var file in _soundRegistryInputLeases.ToArray())
            try { file.Dispose(); _soundRegistryInputLeases.Remove(file); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { (errors ??= []).Add(error); }
        if (errors is { Count: 1 }) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is { Count: > 1 }) throw new AggregateException("Sound registry retained independent source retirement failures.", errors);
    }
}
