namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginIoMove(NativePluginIoRoute Source, NativePluginIoRoute Destination, uint Flags);
internal sealed record NativePluginIoMoveReceipt(ulong Sequence, ulong Generation, ulong Parent,
    ulong SourceRoute, ulong DestinationRoute, string Source, string Destination, uint Flags,
    bool Returned, uint LastError, bool SourceRemoved);

internal sealed partial class NativePluginPrivateIo
{
    internal const uint MoveFileApi = 13;
    private const uint MoveReplaceExisting = 1, MoveCopyAllowed = 2, MoveWriteThrough = 8, MoveFailIfNotTrackable = 32;
    private const uint ImmediateMoveFlags = MoveReplaceExisting | MoveCopyAllowed | MoveWriteThrough | MoveFailIfNotTrackable;
    private readonly Dictionary<ulong, NativePluginIoMove> _moves = [];
    private readonly List<NativePluginIoMoveReceipt> _moveReceipts = [];
    internal IReadOnlyList<NativePluginIoMoveReceipt> MoveReceipts => _moveReceipts.AsReadOnly();

    internal NativePluginIoMove ResolveMove(ulong parent, string source, string destination, uint flags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((flags & ~ImmediateMoveFlags) != 0)
            throw new NotSupportedException("Delayed, hard-link or unknown MoveFileEx flags have no private lifetime owner.");
        var from = Virtual(source); var to = Virtual(destination);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to)))
            throw new NotSupportedException("Cross-volume MoveFileEx copy/delete outcomes have no source-volume owner.");

        // Both names must be admissible before a winner is copied or either
        // route is published. Owned game assets are never writable sources.
        PreflightMovePath(from); PreflightMovePath(to);
        var sourceRoute = Resolve(parent, MoveFileApi, NativePluginIoAction.MoveSource, from);
        var destinationRoute = Resolve(parent, MoveFileApi, NativePluginIoAction.MoveDestination, to);
        var move = new NativePluginIoMove(sourceRoute, destinationRoute, flags);
        if (!_moves.TryAdd(sourceRoute.Id, move)) throw new InvalidDataException("Native move repeats a retained source route.");
        return move;
    }

    private void PreflightMovePath(string path)
    {
        var scope = WriteScope(path); var target = Target(scope, path);
        NoReparse(path);
        if (Directory.Exists(target) || Directory.Exists(path))
            throw new NotSupportedException("Directory MoveFileEx requires a complete descendant winner/tombstone owner.");
        if (!File.Exists(target) && !_deleted.Contains(path) && Selection.ResolveWinningRead(path) is { } winner &&
            !(scope.Source.Role == NativePluginIoRole.Configuration && winner.Configuration && !winner.Diagnostic ||
                scope.Source.Role == NativePluginIoRole.Diagnostic && winner.Diagnostic && !winner.Configuration))
            throw new NotSupportedException("An original asset cannot enter a writable MoveFileEx route.");
    }

    internal void CompleteMove(ulong parent, ulong sourceId, ulong destinationId, uint flags, bool returned, uint lastError)
    {
        if (!_moves.TryGetValue(sourceId, out var move) || move.Destination.Id != destinationId || move.Flags != flags ||
            !_routes.TryGetValue(sourceId, out var from) || from != move.Source ||
            !_routes.TryGetValue(destinationId, out var to) || to != move.Destination)
            throw new InvalidDataException("Native move result has no exact paired route/flags lifetime.");
        if (!returned && lastError == 0)
            throw new InvalidDataException("Failed native MoveFileEx lost its actual error.");
        var sourceRemoved = returned && !File.Exists(from.PhysicalPath);
        if (returned && (!File.Exists(to.PhysicalPath) || from.Error != 0 || to.Error != 0))
            throw new InvalidDataException("Successful native MoveFileEx has no actual admitted destination file.");
        _moves.Remove(sourceId); _routes.Remove(sourceId); _routes.Remove(destinationId);
        var error = returned ? 0U : lastError;
        if (returned)
        {
            if (sourceRemoved) _deleted.Add(from.VirtualPath); else _deleted.Remove(from.VirtualPath);
            _deleted.Remove(to.VirtualPath); PersistDeletions();
        }
        var generation = _generation ?? throw new InvalidOperationException("Native move completion lost its actual generation.");
        foreach (var route in new[] { from, to })
            _receipts.Add(new(checked(++_sequence), generation, parent, MoveFileApi, route.VirtualPath,
                route.PhysicalPath, route.WinnerSha256, route.Role, error));
        _moveReceipts.Add(new(checked(++_sequence), generation, parent, sourceId, destinationId,
            from.VirtualPath, to.VirtualPath, flags, returned, lastError, sourceRemoved));
    }
}

internal sealed partial class NativePluginExecutionDomain
{
    private const uint MoveFileCallback = 30;
    private byte[] DispatchPrivateMoveFile(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var operation = reader.ReadUInt32();
        if (operation == 1)
        {
            var source = ReadText(reader); var destination = ReadText(reader); var flags = reader.ReadUInt32(); Finish(reader);
            var move = owner.ResolveMove(parent, source, destination, flags);
            return Payload(writer =>
            {
                foreach (var route in new[] { move.Source, move.Destination })
                {
                    writer.Write(route.Id); writer.Write(route.Error); WriteText(writer, route.PhysicalPath ?? string.Empty);
                }
            });
        }
        if (operation == 2)
        {
            var source = reader.ReadUInt64(); var destination = reader.ReadUInt64(); var flags = reader.ReadUInt32();
            var returned = reader.ReadUInt32(); var error = reader.ReadUInt32(); Finish(reader);
            if (returned > 1) throw new InvalidDataException("Native MoveFileEx result has an invalid BOOL declaration.");
            owner.CompleteMove(parent, source, destination, flags, returned != 0, error);
            return Payload(writer => writer.Write(1U));
        }
        throw new InvalidDataException("Native MoveFileEx callback has an undeclared operation.");
    }
}
