using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock ReferenceFollowerQualificationLock = new();
    private static string? _qualifiedReferenceFollower;

    internal static FalloutReferenceFollowerSource ReadReferenceFollowerExtra(string path, FalloutReferencePositionSource reference)
    {
        var source = FalloutReferenceFollowerSource.Reviewed(reference); source.Validate(reference);
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != reference.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Follower membership changed its selected original source image.");
        lock (ReferenceFollowerQualificationLock)
        {
            if (_qualifiedReferenceFollower == source.Identity) return source;
            _ = ReadReferencePosition(path, reference.Fields);
            var (code, image) = Load(bytes);
            var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
                throw new NotSupportedException("Follower lookup lost its actual selected Actor class.");
            var body = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x2f4, 4), 0));
            Need(body + 0x280, [0x8d, 0x4f, 0x40, 0xe8], "same actual Actor reference Extra-list receiver");
            var lookup = Call(body + 0x283);
            Need(lookup, [0x6a, FalloutReferenceFollowerSource.ExtraKind, 0xe8], "selected type29 lookup operand");
            Need(lookup + 7, [0xc3], "typed lookup returns the actual Extra object");
            var core = Call(lookup + 2);
            Need(core + 0xf, [0xc1, 0xea, 3], "presence byte selected by original kind shift");
            Need(core + 0x17, [0x83, 0xfa, 0x12, 0x0f, 0x83], "source presence table extent");
            Need(core + 0x20, [0x8b, 0xce, 0xb8, 1, 0, 0, 0, 0x83, 0xe1, 7, 0xd3, 0xe0], "actual membership bit mask");
            Need(core + 0x2c, [0x84, 0x44, 0x3a, 8, 0x0f, 0x84], "absent membership returns before TLS and list lookup");
            var absent = checked(core + 0x36 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(core + 0x32)));
            Need(absent, [0x5f, 0x5e, 0x33, 0xc0, 0x5b, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0], "genuine absent Extra null result");
            Need(body + 0x288, [0x85, 0xc0, 0x74, 0x3a, 0x8b, 0x70, 0xc, 0x85, 0xf6, 0x74, 0x33],
                "distinct absent Extra and present-null payload returns");
            Need(body + 0x293, [0x8b, 0x3e, 0x85, 0xff, 0x74, 0x2d], "null first Actor terminates the ordered tail");
            Need(body + 0x2bf, [0x8b, 0x76, 4, 0x85, 0xf6, 0x75, 0xcd], "actual linked next pointer after each Actor");
            _qualifiedReferenceFollower = source.Identity; return source;

            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Follower lookup changed an actual source child.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Follower membership changed its " + role + "."); }
        }
    }
}
