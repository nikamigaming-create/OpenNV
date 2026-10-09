namespace OpenNV.Runtime.Compatibility.NativePlugins;

// A real generated Script producer supplies this authority after compiling and
// publishing its actual source object. An ordinary winning SCPT never supplies it.
internal abstract class NativeNvseLambdaScriptAuthority : NativeNvseScriptAuthority
{
    internal abstract NativeNvseSourceObject ParentScript { get; }
    internal abstract NativeNvseLocalContext ParentEventList { get; }
    internal abstract NativeNvseLambdaCaptureAuthority CaptureAuthority { get; }
}

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<uint, NativeNvseLambdaCapture> _nvseLambdaCaptures = [];
    private readonly List<IDisposable> _nvseLambdaPendingValues = [];
    internal void BindNvseLambdaCapture(NativeNvsePlugin plugin, NativeNvseSourceObject script)
    {
        VerifyNvseSourceObject(plugin, script); RequireNvseEmptyCall();
        if (script.Authority is not NativeNvseLambdaScriptAuthority source || script.Class != NativeNvseSourceClass.Script ||
            _nvseLambdaCaptures.ContainsKey(script.Address))
            throw new InvalidDataException("Lambda binding needs a genuine generated Script constructor and independent parent event list.");
        VerifyNvseSourceObject(plugin, source.ParentScript); VerifyNvseLocalContext(plugin, source.ParentEventList);
        if (!ReferenceEquals(source.ParentEventList.Script, source.ParentScript) || source.ParentScript.Class != NativeNvseSourceClass.Script ||
            !ReferenceEquals(source.ParentEventList.Authority.ValueStoreIdentity, _nvseValues?.StoreIdentity))
            throw new InvalidDataException("Lambda parent Script/event-list/campaign value authorities differ.");
        source.CaptureAuthority.RequireCurrent();
        if (string.IsNullOrWhiteSpace(source.CaptureAuthority.SourceOwner)) throw new InvalidDataException("Lambda capture lacks its source creation owner.");
        _nvseLambdaCaptures.Add(script.Address, new(script, source.ParentEventList, source.CaptureAuthority));
    }
    private bool DataLambda(NativeNvsePlugin plugin, uint pointer, NativeNvseDataCall call)
    {
        if (pointer == 0) return false;
        var script = _nvseSourceObjects.Values.SingleOrDefault(value => value.Address == pointer)
            ?? throw new NotSupportedException("Lambda API pointer has no genuine current Script projection; original function construction remains unbound.");
        VerifyNvseSourceObject(plugin, script);
        if (script.Class != NativeNvseSourceClass.Script) throw new InvalidDataException("Lambda API pointer is not a Script.");
        if (!_nvseLambdaCaptures.TryGetValue(pointer, out var root))
        {
            if (script.Authority is NativeNvseLambdaScriptAuthority)
                throw new InvalidOperationException("Generated Script has no admitted parent-local capture lifetime.");
            // Public xNVSE GetLambdaContext finds no context for ordinary SCPT.
            // Save/Unsave are no-ops for that genuine non-lambda identity.
            return false;
        }
        root.Authority.RequireCurrent();
        if (call == NativeNvseDataCall.IsLambda) return true;
        if (call == NativeNvseDataCall.LambdaSave)
        {
            var captures = new List<NativeNvseLambdaCapture>();
            Collect(root, []);
            var retained = new List<IDisposable>();
            try
            {
                foreach (var value in captures)
                {
                    VerifyNvseSourceObject(plugin, value.Script); VerifyNvseLocalContext(plugin, value.Context); value.Authority.RequireCurrent();
                    var values = value.Authority.RetainValues(); retained.Add(values); _nvseLambdaPendingValues.Add(values);
                }
                foreach (var group in captures.GroupBy(value => value))
                    if (group.Key.Saves > int.MaxValue - group.Count()) throw new OverflowException("Lambda save count is exhausted.");
                foreach (var group in captures.GroupBy(value => value.Context))
                    if (group.Key.Retainers > int.MaxValue - group.Count()) throw new OverflowException("Event-list retention count is exhausted.");
                foreach (var group in captures.GroupBy(value => value.Script))
                    if (group.Key.Dependents > int.MaxValue - group.Count()) throw new OverflowException("Script dependency count is exhausted.");
                var lease = new NativeNvseLambdaSaveLease(captures.ToArray(), new LambdaValueGroup(retained));
                root.Leases.EnsureCapacity(checked(root.Leases.Count + 1));
                foreach (var value in captures) { ++value.Saves; ++value.Context.Retainers; ++value.Script.Dependents; }
                root.Leases.Push(lease);
                foreach (var values in retained) _nvseLambdaPendingValues.Remove(values);
                return true;
            }
            catch (Exception error)
            {
                var failures = new List<Exception>();
                foreach (var values in retained.AsEnumerable().Reverse())
                    try { values.Dispose(); _nvseLambdaPendingValues.Remove(values); } catch (Exception retirement) { failures.Add(retirement); }
                if (failures.Count != 0) throw new AggregateException("Lambda capture and retained value retirement failed.", new[] { error }.Concat(failures));
                throw;
            }
            void Collect(NativeNvseLambdaCapture value, HashSet<uint> path)
            {
                if (path.Count >= NativeMaximumDepth) throw new NotSupportedException("Lambda capture graph exceeds its bounded ownership depth.");
                if (!path.Add(value.Script.Address)) return;
                captures.Add(value);
                // Original capture memoizes the ordered reference resolution on
                // its first save. Later saves retain those exact children; they
                // do not silently resolve a different local value graph.
                value.Children ??= value.Authority.ChildLambdas().Where(child => child.Address != value.Script.Address && !path.Contains(child.Address)).ToArray();
                foreach (var child in value.Children)
                {
                    VerifyNvseSourceObject(plugin, child);
                    if (!_nvseLambdaCaptures.TryGetValue(child.Address, out var owned))
                        throw new NotSupportedException("Captured child lambda has no genuine parent/event-list authority.");
                    Collect(owned, new(path));
                }
            }
        }
        if (root.Leases.Count == 0) throw new InvalidOperationException("Lambda Unsave has no matching retained variable-list request.");
        ReleaseLambdaSave(root); return true;
    }
    private static void ReleaseLambdaSave(NativeNvseLambdaCapture root)
    {
        var lease = root.Leases.Peek();
        if (lease.Captures.Any(value => value.Saves <= 0 || value.Context.Retainers <= 0 || value.Script.Dependents <= 0))
            throw new InvalidOperationException("Lambda variable-list retention is unbalanced.");
        lease.Values.Dispose();
        foreach (var value in lease.Captures) { --value.Saves; --value.Context.Retainers; --value.Script.Dependents; }
        root.Leases.Pop();
    }
    internal void RetireNvseLambdaCapture(NativeNvsePlugin plugin, NativeNvseSourceObject script)
    {
        VerifyNvseSourceObject(plugin, script); RequireNvseEmptyCall();
        if (!_nvseLambdaCaptures.TryGetValue(script.Address, out var capture)) return;
        if (capture.Saves != 0 || capture.Leases.Count != 0) throw new InvalidOperationException("Saved lambda variables still retain the actual Script/event list.");
        _nvseLambdaCaptures.Remove(script.Address);
    }
    private void ClearNvseLambdaCaptures()
    {
        foreach (var capture in _nvseLambdaCaptures.Values)
            while (capture.Leases.Count != 0) ReleaseLambdaSave(capture);
        if (_nvseLambdaCaptures.Values.Any(value => value.Saves != 0)) throw new InvalidOperationException("Lambda capture retains unmatched child variable lists.");
        _nvseLambdaCaptures.Clear();
        foreach (var values in _nvseLambdaPendingValues.ToArray())
        { values.Dispose(); _nvseLambdaPendingValues.Remove(values); }
    }
    private sealed class LambdaValueGroup(IReadOnlyList<IDisposable> values) : IDisposable
    {
        private int _remaining = values.Count;
        public void Dispose()
        {
            // Failed retirement keeps the failing and earlier leases for retry.
            while (_remaining != 0) { values[_remaining - 1].Dispose(); --_remaining; }
        }
    }
}
