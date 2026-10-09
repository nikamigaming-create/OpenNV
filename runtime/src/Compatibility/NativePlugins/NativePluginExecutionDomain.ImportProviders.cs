namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private string? _importProviderCompanion;
    private NativePluginImportProviderBuild? _importProviderBuild;
    private NativePluginOriginalLoaderAttempt? _originalLoaderAttempt;
    private bool _importProvidersRetiring;
    private readonly List<NativePluginImportProviderReceipt> _importProviderReceipts = [];
    private bool ImportProviderSourcesRetired => _importProviderBuild is null;
    internal IReadOnlyList<NativePluginImportProviderReceipt> ImportProviderReceipts => _importProviderReceipts.AsReadOnly();
    internal NativePluginOriginalLoaderAttempt? OriginalLoaderAttempt => _originalLoaderAttempt;

    internal void BindNativeImportProviderBuild(string actualCompanion)
    {
        VerifyOwner();
        if (_importProviderCompanion is not null || _originalLoaderAttempt is not null || _nvseImageAttempted || NativeModuleCount != 0)
            throw new InvalidOperationException("First-party import-provider build must bind once before original image admission.");
        _importProviderCompanion = Path.GetFullPath(actualCompanion);
    }
    private void PrepareOriginalImportProviders(string path, string expectedSha256)
    {
        var declaration = NativePluginImageDeclarations.Read(path, dll: true);
        if (!declaration.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original dependency declarations differ from the retained unchanged selected DLL.");
        if (!declaration.Imports.Any(row => row.Library == "bcrypt.dll")) return;
        if (_originalLoaderAttempt is not null || _importProviderBuild is not null)
            throw new InvalidOperationException("Original dependency preparation cannot be replayed in this process generation.");
        var companion = _importProviderCompanion ?? throw new NotSupportedException("Selected original imports lack their actual first-party callable-provider build.");
        var build = NativePluginImportProviderBuild.Open(companion, declaration);
        _importProviderBuild = build;
        // Retain the sources before any real provider loader call. Failed
        // transport/entry cannot release them while the exact child is alive.
        using var reader = Exchange(NativePluginDomainOperation.ImportProviders, Payload(writer =>
        {
            writer.Write(1U); WriteText(writer, path); WriteText(writer, declaration.Sha256);
            writer.Write(checked((uint)build.Images.Count));
            foreach (var provider in build.Images)
            {
                writer.Write((uint)provider.Kind); WriteText(writer, provider.Library); WriteText(writer, provider.Path); WriteText(writer, provider.Sha256);
                writer.Write(checked((uint)provider.Exports.Count));
                foreach (var export in provider.Exports) { WriteText(writer, export.Name); writer.Write(export.Rva); }
                writer.Write(checked((uint)provider.OriginalImports.Count));
                foreach (var import in provider.OriginalImports)
                { WriteText(writer, import.Name ?? throw new InvalidDataException("Original provider import omitted its real name.")); writer.Write(import.SlotRva); }
            }
        }));
        var attempt = reader.ReadUInt64();
        if (attempt == 0 || attempt >= uint.MaxValue) throw new InvalidDataException("Original source attempt has no native generation-owned identity.");
        _originalLoaderAttempt = new(path, declaration.Sha256, attempt, build.Images);
        ReadImportProviderReceipts(reader, attempt, retiring: false); Finish(reader); build.RequireCurrent();
    }
    private void EnterOriginalLoaderAttempt()
    {
        if (_originalLoaderAttempt is not { } attempt) return;
        if (attempt.Phase != NativePluginLoaderAttemptPhase.Prepared || _callDepth != 0)
            throw new InvalidOperationException("Original Windows entry has no unique prepared source attempt.");
        _importProviderBuild!.RequireCurrent(); attempt.Phase = NativePluginLoaderAttemptPhase.Entering; ++_callDepth;
    }
    private void CompleteOriginalLoaderAttempt(ulong module)
    {
        if (_originalLoaderAttempt is not { } attempt) return;
        if (attempt.Phase != NativePluginLoaderAttemptPhase.Entering || module != attempt.Id)
            throw new InvalidDataException("Genuine original mapping does not belong to its entered source attempt.");
        attempt.Phase = NativePluginLoaderAttemptPhase.Mapped;
    }
    private void LeaveOriginalLoaderAttempt()
    {
        if (_originalLoaderAttempt is not { } attempt || _callDepth == 0) return;
        if (attempt.Phase == NativePluginLoaderAttemptPhase.Entering) attempt.Phase = NativePluginLoaderAttemptPhase.Failed;
        --_callDepth;
    }
    private bool IsOriginalLoaderCallback(NativePluginDomainOperation operation, Frame frame) =>
        operation == NativePluginDomainOperation.LoadNvse && frame.Kind == NativePluginDomainMessage.IoCallback &&
        frame.Operation == CryptoCallback && _originalLoaderAttempt is { Phase: NativePluginLoaderAttemptPhase.Entering };
    private bool IsEnteredOriginalLoaderIo(uint operation, ulong module, ulong parent) =>
        operation == CryptoCallback && parent != 0 && parent == _currentCall &&
        _originalLoaderAttempt is { Phase: NativePluginLoaderAttemptPhase.Entering } attempt && module == attempt.Id;
    private ulong CurrentOriginalIoModule(ulong parent)
    {
        if (_nvsePlugin is { } plugin && plugin.Generation == Generation) return plugin.Module;
        if (parent != 0 && parent == _currentCall && _originalLoaderAttempt is { Phase: NativePluginLoaderAttemptPhase.Entering } attempt)
            return attempt.Id;
        throw new InvalidDataException("Private provider request has no actual mapped or entered original-source caller.");
    }
    internal IReadOnlyList<NativePluginImportProviderReceipt> InspectNativeImportProviders()
    {
        VerifyOwner();
        var attempt = _originalLoaderAttempt ?? throw new InvalidOperationException("No first-party import provider was selected by this original source.");
        if (_callDepth != 0 || attempt.Phase is NativePluginLoaderAttemptPhase.Entering or NativePluginLoaderAttemptPhase.Retired)
            throw new InvalidOperationException("An active or retired original source prevents provider inspection.");
        using var reader = Exchange(NativePluginDomainOperation.ImportProviders, Payload(writer => writer.Write(2U)));
        if (reader.ReadUInt64() != attempt.Id) throw new InvalidDataException("First-party provider inspection changed its actual source attempt.");
        ReadImportProviderReceipts(reader, attempt.Id, retiring: false); Finish(reader);
        return _importProviderReceipts.AsReadOnly();
    }
    private void RetireNativeImportProviders()
    {
        if (_originalLoaderAttempt is not { } attempt || attempt.Phase == NativePluginLoaderAttemptPhase.Retired) return;
        if (attempt.Phase == NativePluginLoaderAttemptPhase.Entering)
            throw new InvalidOperationException("Original entry is still using its first-party callable provider.");
        _importProvidersRetiring = true;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.ImportProviders, Payload(writer => writer.Write(3U)));
            if (reader.ReadUInt64() != attempt.Id) throw new InvalidDataException("First-party dependency retirement changed its actual source attempt.");
            ReadImportProviderReceipts(reader, attempt.Id, retiring: true); Finish(reader);
            attempt.Phase = NativePluginLoaderAttemptPhase.Retired;
            _importProviderBuild!.RequireCurrent(); _importProviderBuild.Dispose(); _importProviderBuild = null;
        }
        finally { _importProvidersRetiring = false; }
    }
    private void ReadImportProviderReceipts(BinaryReader reader, ulong attemptId, bool retiring)
    {
        var attempt = _originalLoaderAttempt ?? throw new InvalidDataException("Provider receipt precedes its actual source attempt.");
        if (reader.ReadUInt32() != attempt.Providers.Count) throw new InvalidDataException("First-party dependency receipt omitted or duplicated a selected source owner.");
        foreach (var provider in attempt.Providers)
        {
            var kind = (NativePluginImportProviderKind)reader.ReadUInt32(); var library = ReadText(reader);
            var path = ReadText(reader); var sha = ReadText(reader); var image = reader.ReadUInt32(); var thread = reader.ReadUInt32();
            var bound = reader.ReadUInt32(); var attach = reader.ReadUInt32(); var detach = reader.ReadUInt32(); var active = reader.ReadUInt32();
            var calls = new ulong[7]; for (var at = 0; at < calls.Length; ++at) calls[at] = reader.ReadUInt64();
            var released = reader.ReadUInt32(); var present = reader.ReadUInt32();
            if (kind != provider.Kind || library != provider.Library || sha != provider.Sha256 ||
                !StringComparer.OrdinalIgnoreCase.Equals(NativePluginPrivateIo.Canonical(path), NativePluginPrivateIo.Canonical(provider.Path)) ||
                thread != NativeThread || attach != 1 || detach != 0 || active != 0 || bound != (retiring ? 0U : 1U) ||
                released != (retiring ? 1U : 0U) || present != (retiring ? 0U : 1U) || (image == 0) != retiring)
                throw new InvalidDataException("First-party dependency changed its actual source, callable, mapping or loader lifetime.");
            var previous = _importProviderReceipts.LastOrDefault(row => row.Attempt == attemptId && row.Kind == kind && row.Library == library);
            if (previous is not null && calls.Where((count, at) => count < previous.Calls[at]).Any())
                throw new InvalidDataException("First-party dependency lost a real used export invocation.");
            _importProviderReceipts.Add(new(Generation, attemptId, attempt.Sha256, kind, library, provider.Path, sha,
                image, thread, bound != 0, attach, detach, active, Array.AsReadOnly(calls), released != 0, present != 0));
        }
    }
    private void ClearNativeImportProvidersAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Callable provider sources must survive unconfirmed original child closure.");
        _importProviderBuild?.Dispose(); _importProviderBuild = null;
        // Keep phase and actual receipts; fault/process retirement does not
        // assert successful original detach or provider FreeLibrary.
    }
}
