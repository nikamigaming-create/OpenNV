using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeEmptyCrtConstructorContracts
{
    internal static void RunDeclarations()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-empty-crt-").FullName;
        try
        {
            var path = Path.Combine(directory, "authored.dll");
            foreach (var word in new[] { false, true })
            {
                Write(word); Read((image, layout) =>
                {
                    Require(layout.EmptyInitializers == word && layout.DispatchBytes == (word ? 270U : 267U) &&
                        layout.AttachBytes == (word ? 266U : 263U), "selected independent store-width graph");
                    Require(layout.Dispatch(0x11) == 0x11 && layout.Dispatch(0x3d) == (word ? 0x40U : 0x3dU) &&
                        layout.Attach(0x1c) == 0x1c && layout.Attach(0x44) == (word ? 0x47U : 0x44U), "ordered child coordinates");
                    layout.RequireAttach(image, 0x1080);
                    NativePluginCrtDispatchSource.RequireEmptyRange(image, 0x21c0, 0x21c4);
                });
                var reason = word ? 0x22b : 0x228;
                Change(reason, 4); Reject<NotSupportedException>(() => Read((_, _) => { }));
                Write(word); Change(0x222, 0x90); Reject<NotSupportedException>(() => Read((_, _) => { }));
                Write(word); Change(0x2a9, 0x90); Reject<NotSupportedException>(() => Read((image, layout) => layout.RequireAttach(image, 0x1080)));
            }
            Write(true); Change(0x225, 1); Reject<NotSupportedException>(() => Read((_, _) => { }));
            Write(true); Change(0x5c0, 1); Reject<NotSupportedException>(() => Range(0x21c0, 0x21c4));
            Write(true); Change(0x5c4, 1); Reject<NotSupportedException>(() => Range(0x21c0, 0x21c4));
            Write(true); Reject<NotSupportedException>(() => Range(0x21c0, 0x21c0));
            Reject<NotSupportedException>(() => Range(0x21c0, 0x21c8));
            Reject<NotSupportedException>(() => Range(0x21c4, 0x21c0));
            Reject<NotSupportedException>(() => Range(0x21c1, 0x21c5));
            Reject<NotSupportedException>(() => Range(0x1100, 0x1104)); // Executable zero bytes are not a data table.
            Reject<NotSupportedException>(() => Range(0x21fc, 0x2200)); // Incomplete actual mapped/file extent.
            NativePluginCrtDispatchSource.RequireDistinctFields([new(4, 8, 0, "first"), new(12, 4, 0, "second")]);
            Reject<NotSupportedException>(() => NativePluginCrtDispatchSource.RequireDistinctFields([new(4, 8, 0, "first"), new(8, 4, 0, "second")]));
            Console.WriteLine("OPENNV_EMPTY_CRT_DECLARATIONS_PASS authoredStoreFamilies=2 actualMappedRanges=true malformedChildrenRefused=true originalEntry=UNEXECUTED");

            void Range(uint start, uint end) => Read((image, _) => NativePluginCrtDispatchSource.RequireEmptyRange(image, start, end));
            void Read(Action<NativePluginConstructorImage, NativePluginCrtDispatchSource> action)
            {
                var declaration = NativePluginImageDeclarations.Read(path, true);
                using var input = NativeNvseHostSource.Lease(path, declaration.Sha256, true);
                using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
                var image = new NativePluginConstructorImage(pe, input, declaration);
                action(image, NativePluginCrtDispatchSource.Read(image, 0x1000));
            }
            void Write(bool word)
            {
                NativeModuleReadContracts.WriteImage(path, false, false);
                var bytes = File.ReadAllBytes(path); bytes.AsSpan(0x200, 0x200).Clear();
                byte[] store = word ? [0xc7, 0x45, 0xfc, 0, 0, 0, 0] : [0x83, 0x65, 0xfc, 0];
                store.CopyTo(bytes, 0x222); store.CopyTo(bytes, 0x2a9);
                new byte[] { 0x83, 0xff, 1, 0x74, 0x0a, 0x83, 0xff, 2, 0x74, 5 }.CopyTo(bytes, 0x222 + store.Length);
                File.WriteAllBytes(path, bytes);
            }
            void Change(int at, byte value)
            { var bytes = File.ReadAllBytes(path); bytes[at] = value; File.WriteAllBytes(path, bytes); }
        }
        finally
        {
            var absolute = Path.GetFullPath(directory);
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("opennv-empty-crt-", StringComparison.Ordinal))
                throw new InvalidDataException("Empty CRT fixture cleanup escaped its actual generated directory.");
            Directory.Delete(absolute, true);
        }
    }

    internal static void RunOwned(string xrPath, string showOffPath, string ttwPath)
    {
        var original = NativePluginImageDeclarations.Read(xrPath, true); var stack = new string('A', 64);
        var source = NativePluginModuleConstructionSource.Read(original, stack);
        Require(source.Kind == NativePluginModuleConstructionKind.DynamicCrt && source.Family == NativePluginCrtDispatchSource.EmptyFamily &&
            source.Initializers.Count == 0 && source.Fields.Count == 10 && !source.Fields.Any(row => row.Role == "object-singleton") &&
            source.Fields.Single(row => row.Role == "dynamic-UCRT").InitialWord == 1, "complete actual empty-initializer family");
        var read = NativePluginModuleReadIoSource.Read(original, stack) ?? throw new InvalidDataException("Owned read import source absent.");
        Require(read.ConstructorSource.Family == source.Family, "production read admission uses the same structural constructor owner");
        Reject<NotSupportedException>(() => NativePluginCoResidentImports.Require(original, read)); // Independent current import/ABI owners remain mandatory.
        Reject<InvalidDataException>(() => source.Check(original, new string('B', 64)));
        Reject<InvalidDataException>(() => source.Check(original with { Imports = [original.Imports[0] with { SlotRva = 1 }] }));
        Reject<InvalidDataException>(() => source.Check(original with { Path = showOffPath }));
        Reject<NotSupportedException>(() => NativePluginModuleConstructionSource.Read(NativePluginImageDeclarations.Read(showOffPath, true), stack));
        var retained = NativePluginModuleConstructionSource.Read(NativePluginImageDeclarations.Read(ttwPath, true), stack);
        Require(retained.Family == NativePluginCrtDispatchSource.MemoryFamily && retained.Initializers.Count == 3 &&
            retained.Initializers.Count(row => row.Allocates) == 1, "existing independent memory-constructor family preserved");
        using var input = NativeNvseHostSource.Lease(original.Path, original.Sha256, true);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        var image = new NativePluginConstructorImage(pe, input, original); var entry = source.EntryRva;
        var dispatch = image.Call(entry + 23); var layout = NativePluginCrtDispatchSource.Read(image, dispatch);
        var raw = image.Call(dispatch + layout.Dispatch(0x3d));
        var attach = image.Call(image.Call(dispatch + layout.Dispatch(0x54)) + 0x36);
        var cpu = image.Call(image.Call(attach + 0x0e) + 0x10);
        var cppStart = image.Push(attach + layout.Attach(0x7f));
        var originalBytes = File.ReadAllBytes(original.Path);
        Tamper(dispatch + layout.Dispatch(0x26) + 2, [4]);
        Tamper(cppStart + 4, [1, 0, 0, 0]);
        Tamper(source.Fields.Single(row => row.Role == "raw-entry-callback").Rva, [1, 0, 0, 0]);
        var nonnullTls = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(nonnullTls,
            checked(image.Basis + source.Fields.Single(row => row.Role == "dynamic-UCRT").Rva));
        Tamper(image.Call(attach + layout.Attach(0xaa)) + 1, nonnullTls);
        Tamper(source.Fields.Single(row => row.Role == "dynamic-UCRT").Rva, [0, 0, 0, 0]);
        Tamper(cpu + 0x2e, [0xd4]); // The indexed output must remain this actual stack vector.
        Tamper(raw + 0x0d, [0]); // A changed null arm cannot be suppressed by the CFG's known source predicate.
        var options = image.Call(attach + layout.Attach(0x57)); var printf = image.Call(options);
        var alias = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(alias,
            checked(image.Basis + source.Fields.Single(row => row.Role == "dynamic-TLS-callback").Rva));
        Tamper(printf + 1, alias); // A real earlier option writer cannot alias a later supposedly-null predicate.
        Tamper(image.Call(image.Call(entry + 9) + 0x1a) + 8, [0xf0]); // SDK output must retain its proved stack extent.
        Console.WriteLine("OPENNV_EMPTY_CRT_OWNED_SOURCE_PASS emptyCallbacks=0 protectedPredicates=3 malformedSourceRefused=true " +
            "existingMemoryFamily=true independentImportsRefused=true ShowOffCallbacks=UNOWNED originalEntry=UNEXECUTED currentProviderBorrow=UNEXECUTED coldGlobals=UNOWNED");

        void Tamper(uint at, byte[] replacement)
        {
            var section = pe.PEHeaders.SectionHeaders.Single(row => at >= row.VirtualAddress &&
                (ulong)at + (uint)replacement.Length <= (ulong)row.VirtualAddress + (uint)row.SizeOfRawData);
            var changed = (byte[])originalBytes.Clone(); var disk = checked(section.PointerToRawData + (int)(at - (uint)section.VirtualAddress));
            replacement.CopyTo(changed, disk);
            using var copy = new MemoryStream(changed, writable: false); using var changedPe = new PEReader(copy, PEStreamOptions.LeaveOpen);
            var altered = new NativePluginConstructorImage(changedPe, copy, original);
            Reject<NotSupportedException>(() => NativePluginDynamicCrtConstructor.Read(altered, entry));
        }
    }
    private static void Require(bool value, string role)
    { if (!value) throw new InvalidDataException("Empty CRT construction contract: " + role); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Empty CRT construction contract expected " + typeof(T).Name); }
}
