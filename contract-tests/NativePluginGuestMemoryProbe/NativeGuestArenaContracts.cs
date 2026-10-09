using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Gameplay.State;

internal static class NativeGuestArenaContracts
{
    internal static void Run(string companion, string fixtures)
    {
        Require(Environment.Is64BitProcess, "The arena gate needs actual x64 C# and x86 native ownership.");
        var fixture = Path.Combine(Path.GetFullPath(fixtures), "opennv_domain_fixture.dll");
        var inputs = new[] { Path.GetFullPath(companion), fixture, Path.Combine(Path.GetFullPath(fixtures), "opennv_domain_fixture_dependency.dll") };
        var hashes = inputs.ToDictionary(path => path, Hash);
        try
        {
            var retired = MemoryStateReentryAndRetirement(companion, fixture);
            using (var fresh = new NativePluginExecutionDomain(companion))
            {
                Require(fresh.Generation != retired.Generation, "A cold arena reused an old generation.");
                Reject<InvalidOperationException>(() => fresh.ReadGuest(retired, 0, 1));
                var sibling = fresh.AllocateGuest(4, NativePluginGuestAccess.ReadWrite, [1, 2, 3, 4]);
                Same([1, 2, 3, 4], fresh.ReadGuest(sibling, 0, 4), "Foreign-generation refusal changed the valid arena.");
            }
            AllocationQuotasAndRollback(companion);
            MissingStateOperation(companion, fixture);
            RetiredNativeObject(companion, fixture);
            NativeProtectionFault(companion, fixture, retired: false);
            NativeProtectionFault(companion, fixture, retired: true);
            NativeProtectionDrift(companion, fixture);
            NativeStateIdentityDrift(companion, fixture);
            NativeQueryDuringDetach(companion, fixture);
        }
        finally { foreach (var pair in hashes) Require(Hash(pair.Key) == pair.Value, "Authored arena execution changed an admitted input."); }
        Console.WriteLine("OPENNV_NATIVE_GUEST_ARENA_CONTRACT_PASS allocation=actual readWrite=actual capabilities=generation-and-lifetime " +
            "stateOwner=GameplayVitals damage=actual stateThunks=cdecl-stdcall-thiscall reentry=used quarantine=true quotas=true " +
            "naturalRetirement=true authoredOnly=true originalInterfaces=absent gameObjectLayouts=absent hooks=absent serialization=absent NOT_DLL_COMPATIBILITY");
    }

    private static NativePluginGuestAllocation MemoryStateReentryAndRetirement(string companion, string fixture)
    {
        var domain = new NativePluginExecutionDomain(companion);
        NativePluginGuestAllocation? survivor = null;
        try
        {
            Equal(new NativePluginGuestStatistics(0, 0, 0, 0, 0), domain.GuestStatistics(), "A fresh native arena contains owners.");
            var writable = domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite, [0x78, 0x56, 0x34, 0x12]);
            var readOnly = domain.AllocateGuest(4, NativePluginGuestAccess.ReadOnly, [9, 8, 7, 6]);
            survivor = readOnly;
            var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
            var read = domain.Resolve(module, "OpenNvGuestReadDword", NativePluginAbi.Cdecl);
            var write = domain.Resolve(module, "OpenNvGuestWriteDword", NativePluginAbi.Cdecl);
            var cdecl = domain.Resolve(module, "OpenNvGuestQueryCdecl", NativePluginAbi.Cdecl);
            var stdcall = domain.Resolve(module, "OpenNvGuestDamageStdcall", NativePluginAbi.Stdcall);
            var thiscall = domain.Resolve(module, "OpenNvGuestQueryThiscall", NativePluginAbi.Cdecl);
            var reenter = domain.Resolve(module, "OpenNvGuestReenter", NativePluginAbi.Cdecl);
            var scalar = domain.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl);
            Equal(0x12345678U, domain.Call(read, writable.Address, 0).Result, "The DLL did not dereference the actual allocation.");
            domain.WriteGuest(writable, 0, [0x11, 0x22, 0x33, 0x44]);
            Equal(0x44332211U, domain.Call(write, writable.Address, 0x10203040).Result, "A native write did not observe the C# native-memory publication.");
            Same([0x40, 0x30, 0x20, 0x10], domain.ReadGuest(writable, 0, 4), "The native DLL write was only a managed copy.");
            Reject<InvalidOperationException>(() => domain.WriteGuest(readOnly, 0, [0]));
            Reject<ArgumentOutOfRangeException>(() => domain.ReadGuest(writable, uint.MaxValue, 1));
            Reject<InvalidOperationException>(() => domain.ReadGuest(writable with { Address = readOnly.Address }, 0, 1));
            Task.Run(() => Reject<InvalidOperationException>(() => domain.ReadGuest(writable, 0, 1))).GetAwaiter().GetResult();
            Same([9, 8, 7, 6], domain.ReadGuest(readOnly, 0, 4), "Refused writes/ranges changed an independent owner.");

            var state = new GameplayVitals(1, 100, 100, 75, 75, 0, 200); state.Validate();
            var ownerThread = Environment.CurrentManagedThreadId; var entered = 0;
            var binding = domain.BindGuestState(query =>
            {
                Require(Environment.CurrentManagedThreadId == ownerThread, "A state query escaped C# authority's thread.");
                ++entered;
                if (query.Operation == 1) return Bits(state.ExactHitPoints);
                if (query.Operation == 2)
                {
                    state = state.Damage(BitConverter.UInt32BitsToSingle(query.Argument)); state.Validate();
                    return Bits(state.ExactHitPoints);
                }
                if (query.Operation == 3)
                {
                    Require(query.Argument == writable.Address, "The authored callback changed its buffer capability.");
                    Reject<InvalidOperationException>(() => domain.ReleaseGuest(query.Object.Allocation));
                    Reject<InvalidOperationException>(() => domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite));
                    domain.WriteGuest(writable, 0, [0xaa, 0x55, 0xaa, 0x55]);
                    Same([0xaa, 0x55, 0xaa, 0x55], domain.ReadGuest(writable, 0, 4), "Reentrant native memory publication did not persist.");
                    return domain.Call(scalar, 3, 4).Result;
                }
                throw new NotSupportedException("The authoritative fixture has no owner for this state operation.");
            });
            var view = domain.ReadGuest(binding.Allocation, 0, checked((int)NativePluginExecutionDomain.GuestStateSize));
            Equal(NativePluginExecutionDomain.GuestStateMagic, BinaryPrimitives.ReadUInt32LittleEndian(view), "The authored native view magic differs.");
            Equal(binding.Allocation.Handle, BinaryPrimitives.ReadUInt64LittleEndian(view.AsSpan(8)), "The object carries a different lifetime.");
            Equal(binding.ThiscallQuery, BinaryPrimitives.ReadUInt32LittleEndian(view.AsSpan(24)), "The object lacks its real native ECX thunk.");
            Equal(Bits(100), domain.Call(cdecl, binding.Allocation.Address, 0).Result, "Native query did not enter the real C# state owner.");
            state = state.Damage(2.5f); state.Validate();
            Equal(Bits(97.5f), domain.Call(thiscall, binding.Allocation.Address, 0).Result, "ECX query retained a stale snapshot after a genuine state change.");
            Equal(Bits(96.25f), domain.Call(stdcall, binding.Allocation.Address, Bits(1.25f)).Result, "The native thunk did not use shared Damage behavior.");
            Equal(96.25f, state.ExactHitPoints, "Native-owned bytes replaced the C# gameplay authority.");
            Equal(0xcdec0026U ^ 0x55aa55aaU, domain.Call(reenter, binding.Allocation.Address, writable.Address).Result,
                "The DLL did not consume both the nested native result and actual post-callback memory.");
            Equal(4, entered, "State callbacks were invented, omitted or replayed.");
            Equal(4UL, domain.GuestStatistics().Queries, "The actual native and C# query ledgers disagree.");
            domain.ReleaseGuest(writable);
            Reject<InvalidOperationException>(() => domain.ReadGuest(writable, 0, 1));
            Reject<InvalidOperationException>(() => domain.ReleaseGuest(writable));
            var replacement = domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite, [4, 3, 2, 1]);
            Require(replacement.Address != writable.Address && replacement.Handle != writable.Handle, "A retired pointer aliases a new native lifetime.");
            Equal(Bits(96.25f), domain.Call(cdecl, binding.Allocation.Address, 0).Result, "Retiring another owner damaged the live state object.");
            domain.Unload(module);
            // Dispose must release the remaining live capabilities, then native
            // retirement must release all quarantines before natural process exit.
        }
        finally { domain.Dispose(); }
        Require(domain.NaturallyRetired && domain.ChildExited && domain.Fault is null, "Guest resources prevented natural complete retirement.");
        Reject<ObjectDisposedException>(() => domain.ReadGuest(survivor!, 0, 1));
        return survivor!;
    }

    private static void AllocationQuotasAndRollback(string companion)
    {
        using (var domain = new NativePluginExecutionDomain(companion))
        {
            var owners = Enumerable.Range(0, 8).Select(_ => domain.AllocateGuest(1024 * 1024, NativePluginGuestAccess.ReadWrite)).ToArray();
            var before = domain.GuestStatistics();
            Equal(1816U, Reject<NativePluginDomainRefusal>(() => domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite)).Code,
                "Native committed-byte quota was not enforced.");
            Equal(before, domain.GuestStatistics(), "Rejected native allocation changed owners, pages or capability accounting.");
            domain.WriteGuest(owners[0], 0, [1, 2, 3, 4]);
            Same([1, 2, 3, 4], domain.ReadGuest(owners[0], 0, 4), "Quota refusal damaged an independent allocation.");
            domain.ReleaseGuest(owners[1]);
            var added = domain.AllocateGuest(1024 * 1024, NativePluginGuestAccess.ReadWrite);
            Require(added.Address != owners[1].Address, "A quota recovery reused a retired address.");
        }
        using (var domain = new NativePluginExecutionDomain(companion))
        {
            var first = domain.AllocateGuest(1024 * 1024, NativePluginGuestAccess.ReadWrite);
            var reservations = NativePluginExecutionDomain.MaximumGuestReservedBytes / first.ReservedBytes;
            domain.ReleaseGuest(first);
            for (var index = 1; index < reservations; ++index)
                domain.ReleaseGuest(domain.AllocateGuest(1024 * 1024, NativePluginGuestAccess.ReadWrite));
            var before = domain.GuestStatistics();
            Equal(NativePluginExecutionDomain.MaximumGuestReservedBytes, before.ReservedBytes, "The reservation quota did not account for retired ranges.");
            Equal(1816U, Reject<NativePluginDomainRefusal>(() => domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite)).Code, "Virtual-address reservation quota is unowned.");
            Equal(before, domain.GuestStatistics(), "Reservation quota refusal changed committed pages or quarantine accounting.");
        }
        using (var domain = new NativePluginExecutionDomain(companion))
        {
            for (var index = 0; index < NativePluginExecutionDomain.MaximumLiveGuestAllocations; ++index)
                domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite);
            var before = domain.GuestStatistics();
            Equal(1816U, Reject<NativePluginDomainRefusal>(() => domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite)).Code, "Live capability quota is unowned.");
            Equal(before, domain.GuestStatistics(), "Live quota refusal published a partial owner.");
        }
        using (var domain = new NativePluginExecutionDomain(companion))
        {
            for (var index = 0; index < NativePluginExecutionDomain.MaximumGuestReservations; ++index)
                domain.ReleaseGuest(domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite));
            var before = domain.GuestStatistics();
            Equal(0U, before.Live, "Retired guest lifetimes retain committed owners.");
            Equal(1816U, Reject<NativePluginDomainRefusal>(() => domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite)).Code, "Generation reservation quota is unowned.");
            Equal(before, domain.GuestStatistics(), "Reservation refusal discarded pointer quarantines.");
        }
    }

    private static void MissingStateOperation(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var binding = domain.BindGuestState(_ => throw new NotSupportedException("Unowned original operation remains refused."));
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var query = domain.Resolve(module, "OpenNvGuestQueryCdecl", NativePluginAbi.Cdecl);
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(query, binding.Allocation.Address, 0));
        Require(failure.Fault.Reason.Contains("Unowned original operation", StringComparison.Ordinal) && domain.ChildExited,
            "An unowned state operation produced a fabricated callback success.");
    }
    private static void RetiredNativeObject(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var binding = domain.BindGuestState(_ => throw new InvalidOperationException("Retired state delegate was called."));
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var remember = domain.Resolve(module, "OpenNvGuestRemember", NativePluginAbi.Cdecl);
        var query = domain.Resolve(module, "OpenNvGuestUseRemembered", NativePluginAbi.Cdecl);
        Equal(1U, domain.Call(remember, binding.Allocation.Address, 0).Result, "The native fixture did not retain the actual pointer/thunk.");
        domain.ReleaseGuest(binding.Allocation);
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(query, 0, 0));
        Equal(6U, failure.Fault.NativeCode ?? 0, "A native cached thunk accepted a retired receiver.");
        Require(domain.ChildExited, "The invalid native receiver generation remains live.");
    }
    private static void NativeProtectionFault(string companion, string fixture, bool retired)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var allocation = domain.AllocateGuest(4, NativePluginGuestAccess.ReadOnly, [1, 2, 3, 4]);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var operation = domain.Resolve(module, retired ? "OpenNvGuestReadDword" : "OpenNvGuestWriteDword", NativePluginAbi.Cdecl);
        if (retired) domain.ReleaseGuest(allocation);
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(operation, allocation.Address, 0));
        Equal(0xc0000005U, failure.Fault.NativeCode ?? 0, "Native page protection/lifetime was only a managed check.");
        Require(domain.ChildExited, "A protection-failed generation remains live.");
    }
    private static void NativeProtectionDrift(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var allocation = domain.AllocateGuest(4, NativePluginGuestAccess.ReadWrite, [1, 2, 3, 4]);
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var change = domain.Resolve(module, "OpenNvGuestSetReadOnly", NativePluginAbi.Cdecl);
        Equal(4U, domain.Call(change, allocation.Address, 0).Result, "The fixture did not change an actual writable native page.");
        var failure = Reject<NativePluginDomainFaultException>(() => domain.WriteGuest(allocation, 0, [5]));
        Equal(13U, failure.Fault.NativeCode ?? 0, "The native guest owner accepted a changed page protection.");
        Require(domain.ChildExited && failure.Fault.Reason.Contains("protection drifted", StringComparison.Ordinal),
            "Native ownership drift lacks its exact failed generation/reason.");
    }
    private static void NativeStateIdentityDrift(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var entered = 0;
        var binding = domain.BindGuestState(_ => { ++entered; return 1; });
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var tamper = domain.Resolve(module, "OpenNvGuestTamperState", NativePluginAbi.Cdecl);
        var query = domain.Resolve(module, "OpenNvGuestQueryCdecl", NativePluginAbi.Cdecl);
        Equal(1U, domain.Call(tamper, binding.Allocation.Address, 0).Result, "The fixture did not mutate the actual native object identity.");
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(query, binding.Allocation.Address, 0));
        Equal(13U, failure.Fault.NativeCode ?? 0, "A native object with a changed capability reached the C# owner.");
        Require(entered == 0 && domain.ChildExited && failure.Fault.Reason.Contains("layout/identity drifted", StringComparison.Ordinal),
            "Identity drift reached the C# delegate or lost its original native rejection.");
    }
    private static void NativeQueryDuringDetach(string companion, string fixture)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var entered = 0;
        var binding = domain.BindGuestState(_ => { ++entered; return 1; });
        var module = domain.LoadAuthoredModule(fixture, Hash(fixture));
        var arm = domain.Resolve(module, "OpenNvGuestQueryOnDetach", NativePluginAbi.Cdecl);
        Equal(1U, domain.Call(arm, binding.Allocation.Address, 0).Result, "The fixture did not retain an actual unload callback.");
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Unload(module));
        Require(entered == 0 && domain.ChildExited && !domain.NaturallyRetired &&
            failure.Fault.Reason.Contains("no owning call/thread", StringComparison.Ordinal),
            "An unowned loader-lock state query escaped refusal or was silently labeled clean retirement.");
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static void Require(bool pass, string reason) { if (!pass) throw new InvalidOperationException(reason); }
    private static void Same(byte[] expected, byte[] actual, string reason) => Require(expected.AsSpan().SequenceEqual(actual), reason);
    private static void Equal<T>(T expected, T actual, string reason) => Require(EqualityComparer<T>.Default.Equals(expected, actual), reason);
    private static T Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException($"Required {typeof(T).Name} refusal is absent."); }
}
