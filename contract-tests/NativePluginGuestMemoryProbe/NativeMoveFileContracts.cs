using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static partial class NativeMoveFileContracts
{
    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var temporary = Directory.CreateTempSubdirectory("opennv-native-move-").FullName;
        try
        {
            var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
            var sourceInput = Path.Combine(sourceRoot, "source.cfg"); var targetInput = Path.Combine(sourceRoot, "target.cfg");
            File.WriteAllText(sourceInput, "SOURCE", new UTF8Encoding(false)); File.WriteAllText(targetInput, "TARGET", new UTF8Encoding(false));
            originals.Add(sourceInput, Hash(sourceInput)); originals.Add(targetInput, Hash(targetInput));
            NativeCrtContracts.ProtectAuthoredSource(sourceRoot, sourceInput);
            NativeCrtContracts.ProtectAuthoredSource(sourceRoot, targetInput);
            var directory = Path.GetDirectoryName(runtime)!;
            var source = Path.Combine(directory, "OpenNVMove-source.cfg"); var target = Path.Combine(directory, "OpenNVMove-target.cfg");
            var renamed = Path.Combine(directory, "OpenNVMove-\u03a9.cfg"); var missing = Path.Combine(directory, "OpenNVMove-missing.cfg");
            var scopes = new[] { source, target, renamed, missing }
                .Select(path => new NativePluginIoWriteScope(path, false, NativePluginIoRole.Configuration, "authored-file-move-path")).ToArray();
            var stack = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("authored-paired-move\0" + originals[fixture])));
            var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, directory,
                Path.Combine(temporary, "private-state"), [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, sourceRoot],
                scopes, path => StringComparer.OrdinalIgnoreCase.Equals(path, source)
                    ? new(sourceInput, originals[sourceInput], "authored-source-configuration", true) :
                    StringComparer.OrdinalIgnoreCase.Equals(path, target)
                        ? new(targetInput, originals[targetInput], "authored-target-configuration", true) : null,
                "authored-paired-file-move-imports", new HashSet<string>(StringComparer.Ordinal)
                {
                    "kernel32.dll!GetLastError", "kernel32.dll!SetLastError",
                    "kernel32.dll!MultiByteToWideChar", "kernel32.dll!WideCharToMultiByte",
                }) { CrtProviders = [] };
            using (var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack,
                false, "authored-paired-move-selected-owned-standard-edition"))
            using (var io = new NativePluginPrivateIo(selection))
            {
                io.BindSourceAddressSpace(host);
                using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
                var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
                Require(domain.QueryNvse(plugin).Returned, "actual Query returned");
                Require(domain.InitializeNvse(plugin).Returned, "actual Windows move calls returned their expected results");
                var moves = io.MoveReceipts.ToArray();
                Require(moves.Length == 4 && moves.All(row => row.Generation == domain.Generation && row.Parent != 0 &&
                    row.SourceRoute != 0 && row.DestinationRoute != 0 && row.SourceRoute != row.DestinationRoute), "two independent admitted path lifetimes");
                Require(!moves[0].Returned && moves[0].LastError is 80 or 183 && !moves[0].SourceRemoved,
                    "existing destination preserves both files and actual Windows error");
                Require(moves[1].Returned && moves[1].SourceRemoved && moves[1].Flags == 9 &&
                    moves[2].Returned && moves[2].SourceRemoved && moves[2].Destination == renamed,
                    "narrow replace/write-through and wide Unicode rename");
                Require(!moves[3].Returned && moves[3].LastError == 2 && !moves[3].SourceRemoved, "missing source retains the genuine failure");
                Require(!File.Exists(source) && !File.Exists(target) && !File.Exists(renamed), "native writes remain within declared private state");
                var destination = io.Receipts.Last(row => row.Api == NativePluginPrivateIo.MoveFileApi && row.VirtualPath == renamed).PhysicalPath!;
                Require(File.ReadAllText(destination) == "SOURCE", "actual destination retains source bytes");
                domain.UnloadNvse(plugin); domain.Dispose();
                Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired, "native child and provider retire before source cleanup");
            }

            // Cold I/O reconstruction reads its own tombstones and private
            // files. It cannot resurrect the selected original configurations.
            using (var cold = new NativePluginPrivateIo(selection))
            {
                cold.Claim(37);
                foreach (var path in new[] { source, target })
                {
                    var route = cold.Resolve(19, 7, NativePluginIoAction.Attributes, path);
                    Require(route.Error == 2, "cold renamed source cannot reappear from an original winner"); cold.Complete(19, route.Id, 7, route.Error);
                }
                var result = cold.Resolve(19, 7, NativePluginIoAction.Attributes, renamed);
                Require(result.Error == 0 && File.ReadAllText(result.PhysicalPath!) == "SOURCE", "cold destination bytes and namespace");
                cold.Complete(19, result.Id, 7, 0);
                var before = cold.Receipts.Count;
                Reject<NotSupportedException>(() => cold.ResolveMove(19, renamed, source, 4));
                Reject<NotSupportedException>(() => cold.ResolveMove(19, renamed, Path.Combine(directory, "undeclared.cfg"), 0));
                Require(cold.Receipts.Count == before && File.ReadAllText(result.PhysicalPath!) == "SOURCE",
                    "unowned deferred flags and destinations fail before route publication or mutation");
                cold.RequireRetired();
            }
            Console.WriteLine("OPENNV_NATIVE_MOVE_FILE_CALLER_PASS actualWindows=true pairedRoutes=true narrow=true unicode=true replace=true " +
                "writeThrough=true actualErrors=true originalInputs=unchanged coldTombstones=true sourceAssets=refused " +
                "directoryMove=unowned crossVolume=unowned deferredMove=unowned originalModCompatibility=unaccepted");
        }
        finally
        {
            try { foreach (var (path, hash) in originals) Require(Hash(path) == hash, "unchanged input " + Path.GetFileName(path)); }
            finally
            {
                var absolute = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absolute).StartsWith("opennv-native-move-", StringComparison.Ordinal))
                    throw new InvalidDataException("Native move cleanup escaped its exact generated directory.");
                Directory.Delete(absolute, true);
            }
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException("Native file-move contract: " + message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Native file-move contract expected " + typeof(T).Name);
    }
}
