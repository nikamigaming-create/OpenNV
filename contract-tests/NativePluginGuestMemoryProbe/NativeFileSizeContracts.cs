using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeFileSizeContracts
{
    private const long LargeLength = 0x100000013;
    internal static void RunDeclarations()
    {
        foreach (var library in new[] { "kernel32.dll", "kernelbase.dll", "api-ms-win-core-file-l1-1-0.dll" })
            Require(NativePluginSharedFileSizeImports.Owns(library, "GetFileSizeEx"), "exact Windows file-size export");
        Require(!NativePluginSharedFileSizeImports.Owns("foreign.dll", "GetFileSizeEx") &&
            !NativePluginSharedFileSizeImports.Owns("kernel32.dll", "GetFileSize"), "no foreign provider or different ABI promotion");
        NativePluginCoResidentImports.Require(Declaration("kernel32.dll", "GetFileSizeEx"));
        Reject<NotSupportedException>(() => NativePluginCoResidentImports.Require(Declaration("foreign.dll", "GetFileSizeEx")));
        Reject<NotSupportedException>(() => NativePluginCoResidentImports.Require(Declaration("kernel32.dll", "CreateFileW")));
        Console.WriteLine("OPENNV_SHARED_FILE_SIZE_DECLARATIONS_PASS originalCalls=UNEXECUTED");
    }
    internal static void Run(string companion, string primaryPath, string peerPath, string runtime, string nvse)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Shared actual HANDLE calls require Windows x64 parent and x86 companion.");
        RunDeclarations();
        var paths = new[] { companion, primaryPath, peerPath, runtime, nvse }.Select(Path.GetFullPath).ToArray();
        var hashes = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var generations = new HashSet<ulong>();
        foreach (var failure in new[] { 0, 1, 2, 0 })
            Require(generations.Add(RunGeneration(paths, hashes, failure)), "fresh native HANDLE/source generations");
        foreach (var source in hashes) Require(Hash(source.Key) == source.Value, "unchanged original " + Path.GetFileName(source.Key));
        Console.WriteLine("OPENNV_SHARED_FILE_SIZE_CALLER_PASS sdk=true twoOriginalDlls=true actualHandles=true length64=true " +
            "seekIndependent=true invalidHandleError=true x86Abi=true untrackedBorrowRefused=true retiredBorrowRefused=true freshRetry=true coldGlobals=UNOWNED");
    }
    private static ulong RunGeneration(string[] paths, IReadOnlyDictionary<string, string> hashes, int failure)
    {
        var temporary = Directory.CreateTempSubdirectory("opennv-file-size-").FullName;
        NativePluginExecutionDomain? domain = null;
        NativePluginPrivateIo? io = null; NativeNvseHostSource? host = null;
        try
        {
            var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
            var authored = Path.Combine(sourceRoot, "size.cfg"); File.WriteAllBytes(authored, Encoding.ASCII.GetBytes("FILE-SIZE-PROBE\r\n"));
            Require(new FileInfo(authored).Length == 17, "authored byte extent"); var authoredHash = Hash(authored);
            NativeCrtContracts.ProtectAuthoredSource(sourceRoot, authored);
            var directory = Path.GetDirectoryName(paths[3])!;
            var smallPath = Path.Combine(directory, "OpenNVFileSize-small.cfg"); var largePath = Path.Combine(directory, "OpenNVFileSize-large.cfg");
            var declaration = NativePluginImageDeclarations.Read(paths[1], dll: true);
            var peerDeclaration = NativePluginImageDeclarations.Read(paths[2], dll: true);
            var imports = declaration.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
                .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
            var stack = HashText("authored-shared-file-size\0" + hashes[paths[1]] + hashes[paths[2]]);
            var selection = new NativePluginIoSelection(stack, hashes[paths[1]], paths[1], directory,
                Path.Combine(temporary, "private-state"), [directory, Path.GetDirectoryName(paths[4])!, Path.GetDirectoryName(paths[1])!, sourceRoot],
                [new(smallPath, false, NativePluginIoRole.Configuration, "authored-file-size-small"),
                 new(largePath, false, NativePluginIoRole.Configuration, "authored-file-size-large")],
                path => path.Equals(smallPath, StringComparison.OrdinalIgnoreCase) || path.Equals(largePath, StringComparison.OrdinalIgnoreCase)
                    ? new(authored, authoredHash, "authored-size-configuration", true) : null,
                "authored-shared-file-size-imports", imports) { CrtProviders = [] };
            io = new NativePluginPrivateIo(selection);
            host = NativeNvseHostSource.Open(paths[3], hashes[paths[3]], paths[4], hashes[paths[4]], stack, false,
                "authored-shared-file-size-owned-standard-host");
            io.BindSourceAddressSpace(host); domain = new(paths[0], TimeSpan.FromSeconds(30), privateIo: io);
            var generation = domain.Generation;
            // Prepare an actual private sparse configuration through the same
            // route owner. The selected original 17-byte input stays unchanged;
            // the test never writes or hashes a four-gigabyte asset.
            var route = io.Resolve(43, 1, NativePluginIoAction.Write, largePath);
            Require(route.Error == 0 && route.PhysicalPath is not null, "actual private configuration route");
            var opened = false;
            try
            {
                using var file = new FileStream(route.PhysicalPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                io.Complete(43, route.Id, 1, 0); opened = true;
                if (!DeviceIoControl(file.SafeFileHandle, 0x000900c4, 0, 0, 0, 0, out _, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Authored sparse file setup failed.");
                file.SetLength(LargeLength);
            }
            finally { if (opened) io.RecordHandle(43, route.Id, 2, 0, 0); }
            var primary = domain.LoadNvseImage(host, paths[1], hashes[paths[1]], sourcePluginHandle: 11);
            Require(domain.QueryNvse(primary).Returned && domain.InitializeNvse(primary).Returned, "primary actual file producer returned");
            var peer = domain.LoadCoResidentNvseImage(paths[2], hashes[paths[2]], 22);
            Require(domain.QueryNvse(peer).Returned && domain.InitializeNvse(peer).Returned, "peer actual Query/Load returned");
            var small = Call(primary, "FileSizeHandle", [0]); var large = Call(primary, "FileSizeHandle", [1]);
            Require(small is not (0 or uint.MaxValue) && large is not (0 or uint.MaxValue) && small != large, "distinct genuine native HANDLEs");
            Require(Call(primary, "FileSizeProbe", [small, 0]) == 0, "primary uses the same exact size capability");
            foreach (var (handle, mode) in new[] { (small, 0U), (large, 1U), (0U, 2U), (uint.MaxValue, 2U) })
            {
                var before = domain.NvseFileSizeCalls;
                Require(Call(peer, "FileSizeProbe", [handle, mode]) == 0, "original peer stdcall/output/SDK result");
                var receipt = domain.NvseLastFileSizeReceipt ?? throw new InvalidDataException("Actual file-size receipt missing.");
                Require(domain.NvseFileSizeCalls == before + 1 && receipt.Returned && receipt.Module == peer.Module &&
                    receipt.ResourceModule == primary.Module && receipt.Generation == generation && receipt.Handle == handle &&
                    receipt.OutputPresent && receipt.BeforeBits == 0x2233445566778899UL &&
                    receipt.LastError == Call(peer, "FileSizeLastError", []), "executing image/resource owner and raw caller output/error");
                Require(mode == 2 ? receipt.Route == 0 && receipt.SdkResult == 0 && receipt.LastError == 6 && receipt.AfterBits == receipt.BeforeBits :
                    receipt.Route != 0 && receipt.SdkResult != 0 && receipt.AfterBits == (mode == 1 ? (ulong)LargeLength : 17UL),
                    "actual SDK 64-bit extent or genuine invalid-handle failure");
            }
            Require(Call(primary, "FileSizePosition", [0]) == 3 && Call(primary, "FileSizePosition", [1]) == 3,
                "size calls do not change either actual file position");
            if (failure == 1)
            {
                var before = domain.NvseFileSizeCalls;
                Reject<NativePluginDomainFaultException>(() => Call(peer, "FileSizeProbe", [0x13572468, 0]));
                Require(domain.Fault is not null && domain.NvseFileSizeCalls == before,
                    "untracked HANDLE refuses before the real SDK or output mutation");
            }
            else
            {
                domain.UnloadNvse(primary);
                if (failure == 2)
                {
                    var before = domain.NvseFileSizeCalls;
                    Reject<NativePluginDomainFaultException>(() => Call(peer, "FileSizeProbe", [small, 0]));
                    Require(domain.Fault is not null && domain.NvseFileSizeCalls == before,
                        "retired source HANDLE refuses before the SDK or output mutation");
                }
                else domain.UnloadNvse(peer);
            }
            domain.Dispose(); Require(domain.ChildExited && domain.ResourcesRetired, "actual caller/file/provider retirement");
            Require(Hash(authored) == authoredHash && !File.Exists(smallPath) && !File.Exists(largePath), "authored source and original namespace unchanged");
            return generation;

            uint Call(NativeNvsePlugin module, string export, uint[] arguments)
            {
                var image = module == primary ? declaration : peerDeclaration;
                var rva = image.Exports.Single(row => row.Name == export).Rva;
                var borrowed = domain.BorrowNvseFunction(module, module, new(rva, NativePluginAbi.Stdcall, checked((uint)arguments.Length), export));
                try { return domain.InvokeNvseFunction(borrowed, null, arguments).RawEax; }
                finally { if (domain.Fault is null) domain.ReleaseNvseFunction(borrowed); }
            }
        }
        finally
        {
            try { domain?.Dispose(); }
            finally
            {
                if (domain is not null && !domain.ChildExited)
                    throw new InvalidOperationException("Shared file-size cleanup retains its live native child and source resources.");
                try { io?.Dispose(); }
                finally { host?.Dispose(); }
                var absolute = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("opennv-file-size-", StringComparison.Ordinal))
                    throw new InvalidDataException("File-size fixture cleanup escaped its exact generated directory.");
                Directory.Delete(absolute, true);
            }
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static NativePluginImageDeclaration Declaration(string library, string name) =>
        new("authored-file-size.dll", new string('A', 64), true, [new(library, name, null, 128)], [], [], [], false);
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException("Shared file-size contract: " + message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Shared file-size contract expected " + typeof(T).Name); }
    [DllImport("kernel32", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint control,
        nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlap);
}
