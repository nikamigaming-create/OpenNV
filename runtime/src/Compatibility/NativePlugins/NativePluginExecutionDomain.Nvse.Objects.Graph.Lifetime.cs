namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal void RefreshNvseSourceGraph(NativeNvsePlugin plugin, NativeNvseSourceGraph graph)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); RequireNvseGraphIdentity(plugin, graph);
        var replacements = new List<(NativeNvseSourceObject Object, byte[] Before, byte[] After, string Hash)>();
        foreach (var value in graph.Objects.Values)
        {
            value.Authority.RequireCurrent(); VerifyGuest(value.Image); VerifyGuest(value.Metadata);
            var before = graph.NativeImages[value.Id];
            if (!ReadNvseGraphBytes(value.Image).AsSpan().SequenceEqual(before) ||
                !ReadNvseGraphBytes(value.Metadata).AsSpan().SequenceEqual(graph.NativeMetadata[value.Id]))
                throw Fatal(new InvalidDataException("Native graph bytes changed outside their authoritative publication owner."));
            var (after, _, dependencies, hash) = ComposeNvseGraphObject(value);
            if (after.Length != before.Length || !dependencies.Distinct().OrderBy(row => row.Id).SequenceEqual(value.Dependencies.OrderBy(row => row.Id)))
                throw new NotSupportedException("Native graph topology changed; a new genuine linked lifetime owner is required.");
            // The original class table remains the same first-party table.
            if (value.Class != NativeNvseSourceClass.ModInfo) before.AsSpan(0, 4).CopyTo(after);
            if (value.Class == NativeNvseSourceClass.Quest)
                foreach (var at in new[] { 24, 36, 48 }) before.AsSpan(at, 4).CopyTo(after.AsSpan(at));
            if (value.Authority is NativeNvseScriptAuthority script)
            {
                var metadata = new NativeNvseScriptMetadata(script.Read()); var current = metadata.Bytes;
                metadata.Relocate(current, value.Metadata.Address);
                if (!current.AsSpan().SequenceEqual(graph.NativeMetadata[value.Id]))
                    throw new NotSupportedException("Native Script metadata changed without its actual metadata mutation owner.");
                var body = script.Read().Code;
                if (value.Code is { } code ? !ReadNvseGraphBytes(code).AsSpan().SequenceEqual(body.Span) : !body.IsEmpty)
                    throw Fatal(new InvalidDataException("Native graph original code body changed."));
            }
            else if (!((NativeNvseGraphDataAuthority)value.Authority).Metadata(value.Metadata.Address).Bytes.AsSpan()
                .SequenceEqual(graph.NativeMetadata[value.Id]))
                throw new NotSupportedException("Native class metadata changed without its actual metadata mutation owner.");
            replacements.Add((value, before, after, hash));
        }
        try
        {
            foreach (var (value, before, after, hash) in replacements)
            {
                if (!before.AsSpan().SequenceEqual(after))
                {
                    if (after.Length > (MaximumPayload - 64) / 2)
                        throw new NotSupportedException("Native class atomic refresh exceeds its complete transport extent.");
                    using var reader = Exchange(NativePluginDomainOperation.NvseObjectRefresh, Payload(writer =>
                    {
                        writer.Write(plugin.Module); writer.Write(value.Id); writer.Write(checked((uint)before.Length));
                        writer.Write(before); writer.Write(after);
                    }));
                    if (reader.ReadUInt64() != value.Id || reader.ReadUInt32() != value.Address || reader.ReadUInt32() != value.Image.Length)
                        throw new InvalidDataException("Native class refresh changed its actual identity or extent.");
                    Finish(reader);
                }
                graph.NativeImages[value.Id] = after; value.PublishedSha256 = hash;
            }
            foreach (var value in graph.Objects.Values) VerifyNvseSourceObject(plugin, value);
        }
        catch (Exception error) { throw Fatal(error); }
    }

    internal void RetireNvseSourceGraph(NativeNvsePlugin plugin, NativeNvseSourceGraph graph)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); RequireNvseGraphIdentity(plugin, graph);
        foreach (var value in graph.Objects.Values)
        {
            var incoming = graph.Objects.Values.Count(source => source.Dependencies.Contains(value));
            if (value.Calls != 0 || value.Dependents != incoming || _nvseLocals.Values.Any(local => ReferenceEquals(local.Script, value)))
                throw new InvalidOperationException("A native caller, event list or external object still owns this cyclic source graph.");
        }
        try
        {
            // All native class registrations retire before any linked image is
            // decommitted. An error terminally retires the domain; no partial
            // group can be reused as an intact source graph.
            foreach (var value in graph.Objects.Values)
            {
                using var reader = Exchange(NativePluginDomainOperation.NvseObjectRetire,
                    Payload(writer => { writer.Write(plugin.Module); writer.Write(value.Id); }));
                if (reader.ReadUInt32() != 1) throw new InvalidDataException("Native cyclic object registration did not retire.");
                Finish(reader);
            }
            foreach (var value in graph.Objects.Values)
            {
                ReleaseGuest(value.Image); ReleaseGuest(value.Metadata); if (value.Code is { } code) ReleaseGuest(code);
                foreach (var dependency in value.Dependencies) --dependency.Dependents;
                _nvseSourceObjects.Remove(value.Id); _nvseSourceGraphs.Remove(value.Id);
                value.Retired = true; value.SourceLease.Dispose();
            }
            graph.Retired = true; graph.Context.Retire();
        }
        catch (Exception error) { throw Fatal(error); }
    }

    private void RequireNvseGraphIdentity(NativeNvsePlugin plugin, NativeNvseSourceGraph graph)
    {
        if (graph.Generation != Generation || graph.Module != plugin.Module || graph.Retired ||
            graph.Objects.Values.Any(value => value.Staged || value.Retired || !_nvseSourceGraphs.TryGetValue(value.Id, out var owner) ||
                !ReferenceEquals(owner, graph) || !_nvseSourceObjects.TryGetValue(value.Id, out var current) || !ReferenceEquals(current, value)))
            throw new InvalidOperationException("Native cyclic graph belongs to a foreign, staged or retired generation.");
    }

    private void ClearNvseSourceGraphs()
    {
        foreach (var graph in _nvseSourceGraphs.Values.Distinct())
        { graph.Retired = true; graph.Context.Retire(); }
        _nvseSourceGraphs.Clear();
    }
}
