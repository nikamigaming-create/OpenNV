using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCrtPureContracts
{
    private const string TestHandler = "_set_thread_local_invalid_parameter_handler";

    internal static void Run()
    {
        var declarations = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["api-ms-win-crt-string-l1-1-0.dll"] = new HashSet<string>(StringComparer.Ordinal) { "strncat_s" },
            ["api-ms-win-crt-convert-l1-1-0.dll"] = new HashSet<string>(StringComparer.Ordinal) { "atoi" },
            ["api-ms-win-crt-math-l1-1-0.dll"] = new HashSet<string>(StringComparer.Ordinal)
                { "fmaxf", "floor", "round", "_libm_sse2_tan_precise" },
        };
        var selection = new NativePluginCrtProviderSelection("authored-provider", new('a', 64), "authored-source", declarations);
        var exports = declarations.Values.SelectMany(names => names).Select((name, index) =>
            new NativePluginImageExport(name, checked(0x1000U + (uint)index * 16), true, false)).ToArray();
        var selected = NativePluginCrtPureImports.SelectExports(selection, exports);
        Require(selected.Count == 6, "Six independent source exports are required.");
        foreach (var pair in declarations)
            foreach (var name in pair.Value)
            {
                Require(NativePluginPlatformImports.Owner(pair.Key, name) is not null &&
                    NativePluginCrtPureImports.Owns("ucrtbase.dll", name) && !NativePluginCrtImports.IsFileImport(pair.Key, name),
                    "Pure entry keeps its exact declaration and independent non-I/O lane.");
                NativePluginCrtPureImports.RequireBinding(selection, selected, pair.Key, name, 0x50000000,
                    0x50000000 + selected[name], selected[name]);
                Reject<InvalidDataException>(() => NativePluginCrtPureImports.RequireBinding(selection, selected,
                    pair.Key, name, 0x50000000, 0x50000001 + selected[name], selected[name]));
                Reject<InvalidDataException>(() => NativePluginCrtPureImports.RequireBinding(selection, selected,
                    pair.Key, name, uint.MaxValue - 1, selected[name] - 2, selected[name]));
            }
        foreach (var (library, name) in new[]
        {
            ("api-ms-win-crt-math-l1-1-0.dll", "atoi"), ("api-ms-win-crt-convert-l1-1-0.dll", "round"),
            ("vcruntime140.dll", "strncat_s"), ("ucrtbase.dll", TestHandler), ("ucrtbase.dll", "tan"),
        }) Require(!NativePluginCrtPureImports.Owns(library, name) && NativePluginPlatformImports.Owner(library, name) is null,
            "Neighbor or test-only API cannot become a production pure entry.");
        foreach (var corrupt in new[] { exports[0] with { Rva = 0 }, exports[0] with { Forwarded = true }, exports[0] with { Executable = false } })
            Reject<NotSupportedException>(() => NativePluginCrtPureImports.SelectExports(selection, exports.Skip(1).Prepend(corrupt)));
        Reject<NotSupportedException>(() => NativePluginCrtPureImports.SelectExports(selection, exports.Skip(1)));
        Reject<InvalidDataException>(() => NativePluginCrtPureImports.RequireBinding(selection, selected,
            "ucrtbase.dll", "atoi", 0x50000000, 0x50000000 + selected["atoi"], selected["atoi"]));
        Reject<InvalidDataException>(() => NativePluginCrtPureImports.RequireBinding(selection, selected,
            "api-ms-win-crt-convert-l1-1-0.dll", "atoi", 0, selected["atoi"], selected["atoi"]));
        Console.WriteLine("OPENNV_CRT_PURE_DECLARATION_CONTRACT_PASS exactImports=6 directExports=true " +
            "corruptCoordinates=refused testHandlerProductionOwner=absent nativeExecution=unverified");
    }

    internal static void RunNative(string companion, string fixture, string runtime, string nvse)
    {
        Run();
        Require(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "Pure CRT execution needs the actual x64-managed/x86-native Windows boundary.");
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var providers = NativePluginCrtImports.ReadProviders(fixture, originals[fixture]);
        Require(providers.Count == 1, "The real authored /MD import graph requires a unique genuine provider.");
        foreach (var provider in providers) originals.Add(provider.Path, Hash(provider.Path));
        Reject<InvalidDataException>(() => NativePluginCrtImports.ReadProviders(fixture, new('0', 64)));
        var source = NativePluginImageDeclarations.Read(fixture, dll: true);
        var imports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var import in source.Imports)
        {
            var name = import.Name ?? throw new InvalidDataException("Authored pure fixture unexpectedly contains an ordinal import.");
            if (NativePluginPlatformImports.Owner(import.Library, name) is null &&
                !(import.Library == "api-ms-win-crt-runtime-l1-1-0.dll" && name == TestHandler))
                throw new NotSupportedException("Authored fixture reached an independently unowned import: " + import.Library + "!" + name);
            imports.Add(import.Library + "!" + name);
        }
        Require(source.Imports.Count(row => row.Name is { } name && NativePluginCrtPureImports.Owns(row.Library, name)) == 6 &&
            source.Imports.Count(row => row.Name == TestHandler) == 1,
            "The actual optimized source must retain all six genuine IAT entries and the separate test-only returning handler.");
        var temporary = Directory.CreateTempSubdirectory("opennv-crt-pure-").FullName;
        try
        {
            var directory = Path.GetDirectoryName(runtime)!; var stack = new string('c', 64);
            var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, directory,
                Path.Combine(temporary, "state"), [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!],
                [], _ => null, "authored-six-genuine-CRT-imports", imports);
            Reject<InvalidDataException>(() =>
            {
                using var invalid = new NativePluginPrivateIo(selection with
                { CrtProviders = [providers[0] with { Sha256 = new('0', 64) }] });
            });
            using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack, false,
                "authored-pure-CRT-selected-owned-standard-edition");
            using var io = new NativePluginPrivateIo(selection); io.BindSourceAddressSpace(host);
            using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
            Require(domain.CrtPureBindings.Count == 0, "Actual pure receipts must precede Query through the real mapped runtime publication.");
            var query = domain.QueryNvse(plugin);
            Require(query.Returned && query.Info.Version == 63, "Actual fixture Query failed a concat/atoi/max/unary/rounding/tan ABI case; mask=" + query.Info.Version);
            var rows = domain.CrtPureBindings.ToArray();
            Require(rows.Length == 6 && rows.All(row => row.Generation == domain.Generation && row.Parent != 0 &&
                row.Thread == domain.NativeThread && row.Provider != 0 && row.Module != 0 && row.Address == row.Module + row.Rva &&
                row.Sha256 == providers[0].Sha256 && row.SourceOwner == providers[0].SourceOwner) &&
                rows.Select(row => row.Library + "!" + row.Name).Distinct(StringComparer.Ordinal).Count() == 6,
                "Six real IAT/provider/export-coordinate receipts must retain the actual generation/thread/source.");
            Require(domain.InitializeNvse(plugin).Returned && domain.CrtPureBindings.SequenceEqual(rows),
                "Repeated native execution changed or replayed the six source binding receipts.");
            _ = domain.UnloadNvse(plugin); domain.Dispose();
            Require(domain.ChildExited && domain.NaturallyRetired && domain.Fault is null,
                "Actual original image and retained genuine CRT provider did not retire naturally.");
            Console.WriteLine("OPENNV_NATIVE_CRT_PURE_CONTRACT_PASS exactImports=6 actualProvider=true originalIat=true " +
                "concatCanaries=true secureFailure=true returningThreadHandler=true errnoRestored=true atoiOverflow=true " +
                "nanInfinityZero=true roundingModes=true tanXmm0=true stackAndCalleeRegisters=true retirement=true " +
                "sourceBytes=unchanged originalDllCompatibility=unaccepted");
        }
        finally
        {
            try { foreach (var row in originals) Require(Hash(row.Key) == row.Value, "Pure CRT execution changed a readonly source: " + row.Key); }
            finally
            {
                var absolute = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absolute).StartsWith("opennv-crt-pure-", StringComparison.Ordinal))
                    throw new InvalidDataException("Pure CRT cleanup escaped the exact fresh test root.");
                Directory.Delete(absolute, true);
            }
        }
    }
    internal static void RunOwned(string module)
    {
        module = Path.GetFullPath(module); var digest = Hash(module);
        var original = NativePluginImageDeclarations.Read(module, dll: true);
        Require(original.Sha256 == digest, "The owned original module changed before its import audit.");
        var selected = original.Imports.Where(row => row.Name is { } name && NativePluginCrtPureImports.Owns(row.Library, name)).ToArray();
        Require(selected.Length == 6, "The selected owned module must declare the six independent reached pure imports.");
        var providers = NativePluginCrtImports.ReadProviders(module, digest);
        Require(providers.Count == 1, "Owned pure import graph has no unique genuine x86 provider source.");
        var provider = NativePluginImageDeclarations.Read(providers[0].Path, dll: true);
        Require(provider.Sha256 == providers[0].Sha256, "The actual owned provider changed before direct export validation.");
        var exports = NativePluginCrtPureImports.SelectExports(providers[0], provider.Exports);
        Require(exports.Count == 6 && selected.All(row => providers[0].Imports.TryGetValue(row.Library, out var names) && names.Contains(row.Name!)),
            "Owned six imports were omitted or substituted during provider selection.");
        Require(Hash(module) == digest && Hash(provider.Path) == provider.Sha256, "Owned source bytes changed during read-only import audit.");
        Console.WriteLine("OPENNV_OWNED_CRT_PURE_IMPORT_PASS exactImports=6 directExecutableExports=true " +
            "actualProviderSource=true unchangedModule=" + digest + " nativeExecution=unverified modCompatibility=unaccepted");
    }
    private static string Hash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Expected " + typeof(T).Name);
    }
}
