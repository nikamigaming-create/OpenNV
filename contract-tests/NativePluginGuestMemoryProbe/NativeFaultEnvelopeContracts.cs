using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeFaultEnvelopeContracts
{
    internal static void Run(string companion)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(companion))!, "protocol-fixtures");
        for (var mode = 0; mode <= 6; mode++)
        {
            var executable = Path.Combine(directory, $"opennv_fault_envelope_{mode}.exe");
            NativePluginDomainFaultException failure;
            try
            {
                using var unexpected = new NativePluginExecutionDomain(executable);
                throw new InvalidDataException("An authored fault envelope was admitted as a native hello.");
            }
            catch (NativePluginDomainFaultException error) { failure = error; }
            var owner = failure.Owner;
            try
            {
                var fault = failure.Fault;
                Require(fault.Generation == owner.Generation && fault.ProcessId == owner.ProcessId &&
                    fault.Call == 1 && fault.Operation == "Hello", "A transport fault lost its actual waiting request owner.");
                if (mode == 0)
                    Require(fault.NativeCode == 13 && fault.Reason == "authored-fault-envelope-fixture",
                        "A correctly correlated native fault lost its code or reason.");
                else
                {
                    Require(fault.NativeCode is null, "An unowned or malformed fault supplied a trusted native code.");
                    var expected = mode switch
                    {
                        1 or 2 or 3 => "does not belong to its waiting call frame",
                        4 => "foreign process generation",
                        _ => "lacks a failure code or reason",
                    };
                    Require(fault.Reason.Contains(expected, StringComparison.Ordinal), "The malformed native fault lost its refusal owner.");
                }
            }
            finally { owner.Dispose(); }
            Require(owner.ChildExited && !owner.NaturallyRetired && owner.Fault is not null,
                "A rejected fault envelope left its child alive or claimed natural retirement.");
        }
        Console.WriteLine("OPENNV_NATIVE_FAULT_ENVELOPE_CONTRACT_PASS cases=7 actualChild=true correlatedFault=true foreignFault=refused");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
