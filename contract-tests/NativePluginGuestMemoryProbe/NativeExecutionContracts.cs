using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeExecutionContracts
{
    internal static void Run(string companion, string fixtureDirectory)
    {
        Require(Environment.Is64BitProcess, "This gate requires the actual x64 C# to x86 native process boundary.");
        var fixture = Path.Combine(Path.GetFullPath(fixtureDirectory), "opennv_domain_fixture.dll");
        var files = new[] { Path.GetFullPath(companion), fixture, Path.Combine(fixtureDirectory, "opennv_domain_fixture_dependency.dll"),
            Path.Combine(fixtureDirectory, "opennv_domain_fixture_reject.dll"), Path.Combine(fixtureDirectory, "..", "missing-import", "opennv_domain_fixture.dll") };
        var identities = files.ToDictionary(Path.GetFullPath, Hash);
        try
        {
            RequireLoaderSource(fixture);
            var previous = LoaderCallsCallbacksAndRetirement(companion, fixture);
            LoaderRefusals(companion, fixtureDirectory, fixture);
            using (var fresh = new NativePluginExecutionDomain(companion))
            {
                var module = fresh.LoadAuthoredModule(fixture, Hash(fixture));
                Require(fresh.Generation != previous.Generation, "Cold process reused a retired generation.");
                Reject<InvalidOperationException>(() => fresh.Call(previous, 1, 2));
                Equal(CdeclResult(1, 2), fresh.Call(fresh.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl), 1, 2).Result,
                    "A stale-handle refusal changed the fresh native generation.");
            }
            FatalCall(companion, fixture, "OpenNvBadCleanup", expectedCode: 13, expectedReason: "stackDelta=8");
            FatalCall(companion, fixture, "OpenNvBadPreservation", expectedCode: 13, expectedReason: "registerMask=6");
            FatalCall(companion, fixture, "OpenNvBadStdcallCleanup", expectedCode: 13, abi: NativePluginAbi.Stdcall, expectedReason: "stackDelta=-8");
            FatalCall(companion, fixture, "OpenNvBadThiscallCleanup", expectedCode: 13, abi: NativePluginAbi.Thiscall, expectedReason: "stackDelta=-8");
            FatalCall(companion, fixture, "OpenNvRaise", expectedCode: 0xe04e5601);
            FatalCall(companion, fixture, "OpenNvForeignCallback", expectedCode: 1444);
            MissingCallbackOwner(companion, fixture);
            ThrowingCallbackOwner(companion, fixture);
            ReentrantDepthRefusal(companion, fixture);
            CallbackBudgetRefusal(companion, fixture);
            NativeCallbackBudgetClassification(companion, fixture);
            DeadlineRefusal(companion, fixture);
            ManagedCallbackDeadline(companion, fixture);
            NativeExecutionProtocolContracts.Run(companion);
            NativeFaultEnvelopeContracts.Run(companion);
        }
        finally
        {
            foreach (var (file, identity) in identities)
                Require(Hash(file) == identity, $"Native execution changed source bytes: {file}");
        }
        Console.WriteLine("OPENNV_NATIVE_EXECUTION_DOMAIN_CONTRACT_PASS machine=I386 tls=true imports=true dllEntry=true " +
            "cdecl=true stdcall=true thiscall=true callbacks=true nestedCalls=true abiFaults=true callbackFaults=true " +
            "deadline=true retirement=true sourceBytes=unchanged authoredDllOnly=true " +
            "unchangedPluginInitialization=absent gameObjects=absent engineHooks=absent serialization=absent NOT_DLL_COMPATIBILITY");
    }

    private static NativePluginFunction LoaderCallsCallbacksAndRetirement(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        Require(module.GuestBase != 0 && module.GuestReceiver != 0 && module.Entry.TlsAttach == 1 && module.Entry.DllAttach == 1 &&
            module.Entry.TlsOrder < module.Entry.DllOrder, "The native loader did not execute TLS before DllMain.");
        Equal(0x13, module.Entry.ImportedValue, "A declared import was not used by the actual loaded module.");
        Equal(0x746c7301, module.Entry.TlsValue, "The actual static TLS scalar is absent.");
        var cdecl = domain.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl);
        var stdcall = domain.Resolve(module, "OpenNvScalarStdcall", NativePluginAbi.Stdcall);
        var thiscall = domain.Resolve(module, "OpenNvThiscall", NativePluginAbi.Thiscall);
        var callbackCdecl = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var callbackStdcall = domain.Resolve(module, "OpenNvCallbackStdcall", NativePluginAbi.Stdcall);
        Equal(0xcdec0035, domain.Call(cdecl, 5, 7).Result, "Cdecl argument order/native result differs.");
        Equal((5U * 3 + 7) ^ 0x5dca0000U, domain.Call(stdcall, 5, 7).Result, "Stdcall argument order/return differs.");
        Equal(0x1234 + 5 + 2 * 7, domain.Call(thiscall, 5, 7).Result, "Thiscall did not use its real ECX receiver.");
        Equal(0x1235 + 1 + 2 * 2, domain.Call(thiscall, 1, 2).Result, "Thiscall did not retain the actual module object's mutation.");
        Equal(0xcdec000c, domain.Call(cdecl, uint.MaxValue, 2).Result, "DWORD overflow was replaced by host integer behavior.");
        var wrongAbi = Reject<NativePluginDomainRefusal>(() => domain.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Stdcall));
        Equal(87, wrongAbi.Code, "A declared convention mismatch was not refused before execution.");
        Equal(1, wrongAbi.LiveModules, "ABI refusal retired an unrelated valid module.");
        Equal(87, Reject<NativePluginDomainRefusal>(() => domain.Resolve(module, "OpenNvScalarStdcall", NativePluginAbi.Cdecl)).Code,
            "Stdcall was relabeled cdecl.");
        Equal(87, Reject<NativePluginDomainRefusal>(() => domain.Resolve(module, "OpenNvThiscall", NativePluginAbi.Cdecl)).Code,
            "Thiscall was relabeled cdecl.");
        Equal(127, Reject<NativePluginDomainRefusal>(() => domain.Resolve(module, "NoOwnedVariadicContract", NativePluginAbi.Cdecl)).Code,
            "An absent call contract was invented.");
        var ownerThread = Environment.CurrentManagedThreadId; var callbackCount = 0;
        domain.Callback = callback =>
        {
            Require(Environment.CurrentManagedThreadId == ownerThread && callback.Generation == domain.Generation &&
                callback.ParentCall != 0 && callback.NativeThread == domain.NativeThread, "Callback lost its source call/thread owner.");
            callbackCount++;
            Reject<InvalidOperationException>(domain.Dispose);
            Reject<InvalidOperationException>(() => domain.Unload(module));
            Reject<InvalidOperationException>(() => domain.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl));
            Reject<InvalidOperationException>(() => domain.Callback = null);
            return callback.Event switch
            {
                11 => domain.Call(cdecl, callback.First, callback.Second).Result,
                12 => domain.Call(callbackCdecl, callback.First, callback.Second).Result,
                _ => throw new InvalidDataException("Unowned callback event."),
            };
        };
        Equal(unchecked(CdeclResult(5, 7) + 0x100), domain.Call(callbackCdecl, 5, 7).Result, "Cdecl callback result did not return to native code.");
        var after = domain.Call(callbackStdcall, 9, 4);
        Equal(unchecked(CdeclResult(9, 4) + 0x100 + 0x200), after.Result, "Stdcall/reentrant callback order or return differs.");
        Require(callbackCount == 3 && after.NativeCallbacks == 3 && after.NativeCalls == 10,
            "Real native call/callback counts differ; a declaration may have stood in for execution.");
        Exception? foreignError = null;
        var foreign = new Thread(() =>
        {
            try { Reject<InvalidOperationException>(() => domain.Call(cdecl, 1, 2)); Reject<InvalidOperationException>(domain.Dispose); }
            catch (Exception error) { foreignError = error; }
        });
        foreign.Start(); Require(foreign.Join(2000), "Foreign-thread refusal did not return.");
        if (foreignError is not null) throw foreignError;
        Require(domain.Fault is null, "A caller-thread refusal changed native process ownership.");
        var detached = domain.Unload(module);
        Require(detached.Lifetime.TlsDetach == 1 && detached.Lifetime.DllDetach == 1 && !detached.MappingPresent,
            "Actual native module did not retire its TLS, entry and image mapping once.");
        Reject<InvalidOperationException>(() => domain.Call(cdecl, 1, 2));
        Reject<InvalidOperationException>(() => domain.Unload(module));
        var second = domain.LoadAuthoredModule(fixture, Hash(fixture));
        Require(second.Handle != module.Handle, "Reload reused a module capability despite generation-local retirement.");
        Reject<InvalidOperationException>(() => domain.Call(cdecl, 1, 2));
        Equal(CdeclResult(1, 2), domain.Call(domain.Resolve(second, "OpenNvScalarCdecl", NativePluginAbi.Cdecl), 1, 2).Result,
            "Reload inherited retired module call state.");
        domain.Dispose(); domain.Dispose();
        Require(domain.NaturallyRetired && domain.ChildExited && domain.Fault is null, "Native generation did not quit naturally with clean telemetry.");
        Reject<ObjectDisposedException>(() => domain.Call(cdecl, 1, 2));
        return cdecl;
    }
    private static void LoaderRefusals(string companion, string fixtureDirectory, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        Reject<InvalidDataException>(() => domain.LoadAuthoredModule(fixture, new string('0', 64)));
        var system64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "kernel32.dll");
        Reject<InvalidDataException>(() => domain.LoadAuthoredModule(system64, Hash(system64)));
        var missing = Path.Combine(fixtureDirectory, "..", "missing-import", "opennv_domain_fixture.dll");
        var refusedImport = Reject<NativePluginDomainRefusal>(() => domain.LoadAuthoredModule(missing, Hash(missing)));
        Require(refusedImport.Code == 126 && refusedImport.LiveModules == 0, "Missing actual native import did not fail without a live module.");
        var entry = Path.Combine(fixtureDirectory, "opennv_domain_fixture_reject.dll");
        var refusedEntry = Reject<NativePluginDomainRefusal>(() => domain.LoadAuthoredModule(entry, Hash(entry)));
        Require(refusedEntry.Code == 1114 && refusedEntry.LiveModules == 0, "False DllMain entry did not fail without a live module.");
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        Equal(CdeclResult(1, 2), domain.Call(domain.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl), 1, 2).Result,
            "Loader failure poisoned later owned module admission.");
        domain.Dispose(); Require(domain.ChildExited && domain.NaturallyRetired, "Recovered loader domain did not retire naturally.");
    }
    private static void FatalCall(string companion, string fixture, string export, uint expectedCode, NativePluginAbi abi = NativePluginAbi.Cdecl,
        string? expectedReason = null)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, export, abi);
        var callbackCount = 0; domain.Callback = _ => { callbackCount++; return 1; };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(failure.Fault.NativeCode == expectedCode && callbackCount == 0, $"Native ABI/exception/thread fault differs: {export}");
        if (expectedReason is not null) Require(failure.Fault.Reason.Contains(expectedReason, StringComparison.Ordinal),
            $"Native ABI fault lost its actual stack/register observation: {export}");
        RequireFaultRetirement(domain, function);
    }
    private static void MissingCallbackOwner(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(failure.Fault.Reason.Contains("no C# runtime owner", StringComparison.Ordinal), "Missing callback owner was treated as callback success.");
        RequireFaultRetirement(domain, function);
    }
    private static void ThrowingCallbackOwner(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvCallbackStdcall", NativePluginAbi.Stdcall);
        var callbacks = 0; domain.Callback = _ => { callbacks++; throw new InvalidDataException("authored-callback-owner-failure"); };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(callbacks == 1 && failure.Fault.Reason.Contains("authored-callback-owner-failure", StringComparison.Ordinal),
            "Throwing callback was retried, completed or replaced with success.");
        RequireFaultRetirement(domain, function);
    }
    private static void ReentrantDepthRefusal(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion, maximumDepth: 2);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var callbacks = 0; domain.Callback = callback => { callbacks++; return domain.Call(function, callback.First + 1, callback.Second).Result; };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(callbacks == 2 && failure.Fault.Reason.Contains("depth", StringComparison.OrdinalIgnoreCase), "Reentrant depth did not fail at its exact owning boundary.");
        RequireFaultRetirement(domain, function);
    }
    private static void CallbackBudgetRefusal(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion, maximumCallbacks: 1);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var callbacks = 0; domain.Callback = callback => { callbacks++; return domain.Call(function, callback.First, callback.Second).Result; };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(callbacks == 1 && failure.Fault.Reason.Contains("callback budget", StringComparison.OrdinalIgnoreCase), "Unbounded callback work escaped its transaction budget.");
        RequireFaultRetirement(domain, function);
    }
    private static void NativeCallbackBudgetClassification(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(15), maximumCallbacks: 64);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var outer = domain.Resolve(module, "OpenNvCallbackStdcall", NativePluginAbi.Stdcall);
        var nested = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var callbacks = 0;
        domain.Callback = callback =>
        {
            callbacks++;
            if (callback.Event == 11) return 1;
            Require(callback.Event == 12, "Native quota fixture emitted an unowned callback.");
            // Sequential nested calls reach the native quota without exhausting
            // the independent depth gate. The 65th callback must fail natively
            // before it can enter the managed callback dispatcher.
            for (uint index = 0; index < 64; index++) domain.Call(nested, index, 2);
            throw new InvalidDataException("Native quota accepted a 65th callback.");
        };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(outer, 1, 2));
        Require(callbacks == 64 && failure.Fault.NativeCode == 1816 &&
            failure.Fault.Reason.Contains("Native callback budget exceeded.", StringComparison.Ordinal),
            "Native callback quota lost its original code, reason or exact accepted extent.");
        RequireFaultRetirement(domain, nested);
    }
    private static void DeadlineRefusal(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromMilliseconds(750));
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvHang", NativePluginAbi.Cdecl);
        var clock = Stopwatch.StartNew(); var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(failure.Fault.Reason.Contains("deadline", StringComparison.Ordinal) && clock.Elapsed < TimeSpan.FromSeconds(5),
            "Native blocked code did not fault under its complete transaction deadline.");
        Require(failure.Fault.Operation == "Call" && failure.Fault.Call != 0 && failure.Fault.NativeCode is null,
            "The watchdog fault lost its actual waiting native request.");
        RequireFaultRetirement(domain, function);
    }
    private static void RequireFaultRetirement(NativePluginExecutionDomain domain, NativePluginFunction function)
    {
        Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        domain.Dispose(); domain.Dispose();
        Require(domain.Fault is not null && domain.ChildExited && !domain.NaturallyRetired,
            "A native fault lost ownership, left the child alive or claimed natural retirement.");
    }
    private static void ManagedCallbackDeadline(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromMilliseconds(750));
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture)); var function = domain.Resolve(module, "OpenNvCallbackCdecl", NativePluginAbi.Cdecl);
        var callbacks = 0; var observedWatchdogExit = false;
        domain.Callback = _ =>
        {
            callbacks++; Thread.Sleep(1200);
            observedWatchdogExit = domain.Fault is not null && domain.ChildExited;
            return 1;
        };
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, 1, 2));
        Require(callbacks == 1 && observedWatchdogExit && failure.Fault.Reason.Contains("deadline", StringComparison.Ordinal),
            "An expired managed callback returned a successful native continuation.");
        RequireFaultRetirement(domain, function);
    }
    private static void RequireLoaderSource(string fixture)
    {
        using var source = File.OpenRead(fixture); using var pe = new PEReader(source); var headers = pe.PEHeaders;
        Require(headers.CoffHeader.Machine == Machine.I386 && headers.CorHeader is null && headers.PEHeader is { Magic: PEMagic.PE32 } image &&
            image.ThreadLocalStorageTableDirectory.Size == 24 && image.ImportTableDirectory.Size > 0 && image.AddressOfEntryPoint != 0,
            "Authored fixture does not contain native PE32 TLS/import/entry declarations.");
    }
    private static uint CdeclResult(uint first, uint second) => unchecked((first + second + 0x10 + first * 5) ^ 0xcdec0000U);
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
    private static void Equal(uint expected, uint actual, string reason) => Require(expected == actual, reason);
    private static T Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T failure) { return failure; }
        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }
}
