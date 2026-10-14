using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPackageAssignmentSource ReadPackageAssignment(string path, FalloutPackageCommandFieldsSource fields)
    {
        var source = FalloutPackageAssignmentSource.Reviewed(fields); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Package assignment changed its selected original image.");
        _ = ReadPackageCommandFields(path, fields.Controls);
        _ = ReadPackageState(path, fields.Controls);
        var (code, image) = Load(original);
        var high = Class(".?AVHighProcess@@", 460);
        var player = Class(".?AVPlayerCharacter@@", 191);
        var middle = Class(".?AVMiddleHighProcess@@", 460);
        var declaration = image.ScriptCommandDeclarations(code).Values.Where(value => value.Name == "AddScriptPackage").ToArray();
        if (declaration is not [{ RequiresReference: true, Required: 1, Parameters: { Count: 1 } }])
            throw new NotSupportedException("Package assignment lost its original reference command and one required package operand.");
        var command = image.ScriptCommandStart("AddScriptPackage", code);
        Need(command, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x56, 0x8b, 0x75, 0x10, 0x8d, 0x45, 0xfc, 0x50,
            0xff, 0x75, 0x1c, 0xc7, 0x45, 0xfc, 0, 0, 0, 0], "actual named AddScriptPackage command handler");
        Need(command + 0x30, [0x84, 0xc0, 0x75, 5, 0x5e, 0x8b, 0xe5, 0x5d, 0xc3], "failed original parameter extraction return");
        var special = Call(command + 0x54);
        Need(special, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 0x15, 0x8b, 1, 0xff, 0x90, 0x78, 2, 0, 0,
            0x85, 0xc0, 0x74, 9, 0x80, 0x78, 0x20, 0x1c, 0x75, 3, 0xb0, 1, 0xc3, 0x32, 0xc0, 0xc3], "special package process/getter/kind predicate");
        Need(Get(high, 0x278), [0x56, 0x8b, 0xf1, 0x8b, 6, 0xff, 0x90, 8, 2, 0, 0, 0x8b, 0x16, 0x8b, 0xce, 0x5e,
            0x85, 0xc0, 0x74, 6, 0xff, 0xa2, 8, 2, 0, 0, 0xff, 0xa2, 0x28, 2, 0, 0], "Primary predicate then reread or Current getter");
        Need(Get(high, 0x208), [0x8b, 0x81, 0xe4, 0, 0, 0, 0xc3], "distinct Primary pointer getter");
        Need(Get(high, 0x228), [0x8b, 0x41, 4, 0xc3], "distinct Current pointer getter");
        Need(command + 0x5b, [0x74, 0xa, 0x8b, 0x16, 0x8b, 0xce, 0xff, 0x92, 0x88, 2, 0, 0], "selected special callback before initial guards");
        Need(Get(player, 0x288), [0xc2, 0, 0], "selected Player special callback void return");
        Need(command + 0x67, [0x85, 0xf6, 0x0f, 0x84, 0x40, 1, 0, 0, 0x83, 0x7d, 0xfc, 0, 0x0f, 0x84, 0x36, 1, 0, 0,
            0x8b, 0x4e, 0x60, 0x85, 0xc9, 0x0f, 0x84, 0x2b, 1, 0, 0], "initial nulls join direct true return");
        Need(command + 0x88, [0xff, 0x90, 0x28, 2, 0, 0], "direct Current getter before outgoing event");
        Need(command + 0x97, [0x68, 0, 8, 0, 0, 0x53, 0x57, 0xe8], "old package mask 800 before change callback");
        Need(command + 0xa9, [0x8b, 1, 0x57, 0x56, 0xff, 0x90, 0x94, 5, 0, 0], "old package selected change callback");
        Need(command + 0xb3, [0x8b, 0x4e, 0x60, 0x6a, 0, 0x8b, 1, 0xff, 0x90, 0xa0, 5, 0, 0], "request-byte false setter before start event");
        Need(Get(high, 0x5a0), [0x55, 0x8b, 0xec, 0x8a, 0x51, 0x30, 0x8a, 0xc2, 0x80, 0xca, 0x20, 0x24, 0xdf,
            0x0f, 0xb6, 0xd2, 0x80, 0x7d, 8, 0, 0x0f, 0xb6, 0xc0, 0x0f, 0x44, 0xd0, 0x88, 0x51, 0x30, 0x5d, 0xc2, 4, 0],
            "supplied Boolean mask20 independent byte store");
        Need(command + 0xc0, [0x68, 0, 2, 0, 0, 0x53, 0xff, 0x75, 0xfc, 0xe8], "requested package mask 200 before start callback");
        if (Call(command + 0x9e) != Call(command + 0xc9)) throw new NotSupportedException("Package command selected different event-marker children.");
        var eventWrapper = Call(command + 0xc9);
        Need(eventWrapper, [0x55, 0x8b, 0xec, 0x53, 0x56, 0x8b, 0x75, 0xc, 0x32, 0xdb, 0x85, 0xf6, 0x74, 0x23,
            0x8b, 0xce, 0xe8], "nonnull Extra list before event-list getter predicate");
        Need(eventWrapper + 0x15, [0x85, 0xc0, 0x74, 0x18, 0x8b, 0xce, 0xe8], "genuine absent-event-list return versus distinct getter reread");
        if (Call(eventWrapper + 0x10) != Call(eventWrapper + 0x1b))
            throw new NotSupportedException("Package event marking lost its repeated selected event-list getter.");
        Need(eventWrapper + 0x20, [0xff, 0x75, 0x10, 0x8b, 0xc8, 0xff, 0x75, 8, 0xe8], "mask and actual package operands before existing-list marker");
        Need(command + 0xda, [0xff, 0x90, 0x90, 5, 0, 0], "selected package-start callback before procedure");
        Need(command + 0xe5, [0x83, 0x78, 0x18, 0xff, 0x75, 0x10], "independent mutable procedure minus-one predicate");
        var procedure = Call(command + 0xf6);
        Need(procedure + 0x18, [0x0f, 0xbe, 0x46, 0x20, 0x83, 0xf8, 0x24, 0x0f, 0x87], "signed package-kind procedure table");
        Need(procedure + 0x25, [0xff, 0x24, 0x85], "actual procedure table dispatch");
        var procedureTable = Word(code, procedure + 0x28);
        var pureReturn = ProcedureJoin(Offset(Word(image.Read(procedureTable + 6 * 4, 4), 0)));
        Need(pureReturn, [0x83, 0x7e, 0x18, 0xff, 0x75], "terminal minus-one error predicate");
        for (sbyte kind = 0; kind <= 36; ++kind)
        {
            if (FalloutPackageAssignmentSource.PureProcedure(kind) is not { } value) continue;
            var arm = Offset(Word(image.Read(procedureTable + (uint)kind * 4, 4), 0));
            Need(arm, [0xc7, 0x46, 0x18], "pure procedure field store");
            if (BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(arm + 3)) != value)
                throw new NotSupportedException("Package assignment changed its signed-kind pure procedure value.");
            if (ProcedureJoin(arm) != pureReturn)
                throw new NotSupportedException("Package procedure store no longer joins its terminal predicate directly.");
        }
        Need(command + 0xfb, [0x8b, 6, 0xf3, 0x0f, 0x10, 0x46, 0x28, 0x51, 0x8b, 0xce, 0xf3, 0x0f, 0x11, 4, 0x24,
            0xff, 0x90, 0xf4, 1, 0, 0, 0x50, 0xff, 0x76, 0x3c, 0x8b, 0xce, 0xe8], "raw rotation then current XYZ and ParentCELL before Worldspace");
        Need(command + 0x123, [0x8b, 0x4d, 0xfc, 0x6a, 1, 0xe8], "package runtime flag writer after saved-reference child");
        var packageFlag = Call(command + 0x128);
        Need(packageFlag, [0x55, 0x8b, 0xec, 0x8b, 0x51, 0x1c, 0x8b, 0xc2, 0x25, 0xff, 0xbf, 0xff, 0xff,
            0x81, 0xca, 0, 0x40, 0, 0, 0x80, 0x7d, 8, 0, 0x0f, 0x44, 0xd0, 0x89, 0x51, 0x1c, 0x5d, 0xc2, 4, 0],
            "supplied Boolean flag4000 preserving independent runtime bits");
        Need(command + 0x12d, [0x8b, 6, 0x8b, 0xce, 0x6a, 0, 0x6a, 0, 0xff, 0x75, 0xfc, 0xff, 0x90, 0xf4, 2, 0, 0], "Actor current assignment with both original false modes");
        Need(command + 0x13e, [0x8b, 0x46, 0x60, 0x85, 0xc0, 0x74, 0x5a, 0x83, 0x78, 0x28, 0, 0x74, 0x54], "fresh tail process-null and tier-zero Player join");
        Need(command + 0x189, [0x6a, 0, 0x6a, 0, 0x6a, 0, 0xff, 0x70, 0x28, 0x56, 0xe8], "tier-one independent process-manager tail");
        Need(command + 0x19f, [0xa1], "independent actual Player identity tail");
        Need(command + 0x1a4, [0x3b, 0xf0, 0x75, 7, 0xc6, 0x80, 0xe7, 6, 0, 0, 1], "Fourth byte true only in actual Player tail");
        Need(command + 0x1af, [0xb0, 1, 0x5e, 0x8b, 0xe5, 0x5d, 0xc3], "direct source true command return");

        var start = Get(high, 0x590);
        Need(start + 0x16, [0x8b, 0x43, 0x50, 0x89, 0x45, 0xc, 0x85, 0xc0, 0x74, 0x16], "independent result Script before idle selection");
        Need(start + 0x36, [0x8b, 0x4b, 0x4c, 0x85, 0xc9, 0x74, 0x1f], "nonnull versus null IDLE branch");
        Need(start + 0x4b, [0xff, 0x50, 0x44, 0x8b, 7, 0x8b, 0xcf, 0x6a, 1, 0xff, 0x90, 0x80, 5, 0, 0, 0xeb, 0x3c], "idle child return before start byte supplied one");
        Need(Get(high, 0x580), [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x81, 0x9c, 1, 0, 0, 0x5d, 0xc2, 4, 0], "distinct supplied start byte setter");
        var middleConstructor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
            Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]), 0x440);
        Need(middleConstructor + 0x365, [0x66, 0xc7, 0x86, 0x9c, 1, 0, 0, 0, 0], "MiddleHigh start word constructor with independent adjacent byte");
        Need(start + 0x5c, [0xc6, 0x87, 0x10, 1, 0, 0, 1], "null-IDLE different byte one before animation query");
        Need(start + 0x67, [0xff, 0x90, 0xe4, 1, 0, 0, 0x85, 0xc0, 0x74, 0x27, 0x8b, 6, 0x8b, 0xce, 0xff, 0x90, 0xe4, 1, 0, 0],
            "null-IDLE selected animation predicate and distinct reread");
        Need(start + 0x98, [0x8b, 0x4b, 0x54, 0x85, 0xc9, 0x74, 0x14], "independent topic after returned idle arm");
        Need(start + 0xb3, [0x85, 0xf6, 0x74, 7, 0x8b, 0xce, 0xe8], "nonnull Actor final cached-child cleanup");
        var cleanup = Call(start + 0xb9);
        Need(cleanup, [0x8b, 0x41, 0x60, 0x85, 0xc0, 0x74, 0x1f, 0x8b, 0x50, 0x2c, 0x85, 0xd2, 0x74, 0xa,
            0x81, 0x62, 0x40, 0xff, 0xdf, 0xff, 0xff, 0x8b, 0x41, 0x60, 0x8b, 0x40, 0x2c, 0x85, 0xc0, 0x74, 7,
            0x81, 0x60, 0x40, 0xff, 0xef, 0xff, 0xff, 0xc3], "nonnull cached child 2000 then fresh child 1000 clear");
        return source;

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Package command lacks a unique selected original class: " + name);
        int Get(uint table, uint slot) => Offset(Word(image.Read(table + slot, 4), 0));
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Package command lost an exact source child association.");
        int ProcedureJoin(int arm) => code[arm + 7] switch
        {
            0xe9 => checked(arm + 12 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(arm + 8))),
            0xeb => checked(arm + 9 + (sbyte)code[arm + 8]),
            _ => throw new NotSupportedException("Pure package procedure arm no longer returns through a direct source join.")
        };
        int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
            throw new NotSupportedException("Package command source is absent or ambiguous.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Package assignment changed its original " + role + "."); }
    }
}
