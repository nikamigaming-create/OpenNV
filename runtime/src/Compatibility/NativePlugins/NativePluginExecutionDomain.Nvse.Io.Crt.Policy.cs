using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginPrivateIo
{
    private Exception? _ioDisposeFailure;
    private readonly List<(NativePluginCrtProviderSelection Selection, FileStream Lease)> _crtProviderLeases = [];
    private readonly List<NativePluginCrtReceipt> _crtReceipts = [];
    private readonly List<NativePluginCrtReceipt> _crtFailures = [];
    internal IReadOnlyList<NativePluginCrtProviderSelection> CrtProviders => _crtProviderLeases.Select(row => row.Selection).ToArray();
    internal IReadOnlyList<NativePluginCrtReceipt> CrtReceipts => _crtReceipts.AsReadOnly();
    private void InitializeCrtProviders()
    {
        foreach (var selected in Selection.CrtProviders ?? throw new InvalidDataException("CRT selection was not derived from the actual original module."))
        {
            if (selected is null || string.IsNullOrWhiteSpace(selected.SourceOwner) || selected.Sha256.Length != 64 ||
                !selected.Sha256.All(Uri.IsHexDigit) || selected.Imports.Count == 0 || selected.Imports.Any(pair => pair.Value.Count == 0 ||
                    !(NativePluginCrtImports.StreamLibraries.Contains(pair.Key) || NativePluginCrtImports.DirectoryLibraries.Contains(pair.Key)) ||
                    pair.Value.Any(name => !NativePluginCrtImports.Owned.Contains(name))) ||
                _crtProviderLeases.Any(row => StringComparer.OrdinalIgnoreCase.Equals(Canonical(row.Selection.Path), Canonical(selected.Path))))
                throw new InvalidDataException("CRT provider has an absent/duplicate source identity or unowned callable declaration.");
            NoReparse(selected.Path); var input = new FileStream(selected.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
                if (pe.PEHeaders.CoffHeader.Machine != Machine.I386)
                    throw new InvalidDataException("CRT export provider is not the selected x86 source.");
                input.Position = 0;
                if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(input)), selected.Sha256))
                    throw new InvalidDataException("CRT export provider changed source architecture/hash before process entry.");
                _crtProviderLeases.Add((selected, input));
            }
            catch { input.Dispose(); throw; }
        }
    }
    internal NativePluginCrtProviderSelection CrtProvider(string path, string sha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _crtProviderLeases.Select(row => row.Selection).SingleOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(Canonical(row.Path), Canonical(path)) &&
            StringComparer.OrdinalIgnoreCase.Equals(row.Sha256, sha)) ?? throw new InvalidDataException("Actual CRT provider is not the exact retained selected source.");
    }
    internal string CrtAbsentRead(NativePluginIoRoute route)
    {
        if (route.Api != 1 || route.Action != NativePluginIoAction.Read || route.PhysicalPath is not null ||
            route.Error != 2 || !_routes.TryGetValue(route.Id, out var pending) || pending != route)
            throw new InvalidDataException("CRT source absence has no exact pending selected read decision.");
        var directory = Path.Combine(ModuleRoot, ".source-absent");
        NoReparse(directory); Directory.CreateDirectory(directory); NoReparse(directory);
        var identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(route.VirtualPath.ToUpperInvariant())));
        var target = Path.Combine(directory, identity); NoReparse(target);
        if (File.Exists(target) || Directory.Exists(target))
            throw new InvalidDataException("Private CRT source-absence namespace contains an unexpected file.");
        return target;
    }
    internal void CompleteCrt(ulong parent, ulong routeId, uint api, bool success)
    {
        if (!_routes.TryGetValue(routeId, out var route) || route.Api != api || api is not (1 or 5))
            throw new InvalidDataException("CRT result changed its actual pending path/API decision.");
        _routes.Remove(routeId);
        if (!success) return;
        if (api == 1) _fileRoutes.Add(routeId, route);
        if (route.Role is not null) { _deleted.Remove(route.VirtualPath); PersistDeletions(); }
    }
    internal void RecordCrt(NativePluginCrtReceipt receipt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (receipt.Generation != _generation || receipt.Provider == 0 || string.IsNullOrWhiteSpace(receipt.DeclarationOwner) ||
            !Enum.IsDefined(receipt.Operation)) throw new InvalidDataException("CRT receipt has a foreign generation/provider/callable owner.");
        if (receipt.StreamRetired && !_fileRoutes.Remove(receipt.Route))
            throw new InvalidDataException("CRT close retired an absent/repeated route lifetime.");
        _crtReceipts.Add(receipt with { Sequence = checked(++_sequence) });
        // fclose invalidates its FILE even when a flush fails. Preserve the
        // failure and never retry that pointer or relabel it as a clean close.
        if (receipt.Operation == NativePluginCrtOperation.Close && receipt.Result != 0) _crtFailures.Add(receipt);
    }
    internal void RequireCrtRetired()
    {
        if (_crtFailures.Count != 0) throw new InvalidDataException("Original CRT retirement retains actual failed close receipts.");
        foreach (var row in _crtProviderLeases)
        {
            row.Lease.Position = 0;
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(row.Lease)), row.Selection.Sha256))
                throw new InvalidDataException("The retained original CRT export provider changed before retirement.");
        }
    }
    private void DisposeCrtProviderLeases()
    {
        var failures = new List<Exception>();
        foreach (var row in _crtProviderLeases.ToArray())
        {
            try { row.Lease.Dispose(); _crtProviderLeases.Remove(row); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("CRT source-provider leases did not all retire.", failures);
    }
}
