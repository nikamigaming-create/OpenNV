using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCoResidentModuleContracts
{
    internal static void RunDeclarations()
    {
        NativePluginCoResidentImports.Require(Declaration([new("kernel32.dll", "GetCurrentThreadId", null, 128)]));
        NativePluginCoResidentImports.Require(Declaration([new("kernel32.dll", "RaiseException", null, 132)]));
        Require(NativePluginPlatformImports.Owner("kernel32.dll", "RaiseException") is not null &&
            NativePluginPlatformImports.Owner("foreign.dll", "RaiseException") is null,
            "actual Windows same-thread exception export remains exact");
        RequireThrows<NotSupportedException>(() => NativePluginCoResidentImports.Require(Declaration([new("kernel32.dll", "CreateFileW", null, 128)])),
            "additional source file policy is not borrowed from the primary DLL");
        RequireThrows<NotSupportedException>(() => NativePluginCoResidentImports.Require(Declaration([new("kernel32.dll", null, 7, 128)])), "exact ordinal owner absent");
        RequireThrows<NotSupportedException>(() => NativePluginCoResidentImports.Require(Declaration([], delay: true)), "original delay loader owner absent");
        foreach (var worker in new[] { "__std_create_threadpool_work", "__std_bulk_submit_threadpool_work",
            "__std_wait_for_threadpool_work_callbacks", "__std_close_threadpool_work" })
            Require(NativePluginPlatformImports.Owner(NativePluginParallelHardwareImports.Library, worker) is null,
                "callback-bearing parallel export remains unowned: " + worker);
        Require(NativePluginPlatformImports.Owner(NativePluginParallelHardwareImports.Library, NativePluginParallelHardwareImports.HardwareThreads) is not null &&
            NativePluginPlatformImports.Owner("foreign.dll", NativePluginParallelHardwareImports.HardwareThreads) is null,
            "hardware query retains exact real provider identity");
        Console.WriteLine("OPENNV_NATIVE_MODULE_SET_DECLARATIONS_PASS originalCallProof=UNEXECUTED");
    }
    internal static void Run(string companion, string primary, string secondary, string runtime, string nvse)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Original co-resident x86 calls require the Windows x64 parent.");
        RunDeclarations();
        var paths = new[] { companion, primary, secondary, runtime, nvse }.Select(Path.GetFullPath).ToArray();
        var source = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var first = RunGeneration(paths, source, fail: false, commandPointers: false, automaticDispose: false);
        _ = RunGeneration(paths, source, fail: false, commandPointers: true, automaticDispose: false);
        _ = RunGeneration(paths, source, fail: false, commandPointers: false, automaticDispose: true);
        _ = RunGeneration(paths, source, fail: false, commandPointers: true, automaticDispose: true);
        _ = RunGeneration(paths, source, fail: false, commandPointers: true, automaticDispose: true, reverseCommandPointer: true);
        _ = RunGeneration(paths, source, fail: false, commandPointers: true, automaticDispose: true, cyclicCommandPointers: true);
        _ = RunGeneration(paths, source, fail: false, commandPointers: false, automaticDispose: false, nullPeer: true);
        var failed = RunGeneration(paths, source, fail: true, commandPointers: false, automaticDispose: false);
        var retry = RunGeneration(paths, source, fail: false, commandPointers: false, automaticDispose: false);
        Require(first != failed && first != retry && failed != retry, "actual fresh original image/static/provider generations after retirement and failure");
        foreach (var row in source) Require(Hash(row.Key) == row.Value, "unchanged source " + Path.GetFileName(row.Key));
        Console.WriteLine("OPENNV_NATIVE_MODULE_SET_PASS twoOriginalDlls=true sharedAddressSpace=true sharedCore=true originalCrossCalls=true coldState=UNOWNED");
    }
    private static ulong RunGeneration(string[] paths, IReadOnlyDictionary<string, string> hashes, bool fail, bool commandPointers, bool automaticDispose, bool nullPeer = false,
        bool reverseCommandPointer = false, bool cyclicCommandPointers = false)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-module-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        NativePluginExecutionDomain? domain = null; NativePluginPrivateIo? io = null; NativeNvseHostSource? host = null;
        try
        {
            var declaration = NativePluginImageDeclarations.Read(paths[1], dll: true);
            var imports = declaration.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
                .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
            var runtimeDirectory = Path.GetDirectoryName(paths[3])!;
            var selection = new NativePluginIoSelection(HashText("authored-module-set\0" + hashes[paths[1]] + hashes[paths[2]]), hashes[paths[1]], paths[1],
                runtimeDirectory, Path.Combine(temporary, "private-state"),
                [runtimeDirectory, Path.GetDirectoryName(paths[4])!, Path.GetDirectoryName(paths[1])!, Path.GetDirectoryName(paths[2])!],
                [], _ => null, "actual-authored-module-imports", imports);
            io = new NativePluginPrivateIo(selection);
            host = NativeNvseHostSource.Open(paths[3], hashes[paths[3]], paths[4], hashes[paths[4]], selection.StackSha256, false,
                "authored-owned-standard-edition-host");
            io.BindSourceAddressSpace(host); domain = new(paths[0], TimeSpan.FromSeconds(30), privateIo: io);
            var generation = domain.Generation;
            var primary = domain.LoadNvseImage(host, paths[1], hashes[paths[1]], sourcePluginHandle: 11);
            var table = new ActualModuleTable(domain, selection.StackSha256);
            domain.AttachNvseCommandTable(primary, table);
            Require(domain.QueryNvse(primary).Returned && domain.InitializeNvse(primary, 1).Returned, "primary original Query/Load returned");
            var secondary = domain.LoadCoResidentNvseImage(paths[2], hashes[paths[2]], 22);
            Require(domain.QueryNvse(secondary).Returned && domain.InitializeNvse(secondary, 2).Returned, "secondary original Query/Load returned");
            Require(primary.Module != secondary.Module && primary.Image != secondary.Image && primary.Interface == secondary.Interface &&
                domain.NvseModules.Count == 2, "actual distinct mappings, independent source registrations, shared core allocation");
            RequireThrows<NotSupportedException>(() => domain.RequireNvseModuleSetColdState(), "current original statics and pointers are not a whole cold save");
            if (nullPeer)
            {
                var listener = Borrow(secondary, secondary, "ModuleListenNull", NativePluginAbi.Stdcall, 0);
                RequireThrows<NativePluginDomainFaultException>(() => domain.InvokeNvseFunction(listener, null, []),
                    "actual original NULL-sender listener refuses real peers before an empty subscription success");
                Require(listener.Entered && secondary.Registry.ListenerHistory.Count == 0 && secondary.Registry.UnownedRequests.Any(row =>
                    row.Operation.Contains("loaded-peer subscriptions", StringComparison.Ordinal)) &&
                    domain.NativeException?.Observation.PriorCallbackCode == 50,
                    "actual unsupported callback/native failure retains its peer demand and publishes no successful listener");
                domain.Dispose(); Require(domain.ChildExited && domain.ResourcesRetired, "refused peer caller retires its exact child and source leases");
                return generation;
            }
            var readPrimary = Borrow(primary, primary, "ModuleRead", NativePluginAbi.Stdcall, 1);
            var readSecondary = Borrow(secondary, secondary, "ModuleRead", NativePluginAbi.Stdcall, 1);
            Require(Read(readPrimary, 0) == primary.Interface && Read(readSecondary, 0) == secondary.Interface &&
                Read(readPrimary, 1) == 11 && Read(readSecondary, 1) == 22 && Read(readPrimary, 2) == 1 && Read(readSecondary, 2) == 2,
                "original Query and Load handles remain distinct from the currently executing image");
            Require(Read(readPrimary, 3) == primary.Image && Read(readSecondary, 3) == secondary.Image &&
                Read(readPrimary, 4) == domain.NativeThread && Read(readSecondary, 4) == domain.NativeThread,
                "real DllMain image/thread declarations in the same child");
            Require(Read(readPrimary, 7) == uint.MaxValue && Read(readSecondary, 7) == uint.MaxValue, "GetPluginHandle outside actual Query/Load remains invalid");
            Require(primary.Registry.Commands.Single().AssignedOpcode == 0x3100 && secondary.Registry.Commands.Single().AssignedOpcode == 0x3200,
                "independent original opcode and command registrations");
            var cdecl = Borrow(primary, secondary, "ModuleCdecl4", NativePluginAbi.Cdecl, 4);
            Require(domain.InvokeNvseFunction(cdecl, null, [0x12345678, 2, 3, 4]).RawEax == (0x12345678U ^ 2U ^ 3U ^ 4U),
                "actual original cdecl caller cleanup and raw result");
            domain.ReleaseNvseFunction(cdecl);
            var hardware = Borrow(primary, primary, "ModuleHardware", NativePluginAbi.Stdcall, 0);
            var selectedHardware = io.ParallelHardwareProvider ?? throw new InvalidDataException("Real hardware provider declaration is absent.");
            var actualHardware = domain.ParallelHardwareBinding ?? throw new InvalidDataException("Actual hardware IAT publication is absent.");
            Require(actualHardware.Sha256 == selectedHardware.Sha256 && actualHardware.Rva == selectedHardware.Rva &&
                (ulong)actualHardware.Module + actualHardware.Rva == actualHardware.Address,
                "genuine source provider/export/IAT association");
            Require(domain.InvokeNvseFunction(hardware, null, []).RawEax == Read(readPrimary, 6), "original cached hardware stdcall/uint32 result");
            var receiver = domain.AllocateGuest(12, NativePluginGuestAccess.ReadWrite, new byte[12]);
            var task0 = Borrow(primary, secondary, "ModuleTask0", NativePluginAbi.Thiscall, 0);
            var task1 = Borrow(primary, secondary, "ModuleTask1", NativePluginAbi.Thiscall, 1);
            var task4 = Borrow(primary, secondary, "ModuleTask4", NativePluginAbi.Thiscall, 4);
            Require(domain.InvokeNvseFunction(task0, receiver, []).RawEax == 0, "actual source ECX with zero stack words");
            Require(domain.InvokeNvseFunction(task1, receiver, [0xf1234567]).RawEax == 0xf1234567, "actual raw uint32 argument and EAX bits");
            Require(domain.InvokeNvseFunction(task4, receiver, [0x3f800000, 2, 3, 4]).RawEax == (0x3f800000U ^ 2U ^ 3U ^ 4U),
                "original callee cleanup and four raw words");
            var relay = Borrow(primary, primary, "ModuleRelay", NativePluginAbi.Stdcall, 4);
            var priorPrimary = primary.Registry.InterfaceQueries.Count; var priorSecondary = secondary.Registry.InterfaceQueries.Count;
            var relayed = domain.InvokeNvseFunction(relay, null, [task4.Address, receiver.Address, 0x12345678, 0xabcdef12]);
            Require(relayed.RawEax == (0x12345678U ^ 0xabcdef12U ^ 0xa55aa55aU ^ 0x55aa55aaU) &&
                primary.Registry.InterfaceQueries.Count == priorPrimary + 2 && secondary.Registry.InterfaceQueries.Count == priorSecondary + 1,
                "original A calls retained B code and B callbacks bind B before A resumes");
            var bytes = domain.ReadGuest(receiver, 0, 12);
            Require(BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) == uint.MaxValue,
                "real mutable receiver stores and invalid nonloading handle survive actual native calls");
            RequireThrows<InvalidOperationException>(() => domain.UnloadNvse(secondary), "live source function prevents provider retirement");
            if (automaticDispose)
            {
                NativeNvseFunctionBorrow? crossCommand = null;
                var consumer = reverseCommandPointer ? primary : secondary;
                var provider = reverseCommandPointer ? secondary : primary;
                if (commandPointers)
                {
                    crossCommand = Borrow(consumer, consumer, "ModuleCallCommand", NativePluginAbi.Stdcall, 1);
                    Require(domain.InvokeNvseFunction(crossCommand, null, [reverseCommandPointer ? 0x3200U : 0x3100U]).RawEax ==
                        (reverseCommandPointer ? 2U : 1U), "original automatic-retirement consumer actually borrows and calls its peer command");
                    Require(domain.InvokeNvseFunction(crossCommand, null, [reverseCommandPointer ? 0x3200U : 0x3100U]).RawEax ==
                        (reverseCommandPointer ? 2U : 1U), "duplicate original command publication keeps one live retirement dependency");
                }
                if (cyclicCommandPointers)
                {
                    var opposite = Borrow(provider, provider, "ModuleCallCommand", NativePluginAbi.Stdcall, 1);
                    Require(domain.InvokeNvseFunction(opposite, null, [reverseCommandPointer ? 0x3100U : 0x3200U]).RawEax ==
                        (reverseCommandPointer ? 1U : 2U), "original commands actually retain both directions of the publication cycle");
                    RequireThrows<NativePluginDomainFaultException>(() => domain.Dispose(), "publication cycle refuses orderly original retirement");
                    Require(domain.Fault?.Reason.Contains("cyclic native publication borrows", StringComparison.Ordinal) == true &&
                        primary.Retirement is null && secondary.Retirement is null && !domain.NaturallyRetired &&
                        domain.ChildExited && domain.ResourcesRetired,
                        "failed cycle retains its source refusal and closes the exact child without a natural unload receipt");
                    return generation;
                }
                domain.Dispose();
                Require(!task0.Live && !task1.Live && !task4.Live && !relay.Live && !hardware.Live && !readPrimary.Live && !readSecondary.Live &&
                    (crossCommand is null || !crossCommand.Live) &&
                    primary.Phase == NativeNvsePhase.Retired && secondary.Phase == NativeNvsePhase.Retired &&
                    domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired && domain.Fault is null,
                    "automatic Dispose releases all retained functions before dependency-ordered original retirement and native receiver cleanup");
                if (commandPointers)
                    Require(consumer.Retirement?.InterfaceMappingPresent == true && provider.Retirement?.InterfaceMappingPresent == false,
                        "automatic Dispose retires the genuine command consumer before its provider in either module order");
                return generation;
            }
            if (fail)
            {
                var raise = Borrow(primary, secondary, "ModuleRaise", NativePluginAbi.Stdcall, 0);
                RequireThrows<NativePluginDomainFaultException>(() => domain.InvokeNvseFunction(raise, null, []), "genuine original cross-image exception stays fatal");
                Require(raise.Entered && domain.NativeException?.Observation.Code == 0xe04e5677, "failed invocation retains actual native exception/prefix");
                domain.Dispose(); Require(domain.ChildExited && domain.ResourcesRetired, "failed exact child closes before its source files");
                return generation;
            }
            domain.ReleaseNvseFunction(task0); domain.ReleaseNvseFunction(task1); domain.ReleaseNvseFunction(task4); domain.ReleaseNvseFunction(relay);
            domain.ReleaseGuest(receiver); domain.ReleaseNvseFunction(hardware); domain.ReleaseNvseFunction(readPrimary);
            if (commandPointers)
            {
                var crossCommand = Borrow(secondary, secondary, "ModuleCallCommand", NativePluginAbi.Stdcall, 1);
                Require(domain.InvokeNvseFunction(crossCommand, null, [0x3100]).RawEax == 1,
                    "original B queries the actual command pointer and calls original A with its authored public frame");
                domain.ReleaseNvseFunction(crossCommand); domain.ReleaseNvseFunction(readSecondary);
                RequireThrows<NativePluginDomainRefusal>(() => domain.UnloadNvse(primary), "public command pointer blocks its live source DLL retirement");
                Require(domain.UnloadNvse(secondary).InterfaceMappingPresent && !domain.UnloadNvse(primary).InterfaceMappingPresent,
                    "retiring actual consumer releases its pointer borrow before the provider and shared core");
                domain.Dispose(); Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired, "real source command publication retirement");
                return generation;
            }
            // A can retire while B continues using the same genuine core pages.
            var retiredPrimary = domain.UnloadNvse(primary);
            Require(!retiredPrimary.ImageMappingPresent && retiredPrimary.InterfaceMappingPresent && Read(readSecondary, 0) == secondary.Interface &&
                Read(readSecondary, 7) == uint.MaxValue, "independent original retirement preserves the surviving shared host");
            domain.ReleaseNvseFunction(readSecondary);
            Require(!domain.UnloadNvse(secondary).InterfaceMappingPresent, "last original DLL releases the shared core allocation");
            domain.Dispose();
            Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired && domain.Fault is null, "actual natural all-module/native provider retirement");
            return generation;

            NativeNvseFunctionBorrow Borrow(NativeNvsePlugin caller, NativeNvsePlugin provider, string name, NativePluginAbi abi, uint words)
            {
                var export = NativePluginImageDeclarations.Read(provider.Path, dll: true).Exports.Single(row => row.Name == name);
                return domain.BorrowNvseFunction(caller, provider, new(export.Rva, abi, words, name));
            }
            uint Read(NativeNvseFunctionBorrow function, uint field) => domain.InvokeNvseFunction(function, null, [field]).RawEax;
        }
        catch (NativePluginDomainFaultException failure) { domain ??= failure.Owner; throw; }
        finally
        {
            try { domain?.Dispose(); }
            finally
            {
                if (domain is null || domain.ChildExited)
                {
                    io?.Dispose(); host?.Dispose();
                    var target = Path.GetFullPath(temporary); var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("opennv-module-set-", StringComparison.Ordinal))
                        throw new InvalidDataException("Authored module-set cleanup escaped its exact temporary workspace.");
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                }
            }
        }
    }
    private sealed class ActualModuleTable(NativePluginExecutionDomain domain, string source) : NativeNvseCommandTableAuthority
    {
        internal override string SourceIdentity => source;
        internal override void RequireCurrent()
        {
            if (domain.ChildExited || domain.Fault is not null) throw new InvalidOperationException("Authored command authority lost its actual child/module generation.");
        }
        internal override IReadOnlyList<NativeNvseCommand> Commands() => domain.NvseModules
            .Where(row => row.Phase is NativeNvsePhase.Loading or NativeNvsePhase.LoadedTrue).SelectMany(row => row.Registry.Commands).ToArray();
        internal override IReadOnlyList<NativeNvseCommandTableModule> LoadedModules() => domain.NvseModules.Where(row => row.Phase == NativeNvsePhase.LoadedTrue)
            .Select(row => new NativeNvseCommandTableModule(row.Generation, row.Module, row.Handle, row.Path, row.Sha256, row.QueryReceipt!.Info)).ToArray();
    }
    private static NativePluginImageDeclaration Declaration(IReadOnlyList<NativePluginImageImport> imports, bool delay = false) =>
        new("authored.dll", new string('A', 64), true, imports, [], [], [], delay);
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Require(bool value, string reason) { if (!value) throw new InvalidDataException("Native module set: " + reason); }
    private static void RequireThrows<T>(Action action, string reason) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Native module set did not refuse: " + reason);
    }
}
