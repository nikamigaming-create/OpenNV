using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class SpecialBookTextureContracts
{
    internal static void Run()
    {
        foreach (var optimized in new[] { false, true })
            foreach (var scheduled in new[] { false, true })
                foreach (var origin in new uint[] { 0x400000, 0x830000 })
                {
                    var fixture = new Fixture(optimized, origin, scheduled); var result = fixture.Read();
                    Require(result.Digits.Count == 11 && result.Digits[10] == "Textures/SourceBook/BBNumber10.dds" &&
                        result.Messages.Count == 4 && result.Message(2) == "Textures/SourceLanguage/BBMessage02.dds" &&
                        result.Message(1) == "Textures/SourceLanguage/BBMessage03.dds" && result.Message(0) == "Textures/SourceLanguage/BBMessage04.dds",
                        "Source texture tables, formats or message alternatives were replaced.");
                    Require(result.Buttons["BBLTOn"] == "Textures/SourceBook/BBLTOn.dds" && result.Buttons["BBRTOn"] == "Textures/SourceBook/BBRTOn.dds" &&
                        result.Buttons["BBLTOff"] == "Textures/SourceBook/PC/BBLTOff.dds" && result.Buttons["BBRTOff"] == "Textures/SourceBook/PC/BBRTOff.dds" &&
                        result.Buttons["BBXOn"] == "Textures/SourceBook/PC/BBXOn.dds" && result.Buttons.Count == 10 && result.Paths.Count() == 25,
                        "Direct enabled arrow resources were moved into the platform folder.");
                    void Alter(Action<Fixture> change)
                    {
                        var changed = new Fixture(optimized, origin, scheduled); change(changed); Reject(() => changed.Read());
                    }
                    Alter(changed => changed.Write(changed.ConstructorCall, 0));
                    Alter(changed => changed.Write(changed.Fields["BBLTOn"], 0x204));
                    Alter(changed => changed.Write(changed.FormatCalls["BBLTOn"], 0));
                    Alter(changed => changed.Write(changed.Managers["BBLTOn"], origin + 100));
                    Alter(changed => changed.Write(changed.LoadBuffers["BBLTOn"], unchecked((uint)-0x118)));
                    Alter(changed => changed.Strings[changed.DirectFormat] = "%s/%s/%s.dds");
                    Alter(changed => changed.Strings[changed.Directory] = "../textures/");
                    Alter(changed => changed.Strings[changed.Pc] = "UnknownInputMode");
                    Alter(changed => changed.Write(changed.PlatformBuffer, unchecked((uint)-0x12c)));
                    Alter(changed => changed.Retarget(changed.CopyCalls["BBLTOn"], changed.AlternativeTransport));
                    Alter(changed => { foreach (var call in changed.CopyCalls.Values) changed.Retarget(call, changed.AppendHelper); });
                    Alter(changed => changed.Code[changed.TransportGuard] = 0x75);
                    Alter(changed => changed.Code[changed.TransportDestination] = 8);
                    Alter(changed => changed.Code[changed.TransportSource] = 12);
                    Alter(changed => changed.Code[changed.AppendSource] ^= 1);
                    Alter(changed => changed.Code[changed.AppendCapacity] ^= 1);
                    Alter(changed => changed.BranchTo(changed.AppendDestinationGuard, changed.AppendDestinationClear));
                    if (optimized) Alter(changed =>
                    {
                        changed.BranchTo(changed.AppendDestinationGuard, changed.AppendDestinationClear);
                        changed.BranchTo(changed.AppendCapacityGuard, changed.AppendDestinationClear);
                    });
                    else Alter(changed =>
                    {
                        changed.BranchTo(changed.AppendDestinationGuard, changed.AppendDestinationClear);
                        changed.BranchTo(changed.AppendNullSourceExit, changed.AppendDestinationClear);
                    });
                    Alter(changed => changed.BranchTo(changed.AppendScanOverflow, changed.AppendSuccess));
                    Alter(changed => changed.BranchTo(changed.AppendTerminated, changed.AppendSuccess));
                    Alter(changed => changed.BranchTo(changed.AppendCapacityResult, changed.AppendDestinationClear));
                    Alter(changed => changed.Code[changed.AppendCapacityResult - 1] = 0x74);
                    Alter(changed => changed.BranchTo(changed.AppendOverflowExit, changed.AppendSuccess));
                    Alter(changed => changed.Code[changed.AppendOverflowStatus] = 0);
                    Alter(changed => changed.Code[changed.AppendInvalidStatus] = 0);
                    Alter(changed => changed.Code[changed.AppendStatusResult] = 0xc0);
                    if (!optimized) Alter(changed => changed.BranchTo(changed.AppendOverflowExit, changed.AppendStatusResult - 1));
                    Alter(changed => changed.Retarget(changed.TransportAppendCall, changed.AlternativeTransport));
                    if (!optimized) Alter(changed => changed.Code[changed.AppendForwardSource] = 12);
                    if (!optimized) Alter(changed => changed.Write(changed.Receivers["BBLTOn"], unchecked((uint)-0x24c)));
                    else Alter(changed => changed.Code[changed.NumberOrigin] = 0x90);
                    if (scheduled && optimized) Alter(changed => changed.Code[changed.CleanupInstructions["BBDecreaseOn"] + 1] = 0xc5);
                    if (scheduled && !optimized) Alter(changed => changed.Code[changed.FieldInstructions["BBXOn"]] = 0x0d);
                }
        Console.WriteLine("PASS source-bound SPECIAL texture callee, direct/platform formats, indexed digits/messages, typed receiver slots, scheduled cleanup/accumulator forms, guarded path transport/bounded append and independent copy-target/data-flow/null-guard/exhaustion-status drift refusals.");
        DiagnosticPaths();
    }

    private static void DiagnosticPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-book-diagnostic-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var roots = new[] { "repository", "game", "mod", "dependencies", "selected-content" }
                .Select(name => Path.Combine(directory, name)).ToArray();
            foreach (var root in roots) Directory.CreateDirectory(root);
            foreach (var root in roots) Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath(Path.Combine(root, "fresh.png"), roots));
            Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath(Path.Combine(directory, "fresh.png"), [Path.GetPathRoot(directory)!]));
            Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath("relative.png", roots));
            Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath(Path.Combine(directory, "fresh.dds"), roots));
            var existing = Path.Combine(directory, "existing.png"); File.WriteAllText(existing, "preserve input");
            Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath(existing, roots));
            Refuse(() => NativeSpecialBookMenuAudit.CreateDiagnostic(existing));
            Require(File.ReadAllText(existing) == "preserve input", "Diagnostic admission overwrote an existing file.");
            var folder = Path.Combine(directory, "existing-directory.png"); Directory.CreateDirectory(folder);
            Refuse(() => NativeSpecialBookMenuAudit.DiagnosticPath(folder, roots));
            var sibling = Path.Combine(directory, "game-sibling", "diagnostic.png"); Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
            Require(NativeSpecialBookMenuAudit.DiagnosticPath(sibling, roots) == Path.GetFullPath(sibling), "A sibling outside an input root was rejected by a textual prefix.");
            var fresh = Path.Combine(directory, "fresh.png");
            var admitted = NativeSpecialBookMenuAudit.DiagnosticPath(fresh, roots);
            File.WriteAllText(fresh, "created after admission");
            Refuse(() => NativeSpecialBookMenuAudit.CreateDiagnostic(admitted));
            Require(File.ReadAllText(fresh) == "created after admission", "Diagnostic creation lost its atomic freshness check.");
            Console.WriteLine("PASS SPECIAL diagnostic fresh-PNG policy, all input roots, directory boundaries and atomic no-overwrite admission.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        static void Refuse(Action action)
        {
            try { action(); } catch (Exception error) when (error is IOException or ArgumentException) { return; }
            throw new InvalidDataException("Unsafe SPECIAL diagnostic output was accepted.");
        }
    }

    private sealed class Fixture
    {
        internal List<byte> Code { get; } = [];
        internal Dictionary<uint, string> Strings { get; } = [];
        internal Dictionary<string, int> Fields { get; } = [];
        internal Dictionary<string, int> FormatCalls { get; } = [];
        internal Dictionary<string, int> CopyCalls { get; } = [];
        internal Dictionary<string, int> Managers { get; } = [];
        internal Dictionary<string, int> LoadBuffers { get; } = [];
        internal Dictionary<string, int> Receivers { get; } = [];
        internal Dictionary<string, int> CleanupInstructions { get; } = [];
        internal Dictionary<string, int> FieldInstructions { get; } = [];
        internal uint Directory { get; private set; }
        internal uint DirectFormat { get; private set; }
        internal uint Pc { get; private set; }
        internal int ConstructorCall { get; private set; }
        internal int PlatformBuffer { get; private set; }
        internal int NumberOrigin { get; private set; }
        internal int AlternativeTransport { get; private set; }
        internal int TransportGuard { get; private set; }
        internal int TransportDestination { get; private set; }
        internal int TransportSource { get; private set; }
        internal int AppendHelper { get; private set; }
        internal int AppendSource { get; private set; }
        internal int AppendCapacity { get; private set; }
        internal int AppendForwardSource { get; private set; }
        internal int AppendDestinationGuard { get; private set; }
        internal int AppendCapacityGuard { get; private set; }
        internal int AppendDestinationClear { get; private set; }
        internal int AppendNullSourceExit { get; private set; }
        internal int AppendScanOverflow { get; private set; }
        internal int AppendTerminated { get; private set; }
        internal int AppendCapacityResult { get; private set; }
        internal int AppendOverflowExit { get; private set; }
        internal int AppendOverflowStatus { get; private set; }
        internal int AppendInvalidStatus { get; private set; }
        internal int AppendStatusResult { get; private set; }
        internal int AppendSuccess { get; private set; }
        internal int TransportAppendCall { get; private set; }
        private uint _next;
        private readonly List<(int At, int Kind)> _calls = [];
        private readonly uint _manager;

        internal Fixture(bool optimized, uint origin, bool scheduled)
        {
            _next = origin; _manager = origin + 0x1000;
            Bytes(0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0); Push("Meshes/Terminals/Babybook02.NIF");
            Push("LookInside_Btn:0"); ConstructorCall = Call(0); Push("Surgery3DCamera");
            var owner = Code.Count; Bytes(0x55, 0x8b, 0xec, 0x81, 0xec); Dword(0x300);
            if (optimized) Bytes(0x8b, 0xf9); else { Bytes(0x89, 0x8d); Dword(unchecked((uint)-0x248)); }
            Directory = Literal("Textures/SourceBook/"); DirectFormat = Literal("%s%s.dds"); Pc = Literal("PC");
            var xbox = Literal("XBOX");
            var declarations = new[] { "BBNumber", "BBMessage0", "BBRTOff", "BBRTOn", "BBLTOff", "BBLTOn", "BBXOff", "BBXOn",
                "BBDecreaseOff", "BBDecreaseOn", "BBIncreaseOff", "BBIncreaseOn" };
            var offsets = new[] { 0x180, 0x1a8, 0x1c0, 0x1bc, 0x1c4, 0x1c8, 0x1cc, 0x1d0, 0x1d4, 0x1d8, 0x1dc, 0x1e0 };
            for (var index = 0; index < declarations.Length; index++)
            {
                var name = declarations[index]; var numeric = index < 2; var platform = index is 2 or 4 or 6 or 7;
                if (index == 2)
                {
                    if (optimized)
                    {
                        Bytes(0xba); Dword(Pc); Bytes(0xb9); Dword(xbox); Bytes(0x8d, 0x45, 0xf4, 0x0f, 0x44, 0xca, 0x51, 0x6a, 5, 0x50); Call(3);
                    }
                    else foreach (var pointer in new[] { xbox, Pc })
                        {
                            Bytes(0x68); Dword(pointer); Bytes(0x6a, 5); Address(-0x220); Bytes(0x50); Call(3);
                        }
                }
                var indexSlot = index == 0 ? -0x224 : -0x228;
                if (numeric)
                {
                    NumberOrigin = index == 0 ? Code.Count : NumberOrigin;
                    if (optimized) { if (index == 0) Bytes(0x33, 0xf6); else { Bytes(0xbe); Dword(1); } Bytes(0x56); }
                    else
                    {
                        Bytes(0xc7, 0x85); Dword(unchecked((uint)indexSlot)); Dword(index == 0 ? 0u : 1u);
                        Bytes(0x83, 0xbd); Dword(unchecked((uint)indexSlot)); Bytes(index == 0 ? (byte)11 : (byte)5);
                        Value(indexSlot, 1); Bytes(0x51);
                    }
                }
                Push(name);
                if (platform)
                {
                    if (optimized) { Bytes(0x8d, 0x85); PlatformBuffer = index == 2 ? Code.Count : PlatformBuffer; Dword(unchecked((uint)-12)); }
                    else { Bytes(0x8d, 0x85); PlatformBuffer = index == 2 ? Code.Count : PlatformBuffer; Dword(unchecked((uint)-0x220)); }
                    Bytes(0x50);
                }
                Bytes(0x68); Dword(index == 1 ? Literal("Textures/SourceLanguage/") : Directory);
                Bytes(0x68); Dword(numeric ? Literal("%s%s%i.dds") : platform ? Literal("%s\\%s\\%s.dds") : DirectFormat);
                var buffer = platform ? -0x108 : -0x200;
                if (optimized) { Address(buffer); Size(); Bytes(0x50); } else { Size(); Address(buffer); Bytes(0x50); }
                FormatCalls.Add(name, Call(1));
                if (!platform) { Size(); Address(-0x108); Bytes(0x50); Address(buffer); Bytes(0x50); CopyCalls.Add(name, Call(4)); }
                if (optimized)
                {
                    if (scheduled && index == 9) { CleanupInstructions.Add(name, Code.Count); Bytes(0x83, 0xc4, 0x20); }
                    Bytes(0x8b, 0x0d); Managers.Add(name, Code.Count); Dword(_manager);
                    Bytes(0x8d, 0x87); Fields.Add(name, Code.Count); Dword((uint)offsets[index]);
                    if (!scheduled || index != 9) { CleanupInstructions.Add(name, Code.Count); Bytes(0x83, 0xc4, 0x18); }
                    if (numeric) Bytes(0x8d, 0x04, 0xb0);
                    Bytes(0x6a, 0, 0x6a, 0, 0x50); Bytes(0x8d, 0x85); LoadBuffers.Add(name, Code.Count); Dword(unchecked((uint)-0x108)); Bytes(0x50); Call(2);
                    if (numeric) Bytes(0x46, 0x83, 0xfe, index == 0 ? (byte)11 : (byte)5, 0x7c, 0);
                }
                else
                {
                    Bytes(0x6a, 0, 0x6a, 0);
                    if (numeric) Value(indexSlot, 1);
                    var accumulator = scheduled && index is 7 or 10;
                    Bytes(0x8b, numeric || accumulator ? (byte)0x85 : (byte)0x95); Receivers.Add(name, Code.Count); Dword(unchecked((uint)-0x248));
                    FieldInstructions.Add(name, Code.Count);
                    if (numeric) Bytes(0x8d, 0x94, 0x88); else if (accumulator) Bytes(0x05); else Bytes(0x81, 0xc2);
                    Fields.Add(name, Code.Count); Dword((uint)offsets[index]); Bytes(accumulator ? (byte)0x50 : (byte)0x52);
                    Bytes(0x8d, 0x8d); LoadBuffers.Add(name, Code.Count); Dword(unchecked((uint)-0x108)); Bytes(0x51);
                    Bytes(0x8b, 0x0d); Managers.Add(name, Code.Count); Dword(_manager); Call(2);
                }
            }
            Bytes(0x8b, 0xe5, 0x5d, 0xc3);
            var helpers = new int[9]; helpers[0] = owner;
            for (var index = 1; index <= 3; index++) { helpers[index] = Code.Count; Bytes(0xc3); }
            AlternativeTransport = Transport(optimized); helpers[4] = Transport(optimized);
            helpers[5] = Code.Count;
            if (!optimized)
            {
                // A transparent argument-forwarding wrapper has a distinct
                // identity from both the normalizer and bounded append body.
                Bytes(0x55, 0x8b, 0xec);
                for (var index = 0; index < 3; index++)
                {
                    Bytes(0x8b, (byte)(0x45 | index << 3));
                    if (index == 0) AppendForwardSource = Code.Count;
                    Bytes((byte)(16 - index * 4), (byte)(0x50 + index));
                }
                Call(6); Bytes(0x83, 0xc4, 12, 0x5d, 0xc3);
            }
            helpers[6] = AppendHelper = Append(optimized);
            // First-party fixture callback owners keep error/status calls in
            // the code extent without importing a retail function body.
            for (var index = 7; index <= 8; index++) { helpers[index] = Code.Count; Bytes(0xc3); }
            foreach (var (at, kind) in _calls) Write(at, unchecked((uint)(helpers[kind] - at - 4)));
        }
        internal FalloutSpecialBookTextures Read() => FalloutExecutableStringTable.ReadSpecialBookTextureDeclarations(Code.ToArray(), pointer => Strings.GetValueOrDefault(pointer));
        internal void Write(int at, uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); for (var index = 0; index < 4; index++) Code[at + index] = bytes[index]; }
        internal void Retarget(int operand, int target) => Write(operand, unchecked((uint)(target - operand - 4)));
        internal void BranchTo(int operand, int target) => PatchShort(operand, target);
        private int Transport(bool optimized)
        {
            var owner = Code.Count; Bytes(0x55, 0x8b, 0xec, 0x83, 0xec, 0x44);
            const byte flag = 0xe7, source = 0xe8;
            if (optimized)
            {
                Bytes(0x57, 0x8b, 0x7d, 8, 0x85, 0xff); TransportGuard = Code.Count + 1; var inputGuard = Near(0x84);
                Bytes(0x8b, 0x45); TransportDestination = Code.Count; Bytes(12, 0x85, 0xc0); var destinationGuard = Near(0x84);
                Bytes(0x83, 0x7d, 16, 0); var sizeGuard = Near(0x84); Bytes(0xc6, 0, 0, 0xc6, 0x45, flag, 0, 0x53, 0x56);
                Bytes(0x90, 0x90, 0x90); Bytes(0x57, 0xff, 0x75, 16, 0xff, 0x75, 12); TransportAppendCall = Call(5);
                Bytes(0x8a, 0x45, flag, 0x83, 0xc4, 12, 0x5e, 0x5b, 0x5f, 0x8b, 0xe5, 0x5d, 0xc3);
                var invalid = Code.Count; Bytes(0x32, 0xc0, 0x5f, 0x8b, 0xe5, 0x5d, 0xc3);
                foreach (var branch in new[] { inputGuard, destinationGuard, sizeGuard }) Retarget(branch, invalid);
                TransportSource = owner + 9;
            }
            else
            {
                Bytes(0x83, 0x7d, 8, 0); TransportGuard = Code.Count; var inputGuard = Short(0x74);
                Bytes(0x83, 0x7d, 12, 0); var destinationGuard = Short(0x74);
                Bytes(0x83, 0x7d, 16, 0); var sizeGuard = Short(0x75);
                var invalid = Code.Count; Bytes(0x32, 0xc0, 0xe9); var exit = Code.Count; Dword(0);
                var body = Code.Count; Bytes(0xc6, 0x45, flag, 0, 0x8b, 0x45); TransportDestination = Code.Count;
                Bytes(12, 0xc6, 0, 0, 0x8b, 0x4d); TransportSource = Code.Count; Bytes(8, 0x89, 0x4d, source);
                Bytes(0x90, 0x90, 0x90);
                Bytes(0x8b, 0x55, source, 0x52, 0x8b, 0x45, 16, 0x50, 0x8b, 0x4d, 12, 0x51); TransportAppendCall = Call(5);
                Bytes(0x83, 0xc4, 12, 0x8a, 0x45, flag); Retarget(exit, Code.Count); Bytes(0x8b, 0xe5, 0x5d, 0xc3);
                PatchShort(inputGuard, invalid); PatchShort(destinationGuard, invalid); PatchShort(sizeGuard, body);
            }
            return owner;
        }
        private int Append(bool optimized)
        {
            var owner = Code.Count; Bytes(0x8b, 0xff, 0x55, 0x8b, 0xec);
            int scan, scanBack, copy, copyBack;
            if (optimized)
            {
                Bytes(0x56, 0x8b, 0x75, 8, 0x57, 0x85, 0xf6); AppendDestinationGuard = Short(0x74);
                Bytes(0x8b, 0x4d, 12, 0x85, 0xc9); AppendCapacityGuard = Short(0x74);
                Bytes(0x8b, 0x7d, 16, 0x85, 0xff); var sourceGuard = Short(0x75);
                AppendDestinationClear = Code.Count; Bytes(0xc6, 6, 0);
                var invalid = Code.Count; Call(7); Bytes(0x6a); AppendInvalidStatus = Code.Count; Bytes(22, 0x5e, 0x89, 0x30); Call(8);
                var exit = Code.Count; Bytes(0x5f, 0x8b); AppendStatusResult = Code.Count; Bytes(0xc6, 0x5e, 0x5d, 0xc3);
                scan = Code.Count; Bytes(0x8b, 0xd6, 0x80, 0x3a, 0); var found = Short(0x74);
                Bytes(0x42, 0x83, 0xe9, 1); scanBack = Short(0x75); Bytes(0x85, 0xc9); AppendScanOverflow = Short(0x74);
                PatchShort(found, Code.Count - 4); Bytes(0x2b, 0xfa);
                copy = Code.Count; Bytes(0x8a); AppendSource = Code.Count; Bytes(4, 0x17, 0x88, 2, 0x42, 0x84, 0xc0); AppendTerminated = Short(0x74);
                AppendCapacity = Code.Count + 1; Bytes(0x83, 0xe9, 1); copyBack = Short(0x75);
                PatchShort(AppendTerminated, Code.Count); Bytes(0x85, 0xc9); AppendCapacityResult = Short(0x75);
                Bytes(0x88, 0x0e); Call(7); Bytes(0x6a); AppendOverflowStatus = Code.Count; Bytes(34); AppendOverflowExit = Short(0xeb);
                AppendSuccess = Code.Count; Bytes(0x33, 0xf6); var successExit = Short(0xeb);
                PatchShort(AppendDestinationGuard, invalid); PatchShort(AppendCapacityGuard, invalid);
                PatchShort(sourceGuard, scan); PatchShort(AppendScanOverflow, AppendDestinationClear);
                PatchShort(AppendCapacityResult, AppendSuccess); PatchShort(AppendOverflowExit, invalid + 7); PatchShort(successExit, exit);
                PatchShort(scanBack, scan + 2);
            }
            else
            {
                Bytes(0x8b, 0x45, 8, 0x53, 0x33, 0xdb, 0x56, 0x57, 0x3b, 0xc3); AppendDestinationGuard = Short(0x74);
                Bytes(0x8b, 0x7d, 12, 0x3b, 0xfb); AppendCapacityGuard = Short(0x77);
                var invalid = Code.Count; Call(7); Bytes(0x6a); AppendInvalidStatus = Code.Count; Bytes(22, 0x5e, 0x89, 0x30);
                Bytes(0x53, 0x53, 0x53, 0x53, 0x53); Call(8); Bytes(0x83, 0xc4, 20, 0x8b); AppendStatusResult = Code.Count; Bytes(0xc6); var invalidExit = Short(0xeb);
                scan = Code.Count; Bytes(0x8b, 0x75, 16, 0x3b, 0xf3); var sourceGuard = Short(0x75);
                AppendDestinationClear = Code.Count; Bytes(0x88, 0x18); AppendNullSourceExit = Short(0xeb);
                Bytes(0x8b, 0xd0, 0x38, 0x1a); var found = Short(0x74);
                Bytes(0x42, 0x4f); scanBack = Short(0x75); Bytes(0x3b, 0xfb); AppendScanOverflow = Short(0x74);
                copy = Code.Count; Bytes(0x8a); AppendSource = Code.Count; Bytes(0x0e, 0x88, 0x0a, 0x42, 0x46, 0x3a, 0xcb); AppendTerminated = Short(0x74);
                AppendCapacity = Code.Count; Bytes(0x4f); copyBack = Short(0x75);
                PatchShort(AppendTerminated, Code.Count); Bytes(0x3b, 0xfb); AppendCapacityResult = Short(0x75);
                Bytes(0x88, 0x18); Call(7); Bytes(0x6a); AppendOverflowStatus = Code.Count;
                Bytes(34, 0x59, 0x89, 8, 0x8b, 0xf1); AppendOverflowExit = Short(0xeb);
                AppendSuccess = Code.Count; Bytes(0x33, 0xc0); var exit = Code.Count; Bytes(0x5f, 0x5e, 0x5b, 0x5d, 0xc3);
                PatchShort(AppendDestinationGuard, invalid); PatchShort(AppendCapacityGuard, scan); PatchShort(sourceGuard, scan + 11);
                PatchShort(AppendNullSourceExit, invalid); PatchShort(found, copy - 4); PatchShort(AppendScanOverflow, AppendDestinationClear); PatchShort(scanBack, scan + 13);
                PatchShort(AppendCapacityResult, AppendSuccess); PatchShort(AppendOverflowExit, invalid + 10); PatchShort(invalidExit, exit);
            }
            PatchShort(copyBack, copy);
            while (Code.Count < owner + 116) Bytes(0x90);
            return owner;
        }
        private int Near(byte condition) { Bytes(0x0f, condition); var operand = Code.Count; Dword(0); return operand; }
        private int Short(byte instruction) { Bytes(instruction); var operand = Code.Count; Bytes(0); return operand; }
        private void PatchShort(int operand, int target) => Code[operand] = unchecked((byte)checked((sbyte)(target - operand - 1)));
        private void Bytes(params byte[] bytes) => Code.AddRange(bytes);
        private void Dword(uint value) => Code.AddRange(BitConverter.GetBytes(value));
        private uint Literal(string value) { var pointer = _next++; Strings.Add(pointer, value); return pointer; }
        private void Push(string value) { Bytes(0x68); Dword(Literal(value)); }
        private int Call(int kind) { Bytes(0xe8); var at = Code.Count; Dword(0); _calls.Add((at, kind)); return at; }
        private void Size() { Bytes(0x68); Dword(0x104); }
        private void Address(int slot) { Bytes(0x8d, 0x85); Dword(unchecked((uint)slot)); }
        private void Value(int slot, byte register) { Bytes(0x8b, (byte)(0x85 | register << 3)); Dword(unchecked((uint)slot)); }
    }
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidDataException("Malformed SPECIAL texture declaration was accepted.");
    }
}
