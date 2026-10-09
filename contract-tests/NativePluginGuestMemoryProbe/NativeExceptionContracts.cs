using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeExceptionContracts
{
    // Existing first-party DLL exports reach Windows software/read/write faults.
    // This neither loads an original DLL nor creates a new diagnostic module.
    internal static void Run(string companion, string fixture)
    {
        var hash = Hash(fixture);
        try
        {
            RunFault(companion, fixture, hash, "OpenNvRaise", 0xe04e5601, null);
            RunFault(companion, fixture, hash, "OpenNvGuestReadDword", 0xc0000005, 0);
            RunFault(companion, fixture, hash, "OpenNvGuestWriteDword", 0xc0000005, 1);
            using var independent = new NativePluginExecutionDomain(companion);
            var module = independent.LoadAuthoredModule(fixture, hash);
            var function = independent.Resolve(module, "OpenNvScalarCdecl", NativePluginAbi.Cdecl);
            Require(independent.Call(function, 5, 7).Result == 0xcdec0035 && independent.NativeException is null,
                "A separate valid generation inherited a fault or changed its native call.");
            independent.Dispose();
            Require(independent.ChildExited && independent.NaturallyRetired, "Independent valid child did not retire naturally.");
        }
        finally { Require(Hash(fixture) == hash, "Fault diagnosis changed its original authored DLL input."); }
        Console.WriteLine("OPENNV_NATIVE_EXCEPTION_RECEIPT_PASS authoredFaults=3 software=true read=true write=true actualWindowsContext=true originalDllCalls=unexecuted");
    }

    private static void RunFault(string companion, string fixture, string hash, string export, uint code, uint? access)
    {
        using var domain = new NativePluginExecutionDomain(companion);
        var module = domain.LoadAuthoredModule(fixture, hash);
        var function = domain.Resolve(module, export, NativePluginAbi.Cdecl);
        var pointer = 0U;
        if (access == 1)
        {
            // The existing native export first reads, then writes. A genuinely
            // initialized read-only page reaches the write fault after that read.
            var allocation = domain.AllocateGuest(4, NativePluginGuestAccess.ReadOnly, [1, 2, 3, 4]);
            pointer = allocation.Address;
        }
        var failure = Reject<NativePluginDomainFaultException>(() => domain.Call(function, pointer, 0));
        var retained = domain.NativeException ?? throw new InvalidDataException("A real native exception lost its private raw receipt.");
        var observed = retained.Observation;
        Require(failure.Fault.NativeCode == code && retained.Generation == domain.Generation && retained.ProcessId == domain.ProcessId &&
            retained.Call == failure.Fault.Call && retained.Operation == NativePluginDomainOperation.Call &&
            observed.Stage == "authored scalar call" && observed.InvocationEntry == function.GuestAddress && observed.Code == code &&
            observed.Thread == domain.NativeThread && observed.RecordPresent && observed.RecordCode == code &&
            observed.ContextPresent && observed.ControlPresent && observed.ProgramCounter != 0 && observed.ExceptionAddress != 0,
            "Real native exception lost its entered call, thread, code or Windows context.");
        Require(!observed.ParametersTruncated && observed.AccessOperation == access &&
            observed.AccessOperand == (access.HasValue ? (uint?)pointer : null), "Actual access operands were invented, replaced or lost.");
        Require(failure.Fault.Reason == observed.PublicReason && retained.ToString() == observed.PublicReason &&
            observed.ToString() == observed.PublicReason, "Product error exposed private exception fields or lost its stage/code.");
        Require(domain.PrivateNativeExceptionFile is null && domain.PrivateNativeExceptionDeliveryFailure is null,
            "An authored domain without private selection invented a disk-delivery receipt.");
        Reject<NativePluginDomainFaultException>(() => domain.Call(function, pointer, 0));
        domain.Dispose(); domain.Dispose();
        Require(ReferenceEquals(domain.NativeException, retained) && domain.ChildExited && !domain.NaturallyRetired,
            "Fault retirement cleared the retained receipt, replayed the call or claimed normal detach.");
    }

    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
    private static T Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }
}
