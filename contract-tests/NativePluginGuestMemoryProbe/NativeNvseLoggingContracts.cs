using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeNvseLoggingContracts
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("OPENNV_NVSE_LOGGING_CONTRACT_SKIPPED actualWindows=absent"); return;
        }
        var temporary = Directory.CreateTempSubdirectory("opennv-nvse-logging-").FullName;
        try
        {
            var config = Path.Combine(temporary, "source.ini");
            foreach (var (body, expected) in new[]
            {
                ("[Other]\r\nsPluginLogPath=ignored\r\n", ""),
                ("[Logging]\r\nsPluginLogPath=\r\n", ""),
                ("[logging]\r\nsPluginLogPath=\"Logs\\authored\"\r\n", "Logs\\authored\\"),
                ("[Logging]\r\nsPluginLogPath=Logs\\authored\\\r\n", "Logs\\authored\\\\"),
                ("[Logging]\r\nsPluginLogPath=Logs\\.hidden\r\n", "Logs\\.hidden\\"),
                ("[Logging]\r\nsPluginLogPath=Logs\\name.\r\n", ""),
                ("[Logging]\r\nsPluginLogPath=Logs\\invalid.log\r\n", ""),
                ("[Logging]\r\nsPluginLogPath=" + new string('a', 300) + "\r\n", new string('a', 254) + "\\")
            })
            {
                File.WriteAllText(config, body, Encoding.ASCII);
                var actual = NativeNvseLoggingSdk.ReadConfiguration(config, out var actualLastError);
                var oracle = new byte[256];
                var length = GetPrivateProfileStringA("LOGGING", "sPluginLogPath", null, oracle, 255, config);
                var oracleLastError = unchecked((uint)Marshal.GetLastPInvokeError());
                Require(actual.AsSpan().SequenceEqual(oracle.AsSpan(0, checked((int)length))), "exact original Win32 profile bytes/extent");
                Require(actualLastError == oracleLastError, "actual successful/empty Win32 profile last error observation");
                Require(NativeNvseLoggingSdk.ReturnedPath(actual).AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(expected)),
                    "source extension, slash, quote and truncation semantics");
            }
            foreach (var path in new[] { "..\\Logs", "C:\\Logs", "\\Logs", "Logs\\..\\next", "Logs\nnext" })
                Reject<NotSupportedException>(() => NativeNvseLoggingSdk.ReturnedPath(Encoding.ASCII.GetBytes(path)));
            Reject<InvalidDataException>(() => NativeNvseLoggingSdk.ReturnedPath([1, 0, 2]));
            Reject<InvalidDataException>(() => NativeNvseLoggingSdk.ReturnedPath(new byte[255]));
            var absentFile = Path.Combine(temporary, "absent.ini");
            Require(NativeNvseLoggingSdk.ReadConfiguration(absentFile, out var absentError).Length == 0, "actual absent profile result");
            var absentBuffer = new byte[256];
            var absentResult = GetPrivateProfileStringA("LOGGING", "sPluginLogPath", null, absentBuffer, 255, absentFile);
            var absentOracleError = unchecked((uint)Marshal.GetLastPInvokeError());
            Require(absentResult == 0 && absentError == absentOracleError && absentError != 0, "missing profile retains actual Windows failure rather than invented zero");
            SelectedSourceAndLifetime(temporary);
            var plugin = new NativeNvsePlugin(1, 1, 1, 0x1000, 0x2000, 0x3000, 0x4000, "authored", new('a', 64));
            Require(plugin.Registry.QueryInterface(1, 9) == 0 && plugin.Registry.QueryInterface(2, 9, loggingAvailable: true) == 1 &&
                plugin.Registry.QueryInterface(3, 1, loggingAvailable: true) == 0 &&
                plugin.Registry.QueryInterface(4, 10, loggingAvailable: true) == 0,
                "logging needs its owner and cannot admit Console or PlayerControls");
        }
        finally { Cleanup(temporary); }
        Console.WriteLine("OPENNV_NVSE_LOGGING_CONTRACT_PASS originalAnsiProfile=true selectedWinner=true sourceAbsence=true " +
            "privateDirectory=true sourceLease=true threadLifetime=true coldRebind=true retailWrites=absent nativeExecution=unverified");
    }

    internal static void RunNative(string companion, string fixture, string runtime, string nvse)
    {
        Run();
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var temporary = Directory.CreateTempSubdirectory("opennv-nvse-logging-").FullName;
        try
        {
            var config = Path.Combine(temporary, "selected", "nvse_config.ini"); Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            var relative = "OpenNVAuthoredLogging\\" + Guid.NewGuid().ToString("N");
            File.WriteAllText(config, "[Logging]\r\nsPluginLogPath=" + relative + "\r\n", Encoding.ASCII);
            NativeCrtContracts.ProtectAuthoredSource(Path.GetDirectoryName(config)!, config);
            var configHash = Hash(config); originals.Add(config, configHash); var directory = Path.GetDirectoryName(runtime)!;
            var configVirtual = Path.Combine(directory, "Data", "NVSE", "nvse_config.ini");
            var logVirtual = Path.Combine(directory, relative, "opennv_logging_fixture.log");
            var stack = new string('b', 64);
            var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, directory, Path.Combine(temporary, "state"),
                [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, Path.GetDirectoryName(config)!],
                [new(logVirtual, false, NativePluginIoRole.Diagnostic, "authored-logging-caller-log-literal")],
                path => path.Equals(configVirtual, StringComparison.OrdinalIgnoreCase) ?
                    new(config, configHash, "authored-selected-logging-configuration", true) : null,
                "authored-logging-public-imports", new HashSet<string>(StringComparer.Ordinal));
            using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack, false,
                "authored-logging-caller-selected-owned-standard-edition");
            using var io = new NativePluginPrivateIo(selection); io.BindSourceAddressSpace(host);
            using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
            Require(domain.QueryNvse(plugin).Returned && domain.InitializeNvse(plugin).Returned, "actual versionless native Query/Load caller");
            Require(domain.NvseLoggingReceipts.Count == 4 && domain.NvseLoggingReceipts.All(row =>
                row.Generation == domain.Generation && row.Module == plugin.Module && row.Thread == domain.NativeThread && row.Call != 0 &&
                row.Address == domain.NvseLoggingReceipts[0].Address && row.Source.ConfigurationWinnerSha256 == configHash),
                "actual fastcall pointer, source bytes, caller and thread receipts");
            Require(io.Receipts.Any(row => row.VirtualPath.Equals(logVirtual, StringComparison.OrdinalIgnoreCase) &&
                row.Role == NativePluginIoRole.Diagnostic && row.Transferred != 0 && row.Error == 0), "actual declared log write to private storage");
            Require(Hash(config) == configHash && !File.Exists(logVirtual) && !Directory.Exists(Path.GetDirectoryName(logVirtual)),
                "selected source bytes and absent retail directory remain unchanged");
            var log = io.Receipts.First(row => row.VirtualPath.Equals(logVirtual, StringComparison.OrdinalIgnoreCase) && row.PhysicalPath is not null).PhysicalPath!;
            Require(File.ReadAllText(log) == "authored logging caller\r\n", "genuine private log bytes");
            domain.UnloadNvse(plugin); domain.Dispose();
            Require(domain.NaturallyRetired && domain.ChildExited && domain.ResourcesRetired, "actual native pointer/source retirement");
        }
        finally
        {
            foreach (var (path, hash) in originals) Require(Hash(path) == hash, "unchanged original " + Path.GetFileName(path));
            Cleanup(temporary);
        }
        Console.WriteLine("OPENNV_NVSE_LOGGING_CALLER_PASS actualNativeFastcall=true versionWord=absent pointerStable=true " +
            "privateLogWrite=true actualRetirement=true originalModCompatibility=UNACCEPTED");
    }

    private static void SelectedSourceAndLifetime(string temporary)
    {
        var runtime = Path.Combine(temporary, "game"); var source = Path.Combine(temporary, "selected");
        Directory.CreateDirectory(runtime); Directory.CreateDirectory(source);
        var module = Path.Combine(runtime, "authored.dll"); File.WriteAllBytes(module, ModuleImage());
        var config = Path.Combine(source, "nvse_config.ini"); File.WriteAllText(config, "[Logging]\r\nsPluginLogPath=Logs\\owned\r\n", Encoding.ASCII);
        var configVirtual = Path.Combine(runtime, "Data", "NVSE", "nvse_config.ini"); var moduleHash = Hash(module); var sourceHash = Hash(config);
        var selection = new NativePluginIoSelection(new('c', 64), moduleHash, module, runtime, Path.Combine(temporary, "state"),
            [runtime, source], [], path => path.Equals(configVirtual, StringComparison.OrdinalIgnoreCase) && File.Exists(config) ?
                new(config, Hash(config), "authored-selected-profile", true) : null, "authored-no-imports", new HashSet<string>(StringComparer.Ordinal))
        { CrtProviders = [] };
        NativeNvseLoggingPath first;
        using (var io = new NativePluginPrivateIo(selection))
        {
            io.Claim(11); first = io.PrepareNvseLogging(11);
            Require(first.ConfigurationWinnerSha256 == sourceHash && first.ConfigurationPhysicalPath == config &&
                first.ReturnedBytes.AsSpan().SequenceEqual("Logs\\owned\\"u8) && Directory.Exists(first.PrivateDirectory) &&
                !Directory.Exists(Path.Combine(runtime, "Logs")), "selected config winner and actual private directory");
            Reject<IOException>(() => File.WriteAllText(config, "changed"));
            Reject<InvalidOperationException>(() => io.RequireNvseLogging(12, first));
            Exception? foreign = null; var worker = new Thread(() => { try { io.RequireNvseLogging(11, first); } catch (Exception error) { foreign = error; } });
            worker.Start(); worker.Join(); Require(foreign is InvalidOperationException, "actual thread refusal");
            Reject<NotSupportedException>(() => io.Resolve(1, 1, NativePluginIoAction.Write, Path.Combine(runtime, "Logs", "owned", "asset.dds")));
            io.RequireRetired();
        }
        Require(Hash(module) == moduleHash && Hash(config) == sourceHash, "source bytes stay read-only");
        File.Delete(config);
        using (var fresh = new NativePluginPrivateIo(selection))
        {
            fresh.Claim(12); var absent = fresh.PrepareNvseLogging(12);
            Require(absent.ConfigurationSha256 is null && absent.ReturnedBytes.IsEmpty && absent.VirtualDirectory == runtime &&
                absent.PrivateDirectory != first.PrivateDirectory, "cold source absence binds independently instead of restoring a pointer/path");
            _ = NativeNvseLoggingSdk.ReadConfiguration(absent.ConfigurationPhysicalPath, out var absentLastError);
            Require(fresh.ProfileReceipts.Single().LastError == absentLastError && absentLastError != 0,
                "source factory forwards its actual observed missing-profile error");
            fresh.RequireNvseLogging(12, absent);
            File.WriteAllText(config, "[Logging]\r\nsPluginLogPath=changed\r\n");
            Reject<InvalidDataException>(() => fresh.RequireNvseLogging(12, absent));
        }
        using (var modulePrivate = new NativePluginPrivateIo(selection with
        {
            WriteScopes = [new(configVirtual, false, NativePluginIoRole.Configuration, "authored-module-private-configuration")]
        }))
        {
            modulePrivate.Claim(13);
            // Establish an actual authored private file through the declared
            // route. This is a component contract, not a native-call receipt.
            var write = modulePrivate.Resolve(1, 1, NativePluginIoAction.Write, configVirtual);
            File.WriteAllText(write.PhysicalPath!, "[Logging]\r\nsPluginLogPath=private-override\r\n", Encoding.ASCII);
            modulePrivate.Complete(1, write.Id, 1, 0); modulePrivate.RecordHandle(1, write.Id, 2, 0, 0);
            var before = modulePrivate.ProfileReceipts.Count;
            Reject<NotSupportedException>(() => modulePrivate.PrepareNvseLogging(13));
            Require(modulePrivate.ProfileReceipts.Count == before && !Directory.Exists(Path.Combine(runtime, "private-override")),
                "module private config cannot replace global source config or fabricate an SDK read result");
        }
    }
    private static byte[] ModuleImage()
    {
        var bytes = new byte[0x600];
        Short(0, 0x5a4d); Word(0x3c, 0x80); Word(0x80, 0x4550); Short(0x84, 0x14c); Short(0x86, 2);
        Short(0x94, 0xe0); Short(0x96, 0x2102); Short(0x98, 0x10b); Word(0xb4, 0x400000);
        Word(0xb8, 0x1000); Word(0xbc, 0x200); Word(0xd0, 0x3000); Word(0xd4, 0x200); Short(0xdc, 3); Word(0xf4, 16);
        Section(0x178, ".text", 0x1000, 0x200, 0x60000020); Section(0x1a0, ".rdata", 0x2000, 0x400, 0x40000040);
        bytes[0x200] = 0xc3; return bytes;
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        void Short(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void Section(int at, string name, uint rva, uint file, uint flags)
        { Encoding.ASCII.GetBytes(name).CopyTo(bytes, at); Word(at + 8, 0x200); Word(at + 12, rva); Word(at + 16, 0x200); Word(at + 20, file); Word(at + 36, flags); }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException("NVSE logging: " + reason); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Expected logging refusal " + typeof(T).Name); }
    private static void Cleanup(string directory)
    {
        if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(directory).StartsWith("opennv-nvse-logging-", StringComparison.Ordinal))
            throw new InvalidDataException("Authored logging cleanup escaped its created directory.");
        Directory.Delete(directory, true);
    }
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern uint GetPrivateProfileStringA(string section, string key, string? fallback, [Out] byte[] output, uint capacity, string file);
}
