using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock ActorPackageAssignmentQualificationLock = new();
    private static string? _qualifiedActorPackageAssignment;

    internal static FalloutActorPackageAssignmentSource ReadActorPackageAssignment(string path, FalloutPackageCommandFieldsSource fields)
    {
        var source = FalloutActorPackageAssignmentSource.Reviewed(fields); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Actor assignment changed its selected owned source image.");
        lock (ActorPackageAssignmentQualificationLock)
        {
            if (_qualifiedActorPackageAssignment == source.Identity) return source;
            _ = ReadPackageAssignment(path, fields); _ = ReadReferencePosition(path, fields);
            var (code, image) = Load(original);
            var player = Class(".?AVPlayerCharacter@@", 191); var high = Class(".?AVHighProcess@@", 460);
            var body = Get(player, 0x2f4);
            Need(body, [0x55, 0x8b, 0xec, 0x57, 0x8b, 0xf9, 0x83, 0x7f, 0x60, 0], "actual nullable process before every Actor child");
            Need(body + 0x10, [0x53, 0x8b, 0x5d, 8, 0x83, 0x7b, 0x18, 0xff], "same mutable requested procedure reread");
            var command = image.ScriptCommandStart("AddScriptPackage", code);
            if (Call(body + 0x22) != Call(command + 0xf6))
                throw new NotSupportedException("Actor and command procedure calls lost their shared source producer.");
            Need(body + 0x27, [0x8a, 0x43, 0x20, 0x3c, 0x1c], "kind28 status bypass");
            Need(body + 0x32, [0x3c, 0x1b], "kind27 status bypass");
            Need(body + 0x3a, [0x3c, 0x1a], "kind26 status bypass");
            Need(body + 0x46, [0xff, 0x90, 0x14, 2, 0, 0], "same existing raw Actor status getter");
            Need(Get(player, 0x214), [0x8b, 0x81, 0x9c, 1, 0, 0, 0xc3], "status and yaw-selection field alias");
            Need(body + 0xc5, [0x80, 0x7d, 0x10, 0, 0x74], "independent alternate argument");
            Need(body + 0xd4, [0x80, 0x7d, 0xc, 0], "independent Primary argument");
            Need(body + 0xe4, [0x8b, 0x80, 0xdc, 1, 0, 0, 0xff, 0xd0], "Current adjustment selected child");
            Need(Get(high, 0x1dc), [0xd9, 0x81, 0x44, 3, 0, 0, 0xc3], "independent High adjustment getter");
            Need(body + 0xf4, [0x0f, 0x2f, 5], "ordered positive adjustment comparison");
            if (Word(image.Read(Word(code, body + 0xf7), 4), 0) != 0)
                throw new NotSupportedException("Actor adjustment comparison changed its original positive-zero operand.");
            Need(body + 0x117, [0xff, 0x96, 0xe0, 1, 0, 0], "selected adjustment accumulation child");
            Need(Get(high, 0x1e0), [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x45, 8, 0xf3, 0x0f, 0x58, 0x81, 0x44, 3, 0, 0,
                0xf3, 0x0f, 0x11, 0x81, 0x44, 3, 0, 0, 0x5d, 0xc2, 4, 0], "Float32 adjustment accumulate/store");
            Need(body + 0x145, [0x80, 0x7b, 0x20, 0x12, 0x74], "kind18 assignment byte bypass");
            Need(body + 0x14b, [0x8a, 0x87, 0xf8, 0, 0, 0], "old independent Actor byte read");
            Need(body + 0x158, [0xc6, 0x87, 0xf8, 0, 0, 0, 0, 0x8b, 0x80, 0x5c, 3, 0, 0, 0xff, 0xd0],
                "byte-zero store before eligibility");
            Need(Get(player, 0x35c), [0xb0, 1, 0xc3], "selected Player eligibility true return");
            Need(Get(player, 0x21c), [0x32, 0xc0, 0xc3], "separate Player alternate eligibility false return");
            Need(body + 0x192, [0x8b, 0x4f, 0x60, 0x57, 0x53, 0x8b, 1, 0xff, 0x90, 0x2c, 2, 0, 0],
                "Current six-field wrapper with actual Actor argument");
            Need(Call(body + 0x1a2), [0xc2, 4, 0], "direct Actor package callback void return");
            Need(body + 0x1a7, [0x80, 0xbf, 0xf8, 0, 0, 0, 0], "fresh post-assignment Actor byte");
            Need(body + 0x1bf, [0x8b, 1, 0xff, 0x90, 0xb4, 1, 0, 0], "direct current Common animation getter");
            Need(Get(high, 0x1b4), [0x8b, 0x81, 0xc0, 1, 0, 0, 0xc3], "Common animation field without Player view selector");
            Need(body + 0x248, [0x8b, 0x4f, 0x60, 0x6a, 0, 0x8b, 1, 0xff, 0x90, 0x2c, 1, 0, 0,
                0x8b, 0x4f, 0x60, 0x8b, 1, 0xff, 0x90, 0x40, 2, 0, 0], "top linked-null then High reset");
            Need(body + 0x260, [0x80, 0xbf, 0xf8, 0, 0, 0, 0], "fresh mover-gate assignment byte");
            Need(body + 0x269, [0x8b, 0x4f, 0x1c, 0xe8], "actual current Base before creature predicate");
            var creature = Call(body + 0x26c);
            Need(creature, [0x80, 0x79, 4, 0x2b, 0x75, 0xd, 0x8b, 0x41, 0x34, 0xc1, 0xe8, 0x17,
                0xa8, 1, 0x74, 3, 0xb0, 1, 0xc3, 0x32, 0xc0, 0xc3], "CREA bit23 predicate and non-CREA no-flags return");
            Need(body + 0x275, [0x8b, 0x8f, 0x84, 1, 0, 0, 0xe8], "same Actor-owned logical mover receiver");
            Need(body + 0x280, [0x8d, 0x4f, 0x40, 0xe8], "independent Actor Extra follower lookup");
            var follower = Call(body + 0x283);
            Need(follower, [0x6a, 0x1d, 0xe8], "actual follower Extra type29");
            Need(command + 0x13e, [0x8b, 0x46, 0x60, 0x85, 0xc0, 0x74, 0x5a, 0x83, 0x78, 0x28, 0, 0x74, 0x54],
                "fresh command-tail mutable tier DWORD");

            var linked = Get(high, 0x12c);
            Need(linked + 0xb, [0x8b, 0x5e, 0x40, 0x3b, 0xfb, 0x74, 0x19], "linked equality before cleanup/store");
            var cleanup = Call(linked + 0x16);
            Need(linked + 0x1b, [0x89, 0x7e, 0x40, 0x8b, 0xce, 0x8b, 6, 0xff, 0x90, 0x74, 2, 0, 0, 0x89, 0x78, 8],
                "changed pointer before selected package Extra");
            Need(Get(high, 0x274), [0x56, 0x8b, 0xf1, 0x8b, 6, 0xff, 0x90, 8, 2, 0, 0, 0xf7, 0xd8, 0x1b, 0xc0,
                0x83, 0xc6, 4, 0x25, 0xe0, 0, 0, 0, 3, 0xc6, 0x5e, 0xc3], "actual Primary-pointer-based state selection");
            Need(linked + 0x75, [0x8b, 6, 0x8b, 0xce, 0x6a, 0, 0xff, 0x90, 0x90, 2, 0, 0], "equal-null retained cleanup tail");
            var nullCleanup = Get(high, 0x290);
            Need(nullCleanup, [0x55, 0x8b, 0xec, 0x8b, 1, 0x6a, 1, 0xff, 0x90, 0x20, 1, 0, 0,
                0x8b, 0x4d, 8, 0x85, 0xc9, 0x74, 5], "null Actor skips only its own cleanup after Boolean child");
            Need(Get(high, 0x120), [0xc2, 4, 0], "selected High Boolean child void return");
            var reset = Get(high, 0x240); var shared = Call(reset + 3);
            Need(shared, [0x56, 0x8b, 0xf1, 0xb8, 2, 0, 0, 0, 0x6a, 0, 0x66, 0x89, 0x86, 0x2a, 2, 0, 0,
                0x8b, 6, 0xc7, 0x46, 0x34, 0, 0, 0, 0], "shared signed mode2 then same Common first scalar word zero");
            Need(Get(high, 0x4dc), [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x81, 0x6c, 3, 0, 0, 0x5d, 0xc2, 4, 0],
                "independent shared-reset byte setter");
            Need(Get(high, 0x118) + 8, [0x80, 0xca, 2, 0x24, 0xfd], "same Common mask2 Boolean setter");
            Need(Get(high, 0x4e4) + 8, [0x80, 0xca, 1, 0x24, 0xfe], "same Common mask1 Boolean setter");
            Need(shared + 0x3c, [0x80, 0x66, 0x30, 0xbf, 0x6a, 0, 0xff, 0x90, 0xa0, 5, 0, 0], "direct mask40 then actual mask20 setter");
            Need(shared + 0x48, [0x8b, 6, 0x8b, 0xce, 0x6a, 0, 0xff, 0x90, 0x2c, 1, 0, 0,
                0x8b, 6, 0x8b, 0xce, 0xff, 0x90, 0x2c, 3, 0, 0], "second linked-null occurrence before inline list drain");
            if (Jump(shared + 0x61) != cleanup)
                throw new NotSupportedException("Shared reset changed its linked-source cleanup tail.");
            Need(Get(high, 0x32c) + 0x2d, [0x8d, 0x7e, 0x5c, 0xeb, 3], "actual inline list receiver");
            Need(Get(high, 0x32c) + 0x35, [0x83, 0x7f, 4, 0, 0x75, 0xd, 0x83, 0x3f, 0, 0x0f, 0x84], "both list cells required for empty return");
            Need(cleanup, [0xa1], "actual shared DataHandler owner before cleanup");
            Need(cleanup + 8, [0x80, 0xb8, 0xa9, 5, 0, 0, 0, 0x75], "loading byte before all child-null predicates");
            Need(cleanup + 0x11, [0x8b, 0x4e, 0x40, 0x85, 0xc9, 0x74], "linked-pointer predicate");
            Need(cleanup + 0x18, [0x8b, 0x86, 0xa8, 1, 0, 0, 0x85, 0xc0], "independent signed linked-effect index");
            Need(cleanup + 0x34, [0x8b, 0x8e, 0x40, 1, 0, 0, 0x85, 0xc9, 0x74], "independent other effect Actor");
            Need(reset + 8, [0x33, 0xc0, 0xc6, 0x86, 0x6d, 3, 0, 0, 0, 0x66, 0x89, 0x86, 0xb8, 2, 0, 0,
                0x83, 0xc8, 0xff, 0x66, 0x89, 0x86, 0xba, 2, 0, 0, 0x33, 0xc0, 0x66, 0x89, 0x86, 0xbc, 2, 0, 0,
                0x89, 0x86, 0xb4, 2, 0, 0, 0x89, 0x86, 0x70, 3, 0, 0, 0x89, 0x86, 0xb0, 2, 0, 0,
                0x88, 0x86, 0x98, 3, 0, 0, 0x89, 0x86, 0xd8, 2, 0, 0, 0x88, 0x86, 0x41, 3, 0, 0,
                0x89, 0x86, 0x94, 1, 0, 0, 0x88, 0x86, 0xd8, 3, 0, 0, 0x5e, 0xc3], "twelve ordered High tail stores and widths");
            QualifyConstructors();
            QualifyGameHourBinding();
            QualifyCurrentTarget();
            _qualifiedActorPackageAssignment = source.Identity; return source;

            void QualifyConstructors()
            {
                var highStore = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == high &&
                    Match(code, at + 0x653, [0xc7, 0x46, 0x28, 0, 0, 0, 0]), 0x680);
                var middle = Call(highStore - 5); var inherited = Call(middle + 0x2d); var low = Call(inherited + 0x29);
                Need(low + 0x42, [0xc7, 0x46, 0x40, 0, 0, 0, 0], "actual inherited linked pointer-null constructor");
                Need(low + 0x50, [0xc7, 0x46, 0x5c, 0, 0, 0, 0, 0xc7, 0x46, 0x60, 0, 0, 0, 0], "two independent inline-list constructor cells");
                Need(middle + 0x1f9, [0xc7, 0x86, 0x40, 1, 0, 0, 0, 0, 0, 0], "other effect Actor-null constructor");
                Need(middle + 0x365, [0xb8, 2, 0, 0, 0, 0xc6, 0x86, 0x28, 2, 0, 0, 0, 0x66, 0x89, 0x86, 0x2a, 2, 0, 0], "actual initial reset mode2");
                Need(middle + 0x3a0, [0xc7, 0x86, 0xa8, 1, 0, 0, 0xff, 0xff, 0xff, 0xff], "linked-effect minus-one constructor");
                Need(highStore + 0x22c, [0xc7, 0x86, 0x44, 3, 0, 0, 0, 0, 0, 0], "adjustment zero constructor");
                Need(highStore + 0x225, [0xc6, 0x86, 0x6c, 3, 0, 0, 0], "independent reset byte-zero constructor");
                Need(highStore + 0x11b, [0x83, 0xcf, 0xff], "source constructor signed minus-one word producer");
                Need(highStore + 0x25e, [0x66, 0x89, 0x86, 0xb8, 2, 0, 0, 0x66, 0x89, 0xbe, 0xba, 2, 0, 0,
                    0x66, 0x89, 0x86, 0xbc, 2, 0, 0], "three independent High reset constructor words");
                Need(highStore + 0x21, [0xc7, 0x86, 0x74, 2, 0, 0, 0, 0, 0, 0], "source constructor association anchor");
                foreach (var (at, field) in new[] { (0x240, 0x2d8), (0x24a, 0x2b4), (0x254, 0x2b0), (0x39f, 0x370) })
                    Need(highStore + at, [0xc7, 0x86, (byte)field, (byte)(field >> 8), 0, 0, 0, 0, 0, 0], "independent High reset constructor pointer/clock");
                foreach (var (at, field) in new[] { (0x398, 0x36d), (0x448, 0x398), (0x203, 0x341), (0x64c, 0x3d8) })
                    Need(highStore + at, [0xc6, 0x86, (byte)field, (byte)(field >> 8), 0, 0, 0], "independent High reset constructor byte");
                Need(middle + 0x316, [0xc7, 0x86, 0x94, 1, 0, 0, 0, 0, 0, 0], "retained MiddleHigh tail pointer-null constructor");
                var copy = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0x75, 8, 0x57, 0x8b, 0xf9, 0x8b, 0x46, 0x20]) &&
                    Match(code, at + 0x3b, [0x8b, 0x46, 0x34, 0x89, 0x47, 0x34]), 0x80);
                Need(copy + 0x52, [0x8b, 0x46, 0x24, 0x8b, 0xcf, 0x89, 0x47, 0x24, 0x8b, 7, 0xff, 0x76, 0x40,
                    0xff, 0x90, 0x2c, 1, 0, 0], "separate copied scalar and linked setter association");
                var zero = Unique(at => Match(code, at, [0x89, 0x8e, 0xa4, 5, 0, 0, 0x66, 0x89, 0x8e, 0xa8, 5, 0, 0,
                    0xc6, 0x86, 0xaa, 5, 0, 0, 1]), 0x20);
                Need(zero - 0x6c, [0x33, 0xc9], "DataHandler word-zero constructor producer independently of loading writers");
            }
            void QualifyGameHourBinding()
            {
                var current = Get(high, 0x22c); var setter = Call(current + 0x18);
                Need(setter + 0x1b, [0xb9], "actual shared calendar owner passed to GameHour");
                var owner = Word(code, setter + 0x1c); var hour = Call(setter + 0x20);
                Need(hour + 4, [0x8b, 0x41, 0xc], "calendar retained GameHour pointer slot");
                var binding = Unique(at => Match(code, at, [0x56, 0x6a, 0x35, 0x8b, 0xf1, 0xe8]) &&
                    Match(code, at + 0x1d, [0x6a, 0x38, 0x89, 0x46, 8, 0xe8]), 0x67);
                var lookup = Call(binding + 5);
                var pushes = new[] { 1, 0xa, 0x13, 0x1d, 0x27, 0x31 };
                var calls = new[] { 5, 0xe, 0x18, 0x22, 0x2c, 0x36 };
                for (var index = 0; index < 6; ++index)
                {
                    Need(binding + pushes[index], [0x6a, (byte)(0x35 + index)], "ordered reserved calendar GLOB identity");
                    if (Call(binding + calls[index]) != lookup)
                        throw new NotSupportedException("Calendar binding changed its shared actual form lookup child.");
                }
                Need(binding + 0x29, [0x89, 0x46, 0xc], "reserved GameHour38 lookup return bound to same calendar slot");
                _ = Unique(at => code[at] == 0xb9 && Word(code, at + 1) == owner && code[at + 5] == 0xe8 && Call(at + 5) == binding, 0x10);
                Need(lookup + 0x14, [0x8b, 0x5d, 8, 0x53], "actual requested ID into shared winning form lookup");
                Need(lookup + 0x43, [0x8b, 0x46, 8], "selected real form payload return");
            }
            void QualifyCurrentTarget()
            {
                var current = Get(high, 0x22c); var extra = Call(current + 0xc);
                Need(extra, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x8b, 0x4e, 0x30, 0x85, 0xc9, 0x74, 0x28, 0xe8],
                    "same winning mutable target pointer before wrapper equality");
                var kind = Call(extra + 0xd);
                Need(kind, [0x0f, 0xb6, 1, 0xc3], "actual target type byte getter");
                Need(extra + 0x12, [0x85, 0xc0, 0x74, 0x12, 0x83, 0xf8, 3, 0x75, 0x1a], "kind0 or3 versus null-Extra return");
                Need(Call(extra + 0x2b), [0x80, 0x39, 0, 0x75, 4, 0x8b, 0x41, 4, 0xc3, 0x33, 0xc0, 0xc3], "actual typed kind0 reference payload getter");
                var actorExtra = Call(extra + 0x1e);
                Need(actorExtra, [0x83, 0xc1, 0x40, 0xe9], "kind3 uses actual Actor Extra list");
                var lookup = Jump(actorExtra + 3);
                Need(lookup, [0x6a, 0x51, 0xe8], "actual independent Extra81 membership");
                Need(lookup + 7, [0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 0xc, 0xc3, 0x33, 0xc0, 0xc3], "absent Extra81 or its genuine nullable payload");
                var package = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68]) &&
                    Match(code, at + 0xc0, [0xc7, 0x47, 0x18, 0xff, 0xff, 0xff, 0xff, 0xc6, 0x47, 0x20, 0xff]) &&
                    Match(code, at + 0xd8, [0xc7, 0x47, 0x7c, 0, 0, 0, 0]), 0xfb);
                Need(package + 0x76, [0x33, 0xc0], "same target-pointer constructor zero producer");
                Need(package + 0x90, [0x89, 0x47, 0x30], "actual current target-pointer null constructor");
                var loader = Get(Word(code, package + 0x3d), 0x20);
                Need(loader + 0x739, [0x3d, 0x50, 0x54, 0x44, 0x54], "actual PTDT selected load arm");
                var target = loader + 0x867;
                Need(target, [0x6a, 0x10, 0x8d, 0x45, 0xc8, 0x0f, 0x57, 0xc0, 0x50, 0x8b, 0xce, 0x0f, 0x11, 0x45, 0xc8, 0xe8],
                    "sixteen-byte zero buffer before real target input read");
                var inputRead = Call(target + 0xf);
                Need(inputRead + 0x11, [0x8b, 0x5d, 0xc, 0x56, 0x57, 0x8b, 0xf9, 0x8b, 0x4d, 8],
                    "actual target buffer capacity and destination arguments");
                Need(inputRead + 0x8b, [0x85, 0xdb, 0x0f, 0x84, 0xe6, 0, 0, 0, 0x39, 0x9f, 0x5c, 2, 0, 0,
                    0x0f, 0x86, 0xda, 0, 0, 0], "source extent within capacity selects complete actual field read");
                Need(inputRead + 0x17d, [0xff, 0xb7, 0x5c, 2, 0, 0, 0x51, 0x8b, 0x4f, 0x10, 0xe8],
                    "complete actual short source extent copied into zero buffer");
                Need(inputRead + 0x9f, [0xc6, 0x44, 0x0b, 0xff, 0, 0x84, 0xc0, 0x75, 0x6a, 0x8d, 0x73, 0xff],
                    "distinct oversized source read truncation and diagnostic branch");
                Need(target + 0x34, [0x8b, 0x47, 0x30], "actual existing target allocation predicate");
                Need(target + 0x42, [0x6a, 0x10], "real sixteen-byte target object allocation extent");
                var constructed = Call(target + 0x5e);
                Need(constructed, [0xc6, 1, 2, 0x8b, 0xc1, 0xc7, 0x41, 4, 0, 0, 0, 0, 0xc7, 0x41, 8, 0, 0, 0, 0,
                    0xc7, 0x41, 0xc, 0, 0, 0, 0, 0xc3], "actual target kind2/payload/count/distance constructor");
                Need(target + 0x6b, [0x89, 0x47, 0x30], "actual target object publication into same PACK");
                var copy = Call(target + 0x77);
                Need(copy + 0x1a, [0x88, 0x39, 0x8a, 0xdf, 0x83, 0xee, 0, 0x74, 0xa, 0x83, 0xee, 1, 0x74, 5,
                    0x83, 0xee, 1, 0x75, 9, 0xc7, 0x41, 4, 0, 0, 0, 0], "kind-change clear only for payload kinds012");
                Need(copy + 0x76, [0x8b, 0x42, 8, 0x89, 0x41, 8, 0x8b, 0x42, 0xc, 0x89, 0x41, 0xc], "independent copied count/distance words");
            }
            uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
                throw new NotSupportedException("Actor assignment lacks its exact selected source class: " + name);
            int Get(uint table, uint slot) => Offset(Word(image.Read(table + slot, 4), 0));
            int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Actor assignment lost an exact original direct child.");
            int Jump(int at) => code[at] == 0xe9 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Actor reset lost its exact original tail child.");
            int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Actor assignment source association is absent or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Actor assignment changed its original " + role + "."); }
        }
    }
}
