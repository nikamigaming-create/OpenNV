using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock PackageCommandFieldsQualificationLock = new();
    private static string? _qualifiedPackageCommandFieldsIdentity;

    internal static FalloutPackageCommandFieldsSource ReadPackageCommandFields(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutPackageCommandFieldsSource.Reviewed(controls); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Package command fields changed their selected original image.");
        // Related command, mover and Script readers qualify the same immutable
        // selectors. Retain one completed qualification, while every request
        // still checks the current original bytes before using it.
        lock (PackageCommandFieldsQualificationLock)
        {
            if (_qualifiedPackageCommandFieldsIdentity == source.Identity) return source;
            QualifyPackageCommandFields(path, controls, original);
            _qualifiedPackageCommandFieldsIdentity = source.Identity;
            return source;
        }
    }

    private static void QualifyPackageCommandFields(string path, FalloutPlayerMovementControlSource controls, byte[] original)
    {
        _ = ReadPlayerReferenceHud(path, controls.Main);
        _ = ReadPlayerFollowingContinuation(path, controls);
        var (code, image) = Load(original);
        var player = Class(".?AVPlayerCharacter@@", 191);
        var high = Class(".?AVHighProcess@@", 460);
        var middle = Class(".?AVMiddleHighProcess@@", 460);
        var constructor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
        var character = Call(constructor - 5);
        Need(character + 7, [0x6a, 1], "actual Actor constructor mode");
        var actor = Call(character + 12);
        var mobile = Call(actor + 0x2b);
        var reference = Call(mobile + 0xa);
        Need(reference + 0x36, [0x8d, 0x4e, 0x40], "actual Extra-list constructor receiver");
        var extra = Call(reference + 0x4d);
        Need(extra + 4, [0xc7, 0x41, 4, 0, 0, 0, 0, 0x0f, 0x57, 0xc0, 0x0f, 0x11, 0x41, 8,
            0x66, 0xc7, 0x41, 0x18, 0, 0], "null Extra head and zero presence bits");
        Need(reference + 0x63, [0x0f, 0x11, 0x46, 0x1c], "independent raw reference rotation constructor stores");
        Need(reference + 0x6b, [0x66, 0x0f, 0xd6, 0x46, 0x2c, 0xc7, 0x46, 0x34, 0, 0, 0, 0], "three raw current-position constructor lanes");
        Need(reference + 0x17e, [0xc7, 0x46, 0x3c, 0, 0, 0, 0], "independent constructor ParentCELL");
        var initialize = Call(actor + 0xe9);
        Need(initialize + 0x205, [0xc6, 0x86, 0x41, 1, 0, 0, 0], "adjusted-position flag constructor store");
        var position = Get(player, 0x1f4);
        Need(position + 9, [0x80, 0xbe, 0x41, 1, 0, 0, 0, 0x0f, 0x84], "selected raw-versus-adjusted position predicate");
        Need(position + 0xe1, [0x8d, 0x46, 0x2c, 0x5e, 0x8b, 0xe5, 0x5d, 0xc3], "current raw XYZ getter without pending/native pose");

        Need(constructor + 0x54a, [0xc7, 0x87, 0xa8, 5, 0, 0, 0, 0, 0, 0], "Player view constructor word");
        Need(constructor + 0x5bf, [0xc7, 0x87, 0xec, 5, 0, 0, 0, 0, 0, 0], "independent first-person animation context constructor");
        Need(constructor + 0x617, [0xc7, 0x87, 0xfc, 5, 0, 0, 0, 0, 0, 0], "independent override context constructor");
        var animation = Get(player, 0x1e4);
        Need(animation, [0x8b, 0x81, 0xfc, 5, 0, 0, 0x85, 0xc0, 0x75, 0x17, 0x38, 0x81, 0xa8, 5, 0, 0,
            0x75, 0xa, 0x8b, 0x81, 0xec, 5, 0, 0, 0x85, 0xc0, 0x75, 5, 0xe9], "exact override/first/process animation selector");
        var fallback = Jump(animation + 0x1c);
        Need(fallback, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0xb4, 1, 0, 0,
            0x33, 0xc0, 0xc3], "current process animation getter and genuine absent-process return");
        Need(Get(high, 0x1b4), [0x8b, 0x81, 0xc0, 1, 0, 0, 0xc3], "actual Common animation-context field");
        var middleConstructor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
            Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]), 0x440);
        Need(middleConstructor + 0x149, [0xc7, 0x86, 0xc0, 1, 0, 0, 0, 0, 0, 0], "independent process animation constructor");
        Need(Get(player, 0x100), [0xb0, 1, 0xc3], "selected Player eligibility virtual");
        var idle = Get(high, 0x44);
        Need(idle + 0x12, [0xff, 0x90, 0xe4, 1, 0, 0], "package-start animation getter before source eligibility");
        Need(idle + 0x1f, [0x8b, 0x80, 0, 1, 0, 0, 0xff, 0xd0], "independent Actor eligibility child");
        Need(idle + 0x2f, [0x83, 0x7d, 0xc, 0, 0x0f, 0x84], "context null skips later animation effects");
        Need(idle + 0x1bd, [0x5f, 0x5e, 0x32, 0xc0, 0x5b, 0x5d, 0xc2, 0x18, 0], "genuine false idle return");

        var eventGetter = Unique(at => Match(code, at, [0x6a, 0xd, 0xe8]) &&
            Match(code, at + 7, [0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 0x10, 0xc3, 0x33, 0xc0, 0xc3]), 18);
        var lookup = Call(eventGetter + 2);
        Need(lookup + 0x17, [0x83, 0xfa, 0x12, 0x0f, 0x83], "source Extra presence extent");
        Need(lookup + 0x2c, [0x84, 0x44, 0x3a, 8, 0x0f, 0x84], "absent Extra type skips cache/list children");
        var scriptClass = Class(".?AVExtraScript@@", 2);
        var scriptConstructor = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x45, 8,
            0x89, 0x41, 0xc, 0x8b, 0xc1, 0x89, 0x4d, 0xfc, 0xc6, 0x41, 4, 0xd, 0xc7, 0x41, 8, 0, 0, 0, 0,
            0xc7, 1]) && Word(code, at + 28) == scriptClass, 0x2d);
        Need(scriptConstructor + 0x20, [0xc7, 0x41, 0x10, 0, 0, 0, 0, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0],
            "separate Script identity and constructor-null event list");
        var marker = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x8b, 0xc1,
            0xc6, 0x45, 0xff, 0, 0x89, 0x45, 0xf8, 0x8b, 0x50, 8, 0x85, 0xd2, 0x75, 8,
            0x32, 0xc0, 0x8b, 0xe5, 0x5d, 0xc2, 8, 0]), 0x8d);
        Need(marker + 0x31, [0x3b, 0xf3, 0x74, 0x12], "existing matching event row selection");
        Need(marker + 0x47, [0x09, 0x78, 4, 0xb2, 1], "matched/empty row mask OR store");
        Need(marker + 0x7d, [0x09, 0x79, 4, 0xb2, 1], "independent first wildcard row mask OR");
        Need(marker + 0x84, [0x8a, 0xc2, 0x5b, 0x8b, 0xe5, 0x5d, 0xc2, 8, 0], "event marker return without allocation");

        var saved = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68]) &&
            Match(code, at + 0x24, [0x8b, 0xf1, 0x6a, 0x18, 0xe8]) &&
            Match(code, at + 0x2d, [0x8b, 0xd0, 0x85, 0xd2]), 0xcc);
        var savedConstructor = Call(saved + 0x63);
        Need(savedConstructor + 7, [0x85, 0xc0, 0xc6, 0x41, 4, 0x18, 0x0f, 0x44, 0x45, 0xc], "saved space chooses Worldspace then CELL");
        Need(savedConstructor + 0x38, [0x89, 0x41, 0x18, 0x8b, 0xc1, 0xf3, 0x0f, 0x11, 0x41, 0x1c], "new saved XYZ and raw rotation-Z fields");
        Need(saved + 0x9e, [0x8b, 0x45, 8, 0x85, 0xc0, 0x0f, 0x44, 0x45, 0xc, 0x89, 0x42, 0xc], "existing saved space store");
        Need(saved + 0xad, [0xf3, 0x0f, 0x7e, 0, 0x66, 0x0f, 0xd6, 0x42, 0x10], "existing saved XY without rotation rewrite");
        RequirePackageAnimationConfiguredStart(code, image);
        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Package command fields lack a unique original class: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
            throw new NotSupportedException("Package command field constructor/getter is absent or ambiguous.");
        int Call(int at) => Branch(at, 0xe8);
        int Jump(int at) => Branch(at, 0xe9);
        int Branch(int at, byte operation) => code[at] == operation ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Package command field source lost an actual selected constructor/getter child.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Package command fields changed their original " + role + "."); }
    }
}
