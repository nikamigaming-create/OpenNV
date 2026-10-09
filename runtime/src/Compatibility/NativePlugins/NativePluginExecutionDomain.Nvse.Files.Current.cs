using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeNvseSourceFileCurrentStamp(string Schema, string SourceSha256, string StateSha256);
internal interface INativeNvseSourceFileCurrentAuthority : INativeNvseSourceFileAuthority
{
    NativeNvseSourceFileCurrentStamp ReadSourceFileCurrentStamp();
    void RetainSourceFileOutputFailure(Exception error);
}
internal sealed record NativeNvseSourceFileCurrentEntry(string Key, string SourceOwner,
    string SourceSha256, string StateSha256, string StateSchema);

// This receipt is process-local authority. A serialized record or cloned
// flag cannot admit another generation, source cursor or native publication.
internal sealed class NativeNvseSourceFileCurrentCapture
{
    internal NativePluginExecutionDomain Domain { get; }
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal int CompletedCalls { get; }
    internal IReadOnlyList<NativeNvseSourceFileCurrentEntry> Entries { get; }
    internal IReadOnlyDictionary<ulong, (NativeNvseSourceObject Object, string Image, string Metadata)> Native { get; }
    internal NativeNvseSourceFileCurrentCapture(NativePluginExecutionDomain domain, ulong generation, ulong module,
        int calls, IReadOnlyList<NativeNvseSourceFileCurrentEntry> entries,
        IReadOnlyDictionary<ulong, (NativeNvseSourceObject Object, string Image, string Metadata)> native)
    {
        Domain = domain; Generation = generation; Module = module; CompletedCalls = calls;
        Entries = entries; Native = native;
    }
}

internal sealed partial class NativePluginExecutionDomain
{
    private NativeNvseSourceFileCurrentCapture? _nvseSourceFileCurrentCapture;

    internal NativeNvseSourceFileCurrentCapture CaptureNvseSourceFileCurrent(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); RequireNvseSourceFilesIdle();
        if (_nvseLocalCallers.Count != 0 || _nvseSourceObjects.Values.Any(value => value.Calls != 0))
            throw new InvalidOperationException("An actual caller still owns native source continuation.");
        foreach (var graph in _nvseSourceGraphs.Values.Distinct()) RefreshNvseSourceGraph(plugin, graph);
        var entries = new List<NativeNvseSourceFileCurrentEntry>();
        var native = new Dictionary<ulong, (NativeNvseSourceObject Object, string Image, string Metadata)>();
        foreach (var value in _nvseSourceObjects.Values.Where(value => value.Authority is INativeNvseSourceFileAuthority).OrderBy(value => value.Id))
        {
            VerifyNvseSourceObject(plugin, value);
            if (value.Authority is not INativeNvseSourceFileCurrentAuthority authority ||
                !_nvseSourceGraphs.TryGetValue(value.Id, out var graph))
                throw new NotSupportedException("Reached contributor continuation lacks its actual retained parser/binary/metadata graph owner.");
            var key = graph.Objects.Single(pair => ReferenceEquals(pair.Value, value)).Key;
            var before = RequireSourceFileStamp(value, authority.ReadSourceFileCurrentStamp());
            var image = ReadNvseGraphBytes(value.Image); var metadata = ReadNvseGraphBytes(value.Metadata);
            if (!image.AsSpan().SequenceEqual(graph.NativeImages[value.Id]) ||
                !metadata.AsSpan().SequenceEqual(graph.NativeMetadata[value.Id]))
                throw Fatal(new InvalidDataException("Current native contributor bytes differ from their actual completed publication."));
            var after = RequireSourceFileStamp(value, authority.ReadSourceFileCurrentStamp());
            if (before != after) throw new InvalidOperationException("Actual shared source state changed during native continuation capture.");
            entries.Add(new(key, value.Authority.SourceOwner, before.SourceSha256, before.StateSha256, before.Schema));
            native.Add(value.Id, (value, Convert.ToHexString(SHA256.HashData(image)), Convert.ToHexString(SHA256.HashData(metadata))));
        }
        var capture = new NativeNvseSourceFileCurrentCapture(this, Generation, plugin.Module, _nvseFileCalls.Count,
            entries.AsReadOnly(), new System.Collections.ObjectModel.ReadOnlyDictionary<ulong,
                (NativeNvseSourceObject Object, string Image, string Metadata)>(native));
        _nvseSourceFileCurrentCapture = capture; RequireNvseSourceFileCurrent(plugin, capture); return capture;
    }

    internal void RequireNvseSourceFileCurrent(NativeNvsePlugin plugin, NativeNvseSourceFileCurrentCapture capture)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); RequireNvseSourceFilesIdle();
        if (!ReferenceEquals(capture, _nvseSourceFileCurrentCapture) || !ReferenceEquals(capture.Domain, this) ||
            capture.Generation != Generation || capture.Module != plugin.Module || capture.CompletedCalls != _nvseFileCalls.Count ||
            capture.Native.Count != _nvseSourceObjects.Values.Count(value => value.Authority is INativeNvseSourceFileAuthority) ||
            _nvseLocalCallers.Count != 0)
            throw new InvalidOperationException("Native source current receipt has a stale, foreign or unfinished lifetime.");
        foreach (var (id, observed) in capture.Native)
        {
            var value = observed.Object; VerifyNvseSourceObject(plugin, value);
            if (value.Id != id || !_nvseSourceGraphs.TryGetValue(id, out var graph) ||
                value.Authority is not INativeNvseSourceFileCurrentAuthority authority)
                throw new InvalidOperationException("Captured contributor lost its genuine native/source graph identity.");
            var key = graph.Objects.Single(pair => ReferenceEquals(pair.Value, value)).Key;
            var expected = capture.Entries.Single(entry => entry.Key == key);
            var stamp = RequireSourceFileStamp(value, authority.ReadSourceFileCurrentStamp());
            if (expected.SourceOwner != value.Authority.SourceOwner || expected.StateSchema != stamp.Schema ||
                !StringComparer.OrdinalIgnoreCase.Equals(expected.SourceSha256, stamp.SourceSha256) ||
                !StringComparer.OrdinalIgnoreCase.Equals(expected.StateSha256, stamp.StateSha256) ||
                observed.Image != Convert.ToHexString(SHA256.HashData(ReadNvseGraphBytes(value.Image))) ||
                observed.Metadata != Convert.ToHexString(SHA256.HashData(ReadNvseGraphBytes(value.Metadata))))
                throw new InvalidDataException("Native/source continuation changed after its actual idle publication capture.");
        }
    }

    private static NativeNvseSourceFileCurrentStamp RequireSourceFileStamp(NativeNvseSourceObject value, NativeNvseSourceFileCurrentStamp stamp)
    {
        if (stamp is null || string.IsNullOrWhiteSpace(stamp.Schema) ||
            stamp.SourceSha256 is null || stamp.StateSha256 is null || stamp.StateSha256.Length != 64 || !stamp.StateSha256.All(Uri.IsHexDigit) ||
            !StringComparer.OrdinalIgnoreCase.Equals(stamp.SourceSha256, value.Authority.SourceSha256))
            throw new InvalidDataException("Native source current stamp lacks its complete actual source/state identity.");
        return stamp;
    }
}
