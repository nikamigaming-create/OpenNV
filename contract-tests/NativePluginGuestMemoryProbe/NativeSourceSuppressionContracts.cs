using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static class NativeSourceSuppressionContracts
{
    internal static void Run()
    {
        const uint address = 0x1000;
        var code = new byte[32]; code[0] = 0xe8;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(1), 11);
        code[16] = 0xd9; // authored effectful body, deliberately not a getter
        var source = FalloutExecutableStringTable.ReadEngineCallSite(code, address, address, new('a', 64), address,
            value => value >= address && value < address + code.Length);
        Require(source.OriginalTarget == address + 16 && source.Getter is null, "CALL identity does not classify or execute an effectful target");
        Require(source.Identity != (source with { Address = address + 1, OriginalTarget = address + 17 }).Identity,
            "identical CALL bytes at different source sites cannot share a cold identity");
        var reads = 0;
        var declaration = new FalloutEngineCallSiteSource(new('a', 64), _ => source,
            _ => { ++reads; throw new NotSupportedException("authored effect owner absent"); });
        Require(ReferenceEquals(declaration.Read(address), declaration.Read(address)) && reads == 0, "retained declaration never requires getter decoding");
        Reject<NotSupportedException>(() => declaration.ReadGetter(address));
        Require(reads == 1 && declaration.Read(address).Getter is null, "failed getter inspection cannot establish another owner");
        var suppression = NativeSourceCallSiteCode.Read(address, [0x0f, 0x1f, 0x44, 0, 0], false);
        Require(suppression.Kind == NativeSourceCallSiteCodeKind.Suppressed && suppression.Target == 0, "exact NOP is suppression without a target");
        Require(NativeSourceCallSiteCode.Read(address, [0xcc, 0xcc, 0xcc, 0xcc, 0xcc], false).Kind == NativeSourceCallSiteCodeKind.Unbound,
            "initial five-trap write lease supplies no fake RET or behavior");
        Require(NativeSourceCallSiteCode.Read(address, [0xcc, 0xcc, 0xcc, 0xcc, 0xcc], false, true).Kind == NativeSourceCallSiteCodeKind.Retired,
            "retired extent is an actual trap owner");
        Reject<InvalidDataException>(() => NativeSourceCallSiteCode.Read(address, [0x0f, 0x1f, 0x44, 0, 0, 0xc3], false));
        Reject<NotSupportedException>(() => NativeSourceCallSiteCode.Read(address, [0x90, 0x90, 0x90, 0x90, 0x90], false));
        Reject<NotSupportedException>(() => NativeSourceCallSiteCode.Read(address, [0xe9, 0x1f, 0x44, 0, 0], false));
        Reject<InvalidDataException>(() => NativeSourceCallSiteCode.Read(address, [0x0f, 0x1f, 0x44, 0, 0], true));
        Reject<InvalidDataException>(() => NativeSourceCallSiteCode.Read(address, [0x0f, 0x1f, 0x44, 0, 0], false, true));
        Console.WriteLine("OPENNV_SOURCE_SUPPRESSION_CONTRACT_PASS sourceCallIdentity=true effectDecoder=unentered exactNop=true fakeReturn=absent nativeExecution=unverified productionLowCaller=unowned");
    }

    internal static void RunNative(string companion, string fixture, string runtime, string nvse, uint address, uint? neighbor = null)
    {
        Run();
        companion = Path.GetFullPath(companion); fixture = Path.GetFullPath(fixture);
        runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { companion, fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var source = FalloutExecutableStringTable.ReadEngineCallSiteSource(runtime, originals[runtime]).Read(address);
        Require(source.Getter is null, "actual owned CALL is inspected independently of its target");
        var temporary = Directory.CreateTempSubdirectory("opennv-source-suppression-").FullName;
        var cold = new Dictionary<int, (NativeSourceSuppressionCold State, NativeSourceCallSite Site, ulong Generation)>();
        if (neighbor is { } neighborAddress) {
            Require(neighborAddress != address && Math.Abs((long)neighborAddress - address) >= 5 &&
                neighborAddress / Environment.SystemPageSize == address / Environment.SystemPageSize,
                "authored cohort uses two distinct original CALL declarations on one actual SDK page");
            _ = FalloutExecutableStringTable.ReadEngineCallSiteSource(runtime, originals[runtime]).Read(neighborAddress);
        }
        try
        {
            var modes = new List<int> { 0, 0, 1, 2, 3, 4, 4, 5, 6 }; if (neighbor is not null) modes.Add(7);
            for (var ordinal = 0; ordinal < modes.Count; ++ordinal)
            {
                var mode = modes[ordinal];
                var selected = Path.Combine(temporary, "selected" + ordinal); Directory.CreateDirectory(selected);
                var config = Path.Combine(selected, "nvse_config.ini");
                File.WriteAllText(config, "[SUPPRESSION]\r\nAddress=" + address.ToString(CultureInfo.InvariantCulture) +
                    "\r\nMode=" + mode.ToString(CultureInfo.InvariantCulture) +
                    (neighbor is { } selectedNeighbor ? "\r\nNeighbor=" + selectedNeighbor.ToString(CultureInfo.InvariantCulture) : "") + "\r\n", Encoding.ASCII);
                NativeCrtContracts.ProtectAuthoredSource(selected, config);
                var configHash = Hash(config); originals.Add(config, configHash);
                var directory = Path.GetDirectoryName(runtime)!;
                var configVirtual = Path.Combine(directory, "Data", "NVSE", "nvse_config.ini");
                var stack = new string('d', 64);
                var selection = new NativePluginIoSelection(stack, originals[fixture], fixture, directory, Path.Combine(temporary, "state" + ordinal),
                    [directory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, selected], [],
                    path => path.Equals(configVirtual, StringComparison.OrdinalIgnoreCase) ? new(config, configHash, "authored-source-suppression-input", true) : null,
                    "authored-source-suppression-public-imports", new HashSet<string>(StringComparer.Ordinal) {
                        "kernel32.dll!VirtualProtect", "kernel32.dll!FlushInstructionCache", "kernel32.dll!GetCurrentProcess", "kernel32.dll!GetLastError" });
                using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse], stack, false,
                    "authored-source-suppression-selected-owned-standard-edition");
                using var io = new NativePluginPrivateIo(selection); io.BindSourceAddressSpace(host);
                using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
                var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
                Require(domain.QueryNvse(plugin).Returned, "actual authored Query");
                if (mode is 1 or 6)
                {
                    Reject<NativePluginDomainFaultException>(() => domain.InitializeNvse(plugin));
                    Require(domain.Fault is not null && domain.SourceCallSiteEvents.Count >= 3,
                        "invalid or changed cached mutations retain their actual entered SDK prefix");
                    if (mode == 1) Require(domain.SourceCallSiteFailure?.Events.Last().Failure is not null,
                        "invalid restored bytes retain independent parent readback refusal");
                    if (mode == 6) Require(domain.SourceCallSiteEvents.Any(row => row.Action == 4 && row.SdkResult == 0) &&
                        domain.SourceCallSiteEvents.Any(row => row.Action == 3 && row.SdkResult != 0), "changed duplicate retains genuine failed restore and successful flush prefix");
                    Reject<NativePluginDomainFaultException>(() => domain.InitializeNvse(plugin));
                    domain.Dispose(); Require(domain.ChildExited && domain.ResourcesRetired, "failed exact child closure before source cleanup");
                    continue;
                }
                var loaded = domain.InitializeNvse(plugin);
                if (!loaded.Returned) {
                    var lastSdk = domain.SourceCallSiteEvents.LastOrDefault(row => row.SdkResult is not null);
                    Console.WriteLine($"OPENNV_SOURCE_SUPPRESSION_CASE_FAILURE mode={mode} returned={loaded.Returned} sdkBool={lastSdk?.SdkResult} sdkLastError={lastSdk?.LastError} phase={lastSdk?.Phase}");
                }
                Require(loaded.Returned && domain.Fault is null, $"actual authored SDK results return independently of source protection restoration mode={mode}");
                var site = domain.RetainedNvseSourceCallSite(plugin, address);
                Require(site.Extent == 5 && site.Source.Getter is null && site.Source.DeclarationSha256 == source.DeclarationSha256,
                    "real five-byte lease has no invented getter/RET");
                Require(site.Protection == (mode is 4 or 7 ? 0x40U : 0x20U) && site.CachePublished,
                    "actual writable or restored protection remains separate from cache publication");
                if (mode is 4 or 7) {
                    var failed = domain.SourceCallSiteEvents.Last(row => row.Action == 4);
                    Require(failed.SdkResult == 0 && failed.LastError is > 0 && failed.RequestedAddress != failed.Address &&
                        failed.RequestedExtent == 5 && failed.PreviousProtection is null && failed.Protection == 0x40 &&
                        failed.Phase == NativeSourceCallSitePhase.Writable && failed.Failure is null && site.Failure is null,
                        "genuine failed foreign restoration preserves BOOL/LastError/address and does not manufacture restored state");
                    Require(domain.SourceCallSiteFailure is { Scope: NativeSourceCallSiteFailureScope.SdkFailureTail } retained &&
                        retained.Events.Count == 2 && retained.Events[0].SdkResult == 0 && retained.Events.Last().Phase == NativeSourceCallSitePhase.Flushed &&
                        retained.Events.All(row => row.Site == site.Id) && retained.StartEvent > 0,
                        "bounded private SDK tail includes failed restoration and later RWX flush without unrelated earlier sites");
                    Console.WriteLine($"OPENNV_SOURCE_SUPPRESSION_SDK_FAILURE mode={mode} bool={failed.SdkResult} lastError={failed.LastError} protection={failed.Protection} output=same-thread-provider-baseline-matched");
                }
                if (mode is 2 or 5) Require(domain.SourceCallSiteEvents.Count(row => row.Action == 3 && row.SdkResult != 0) == 2,
                    "duplicate and flush-before-restore invoke the genuine SDK twice");
                if (mode == 7) {
                    var cohort = domain.RetainedNvseSourceCallSite(plugin, neighbor!.Value);
                    Require(cohort.Protection == 0x40 && cohort.CachePublished && cohort.Suppressed, "second declaration/write updates the first source site's real shared-page protection");
                    Require(domain.CallNvseSourceCallSite(plugin, cohort, null).Suppressed, "shared-page source suppression remains independently callable");
                }
                if (mode == 3)
                {
                    Require(site.Code?.Kind == NativeSourceCallSiteCodeKind.Call, "original installed CALL transport remains distinct");
                    Reject<NotSupportedException>(() => domain.CallNvseSourceCallSite(plugin, site, null));
                    Reject<NotSupportedException>(() => domain.CaptureNvseSuppression(plugin, site));
                }
                else
                {
                    var first = domain.CallNvseSourceCallSite(plugin, site, null);
                    var second = domain.CallNvseSourceCallSite(plugin, site, null);
                    Require(first.Suppressed && second.Suppressed && first.Call != second.Call && first.Receiver is null && first.Result is null &&
                        first.StackDelta is null && first.PreservedRegisters is null && first.NativeGetterCalls == 0 && first.CodeSha256 == second.CodeSha256,
                        "C# consumer honors source suppression without a native target call or fabricated ABI result");
                    if (cold.TryGetValue(mode, out var previous))
                    {
                        Require(domain.Generation != previous.Generation, "cold native generation is fresh");
                        Reject<InvalidDataException>(() => domain.CallNvseSourceCallSite(plugin, previous.Site, null));
                        domain.RequireNvseSuppressionCold(plugin, site, previous.State);
                    }
                    var captured = domain.CaptureNvseSuppression(plugin, site);
                    Reject<InvalidDataException>(() => domain.RequireNvseSuppressionCold(plugin, site, captured with { CodeSha256 = new('e', 64) }));
                    Reject<InvalidDataException>(() => domain.RequireNvseSuppressionCold(plugin, site, captured with { Protection = captured.Protection == 0x40 ? 0x20U : 0x40U }));
                    Exception? foreign = null;
                    var thread = new Thread(() => { try { domain.CallNvseSourceCallSite(plugin, site, null); } catch (Exception error) { foreign = error; } });
                    thread.Start(); thread.Join(); Require(foreign is InvalidOperationException, "foreign thread cannot consume suppression");
                    cold[mode] = (captured, site, domain.Generation);
                }
                domain.UnloadNvse(plugin);
                Require(site.Phase == NativeSourceCallSitePhase.Retired && site.Protection == 0x20, "native trap retirement genuinely normalizes source protection before closure");
                Reject<InvalidOperationException>(() => domain.CallNvseSourceCallSite(plugin, site, null));
                domain.Dispose(); Require(domain.NaturallyRetired && domain.ChildExited && domain.ResourcesRetired, "actual native retirement and child closure");
            }
        }
        finally
        {
            foreach (var (path, hash) in originals) Require(Hash(path) == hash, "original source remains unchanged");
            var absolute = Path.GetFullPath(temporary); var temp = Path.GetFullPath(Path.GetTempPath());
            if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("opennv-source-suppression-", StringComparison.Ordinal))
                throw new InvalidDataException("Authored suppression cleanup escaped its exact scratch directory.");
            Directory.Delete(absolute, true);
        }
        Console.WriteLine("OPENNV_SOURCE_SUPPRESSION_CALLER_PASS actualNativeWrite=true exactProtection=true exactCache=true effectTargetCalls=0 coldRebind=true failedRestoreRwx=true duplicateSdkFlush=true cachedChangeRefusal=true sharedPageCohort=" + (neighbor is not null) + " retirement=true productionLowCaller=unowned modCompatibility=UNACCEPTED");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected suppression refusal " + typeof(T).Name); }
}
