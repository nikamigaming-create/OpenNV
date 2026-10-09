using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginIoRole : uint { Configuration = 1, State = 2, CoSave = 3, Diagnostic = 4 }
internal enum NativePluginIoAction : uint { Read = 1, Write = 2, Directory = 3, Delete = 4, Attributes = 5, ProfileRead = 6, ProfileWrite = 7 }
internal sealed record NativePluginIoWriteScope(string VirtualPath, bool Directory, NativePluginIoRole Role, string DeclarationOwner);
internal sealed record NativePluginIoReadWinner(string PhysicalPath, string Sha256, string SourceOwner, bool Configuration);
internal sealed record NativePluginIoSelection(string StackSha256, string ModuleSha256, string ModulePath,
    string RuntimeDirectory, string PrivateStateRoot, IReadOnlyList<string> OriginalRoots,
    IReadOnlyList<NativePluginIoWriteScope> WriteScopes, Func<string, NativePluginIoReadWinner?> ResolveWinningRead,
    string ImportDeclarationOwner, IReadOnlySet<string> DeclaredNonIoImports)
{
    // Null derives the exact original import graph. Explicit empty means no
    // provider and never admits an imported file arm through a non-I/O list.
    internal IReadOnlyList<NativePluginCrtProviderSelection>? CrtProviders { get; init; }
    internal Func<string, NativePluginDirectorySelection>? ResolveWinningDirectory { get; init; }
}
internal sealed record NativePluginIoReceipt(ulong Sequence, ulong Generation, ulong Parent, uint Api,
    string VirtualPath, string? PhysicalPath, string? WinnerSha256, NativePluginIoRole? Role, uint Error, uint Transferred = 0);
internal sealed record NativePluginIoRoute(ulong Id, uint Api, NativePluginIoAction Action, string VirtualPath, string? PhysicalPath, uint Error,
    string? WinnerSha256, NativePluginIoRole? Role);

// Selection and source-winner decisions belong to C#. The native adapter only
// opens the exact returned path with the admitted Windows API access mode.
internal sealed partial class NativePluginPrivateIo : IDisposable
{
    internal NativePluginIoSelection Selection { get; }
    internal string ModuleRoot { get; }
    internal string RestrictingSid { get; }
    internal string CurrentDirectory { get; private set; }
    private readonly List<(NativePluginIoWriteScope Source, string Virtual, string Private)> _scopes = [];
    private readonly Dictionary<string, FileStream> _reads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativePluginIoReadWinner> _winners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, NativePluginIoRoute> _routes = [];
    private readonly Dictionary<ulong, NativePluginIoRoute> _fileRoutes = [];
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NativePluginIoReceipt> _receipts = [];
    private readonly string _deletionFile;
    private readonly FileStream _moduleLease;
    private ulong _sequence;
    private ulong? _generation;
    private bool _disposed;
    internal IReadOnlyList<NativePluginIoReceipt> Receipts => _receipts.AsReadOnly();
    internal NativePluginPrivateIo(NativePluginIoSelection selection)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native plugin private I/O requires Windows.");
        ArgumentNullException.ThrowIfNull(selection);
        RequireHash(selection.StackSha256); RequireHash(selection.ModuleSha256);
        if (selection.OriginalRoots is null || selection.OriginalRoots.Count == 0 || selection.WriteScopes is null ||
            selection.ResolveWinningRead is null || string.IsNullOrWhiteSpace(selection.ImportDeclarationOwner) || selection.DeclaredNonIoImports is null)
            throw new InvalidDataException("Native I/O needs the complete source selection, write declarations and import owner.");
        Selection = selection with
        {
            OriginalRoots = selection.OriginalRoots.Select(Canonical).ToArray(),
            WriteScopes = selection.WriteScopes.ToArray(),
            DeclaredNonIoImports = new HashSet<string>(selection.DeclaredNonIoImports, StringComparer.Ordinal),
            CrtProviders = (selection.CrtProviders ?? NativePluginCrtImports.ReadProviders(selection.ModulePath, selection.ModuleSha256))
                .Select(row => row with
                {
                    Imports = row.Imports.ToDictionary(pair => pair.Key.ToLowerInvariant(),
                    pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase)
                }).ToArray()
        };
        if (Selection.DeclaredNonIoImports.Any(declaration => NativePluginIoImports.Unowned.Contains(declaration[(declaration.LastIndexOf('!') + 1)..]) || NativePluginCrtImports.IsFileDeclaration(declaration)))
            throw new NotSupportedException("A file API cannot be admitted as a non-I/O import.");
        CurrentDirectory = Canonical(selection.RuntimeDirectory);
        if (!Selection.OriginalRoots.Any(root => Within(root, Canonical(selection.ModulePath))) ||
            !Selection.OriginalRoots.Any(root => Within(root, CurrentDirectory)))
            throw new InvalidDataException("Native module/runtime has no exact selected input-root ownership.");
        var privateRoot = Canonical(selection.PrivateStateRoot);
        var physicalPrivateRoot = NativePluginIoSecurity.PhysicalPath(privateRoot);
        if (Selection.OriginalRoots.Any(root => Within(root, privateRoot) || Within(privateRoot, root) ||
            Within(NativePluginIoSecurity.PhysicalPath(root), physicalPrivateRoot) || Within(physicalPrivateRoot, NativePluginIoSecurity.PhysicalPath(root))))
            throw new InvalidDataException("Private native state overlaps an original source root.");
        var moduleInstance = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection.ModuleSha256.ToUpperInvariant() + "\0" + Canonical(selection.ModulePath).ToUpperInvariant())));
        // One full digest still owns the complete stack/module/path identity.
        // Repeating three digests as nested directories exceeded the actual
        // Windows process-current-directory limit before native admission.
        var namespaceIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            selection.StackSha256.ToUpperInvariant() + "\0" + moduleInstance)));
        ModuleRoot = Path.Combine(privateRoot, namespaceIdentity);
        RestrictingSid = NativePluginIoSecurity.ModuleSid(selection.StackSha256, moduleInstance);
        NoReparse(privateRoot); Directory.CreateDirectory(ModuleRoot); NoReparse(ModuleRoot);
        NativePluginIoSecurity.RequirePrivateTreeOwned(ModuleRoot);
        NativePluginIoSecurity.ProtectDirectory(ModuleRoot, RestrictingSid);
        foreach (var role in Enum.GetValues<NativePluginIoRole>())
        {
            var directory = Path.Combine(ModuleRoot, role.ToString()); NoReparse(directory); Directory.CreateDirectory(directory);
        }
        for (var index = 0; index < Selection.WriteScopes.Count; ++index)
        {
            var scope = Selection.WriteScopes[index];
            if (scope is null || !Enum.IsDefined(scope.Role) || string.IsNullOrWhiteSpace(scope.DeclarationOwner))
                throw new InvalidDataException("Writable route lacks its original declaration and role.");
            var virtualPath = Virtual(scope.VirtualPath);
            if (!Selection.OriginalRoots.Any(root => Within(root, virtualPath)))
                throw new InvalidDataException("Writable virtual route is outside the selected source roots.");
            if (_scopes.Any(old => StringComparer.OrdinalIgnoreCase.Equals(old.Virtual, virtualPath)))
                throw new InvalidDataException("Writable declarations repeat one virtual identity.");
            var scopeId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Role + "\0" + virtualPath.ToUpperInvariant() + "\0" + scope.Directory)));
            var target = Path.Combine(ModuleRoot, scope.Role.ToString(), scopeId);
            if (!scope.Directory) target = Path.Combine(target, Path.GetFileName(virtualPath));
            _scopes.Add((scope, virtualPath, target));
        }
        _deletionFile = Path.Combine(ModuleRoot, "deleted-paths.json");
        var leasePath = Path.Combine(ModuleRoot, ".owner.lock"); NoReparse(leasePath);
        _moduleLease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            InitializeCrtProviders();
            if (File.Exists(_deletionFile))
            {
                NoReparse(_deletionFile);
                foreach (var value in System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(_deletionFile)) ??
                    throw new InvalidDataException("Private deletion state is null."))
                {
                    var path = Virtual(value); _ = WriteScope(path);
                    if (!_deleted.Add(path)) throw new InvalidDataException("Private deletion state repeats an identity.");
                }
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            try { DisposeCrtProviderLeases(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { _moduleLease.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count != 1) throw new AggregateException("Native I/O construction and retained source cleanup failed.", failures);
            throw;
        }
    }
    internal void Claim(ulong generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generation is not null || generation == 0) throw new InvalidOperationException("Private I/O already belongs to a generation.");
        _generation = generation;
    }
    internal void CheckModule(string path, string hash, string stack)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Canonical(path), Canonical(Selection.ModulePath)) ||
            !StringComparer.OrdinalIgnoreCase.Equals(hash, Selection.ModuleSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(stack, Selection.StackSha256))
            throw new InvalidDataException("Native writable state belongs to another selected module/stack.");
    }
    internal string Virtual(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Contains(':') && !(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' && path.AsSpan(2).IndexOf(':') < 0))
            throw new NotSupportedException("Native path has an unowned device/network/stream identity.");
        var actual = Canonical(Path.GetFullPath(path, CurrentDirectory));
        if (!Selection.OriginalRoots.Any(root => Within(root, actual)))
            throw new NotSupportedException("Native path has no selected source-root owner.");
        return actual;
    }
    internal NativePluginIoRoute Resolve(ulong parent, uint api, NativePluginIoAction action, string declared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!(api == 1 && (action is NativePluginIoAction.Read or NativePluginIoAction.Write) ||
            api == 5 && action == NativePluginIoAction.Directory || api == 6 && action == NativePluginIoAction.Delete ||
            api == 7 && action == NativePluginIoAction.Attributes || api == 8 && action == NativePluginIoAction.ProfileRead ||
            api == 9 && action == NativePluginIoAction.ProfileWrite))
            throw new InvalidDataException("Native route changed its callable API/action contract.");
        var generation = _generation ?? throw new InvalidOperationException("Private I/O has no process generation.");
        var path = Virtual(declared); string? physical = null, hash = null; NativePluginIoRole? role = null; uint error = 0;
        if (action is NativePluginIoAction.ProfileRead or NativePluginIoAction.ProfileWrite) RequireUnmappedProfile(path);
        var scope = _scopes.Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.Virtual, path) || item.Source.Directory && Within(item.Virtual, path))
            .OrderByDescending(item => item.Virtual.Length).FirstOrDefault();
        var writable = scope.Source is not null;
        var target = writable ? Target(scope, path) : null;
        if ((action is NativePluginIoAction.ProfileRead or NativePluginIoAction.ProfileWrite) &&
            scope.Source is { } profileScope && profileScope.Role != NativePluginIoRole.Configuration)
            throw new NotSupportedException("Profile route conflicts with its source-declared private configuration role.");
        if (action is NativePluginIoAction.Read or NativePluginIoAction.Attributes or NativePluginIoAction.ProfileRead)
        {
            if (_deleted.Contains(path)) error = 2;
            else if (target is not null && scope.Source is { } readScope && (File.Exists(target) || Directory.Exists(target)))
            { physical = target; role = readScope.Role; }
            else if (Selection.ResolveWinningRead(path) is { } winner)
            {
                physical = LeaseWinner(path, winner); hash = winner.Sha256;
                if (action == NativePluginIoAction.ProfileRead && !winner.Configuration)
                    throw new NotSupportedException("Profile read winner lacks a declared configuration role.");
            }
            else error = 2; // Genuine selected-source absence is retained as a failed Windows open.
        }
        else
        {
            if (scope.Source is not { } writeScope || target is null)
                throw new NotSupportedException("Native write has no source-declared private destination.");
            physical = target; role = writeScope.Role; NoReparse(physical);
            if (writeScope.Directory)
            {
                if (action == NativePluginIoAction.Directory && !Directory.Exists(physical))
                {
                    if (Directory.Exists(path) || File.Exists(path) || Selection.ResolveWinningRead(path) is not null)
                        throw new NotSupportedException("Existing original directory/file creation semantics require their selected directory owner.");
                }
                var parentDirectory = Path.GetDirectoryName(physical)!;
                if (Within(scope.Private, parentDirectory) && !Directory.Exists(parentDirectory)) error = 3;
                else if (!Within(scope.Private, parentDirectory)) Directory.CreateDirectory(parentDirectory);
            }
            else Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            if ((action is NativePluginIoAction.Write or NativePluginIoAction.ProfileWrite or NativePluginIoAction.Delete) &&
                error == 0 && !File.Exists(physical) && !_deleted.Contains(path))
            {
                if (Selection.ResolveWinningRead(path) is { } winner)
                {
                    if (role != NativePluginIoRole.Configuration || !winner.Configuration)
                        throw new NotSupportedException("Original source content cannot become a private writable asset copy.");
                    var original = LeaseWinner(path, winner); hash = winner.Sha256;
                    if (action == NativePluginIoAction.ProfileWrite) RequireUnmappedProfile(original);
                    using var output = new FileStream(physical, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
                    input.CopyTo(output); output.Flush(true);
                }
            }
        }
        if (action == NativePluginIoAction.ProfileRead && error != 0)
        {
            var absentId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            physical = Path.Combine(ModuleRoot, "AbsentConfiguration", absentId, Path.GetFileName(path)); NoReparse(physical);
            if (File.Exists(physical) || Directory.Exists(physical)) throw new InvalidDataException("Missing profile input collides with retained private content.");
        }
        if ((action is NativePluginIoAction.ProfileRead or NativePluginIoAction.ProfileWrite) && physical is not null)
        {
            RequireUnmappedProfile(physical);
        }
        var id = checked(++_sequence); var route = new NativePluginIoRoute(id, api, action, path, physical, error, hash, role);
        _routes.Add(id, route); _receipts.Add(new(id, generation, parent, api, path, physical, hash, role, error)); return route;
    }
    internal void Complete(ulong parent, ulong routeId, uint api, uint error)
    {
        if (!_routes.TryGetValue(routeId, out var waiting) || waiting.Api != api)
            throw new InvalidDataException("Native file result changed its admitted API/route identity.");
        if (!_routes.Remove(routeId, out var route)) throw new InvalidDataException("Native file result has no live correlated route.");
        if (api == 1 && error == 0) _fileRoutes.Add(routeId, route);
        if (error == 0 && route.Role is not null) { _deleted.Remove(route.VirtualPath); PersistDeletions(); }
        _receipts.Add(new(checked(++_sequence), _generation!.Value, parent, api, route.VirtualPath,
            route.PhysicalPath, route.WinnerSha256, route.Role, error));
    }
    internal void ValidateResult(ulong routeId, uint api, bool writable)
    {
        if (!_routes.TryGetValue(routeId, out var route) || route.Api != api ||
            writable && (api != 1 || route.Action != NativePluginIoAction.Write || route.Role is null))
            throw new InvalidDataException("Native file completion has no exact admitted API/write capability.");
    }
    internal void RecordHandle(ulong parent, ulong id, uint api, uint count, uint error)
    {
        if (!_fileRoutes.TryGetValue(id, out var route)) throw new InvalidDataException("Native file event has no retained open provenance.");
        _receipts.Add(new(checked(++_sequence), _generation!.Value, parent, api, route.VirtualPath,
            route.PhysicalPath, route.WinnerSha256, route.Role, error, count));
        if (api == 2 && error == 0) _fileRoutes.Remove(id);
    }
    internal void Deleted(ulong parent, ulong routeId, uint api, uint error)
    {
        if (!_routes.TryGetValue(routeId, out var route) || route.Role is null) throw new InvalidDataException("Native deletion has no private route.");
        Complete(parent, routeId, api, error);
        if (error == 0) { _deleted.Add(route.VirtualPath); PersistDeletions(); }
    }
    internal void ChangeDirectory(string path)
    {
        var route = Virtual(path);
        // Directory source enumeration needs its own exact winner owner. Existing
        // selected runtime/declared private virtual roots are sufficient here.
        if (!StringComparer.OrdinalIgnoreCase.Equals(route, Canonical(Selection.RuntimeDirectory)) &&
            !_scopes.Any(scope => scope.Source.Directory && StringComparer.OrdinalIgnoreCase.Equals(scope.Virtual, route) && Directory.Exists(scope.Private)))
            throw new NotSupportedException("Native current directory lacks an admitted source-directory owner.");
        CurrentDirectory = route;
    }
    private (NativePluginIoWriteScope Source, string Virtual, string Private) WriteScope(string path) =>
        _scopes.Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.Virtual, path) || item.Source.Directory && Within(item.Virtual, path))
            .OrderByDescending(item => item.Virtual.Length).FirstOrDefault() is var scope && scope.Source is not null
                ? scope : throw new NotSupportedException("Private deletion has no declared writable source route.");
    private static string Target((NativePluginIoWriteScope Source, string Virtual, string Private) scope, string path)
    {
        var target = scope.Source.Directory ? Path.GetFullPath(Path.GetRelativePath(scope.Virtual, path), scope.Private) : scope.Private;
        if (!Within(scope.Private, target)) throw new InvalidDataException("Private route escaped its declared destination.");
        NoReparse(target); return target;
    }
    private string LeaseWinner(string path, NativePluginIoReadWinner winner)
    {
        RequireHash(winner.Sha256);
        var physical = Canonical(winner.PhysicalPath); NoReparse(physical);
        if (string.IsNullOrWhiteSpace(winner.SourceOwner) || !Selection.OriginalRoots.Any(root => Within(root, physical)))
            throw new InvalidDataException("Native read winner lacks exact original source-root provenance.");
        if (_winners.TryGetValue(path, out var original))
        {
            if (original != winner) throw new InvalidDataException("Native read winner changed during the process generation.");
            return physical;
        }
        var lease = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(winner.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native original read winner changed before admission.");
            lease.Position = 0; _reads.Add(path, lease); _winners.Add(path, winner); return physical;
        }
        catch { lease.Dispose(); throw; }
    }
    private void PersistDeletions()
    {
        NoReparse(_deletionFile); var temporary = _deletionFile + ".new";
        NoReparse(temporary); File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(_deleted.Order(StringComparer.OrdinalIgnoreCase)));
        File.Move(temporary, _deletionFile, true);
    }
    internal void RequireRetired()
    {
        if (_routes.Count != 0 || _fileRoutes.Count != 0) throw new InvalidDataException("Native private I/O retains unfinished route/file results.");
        foreach (var (path, lease) in _reads)
        {
            lease.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(lease)).Equals(_winners[path].Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original native input changed before generation retirement.");
        }
    }
    public void Dispose()
    {
        if (_disposed) { if (_ioDisposeFailure is not null) throw _ioDisposeFailure; return; }
        var failures = new List<Exception>();
        try { DisposeCrtProviderLeases(); } catch (Exception error) { failures.Add(error); }
        foreach (var lease in _reads.Values) try { lease.Dispose(); } catch (Exception error) { failures.Add(error); }
        _reads.Clear(); _winners.Clear();
        try { _moduleLease.Dispose(); } catch (Exception error) { failures.Add(error); }
        _disposed = true;
        if (failures.Count != 0)
        {
            var error = new AggregateException("Native I/O source leases did not all retire.", failures);
            _ioDisposeFailure = error; throw error;
        }
    }
    internal static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf('\0') >= 0 || path.AsSpan(2).IndexOf(':') >= 0)
            throw new InvalidDataException("Native source/private path needs a fully qualified local identity.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var part in full[Path.GetPathRoot(full)!.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.None))
        {
            if (part.Length == 0) continue;
            if (part[^1] is '.' or ' ') throw new NotSupportedException("Native trailing-dot/space path alias is unowned.");
            var device = part.Split('.')[0].ToUpperInvariant();
            if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
                (device[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'))
                throw new NotSupportedException("Native DOS device path has no selected file owner.");
        }
        return full;
    }
    internal static bool Within(string root, string path) => StringComparer.OrdinalIgnoreCase.Equals(root, path) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void NoReparse(string path)
    {
        for (var at = path; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
            if ((File.Exists(at) || Directory.Exists(at)) && (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Native I/O reparse ownership is unbound.");
    }
    private static void RequireHash(string hash)
    {
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Native private I/O needs exact source SHA256 identities.");
    }
    private static void RequireUnmappedProfile(string physical)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native profile mapping requires Windows.");
        using var mapping = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\IniFileMapping\\" + Path.GetFileName(physical));
        if (mapping is not null) throw new NotSupportedException("Profile registry mapping has no admitted selected configuration owner.");
    }
}
