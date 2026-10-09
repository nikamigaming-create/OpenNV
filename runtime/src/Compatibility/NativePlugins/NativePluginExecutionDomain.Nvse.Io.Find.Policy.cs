namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    internal NativePluginFindSearch FindBegin(ulong parent, string input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = input.Replace('/', '\\');
        var separator = normalized.LastIndexOf('\\');
        var name = separator < 0 ? normalized : normalized[(separator + 1)..];
        if (name.Length == 0 || name.IndexOfAny(['\0', ':']) >= 0 || name[^1] is '.' or ' ')
            throw new NotSupportedException("Native find pattern has an unowned empty/device/trailing alias.");
        var directory = separator < 0 ? CurrentDirectory : Virtual(normalized[..(separator + 1)]);
        if (directory.IndexOfAny(['*', '?']) >= 0)
            throw new NotSupportedException("Native wildcard directory traversal has no selected namespace owner.");
        var resolve = Selection.ResolveWinningDirectory ?? throw new NotSupportedException("Native directory iterator has no exact source namespace producer.");
        var selected = resolve(directory);
        if (selected is null || selected.Sources is null || string.IsNullOrWhiteSpace(selected.SourceOwner) || selected.VerifyCurrent is null || selected.ResolveWinningDirectory is null)
            throw new InvalidDataException("Native directory selection has absent source/winner/current owners.");
        selected.VerifyCurrent(); var sources = new List<NativePluginFindSource>();
        foreach (var scope in _scopes.OrderByDescending(scope => scope.Virtual.Length))
        {
            if (scope.Source.Directory && Within(scope.Virtual, directory))
            {
                var physical = Target(scope, directory);
                NoReparse(physical); if (Directory.Exists(physical)) sources.Add(new(physical, scope.Source.DeclarationOwner));
            }
            else if (!scope.Source.Directory && StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(scope.Virtual), directory) && File.Exists(scope.Private))
            {
                NoReparse(scope.Private);
                sources.Add(new(Path.GetDirectoryName(scope.Private)!, scope.Source.DeclarationOwner, Path.GetFileName(scope.Virtual)));
            }
        }
        foreach (var source in selected.Sources)
        {
            var physical = Canonical(source.PhysicalDirectory); NoReparse(physical);
            if (!Selection.OriginalRoots.Any(root => Within(root, physical)) || string.IsNullOrWhiteSpace(source.SourceOwner) || !Directory.Exists(physical))
                throw new InvalidDataException("Native find source has no actual readonly selected directory.");
            sources.Add(new(physical, source.SourceOwner));
        }
        if (sources.Count == 0)
        {
            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
            var absent = Path.Combine(ModuleRoot, "AbsentEnumeration", identity); NoReparse(absent);
            if (File.Exists(absent) || Directory.Exists(absent)) throw new InvalidDataException("Source-absent directory collides with private content.");
            sources.Add(new(absent, selected.SourceOwner, Absent: true));
        }
        var id = checked(++_sequence); var search = new NativePluginFindSearch(id, directory, name, selected, sources);
        _findSearches.Add(id, search);
        _findReceipts.Add(new(id, _generation ?? throw new InvalidOperationException("Native find lacks its generation."), parent,
            id, 0, 1, 0, 0, input, selected.SourceOwner, null, null, null, null));
        return search;
    }
    private NativePluginFindSearch Find(ulong id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _findSearches.TryGetValue(id, out var search) && !search.Closed ? search :
            throw new InvalidDataException("Native directory iterator belongs to a foreign/closed search lifetime.");
    }
    internal void FindBackend(ulong parent, ulong id, uint sourceIndex, uint operation, uint handle, uint result, uint last)
    {
        var search = Find(id);
        if (sourceIndex >= search.Sources.Count || operation is not (1 or 2) || result > 1 ||
            operation == 1 && (result == 0) != (handle == uint.MaxValue) ||
            result == 1 && (handle is 0 or uint.MaxValue) || operation == 2 &&
            (!search.Handles.TryGetValue(handle, out var current) || current != sourceIndex))
            throw new InvalidDataException("Native directory backend result changed its actual source/API/handle lifetime.");
        if (operation == 1 && result == 1)
        {
            if (search.Handles.ContainsKey(handle) || _findSearches.Values.Any(other => !ReferenceEquals(other, search) && other.Handles.ContainsKey(handle)))
                throw new InvalidDataException("Native directory backend repeats an actual live OS handle.");
            search.Handles.Add(handle, sourceIndex);
        }
        _findReceipts.Add(new(checked(++_sequence), _generation!.Value, parent, id, handle, 5, result, last,
            search.Pattern, search.Sources[checked((int)sourceIndex)].SourceOwner + ":backend-" + operation,
            null, null, null, null));
        search.Selection.VerifyCurrent();
    }
    internal bool FindCandidate(ulong parent, ulong id, uint sourceIndex, uint handle, bool wide, NativePluginFindData data)
    {
        var search = Find(id);
        if (sourceIndex >= search.Sources.Count || handle is 0 or uint.MaxValue || search.Pending is not null ||
            data.Name.Length == 0 || data.Name.IndexOfAny(['\0', '\\', '/', ':']) >= 0 ||
            data.AlternateName.IndexOfAny(['\0', '\\', '/', ':']) >= 0 || data.Name.Length >= 260 || data.AlternateName.Length >= 14)
            throw new InvalidDataException("Native directory candidate lost its actual source/handle/name/output extent.");
        var source = search.Sources[checked((int)sourceIndex)];
        var observation = _findReceipts.Count;
        _findReceipts.Add(new(checked(++_sequence), _generation!.Value, parent, id, handle, 2, 0, 0,
            search.Pattern, source.SourceOwner, data, null, null, null));
        search.Selection.VerifyCurrent();
        // Win32 search handles admit either A/W next-call ABI. Each output uses
        // that actual OS function rather than a first-party name conversion.
        search.Wide = wide;
        if (search.Handles.TryGetValue(handle, out var previous) && previous != sourceIndex ||
            _findSearches.Values.Any(other => !ReferenceEquals(search, other) && other.Handles.ContainsKey(handle)))
            throw new InvalidDataException("Actual native find handle aliases another source/search lifetime.");
        search.Handles.TryAdd(handle, sourceIndex);
        if (source.Absent) throw new InvalidDataException("Source-absent native search unexpectedly produced a file.");
        var virtualPath = Virtual(Path.GetFullPath(data.Name, search.Directory));
        var physical = Canonical(Path.GetFullPath(data.Name, source.PhysicalDirectory)); NoReparse(physical);
        var admitted = source.OnlyEntry is null || StringComparer.OrdinalIgnoreCase.Equals(source.OnlyEntry, data.Name);
        string? winner = null;
        if (admitted && !_deleted.Contains(virtualPath))
        {
            var scope = _scopes.Where(scope => StringComparer.OrdinalIgnoreCase.Equals(scope.Virtual, virtualPath) || scope.Source.Directory && Within(scope.Virtual, virtualPath))
                .OrderByDescending(scope => scope.Virtual.Length).FirstOrDefault();
            var target = scope.Source is null ? null : Target(scope, virtualPath);
            if (target is not null && (File.Exists(target) || Directory.Exists(target))) winner = target;
            else if ((data.Attributes & 0x10) != 0) winner = search.Selection.ResolveWinningDirectory(virtualPath);
            else if (Selection.ResolveWinningRead(virtualPath) is { } read) winner = LeaseWinner(virtualPath, read);
            else throw new NotSupportedException("Actual native directory file has no exact selected source/read winner: " + virtualPath);
            if (winner is null) throw new NotSupportedException("Actual native directory entry has no complete selected directory winner: " + virtualPath);
            admitted = StringComparer.OrdinalIgnoreCase.Equals(Canonical(winner), physical);
            if (admitted && (((data.Attributes & 0x10) != 0) != Directory.Exists(physical) ||
                (data.Attributes & 0x400) != 0)) throw new InvalidDataException("Native directory entry changed its actual class/reparse provenance.");
        }
        else admitted = false;
        if (admitted && !search.Produced.Add(virtualPath)) admitted = false;
        if (admitted) search.Pending = data;
        _findReceipts[observation] = _findReceipts[observation] with { Result = admitted ? 1U : 0U, VirtualPath = virtualPath,
            PhysicalPath = physical, Winner = admitted };
        return admitted;
    }
    internal void FindResult(ulong parent, ulong id, uint operation, uint handle, uint result, uint last)
    {
        var search = Find(id);
        if (operation is not (1 or 2) || result > 1 || result == 1 && (search.Pending is null || !search.Handles.ContainsKey(handle)) ||
            result == 0 && search.Pending is not null || operation == 2 && search.PublicHandle != handle ||
            operation == 1 && search.PublicHandle is not null || operation == 1 && result == 0 && (handle != uint.MaxValue || search.Handles.Count != 0))
            throw new InvalidDataException("Actual find result lost its produced/delivered/public native handle lifetime.");
        var data = search.Pending; search.Pending = null;
        if (operation == 1 && result == 1) search.PublicHandle = handle;
        if (operation == 1 && result == 0) { search.Closed = true; _findSearches.Remove(id); }
        _findReceipts.Add(new(checked(++_sequence), _generation!.Value, parent, id, handle, 3, result, last,
            search.Pattern, search.Selection.SourceOwner, data, null, null, null));
        search.Selection.VerifyCurrent();
    }
    internal void FindClosed(ulong parent, ulong id, uint publicHandle, IReadOnlyList<(uint Handle, uint Result, uint Error)> handles)
    {
        var search = Find(id);
        var constructing = search.PublicHandle is null && publicHandle == uint.MaxValue;
        if (!constructing && search.PublicHandle != publicHandle || search.Pending is not null || handles.Count == 0 ||
            handles.Select(handle => handle.Handle).Distinct().Count() != handles.Count ||
            handles.Any(handle => handle.Result > 1 || !search.Handles.ContainsKey(handle.Handle)))
            throw new InvalidDataException("Native find close has no exact backend/public search lifetime.");
        foreach (var handle in handles)
        {
            var receipt = new NativePluginFindReceipt(checked(++_sequence), _generation!.Value, parent, id, handle.Handle, 4,
                handle.Result, handle.Error, search.Pattern, search.Selection.SourceOwner, null, null, null, null);
            _findReceipts.Add(receipt);
            if (handle.Result == 1) search.Handles.Remove(handle.Handle); else _findCloseFailures.Add(receipt);
        }
        if (search.Handles.Count == 0 && !constructing) { search.Closed = true; _findSearches.Remove(id); }
        search.Selection.VerifyCurrent();
    }
    internal void RequireFindRetired()
    {
        if (_findSearches.Count != 0 || _findCloseFailures.Count != 0)
            throw new InvalidDataException("Actual native directory search/close lifetimes remain unretired.");
    }
}
