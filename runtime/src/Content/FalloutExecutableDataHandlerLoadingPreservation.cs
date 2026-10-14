using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock DataHandlerPreservationQualificationLock = new();
    private static string? _qualifiedDataHandlerPreservation;

    internal static FalloutDataHandlerLoadingPreservationSource ReadDataHandlerLoadingPreservation(string path,
        FalloutActorPackageAssignmentSource assignment)
    {
        var source = FalloutDataHandlerLoadingPreservationSource.Reviewed(assignment); source.Validate(assignment);
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != assignment.Command.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("DataHandler preservation changed its selected original source image.");
        lock (DataHandlerPreservationQualificationLock)
        {
            if (_qualifiedDataHandlerPreservation == source.Identity) return source;
            var (code, image) = Load(bytes);
            var zero = Unique(at => Match(code, at, [0x89, 0x8e, 0xa4, 5, 0, 0, 0x66, 0x89, 0x8e, 0xa8, 5, 0, 0,
                0xc6, 0x86, 0xaa, 5, 0, 0, 1]), 0x20);
            Need(zero - 0x6c, [0x33, 0xc9], "genuine shared loading constructor zero producer");
            var tes = image.SourceClassTables(".?AVTES@@", 1).ToArray() is [var table] ? table :
                throw new NotSupportedException("Loading preservation lost its source TES class.");
            var teardown = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == tes &&
                Match(code, at + 0x1a, [0xc6, 0x80, 0xa9, 5, 0, 0, 1]), 0x253);
            var handler = Word(code, teardown + 0xc);
            Need(teardown + 0xb, [0xa1], "actual shared DataHandler before teardown loading writer");
            Need(teardown + 0x246, [0xc6, 0x80, 0xa9, 5, 0, 0, 1], "separate post-clear teardown one store");
            var clear = Call(teardown + 0x20e);
            Need(clear + 0x31, [0xc6, 0x86, 0xa9, 5, 0, 0, 1], "clear entered loading one before resource children");
            Need(clear + 0x78, [0xc6, 0x86, 0xa9, 5, 0, 0, 1], "clear selected list arm loading one");
            Need(clear + 0x187a, [0xc6, 0x86, 0xa9, 5, 0, 0, 0], "clear returned loading zero independently of records");
            RequireOnlyDirectCaller(clear, teardown + 0x20e);
            var constructed = Unique(at => Match(code, at, [0xc7, 3]) && Word(code, at + 2) == tes &&
                Match(code, at + 0xc2, [0x68, 0xc4, 0, 0, 0, 0x6a, 0, 0x53]), 0x3be);
            if (Call(constructed + 0x3a0) != zero - 0x3b7)
                throw new NotSupportedException("Current TES changed its actual DataHandler constructor.");
            Need(constructed + 0x3b2, [0xa3], "actual constructed DataHandler global publication");
            if (Word(code, constructed + 0x3b3) != handler)
                throw new NotSupportedException("New DataHandler construction and loading cleanup selected different shared fields.");

            var close = image.DataHandlerConsoleCommandStart("CloseFile", code);
            Need(close + 0x81, [0x8b, 0x0d], "CloseFile actual shared owner");
            if (Word(code, close + 0x83) != handler || Word(code, close + 0x99) != handler || Word(code, close + 0xac) != handler)
                throw new NotSupportedException("CloseFile changed its shared DataHandler field association.");
            Need(close + 0x9d, [0xc6, 0x81, 0xa9, 5, 0, 0, 1], "CloseFile entered one before selected file close");
            Need(close + 0xb6, [0xc6, 0x80, 0xa9, 5, 0, 0, 0], "CloseFile zero only after its close child returns");

            // The complete reviewed shared-byte immediate writers belong to
            // these construction/retirement/console paths. Record callbacks
            // receive actual form objects; similarly indexed Player fields
            // are another receiver, independently of their rendering effects.
            var expected = new HashSet<int> { teardown + 0x1a, teardown + 0x246, clear + 0x31, clear + 0x78,
                clear + 0x187a, close + 0x9d, close + 0xb6 };
            var found = new HashSet<int>();
            for (var at = 0; at <= code.Length - 7; ++at)
                if (code[at] == 0xc6 && (code[at + 1] & 0xf8) == 0x80 && Word(code, at + 2) == 0x5a9)
                    found.Add(at);
            if (!found.SetEquals(expected))
                throw new NotSupportedException("DataHandler cleanup loading acquired an unreviewed shared-byte writer.");

            var busy = Unique(at => Match(code, at, [0xc6, 0x86, 0xa6, 5, 0, 0, 1, 0x6a, 0]), 0xcf);
            Need(busy - 0xd, [0x80, 0x7d, 8, 0, 0x6a, 0], "bulk records mode operand before independent busy field");
            var bulk = busy - 0x98;
            Need(bulk, [0x55, 0x8b, 0xec, 0x6a, 0xff], "actual bulk record loading frame");
            Need(bulk + 0x667, [0x80, 0x7d, 8, 0, 0xc6, 0x83, 0xac, 5, 0, 0, 1], "independent materialization phase");
            Need(bulk + 0x6ef, [0x8b, 0xcb, 0xe8], "current DataHandler receiver for selected file record loop");
            Need(bulk + 0x6f6, [0x84, 0xc0], "actual selected file result before subsequent phase");
            Need(bulk + 0x75e, [0xc6, 0x83, 0xac, 5, 0, 0, 0], "materialization phase ends independently of cleanup loading");
            Need(bulk + 0xcd4, [0xc6, 0x83, 0xa6, 5, 0, 0, 0], "successful bulk return clears busy rather than cleanup loading");
            Need(bulk + 0x770, [0x8b, 0x0e, 0x85, 0xc9, 0x74, 0xf, 0x8b, 1, 0xff, 0x90, 0x88, 0, 0, 0],
                "selected form object finalizers use actual record receivers");
            var file = Call(bulk + 0x6f1);
            Need(file + 0x171, [0x6a, 0, 0x6a, 0, 0xff, 0x75, 0xc, 0x8b, 0xcf, 0x56, 0xe8],
                "selected ordinary file record dispatch arguments");
            var record = Call(file + 0x17b);
            Need(record + 0x233, [0x8b, 7, 0x8b, 0xcf, 0x53, 0xff, 0x50, 0x24],
                "existing source form copy/load dispatch uses a record receiver");
            _qualifiedDataHandlerPreservation = source.Identity; return source;

            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Loading preservation lost an actual source child.");
            int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Loading preservation source association is absent or ambiguous.");
            void Need(int at, byte[] expectedBytes, string role)
            { if (!Match(code, at, expectedBytes)) throw new NotSupportedException("DataHandler loading changed its " + role + "."); }
            void RequireOnlyDirectCaller(int target, int actual)
            {
                var callers = new List<int>();
                for (var at = 0; at <= code.Length - 5; ++at)
                    if (code[at] is 0xe8 or 0xe9 && at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)) == target)
                        callers.Add(at);
                if (callers is not [var caller] || caller != actual)
                    throw new NotSupportedException("Selected record callbacks acquired an unreviewed DataHandler clear entry.");
            }
        }
    }

    private sealed partial class Image
    {
        internal int DataHandlerConsoleCommandStart(string name, byte[] code)
        {
            uint? execute = null;
            foreach (var section in headers.SectionHeaders.Where(section => (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 40; at += sizeof(uint))
                {
                    var row = data[at..]; if (Literal(U32(row, 0)) != name) continue;
                    if (U32(row, 8) is 0 or >= 0x1000 || U32(row, 32) != 0 ||
                        Literal(U32(row, 4)) is null || Literal(U32(row, 12)) is null || execute is not null)
                        throw new NotSupportedException("Shared loading writer lost its distinct console command declaration.");
                    var candidate = U32(row, 24);
                    if (candidate < CodeBase || candidate - CodeBase >= code.Length)
                        throw new NotSupportedException("Shared loading console writer has an unowned source entry.");
                    execute = candidate;
                }
            }
            return execute is { } selected ? checked((int)(selected - CodeBase)) :
                throw new NotSupportedException("Shared loading console writer declaration is absent.");
        }
    }
}
