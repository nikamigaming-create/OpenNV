using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutNativeSourceContinuationCapture
{
    internal FalloutNativePluginCampaign Campaign { get; }
    internal FalloutNativeSourceContinuationState State { get; }
    internal IReadOnlyDictionary<ulong, NativeNvseSourceFileCurrentCapture> Native { get; }
    internal FalloutNativeSourceContinuationCapture(FalloutNativePluginCampaign campaign,
        FalloutNativeSourceContinuationState state, IReadOnlyDictionary<ulong, NativeNvseSourceFileCurrentCapture> native)
    { Campaign = campaign; State = state; Native = native; }
}

internal sealed partial class FalloutNativePluginCampaign
{
    private FalloutNativeSourceContinuationCapture? _sourceContinuation;

    internal FalloutNativeSourceContinuationCapture? CaptureSourceContinuation()
    {
        RequireCurrent();
        if (ModuleFailure is { } failure) throw new NotSupportedException("Native source continuation retains selected module failure: " + failure);
        if (_active != 0) throw new InvalidOperationException("Native source continuation overlaps an actual original caller.");
        if (_modules.Count == 0) return null;
        var selection = _selection ?? throw new InvalidOperationException("Native source continuation has no exact selected module/runtime identity.");
        var captures = new Dictionary<ulong, NativeNvseSourceFileCurrentCapture>();
        var modules = new List<FalloutNativeSourceModuleState>();
        foreach (var module in _modules)
        {
            if (module.Retired || module.Plugin.Phase != NativeNvsePhase.LoadedTrue)
                throw new NotSupportedException("Native source continuation requires a genuinely initialized live module.");
            var capture = module.Domain.CaptureNvseSourceFileCurrent(module.Plugin);
            captures.Add(module.Domain.Generation, capture);
            modules.Add(new(module.Admission.LogicalPath, module.Admission.Sha256,
                checked((ulong)capture.CompletedCalls), Array.AsReadOnly(capture.Entries.Select(entry =>
                    new FalloutNativeSourceModuleContributor(entry.Key, entry.SourceOwner, entry.SourceSha256, entry.StateSha256, entry.StateSchema)).ToArray())));
        }
        var sources = _graphSource?.CaptureLoadedSourceState();
        foreach (var module in modules)
            foreach (var contributor in module.Contributors)
            {
                if (sources is null) throw new InvalidDataException("Reached original contributor has no shared campaign source collection.");
                var matches = sources.Contributors.Where(row => contributor.Key == "mod:" + row.Plugin.ToUpperInvariant() &&
                    StringComparer.OrdinalIgnoreCase.Equals(contributor.SourceSha256, row.Sha256)).ToArray();
                if (matches.Length != 1 || contributor.StateSchema != "opennv-actual-loaded-source/v1" ||
                    contributor.CurrentStateSha256 != FalloutNativePluginLoadedFile.SourceStateSha256(matches[0].State))
                    throw new InvalidDataException("Native module continuation differs from its one actual shared contributor cursor/buffer/metadata state.");
            }
        var state = new FalloutNativeSourceContinuationState("opennv-native-source-continuation/v1", _source.StackId,
            selection.RuntimeSha256, modules.AsReadOnly(), sources);
        var owned = new FalloutNativeSourceContinuationCapture(this, state,
            new System.Collections.ObjectModel.ReadOnlyDictionary<ulong, NativeNvseSourceFileCurrentCapture>(captures));
        _sourceContinuation = owned; RequireSourceContinuationCurrent(owned); return owned;
    }

    internal void RequireSourceContinuationCurrent(FalloutNativeSourceContinuationCapture capture)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(capture);
        if (!ReferenceEquals(capture, _sourceContinuation) || !ReferenceEquals(capture.Campaign, this) ||
            _active != 0 || ModuleFailure is not null || _selection is null || capture.State.StackId != _source.StackId ||
            capture.State.RuntimeSha256 != _selection.RuntimeSha256 || capture.Native.Count != _modules.Count ||
            capture.State.Modules.Count != _modules.Count)
            throw new InvalidOperationException("Native source continuation has a stale, foreign or incomplete actual campaign lifetime.");
        for (var ordinal = 0; ordinal < _modules.Count; ++ordinal)
        {
            var module = _modules[ordinal]; var saved = capture.State.Modules[ordinal];
            if (module.Retired || saved.LogicalPath != module.Admission.LogicalPath || saved.Sha256 != module.Admission.Sha256 ||
                !capture.Native.TryGetValue(module.Domain.Generation, out var native) || saved.CompletedSourceFileCalls != (ulong)native.CompletedCalls)
                throw new InvalidOperationException("Native source continuation changed the exact original module order/identity/generation.");
            module.Domain.RequireNvseSourceFileCurrent(module.Plugin, native);
        }
        var current = _graphSource?.CaptureLoadedSourceState();
        if (!SourceCollectionsEqual(current, capture.State.LoadedSources))
            throw new InvalidDataException("Actual shared source state changed after native campaign continuation capture.");
    }

    // This diagnostic state can reconstruct the actual retained source component
    // from original coordinates. It is not an original DLL's cold execution owner.
    private static bool SourceCollectionsEqual(FalloutNativeSourceCollectionState? current, FalloutNativeSourceCollectionState? saved)
        => current is null ? saved is null : saved is not null &&
            System.Text.Json.JsonSerializer.Serialize(current) == System.Text.Json.JsonSerializer.Serialize(saved);

    private void RequireOriginalModuleColdContinuation()
    {
        if (_modules.Count != 0)
            throw new NotSupportedException("Initialized original modules require their complete native globals, used hooks, callbacks, class/CRT graph and co-save cold execution owner; empty source/I/O histories cannot admit a campaign cold save.");
    }
}
