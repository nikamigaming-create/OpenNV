using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCppStreamsContracts
{
    private static readonly string[] PositionNames =
    [NativePluginCppRuntimeImports.ClearInputState, NativePluginCppRuntimeImports.SeekInputPosition,
        NativePluginCppRuntimeImports.SeekInputOffset, NativePluginCppRuntimeImports.TellInputPosition,
        NativePluginCppRuntimeImports.StreamEndOfFile, NativePluginCppRuntimeImports.StreamFailed,
        NativePluginCppRuntimeImports.StreamBad, NativePluginCppRuntimeImports.SetStreamExceptions];

    internal static void RunDeclarations()
    {
        foreach (var name in PositionNames)
        {
            Require(NativePluginCppRuntimeImports.Owner("msvcp140.dll", name) is not null &&
                NativePluginPlatformImports.Owner("MSVCP140.DLL", name) is not null &&
                !NativePluginCppRuntimeImports.IsFileImport("msvcp140.dll", name),
                "exact retained opaque stream method declaration: " + name);
            Require(NativePluginCppRuntimeImports.Owner("unselected.dll", name) is null &&
                NativePluginCppRuntimeImports.Owner("msvcp140.dll", name + "x") is null,
                "foreign provider/altered stream ABI rejected");
        }
        Require(NativePluginCppRuntimeImports.Owner("msvcp140.dll", NativePluginCppRuntimeImports.OpenWide) is null &&
            NativePluginCppRuntimeImports.Owner("msvcp140.dll", NativePluginCppRuntimeImports.OpenNarrow) is null,
            "actual file constructors still require private path authority");
        foreach (var name in new[] { NativePluginCppRuntimeImports.StreamVirtualBaseDestructor,
            NativePluginCppRuntimeImports.LocaleWideMonths, NativePluginCppRuntimeImports.LocaleWideDays })
            Require(NativePluginCppRuntimeImports.Owner("msvcp140.dll", name) is not null &&
                NativePluginCppRuntimeImports.Owner("unselected.dll", name) is null &&
                NativePluginCppRuntimeImports.Owner("msvcp140.dll", name + "x") is null,
                "exact compiler-owned destructor and retained locale object methods: " + name);
        Console.WriteLine("OPENNV_NATIVE_CPP_POSITION_DECLARATIONS_PASS actualCallableProof=UNEXECUTED");
    }

    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Actual C++ boundary requires Windows x64/x86.");
        RunDeclarations();
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture); runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var original = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var first = RunGeneration(companion, fixture, runtime, nvse, original);
        var retry = RunGeneration(companion, fixture, runtime, nvse, original);
        Require(first != retry, "actual fresh child/provider/object generation after natural retirement");
        foreach (var row in original) Require(Hash(row.Key) == row.Value, "unchanged original source/provider " + Path.GetFileName(row.Key));
        Console.WriteLine("OPENNV_NATIVE_CPP_STREAMS_PASS actualPositionMethods=true x86Thiscall=true hiddenFpos=true " +
            "signed64Position=true eofFailureRecovery=true actualStatePredicates=true realCppExceptions=true retirement=true freshRetry=true originalModCompatibility=UNACCEPTED");
    }

    private static ulong RunGeneration(string companion, string fixture, string runtime, string nvse, Dictionary<string, string> original)
    {
        var module = NativePluginImageDeclarations.Read(fixture, dll: true);
        RequireAllImportOwners(module);
        var cpp = NativePluginCppRuntimeImports.Read(fixture, original[fixture]) ?? throw new InvalidDataException("Authored C++ caller has no selected Microsoft dependency.");
        foreach (var name in PositionNames)
        {
            Require(module.Imports.Any(row => row.Library.Equals(NativePluginCppRuntimeImports.Library, StringComparison.OrdinalIgnoreCase) && row.Name == name),
                "original authored IAT carries actual stream ABI: " + name);
            Require(cpp.Exports.Single(row => row.Name == name) is { Executable: true, Rva: > 0 },
                "actual provider declares a direct executable stream method");
        }
        original.TryAdd(cpp.Path, Hash(cpp.Path)); original.TryAdd(cpp.UcrtPath, Hash(cpp.UcrtPath));
        var temporary = Directory.CreateTempSubdirectory("opennv-cpp-streams-").FullName;
        var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
        var source = Path.Combine(sourceRoot, "input.cfg"); File.WriteAllText(source, "ABCDE", new UTF8Encoding(false));
        NativeCrtContracts.ProtectAuthoredSource(sourceRoot, source); var sourceSha256 = Hash(source);
        var directory = Path.GetDirectoryName(runtime)!;
        var virtualInput = Path.Combine(directory, "OpenNVCppAuthored.cfg"); var virtualOutput = Path.Combine(directory, "OpenNVCppAuthored.out");
        var declarations = module.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
            .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
        var selection = new NativePluginIoSelection(HashText("authored-C++-runtime\0" + original[fixture]), original[fixture], fixture,
            directory, Path.Combine(temporary, "private-state"), [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, sourceRoot],
            [new(virtualOutput, false, NativePluginIoRole.Diagnostic, "authored-C++-append-output")],
            path => path.Equals(virtualInput, StringComparison.OrdinalIgnoreCase) ? new(source, sourceSha256, "authored-full-five-byte-input", false) : null,
            "actual-authored-C++-import-declarations", declarations);
        NativePluginExecutionDomain? domain = null;
        NativePluginPrivateIo? io = null;
        NativeNvseHostSource? host = null;
        try
        {
            io = new NativePluginPrivateIo(selection);
            host = NativeNvseHostSource.Open(runtime, original[runtime], nvse, original[nvse], selection.StackSha256, false,
                "authored-host-selected-owned-standard-edition");
            io.BindSourceAddressSpace(host);
            domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            Require(domain.SourceAddressSpaceReceipt is { } space && space.Generation == domain.Generation &&
                space.ProcessId == domain.ProcessId && space.ImageBase == host.SourceAddressSpace.ImageBase &&
                space.ImageBytes == host.SourceAddressSpace.ImageBytes && space.ReservedBytes >= space.ImageBytes,
                "actual parent-created and native-adopted selected image reservation");
            var plugin = domain.LoadNvseImage(host, fixture, original[fixture]);
            var generation = domain.Generation;
            var queried = domain.QueryNvse(plugin);
            Require(queried.Returned && queried.StackDelta == 0 && queried.PreservedRegisters == 7 &&
                queried.ExceptionCode == 0 && queried.Info.Version == 3, "real original public Query and x86 ABI");
            Require(domain.CppRuntimeBindings.Count == cpp.Exports.Count && domain.CppRuntimeBindings.All(row => row.Generation == domain.Generation), "exact actual MSVCP publication");
            foreach (var name in PositionNames)
            {
                var declared = cpp.Exports.Single(row => row.Name == name);
                var bound = domain.CppRuntimeBindings.Single(row => row.Name == name);
                Require(bound.Sha256 == cpp.Sha256 && bound.Path.Equals(cpp.Path, StringComparison.OrdinalIgnoreCase) &&
                    bound.Rva == declared.Rva && bound.Executable && (ulong)bound.Module + bound.Rva == bound.Address,
                    "original real provider export/IAT identity: " + name);
            }
            var loaded = domain.InitializeNvse(plugin);
            Require(loaded.Returned && loaded.StackDelta == 0 && loaded.PreservedRegisters == 7 && loaded.ExceptionCode == 0,
                "actual C++ position/aggregate/error/stream/locale/FILE/descriptor caller");
            Require(domain.CrtDescriptorReceipts.Any(row => row.Operation == NativePluginCrtDescriptorOperation.Duplicate && row.Result >= 3) &&
                domain.CrtDescriptorReceipts.Any(row => row.Operation == NativePluginCrtDescriptorOperation.Close && row.Result == 0), "real independent UCRT descriptor alias lifetime");
            var standard = ReadSharedText(Path.Combine(io.ModuleRoot, "Diagnostic", "stderr.private.log")).Replace("\r\n", "\n", StringComparison.Ordinal);
            Require(standard.Contains("descriptor\nFILE\ncerr:91\n", StringComparison.Ordinal), "actual shared private stderr bytes and C++ global object");
            Require(standard.Contains("position:64bit/hidden-fpos/eof/fail/exceptions/abi\n", StringComparison.Ordinal),
                "actual guarded full fpos, stream failure recovery and original C++ exception proof completed");
            var written = io.Receipts.LastOrDefault(row => row.VirtualPath.Equals(virtualOutput, StringComparison.OrdinalIgnoreCase) && row.PhysicalPath is not null)
                ?? throw new InvalidDataException("C++ file path did not reach actual private write authority.");
            Require(File.ReadAllText(written.PhysicalPath!).Contains("cpp:37:125:2.5\n", StringComparison.Ordinal), "actual independent C++ file bytes");
            RequireThrows<NotSupportedException>(() => domain.RequirePrivateCrtSaveOwned(), "unowned opaque current/cold CRT state");
            domain.UnloadNvse(plugin); domain.Dispose();
            Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired && domain.Fault is null, "real child/provider/object retirement");
            foreach (var row in original) Require(Hash(row.Key) == row.Value, "unchanged original input " + Path.GetFileName(row.Key));
            Require(Hash(source) == sourceSha256, "unchanged real authored input after read-only high-position/EOF/error operations");
            return generation;
        }
        catch (NativePluginDomainFaultException error)
        {
            domain ??= error.Owner;
            Console.Error.WriteLine($"OPENNV_NATIVE_CPP_STREAMS_FAILURE childExited={error.Owner.ChildExited} nativeCode={error.Fault.NativeCode}");
            throw;
        }
        finally
        {
            domain?.Dispose();
            // Source/host objects cannot retire while the exact child still
            // owns their pointers. Failed construction also retains its child.
            if (domain is null || domain.ChildExited) { io?.Dispose(); host?.Dispose(); }
            if (domain is null || domain.ChildExited)
            {
                if (io?.HasFailedChildConstruction == true) throw new InvalidDataException("Authored C++ construction still owns an unfinished child.");
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var target = Path.GetFullPath(temporary);
                if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("opennv-cpp-streams-", StringComparison.Ordinal))
                    throw new InvalidDataException("Authored C++ cleanup escaped its exact temporary workspace.");
                Directory.Delete(target, true);
            }
        }
    }

    private static void RequireAllImportOwners(NativePluginImageDeclaration module)
    {
        var missing = module.Imports.Where(row => row.Name is not { } name || !HasOwner(row.Library, name))
            .Select(row => row.Library + "!" + (row.Name ?? "<ordinal>"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(missing.Length == 0, "complete authored original import inventory has unowned capabilities: " + string.Join(", ", missing));
        Console.WriteLine($"OPENNV_NATIVE_CPP_FIXTURE_IMPORT_INVENTORY_PASS imports={module.Imports.Count} " +
            $"msvc={module.Imports.Count(row => row.Library.Equals(NativePluginCppRuntimeImports.Library, StringComparison.OrdinalIgnoreCase))} actualCallableProof=UNEXECUTED");

        static bool HasOwner(string library, string name)
        {
            if (NativePluginCrtImports.IsFileImport(library, name))
                return NativePluginCppRuntimeImports.IsFileImport(library, name) || NativePluginCrtImports.Owned.Contains(name);
            if (NativePluginIoImports.Unowned.Contains(name)) return false;
            var windows = library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal);
            return windows && NativePluginIoImports.Owned.Contains(name) || NativePluginPlatformImports.Owner(library, name) is not null;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void RequireThrows<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException(message); }
    private static string Hash(string path) { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(input)); }
    private static string ReadSharedText(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
