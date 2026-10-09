using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCngSharedContracts
{
    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var temporary = Directory.CreateTempSubdirectory("opennv-cng-shared-caller-").FullName;
        try
        {
            var stack = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("authored-CNG-caller\0" + originals[fixture])));
            var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, Path.GetDirectoryName(runtime)!,
                Path.Combine(temporary, "private-state"),
                [Path.GetDirectoryName(runtime)!, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!],
                [], _ => null, "authored-CNG-imports", new HashSet<string>(StringComparer.Ordinal));
            using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack,
                false, "authored-caller-selected-owned-standard-edition");
            using var io = new NativePluginPrivateIo(selection);
            io.BindSourceAddressSpace(host);
            using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            domain.BindCngSystemServiceBuild(companion);
            domain.BindNativeImportProviderBuild(companion);
            try
            {
                var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
                Require(domain.QueryNvse(plugin).Returned, "actual original Query");
                Require(domain.InitializeNvse(plugin).Returned, "actual original Load and independent authored digest");
                var shared = domain.CngSharedReceipts;
                Require(shared.Count >= 2 && shared.All(row => row.OriginalGeneration == domain.Generation && row.OriginalCall != 0 &&
                    row.ServiceGeneration != 0 && row.ServiceProcess > 0 && row.Source.Kind == 2 &&
                    row.Source.Address == row.ServiceAddress && row.CommonPlacement != 0), "actual same-address mapped caller/service views");
                var placements = domain.SharedPlacementReceipts;
                Require(placements.Count == 2 && placements.All(row => row.Published && row.Failure is null &&
                    row.OriginalGeneration == domain.Generation && row.OriginalProcess == domain.ProcessId &&
                    row.ServiceProcess > 0 && row.ServiceProcess != row.OriginalProcess && row.Source.Preferred == 0 &&
                    row.OriginalMemory == NativePluginSharedPlacementMemory.Mapped && row.ServiceMemory == NativePluginSharedPlacementMemory.Mapped),
                    "both actual process mappings precede OS-selected pointer publication");
                Require(placements.All(row => domain.SharedPlacementApiReceipts.Count(api => api.Placement == row.Id &&
                    api.Succeeded && api.RequestedAddress == row.Address && api.ReturnedAddress == row.Address &&
                    api.Operation is "VirtualAlloc2/placeholder" or "MapViewOfFile3/replace-placeholder") == 4),
                    "actual reservation and section replacement in each retained creation process");
                Require(shared.Select(row => row.Source.Offset).Distinct().Order().SequenceEqual(new uint[] { 0, 196608 }) &&
                    shared.Select(row => row.Section).Distinct().Count() == 1, "same kernel section with independent offset views");
                Require(domain.CngSharedCalls.Count(row => row.Operation == NativePluginCryptoOperation.CreateHash && row.Object is not null &&
                    row.Result.Status >= 0 && row.Result.Created != 0) == 2 &&
                    domain.CngSharedCalls.Any(row => row.Operation == NativePluginCryptoOperation.HashData && row.Buffers[2] is { Length: 140013 }) &&
                    domain.CngSharedCalls.Any(row => row.Operation == NativePluginCryptoOperation.FinishHash && row.Buffers[3] is { Length: 32 }),
                    "real SDK nonnull object/input/output consumers");
                domain.UnloadNvse(plugin);
                Require(domain.CngDetachReceipts.Count == 2 && domain.CngDetachReceipts.Last() is
                    { Entered: true, Returned: true, FreeLibrarySucceeded: true, Error: 0 } &&
                    domain.CngSharedReceipts.Last().ParentHandleRetired, "actual detach cleanup and checked shared retirement");
                domain.Dispose();
                Require(domain.NaturallyRetired && domain.ChildExited && domain.CngServiceProcessResourcesRetired,
                    "normal exact original/service process retirement");
                Require(domain.SharedPlacementReceipts.All(row => row.SourceReleased && row.CngBorrowRetired &&
                    row.ParentSectionClosed && row.OriginalMemory is NativePluginSharedPlacementMemory.None or NativePluginSharedPlacementMemory.ClosedProcess &&
                    row.ServiceMemory is NativePluginSharedPlacementMemory.None or NativePluginSharedPlacementMemory.ClosedProcess),
                    "independent actual source maps, service maps and retained kernel sections retired");
                Console.WriteLine("OPENNV_CNG_SHARED_CALLER_PASS importedProvider=true actualWindowsSdk=true nonnullObject=true " +
                    "sameAddresses=true offsetViews=true commonPlacement=true completeDigest=true actualDetach=true normalExit=true originalModCompatibility=UNACCEPTED");
            }
            catch
            {
                Console.Error.WriteLine($"OPENNV_CNG_SHARED_CALLER_PREFIX sharedViews={domain.CngSharedReceipts.Count} " +
                    $"sharedCalls={domain.CngSharedCalls.Count} detach={domain.CngDetachReceipts.Count}");
                throw;
            }
        }
        finally
        {
            foreach (var (path, hash) in originals) Require(Hash(path) == hash, "unchanged source input " + Path.GetFileName(path));
            var cleanup = Path.GetFullPath(temporary);
            if (!cleanup.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(cleanup).StartsWith("opennv-cng-shared-caller-", StringComparison.Ordinal))
                throw new InvalidDataException("Authored CNG cleanup escaped its created temporary directory.");
            Directory.Delete(cleanup, true);
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool value, string operation) { if (!value) throw new InvalidDataException("CNG shared caller: " + operation); }
}
