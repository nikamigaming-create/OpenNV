namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginDynamicCrtConstructor
{
    internal sealed record Reduction(IReadOnlyList<NativePluginConstructorField> Fields,
        IReadOnlyList<NativePluginConstructorInitializer> Initializers, string Family);
    internal static Reduction Read(NativePluginConstructorImage image, uint entry)
    {
        // Select relationships through the actual entry/children/table operands.
        // None of these offsets is an original image address or digest allowlist.
        image.Need(entry, [0x55, 0x8b, 0xec, 0x83, 0x7d, 0x0c, 1, 0x75, 5], "MSVC process-attach entry predicate");
        var cookie = image.Call(entry + 9); var dispatch = image.Call(entry + 23);
        image.Need(entry + 28, [0x83, 0xc4, 0x0c, 0x5d, 0xc2, 0x0c, 0], "MSVC entry return");
        var layout = NativePluginCrtDispatchSource.Read(image, dispatch);
        uint D(uint offset) => checked(dispatch + layout.Dispatch(offset));
        var prolog = image.Call(dispatch + 7); var raw = image.Call(D(0x3d));
        var crtDispatch = image.Call(D(0x54)); var client = image.Call(D(0x6b));
        if (!image.LeafTrue(client)) throw new NotSupportedException("Original construction requires its actual client DllMain owner.");
        image.Need(raw + 3, [0x56, 0x8b, 0x35], "nullable raw DllMain callback");
        image.Need(raw + 10, [0x85, 0xf6, 0x75, 5, 0x33, 0xc0, 0x40, 0xeb, 0x13], "raw-null returned arm");
        var fields = new List<NativePluginConstructorField> { image.Field(image.Address(raw, 6), 4, 0, "raw-entry-callback", false) };
        image.Need(crtDispatch + 3, [0x8b, 0x45, 0x0c, 0x83, 0xe8, 0, 0x74, 0x33, 0x83, 0xe8, 1, 0x74, 0x20], "actual CRT process reason");
        var attach = image.Call(crtDispatch + 0x36);
        layout.RequireAttach(image, attach);
        uint A(uint offset) => checked(attach + layout.Attach(offset));
        if (image.Call(attach + 7) != prolog) throw new NotSupportedException("Original constructor changed its shared exception frame owner.");
        var initialize = image.Call(A(0x0e)); var acquire = image.Call(A(0x1c));
        var before = image.Call(A(0x44)); var rtc = image.Call(A(0x4d));
        var typeInfo = image.Call(A(0x52)); var options = image.Call(A(0x57));
        var after = image.Call(A(0x71)); var dynamicTls = image.Call(A(0xaa));
        image.Need(A(0x2d), [0x83, 0x3d], "fresh native startup predicate");
        var startup = image.Address(A(0x2f), 0); fields.Add(image.Field(startup, 4, 0, "native-startup"));
        image.Need(A(0x33), [0, 0x0f, 0x85], "startup-zero branch");
        image.Need(A(0x3a), [0xc7, 5], "initializing store");
        if (image.Address(A(0x3c), 0) != startup || NativePluginConstructorImage.Word(image.Read(A(0x40), 4), 0) != 1 ||
            image.Address(A(0x8d), 0) != startup || NativePluginConstructorImage.Word(image.Read(A(0x91), 4), 0) != 2)
            throw new NotSupportedException("Original constructor changed its ordered startup stores.");
        image.Need(A(0xb1), [0x83, 0x3e, 0, 0x74, 0x1f], "independent dynamic TLS-null branch");
        var tls = image.Getter(dynamicTls); fields.Add(image.Field(tls, 4, 0, "dynamic-TLS-callback"));
        var release = image.Call(A(0xe6));
        var markerGetter = image.Call(acquire + 1);
        image.Need(markerGetter, [0x33, 0xc0, 0x39, 5], "current dynamic-UCRT marker");
        image.Need(markerGetter + 8, [0x0f, 0x95, 0xc0, 0xc3], "dynamic-UCRT marker return");
        var marker = image.Address(markerGetter, 4); fields.Add(image.Field(marker, 4, 1, "dynamic-UCRT"));
        image.Need(acquire + 0x10, [0xbe], "actual startup lock cell");
        var startupLock = image.Address(acquire, 0x11); fields.Add(image.Field(startupLock, 4, 0, "native-startup-lock"));
        if (image.Call(after) != markerGetter || image.Call(release + 3) != markerGetter || image.Address(release, 0x15) != startupLock)
            throw new NotSupportedException("Original constructor changed its current marker/lock owner.");
        image.Need(before, [0x6a, 0], "opaque onexit initialization kind");
        var onexitInitialize = image.Call(before + 2);
        if (image.Call(onexitInitialize + 0x1d) != markerGetter)
            throw new NotSupportedException("Original onexit tables lost their actual dynamic-UCRT predicate.");
        var table = image.Push(onexitInitialize + 0x2a); var quickTable = image.Push(onexitInitialize + 0x39);
        fields.Add(image.Field(table, 12, 0, "module-onexit-table")); fields.Add(image.Field(quickTable, 12, 0, "module-quick-onexit-table"));
        image.Need(onexitInitialize + 3, [0x80, 0x3d], "independent onexit initialization byte");
        var initializedByte = image.Address(onexitInitialize, 5);
        image.RequireWritable(initializedByte, 1);
        if (image.Read(initializedByte, 1)[0] != 0) throw new NotSupportedException("Original onexit initialization is not fresh source state.");
        var imported = new HashSet<string>(StringComparer.Ordinal);
        void Import(NativePluginImageImport row) => imported.Add(row.Library + "!" + row.Name);
        Import(image.Thunk(image.Call(onexitInitialize + 0x2f), "_initialize_onexit_table"));
        if (image.Call(onexitInitialize + 0x3e) != image.Call(onexitInitialize + 0x2f))
            throw new NotSupportedException("Original onexit tables changed their actual opaque provider.");
        var cStart = image.Push(A(0x61)); var cEnd = image.Push(A(0x5c));
        NativePluginCrtDispatchSource.RequireEmptyRange(image, cStart, cEnd);
        Import(image.Thunk(image.Call(A(0x66)), "_initterm_e"));
        var cppStart = image.Push(A(0x7f)); var cppEnd = image.Push(A(0x7a));
        var initializers = new List<NativePluginConstructorInitializer>();
        uint objectConstructor = 0, objectRegistration = 0, allocator = 0;
        if (layout.EmptyInitializers)
        {
            NativePluginCrtDispatchSource.RequireEmptyRange(image, cppStart, cppEnd);
            Import(image.Thunk(image.Call(A(0x84)), "_initterm"));
        }
        else
        {
            if (cppEnd < cppStart || cppEnd - cppStart != 16)
                throw new NotSupportedException("Original C++ initializer graph is outside the bounded memory constructor family.");
            Import(image.Thunk(image.Call(A(0x84)), "_initterm"));
            var entries = image.Read(cppStart, 20);
            if (NativePluginConstructorImage.Word(entries, 0) != 0 || NativePluginConstructorImage.Word(entries, 16) != 0)
                throw new NotSupportedException("Original C++ callback range changed its null boundary members.");
            var callbacks = Enumerable.Range(1, 3).Select(index => image.ToRva(NativePluginConstructorImage.Word(entries, index * 4))).ToArray();
            uint? registrar = null; uint? allocated = null;
            foreach (var callback in callbacks)
            {
                var leading = image.Read(callback, 1, true)[0]; var offset = leading == 0xe8 ? 5U : 0U;
                if (offset != 0) { if (allocated is not null) throw new NotSupportedException("Original initializer repeats an unowned object constructor."); allocated = image.Call(callback); }
                var destructor = image.Push(callback + offset); var current = image.Call(callback + offset + 5);
                image.Need(callback + offset + 10, [0x59, 0xc3], "returned destructor registration");
                if (registrar is not null && registrar != current) throw new NotSupportedException("Original callbacks span unowned registration providers.");
                registrar = current; initializers.Add(new(callback, destructor, offset != 0));
            }
            if (allocated is not { } construct || registrar is not { } registration || !initializers[1].Allocates || initializers[0].Allocates || initializers[2].Allocates)
                throw new NotSupportedException("Original callback order changed its independent registration/allocation/registration graph.");
            image.Need(registration + 1, [0x8b, 0xec, 0xff, 0x75, 8], "actual atexit operand");
            var onexit = image.Call(registration + 6);
            image.Need(onexit + 3, [0x83, 0x3d], "current module onexit table");
            if (image.Address(onexit, 5) != table || image.Push(onexit + 0x16) != table)
                throw new NotSupportedException("Original destructor registration lost its initialized actual module table.");
            Import(image.Thunk(image.Call(onexit + 0x0f), "_crt_atexit"));
            Import(image.Thunk(image.Call(onexit + 0x1b), "_register_onexit_function"));
            allocator = ReadObject(image, construct, fields, imported);
            objectConstructor = construct; objectRegistration = registration;
        }
        var cpu = image.Call(initialize + 0x10);
        if (image.Call(after + 9) != cpu) throw new NotSupportedException("Original CPU/runtime initialization changed its current provider.");
        var stub = image.Call(initialize + 0x15); image.Need(stub, [0xb0, 1, 0xc3], "actual dynamic CRT initialization stub");
        if (image.Call(initialize + 0x22) != stub || image.Call(initialize + 0x2d) != stub)
            throw new NotSupportedException("Original CRT initialization changed its actual returned stub children.");
        image.Need(rtc + 2, [0xbe], "RTC callback start"); image.Need(rtc + 7, [0xbb], "RTC callback end");
        if (image.Address(rtc, 3) != image.Address(rtc, 8)) throw new NotSupportedException("Original construction has reached RTC callbacks.");
        image.Need(typeInfo, [0x68], "module type-info header");
        fields.Add(image.Field(image.Push(typeInfo), 8, 0, "module-type-info-head"));
        Import(image.ImportAt(typeInfo + 5, "InitializeSListHead"));
        var printf = image.Call(options); var scanf = image.Call(options + 0x0e);
        var printfCell = image.Getter(printf); var scanfCell = image.Getter(scanf);
        fields.Add(image.Field(printfCell, 8, 0, "local-printf-options")); fields.Add(image.Field(scanfCell, 8, 0, "local-scanf-options"));
        image.Need(options + 5, [0x8b, 0x48, 4, 0x83, 8, 0x24, 0x89, 0x48, 4], "local printf option stores");
        image.Need(options + 0x13, [0x8b, 0x48, 4, 0x83, 8, 2, 0x89, 0x48, 4, 0xc3], "local scanf option stores");
        Import(image.ImportAt(cpu + layout.ProcessorImportOffset, "IsProcessorFeaturePresent"));
        var entropy = image.Call(cookie + 0x1a);
        image.Need(entropy, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x14, 0x8d, 0x45, 0xf4, 0x0f, 0x57, 0xc0, 0x50, 0x66, 0x0f, 0x13, 0x45, 0xf4], "cookie entropy source stack destination");
        image.Need(entropy + 0x33, [0x8d, 0x45, 0xec, 0x50], "counter entropy source stack destination");
        foreach (var pair in new[] { (0x12U, "GetSystemTimeAsFileTime"), (0x21U, "GetCurrentThreadId"),
            (0x2aU, "GetCurrentProcessId"), (0x37U, "QueryPerformanceCounter") }) Import(image.ImportAt(entropy + pair.Item1, pair.Item2));
        var writes = new HashSet<uint>();
        void Body(uint start, uint size, uint[] children, (uint, bool)[]? branches = null, uint[]? indexed = null) =>
            NativePluginConstructorFootprint.Check(image, new(start, size, children.ToHashSet(),
                branches?.ToDictionary(row => checked(start + row.Item1), row => row.Item2),
                indexed?.Select(at => checked(start + at)).ToHashSet()), imported, writes);
        Body(entry, 35, [cookie, dispatch], [(7, false)]);
        Body(dispatch, layout.DispatchBytes, [prolog, raw, crtDispatch, client], [(0x11, true), (layout.Dispatch(0x29), true),
            (layout.Dispatch(0x49), false), (layout.Dispatch(0x78), false), (layout.Dispatch(0x7c), true),
            (layout.Dispatch(0xa3), false), (layout.Dispatch(0xa8), true)]);
        Body(raw, 43, [], [(0x0c, false)]);
        Body(crtDispatch, 83, [attach], [(9, false), (0x0e, true)]);
        Body(attach, layout.AttachBytes, [prolog, initialize, acquire, before, rtc, typeInfo, options, image.Call(A(0x66)), after,
            image.Call(A(0x84)), A(0xe3), dynamicTls, release], [(layout.Attach(0x34), false), (layout.Attach(0xb4), true)]);
        Body(A(0xe3), 10, [release]);
        Body(initialize, 57, [cpu, stub]); Body(stub, 3, []);
        Body(markerGetter, 12, []);
        Body(acquire, 50, [markerGetter], [(8, false)], [0x22]);
        Body(before, 14, [onexitInitialize]);
        Body(onexitInitialize, 135, [markerGetter, image.Call(onexitInitialize + 0x2f)], [(0x0a, true), (0x16, true), (0x24, false), (0x28, false)]);
        Body(after, 43, [markerGetter, cpu], [(7, false)]);
        Body(release, 29, [markerGetter], [(0x0a, false)], [0x19]);
        Body(rtc, 44, [], [(0x0e, true)]); Body(typeInfo, 12, []);
        Body(options, 29, [printf, scanf], indexed: [8, 11, 0x16, 0x19]);
        Body(printf, 6, []); Body(scanf, 6, []); Body(dynamicTls, 6, []);
        if (!layout.EmptyInitializers)
        {
            var onexit = image.Call(objectRegistration + 6);
            Body(objectRegistration, 21, [onexit]); Body(onexit, 46, [image.Call(onexit + 0x0f), image.Call(onexit + 0x1b)]);
            foreach (var row in initializers) Body(row.Rva, row.Allocates ? 17U : 12U, row.Allocates ? [objectConstructor, objectRegistration] : [objectRegistration]);
            Body(objectConstructor, 160, [image.Call(objectConstructor + 7), allocator, image.Call(objectConstructor + 0x7d), image.Call(objectConstructor + 0x87)], [(0x25, false)], [0x73, 0x7b]);
            var cppProlog = image.Call(objectConstructor + 7); var cppEpilog = image.Call(objectConstructor + 0x87);
            image.Need(cppProlog + 0x13, [0x89, 0x28, 0x8b, 0xe8], "C++ exception local frame association");
            Body(cppProlog, 51, [], indexed: [0x13]); Body(cppEpilog, 20, []);
        }
        image.Need(prolog + 0x3b, [0x8d, 0x45, 0xf0, 0x64, 0xa3], "actual source-thread exception registration");
        Body(prolog, 69, []); Body(cookie, 75, [entropy]); Body(entropy, 77, []);
        var scratch = layout.RequireProcessorScratch(image, cpu);
        Body(cpu, layout.ProcessorBytes, [], indexed: scratch);
        foreach (var thunk in new[] { image.Call(onexitInitialize + 0x2f), image.Call(A(0x66)), image.Call(A(0x84)) }) Body(thunk, 6, []);
        if (!layout.EmptyInitializers)
        {
            var mallocThunk = image.Call(allocator + 0x15); var handlerThunk = image.Call(allocator + 8);
            var allocationFailure = image.Tail(allocator + 0x2b); var failureObject = image.Call(allocationFailure + 9);
            var throwThunk = image.Call(allocationFailure + 0x17);
            Import(image.Thunk(throwThunk, "_CxxThrowException"));
            image.Need(allocationFailure + 6, [0x8d, 0x4d, 0xf4], "actual stack allocation-failure object");
            image.Need(allocationFailure + 0x1c, [0xcc], "original nonreturning allocation failure");
            image.Need(allocator + 0x21, [0x83, 0x7d, 8, 0xff, 0x0f, 0x84], "independent maximum-size failure predicate");
            Body(allocator, 48, [mallocThunk, handlerThunk, allocationFailure], [(0x25, false)]);
            Body(allocationFailure, 29, [failureObject, throwThunk]); Body(failureObject, 24, [], indexed: [0, 6, 0x0a, 0x11]);
            foreach (var thunk in new[] { mallocThunk, handlerThunk, throwThunk, image.Call(objectConstructor + 0x7d),
                image.Call(image.Call(objectRegistration + 6) + 0x0f), image.Call(image.Call(objectRegistration + 6) + 0x1b), image.Call(onexitInitialize + 0x2f),
                image.Call(A(0x66)), image.Call(A(0x84)) }) Body(thunk, 6, []);
            // The partially constructed object's only unwind action is its base
            // destructor. Singleton publication precedes allocation, so this
            // rollback takes the genuine nonnull branch and clears that same cell.
            var handler = image.Address(objectConstructor, 3);
            image.Need(handler + 0x13, [0xb8], "source constructor exception function-info");
            var functionInfo = image.Address(handler, 0x14); var info = image.Read(functionInfo, 36);
            if (NativePluginConstructorImage.Word(info, 0) != 0x19930522 || NativePluginConstructorImage.Word(info, 4) != 1 ||
                info.AsSpan(12, 20).IndexOfAnyExcept((byte)0) >= 0 || NativePluginConstructorImage.Word(info, 32) != 1)
                throw new NotSupportedException("Original constructor exception action map has an unowned callback extent.");
            var map = image.ToRva(NativePluginConstructorImage.Word(info, 8)); var unwind = image.Read(map, 8);
            if (NativePluginConstructorImage.Word(unwind, 0) != uint.MaxValue)
                throw new NotSupportedException("Original constructor rollback changed its independent prior state.");
            var cleanup = image.ToRva(NativePluginConstructorImage.Word(unwind, 4));
            image.Need(cleanup, [0x8b, 0x4d, 0xf0], "partially constructed actual receiver"); var baseDestructor = image.Tail(cleanup + 3);
            var singleton = fields.Single(row => row.Role == "object-singleton").Rva;
            image.Need(baseDestructor + 0x22, [0x83, 0x3d], "current singleton rollback predicate");
            image.Need(baseDestructor + 0x29, [0xc7, 1], "actual base vtable rollback store");
            image.Need(baseDestructor + 0x31, [0xc7, 5], "nonnull singleton rollback clear");
            if (image.Address(baseDestructor, 0x24) != singleton || image.Address(baseDestructor, 0x33) != singleton)
                throw new NotSupportedException("Original rollback changed its real singleton ownership.");
            var frameHandler = image.Tail(handler + 0x18); Import(image.Thunk(frameHandler, "__CxxFrameHandler3"));
            Body(handler, 29, [image.Call(handler + 0x0e), frameHandler]); Body(cleanup, 8, [baseDestructor]);
            var checkCookie = image.Call(handler + 0x0e);
            image.Need(checkCookie, [0x3b, 0x0d], "source frame-cookie comparison");
            if (image.Address(checkCookie, 2) != image.Address(cookie, 2))
                throw new NotSupportedException("Original exception frame changed its actual initialized cookie owner.");
            Body(checkCookie, 14, [], [(6, false)]);
            Body(baseDestructor, 96, [], [(0x2f, false)], [0x29]); Body(frameHandler, 6, []);
        }
        NativePluginCrtDispatchSource.RequireDistinctFields(fields);
        foreach (var protectedField in fields.Where(row => row.Role is "raw-entry-callback" or "dynamic-TLS-callback" or "dynamic-UCRT"))
            if (writes.Any(at => at < (ulong)protectedField.Rva + protectedField.Bytes && (ulong)at + 4 > protectedField.Rva))
                throw new NotSupportedException("Original constructor changed a preceding nullable/selection field.");
        return new(fields.AsReadOnly(), initializers.AsReadOnly(), layout.Family);
    }
    private static uint ReadObject(NativePluginConstructorImage image, uint construct,
        List<NativePluginConstructorField> fields, HashSet<string> imports)
    {
        image.Need(construct, [0x6a, 8, 0xb8], "bounded object exception scope");
        image.Need(construct + 0x0c, [0x83, 0x3d], "fresh singleton predicate");
        var singleton = image.Address(construct, 0x0e); fields.Add(image.Field(singleton, 4, 0, "object-singleton"));
        image.Need(construct + 0x13, [0xbb], "current module object"); var instance = image.Address(construct, 0x14);
        image.Need(construct + 0x27, [0x89, 0x1d], "singleton publication before allocation");
        if (image.Address(construct, 0x29) != singleton || image.Address(construct, 0x1d) != instance || image.Address(construct, 0x3b) != instance)
            throw new NotSupportedException("Original object constructor changed its real singleton/vtable association.");
        image.Need(construct + 0x34, [0x89, 0x7d, 0xfc, 0x6a, 8], "original eight-byte allocation operand");
        var container = image.Address(construct, 0x30); fields.Add(image.Field(container, 20, 0, "empty-container"));
        image.RequireWritable(instance, 4);
        for (uint index = 0; index < 5; ++index)
        { image.Need(construct + 0x43 + index * 6, [0x89, 0x3d], "container-null store before allocation"); if (image.Address(construct, checked((int)(0x45 + index * 6))) != container + index * 4) throw new NotSupportedException("Original container fields changed their ordered span."); }
        var allocator = image.Call(construct + 0x61);
        image.Need(construct + 0x66, [0xc7, 4, 0x24], "backing-span extent");
        var extent = NativePluginConstructorImage.Word(image.Read(construct + 0x69, 4), 0);
        image.Need(construct + 0x6d, [0x57, 0x68], "source backing destination"); var backing = image.Address(construct, 0x6f);
        if (backing != instance + 4 || extent == 0 || extent > 1024 * 1024) throw new NotSupportedException("Original object backing span has no bounded source owner.");
        image.RequireWritable(backing, extent);
        image.Need(construct + 0x73, [0x89, 0x78, 4, 0xa3], "returned node before publication");
        if (image.Address(construct, 0x77) != container) throw new NotSupportedException("Original allocated node publication changed its actual container.");
        image.Need(construct + 0x7b, [0x89, 0x30], "allocated node owner link");
        var clear = image.Thunk(image.Call(construct + 0x7d), "memset"); imports.Add(clear.Library + "!" + clear.Name);
        image.Need(allocator + 3, [0xeb, 0x0d], "source allocator first-attempt order");
        var handler = image.Thunk(image.Call(allocator + 8), "_callnewh"); var malloc = image.Thunk(image.Call(allocator + 0x15), "malloc");
        imports.Add(handler.Library + "!" + handler.Name); imports.Add(malloc.Library + "!" + malloc.Name);
        return allocator;
    }
}
