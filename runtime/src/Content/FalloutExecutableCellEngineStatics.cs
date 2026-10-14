using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireCellEngineStaticBases(byte[] code, Image image)
    {
        var tables = image.SourceClassTables(".?AVTESObjectSTAT@@", 1).ToArray();
        if (tables is not [var stat]) throw new NotSupportedException("CELL engine marker bases lost their actual STAT source class.");
        int? constructor = null;
        foreach (var (identity, name) in new (uint Identity, string Name)[]
        {
            (FalloutDynamicPrimitiveSource.PlaneMarkerRuntimeId, "PlaneMarker"),
            (FalloutDynamicPrimitiveSource.RoomMarkerRuntimeId, "RoomMarker"),
            (FalloutDynamicPrimitiveSource.PortalMarkerRuntimeId, "PortalMarker"),
            (FalloutDynamicPrimitiveSource.CollisionMarkerRuntimeId, "CollisionMarker"),
        })
        {
            var candidates = new List<int>();
            for (var at = 0x4d; at <= code.Length - 0x53; at++)
                if (Match(code, at, [0x8b, 1, 0x6a, 1, 0x6a, checked((byte)identity), 0xc6, 0x45, 0xfc, 0,
                    0xff, 0x90, 0x28, 1, 0, 0]) && image.Literal(Word(code, at + 0x17)) == name)
                    candidates.Add(at);
            if (candidates is not [var factory]) throw new NotSupportedException("CELL engine marker source factory is absent or ambiguous: " + name + ".");
            Need(factory - 0x4d, [0x6a, checked((byte)identity), 0xe8], "existing engine marker lookup before constructor");
            Need(factory - 0x2d, [0x6a, 0x50, 0xb9], "genuine engine STAT allocation before the selected constructor");
            Need(factory - 0x13, [0x8b, 0xc8, 0xe8], "actual allocated object supplied to the same STAT constructor");
            var child = Call(factory - 0x11);
            if (constructor is { } selected && selected != child)
                throw new NotSupportedException("CELL engine marker factories changed their shared actual STAT constructor.");
            constructor = child;
            if (!Match(code, child + 0x3e, [0xc7, 7]) || Word(code, child + 0x40) != stat)
                throw new NotSupportedException("CELL marker base is not the actual selected STAT constructor.");
            Need(child + 0x4d, [0xc6, 0x47, 4, 0x20], "actual constructed STAT kind before reference base lookup");
            Need(factory + 0x29, [0x83, 0xc1, 0x30, 0x68], "same constructed STAT model field receiver");
            if (image.Literal(Word(code, factory + 0x2d)) != string.Empty)
                throw new NotSupportedException("Source marker model initialization is not its actual empty declaration.");
            Need(factory + 0x33, [0xff, 0x50, 0x18, 0xff, 0x35], "model initialization precedes actual STAT registration");
            Need(factory + 0x49, [0x6a, 0, 0x8b, 1, 0xff, 0x90, 0xc8, 0, 0, 0], "actual marker construction flag setter follows registration");
        }
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("CELL engine marker lost its actual constructor child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("CELL marker changed its " + role + "."); }
    }
}
