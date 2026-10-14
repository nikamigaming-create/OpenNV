using System.Reflection.PortableExecutable;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeModuleConstructionContracts
{
    internal static void RunDeclarations()
    {
        // Existing authored PE cases include genuine leaf, foreign stack,
        // altered IAT association, reached direct-call and TLS negatives.
        NativeModuleReadContracts.RunDeclarations();
        RunInstructionFootprints();
        Console.WriteLine("OPENNV_MODULE_CONSTRUCTION_DECLARATIONS_PASS originalEntry=UNEXECUTED currentProviders=UNEXECUTED");
    }
    private static void RunInstructionFootprints()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-constructor-instructions-").FullName;
        try
        {
            var path = Path.Combine(directory, "authored.dll");
            // Authored stack buffer, not original constructor bytes: zero XMM0
            // and store the low double on this current stack frame.
            Check([0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x0f, 0x57, 0xc0, 0x66, 0x0f, 0x13, 0x45, 0xf8, 0xc9, 0xc3]);
            Reject<NotSupportedException>(() => Check([0x66, 0x0f, 0x13, 1, 0xc3])); // Foreign indexed output.
            Reject<NotSupportedException>(() => Check([0x0f, 0xae, 0, 0xc3])); // Unowned extended instruction.
            Reject<NotSupportedException>(() => Check([0xff, 0xd0, 0xc3])); // Actual unowned indirect callback.
            Reject<NotSupportedException>(() => Check([0xff, 0x15, 0x50, 0x20, 0, 0x10, 0xc3])); // Named file child remains unowned.
            Reject<NotSupportedException>(() => Check([0x75, 1, 0xb8, 0, 0, 0, 0, 0xc3])); // Branch into another instruction.
            void Check(byte[] code)
            {
                NativeModuleReadContracts.WriteImage(path, false, false);
                var bytes = File.ReadAllBytes(path); code.CopyTo(bytes, 0x200); File.WriteAllBytes(path, bytes);
                var declaration = NativePluginImageDeclarations.Read(path, true);
                using var input = NativeNvseHostSource.Lease(path, declaration.Sha256, true);
                using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
                NativePluginConstructorFootprint.Check(new(pe, input, declaration), new(0x1000, checked((uint)code.Length), new HashSet<uint>()),
                    new HashSet<string>(StringComparer.Ordinal), new HashSet<uint>());
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    internal static void RunOwned(string ttw, string showOff)
    {
        var original = NativePluginImageDeclarations.Read(ttw, dll: true);
        var source = NativePluginModuleConstructionSource.Read(original, new string('A', 64));
        Require(source.Kind == NativePluginModuleConstructionKind.DynamicCrt && source.Initializers.Count == 3 &&
            source.Initializers.Count(row => row.Allocates) == 1 && source.Fields.Any(row => row.Role == "object-singleton") &&
            source.Fields.Single(row => row.Role == "dynamic-UCRT").InitialWord == 1, "complete selected dynamic CRT source family");
        var cpp = NativePluginModuleCppSource.Read(original) ?? throw new InvalidDataException("Selected original C++ imports absent.");
        Require(cpp.Owns(NativePluginCppRuntimeImports.Library, NativePluginCppRuntimeImports.LocaleIdConversion), "actual locale conversion source/ABI");
        var read = NativePluginModuleReadIoSource.Read(original, new string('A', 64));
        NativePluginCoResidentImports.Require(original, read, cpp);
        Reject<InvalidDataException>(() => source.Check(original, new string('B', 64)));
        Reject<InvalidDataException>(() => source.Check(original with { Imports = [original.Imports[0] with { SlotRva = 1 }] }));
        Reject<NotSupportedException>(() => NativePluginModuleConstructionSource.Read(NativePluginImageDeclarations.Read(showOff, true), new string('A', 64)));
        Console.WriteLine("OPENNV_MODULE_CONSTRUCTION_OWNED_SOURCE_PASS selectedInitializers=3 actualProviderSource=true unknownFamilyRefused=true originalEntry=UNEXECUTED");
    }
    internal static void CheckActualReturned(NativePluginExecutionDomain domain, NativeNvsePlugin peer)
    {
        var receipt = domain.NvseLastModuleConstructionReceipt ?? throw new InvalidDataException("Actual native constructor receipt absent.");
        Require(receipt.Generation == domain.Generation && receipt.Process == domain.ProcessId && receipt.Thread == domain.NativeThread &&
            receipt.Path.Equals(peer.Path, StringComparison.OrdinalIgnoreCase) && receipt.Sha256.Equals(peer.Sha256, StringComparison.OrdinalIgnoreCase) &&
            receipt.NativeReply && receipt.LoaderEntered == true && receipt.LoaderReturned == true && receipt.AttachReturned == true &&
            receipt.NewHandler == 0 && receipt.ProviderBorrowReleased, "real original Windows attach and released current provider borrow");
        foreach (var row in domain.NvseModuleCppBindings.Where(row => row.OriginalSha256 == peer.Sha256))
            Require(row.Generation == domain.Generation && row.Process == domain.ProcessId && row.Thread == domain.NativeThread && row.LoaderReturned && row.AttachReturned,
                "original caller retains the same current shared C++ provider/export lifetime");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException("Module construction contract: " + message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Module construction contract expected " + typeof(T).Name); }
}
