using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeModuleDiagnosticContracts
{
    internal static void RunOwnedSource(string path)
    {
        var image = NativePluginImageDeclarations.Read(path, dll: true); var stack = new string('A', 64);
        var constructor = NativePluginModuleConstructionSource.Read(image, stack);
        var source = NativePluginModuleDiagnosticCrtSource.Read(image, stack, constructor) ?? throw new InvalidDataException("Selected source diagnostic producer is absent.");
        Require(source.Mode == "w" && source.Sharing == 0x20 && source.RetryLimit == 5 && source.StreamRva != 0 &&
            source.FirstReturnRva != source.RetryReturnRva, "actual declared producer/global/mode/share/retry relationships");
        Require(source.AttemptName(0) == source.Name && source.AttemptName(1) == source.Name + "0" && source.AttemptName(5) == source.Name + "4",
            "source first path and five ordered fallback spellings");
        Reject<NotSupportedException>(() => source.AttemptName(6));
        Reject<InvalidDataException>(() => source.Check(image, new string('B', 64)));
        Reject<InvalidDataException>(() => source.Check(image with { Imports = [image.Imports[0] with { SlotRva = 1 }] }, stack));
        Reject<InvalidDataException>(() => NativePluginModuleDiagnosticCrtSource.Read(image, new string('B', 64), constructor));
        Console.WriteLine("OPENNV_MODULE_DIAGNOSTIC_SOURCE_PASS actualProducer=true exactModeShare=true sourceNullRetry=true foreignLineageRefused=true originalCalls=UNEXECUTED");
    }
    internal static void Run(string companion, string primaryFixture, string originalPeer, string runtime, string nvse)
    {
        RunOwnedSource(originalPeer);
        var paths = new[] { companion, primaryFixture, originalPeer, runtime, nvse }.Select(Path.GetFullPath).ToArray();
        var hashes = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var generations = new HashSet<ulong>();
        foreach (var locked in new[] { 0, 1, 6, 0 }) Require(generations.Add(RunGeneration(paths, hashes, locked)), "fresh actual module/global/FILE generation");
        foreach (var row in hashes) Require(Hash(row.Key) == row.Value, "unchanged selected source " + Path.GetFileName(row.Key));
        Console.WriteLine("OPENNV_MODULE_DIAGNOSTIC_CALLER_PASS originalTtwQuery=true sharedDomain=true sharedUcrt=true actualFile=true " +
            "genuineSharingFailure=true originalRetries=true actualClose=true sourceBytes=unchanged loadAndCampaign=UNACCEPTED coldGlobals=UNOWNED");
    }
    private static ulong RunGeneration(string[] paths, IReadOnlyDictionary<string, string> hashes, int locked)
    {
        var temporary = Directory.CreateTempSubdirectory("opennv-module-diagnostic-").FullName;
        NativePluginExecutionDomain? domain = null; NativePluginPrivateIo? io = null; NativeNvseHostSource? host = null;
        var locks = new List<FileStream>();
        try
        {
            var directory = Path.GetDirectoryName(paths[3])!;
            var stack = HashText("authored-shared-diagnostic\0" + hashes[paths[1]] + hashes[paths[2]]);
            var image = NativePluginImageDeclarations.Read(paths[1], dll: true);
            var imports = image.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
                .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
            io = new(new(stack, hashes[paths[1]], paths[1], directory, Path.Combine(temporary, "private-state"),
                [directory, Path.GetDirectoryName(paths[4])!, Path.GetDirectoryName(paths[1])!, Path.GetDirectoryName(paths[2])!], [], _ => null,
                "authored-primary-without-peer-path-authority", imports));
            host = NativeNvseHostSource.Open(paths[3], hashes[paths[3]], paths[4], hashes[paths[4]], stack, false, "authored-owned-diagnostic-host");
            io.BindSourceAddressSpace(host); domain = new(paths[0], TimeSpan.FromSeconds(30), privateIo: io);
            var primary = domain.LoadNvseImage(host, paths[1], hashes[paths[1]], sourcePluginHandle: 11);
            Require(domain.QueryNvse(primary).Returned && domain.InitializeNvse(primary).Returned, "authored primary returns with actual retained provider");
            var peerImage = NativePluginImageDeclarations.Read(paths[2], dll: true);
            var construction = NativePluginModuleConstructionSource.Read(peerImage, stack);
            var source = NativePluginModuleDiagnosticCrtSource.Read(peerImage, stack, construction)!;
            var before = io.Receipts.Count;
            Reject<NotSupportedException>(() => io.ResolveModuleDiagnostic(1, source, source.Name + ".foreign", "w", source.Sharing, 0));
            Reject<NotSupportedException>(() => io.ResolveModuleDiagnostic(1, source, source.Name, "w+", source.Sharing, 0));
            Reject<NotSupportedException>(() => io.ResolveModuleDiagnostic(1, source, source.Name, "w", 0x40, 0));
            Require(io.Receipts.Count == before, "unowned request refuses before publishing a route");
            for (uint attempt = 0; attempt < locked; ++attempt)
                locks.Add(new FileStream(io.PrepareModuleDiagnosticPath(source, attempt), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read));
            var peer = domain.LoadCoResidentNvseImage(paths[2], hashes[paths[2]], 22);
            NativeModuleConstructionContracts.CheckActualReturned(domain, peer);
            Require(domain.QueryNvse(peer).Returned && peer.QueryReceipt?.Info.Version == 333, "unchanged original TTW Query returns its actual PluginInfo");
            var opens = domain.NvseModuleDiagnosticOpens.Where(row => row.Module == peer.Module).ToArray();
            var expected = locked == 6 ? 6 : locked + 1;
            Require(opens.Length == expected && opens.All(row => row.Returned && row.Generation == domain.Generation && row.Thread == domain.NativeThread &&
                row.Process == domain.ProcessId && row.Parent != 0 && row.SourceSha256.Equals(hashes[paths[2]], StringComparison.OrdinalIgnoreCase) &&
                row.StackSha256 == stack && row.CellValue == 0 && row.Sharing == source.Sharing && row.Mode == source.Mode), "actual original call/source/error prefixes");
            for (var at = 0; at < opens.Length; ++at)
                Require(opens[at].Attempt == at && opens[at].Path == source.AttemptName((uint)at) && opens[at].Route != 0 &&
                    (at < locked ? opens[at].Stream == 0 && opens[at].Status is { Errno: not 0, DosError: not 0 } : opens[at].Stream is > 0),
                    "actual SDK sharing result controls original null retry");
            Require(domain.NvseCrtReceipts.Where(row => row.Module == peer.Module).All(row => row.Provider != 0 && row.Generation == domain.Generation),
                "diagnostic uses the same retained real CRT provider and module member history");
            Reject<NotSupportedException>(domain.RequirePrivateCrtSaveOwned);
            _ = domain.UnloadNvse(peer);
            var closes = domain.NvseCrtReceipts.Where(row => row.Module == peer.Module && row.Operation == NativePluginCrtOperation.Close).ToArray();
            Require(closes.Length == (locked == 6 ? 0 : 1) && closes.All(row => row.StreamRetired && row.Result == 0 && row.Stream == opens[^1].Stream),
                "actual original destructor closes exactly its returned real FILE");
            _ = domain.UnloadNvse(primary); domain.Dispose();
            Require(domain.ChildExited && domain.NaturallyRetired && domain.Fault is null, "actual original/shared-provider retirement returns");
            return domain.Generation;
        }
        finally
        {
            try { domain?.Dispose(); }
            finally
            {
                foreach (var lease in locks) lease.Dispose();
                if (domain is not null && !domain.ChildExited) throw new InvalidDataException("Diagnostic source/private state outlives its native child.");
                io?.Dispose(); host?.Dispose();
                var absolute = Path.GetFullPath(temporary); var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("opennv-module-diagnostic-", StringComparison.Ordinal))
                    throw new InvalidDataException("Diagnostic temporary cleanup escaped its authored directory.");
                Directory.Delete(absolute, true);
            }
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static void Require(bool value, string reason) { if (!value) throw new InvalidDataException("Diagnostic CRT contract failed: " + reason); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Diagnostic CRT did not retain its required refusal: " + typeof(T).Name); }
}
