using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeModuleReadContracts
{
    internal static void RunDeclarations()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-module-read-source-").FullName;
        try
        {
            var path = Path.Combine(directory, "authored.dll"); var stack = new string('A', 64);
            WriteImage(path, tlsCallback: false, callsFromEntry: false);
            var image = NativePluginImageDeclarations.Read(path, dll: true);
            var source = NativePluginModuleReadIoSource.Read(image, stack) ?? throw new InvalidDataException("Actual authored source capability absent.");
            Require(source.BoundImports == 1 && source.Owns("kernel32.dll", "CreateFileW"), "actual import and returned pure leaf source proof");
            NativePluginCoResidentImports.Require(image, source);
            Reject<NotSupportedException>(() => NativePluginCoResidentImports.Require(image));
            Reject<InvalidDataException>(() => source.Check(image, new string('B', 64)));
            Reject<InvalidDataException>(() => source.Check(image with { Imports = [image.Imports[0] with { SlotRva = 1 }] }));
            WriteImage(path, tlsCallback: true, callsFromEntry: false);
            Reject<NotSupportedException>(() => NativePluginModuleReadIoSource.Read(NativePluginImageDeclarations.Read(path, dll: true), stack));
            WriteImage(path, tlsCallback: false, callsFromEntry: true);
            Reject<NotSupportedException>(() => NativePluginModuleReadIoSource.Read(NativePluginImageDeclarations.Read(path, dll: true), stack));
            Console.WriteLine("OPENNV_MODULE_READ_SOURCE_PASS actualPe=true preEntry=true tlsCallbackRefused=true constructorCallRefused=true originalCalls=UNEXECUTED");
        }
        finally { Directory.Delete(directory, true); }
    }
    internal static void RunOwnedConstruction(string[] paths)
    {
        foreach (var path in paths)
        {
            var image = NativePluginImageDeclarations.Read(path, dll: true);
            Require(image.Imports.Any(row => row.Name is not null && NativePluginModuleReadIoSource.OwnsExport(row.Library, row.Name)),
                "selected original has a read import requiring its own construction capability");
            Reject<NotSupportedException>(() => NativePluginModuleReadIoSource.Read(image, new string('A', 64)));
        }
        Console.WriteLine("OPENNV_MODULE_READ_OWNED_CONSTRUCTION_REFUSAL_PASS selectedImages=" + paths.Length + " originalCalls=UNEXECUTED");
    }
    internal static void Run(string companion, string primaryPath, string peerPath, string runtime, string nvse)
    {
        RunDeclarations();
        var paths = new[] { companion, primaryPath, peerPath, runtime, nvse }.Select(Path.GetFullPath).ToArray();
        var hashes = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var generations = new HashSet<ulong>();
        foreach (var failure in new[] { 0, 5, 6, 7, 0 })
            Require(generations.Add(RunGeneration(paths, hashes, failure)), "fresh original module/read resource generation");
        foreach (var pair in hashes) Require(Hash(pair.Key) == pair.Value, "unchanged selected source " + Path.GetFileName(pair.Key));
        Console.WriteLine("OPENNV_MODULE_READ_CALLER_PASS originalCalls=true sharedRoutes=true sharedHandles=true actualSdk=true " +
            "gameModuleSource=true profileMultiSz=true requestedAccessPreserved=true failedPrefixes=true freshRetry=true coldGlobals=UNOWNED");
    }
    private static ulong RunGeneration(string[] paths, IReadOnlyDictionary<string, string> hashes, int failure)
    {
        var temporary = Directory.CreateTempSubdirectory("opennv-module-read-").FullName;
        NativePluginExecutionDomain? domain = null; NativePluginPrivateIo? io = null; NativeNvseHostSource? host = null;
        try
        {
            var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
            var authored = Path.Combine(sourceRoot, "input.cfg"); File.WriteAllText(authored, "FILE-SIZE-PROBE\r\n", Encoding.ASCII);
            var profile = Path.Combine(sourceRoot, "profile.ini"); File.WriteAllText(profile, "[Example]\r\nAnswer=42\r\nOther=7\r\n", Encoding.ASCII);
            var inputHash = Hash(authored); var profileHash = Hash(profile);
            NativeCrtContracts.ProtectAuthoredSource(sourceRoot, authored); NativeCrtContracts.ProtectAuthoredSource(sourceRoot, profile);
            var directory = Path.GetDirectoryName(paths[3])!;
            Require(directory.All(char.IsAscii), "authored caller's explicit ASCII game installation path");
            var readPath = Path.Combine(directory, "OpenNV-module-read.cfg");
            var profilePath = Path.Combine(directory, "OpenNV-module-profile.ini");
            var primaryInputs = new[] { Path.Combine(directory, "OpenNVFileSize-small.cfg"), Path.Combine(directory, "OpenNVFileSize-large.cfg") };
            var primaryImage = NativePluginImageDeclarations.Read(paths[1], dll: true);
            var peerImage = NativePluginImageDeclarations.Read(paths[2], dll: true);
            var imports = primaryImage.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
                .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
            var stack = HashText("authored-module-read\0" + hashes[paths[1]] + hashes[paths[2]]);
            io = new(new(stack, hashes[paths[1]], paths[1], directory, Path.Combine(temporary, "private-state"),
                [directory, Path.GetDirectoryName(paths[4])!, Path.GetDirectoryName(paths[1])!, Path.GetDirectoryName(paths[2])!, sourceRoot], [], Resolve,
                "authored-module-read-imports", imports) { CrtProviders = [] });
            host = NativeNvseHostSource.Open(paths[3], hashes[paths[3]], paths[4], hashes[paths[4]], stack, false, "authored-module-read-owned-host");
            io.BindSourceAddressSpace(host); domain = new(paths[0], TimeSpan.FromSeconds(30), privateIo: io);
            var primary = domain.LoadNvseImage(host, paths[1], hashes[paths[1]], sourcePluginHandle: 11);
            Require(domain.QueryNvse(primary).Returned && domain.InitializeNvse(primary).Returned, "original primary file owner returned");
            var peer = domain.LoadCoResidentNvseImage(paths[2], hashes[paths[2]], 22);
            Require(domain.QueryNvse(peer).Returned && domain.InitializeNvse(peer).Returned, "original module source/read loader returned");
            var moduleName = domain.NvseLastModuleReadIoReceipt ?? throw new InvalidDataException("Actual module-zero filename receipt missing.");
            Require(moduleName.Returned && !moduleName.Sdk && moduleName.Operation == NativePluginModuleReadOperation.GameModuleNameA &&
                moduleName.Module == peer.Module && moduleName.ResourceModule == primary.Module && moduleName.Result is > 0,
                "actual selected game module path independent of companion executable");
            Require(Encoding.ASCII.GetString(moduleName.After!, 0, checked((int)moduleName.Result!.Value)) == paths[3], "exact selected game module-zero path");
            foreach (var mode in new uint[] { 0, 1, 2, 3, 4 }) Require(Call(mode) == 0, "original SDK/output probe " + mode);
            Require(domain.NvseLastFileSizeReceipt is { Returned: true } size && size.Module == peer.Module && size.ResourceModule == primary.Module &&
                size.AfterBits == 17, "peer open feeds existing size owner through the same real shared HANDLE table");
            var open = domain.NvseModuleReadIoReceipts.First(row => row.Operation == NativePluginModuleReadOperation.OpenW && row.Result is not (0 or uint.MaxValue));
            Require(open.Sdk && open.Returned && open.Module == peer.Module && open.Arguments.SequenceEqual(new uint[] { 0x80000000, 7, 0, 3, 0x08000080, 0 }),
                "exact requested access/share/security/disposition/flags/template retained");
            Require(domain.NvseModuleReadIoReceipts.Any(row => row.Operation == NativePluginModuleReadOperation.ProfileStringA &&
                row.Sdk && row.Returned && row.Result == 10 && row.After![10] == 0 && row.After[11] == 0), "SDK-owned truncated multi-SZ");
            Reject<NotSupportedException>(() => domain.RequirePrivateCrtSaveOwned());
            if (failure != 0)
            {
                Reject<NativePluginDomainFaultException>(() => Call(checked((uint)failure)));
                var failed = domain.NvseLastModuleReadIoReceipt ?? throw new InvalidDataException("Entered failed module caller receipt absent.");
                Require(domain.Fault is not null && !failed.Returned && failed.Result is null && failed.After is null &&
                    failed.Module == peer.Module && failed.Route == 0 && (failure != 5 || failed.Arguments[0] == 0x40000000),
                    "failed original requested write/module/closed HANDLE prefix remains visible before SDK/output mutation");
            }
            else { domain.UnloadNvse(peer); domain.UnloadNvse(primary); }
            var generation = domain.Generation;
            domain.Dispose(); Require(domain.ChildExited && domain.ResourcesRetired, "actual shared native file/source retirement");
            if (failure != 0) Require(domain.NvseLastModuleReadIoReceipt is { ReleasedAfterChildExit: true }, "failed borrow released only after actual child exit");
            Require(Hash(authored) == inputHash && Hash(profile) == profileHash && !File.Exists(readPath) && !File.Exists(profilePath),
                "original asset/configuration inputs unchanged with no original namespace writes");
            return generation;

            NativePluginIoReadWinner? Resolve(string path) =>
                path.Equals(profilePath, StringComparison.OrdinalIgnoreCase) ? new(profile, profileHash, "authored-profile-winner", true) :
                path.Equals(readPath, StringComparison.OrdinalIgnoreCase) || primaryInputs.Contains(path, StringComparer.OrdinalIgnoreCase)
                    ? new(authored, inputHash, "authored-input-winner", false) : null;
            uint Call(uint mode)
            {
                var export = peerImage.Exports.Single(row => row.Name == "ModuleReadProbe");
                var function = domain.BorrowNvseFunction(peer, peer, new(export.Rva, NativePluginAbi.Stdcall, 1, export.Name));
                try { return domain.InvokeNvseFunction(function, null, [mode]).RawEax; }
                finally { if (domain.Fault is null) domain.ReleaseNvseFunction(function); }
            }
        }
        finally
        {
            try { domain?.Dispose(); }
            finally
            {
                if (domain is not null && !domain.ChildExited) throw new InvalidOperationException("Module read cleanup retains its original live child.");
                try { io?.Dispose(); } finally { host?.Dispose(); }
                var absolute = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("opennv-module-read-", StringComparison.Ordinal))
                    throw new InvalidDataException("Module read cleanup escaped its exact generated directory.");
                Directory.Delete(absolute, true);
            }
        }
    }
    private static void WriteImage(string path, bool tlsCallback, bool callsFromEntry)
    {
        var bytes = new byte[0x600];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        void Text(int at, string value) => Encoding.ASCII.GetBytes(value).CopyTo(bytes, at);
        U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x4550); U16(0x84, 0x14c); U16(0x86, 2);
        U16(0x94, 0xe0); U16(0x96, 0x2102); var optional = 0x98;
        U16(optional, 0x10b); U32(optional + 16, 0x1000); U32(optional + 20, 0x1000); U32(optional + 24, 0x2000);
        U32(optional + 28, 0x10000000); U32(optional + 32, 0x1000); U32(optional + 36, 0x200);
        U32(optional + 56, 0x3000); U32(optional + 60, 0x200); U16(optional + 68, 3); U32(optional + 92, 16);
        U32(optional + 104, 0x2000); U32(optional + 108, 40);
        var sections = optional + 0xe0;
        Text(sections, ".text"); U32(sections + 8, 0x200); U32(sections + 12, 0x1000); U32(sections + 16, 0x200);
        U32(sections + 20, 0x200); U32(sections + 36, 0x60000020);
        Text(sections + 40, ".rdata"); U32(sections + 48, 0x200); U32(sections + 52, 0x2000); U32(sections + 56, 0x200);
        U32(sections + 60, 0x400); U32(sections + 76, 0x40000040);
        (callsFromEntry ? new byte[] { 0xe8, 0, 0, 0, 0, 0xc2, 12, 0 } : new byte[] { 0xb8, 1, 0, 0, 0, 0xc2, 12, 0 }).CopyTo(bytes, 0x200);
        U32(0x400, 0x2040); U32(0x40c, 0x2070); U32(0x410, 0x2050);
        U32(0x440, 0x20a0); U32(0x450, 0x20a0); Text(0x470, "kernel32.dll"); Text(0x4a2, "CreateFileW");
        if (tlsCallback)
        {
            U32(optional + 96 + 9 * 8, 0x20c0); U32(optional + 100 + 9 * 8, 24);
            U32(0x4cc, 0x10002100); U32(0x500, 0x10001000);
        }
        File.WriteAllBytes(path, bytes);
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException("Module read contract: " + message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Module read contract expected " + typeof(T).Name); }
}
