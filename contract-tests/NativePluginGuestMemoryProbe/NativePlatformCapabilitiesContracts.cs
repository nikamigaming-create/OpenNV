using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePlatformCapabilitiesContracts
{
    private const uint Breakpoint = 0x80000003;
    private sealed record CallerStage(string Step, bool Passed);

    // Public gate: declarations alone never claim that original DLLs executed.
    internal static void Run()
    {
        RequireCallerDiagnosticFraming();
        Require(NativePluginPlatformImports.Owner("kernel32.dll", "TryAcquireSRWLockShared") is not null &&
            NativePluginPlatformImports.Owner("kernelbase.dll", "TryAcquireSRWLockShared") is not null &&
            NativePluginPlatformImports.Owner("kernel32.dll", "DebugBreak") is not null &&
            NativePluginPlatformImports.Owner("kernel32.dll", "GetStartupInfoW") is not null &&
            NativePluginPlatformImports.Owner("kernel32.dll", "GetStdHandle") is not null &&
            NativePluginPlatformImports.Owner("kernelbase.dll", "GetStdHandle") is not null &&
            NativePluginPlatformImports.Owner("kernel32.dll", "SetStdHandle") is null,
            "selected Windows synchronization/exception/startup declarations");
        Require(NativePluginPlatformImports.Owner("unselected.dll", "TryAcquireSRWLockShared") is null &&
            NativePluginPlatformImports.Owner("kernel32.dll", "tryacquiresrwlockshared") is null,
            "foreign library and altered case rejected");
        Require(NativePluginCppRuntimeImports.Owns("msvcp140.dll", NativePluginCppRuntimeImports.RandomDevice) &&
            NativePluginCppRuntimeImports.Owner("msvcp140.dll", NativePluginCppRuntimeImports.RandomDevice) is not null &&
            !NativePluginCppRuntimeImports.Owns("msvcp140_atomic_wait.dll", NativePluginCppRuntimeImports.RandomDevice) &&
            NativePluginCppRuntimeImports.Owner("msvcp140.dll", NativePluginCppRuntimeImports.RandomDevice + "x") is null,
            "exact Microsoft uint32/cdecl decorated export selection");
        foreach (var name in new[] { "__std_create_threadpool_work", "__std_bulk_submit_threadpool_work",
            "__std_wait_for_threadpool_work_callbacks", "__std_close_threadpool_work" })
            Require(NativePluginPlatformImports.Owner("msvcp140_atomic_wait.dll", name) is null,
                "worker callback/source lifetime remains explicitly unowned: " + name);
        Require(NativePluginPlatformImports.Owner("msvcp140_atomic_wait.dll", "__std_parallel_algorithms_hw_threads") is not null &&
            NativePluginPlatformImports.Owner("unselected.dll", "__std_parallel_algorithms_hw_threads") is null,
            "exact real cached hardware provider is independent of worker callback lifetime");
        Console.WriteLine("OPENNV_NATIVE_PLATFORM_IMPORT_DECLARATIONS_PASS actualCallableProof=UNEXECUTED");
    }

    internal static void Run(string companion, string fixture, string unhandledFixture, string runtime, string nvse)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Actual Windows/MSVC caller proof requires a Windows x64 host and x86 companion.");
        Run();
        var paths = new[] { companion, fixture, unhandledFixture, runtime, nvse }.Select(Path.GetFullPath).ToArray();
        var originals = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        var first = RunGeneration(paths[0], paths[1], paths[3], paths[4], originals, unhandled: false);
        var failed = RunGeneration(paths[0], paths[2], paths[3], paths[4], originals, unhandled: true);
        var retry = RunGeneration(paths[0], paths[1], paths[3], paths[4], originals, unhandled: false);
        Require(first != failed && failed != retry && first != retry, "independent retry/source/provider generations");
        foreach (var row in originals) Require(Hash(row.Key) == row.Value, "unchanged source/provider " + Path.GetFileName(row.Key));
        Console.WriteLine("OPENNV_NATIVE_PLATFORM_CAPABILITIES_PASS actualWindows=true byteReturn=true contention=true sameThreadRelease=true " +
            "x86Stack=true actualMsvcEntropy=true actualBreakpoint=true actualStartupInfo=true privateStandardHandles=true retirement=true freshRetry=true originalModCompatibility=UNACCEPTED");
    }

    private static ulong RunGeneration(string companion, string fixture, string runtime, string nvse,
        Dictionary<string, string> originals, bool unhandled)
    {
        var module = NativePluginImageDeclarations.Read(fixture, dll: true);
        Require(module.Imports.Any(row => row.Library == "kernel32.dll" && row.Name == "TryAcquireSRWLockShared") &&
            module.Imports.Any(row => row.Library == "kernel32.dll" && row.Name == "DebugBreak") &&
            module.Imports.Any(row => row.Library == "kernel32.dll" && row.Name == "GetStartupInfoW") &&
            module.Imports.Any(row => row.Library == "kernel32.dll" && row.Name == "GetStdHandle") &&
            module.Imports.Any(row => row.Library == NativePluginCppRuntimeImports.Library && row.Name == NativePluginCppRuntimeImports.RandomDevice),
            "authored original IAT carries the genuine callable contracts");
        var cpp = NativePluginCppRuntimeImports.Read(fixture, originals[fixture])
            ?? throw new InvalidDataException("Authored entropy caller lacks its selected MSVC provider.");
        Require(cpp.Exports.Single(row => row.Name == NativePluginCppRuntimeImports.RandomDevice) is { Executable: true, Rva: > 0 },
            "source-declared direct executable RNG export");
        originals.TryAdd(cpp.Path, Hash(cpp.Path)); originals.TryAdd(cpp.UcrtPath, Hash(cpp.UcrtPath));
        var temporary = Directory.CreateTempSubdirectory("opennv-platform-callers-").FullName;
        var directory = Path.GetDirectoryName(runtime)!;
        var declarations = module.Imports.Where(row => row.Name is not null && NativePluginPlatformImports.Owner(row.Library, row.Name) is not null)
            .Select(row => row.Library + "!" + row.Name).ToHashSet(StringComparer.Ordinal);
        var stack = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("authored-Windows-MSVC-caller\0" + originals[fixture])));
        var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, directory,
            Path.Combine(temporary, "private-state"), [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!],
            [], _ => null, "actual-authored-Windows-MSVC-import-declarations", declarations);
        NativePluginExecutionDomain? domain = null;
        NativePluginPrivateIo? io = null;
        NativeNvseHostSource? host = null;
        try
        {
            io = new NativePluginPrivateIo(selection);
            host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack,
                false, "authored-platform-caller-selected-owned-standard-edition");
            io.BindSourceAddressSpace(host);
            domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
            var generation = domain.Generation;
            if (unhandled)
            {
                var failure = Reject<NativePluginDomainFaultException>(() => domain.QueryNvse(plugin));
                var stages = ReadCallerStages(io.ModuleRoot);
                Require(stages.Count > 0 && stages.All(row => row.Passed) && stages[^1].Step == "query:enter-unhandled-breakpoint",
                    "actual unhandled caller did not finish its preceding providers/ABI stages: " + DescribeStages(stages));
                var retained = domain.NativeException ?? throw new InvalidDataException("Actual unhandled DebugBreak lost its Windows exception receipt.");
                var observed = retained.Observation;
                Require(failure.Fault.NativeCode == Breakpoint && retained.Generation == generation && retained.ProcessId == domain.ProcessId &&
                    retained.Operation == NativePluginDomainOperation.NvseQuery && retained.Call == failure.Fault.Call &&
                    observed.InvocationEntry == plugin.Query && observed.Thread == domain.NativeThread &&
                    observed.Code == Breakpoint && observed.RecordPresent && observed.RecordCode == Breakpoint &&
                    observed.ContextPresent && observed.ControlPresent && observed.ExceptionAddress != 0 && observed.ProgramCounter != 0 &&
                    observed.PriorCallbackCode == 0 && !observed.ParametersTruncated,
                    "genuine breakpoint preserves code, source entry, thread, call and native context");
                Reject<NativePluginDomainFaultException>(() => domain.QueryNvse(plugin));
                domain.Dispose(); domain.Dispose();
                Require(domain.ChildExited && domain.ResourcesRetired && !domain.NaturallyRetired &&
                    ReferenceEquals(domain.NativeException, retained), "fault remains retained across actual child retirement");
            }
            else
            {
                var queried = domain.QueryNvse(plugin);
                var queryStages = ReadCallerStages(io.ModuleRoot);
                Require(queried.Returned && queried.StackDelta == 0 && queried.PreservedRegisters == 7 &&
                    queried.ExceptionCode == 0 && queried.Info.Version == 18,
                    $"actual authored Query returned={queried.Returned} stack={queried.StackDelta} preserved={queried.PreservedRegisters} " +
                    $"exception={queried.ExceptionCode:x8} version={queried.Info.Version}; caller stages: " + DescribeStages(queryStages));
                Require(queryStages.Count > 0 && queryStages.All(row => row.Passed) && queryStages[^1].Step == "query:complete",
                    "actual successful Query omitted a caller result: " + DescribeStages(queryStages));
                Require(queryStages.Any(row => row.Step == "provider:Random_device-original-IAT" && row.Passed) &&
                    queryStages.Any(row => (row.Step is "provider:Random_device-direct-export" or "provider:Random_device-source-thunk") && row.Passed),
                    "actual randomness caller omitted its original named IAT/export/callable proof");
                var binding = domain.CppRuntimeBindings.Single(row => row.Name == NativePluginCppRuntimeImports.RandomDevice);
                Require(binding.Generation == generation && binding.Sha256 == cpp.Sha256 &&
                    binding.Path.Equals(cpp.Path, StringComparison.OrdinalIgnoreCase) && binding.Executable &&
                    (ulong)binding.Module + binding.Rva == binding.Address,
                    "unchanged actual selected MSVC export identity and code address");
                var loaded = domain.InitializeNvse(plugin);
                var loadStages = ReadCallerStages(io.ModuleRoot);
                Require(loaded.Returned && loaded.StackDelta == 0 && loaded.PreservedRegisters == 7 && loaded.ExceptionCode == 0,
                    $"actual authored Load returned={loaded.Returned} stack={loaded.StackDelta} preserved={loaded.PreservedRegisters} " +
                    $"exception={loaded.ExceptionCode:x8}; caller stages: " + DescribeStages(loadStages));
                Require(loadStages.Count > queryStages.Count && loadStages.All(row => row.Passed) && loadStages[^1].Step == "load:complete",
                    "actual successful Load omitted a caller result: " + DescribeStages(loadStages));
                Reject<NotSupportedException>(() => domain.RequirePrivateCrtSaveOwned());
                domain.UnloadNvse(plugin); domain.Dispose();
                Require(domain.ChildExited && domain.NaturallyRetired && domain.ResourcesRetired && domain.Fault is null,
                    "actual original DllMain same-thread shared release and provider/child retirement");
            }
            return generation;
        }
        catch (NativePluginDomainFaultException error)
        {
            domain ??= error.Owner;
            Console.Error.WriteLine($"OPENNV_NATIVE_PLATFORM_CAPABILITIES_FAILURE childExited={error.Owner.ChildExited} nativeCode={error.Fault.NativeCode:x8}");
            throw;
        }
        finally
        {
            domain?.Dispose();
            if (domain is null || domain.ChildExited)
            {
                io?.Dispose(); host?.Dispose();
                if (io?.HasFailedChildConstruction == true)
                    throw new InvalidDataException("Authored platform caller retains an unfinished native child.");
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var target = Path.GetFullPath(temporary);
                if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(target).StartsWith("opennv-platform-callers-", StringComparison.Ordinal))
                    throw new InvalidDataException("Authored platform cleanup escaped its exact temporary workspace.");
                Directory.Delete(target, true);
            }
        }
    }

    private static IReadOnlyList<CallerStage> ReadCallerStages(string moduleRoot)
    {
        using var input = new FileStream(Path.Combine(moduleRoot, "Diagnostic", "stderr.private.log"),
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > 16384) throw new InvalidDataException("Authored caller diagnostic exceeded its fixed bounded extent.");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        return ParseCallerStages(bytes);
    }
    private static IReadOnlyList<CallerStage> ParseCallerStages(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 16384) throw new InvalidDataException("Authored caller diagnostic exceeded its fixed bounded extent.");
        if (bytes.IsEmpty) return [];
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Actual authored caller diagnostic has invalid UTF8; bounded raw bytes=" +
                Convert.ToHexString(bytes[..Math.Min(bytes.Length, 64)]), error);
        }
        var lines = text.Split('\n');
        if (lines.Length > 129) throw new InvalidDataException("Authored caller diagnostic exceeded its fixed stage count.");
        if (lines[^1].Length != 0)
            throw InvalidCallerLine(lines.Length, lines[^1], "missing final LF");
        var result = new List<CallerStage>(lines.Length - 1);
        for (var index = 0; index < lines.Length - 1; ++index)
        {
            var line = lines[index];
            var separator = line.LastIndexOf('=');
            if (separator is < 1 or > 63 || line[(separator + 1)..] is not ("pass" or "fail") ||
                line[..separator].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not (':' or '-' or '_')))
                throw InvalidCallerLine(index + 1, line, "incomplete named step/result");
            result.Add(new(line[..separator], line[(separator + 1)..] == "pass"));
        }
        return result;
    }
    private static InvalidDataException InvalidCallerLine(int lineNumber, string line, string reason) => new(
        $"Actual authored caller diagnostic {reason} at line {lineNumber}, length={line.Length}, " +
        "bounded raw=" + JsonSerializer.Serialize(line[..Math.Min(line.Length, 160)]));
    private static void RequireCallerDiagnosticFraming()
    {
        var parsed = ParseCallerStages("provider:Random_device=pass\nstandard:stdin-stdcall4-and-registers=fail\n"u8);
        Require(parsed.Count == 2 && parsed[0] is { Step: "provider:Random_device", Passed: true } &&
            parsed[1] is { Step: "standard:stdin-stdcall4-and-registers", Passed: false },
            "exact original LF framing and underscore label");
        var malformed = Reject<InvalidDataException>(() => ParseCallerStages("provider:Random_device=pass\0\n"u8));
        Require(malformed.Message.Contains("bounded raw=", StringComparison.Ordinal) &&
            malformed.Message.Contains("\\u0000", StringComparison.Ordinal), "malformed actual line stays bounded and visible");
        Reject<InvalidDataException>(() => ParseCallerStages("query:complete=pass"u8));
        Reject<InvalidDataException>(() => ParseCallerStages("query:complete=pass\n\n"u8));
    }
    private static string DescribeStages(IReadOnlyList<CallerStage> stages) => stages.Count == 0 ? "<none>" :
        string.Join(", ", stages.TakeLast(8).Select(row => row.Step + "=" + (row.Passed ? "pass" : "fail")));

    private static string Hash(string path)
    { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static T Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidDataException("Expected " + typeof(T).Name); }
}
