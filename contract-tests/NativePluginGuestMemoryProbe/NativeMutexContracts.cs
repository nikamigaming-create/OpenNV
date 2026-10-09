using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeMutexContracts
{
    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var temporary = Directory.CreateTempSubdirectory("opennv-actual-mutex-").FullName;
        try
        {
            var stack = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("authored-mutex-caller\0" + originals[fixture])));
            var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, Path.GetDirectoryName(runtime)!,
                Path.Combine(temporary, "private-state"),
                [Path.GetDirectoryName(runtime)!, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!],
                [], _ => null, "authored-mutex-public-imports", new HashSet<string>(StringComparer.Ordinal));
            using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack,
                false, "authored-mutex-caller-selected-owned-standard-edition");
            using var io = new NativePluginPrivateIo(selection);
            using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
            Require(domain.QueryNvse(plugin).Returned, "actual public Query");
            try
            {
                Require(domain.InitializeNvse(plugin).Returned, "actual Load/Windows creation and acquisition");
                Require(domain.NvseMutexReceipts.All(row => row.Generation == domain.Generation && row.Module == plugin.Module &&
                    row.Thread == domain.NativeThread && row.Call != 0), "actual module/generation/caller/thread provenance");
                Require(domain.NvseMutexReceipts.Any(row => row.Api == NativePluginMutexApi.Release && row.Result == 0 && row.LastError == 288),
                    "genuine Windows non-owner release failure retained");
                Require(domain.NvseMutexComparisons.Any(row => row.SameObject) &&
                    domain.NvseMutexComparisons.Any(row => !row.SameObject && row.LastError == 1656), "real kernel identity/independent object comparisons");
                domain.UnloadNvse(plugin);
                Require(domain.NvseMutexDetachReceipts.Count == 2 && domain.NvseMutexDetachReceipts[^1].Returned &&
                    domain.NvseMutexDetachReceipts[^1].Result != 0 &&
                    domain.NvseMutexReceipts.Count(row => row.DeferredDetach && row.Result != 0) == 2,
                    "real original FreeLibrary/ReleaseMutex/CloseHandle in ordered destructor scope");
                Require(domain.NvseMutexPending is null, "no unfinished Windows SDK prefix");
                domain.Dispose(); Require(domain.NaturallyRetired && domain.ChildExited && domain.ResourcesRetired,
                    "actual independent kernel/source/child retirement");
                Console.WriteLine("OPENNV_NATIVE_MUTEX_CALLER_PASS actualWindows=true kernelObjectIdentity=true originalThread=true " +
                    "recursiveAcquisition=true actualFailureRetained=true actualDetach=true originalModCompatibility=UNACCEPTED");
            }
            catch
            {
                Console.Error.WriteLine($"OPENNV_NATIVE_MUTEX_PREFIX calls={domain.NvseMutexReceipts.Count} " +
                    $"comparisons={domain.NvseMutexComparisons.Count} detach={domain.NvseMutexDetachReceipts.Count} " +
                    $"pending={domain.NvseMutexPending is not null}"); throw;
            }
        }
        finally
        {
            foreach (var (path, hash) in originals) Require(Hash(path) == hash, "unchanged input " + Path.GetFileName(path));
            var cleanup = Path.GetFullPath(temporary);
            if (!cleanup.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(cleanup).StartsWith("opennv-actual-mutex-", StringComparison.Ordinal))
                throw new InvalidDataException("Authored mutex cleanup escaped its created directory.");
            Directory.Delete(cleanup, true);
        }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool valid, string operation) { if (!valid) throw new InvalidDataException("Native mutex caller: " + operation); }
}
