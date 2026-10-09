using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCppStreamsContracts
{
    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Actual C++ boundary requires Windows x64/x86.");
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture); runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var original = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var module = NativePluginImageDeclarations.Read(fixture, dll: true);
        var cpp = NativePluginCppRuntimeImports.Read(fixture, original[fixture]) ?? throw new InvalidDataException("Authored C++ caller has no selected Microsoft dependency.");
        original.TryAdd(cpp.Path, Hash(cpp.Path)); original.TryAdd(cpp.UcrtPath, Hash(cpp.UcrtPath));
        var temporary = Directory.CreateTempSubdirectory("opennv-cpp-streams-").FullName;
        var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
        var source = Path.Combine(sourceRoot, "input.cfg"); File.WriteAllText(source, "ABCDE", new UTF8Encoding(false));
        NativeCrtContracts.ProtectAuthoredSource(sourceRoot, source); original.Add(source, Hash(source));
        var directory = Path.GetDirectoryName(runtime)!;
        var virtualInput = Path.Combine(directory, "OpenNVCppAuthored.cfg"); var virtualOutput = Path.Combine(directory, "OpenNVCppAuthored.out");
        var declarations = module.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
            .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
        var selection = new NativePluginIoSelection(HashText("authored-C++-runtime\0" + original[fixture]), original[fixture], fixture,
            directory, Path.Combine(temporary, "private-state"), [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, sourceRoot],
            [new(virtualOutput, false, NativePluginIoRole.Diagnostic, "authored-C++-append-output")],
            path => path.Equals(virtualInput, StringComparison.OrdinalIgnoreCase) ? new(source, original[source], "authored-full-five-byte-input", false) : null,
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
            Require(domain.QueryNvse(plugin).Returned, "real original public Query");
            Require(domain.CppRuntimeBindings.Count == cpp.Exports.Count && domain.CppRuntimeBindings.All(row => row.Generation == domain.Generation), "exact actual MSVCP publication");
            Require(domain.InitializeNvse(plugin).Returned, "actual C++ stream/locale/FILE/descriptor caller");
            Require(domain.CrtDescriptorReceipts.Any(row => row.Operation == NativePluginCrtDescriptorOperation.Duplicate && row.Result >= 3) &&
                domain.CrtDescriptorReceipts.Any(row => row.Operation == NativePluginCrtDescriptorOperation.Close && row.Result == 0), "real independent UCRT descriptor alias lifetime");
            var standard = ReadSharedText(Path.Combine(io.ModuleRoot, "Diagnostic", "stderr.private.log")).Replace("\r\n", "\n", StringComparison.Ordinal);
            Require(standard.Contains("descriptor\nFILE\ncerr:91\n", StringComparison.Ordinal), "actual shared private stderr bytes and C++ global object");
            var written = io.Receipts.LastOrDefault(row => row.VirtualPath.Equals(virtualOutput, StringComparison.OrdinalIgnoreCase) && row.PhysicalPath is not null)
                ?? throw new InvalidDataException("C++ file path did not reach actual private write authority.");
            Require(File.ReadAllText(written.PhysicalPath!).Contains("cpp:37:125:2.5\n", StringComparison.Ordinal), "actual independent C++ file bytes");
            RequireThrows<NotSupportedException>(() => domain.RequirePrivateCrtSaveOwned(), "unowned opaque current/cold CRT state");
            domain.UnloadNvse(plugin); domain.Dispose();
            Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired && domain.Fault is null, "real child/provider/object retirement");
            foreach (var row in original) Require(Hash(row.Key) == row.Value, "unchanged original input " + Path.GetFileName(row.Key));
            Console.WriteLine("OPENNV_NATIVE_CPP_STREAMS_PASS");
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
