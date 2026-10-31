using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Mounted source lifetime owns immutable declarations. Skeleton bindings,
// action clocks, sound events and random selection remain actor-local.
internal sealed class NativeAnimationResources
{
    private sealed record Entry(FalloutNifFile Source, long Bytes, string Identity) { internal long LastUse; }
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, NativeAnimationResources> Libraries = new();
    private readonly RuntimeLiveContentSource _source;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _preparations = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cancellation = new();
    private long _bytes, _clock;
    private const long Budget = 64L * 1024 * 1024;

    private NativeAnimationResources(RuntimeLiveContentSource source)
    {
        _source = source;
        source.RegisterRetirement(Retire);
    }

    internal static NativeAnimationResources For(RuntimeLiveContentSource source)
        => Libraries.GetValue(source, value => new(value));

    internal FalloutNifFile Read(string path) => Read(path, out _);

    internal FalloutNifFile Read(string path, out string identity)
    {
        path = FalloutBsaArchive.CanonicalPath(path);
        lock (_gate)
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(path, out var cached)) { cached.LastUse = ++_clock; identity = cached.Identity; return cached.Source; }
        }
        if (!_source.TryRead(path, null, out var bytes, out identity)) throw new FileNotFoundException("Actor animation is absent.", path);
        var source = FalloutNifFile.Read(bytes);
        _ = source.Sha256;
        lock (_gate)
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(path, out var cached)) { cached.LastUse = ++_clock; identity = cached.Identity; return cached.Source; }
            // Conservative retained decode allowance, separate from raw BSA
            // payload caching. Live bindings retain their sources on eviction.
            var size = bytes.LongLength * 4;
            if (size <= Budget)
            {
                foreach (var candidate in _entries.OrderBy(pair => pair.Value.LastUse).ToArray())
                {
                    if (_bytes + size <= Budget) break;
                    _entries.Remove(candidate.Key); _bytes -= candidate.Value.Bytes;
                }
                _entries.Add(path, new(source, size, identity) { LastUse = ++_clock }); _bytes += size;
            }
            return source;
        }
    }

    internal void Prepare(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            foreach (var path in paths.Select(FalloutBsaArchive.CanonicalPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_preparations.ContainsKey(path) || _entries.ContainsKey(path)) continue;
                var task = FalloutContentWorkers.Run(() =>
                {
                    var source = Read(path);
                    foreach (var sequence in source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>())
                    {
                        _cancellation.Token.ThrowIfCancellationRequested();
                        if (sequence.TextKeys >= 0) _ = source.ReadObject(sequence.TextKeys);
                        foreach (var link in sequence.ControlledBlocks)
                            if (link.Interpolator >= 0 && link.ControllerType == "NiTransformController")
                                _ = new FalloutNifAnimationSampler(source, link.Interpolator);
                    }
                    return true; // Completed jobs must not retain evicted source graphs.
                }, _cancellation.Token);
                _preparations.Add(path, task);
                // Preparation cannot reject an actor because an unselected
                // branch is unsupported. Selecting that resource still throws
                // through Read and the ordinary combat divergence owner.
                _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private void Retire()
    {
        Task[] pending;
        lock (_gate) { _cancellation.Cancel(); pending = _preparations.Values.ToArray(); }
        try { Task.WhenAll(pending).GetAwaiter().GetResult(); }
        catch (Exception) { /* Consumers retain the original source failure; retirement drains every worker. */ }
        lock (_gate) { _entries.Clear(); _preparations.Clear(); _bytes = 0; }
    }
}
